using System.Security.Cryptography;   // RandomNumberGenerator and SHA256, the two primitives used here
using System.Text;                    // Encoding, so the password becomes bytes the same way everywhere

// Helpers: small stateless classes, no database and no UI, callable anywhere.
namespace PharmaLinkApp.Helpers
{
    /// <summary>Stores Base64(SHA-256(salt + password)), never plain text.</summary>
    public static class PasswordHelper   // static: no state, so a salt and password always hash alike
    {
        /// <summary>Creates a fresh 12 byte random salt, Base64 encoded.</summary>
        public static string CreateSalt()
        {
            // One salt per user; a shared one would make equal passwords hash alike.
            byte[] bytes = new byte[12];      // 12 bytes -> 16 Base64 characters
            // Crypto generator, not Random; using() frees the OS handle even if this throws.
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);          // fill the array with random bytes
            }
            // Base64 so the salt fits an NVARCHAR column instead of needing a binary one.
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Hashes a password with the given salt.</summary>
        public static string Hash(string password, string salt)
        {
            // Null guards, so a missing field hashes harmlessly instead of throwing.
            if (password == null) password = string.Empty;
            if (salt == null) salt = string.Empty;         // a null salt would throw on the concatenation below
            // salt + password in THAT order, and UTF8 fixed: the seed accounts were hashed so.
            byte[] input = Encoding.UTF8.GetBytes(salt + password);

            // SHA-256 is one way, so a stolen database hands over nobody's actual password.
            byte[] hash = SHA256.HashData(input);

            return Convert.ToBase64String(hash);   // text, so it fits NVARCHAR(200)
        }

        /// <summary>True when the typed password produces the stored hash.</summary>
        public static bool Verify(string password, string salt, string storedHash)
        {
            // Re-hash what was typed with THEIR salt; hashing is one way, so never decrypt.
            string computed = Hash(password, salt);

            // Compared in memory, not in SQL: the salt lives in the row the query must find.
            return string.Equals(computed, storedHash, StringComparison.Ordinal);   // Ordinal: Base64 is case sensitive
        }
    }
}
