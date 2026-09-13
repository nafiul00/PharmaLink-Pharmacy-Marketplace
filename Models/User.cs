namespace PharmaLinkApp.Models
{
    /// <summary>
    /// One person on the platform. UserType is what the login query reads to
    /// decide which of the three dashboards opens.
    /// </summary>
    public class User
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        // The two halves of the stored credential, never the password itself.
        // PasswordHash is Base64(SHA-256(PasswordSalt + password)); PasswordSalt is a
        // fresh 12 byte random value per user, which is why two people who choose the
        // same password still have completely different hashes - and why the hash
        // cannot be computed until this row has been read, so login matches on Email
        // alone and verifies in C#.
        //
        // These are populated only by AuthService.Login. GetUser deliberately leaves
        // them empty, because no screen ever needs them.
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public string UserType { get; set; } = "";      // SuperAdmin | Admin | Customer
        public string Status { get; set; } = "";        // Pending | Active | Suspended
        public DateTime CreatedAt { get; set; }

        /// <summary>Filled by the LEFT JOIN on Pharmacies; 0 when the user does not own a shop.</summary>
        public int PharmacyId { get; set; }
        public string PharmacyName { get; set; } = "";
    }
}
