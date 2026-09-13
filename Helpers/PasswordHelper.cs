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
            byte[] bytes = new byte[12];      // 12 bytes -> 16 Base64 characters
            // RandomNumberGenerator, NOT System.Random. Random is a predictable
            // pseudo-random sequence seeded from the clock, so two accounts created in
            // the same moment could receive the same salt. This is the cryptographic
            // generator, which is the whole point of a salt being unguessable.
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);          // fill the array with random bytes
            }
            // Base64 so the salt is safe to store in an NVARCHAR column - raw bytes
            // would need a binary column and careful encoding on every read.
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Hashes a password with the given salt.</summary>
        public static string Hash(string password, string salt)
        {
            // Null guards so a missing field cannot throw here; an empty password will
            // simply hash to something that matches nothing.
            if (password == null) password = string.Empty;
            if (salt == null) salt = string.Empty;

            // salt + password, in THAT order, and the order must never change: the nine
            // seed accounts in PharmaLinkDB_Setup.sql were hashed exactly this way, so
            // swapping the operands would lock every one of them out.
            // UTF8 is fixed explicitly rather than left to the machine's default, so the
            // same password produces the same bytes on any computer.
            byte[] input = Encoding.UTF8.GetBytes(salt + password);

            // SHA-256 is one way: the hash can be recomputed from the password but the
            // password cannot be recovered from the hash. That is why a stolen database
            // does not hand over anyone's password, and why "forgot password" would have
            // to RESET rather than email the old one.
            byte[] hash = SHA256.HashData(input);

            return Convert.ToBase64String(hash);   // text, so it fits NVARCHAR(200)
        }

        /// <summary>True when the typed password produces the stored hash.</summary>
        public static bool Verify(string password, string salt, string storedHash)
        {
            string computed = Hash(password, salt);
            return string.Equals(computed, storedHash, StringComparison.Ordinal);
        }
    }
}
