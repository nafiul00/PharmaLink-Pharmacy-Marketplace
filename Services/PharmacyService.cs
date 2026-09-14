using System.Data;                      // DataTable and DataRow, what every read here returns
using Microsoft.Data.SqlClient;         // SqlConnection and SqlTransaction, needed by Delete
using PharmaLinkApp.Database;           // DbHelper, which every other method here goes through
using PharmaLinkApp.Models;             // Pharmacy, the typed object a shop row becomes

// One namespace for every service, so a form needs a single using directive.
namespace PharmaLinkApp.Services
{
    /// <summary>Super Admin control over shops, and the owner's own profile.</summary>
    public class PharmacyService
    {
        // The shared helper every service uses; only Delete steps around it.
        private readonly DbHelper _db = new DbHelper();

        // --- Super Admin: approve, suspend, reinstate, reject, delete, commission ---

        /// <summary>Requirement 2 and 3: every pharmacy, its owner and status.</summary>
        public DataTable Search(string keyword, string status, string area)
        {
            // const, so nothing is ever concatenated in; verbatim keeps -- comments.
            const string sql = @"
-- The grid's full column list, so judging a shop needs no second screen.
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, u.Email AS OwnerEmail,
        p.LicenseNo, p.Area, p.ContactPhone, p.CommissionRate, p.Status, p.RegisteredAt,   -- approving means checking a licence
        -- A correlated subquery, not a JOIN: a join would repeat the pharmacy row.
        (SELECT COUNT(*) FROM Medicines m WHERE m.PharmacyId = p.PharmacyId) AS Medicines,
        -- ISNULL turns 'no reviews yet' into 0, so the column stays numeric and sortable.
        ISNULL((SELECT CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2))
                -- The inner CAST widens Rating first, or 4 and 5 would average to 4.
                FROM   Reviews r
                       INNER JOIN Medicines m2 ON m2.MedicineId = r.MedicineId   -- reviews hang off a medicine
                WHERE  m2.PharmacyId = p.PharmacyId AND r.IsHidden = 0), 0) AS AverageRating   -- m2, since m is taken
FROM    Pharmacies p   -- the driving table, so one row out per shop
        INNER JOIN Users u ON u.UserId = p.OwnerId   -- safe: OwnerId is a NOT NULL key
-- The optional-filter pattern: an empty string means do not filter on this one.
WHERE   (@Keyword = '' OR p.PharmacyName LIKE '%' + @Keyword + '%'
                       OR p.LicenseNo    LIKE '%' + @Keyword + '%'    -- chasing a licence number
                       OR u.FullName     LIKE '%' + @Keyword + '%')   -- or the person behind it
  AND   (@Status  = '' OR p.Status = @Status)   -- = not LIKE: Status is picked from a list
  AND   (@Area    = '' OR p.Area   = @Area)     -- the third filter, built the same way
-- The CASE puts Pending at the top, so waiting registrations get seen first.
ORDER BY CASE p.Status WHEN 'Pending' THEN 0 WHEN 'Approved' THEN 1 ELSE 2 END, p.PharmacyName;";

            // ExecuteTable opens, fills a DataTable and closes, so nothing is left open.
            return _db.ExecuteTable(sql,
                // ?? "" because DBNull = '' is UNKNOWN in SQL, which would break the filter.
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@Status", status ?? ""),     // the ComboBox 'All' entry is an empty string
                DbHelper.P("@Area", area ?? ""));        // one blank control means one relaxed filter
        }

        // The approval queue: a different question and column list from Search.
        public DataTable GetPending()
        {
            // const and verbatim for the same reasons as Search: fixed text, readable layout.
            const string sql = @"
-- A narrower list than Search: only what is needed to judge a registration.
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, p.LicenseNo,
        p.Area, p.ContactPhone, p.RegisteredAt   -- contact details, plus what ORDER BY uses
-- No subqueries: a count or a rating means nothing for a shop that never traded.
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId   -- Pharmacies stores only the key
-- Status is fixed here rather than passed in; Search covers the variable case.
WHERE   p.Status = 'Pending'
-- Oldest first, so applications are dealt with in the order they arrived.
ORDER BY p.RegisteredAt;";

            // No parameters at all, because nothing in this query varies.
            return _db.ExecuteTable(sql);
        }

        /// <summary>Approval flips the shop to Approved and its owner to Active.</summary>
        public bool Approve(int pharmacyId)
        {
            // One string holding a whole transaction, so this is a batch, not one statement.
            const string sql = @"
-- TWO rows: Pharmacies says the shop may trade, Users says the owner may sign in.
BEGIN TRANSACTION;
    -- Statement one: the shop. Every catalogue query tests Status = 'Approved'.
    UPDATE Pharmacies SET Status = 'Approved' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status = 'Active'   -- so the owner can finally log in
    -- Safe here: an UPDATE leaves the Pharmacies row this subquery reads intact.
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);
-- Both flip or neither does, so no approved shop is left with a locked out owner.
COMMIT TRANSACTION;";

            // > 0 not == 2: the count sums two statements, and a bad id changes nothing.
            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>Suspension flips the shop, the login and its medicines.</summary>
        public bool Suspend(int pharmacyId)
        {
            // The same batch-in-one-string shape as Approve, one statement longer.
            const string sql = @"
-- THREE updates, one transaction, and no DELETE: past invoices stay put.
BEGIN TRANSACTION;
    -- Statement one: the shop stops trading; catalogue queries filter on this.
    UPDATE Pharmacies SET Status   = 'Suspended' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status   = 'Suspended'   -- the owner can no longer sign in
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);   -- found through the shop row
    -- The same soft delete the owner's own Delist uses, so Reinstate can mirror it.
    UPDATE Medicines  SET IsActive = 0           WHERE PharmacyId = @PharmacyId;
-- All three land together, so stock is never left on sale after suspension.
COMMIT TRANSACTION;";

            // > 0 again: this totals three statements and the medicine count varies by shop.
            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>Puts a suspended shop back on the platform.</summary>
        public bool Reinstate(int pharmacyId)
        {
            // Three statements again, in the same order, with the values inverted.
            const string sql = @"
-- The exact mirror of Suspend, possible only because suspension destroyed nothing.
BEGIN TRANSACTION;
    -- 'Approved', not 'Pending': a suspension is not a rejection.
    UPDATE Pharmacies SET Status   = 'Approved' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status   = 'Active'   -- or he owns a shop he cannot administer
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);   -- safe: nothing here deletes it
    -- This does relist medicines the owner delisted himself; he can delist them again.
    UPDATE Medicines  SET IsActive = 1          WHERE PharmacyId = @PharmacyId;
-- One commit for all three, so a shop can never come back half reinstated.
COMMIT TRANSACTION;";

            // The same > 0 test, because this is a three statement total as well.
            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>A rejected registration is suspended, not erased.</summary>
        public bool Reject(int pharmacyId)
        {
            // Delegates rather than repeating the SQL, so the two can never drift apart.
            return Suspend(pharmacyId);
        }

        /// <summary>Requirement 3. Delete a shop that has never traded.</summary>
        public bool Delete(int pharmacyId, out string message)
        {
            // The guard comes FIRST: an invoice a customer holds has to keep resolving.
            int orders = _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id;",   // only the number crosses the wire
                DbHelper.P("@Id", pharmacyId));                          // a parameter, never concatenation

            // Any history at all refuses: one order is one invoice that must keep working.
            if (orders > 0)
            {
                // The refusal names the count and then names the alternative.
                message = "This pharmacy has " + orders + " order(s) in its history, so it cannot be deleted. " +
                          "Suspend it instead - suspension hides it from customers without destroying past invoices.";   // ends with something to do
                return false;   // false plus a message, so the form reports the reason
            }

            // Its own connection from here: the deletes must all succeed or all be undone.
            using (SqlConnection conn = _db.GetConnection())
            {
                conn.Open();   // explicit, unlike ExecuteTable where the adapter opens it
                // The transaction is the point: five deletes have to become one operation.
                using (SqlTransaction tx = conn.BeginTransaction())
                {
                    // Guarded, because a failure part way must roll back, not leave half a shop.
                    try
                    {
                        // Hoist OwnerId first - the DELETE below destroys the row a subquery would read.
                        int ownerId;
                        // A separate command, so the value is in C# memory before any DELETE runs.
                        using (SqlCommand read = new SqlCommand(
                            // Given the transaction too, so it sees the same uncommitted state.
                            "SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @Id;", conn, tx))
                        {
                            read.Parameters.AddWithValue("@Id", pharmacyId);   // parameterised even here
                            object value = read.ExecuteScalar();               // one column of one row
                            // null means no rows, DBNull a null column; 0 is the sentinel for both.
                            ownerId = value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
                        }

                        // Order dictated by the foreign keys, deepest child first.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Offers WHERE MedicineId IN (SELECT MedicineId FROM Medicines WHERE PharmacyId = @Id);", pharmacyId);   // the IN subquery still reads Medicines
                        // Cart lines are live baskets, not history, so removing them is correct.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Cart WHERE MedicineId IN (SELECT MedicineId FROM Medicines WHERE PharmacyId = @Id);", pharmacyId);     // same rule: Medicines must still exist
                        // A HARD delete, safe only because the guard proved there are no orders.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Medicines WHERE PharmacyId = @Id;", pharmacyId);   // the last statement that reads Medicines

                        // Pharmacies before Users: FK_Pharmacies_Owner has no ON DELETE CASCADE.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Pharmacies WHERE PharmacyId = @Id;", pharmacyId);   // OwnerId stops being readable here

                        // 0 means the read found nothing, so there is no login to remove.
                        if (ownerId != 0)
                        {
                            // Written out, because ExecuteInTx always binds its value as @Id.
                            using (SqlCommand removeOwner = new SqlCommand(
                                "DELETE FROM Users WHERE UserId = @OwnerId;", conn, tx))   // a literal id, since the subquery's row is gone
                            {
                                // Read BEFORE the pharmacy row was deleted, so it is still usable.
                                removeOwner.Parameters.AddWithValue("@OwnerId", ownerId);
                                removeOwner.ExecuteNonQuery();   // the count is ignored, it is proven above
                            }
                        }

                        // Nothing is durable until this line, which is what makes it one operation.
                        tx.Commit();
                        message = "Pharmacy deleted.";   // the out parameter is assigned on every path
                        return true;                     // true means the whole set committed
                    }
                    // ex is caught rather than allowed to escape, so the rollback always runs.
                    catch (Exception ex)
                    {
                        // Puts the database back exactly as it was: a whole shop, or none of it.
                        tx.Rollback();
                        // The reason travels back out instead of being swallowed.
                        message = ex.Message;
                        return false;   // false after a rollback, so the caller knows nothing changed
                    }
                }
            }
        }

        // A private helper, so the five deletes read as five lines, not five using blocks.
        private static void ExecuteInTx(SqlConnection conn, SqlTransaction tx, string sql, int pharmacyId)
        {
            // using, so the command is disposed even when ExecuteNonQuery throws.
            using (SqlCommand cmd = new SqlCommand(sql, conn, tx))
            {
                // AddWithValue only because this sits outside DbHelper; still a parameter.
                cmd.Parameters.AddWithValue("@Id", pharmacyId);
                // The count is not read: a shop may legitimately have listed no medicines.
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Requirement 9. Sets the shop's commission rate.</summary>
        public bool SetCommissionRate(int pharmacyId, decimal rate)
        {
            // Checked here AND by CK_Pharmacies_Comm; the m suffix keeps both sides decimal.
            if (rate < 0m || rate > 30m) return false;
            // A single statement, so no transaction is needed and DbHelper is enough.
            return _db.ExecuteNonQuery(
                // Only CommissionRate: each order froze its own amount at checkout.
                "UPDATE Pharmacies SET CommissionRate = @Rate WHERE PharmacyId = @Id;",
                DbHelper.P("@Rate", rate),   // decimal all the way, because this is money
                // == 1 this time: the WHERE names a key, so exactly one row must change.
                DbHelper.P("@Id", pharmacyId)) == 1;
        }

        // --- Pharmacy owner: reading and editing his own shop profile ---

        // Returns a typed Pharmacy, because the caller reads properties, not columns.
        public Pharmacy GetById(int pharmacyId)
        {
            // Every column the profile editor shows, so wider than the grid queries above.
            const string sql = @"
-- The full profile, Address and LogoPath included, for the owner's own editor.
SELECT  p.PharmacyId, p.OwnerId, p.PharmacyName, p.LicenseNo, p.Area, p.Address,
        p.ContactPhone, p.LogoPath, p.CommissionRate, p.Status, p.RegisteredAt,   -- read only, but still selected
        u.FullName AS OwnerName   -- from Users, since Pharmacies stores only the key
FROM    Pharmacies p   -- one shop, so the join can only add columns to it
        INNER JOIN Users u ON u.UserId = p.OwnerId   -- INNER, because OwnerId is NOT NULL
-- The primary key alone, so this returns one row or none.
WHERE   p.PharmacyId = @Id;";

            // One round trip, and at most one row because the WHERE names a key.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", pharmacyId));
            // null lets the caller say 'no such shop'; Rows[0] would throw in the service.
            if (table.Rows.Count == 0) return null;

            // Safe to index now, because the check above proved there is a row.
            DataRow row = table.Rows[0];
            // Mapped inline: this is the only query here that fills every column.
            return new Pharmacy
            {
                // Through the DbHelper getters, so a null column becomes an empty value.
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                OwnerId = DbHelper.GetInt(row, "OwnerId"),                     // reaches the login row without a second query
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),        // editable on the profile screen
                LicenseNo = DbHelper.GetString(row, "LicenseNo"),              // shown, never editable
                Area = DbHelper.GetString(row, "Area"),                        // also the customer filter's value
                Address = DbHelper.GetString(row, "Address"),                  // free text, on no grid at all
                ContactPhone = DbHelper.GetString(row, "ContactPhone"),        // text, since a phone is never arithmetic
                LogoPath = DbHelper.GetString(row, "LogoPath"),      // nullable, so "" means no logo chosen
                CommissionRate = DbHelper.GetDecimal(row, "CommissionRate"),   // decimal, because it is applied to money
                Status = DbHelper.GetString(row, "Status"),                    // the owner sees it, the Super Admin sets it
                RegisteredAt = DbHelper.GetDate(row, "RegisteredAt"),          // a null date becomes the default
                OwnerName = DbHelper.GetString(row, "OwnerName")     // from the join, not from Pharmacies
            };
        }

        /// <summary>Requirement 11. The WHERE keeps an owner on his own shop.</summary>
        public bool UpdateProfile(int pharmacyId, string name, string area, string address, string contactPhone, string logoPath)
        {
            // One statement again, so DbHelper is enough: nothing here to keep atomic.
            const string sql = @"
-- One table, one row, five columns; anything an owner must not control is absent.
UPDATE  Pharmacies
-- LicenseNo, Status, CommissionRate and OwnerId are absent on purpose.
SET     PharmacyName = @Name, Area = @Area, Address = @Address,
        ContactPhone = @Phone, LogoPath = @Logo   -- LogoPath is the only one that may be NULL
-- The isolation rule: the id comes from UserSession, never from the form.
WHERE   PharmacyId = @Id;";

            // The return value IS the verdict, which is why the call is the return expression.
            return _db.ExecuteNonQuery(sql,
                // Trimmed, so a trailing space cannot make two identical looking names differ.
                DbHelper.P("@Name", name.Trim()),
                DbHelper.P("@Area", area.Trim()),                 // Area is matched with =, so a stray space never would
                DbHelper.P("@Address", address.Trim()),           // free text, trimmed for the same tidiness
                DbHelper.P("@Phone", contactPhone.Trim()),        // a trailing space is the same number
                // Blank becomes NULL, because the screen tests for null before loading a file.
                DbHelper.P("@Logo", string.IsNullOrWhiteSpace(logoPath) ? null : logoPath),
                // == 1: the WHERE names the key, so a 0 means the id no longer exists.
                DbHelper.P("@Id", pharmacyId)) == 1;
        }

        // --- Shared lookups that fill the customer's filter ComboBoxes ---

        /// <summary>The distinct areas, used by the customer's Area filter.</summary>
        public List<string> GetAreas(bool approvedOnly)
        {
            // Plain strings, because the customer search filters on the area TEXT itself.
            List<string> areas = new List<string>();
            // Short enough to pass inline rather than through a const.
            DataTable table = _db.ExecuteTable(
                // DISTINCT, plus the optional-filter pattern again, on a bool this time.
                "SELECT DISTINCT Area FROM Pharmacies WHERE (@ApprovedOnly = 0 OR Status = 'Approved') ORDER BY Area;",
                DbHelper.P("@ApprovedOnly", approvedOnly ? 1 : 0));   // an int, since the pattern compares to 0

            // Braceless foreach; GetString turns a null area into an empty entry, not null.
            foreach (DataRow row in table.Rows)
                areas.Add(DbHelper.GetString(row, "Area"));   // the one statement the foreach governs

            return areas;   // possibly empty, which the ComboBox shows as an empty list
        }

        /// <summary>Approved pharmacies only, for the customer's Pharmacy filter.</summary>
        public List<Pharmacy> GetApprovedList()
        {
            // Objects, not strings: the search filters by PharmacyId, so it needs a key.
            List<Pharmacy> list = new List<Pharmacy>();
            // Inline SQL and no parameters, because nothing about this query varies.
            DataTable table = _db.ExecuteTable(
                // Status is fixed in the text: a pending shop must never reach a customer.
                "SELECT PharmacyId, PharmacyName, Area FROM Pharmacies WHERE Status = 'Approved' ORDER BY PharmacyName;");

            // One Pharmacy per row, built by hand because only three properties are known.
            foreach (DataRow row in table.Rows)
            {
                list.Add(new Pharmacy   // an object initialiser, so it is complete before the Add
                {
                    // Only the three selected columns; the rest stay at their defaults.
                    PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                    PharmacyName = DbHelper.GetString(row, "PharmacyName"),   // the text the ComboBox shows
                    Area = DbHelper.GetString(row, "Area")                    // the district beside the name
                });
            }
            return list;   // empty until something is approved, shown as an empty filter
        }

        // One method with a parameter, so the counting rule exists in exactly one place.
        public int CountByStatus(string status)
        {
            // COUNT is answered by the database, so only the number crosses the wire.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Pharmacies WHERE Status = @Status;",   // COUNT(*), so no null column shrinks it
                // A parameter even though every caller passes a literal: the rule holds here too.
                DbHelper.P("@Status", status));
        }
    }
}
