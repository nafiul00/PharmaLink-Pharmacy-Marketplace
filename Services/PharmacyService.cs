using System.Data;                      // DataTable and DataRow, the shape every read here returns
using Microsoft.Data.SqlClient;         // SqlConnection, SqlCommand and SqlTransaction, needed only by Delete
using PharmaLinkApp.Database;           // DbHelper, which every other method in this class goes through
using PharmaLinkApp.Models;             // Pharmacy, the typed object a shop row becomes

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// The Super Admin's control over shops (approve, suspend, commission rate)
    /// and the pharmacy owner's control over his own shop profile.
    /// </summary>
    public class PharmacyService
    {
        // The same shared helper as every other service. Delete is the one method that steps
        // around it and opens its own connection, because it needs several statements to
        // succeed or fail together and DbHelper deliberately offers no transaction of its own.
        private readonly DbHelper _db = new DbHelper();

        // ---------------------------------------------------------------------
        //  SUPER ADMIN
        // ---------------------------------------------------------------------

        /// <summary>Requirement 2 and 3: every pharmacy with its owner, licence, area and status.</summary>
        public DataTable Search(string keyword, string status, string area)
        {
            const string sql = @"
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, u.Email AS OwnerEmail,
        -- LicenseNo is on the row because approving a shop means checking a licence, and the
        -- Super Admin needs it in front of him rather than behind another click.
        p.LicenseNo, p.Area, p.ContactPhone, p.CommissionRate, p.Status, p.RegisteredAt,
        -- A correlated scalar subquery rather than a JOIN plus GROUP BY. Joining Medicines
        -- would repeat the pharmacy row once per medicine and force every other column into a
        -- GROUP BY; computed here, each shop stays exactly one row.
        (SELECT COUNT(*) FROM Medicines m WHERE m.PharmacyId = p.PharmacyId) AS Medicines,
        -- The same reasoning for the rating, one level deeper: reviews are written against a
        -- MEDICINE, so the subquery joins back through Medicines to reach this shop's reviews.
        -- ISNULL turns 'no reviews yet' into 0 rather than an empty cell, so the column stays
        -- numeric and the grid can sort on it.
        ISNULL((SELECT CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2))
                FROM   Reviews r
                       INNER JOIN Medicines m2 ON m2.MedicineId = r.MedicineId
                -- m2 is a second alias over Medicines because the outer query already uses m
                -- in the count above; r.IsHidden = 0 keeps moderated reviews out of the average.
                WHERE  m2.PharmacyId = p.PharmacyId AND r.IsHidden = 0), 0) AS AverageRating
FROM    Pharmacies p
        -- INNER JOIN is safe because OwnerId is a NOT NULL foreign key: every shop has exactly
        -- one owner row, so this join can neither drop a pharmacy nor duplicate one.
        INNER JOIN Users u ON u.UserId = p.OwnerId
-- The same optional-filter pattern the rest of the project uses: an empty string means 'do
-- not filter on this', so three controls on one form drive one query and any subset can be
-- combined without the application building different SQL for each case.
WHERE   (@Keyword = '' OR p.PharmacyName LIKE '%' + @Keyword + '%'
                       -- Three searchable columns, because the Super Admin may be looking for
                       -- a shop by name, chasing a licence number, or looking for a person.
                       OR p.LicenseNo    LIKE '%' + @Keyword + '%'
                       OR u.FullName     LIKE '%' + @Keyword + '%')
  AND   (@Status  = '' OR p.Status = @Status)   -- exact match: Status is chosen from a ComboBox, not typed
  AND   (@Area    = '' OR p.Area   = @Area)
-- Ordered by what needs attention rather than alphabetically: the CASE maps the three statuses
-- onto 0, 1 and 2 so pending registrations sit at the top of the grid where they get dealt
-- with, with the name as the tie-breaker inside each group.
ORDER BY CASE p.Status WHEN 'Pending' THEN 0 WHEN 'Approved' THEN 1 ELSE 2 END, p.PharmacyName;";

            return _db.ExecuteTable(sql,
                // ?? "" on all three: a null becomes DBNull inside DbHelper.P, and DBNull = ''
                // is UNKNOWN rather than true in SQL, so the optional filter would stop being
                // optional and an empty search box would return no shops at all.
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@Status", status ?? ""),
                DbHelper.P("@Area", area ?? ""));
        }

        public DataTable GetPending()
        {
            const string sql = @"
-- A narrower column list than Search: this is the approval queue, so it carries only what is
-- needed to judge a registration - who applied, under which licence and from where.
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, p.LicenseNo,
        p.Area, p.ContactPhone, p.RegisteredAt
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
-- Status is fixed in the query rather than passed in, because this method has exactly one
-- purpose. Search already covers the case where the status is a choice.
WHERE   p.Status = 'Pending'
-- Oldest first, so applications are dealt with in the order they arrived rather than the
-- newest one always sitting at the top.
ORDER BY p.RegisteredAt;";

            // No parameters at all, because nothing in this query varies.
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
-- TWO rows change, because the shop record and the login account are separate
-- concerns: Pharmacies says whether the shop may trade, Users says whether the
-- person may sign in. Approving one without the other would leave an owner who
-- can log in to a shop that is invisible, or a live shop nobody can administer.
-- One transaction means both flip or neither does.
--
-- The transaction is written into the SQL rather than managed in C# because both
-- statements are known up front and neither depends on the result of the other, so
-- nothing has to come back to the application between them.
BEGIN TRANSACTION;
    UPDATE Pharmacies SET Status = 'Approved' WHERE PharmacyId = @PharmacyId;
    -- The owner is found THROUGH the pharmacy row, so the caller only ever needs
    -- to know the PharmacyId. FK_Pharmacies_Owner guarantees this subquery finds
    -- exactly one UserId, and UQ_Pharmacies_Owner guarantees it is not shared.
    --
    -- The subquery is safe here because nothing in this batch deletes the row it reads;
    -- Delete, further down, has to hoist the same value into a variable first for exactly
    -- that reason.
    UPDATE Users      SET Status = 'Active'
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);
COMMIT TRANSACTION;";

            // ExecuteNonQuery returns the rows affected by the whole batch, so a successful
            // approval reports 2. The test is > 0 rather than == 2 because the count is the sum
            // of two statements rather than a single row's identity; a bad id updates nothing
            // and returns 0, which is the failure this is guarding against.
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
-- THREE updates, one transaction. Approve needed two; suspending needs a third
-- because the shop's stock must leave the customer catalogue as well.
-- Note what is absent: there is no DELETE anywhere here. Past orders, invoices
-- customers already hold and the sales history all stay exactly as they were.
BEGIN TRANSACTION;
    UPDATE Pharmacies SET Status   = 'Suspended' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status   = 'Suspended'   -- the owner can no longer sign in
    -- Found through the pharmacy row again, so a suspension needs only the shop id.
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);
    -- Every medicine this shop lists is delisted, the same soft delete the owner's
    -- own Delist button uses. Customer queries filter on IsActive = 1, so the stock
    -- vanishes from the catalogue without a single row being destroyed - and
    -- Reinstate simply sets all three back.
    --
    -- Strictly the catalogue queries also test ph.Status = 'Approved', so this third
    -- update is belt and braces; it matters because it leaves the medicines in the
    -- same state the owner's own screens expect, and because it makes the reverse a
    -- single symmetrical statement.
    UPDATE Medicines  SET IsActive = 0           WHERE PharmacyId = @PharmacyId;
COMMIT TRANSACTION;";

            // > 0 for the same reason as Approve: this is the total across three statements, and
            // the medicine count varies by shop, so no fixed number could be asserted.
            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>Puts a suspended shop back on the platform.</summary>
        public bool Reinstate(int pharmacyId)
        {
            const string sql = @"
-- The exact mirror of Suspend, statement for statement, which is only possible because
-- suspension destroyed nothing: the shop keeps its id, its stock, its reviews and its
-- sales history, so reinstating it is three flag flips rather than a re-registration.
BEGIN TRANSACTION;
    UPDATE Pharmacies SET Status   = 'Approved' WHERE PharmacyId = @PharmacyId;
    UPDATE Users      SET Status   = 'Active'
    WHERE  UserId = (SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId);
    -- This does relist medicines the owner had delisted himself before the suspension,
    -- which is the accepted cost of keeping the reverse a single statement; the owner can
    -- delist them again from his own screen.
    UPDATE Medicines  SET IsActive = 1          WHERE PharmacyId = @PharmacyId;
COMMIT TRANSACTION;";

            return _db.ExecuteNonQuery(sql, DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>A rejected registration is suspended rather than erased, so the licence number stays taken.</summary>
        public bool Reject(int pharmacyId)
        {
            // Deliberately delegates rather than repeating the SQL. Rejecting and suspending
            // mean the same thing to the data - the shop may not trade and the owner may not
            // sign in - and the separate name exists so the Super Admin's screen can label the
            // button honestly. Keeping one implementation means the two can never drift apart.
            //
            // Rejection is not a delete, so the licence number stays taken and the same person
            // cannot re-register the same licence to slip past a refusal.
            return Suspend(pharmacyId);
        }

        /// <summary>
        /// Requirement 3: delete a shop entirely. Only possible while it has no
        /// order history; a shop that has traded is suspended instead, because
        /// deleting it would destroy invoices customers already hold.
        /// </summary>
        public bool Delete(int pharmacyId, out string message)
        {
            // The guard comes FIRST, before a single row is touched. Orders are the one thing
            // that cannot be recreated: an invoice a customer already holds has to keep
            // resolving, and the order rows point at this pharmacy.
            int orders = _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id;",
                DbHelper.P("@Id", pharmacyId));

            if (orders > 0)
            {
                // The refusal names the number and then names the alternative, so the Super
                // Admin is not left guessing why the button did nothing. This is the same
                // hard-delete-versus-soft-delete decision the medicines make, taken one level up:
                // a shop with history is suspended, a shop with none can genuinely be removed.
                message = "This pharmacy has " + orders + " order(s) in its history, so it cannot be deleted. " +
                          "Suspend it instead - suspension hides it from customers without destroying past invoices.";
                return false;
            }

            // From here the method manages its own connection instead of calling DbHelper,
            // because the deletes must all succeed or all be undone, and one of them depends on
            // a value read inside the same transaction. using blocks on both the connection and
            // the transaction mean each is disposed even if a statement throws.
            using (SqlConnection conn = _db.GetConnection())
            {
                // Opened explicitly, unlike DbHelper.ExecuteTable where the adapter does it.
                conn.Open();
                using (SqlTransaction tx = conn.BeginTransaction())
                {
                    try
                    {
                        // The owner's UserId is read first, because the Pharmacies row
                        // is the only thing that points at it and that row is about to go.
                        //
                        // Hoisting it into a variable rather than writing the subquery used by
                        // Approve and Suspend is the whole reason this is not one SQL batch: a
                        // 'DELETE FROM Users WHERE UserId = (SELECT OwnerId FROM Pharmacies ...)'
                        // placed after the pharmacy delete would read a row that no longer
                        // exists, match nothing, and leave an orphaned login behind silently.
                        int ownerId;
                        using (SqlCommand read = new SqlCommand(
                            // The command is given the transaction as well as the connection, so
                            // this read sees the same uncommitted state the deletes below write.
                            "SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @Id;", conn, tx))
                        {
                            read.Parameters.AddWithValue("@Id", pharmacyId);
                            object value = read.ExecuteScalar();
                            // ExecuteScalar returns null when there were no rows and DBNull when
                            // the column itself was null; both mean 'no owner to remove', and 0
                            // is used as that sentinel because no IDENTITY key can be 0.
                            ownerId = value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
                        }

                        // The order of these three is dictated by the foreign keys, deepest
                        // child first. Offers and Cart both point at Medicines, so they have to
                        // go before the medicines they reference.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Offers WHERE MedicineId IN (SELECT MedicineId FROM Medicines WHERE PharmacyId = @Id);", pharmacyId);
                        // Cart lines are live baskets belonging to customers, not history, so
                        // removing them is correct: the medicine they point at is about to stop
                        // existing and the line could never be checked out.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Cart WHERE MedicineId IN (SELECT MedicineId FROM Medicines WHERE PharmacyId = @Id);", pharmacyId);
                        // Now the medicines themselves. This is a HARD delete, and it is only
                        // safe because the guard at the top proved there are no orders: with no
                        // OrderItems rows pointing at them, FK_OrderItems_Medicine has nothing
                        // to protect and no invoice loses its product.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Medicines WHERE PharmacyId = @Id;", pharmacyId);

                        // Pharmacies before Users. FK_Pharmacies_Owner points from
                        // Pharmacies to Users and has no ON DELETE CASCADE, so removing
                        // the owner while the shop row still references it is a foreign
                        // key violation and the whole transaction rolls back.
                        ExecuteInTx(conn, tx,
                            "DELETE FROM Pharmacies WHERE PharmacyId = @Id;", pharmacyId);

                        // 0 means the read above found nothing, so there is no login to remove
                        // and attempting one would delete by a key that matches no row.
                        if (ownerId != 0)
                        {
                            // Written out rather than passed to ExecuteInTx, because that helper
                            // always binds its one value as @Id and this statement needs
                            // @OwnerId - a different key, from a different table.
                            using (SqlCommand removeOwner = new SqlCommand(
                                "DELETE FROM Users WHERE UserId = @OwnerId;", conn, tx))
                            {
                                // The value read BEFORE the pharmacy row was deleted, which is
                                // why it is still available here.
                                removeOwner.Parameters.AddWithValue("@OwnerId", ownerId);
                                removeOwner.ExecuteNonQuery();
                            }
                        }

                        // Nothing is durable until this line. Up to here every delete could
                        // still be undone, which is what makes the five statements one operation.
                        tx.Commit();
                        message = "Pharmacy deleted.";
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // Any failure puts the database back exactly as it was, so a foreign key
                        // this method did not anticipate leaves a whole shop rather than half of
                        // one. Without this the earlier deletes would already have committed.
                        tx.Rollback();
                        // The reason travels back through the out parameter instead of being
                        // swallowed, so the form can show what the database objected to.
                        message = ex.Message;
                        return false;
                    }
                }
            }
        }

        // A small private helper so the five deletes above read as five lines rather than five
        // nested using blocks. It takes the connection AND the transaction because a command
        // created on a connection with an open transaction must be enlisted in it explicitly,
        // and it is private because the @Id convention it assumes only holds inside Delete.
        private static void ExecuteInTx(SqlConnection conn, SqlTransaction tx, string sql, int pharmacyId)
        {
            using (SqlCommand cmd = new SqlCommand(sql, conn, tx))
            {
                // AddWithValue rather than DbHelper.P only because this method is outside the
                // helper; the value is still sent as a parameter, so these statements are as
                // injection safe as every other query in the project.
                cmd.Parameters.AddWithValue("@Id", pharmacyId);
                // The row count is not read: each of these deletes may legitimately affect zero
                // rows, for instance a shop that never listed a medicine.
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
            // Checked in the application AND by CK_Pharmacies_Comm in the database. The
            // application check exists so the Super Admin gets a clean refusal instead of a
            // constraint violation; the database check exists because a rule enforced only in
            // C# is a rule any other client could ignore. Neither one makes the other redundant.
            //
            // The literals carry the m suffix so they are decimals like the parameter: without
            // it they would be doubles and the comparison would force a conversion.
            if (rate < 0m || rate > 30m) return false;
            return _db.ExecuteNonQuery(
                // Only CommissionRate is written. The new rate applies to future orders alone,
                // because each order stores its own CommissionAmount at checkout, so nothing
                // here has to reach back and recalculate history.
                "UPDATE Pharmacies SET CommissionRate = @Rate WHERE PharmacyId = @Id;",
                DbHelper.P("@Rate", rate),
                // == 1 rather than > 0 this time: the WHERE names a primary key, so exactly one
                // row must change and anything else is a failure worth reporting.
                DbHelper.P("@Id", pharmacyId)) == 1;
        }

        // ---------------------------------------------------------------------
        //  PHARMACY OWNER
        // ---------------------------------------------------------------------

        public Pharmacy GetById(int pharmacyId)
        {
            const string sql = @"
-- The full profile, including Address and LogoPath, because this feeds the owner's own
-- editable profile screen rather than a grid.
SELECT  p.PharmacyId, p.OwnerId, p.PharmacyName, p.LicenseNo, p.Area, p.Address,
        p.ContactPhone, p.LogoPath, p.CommissionRate, p.Status, p.RegisteredAt,
        -- The owner's name comes from Users, because Pharmacies stores only the key; showing
        -- the name means this one join rather than a second query from the form.
        u.FullName AS OwnerName
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
-- The primary key alone, so this returns one row or none. No PharmacyId isolation clause is
-- needed because the id IS the thing being asked for, and the caller passes the session's own.
WHERE   p.PharmacyId = @Id;";

            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", pharmacyId));
            // Returning null lets the caller say 'no such shop'. Reading Rows[0] straight away
            // would throw from inside the service instead, which the form cannot act on.
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];
            // Mapped inline rather than through a shared mapper, because this is the only query
            // in the class that produces a Pharmacy with every column filled in.
            return new Pharmacy
            {
                // Each value goes through the DbHelper helper for its type, so a null column
                // becomes the type's empty value instead of throwing during conversion.
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                OwnerId = DbHelper.GetInt(row, "OwnerId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                LicenseNo = DbHelper.GetString(row, "LicenseNo"),
                Area = DbHelper.GetString(row, "Area"),
                Address = DbHelper.GetString(row, "Address"),
                ContactPhone = DbHelper.GetString(row, "ContactPhone"),
                LogoPath = DbHelper.GetString(row, "LogoPath"),      // nullable in the table, so "" here means no logo chosen
                CommissionRate = DbHelper.GetDecimal(row, "CommissionRate"),   // decimal, never double, because it is applied to money
                Status = DbHelper.GetString(row, "Status"),
                RegisteredAt = DbHelper.GetDate(row, "RegisteredAt"),
                OwnerName = DbHelper.GetString(row, "OwnerName")     // from the join, not from Pharmacies itself
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
-- Five editable columns, and note which four are absent. LicenseNo is not here because a
-- different licence is a different registration and would need approving again. Status is not
-- here because only the Super Admin may change it, and CommissionRate is not here because an
-- owner must not be able to set his own. OwnerId is not here because a shop does not change
-- hands through a profile form.
SET     PharmacyName = @Name, Area = @Area, Address = @Address,
        ContactPhone = @Phone, LogoPath = @Logo
-- The isolation rule: the id comes from UserSession, so this statement can only ever reach
-- the caller's own shop even though the method itself would accept any number.
WHERE   PharmacyId = @Id;";

            return _db.ExecuteNonQuery(sql,
                // Trim on every free-text field, so a trailing space cannot produce two shops
                // whose names look identical on screen but compare as different values.
                DbHelper.P("@Name", name.Trim()),
                DbHelper.P("@Area", area.Trim()),
                DbHelper.P("@Address", address.Trim()),
                DbHelper.P("@Phone", contactPhone.Trim()),
                // Blank becomes NULL rather than an empty string, because the screen tests the
                // logo path for null before trying to load a file; an empty string would be
                // treated as a path and fail when it is opened.
                DbHelper.P("@Logo", string.IsNullOrWhiteSpace(logoPath) ? null : logoPath),
                // == 1: the WHERE names the primary key, so exactly one row must change. A 0
                // means the id no longer exists and is reported as failure rather than ignored.
                DbHelper.P("@Id", pharmacyId)) == 1;
        }

        // ---------------------------------------------------------------------
        //  SHARED LOOKUPS
        // ---------------------------------------------------------------------

        /// <summary>The distinct areas, used by the customer's Area filter.</summary>
        public List<string> GetAreas(bool approvedOnly)
        {
            // A list of plain strings, because the Area ComboBox needs no key: the customer
            // search filters on the area TEXT, so the selected item is already the value.
            List<string> areas = new List<string>();
            DataTable table = _db.ExecuteTable(
                // DISTINCT because many shops share an area and the ComboBox must list each one
                // once. The optional-filter pattern again, on a bool this time: the customer's
                // screen passes true so no suspended shop's district appears in the list, while
                // the Super Admin's screens can ask for every area with false.
                "SELECT DISTINCT Area FROM Pharmacies WHERE (@ApprovedOnly = 0 OR Status = 'Approved') ORDER BY Area;",
                DbHelper.P("@ApprovedOnly", approvedOnly ? 1 : 0));   // converted to int, because the pattern compares against 0

            // A plain foreach with no braces: one statement per row, and GetString turns a null
            // area into an empty entry rather than putting a null into the list.
            foreach (DataRow row in table.Rows)
                areas.Add(DbHelper.GetString(row, "Area"));

            return areas;
        }

        /// <summary>Approved pharmacies only, for the customer's Pharmacy filter.</summary>
        public List<Pharmacy> GetApprovedList()
        {
            // Objects rather than strings this time, because the customer search filters by
            // PharmacyId: the ComboBox needs a key behind the label it displays.
            List<Pharmacy> list = new List<Pharmacy>();
            DataTable table = _db.ExecuteTable(
                // Three columns only, and Status fixed in the text rather than passed in: a
                // pending or suspended shop must never appear in a customer's filter, so this
                // is not made optional the way GetAreas is.
                "SELECT PharmacyId, PharmacyName, Area FROM Pharmacies WHERE Status = 'Approved' ORDER BY PharmacyName;");

            foreach (DataRow row in table.Rows)
            {
                list.Add(new Pharmacy
                {
                    // Only the three columns the query selected are mapped. The rest of the
                    // Pharmacy object stays at its default, which is all a filter list needs.
                    PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                    PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                    Area = DbHelper.GetString(row, "Area")
                });
            }
            return list;
        }

        public int CountByStatus(string status)
        {
            // One method for all three dashboard tiles, with the status as a parameter rather
            // than three near-identical methods. COUNT is answered by the database, so nothing
            // but the single number crosses the wire.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Pharmacies WHERE Status = @Status;",
                // Passed as a parameter even though every caller supplies a literal, because the
                // rule in this project is that no value is ever concatenated into SQL text.
                DbHelper.P("@Status", status));
        }
    }
}
