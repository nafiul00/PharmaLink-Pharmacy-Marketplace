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
    // A STATIC class: it holds no state of its own, so there is nothing to construct and
    // nothing to keep between calls. Every method is a pure function of its arguments,
    // which is exactly what a hashing helper should be - the same password and the same
    // salt must give the same answer today, tomorrow, and on any machine.
    public static class PasswordHelper
    {
        /// <summary>Creates a fresh 12 byte random salt, Base64 encoded (16 characters).</summary>
        public static string CreateSalt()
        {
            // THIS RUNS ONCE PER USER, at registration, and the result is stored in that
            // person's own Users.PasswordSalt column. It is deliberately not one shared
            // constant in the source. A single application-wide salt would give every
            // account the same hash for the same password, so a leaked table would show
            // at a glance which users share a password, and one precomputed table of
            // common passwords would crack all of them together. A salt per user means
            // an attacker has to start again for every single row.
            //
            // 12 bytes is ample: the salt only has to be unique and unguessable, not
            // secret, which is why it can sit in the same table as the hash it protects.
            byte[] bytes = new byte[12];      // 12 bytes -> 16 Base64 characters
            // RandomNumberGenerator, NOT System.Random. Random is a predictable
            // pseudo-random sequence seeded from the clock, so two accounts created in
            // the same moment could receive the same salt. This is the cryptographic
            // generator, which is the whole point of a salt being unguessable.
            // using(...) rather than a bare variable: the generator holds an operating
            // system handle, and the using block guarantees it is released the moment the
            // salt has been filled in, even if something throws inside.
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
            // Re-hash what the user typed with the salt that came out of THEIR row, then
            // compare the two pieces of text. Hashing is one way, so this is the only way
            // to check a password: recompute and compare, never decrypt.
            string computed = Hash(password, salt);

            // THIS COMPARISON HAPPENS IN MEMORY, NOT IN A SQL WHERE CLAUSE, and that is a
            // direct consequence of the salt being per user. To put "AND PasswordHash =
            // @Hash" in the login query you would have to know the hash before the query
            // runs, and the hash cannot be computed until the salt is known, and the salt
            // is stored in the very row the query is trying to find. So AuthService.Login
            // reads the row by email alone and calls this method afterwards. Keeping the
            // comparison out of SQL also means the password never travels to the database
            // server in any form, and a failed login costs the same work whichever half
            // of the pair was wrong.
            //
            // StringComparison.Ordinal compares the raw characters, with no culture rules
            // and no case folding. That matters because Base64 is case sensitive: a
            // culture-aware or case-insensitive comparison could treat two different
            // hashes as equal and let the wrong password through.
            return string.Equals(computed, storedHash, StringComparison.Ordinal);
        }
    }
}
