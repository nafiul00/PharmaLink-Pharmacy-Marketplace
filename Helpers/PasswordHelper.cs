using System.Security.Cryptography;
using System.Text;

namespace PharmaLinkApp.Helpers
{
    /// <summary>
    /// Passwords are never stored or logged in plain text.
    ///
    /// Every user gets a random salt of their own, and what goes into the
    /// database is Base64( SHA-256( salt + password ) ).  Two people who pick
    /// the same password therefore end up with two completely different hashes,
    /// so one leaked hash tells an attacker nothing about the other account.
    ///
    /// The seed accounts in PharmaLinkDB_Setup.sql were hashed with exactly this
    /// method, which is why they log in without any extra setup.
    /// </summary>
    public static class PasswordHelper
    {
        /// <summary>Creates a fresh 12 byte random salt, Base64 encoded (16 characters).</summary>
        public static string CreateSalt()
        {
            byte[] bytes = new byte[12];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Hashes a password with the given salt.</summary>
        public static string Hash(string password, string salt)
        {
            if (password == null) password = string.Empty;
            if (salt == null) salt = string.Empty;

            byte[] input = Encoding.UTF8.GetBytes(salt + password);
            byte[] hash = SHA256.HashData(input);
            return Convert.ToBase64String(hash);
        }

        /// <summary>True when the typed password produces the stored hash.</summary>
        public static bool Verify(string password, string salt, string storedHash)
        {
            string computed = Hash(password, salt);
            return string.Equals(computed, storedHash, StringComparison.Ordinal);
        }
    }
}
