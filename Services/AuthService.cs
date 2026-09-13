using System.Data;
using Microsoft.Data.SqlClient;
using PharmaLinkApp.Database;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;

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
SELECT  u.UserId, u.FullName, u.Email, u.PasswordHash, u.PasswordSalt,
        u.Phone, u.Address, u.UserType, u.Status, u.CreatedAt,
        p.PharmacyId, p.PharmacyName, p.Status AS PharmacyStatus
FROM    Users u
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
WHERE   u.Email = @Email;";

            // Passed as a PARAMETER, never concatenated, so a typed apostrophe is data
            // and not SQL. Trim first: Email is UNIQUE and a trailing space would not match.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Email", email.Trim()));

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
            string salt = DbHelper.GetString(row, "PasswordSalt");
            string hash = DbHelper.GetString(row, "PasswordHash");

            // Verify recomputes Base64(SHA-256(salt + typed password)) and compares it to
            // the stored hash with an ordinal comparison. The plain password is never
            // stored, never logged, and never sent to SQL Server.
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
                failureReason = "This account has been suspended by the Super Admin.";
                return null;
            }

            User user = new User
            {
                UserId = DbHelper.GetInt(row, "UserId"),
                FullName = DbHelper.GetString(row, "FullName"),
                Email = DbHelper.GetString(row, "Email"),
                Phone = DbHelper.GetString(row, "Phone"),
                Address = DbHelper.GetString(row, "Address"),
                UserType = DbHelper.GetString(row, "UserType"),
                Status = status,
                CreatedAt = DbHelper.GetDate(row, "CreatedAt"),
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName")
            };

            // A pharmacy that is suspended must not be able to trade even if the
            // owner's own account row somehow says Active.
            if (user.UserType == "Admin")
            {
                string pharmacyStatus = DbHelper.GetString(row, "PharmacyStatus");
                if (pharmacyStatus == "Pending")
                {
                    failureReason = "Your pharmacy registration has not been approved yet.";
                    return null;
                }
                if (pharmacyStatus == "Suspended")
                {
                    failureReason = "Your pharmacy has been suspended by the Super Admin.";
                    return null;
                }
            }

            return user;
        }

        // ---------------------------------------------------------------------
        //  REGISTRATION
        // ---------------------------------------------------------------------

        public bool EmailExists(string email)
        {
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Users WHERE Email = @Email;",
                DbHelper.P("@Email", email.Trim())) > 0;
        }

        public bool PhoneExists(string phone)
        {
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Users WHERE Phone = @Phone;",
                DbHelper.P("@Phone", phone.Trim())) > 0;
        }

        public bool LicenseExists(string licenseNo)
        {
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
            string salt = PasswordHelper.CreateSalt();
            string hash = PasswordHelper.Hash(password, salt);

            const string sql = @"
INSERT INTO Users (FullName, Email, PasswordHash, PasswordSalt, Phone, Address, UserType, Status)
VALUES (@FullName, @Email, @Hash, @Salt, @Phone, @Address, 'Customer', 'Active');
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@FullName", user.FullName.Trim()),
                DbHelper.P("@Email", user.Email.Trim()),
                DbHelper.P("@Hash", hash),
                DbHelper.P("@Salt", salt),
                DbHelper.P("@Phone", user.Phone.Trim()),
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
            string salt = PasswordHelper.CreateSalt();
            string hash = PasswordHelper.Hash(password, salt);

            using (SqlConnection conn = _db.GetConnection())
            {
                conn.Open();
                using (SqlTransaction tx = conn.BeginTransaction())
                {
                    try
                    {
                        int newUserId;

                        const string insertUser = @"
INSERT INTO Users (FullName, Email, PasswordHash, PasswordSalt, Phone, Address, UserType, Status)
VALUES (@FullName, @Email, @Hash, @Salt, @Phone, @Address, 'Admin', 'Pending');
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                        using (SqlCommand cmd = new SqlCommand(insertUser, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@FullName", owner.FullName.Trim());
                            cmd.Parameters.AddWithValue("@Email", owner.Email.Trim());
                            cmd.Parameters.AddWithValue("@Hash", hash);
                            cmd.Parameters.AddWithValue("@Salt", salt);
                            cmd.Parameters.AddWithValue("@Phone", owner.Phone.Trim());
                            cmd.Parameters.AddWithValue("@Address", (object)owner.Address ?? DBNull.Value);
                            newUserId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        const string insertPharmacy = @"
INSERT INTO Pharmacies (OwnerId, PharmacyName, LicenseNo, Area, Address, ContactPhone, LogoPath, Status)
VALUES (@OwnerId, @Name, @License, @Area, @Address, @Phone, @Logo, 'Pending');";

                        using (SqlCommand cmd = new SqlCommand(insertPharmacy, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@OwnerId", newUserId);
                            cmd.Parameters.AddWithValue("@Name", pharmacy.PharmacyName.Trim());
                            cmd.Parameters.AddWithValue("@License", pharmacy.LicenseNo.Trim());
                            cmd.Parameters.AddWithValue("@Area", pharmacy.Area.Trim());
                            cmd.Parameters.AddWithValue("@Address", pharmacy.Address.Trim());
                            cmd.Parameters.AddWithValue("@Phone", pharmacy.ContactPhone.Trim());
                            cmd.Parameters.AddWithValue("@Logo",
                                string.IsNullOrWhiteSpace(pharmacy.LogoPath) ? (object)DBNull.Value : pharmacy.LogoPath);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                        return newUserId;
                    }
                    catch
                    {
                        tx.Rollback();
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
SELECT  u.UserId, u.FullName, u.Email, u.Phone, u.Address, u.UserType,
        u.Status, u.CreatedAt, p.PharmacyId, p.PharmacyName
FROM    Users u
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
WHERE   u.UserId = @UserId;";

            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@UserId", userId));
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];
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
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName")
            };
        }

        /// <summary>Email is deliberately not editable: it is the login identifier.</summary>
        public bool UpdateProfile(int userId, string fullName, string phone, string address)
        {
            const string sql = @"
UPDATE  Users
SET     FullName = @FullName, Phone = @Phone, Address = @Address
WHERE   UserId = @UserId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@FullName", fullName.Trim()),
                DbHelper.P("@Phone", phone.Trim()),
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

            if (string.IsNullOrEmpty(currentSalt)) return false;   // no such user

            // What the stored hash SHOULD be if the typed current password is correct.
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
SET     PasswordHash = @NewHash, PasswordSalt = @NewSalt
WHERE   UserId = @UserId AND PasswordHash = @OldHash;";

            // == 1 is the whole result: exactly one row changed means success. 0 means the
            // current password was wrong, and the form turns that into a message.
            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@NewHash", newHash),
                DbHelper.P("@NewSalt", newSalt),
                DbHelper.P("@UserId", userId),
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
        ISNULL(p.PharmacyName, '-') AS PharmacyName, u.CreatedAt
FROM    Users u
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
WHERE   u.UserType <> 'SuperAdmin'
  AND   (@Keyword  = '' OR u.FullName LIKE '%' + @Keyword + '%' OR u.Email LIKE '%' + @Keyword + '%')
  AND   (@Status   = '' OR u.Status   = @Status)
  AND   (@UserType = '' OR u.UserType = @UserType)
ORDER BY u.UserType, u.FullName;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@Status", status ?? ""),
                DbHelper.P("@UserType", userType ?? ""));
        }

        public bool SetUserStatus(int userId, string newStatus)
        {
            return _db.ExecuteNonQuery(
                "UPDATE Users SET Status = @Status WHERE UserId = @UserId AND UserType <> 'SuperAdmin';",
                DbHelper.P("@Status", newStatus),
                DbHelper.P("@UserId", userId)) == 1;
        }
    }
}
