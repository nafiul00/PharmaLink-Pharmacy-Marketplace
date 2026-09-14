// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>One person; UserType decides which dashboard opens.</summary>
    public class User
    {
        public int UserId { get; set; }                  // PK, copied into UserSession.UserId at login
        public string FullName { get; set; } = "";       // greeted on the dashboard, printed on the invoice
        public string Email { get; set; } = "";          // UQ_Users_Email, so login can match on this alone
        // Password not in the WHERE: the salt is per user, so read the row first.
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";   // a salt is not a secret; its job is to be unique per row
        public string Phone { get; set; } = "";          // UQ_Users_Phone, so one number cannot register twice
        public string Address { get; set; } = "";        // copied onto each order, so edits never touch past invoices
        public string UserType { get; set; } = "";       // CK_Users_Type: SuperAdmin | Admin | Customer
        public string Status { get; set; } = "";         // CK_Users_Status: Pending | Active | Suspended
        public DateTime CreatedAt { get; set; }          // database default, so signup time is the server's clock

        /// <summary>From the LEFT JOIN on Pharmacies; 0 when they own no shop.</summary>
        public int PharmacyId { get; set; }
        public string PharmacyName { get; set; } = "";   // carried with the id so the owner's header draws at once
    }
}
