using System.Data;                  // DataTable and DataRow, the shape every read comes back in
using Microsoft.Data.SqlClient;     // SqlConnection, SqlTransaction, SqlCommand: needed only by RegisterPharmacyOwner
using PharmaLinkApp.Database;       // DbHelper, the only class that knows the connection string
using PharmaLinkApp.Helpers;        // PasswordHelper: salt creation, hashing, verification
using PharmaLinkApp.Models;         // User and Pharmacy, the typed objects the rows become

namespace PharmaLinkApp.Services
{
    // -------------------------------------------------------------------------
    //  Layer: service.  Called by LoginForm, SignUpForm, MyProfileForm,
    //  CheckoutForm and SuperAdminManageUsersForm.
    //
    //  Every query goes through DbHelper except RegisterPharmacyOwner, which
    //  opens its own connection so the Users row and the Pharmacies row are
    //  written inside one transaction.
    //
    //  This is the only service that uses PasswordHelper. Login reads the row by
    //  email alone and verifies the hash in C#, because the salt is stored per
    //  user and cannot be applied before that row has been read.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Everything to do with getting into the system: login, registration,
    /// profile editing and password change.
    ///
    /// There is no separate administrator login. All three roles come through
    /// the same query, and the UserType it returns is what decides which
    /// dashboard opens.
    /// </summary>
    public class AuthService
    {
        // One helper per service instance, not one per method. DbHelper holds no open
        // connection of its own - each of its methods opens, runs and closes again - so
        // sharing this field across the methods below is safe, and it saves re-reading
        // the connection string out of App.config on every single call.
        private readonly DbHelper _db = new DbHelper();

        // ---------------------------------------------------------------------
        //  LOGIN
        // ---------------------------------------------------------------------

        /// <summary>
        /// Looks the account up by email, then verifies the typed password
        /// against the stored salt and hash in memory. Returns null when the
        /// email is unknown or the password is wrong.
        ///
        /// The LEFT JOIN on Pharmacies is what supplies PharmacyId for a
        /// pharmacy owner. It is NULL for a SuperAdmin and for a Customer,
        /// because neither of them owns a shop.
        /// </summary>
        public User Login(string email, string password, out string failureReason)
        {
            // Assigned on the very first line rather than at each exit. The compiler
            // refuses a method that leaves an out parameter unset on any path, and doing
            // it once here means a later early return cannot be the path that forgets.
            // The empty string, not null, so the form can display it without a guard.
            failureReason = "";     // out parameters must be assigned on every path

            // THE LOGIN QUERY. Note what is NOT here: the password.
            // The report's section 7.1 puts "AND u.PasswordHash = @PasswordHash" in this
            // WHERE clause. That only works if every account shares one salt, because
            // otherwise the hash cannot be computed until you know which row you are
            // looking at. Our salt is per user, so the row must be read FIRST and the
            // hash compared afterwards, in memory. Keeping the comparison out of SQL
            // also avoids leaking which half of the pair was wrong through query timing.
            //
            // The LEFT JOIN, not INNER, is what lets all three roles use one query:
            // a pharmacy owner gets PharmacyId and PharmacyStatus filled in from the
            // joined row, while a SuperAdmin or Customer matches no Pharmacies row and
            // simply gets NULL in those three columns.
            const string sql = @"
-- Everything the session will need, fetched in ONE round trip. A second query for the
-- pharmacy would have to be sent while the caller is still unauthenticated, and would
-- leave a window in which the two halves of the answer could disagree.
SELECT  u.UserId, u.FullName, u.Email, u.PasswordHash, u.PasswordSalt,   -- the salt and hash travel back so C# can do the comparison
        u.Phone, u.Address, u.UserType, u.Status, u.CreatedAt,           -- Status decides admission; UserType decides which dashboard opens
        p.PharmacyId, p.PharmacyName, p.Status AS PharmacyStatus         -- aliased because BOTH tables have a column called Status
FROM    Users u
        -- LEFT, so a Customer or SuperAdmin still returns their row with NULLs in the
        -- three pharmacy columns. OwnerId is UNIQUE in Pharmacies, so this join can add
        -- at most one row and the result can never be duplicated by it.
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
-- Email alone, and nothing else. UQ_Users_Email guarantees this matches either no rows
-- or exactly one, which is why the code below can take Rows[0] without looping.
WHERE   u.Email = @Email;";

            // Passed as a PARAMETER, never concatenated, so a typed apostrophe is data
            // and not SQL. Trim first: Email is UNIQUE and a trailing space would not match.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Email", email.Trim()));

            // Rows.Count, not a null test: ExecuteTable always hands back a DataTable and
            // returns an EMPTY one when nothing matched, so there is nothing to be null.
            if (table.Rows.Count == 0)
            {
                // No such email. In a bank this message would be deliberately vague to
                // avoid confirming which addresses are registered; for a course project
                // the clearer message is the more useful one.
                failureReason = "No account is registered with that email address.";
                return null;
            }

            DataRow row = table.Rows[0];      // Email is UNIQUE, so there can only be one

            // Read the two halves of the stored credential. The salt is what makes two
            // users who picked the same password end up with different hashes.
            string salt = DbHelper.GetString(row, "PasswordSalt");   // 12 random bytes as 16 Base64 characters
            string hash = DbHelper.GetString(row, "PasswordHash");   // Base64(SHA-256(salt + password)) as stored at registration

            // Verify recomputes Base64(SHA-256(salt + typed password)) and compares it to
            // the stored hash with an ordinal comparison. The plain password is never
            // stored, never logged, and never sent to SQL Server.
            // Identity is settled here, before anything else about the account is
            // looked at, so the account-state messages below can only ever be seen by
            // somebody who has already proved the password.
            if (!PasswordHelper.Verify(password, salt, hash))
            {
                failureReason = "That password is not correct.";
                return null;
            }

            // Password was right, so the person IS who they say. Whether they are allowed
            // in is a separate question, and it is asked only after identity is proven.
            // Status is constrained by CK_Users_Status to exactly these three values.
            string status = DbHelper.GetString(row, "Status");
            if (status == "Pending")
            {
                // A pharmacy owner who registered but whose DGDA licence the Super Admin
                // has not checked yet. Seed account imran@newlifepharmacy.com sits here.
                failureReason = "This account is still waiting for Super Admin approval.";
                return null;
            }
            if (status == "Suspended")
            {
                // Suspension is reversible and keeps the row, so every order this person
                // ever placed stays intact. Deleting the user would break those foreign
                // keys, which is why the Super Admin screen never offers a delete.
                failureReason = "This account has been suspended by the Super Admin.";
                return null;
            }

            // Only now, with identity proved and the account cleared, is the object the
            // rest of the application will carry around actually built.
            User user = new User
            {
                UserId = DbHelper.GetInt(row, "UserId"),
                FullName = DbHelper.GetString(row, "FullName"),
                Email = DbHelper.GetString(row, "Email"),
                // GetString turns DBNull into "" and GetInt turns it into 0, which is why
                // none of these assignments needs a null check of its own even though
                // Address is nullable and the three pharmacy columns are NULL for two of
                // the three roles.
                Phone = DbHelper.GetString(row, "Phone"),
                Address = DbHelper.GetString(row, "Address"),
                UserType = DbHelper.GetString(row, "UserType"),
                // Reuses the local already read above rather than reading the column a
                // second time, so the object cannot possibly disagree with the value the
                // two guards were just tested against.
                Status = status,
                CreatedAt = DbHelper.GetDate(row, "CreatedAt"),
                // 0 for a Customer or SuperAdmin, because the LEFT JOIN matched nothing.
                // Every Admin side query later filters on this number, so it is the single
                // value that keeps one shop's data out of another shop's screens.
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName")
                // PasswordHash and PasswordSalt are deliberately NOT copied onto this
                // object. They were needed for the comparison above and nothing past this
                // method has any use for them, so the credential stops here.
            };

            // A pharmacy that is suspended must not be able to trade even if the
            // owner's own account row somehow says Active.
            // "Admin" is this project's name for a pharmacy owner; the platform owner is
            // "SuperAdmin". The test is on the returned object rather than the row so it
            // reads the same way as every other check on a User elsewhere.
            if (user.UserType == "Admin")
            {
                // The shop's own status, from the joined row. CK_Pharmacies_Status allows
                // only Pending, Approved and Suspended, so these two tests plus the
                // fall-through cover every legal value.
                string pharmacyStatus = DbHelper.GetString(row, "PharmacyStatus");
                if (pharmacyStatus == "Pending")
                {
                    failureReason = "Your pharmacy registration has not been approved yet.";
                    return null;
                }
                if (pharmacyStatus == "Suspended")
                {
                    // Suspending the shop, not the person: the owner's Users row can stay
                    // Active while the business is stopped from selling, and approving the
                    // shop again restores trading without touching the account.
                    failureReason = "Your pharmacy has been suspended by the Super Admin.";
                    return null;
                }
            }

            // Reaching here means: email found, password verified, account allowed in,
            // and, for an owner, the shop allowed to trade. The caller stores this object
            // in UserSession and opens the dashboard that matches user.UserType.
            return user;
        }

        // ---------------------------------------------------------------------
        //  REGISTRATION
        // ---------------------------------------------------------------------

        public bool EmailExists(string email)
        {
            // A courtesy check so the sign-up form can say "that email is already
            // registered" in its own validation, instead of letting the INSERT fail on
            // UQ_Users_Email and surfacing a constraint error. The UNIQUE constraint is
            // still the real guarantee: this read and the later INSERT are two separate
            // round trips, so only the database can settle a genuine race.
            return _db.ExecuteScalarInt(
                // COUNT(*) rather than SELECT TOP 1, because a count always returns
                // exactly one row and one value, so there is no "no rows" case to handle.
                "SELECT COUNT(*) FROM Users WHERE Email = @Email;",
                // Trimmed to match exactly what RegisterCustomer will store, otherwise
                // "ali@x.com " would be reported as free and then collide on insert.
                DbHelper.P("@Email", email.Trim())) > 0;
        }

        public bool PhoneExists(string phone)
        {
            // Phone carries UQ_Users_Phone as well, so the same pre-flight applies: one
            // mobile number belongs to one account, which is what stops a person opening
            // several customer accounts against the same contact details.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Users WHERE Phone = @Phone;",
                DbHelper.P("@Phone", phone.Trim())) > 0;
        }

        public bool LicenseExists(string licenseNo)
        {
            // Pharmacies, not Users: the DGDA licence number is unique per shop
            // (UQ_Pharmacies_License), which is what stops one licence being used to
            // register two storefronts on the platform.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Pharmacies WHERE LicenseNo = @LicenseNo;",
                DbHelper.P("@LicenseNo", licenseNo.Trim())) > 0;
        }

        /// <summary>
        /// Registers a customer. A customer is created Active and can use the
        /// platform immediately.
        /// </summary>
        public int RegisterCustomer(User user, string password)
        {
            // Salt first, then hash, because the hash is computed FROM the salt. A fresh
            // salt per account is what makes two people who chose the same password end
            // up with two unrelated hashes in the table.
            string salt = PasswordHelper.CreateSalt();
            string hash = PasswordHelper.Hash(password, salt);

            const string sql = @"
-- 'Customer' and 'Active' are written as literals rather than parameters on purpose:
-- they are policy decided by this method, not input from the form, so there is no path
-- by which a filled-in field could turn a sign-up into an Admin account. CK_Users_Type
-- and CK_Users_Status would reject anything else in any case.
INSERT INTO Users (FullName, Email, PasswordHash, PasswordSalt, Phone, Address, UserType, Status)
VALUES (@FullName, @Email, @Hash, @Salt, @Phone, @Address, 'Customer', 'Active');
-- Second statement in the SAME batch, so the new id comes back on the same round trip
-- and cannot be confused with an id created by somebody else in between.
-- SCOPE_IDENTITY() is scoped to this INSERT; @@IDENTITY would return an id generated by
-- a trigger on another table. The CAST is because it returns NUMERIC(38,0), not INT.
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            // ExecuteScalarInt reads the single value the final SELECT produced, so the
            // caller gets the new UserId and can log the customer straight in.
            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@FullName", user.FullName.Trim()),
                DbHelper.P("@Email", user.Email.Trim()),
                // The hash and the salt, never the password itself. Nothing in this method
                // holds the plain text after the two lines above.
                DbHelper.P("@Hash", hash),
                DbHelper.P("@Salt", salt),
                DbHelper.P("@Phone", user.Phone.Trim()),
                // Address is NOT trimmed: it is free text where the typist's own line
                // breaks are meaningful, and unlike Email and Phone it carries no UNIQUE
                // constraint that stray spaces could defeat. DbHelper.P turns a null into
                // DBNull, which the nullable column accepts.
                DbHelper.P("@Address", user.Address));
        }

        /// <summary>
        /// Registers a pharmacy owner. The Users row and the Pharmacies row are
        /// written inside one transaction, both with Status 'Pending', so the
        /// application can never end up with an owner who has no shop or a shop
        /// that has no owner. Neither becomes usable until the Super Admin
        /// approves the registration.
        /// </summary>
        public int RegisterPharmacyOwner(User owner, Pharmacy pharmacy, string password)
        {
            // Hashing happens BEFORE the connection is opened. SHA-256 is fast, but doing
            // any avoidable work while a transaction is open holds locks for longer than
            // necessary, and there is nothing here that needs the database.
            string salt = PasswordHelper.CreateSalt();
            string hash = PasswordHelper.Hash(password, salt);

            // This method opens its own connection instead of calling DbHelper's helpers,
            // because the two INSERTs must share one transaction and DbHelper deliberately
            // opens and closes a connection per call. using guarantees the connection goes
            // back to the pool even if an exception is thrown halfway through.
            using (SqlConnection conn = _db.GetConnection())
            {
                // Explicit Open, unlike ExecuteTable where the data adapter opens and
                // closes for you. A transaction cannot be started on a closed connection.
                conn.Open();
                // Default isolation (ReadCommitted) is enough here: this method only
                // inserts and never re-reads a row it has to see unchanged, so the range
                // locks Serializable would take would cost concurrency for no benefit.
                using (SqlTransaction tx = conn.BeginTransaction())
                {
                    try
                    {
                        // Declared out here, not inside the using below, because the second
                        // INSERT needs it after that block has closed.
                        int newUserId;

                        const string insertUser = @"
-- 'Admin' is the pharmacy owner role. 'Pending' is the important part: the account is
-- created but cannot log in, because Login refuses a Pending status. Approval by the
-- Super Admin is what flips it, so no shop can start trading unchecked.
INSERT INTO Users (FullName, Email, PasswordHash, PasswordSalt, Phone, Address, UserType, Status)
VALUES (@FullName, @Email, @Hash, @Salt, @Phone, @Address, 'Admin', 'Pending');
-- The generated UserId is needed immediately as the Pharmacies.OwnerId below, so it is
-- selected back rather than re-read with a second query on Email.
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                        // The third constructor argument, tx, enlists this command in the
                        // open transaction. Omitting it throws, because a connection with a
                        // pending transaction will not run an unenlisted command.
                        using (SqlCommand cmd = new SqlCommand(insertUser, conn, tx))
                        {
                            // AddWithValue rather than DbHelper.P because these commands are
                            // built by hand here; the protection is identical, since the
                            // values still travel separately from the SQL text.
                            cmd.Parameters.AddWithValue("@FullName", owner.FullName.Trim());
                            cmd.Parameters.AddWithValue("@Email", owner.Email.Trim());
                            cmd.Parameters.AddWithValue("@Hash", hash);
                            cmd.Parameters.AddWithValue("@Salt", salt);
                            cmd.Parameters.AddWithValue("@Phone", owner.Phone.Trim());
                            // DBNull.Value, not a C# null: ADO.NET treats a null Value as
                            // "parameter not supplied" and throws rather than sending NULL.
                            // The (object) cast is what lets the two sides of ?? share a
                            // type, since string and DBNull have no conversion between them.
                            cmd.Parameters.AddWithValue("@Address", (object)owner.Address ?? DBNull.Value);
                            // ExecuteScalar hands back the first column of the first row as
                            // object; Convert.ToInt32 unboxes the CAST(... AS INT) above.
                            newUserId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        const string insertPharmacy = @"
-- The shop is created 'Pending' too, so even if the owner's account were activated by
-- some other route the pharmacy still could not trade until it is approved on its own.
-- CommissionRate is left out deliberately: DF_Pharmacies_Comm supplies the platform
-- default of 8 percent, and only the Super Admin may change it afterwards.
INSERT INTO Pharmacies (OwnerId, PharmacyName, LicenseNo, Area, Address, ContactPhone, LogoPath, Status)
VALUES (@OwnerId, @Name, @License, @Area, @Address, @Phone, @Logo, 'Pending');";

                        using (SqlCommand cmd = new SqlCommand(insertPharmacy, conn, tx))
                        {
                            // The id generated moments ago, which is what ties the two rows
                            // together. UQ_Pharmacies_Owner makes OwnerId unique, so this is
                            // also the constraint that enforces one shop per owner.
                            cmd.Parameters.AddWithValue("@OwnerId", newUserId);
                            cmd.Parameters.AddWithValue("@Name", pharmacy.PharmacyName.Trim());
                            // Trimmed because UQ_Pharmacies_License compares the stored text
                            // exactly: " DGDA-123" and "DGDA-123" would both be accepted as
                            // unique and the same licence would exist twice.
                            cmd.Parameters.AddWithValue("@License", pharmacy.LicenseNo.Trim());
                            // Area drives the customer's "pharmacies near me" filter, so a
                            // stray space would put the shop in a group of its own.
                            cmd.Parameters.AddWithValue("@Area", pharmacy.Area.Trim());
                            cmd.Parameters.AddWithValue("@Address", pharmacy.Address.Trim());
                            cmd.Parameters.AddWithValue("@Phone", pharmacy.ContactPhone.Trim());
                            // A logo is optional. IsNullOrWhiteSpace catches null, "" and a
                            // box the user only put spaces in, all of which should become a
                            // real NULL rather than an empty path the image loader would
                            // later try to open. The (object) cast on one branch is what
                            // gives the conditional a common type.
                            cmd.Parameters.AddWithValue("@Logo",
                                string.IsNullOrWhiteSpace(pharmacy.LogoPath) ? (object)DBNull.Value : pharmacy.LogoPath);
                            // ExecuteNonQuery, not ExecuteScalar: nothing needs the new
                            // PharmacyId back, because the owner is not logged in yet.
                            cmd.ExecuteNonQuery();
                        }

                        // Both rows become visible at the same instant. Until this line runs
                        // nothing is durable, so a failure in the second INSERT leaves no
                        // half-registered owner behind.
                        tx.Commit();
                        // The caller uses this only to confirm the registration was filed;
                        // the owner still cannot log in until the Super Admin approves.
                        return newUserId;
                    }
                    catch
                    {
                        // Undo the first INSERT if the second one failed, say on a
                        // duplicate licence number. Without this the Users table would keep
                        // an owner row with no shop attached to it.
                        tx.Rollback();
                        // Bare throw, not "throw ex": it rethrows the SAME exception with the
                        // original stack trace, so the form sees what actually went wrong.
                        // Because this path bypassed DbHelper, what comes out is a raw
                        // SqlException rather than a DataAccessException, which is why
                        // Program.cs carries a second SqlException branch.
                        throw;
                    }
                }
            }
        }

        // ---------------------------------------------------------------------
        //  PROFILE AND PASSWORD
        // ---------------------------------------------------------------------

        public User GetUser(int userId)
        {
            const string sql = @"
-- PasswordHash and PasswordSalt are NOT in this list, unlike the login query. No screen
-- that shows a profile has any use for them, so they never reach memory at all.
SELECT  u.UserId, u.FullName, u.Email, u.Phone, u.Address, u.UserType,
        u.Status, u.CreatedAt, p.PharmacyId, p.PharmacyName
FROM    Users u
        -- Same LEFT JOIN as the login query, so an owner's profile screen can show the
        -- shop name while a customer's simply shows nothing there.
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
-- By primary key this time, not by email: the caller already knows who it is asking
-- about, because the id came from UserSession.
WHERE   u.UserId = @UserId;";

            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@UserId", userId));
            // null, not an empty User: "no such account" and "an account with blank
            // fields" are different answers, and the caller must be able to tell them
            // apart rather than showing an empty profile for a deleted id.
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];      // UserId is the primary key, so at most one
            // Built inline and returned in one expression, because nothing has to be
            // decided between reading the row and handing it back.
            return new User
            {
                UserId = DbHelper.GetInt(row, "UserId"),
                FullName = DbHelper.GetString(row, "FullName"),
                Email = DbHelper.GetString(row, "Email"),
                Phone = DbHelper.GetString(row, "Phone"),
                Address = DbHelper.GetString(row, "Address"),
                UserType = DbHelper.GetString(row, "UserType"),
                Status = DbHelper.GetString(row, "Status"),
                CreatedAt = DbHelper.GetDate(row, "CreatedAt"),
                // 0 and "" for anyone who does not own a shop, because the join matched
                // no row and the GetX helpers turn DBNull into the type's empty value.
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName")
            };
        }

        /// <summary>Email is deliberately not editable: it is the login identifier.</summary>
        public bool UpdateProfile(int userId, string fullName, string phone, string address)
        {
            const string sql = @"
UPDATE  Users
-- Three columns and no more. Email is absent because it is what people log in with and
-- what UQ_Users_Email keys on; UserType and Status are absent because letting a profile
-- screen write them would let a customer promote or unsuspend themselves.
SET     FullName = @FullName, Phone = @Phone, Address = @Address
-- Keyed on the id held in UserSession, so one account can only ever edit itself.
WHERE   UserId = @UserId;";

            // == 1 is both the write and the proof it landed: ExecuteNonQuery returns the
            // rows affected, so anything other than one row means the id was wrong and the
            // form should say so rather than reporting a save that never happened.
            return _db.ExecuteNonQuery(sql,
                // Trimmed, because these two are displayed everywhere and Phone is UNIQUE.
                DbHelper.P("@FullName", fullName.Trim()),
                DbHelper.P("@Phone", phone.Trim()),
                // Address keeps the user's own formatting, as at registration.
                DbHelper.P("@Address", address),
                DbHelper.P("@UserId", userId)) == 1;
        }

        /// <summary>
        /// Changes a password. The current password is verified against the
        /// stored hash inside the same UPDATE, so a wrong entry simply updates
        /// no rows and the form reports failure without the old hash ever
        /// having been read into memory by the caller.
        /// </summary>
        public bool ChangePassword(int userId, string currentPassword, string newPassword)
        {
            // Read this user's CURRENT salt first. Without it the typed "current password"
            // cannot be hashed into anything comparable, because the salt is per user.
            string currentSalt = _db.ExecuteScalarString(
                "SELECT PasswordSalt FROM Users WHERE UserId = @UserId;",
                DbHelper.P("@UserId", userId));

            // ExecuteScalarString returns "" rather than null when the query found nothing,
            // so both are tested. An unknown id fails here instead of hashing against an
            // empty salt and producing a value that could never match anything anyway.
            if (string.IsNullOrEmpty(currentSalt)) return false;   // no such user

            // What the stored hash SHOULD be if the typed current password is correct.
            // Nothing is compared yet: this value is about to become a WHERE parameter.
            string currentHash = PasswordHelper.Hash(currentPassword, currentSalt);

            // A password change gets a brand new salt, not a reuse of the old one. If the
            // salt were kept, anyone who had seen the old hash could tell the password had
            // changed, and rainbow work done against that salt would still apply.
            string newSalt = PasswordHelper.CreateSalt();
            string newHash = PasswordHelper.Hash(newPassword, newSalt);

            // The check and the write are ONE statement. "AND PasswordHash = @OldHash"
            // means a wrong current password matches no row, so the UPDATE changes
            // nothing and ExecuteNonQuery returns 0. There is no window between reading
            // the old hash and writing the new one in which anything could change.
            const string sql = @"
UPDATE  Users
-- Both halves are replaced together. Writing the hash without its matching salt would
-- lock the account out permanently, because verification would then combine the new
-- hash with the old salt and could never agree.
SET     PasswordHash = @NewHash, PasswordSalt = @NewSalt
-- Two conditions: the right account AND proof of the current password. The second is
-- what makes this safe to call from a form that already has a session open - knowing
-- the UserId is not enough on its own.
WHERE   UserId = @UserId AND PasswordHash = @OldHash;";

            // == 1 is the whole result: exactly one row changed means success. 0 means the
            // current password was wrong, and the form turns that into a message.
            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@NewHash", newHash),
                DbHelper.P("@NewSalt", newSalt),
                DbHelper.P("@UserId", userId),
                // The hash of what the user typed as their current password, never the
                // typed text itself: the comparison happens on hashes at both ends.
                DbHelper.P("@OldHash", currentHash)) == 1;
        }

        // ---------------------------------------------------------------------
        //  SUPER ADMIN: USER LIST
        // ---------------------------------------------------------------------

        /// <summary>
        /// Requirement 4. Every Admin and Customer in one grid, with the shop
        /// name filled in beside an owner's row through a LEFT JOIN. An empty
        /// keyword or status means "no filter" rather than "no results".
        /// </summary>
        public DataTable SearchUsers(string keyword, string status, string userType)
        {
            const string sql = @"
SELECT  u.UserId, u.FullName, u.Email, u.Phone, u.UserType, u.Status,
        -- ISNULL so a customer's row shows a dash rather than an empty cell, which in a
        -- DataGridView is indistinguishable from a value that failed to load.
        ISNULL(p.PharmacyName, '-') AS PharmacyName, u.CreatedAt
FROM    Users u
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
-- The platform's own account is filtered out in the QUERY, not hidden by the grid, so
-- there is no code path on this screen that can suspend the Super Admin.
WHERE   u.UserType <> 'SuperAdmin'
  -- The optional filter pattern used across this project: an empty string means no
  -- filter at all and the OR short circuits the rest of the test, so one query serves
  -- the unfiltered grid and every combination of the three boxes. The wildcards are added
  -- HERE, around the parameter, rather than in C#, so the keyword itself stays data and
  -- a typed % cannot turn into a wildcard the user did not intend.
  AND   (@Keyword  = '' OR u.FullName LIKE '%' + @Keyword + '%' OR u.Email LIKE '%' + @Keyword + '%')
  AND   (@Status   = '' OR u.Status   = @Status)
  AND   (@UserType = '' OR u.UserType = @UserType)
-- Grouped by role first so the owners and the customers read as two blocks.
ORDER BY u.UserType, u.FullName;";

            return _db.ExecuteTable(sql,
                // ?? "" matters: a NULL parameter would make "@Keyword = ''" evaluate to
                // UNKNOWN rather than true, every row would fail the test, and an empty
                // filter box would silently return an empty grid.
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@Status", status ?? ""),
                DbHelper.P("@UserType", userType ?? ""));
        }

        public bool SetUserStatus(int userId, string newStatus)
        {
            return _db.ExecuteNonQuery(
                // The "AND UserType <> 'SuperAdmin'" guard lives in the WHERE rather than
                // in a C# if, so even a call made with the platform account's id simply
                // updates no rows. A disabled button on the form is a courtesy; this is
                // the rule. newStatus is not validated here either, because CK_Users_Status
                // already refuses anything outside Pending, Active and Suspended and
                // DbHelper translates that rejection into a readable sentence.
                "UPDATE Users SET Status = @Status WHERE UserId = @UserId AND UserType <> 'SuperAdmin';",
                DbHelper.P("@Status", newStatus),
                // == 1 means exactly one account changed. 0 means the id was unknown or it
                // was the Super Admin's, and either way nothing was written.
                DbHelper.P("@UserId", userId)) == 1;
        }
    }
}
