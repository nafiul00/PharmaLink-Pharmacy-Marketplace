using System.Data;                  // DataTable and DataRow, the shape every read comes back in
using Microsoft.Data.SqlClient;     // SqlConnection and friends, for RegisterPharmacyOwner only
using PharmaLinkApp.Database;       // DbHelper, the only class that knows the connection string
using PharmaLinkApp.Helpers;        // PasswordHelper: salt creation, hashing, verification
using PharmaLinkApp.Models;         // User and Pharmacy, the typed objects the rows become

// Services keep SqlCommand out of Forms: a form sees only these methods.
namespace PharmaLinkApp.Services
{
    // Every query here goes through DbHelper except RegisterPharmacyOwner.

    /// <summary>Login, registration, profile editing and password change.</summary>
    public class AuthService
    {
        // One helper per service, since DbHelper opens and closes a connection per call.
        private readonly DbHelper _db = new DbHelper();

        // ---- LOGIN ----

        /// <summary>Reads the row by email, then verifies the hash in memory.</summary>
        public User Login(string email, string password, out string failureReason)
        {
            failureReason = "";     // out parameters must be assigned on every path

            // Password not in the WHERE: the salt is per user, so read the row first.
            const string sql = @"
-- Everything the session needs in ONE round trip, so the halves cannot disagree.
SELECT  u.UserId, u.FullName, u.Email, u.PasswordHash, u.PasswordSalt,   -- salt and hash travel back for the C# comparison
        u.Phone, u.Address, u.UserType, u.Status, u.CreatedAt,           -- Status decides admission, UserType the dashboard
        p.PharmacyId, p.PharmacyName, p.Status AS PharmacyStatus         -- aliased, because BOTH tables have a Status column
FROM    Users u                                                          -- a login always starts from an account
        -- LEFT, so a Customer or SuperAdmin still returns a row with NULLs here.
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
-- UQ_Users_Email means this matches no rows or exactly one, so Rows[0] is safe.
WHERE   u.Email = @Email;";

            // A parameter, never concatenation, so a typed apostrophe stays data.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Email", email.Trim()));

            // Rows.Count, not null: ExecuteTable returns an empty table when nothing matched.
            if (table.Rows.Count == 0)
            {
                // A bank would be vaguer; the clearer message is more useful here.
                failureReason = "No account is registered with that email address.";
                return null;        // null is the single "not logged in" answer callers test
            }

            DataRow row = table.Rows[0];      // Email is UNIQUE, so there can only be one

            string salt = DbHelper.GetString(row, "PasswordSalt");   // 12 random bytes as 16 Base64 characters
            string hash = DbHelper.GetString(row, "PasswordHash");   // Base64(SHA-256(salt + password)) as stored

            // Identity is settled before anything else about the account is looked at.
            if (!PasswordHelper.Verify(password, salt, hash))
            {
                failureReason = "That password is not correct.";   // names the password, so the right box is retyped
                return null;        // the row matched but the credential failed
            }

            // Admission is a separate question, asked only once identity is proven.
            string status = DbHelper.GetString(row, "Status");
            if (status == "Pending")   // registered but not yet approved by the Super Admin
            {
                // An owner whose DGDA licence the Super Admin has not checked yet.
                failureReason = "This account is still waiting for Super Admin approval.";
                return null;        // refused, but approval later lets the same credentials in
            }
            if (status == "Suspended")   // approved once, then stopped by the Super Admin
            {
                // Reversible and keeps the row, so past orders keep their foreign keys.
                failureReason = "This account has been suspended by the Super Admin.";
                return null;        // 'Active' is the only value left, and it falls through
            }

            // Only now is the object the rest of the application carries around built.
            User user = new User
            {
                UserId = DbHelper.GetInt(row, "UserId"),                  // the key every later query filters on
                FullName = DbHelper.GetString(row, "FullName"),           // shown in the dashboard header
                Email = DbHelper.GetString(row, "Email"),                 // the stored spelling, so casing is consistent
                // GetString turns DBNull into "" and GetInt into 0, so no null checks here.
                Phone = DbHelper.GetString(row, "Phone"),
                Address = DbHelper.GetString(row, "Address"),             // prefills the delivery box at checkout
                UserType = DbHelper.GetString(row, "UserType"),           // 'Customer', 'Admin' or 'SuperAdmin'
                // Reuses the local read above, so it cannot disagree with the two guards.
                Status = status,
                CreatedAt = DbHelper.GetDate(row, "CreatedAt"),           // the join date the profile screen prints
                // 0 for a Customer or SuperAdmin, and every Admin query filters on it.
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName")    // "" for anyone who owns no shop
                // PasswordHash and PasswordSalt are not copied: the credential stops here.
            };

            // "Admin" is this project's name for a pharmacy owner, not the platform owner.
            if (user.UserType == "Admin")
            {
                // CK_Pharmacies_Status allows Pending, Approved and Suspended only.
                string pharmacyStatus = DbHelper.GetString(row, "PharmacyStatus");
                if (pharmacyStatus == "Pending")   // the shop is filed but not yet vetted
                {
                    failureReason = "Your pharmacy registration has not been approved yet.";   // names the shop, so the owner knows what to chase
                    return null;    // refused at shop level even though the account passed
                }
                if (pharmacyStatus == "Suspended")   // the shop was trading and has been stopped
                {
                    // The shop is stopped, not the person: the Users row can stay Active.
                    failureReason = "Your pharmacy has been suspended by the Super Admin.";
                    return null;    // 'Approved' is the only value left, and it falls through
                }
            }

            // Email found, password verified, account admitted, and the shop too.
            return user;
        }

        // ---- REGISTRATION ----

        // Asked while the user is still typing, so a clash appears beside the box.
        public bool EmailExists(string email)
        {
            // A courtesy check; UQ_Users_Email is still the real guarantee against a race.
            return _db.ExecuteScalarInt(
                // COUNT(*) always returns one row and one value, so there is no empty case.
                "SELECT COUNT(*) FROM Users WHERE Email = @Email;",
                // Trimmed to match what RegisterCustomer stores, or a space would mislead.
                DbHelper.P("@Email", email.Trim())) > 0;
        }

        // The same pre-flight for the mobile, so the form can light up the right box.
        public bool PhoneExists(string phone)
        {
            // UQ_Users_Phone: one number per account, so one person cannot open several.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Users WHERE Phone = @Phone;",   // one statement, one value
                DbHelper.P("@Phone", phone.Trim())) > 0;              // > 0 turns the count into a yes/no
        }

        // Only the pharmacy half of the form calls this: a customer has no licence.
        public bool LicenseExists(string licenseNo)
        {
            // UQ_Pharmacies_License stops one licence registering two storefronts.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Pharmacies WHERE LicenseNo = @LicenseNo;",   // the licence lives on the shop row
                DbHelper.P("@LicenseNo", licenseNo.Trim())) > 0;                   // trimmed: the index compares text exactly
        }

        /// <summary>Registers a customer, created Active and usable at once.</summary>
        public int RegisterCustomer(User user, string password)
        {
            // Salt first: the hash is computed FROM it, and a fresh one per account.
            string salt = PasswordHelper.CreateSalt();
            string hash = PasswordHelper.Hash(password, salt);   // the plain text is never kept

            // One batch: the INSERT plus the SELECT that reads back the generated id.
            const string sql = @"
-- 'Customer' and 'Active' are literals, so no field can change the role.
INSERT INTO Users (FullName, Email, PasswordHash, PasswordSalt, Phone, Address, UserType, Status)
-- Columns named explicitly, so a later ALTER TABLE cannot shift a value sideways.
VALUES (@FullName, @Email, @Hash, @Salt, @Phone, @Address, 'Customer', 'Active');
-- SCOPE_IDENTITY() is scoped to this INSERT, and CAST because it is NUMERIC.
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            // ExecuteScalarInt reads the final SELECT, so the caller gets the new UserId.
            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@FullName", user.FullName.Trim()),   // trimmed: it is printed in every header
                DbHelper.P("@Email", user.Email.Trim()),         // trimmed: UQ_Users_Email compares text exactly
                // The hash and the salt, never the password itself.
                DbHelper.P("@Hash", hash),
                DbHelper.P("@Salt", salt),                       // stored beside the hash, since both are needed
                DbHelper.P("@Phone", user.Phone.Trim()),         // trimmed: UQ_Users_Phone is an exact match too
                // Address is NOT trimmed: free text with no UNIQUE constraint on it.
                DbHelper.P("@Address", user.Address));
        }

        /// <summary>Both rows are written in one transaction, both 'Pending'.</summary>
        public int RegisterPharmacyOwner(User owner, Pharmacy pharmacy, string password)
        {
            // Hashed before the connection opens, so no lock is held for avoidable work.
            string salt = PasswordHelper.CreateSalt();
            string hash = PasswordHelper.Hash(password, salt);   // computed once and reused below

            // Its own connection, because the two INSERTs must share one transaction.
            using (SqlConnection conn = _db.GetConnection())
            {
                // Explicit Open: a transaction cannot start on a closed connection.
                conn.Open();
                // Default ReadCommitted is enough: this method inserts and never re-reads.
                using (SqlTransaction tx = conn.BeginTransaction())
                {
                    // try around BOTH inserts, so the rollback is reached either way.
                    try
                    {
                        // Declared out here, because the second INSERT needs it later.
                        int newUserId;

                        // Same shape as RegisterCustomer, but 'Admin' and 'Pending'.
                        const string insertUser = @"
-- 'Pending' is the point: Login refuses it, so no shop starts trading unchecked.
INSERT INTO Users (FullName, Email, PasswordHash, PasswordSalt, Phone, Address, UserType, Status)
-- Neither literal is a parameter, so the form cannot make this an Active account.
VALUES (@FullName, @Email, @Hash, @Salt, @Phone, @Address, 'Admin', 'Pending');
-- The new UserId is needed at once as Pharmacies.OwnerId, so it is selected back.
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                        // The third argument, tx, enlists this command in the transaction.
                        using (SqlCommand cmd = new SqlCommand(insertUser, conn, tx))
                        {
                            // AddWithValue here, but the values still travel apart from the SQL.
                            cmd.Parameters.AddWithValue("@FullName", owner.FullName.Trim());
                            cmd.Parameters.AddWithValue("@Email", owner.Email.Trim());     // UQ_Users_Email, so the space has to go
                            cmd.Parameters.AddWithValue("@Hash", hash);                    // the computed hash, never the typed password
                            cmd.Parameters.AddWithValue("@Salt", salt);                    // useless on its own, and meaningless alone
                            cmd.Parameters.AddWithValue("@Phone", owner.Phone.Trim());     // UQ_Users_Phone, same exact-match reason
                            // DBNull.Value, not null: ADO.NET reads null as "not supplied".
                            cmd.Parameters.AddWithValue("@Address", (object)owner.Address ?? DBNull.Value);
                            // ExecuteScalar returns object, so Convert unboxes the CAST above.
                            newUserId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        // The second half: nothing is durable until the Commit below.
                        const string insertPharmacy = @"
-- The shop is 'Pending' too, so it cannot trade until it is approved on its own.
INSERT INTO Pharmacies (OwnerId, PharmacyName, LicenseNo, Area, Address, ContactPhone, LogoPath, Status)   -- CommissionRate is left to DF_Pharmacies_Comm
-- No SCOPE_IDENTITY() here: nobody needs the new PharmacyId before approval.
VALUES (@OwnerId, @Name, @License, @Area, @Address, @Phone, @Logo, 'Pending');";

                        using (SqlCommand cmd = new SqlCommand(insertPharmacy, conn, tx))   // tx again, so both inserts share it
                        {
                            // The id from a moment ago; UQ_Pharmacies_Owner keeps it one per owner.
                            cmd.Parameters.AddWithValue("@OwnerId", newUserId);
                            cmd.Parameters.AddWithValue("@Name", pharmacy.PharmacyName.Trim());   // the shop name customers search on
                            // Trimmed, or " DGDA-123" and "DGDA-123" would both look unique.
                            cmd.Parameters.AddWithValue("@License", pharmacy.LicenseNo.Trim());
                            // Area drives the "pharmacies near me" filter, so spaces matter.
                            cmd.Parameters.AddWithValue("@Area", pharmacy.Area.Trim());
                            cmd.Parameters.AddWithValue("@Address", pharmacy.Address.Trim());        // the shop's address, not the owner's
                            cmd.Parameters.AddWithValue("@Phone", pharmacy.ContactPhone.Trim());     // the shop's public number
                            // A logo is optional, and null, "" or spaces should all become NULL.
                            cmd.Parameters.AddWithValue("@Logo",
                                string.IsNullOrWhiteSpace(pharmacy.LogoPath) ? (object)DBNull.Value : pharmacy.LogoPath);   // NULL beats an empty path
                            // ExecuteNonQuery: nothing needs the new PharmacyId back.
                            cmd.ExecuteNonQuery();
                        }

                        // Both rows become visible at the same instant, or neither does.
                        tx.Commit();
                        // Confirms the filing only; the owner still waits for approval.
                        return newUserId;
                    }
                    // Catches everything, because any failure has to undo the first INSERT.
                    catch
                    {
                        // Without this the Users table would keep an owner with no shop.
                        tx.Rollback();
                        // Bare throw, so the original stack trace reaches Program.cs intact.
                        throw;
                    }
                }
            }
        }

        // ---- PROFILE AND PASSWORD ----

        // Reads one account by primary key, and returns null when the id is unknown.
        public User GetUser(int userId)
        {
            // Narrower than the login query: this one is for display, not for credentials.
            const string sql = @"
-- No PasswordHash or PasswordSalt here, so they never reach memory at all.
SELECT  u.UserId, u.FullName, u.Email, u.Phone, u.Address, u.UserType,
        u.Status, u.CreatedAt, p.PharmacyId, p.PharmacyName              -- read-only facts plus the two joined columns
FROM    Users u                                                          -- a profile is an account first, a shop sometimes
        -- Same LEFT JOIN, so an owner sees a shop name and a customer sees nothing.
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
-- By primary key this time, because the id came from UserSession.
WHERE   u.UserId = @UserId;";

            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@UserId", userId));   // one trip, at most one row
            // null, not an empty User: "no such account" is a different answer from blank.
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];      // UserId is the primary key, so at most one
            // Built and returned in one expression: nothing has to be decided in between.
            return new User
            {
                UserId = DbHelper.GetInt(row, "UserId"),              // echoed back, so the caller can trust the object
                FullName = DbHelper.GetString(row, "FullName"),       // the editable name box on the profile form
                Email = DbHelper.GetString(row, "Email"),             // shown read only: it is the login identifier
                Phone = DbHelper.GetString(row, "Phone"),             // editable, and compared against itself on save
                Address = DbHelper.GetString(row, "Address"),         // DBNull becomes "", so the box binds unguarded
                UserType = DbHelper.GetString(row, "UserType"),       // decides the caption over the address box
                Status = DbHelper.GetString(row, "Status"),           // display only; this screen never writes it
                CreatedAt = DbHelper.GetDate(row, "CreatedAt"),       // printed as "Member since"
                // 0 and "" when the join matched no row, which is every customer.
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName")   // blank for a customer, as the screen expects
            };
        }

        /// <summary>Email is not editable: it is the login identifier.</summary>
        public bool UpdateProfile(int userId, string fullName, string phone, string address)
        {
            // The SET list is the security story: an unnamed column cannot be written.
            const string sql = @"
-- One table and one row; a shop's own details are edited through PharmacyService.
UPDATE  Users
-- Three columns only: Email, UserType and Status are absent on purpose.
SET     FullName = @FullName, Phone = @Phone, Address = @Address
-- Keyed on the UserSession id, so one account can only ever edit itself.
WHERE   UserId = @UserId;";

            // == 1 is the write and the proof: anything else means the id was wrong.
            return _db.ExecuteNonQuery(sql,
                // Trimmed, because both are displayed everywhere and Phone is UNIQUE.
                DbHelper.P("@FullName", fullName.Trim()),
                DbHelper.P("@Phone", phone.Trim()),         // an untrimmed number would slip past the UNIQUE index
                // Address keeps the user's own formatting, as it did at registration.
                DbHelper.P("@Address", address),
                DbHelper.P("@UserId", userId)) == 1;        // exactly one row, or the form reports failure
        }

        /// <summary>The current password is verified inside the same UPDATE.</summary>
        public bool ChangePassword(int userId, string currentPassword, string newPassword)
        {
            // The salt is per user, so it must be read before anything can be hashed.
            string currentSalt = _db.ExecuteScalarString(
                "SELECT PasswordSalt FROM Users WHERE UserId = @UserId;",   // the SALT only, never the stored hash
                DbHelper.P("@UserId", userId));                             // by primary key: one value or none

            // ExecuteScalarString returns "" for no rows, so an unknown id fails here.
            if (string.IsNullOrEmpty(currentSalt)) return false;   // no such user

            // What the stored hash SHOULD be; it is about to become a WHERE parameter.
            string currentHash = PasswordHelper.Hash(currentPassword, currentSalt);

            // A brand new salt, so work done against the old one no longer applies.
            string newSalt = PasswordHelper.CreateSalt();
            string newHash = PasswordHelper.Hash(newPassword, newSalt);   // the plain text is dropped here

            // Unlike Login, the UserId is known already, so the check CAN live in the WHERE.
            const string sql = @"
-- No SELECT anywhere near it: the verification is folded into the WHERE below.
UPDATE  Users
-- Both halves together: a new hash beside the old salt would lock the account out.
SET     PasswordHash = @NewHash, PasswordSalt = @NewSalt
-- Two conditions: the right account AND proof of the current password.
WHERE   UserId = @UserId AND PasswordHash = @OldHash;";

            // == 1 means success; 0 means the current password was wrong, not an error.
            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@NewHash", newHash),    // what the account verifies against from now on
                DbHelper.P("@NewSalt", newSalt),    // same SET, because a hash without its salt is unusable
                DbHelper.P("@UserId", userId),      // from UserSession, so it can only change its own
                // The HASH of the typed current password, never the typed text itself.
                DbHelper.P("@OldHash", currentHash)) == 1;
        }

        // ---- SUPER ADMIN: USER LIST ----

        /// <summary>Requirement 4. An empty box means "no filter", not "no rows".</summary>
        public DataTable SearchUsers(string keyword, string status, string userType)
        {
            // One query serves the unfiltered grid and every combination of the boxes.
            const string sql = @"
-- A display list, so no credential columns for the Super Admin either.
SELECT  u.UserId, u.FullName, u.Email, u.Phone, u.UserType, u.Status,
        -- ISNULL, so a customer shows a dash rather than a cell that looks unloaded.
        ISNULL(p.PharmacyName, '-') AS PharmacyName, u.CreatedAt
FROM    Users u                                                          -- every account is listed, shop or no shop
        -- LEFT again: an INNER JOIN here would quietly drop every customer.
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
-- Filtered in the QUERY, so no path on this screen can suspend the Super Admin.
WHERE   u.UserType <> 'SuperAdmin'
  -- An empty box means no filter, and the wildcards are added here, not in C#.
  AND   (@Keyword  = '' OR u.FullName LIKE '%' + @Keyword + '%' OR u.Email LIKE '%' + @Keyword + '%')
  -- Same shape for the status list, whose blank entry means 'any status'.
  AND   (@Status   = '' OR u.Status   = @Status)
  -- And for the role list, so the two can be combined or left alone freely.
  AND   (@UserType = '' OR u.UserType = @UserType)
-- Grouped by role first, so the owners and the customers read as two blocks.
ORDER BY u.UserType, u.FullName;";

            return _db.ExecuteTable(sql,   // a grid binds straight to the DataTable
                // ?? "" matters: a NULL parameter makes the test UNKNOWN and empties the grid.
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@Status", status ?? ""),       // the same NULL trap, so the same guard
                DbHelper.P("@UserType", userType ?? ""));  // and once more for the role filter
        }

        // Suspends or reactivates one account, and returns false when nothing was written.
        public bool SetUserStatus(int userId, string newStatus)
        {
            return _db.ExecuteNonQuery(   // rows affected, which is the evidence it landed
                // The SuperAdmin guard is in the WHERE, so such a call updates no rows.
                "UPDATE Users SET Status = @Status WHERE UserId = @UserId AND UserType <> 'SuperAdmin';",
                DbHelper.P("@Status", newStatus),   // untouched: CK_Users_Status is the validator
                // == 1 means one account changed; 0 means unknown id or the Super Admin.
                DbHelper.P("@UserId", userId)) == 1;
        }
    }
}
