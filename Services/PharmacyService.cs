using System.Data;
using Microsoft.Data.SqlClient;
using PharmaLinkApp.Database;
using PharmaLinkApp.Models;

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// The Super Admin's control over shops (approve, suspend, commission rate)
    /// and the pharmacy owner's control over his own shop profile.
    /// </summary>
    public class PharmacyService
    {
        private readonly DbHelper _db = new DbHelper();

        // ---------------------------------------------------------------------
        //  SUPER ADMIN
        // ---------------------------------------------------------------------

        /// <summary>Requirement 2 and 3: every pharmacy with its owner, licence, area and status.</summary>
        public DataTable Search(string keyword, string status, string area)
        {
            const string sql = @"
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, u.Email AS OwnerEmail,
        p.LicenseNo, p.Area, p.ContactPhone, p.CommissionRate, p.Status, p.RegisteredAt,
        (SELECT COUNT(*) FROM Medicines m WHERE m.PharmacyId = p.PharmacyId) AS Medicines,
        ISNULL((SELECT CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2))
                FROM   Reviews r
                       INNER JOIN Medicines m2 ON m2.MedicineId = r.MedicineId
                WHERE  m2.PharmacyId = p.PharmacyId AND r.IsHidden = 0), 0) AS AverageRating
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
WHERE   (@Keyword = '' OR p.PharmacyName LIKE '%' + @Keyword + '%'
                       OR p.LicenseNo    LIKE '%' + @Keyword + '%'
                       OR u.FullName     LIKE '%' + @Keyword + '%')
  AND   (@Status  = '' OR p.Status = @Status)
  AND   (@Area    = '' OR p.Area   = @Area)
ORDER BY CASE p.Status WHEN 'Pending' THEN 0 WHEN 'Approved' THEN 1 ELSE 2 END, p.PharmacyName;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@Status", status ?? ""),
                DbHelper.P("@Area", area ?? ""));
        }

        public DataTable GetPending()
        {
            const string sql = @"
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, p.LicenseNo,
        p.Area, p.ContactPhone, p.RegisteredAt
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
WHERE   p.Status = 'Pending'
ORDER BY p.RegisteredAt;";

            return _db.ExecuteTable(sql);
        }

        /// <summary>
        /// Approval flips two rows, because the pharmacy record and the login
        /// account are separate concerns: the shop becomes Approved and the
        /// owner's account becomes Active so he can finally log in.
        /// </summary>
        public bool Approve(int pharmacyId)
        {
            const string sql = @"
BEGIN TRANSACTION;
    UPDATE Pharmacies SET Status = 'Approved' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status = 'Active'
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);
COMMIT TRANSACTION;";

            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>
        /// Suspension flips three things inside one transaction: the pharmacy,
        /// the login account and every medicine that pharmacy lists. Nothing is
        /// deleted anywhere, so past orders and the invoices customers already
        /// hold stay exactly as they were.
        /// </summary>
        public bool Suspend(int pharmacyId)
        {
            const string sql = @"
BEGIN TRANSACTION;
    UPDATE Pharmacies SET Status   = 'Suspended' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status   = 'Suspended'
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);
    UPDATE Medicines  SET IsActive = 0           WHERE PharmacyId = @PharmacyId;
COMMIT TRANSACTION;";

            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>Puts a suspended shop back on the platform.</summary>
        public bool Reinstate(int pharmacyId)
        {
            const string sql = @"
BEGIN TRANSACTION;
    UPDATE Pharmacies SET Status   = 'Approved' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status   = 'Active'
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);
    UPDATE Medicines  SET IsActive = 1          WHERE PharmacyId = @PharmacyId;
COMMIT TRANSACTION;";

            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>A rejected registration is suspended rather than erased, so the licence number stays taken.</summary>
        public bool Reject(int pharmacyId)
        {
            return Suspend(pharmacyId);
        }

        /// <summary>
        /// Requirement 3: delete a shop entirely. Only possible while it has no
        /// order history; a shop that has traded is suspended instead, because
        /// deleting it would destroy invoices customers already hold.
        /// </summary>
        public bool Delete(int pharmacyId, out string message)
        {
            int orders = _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id;",
                DbHelper.P("@Id", pharmacyId));

            if (orders > 0)
            {
                message = "This pharmacy has " + orders + " order(s) in its history, so it cannot be deleted. " +
                          "Suspend it instead - suspension hides it from customers without destroying past invoices.";
                return false;
            }

            using (SqlConnection conn = _db.GetConnection())
            {
                conn.Open();
                using (SqlTransaction tx = conn.BeginTransaction())
                {
                    try
                    {
                        // The owner's UserId is read first, because the Pharmacies row
                        // is the only thing that points at it and that row is about to go.
                        int ownerId;
                        using (SqlCommand read = new SqlCommand(
                            "SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @Id;", conn, tx))
                        {
                            read.Parameters.AddWithValue("@Id", pharmacyId);
                            object value = read.ExecuteScalar();
                            ownerId = value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
                        }

                        ExecuteInTx(conn, tx,
                            "DELETE FROM Offers WHERE MedicineId IN (SELECT MedicineId FROM Medicines WHERE PharmacyId = @Id);", pharmacyId);
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Cart WHERE MedicineId IN (SELECT MedicineId FROM Medicines WHERE PharmacyId = @Id);", pharmacyId);
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Medicines WHERE PharmacyId = @Id;", pharmacyId);

                        // Pharmacies before Users. FK_Pharmacies_Owner points from
                        // Pharmacies to Users and has no ON DELETE CASCADE, so removing
                        // the owner while the shop row still references it is a foreign
                        // key violation and the whole transaction rolls back.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Pharmacies WHERE PharmacyId = @Id;", pharmacyId);

                        if (ownerId != 0)
                        {
                            using (SqlCommand removeOwner = new SqlCommand(
                                "DELETE FROM Users WHERE UserId = @OwnerId;", conn, tx))
                            {
                                removeOwner.Parameters.AddWithValue("@OwnerId", ownerId);
                                removeOwner.ExecuteNonQuery();
                            }
                        }

                        tx.Commit();
                        message = "Pharmacy deleted.";
                        return true;
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        message = ex.Message;
                        return false;
                    }
                }
            }
        }

        private static void ExecuteInTx(SqlConnection conn, SqlTransaction tx, string sql, int pharmacyId)
        {
            using (SqlCommand cmd = new SqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@Id", pharmacyId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Requirement 9. The rate is a column on Pharmacies rather than a
        /// constant in the code, and the value is checked here and again by
        /// CK_Pharmacies_Comm. Changing it affects only orders placed from this
        /// moment on, because every order froze its own commission at checkout.
        /// </summary>
        public bool SetCommissionRate(int pharmacyId, decimal rate)
        {
            if (rate < 0m || rate > 30m) return false;
            return _db.ExecuteNonQuery(
                "UPDATE Pharmacies SET CommissionRate = @Rate WHERE PharmacyId = @Id;",
                DbHelper.P("@Rate", rate),
                DbHelper.P("@Id", pharmacyId)) == 1;
        }

        // ---------------------------------------------------------------------
        //  PHARMACY OWNER
        // ---------------------------------------------------------------------

        public Pharmacy GetById(int pharmacyId)
        {
            const string sql = @"
SELECT  p.PharmacyId, p.OwnerId, p.PharmacyName, p.LicenseNo, p.Area, p.Address,
        p.ContactPhone, p.LogoPath, p.CommissionRate, p.Status, p.RegisteredAt,
        u.FullName AS OwnerName
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
WHERE   p.PharmacyId = @Id;";

            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", pharmacyId));
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];
            return new Pharmacy
            {
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                OwnerId = DbHelper.GetInt(row, "OwnerId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                LicenseNo = DbHelper.GetString(row, "LicenseNo"),
                Area = DbHelper.GetString(row, "Area"),
                Address = DbHelper.GetString(row, "Address"),
                ContactPhone = DbHelper.GetString(row, "ContactPhone"),
                LogoPath = DbHelper.GetString(row, "LogoPath"),
                CommissionRate = DbHelper.GetDecimal(row, "CommissionRate"),
                Status = DbHelper.GetString(row, "Status"),
                RegisteredAt = DbHelper.GetDate(row, "RegisteredAt"),
                OwnerName = DbHelper.GetString(row, "OwnerName")
            };
        }

        /// <summary>
        /// Requirement 11. The WHERE clause carries PharmacyId so an owner can
        /// never edit another shop. LicenseNo is deliberately not updatable:
        /// changing it would mean a new licence and a fresh approval.
        /// </summary>
        public bool UpdateProfile(int pharmacyId, string name, string area, string address, string contactPhone, string logoPath)
        {
            const string sql = @"
UPDATE  Pharmacies
SET     PharmacyName = @Name, Area = @Area, Address = @Address,
        ContactPhone = @Phone, LogoPath = @Logo
WHERE   PharmacyId = @Id;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@Name", name.Trim()),
                DbHelper.P("@Area", area.Trim()),
                DbHelper.P("@Address", address.Trim()),
                DbHelper.P("@Phone", contactPhone.Trim()),
                DbHelper.P("@Logo", string.IsNullOrWhiteSpace(logoPath) ? null : logoPath),
                DbHelper.P("@Id", pharmacyId)) == 1;
        }

        // ---------------------------------------------------------------------
        //  SHARED LOOKUPS
        // ---------------------------------------------------------------------

        /// <summary>The distinct areas, used by the customer's Area filter.</summary>
        public List<string> GetAreas(bool approvedOnly)
        {
            List<string> areas = new List<string>();
            DataTable table = _db.ExecuteTable(
                "SELECT DISTINCT Area FROM Pharmacies WHERE (@ApprovedOnly = 0 OR Status = 'Approved') ORDER BY Area;",
                DbHelper.P("@ApprovedOnly", approvedOnly ? 1 : 0));

            foreach (DataRow row in table.Rows)
                areas.Add(DbHelper.GetString(row, "Area"));

            return areas;
        }

        /// <summary>Approved pharmacies only, for the customer's Pharmacy filter.</summary>
        public List<Pharmacy> GetApprovedList()
        {
            List<Pharmacy> list = new List<Pharmacy>();
            DataTable table = _db.ExecuteTable(
                "SELECT PharmacyId, PharmacyName, Area FROM Pharmacies WHERE Status = 'Approved' ORDER BY PharmacyName;");

            foreach (DataRow row in table.Rows)
            {
                list.Add(new Pharmacy
                {
                    PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                    PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                    Area = DbHelper.GetString(row, "Area")
                });
            }
            return list;
        }

        public int CountByStatus(string status)
        {
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Pharmacies WHERE Status = @Status;",
                DbHelper.P("@Status", status));
        }
    }
}
