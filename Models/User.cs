namespace PharmaLinkApp.Models
{
    /// <summary>
    /// One person on the platform. UserType is what the login query reads to
    /// decide which of the three dashboards opens.
    /// </summary>
    public class User
    {
        // The primary key, and the value copied into UserSession.UserId at login. Cart,
        // Orders, Reviews and Prescriptions all carry it, so it is the thread that ties
        // a person to everything they have done.
        public int UserId { get; set; }

        // The display name, greeted on the dashboard and printed on the invoice.
        public string FullName { get; set; } = "";

        // The login identifier. UQ_Users_Email makes it unique, which is what allows the
        // login query to look an account up by email alone and expect at most one row,
        // and CK_Users_Email requires it to look like an address.
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

        // The contact number. UQ_Users_Phone makes it unique as well, so one number
        // cannot register two accounts; its eleven digit shape is checked by
        // Validator.IsMobile in the form rather than by a constraint.
        public string Phone { get; set; } = "";

        // Where a customer's parcels go. It is copied onto each order at checkout, so
        // editing it later changes future deliveries and not past invoices.
        public string Address { get; set; } = "";

        public string UserType { get; set; } = "";      // SuperAdmin | Admin | Customer
        // CK_Users_Type allows only those three values. There is no separate
        // administrator login: all three roles come through the same query, and this one
        // column is what decides which dashboard opens afterwards.

        public string Status { get; set; } = "";        // Pending | Active | Suspended
        // CK_Users_Status allows only those three, and the column defaults to Active.
        // Suspending an account is a change of this value rather than a deletion, so the
        // person's orders and reviews stay intact and the block can be lifted.

        // Created by the database default, so the signup date is the server's clock.
        public DateTime CreatedAt { get; set; }

        /// <summary>Filled by the LEFT JOIN on Pharmacies; 0 when the user does not own a shop.</summary>
        // This is the value that becomes UserSession.PharmacyId, so it is where a
        // pharmacy owner's data isolation begins. It is 0 for a SuperAdmin and for a
        // Customer, because the LEFT JOIN finds no Pharmacies row for either of them.
        public int PharmacyId { get; set; }
        public string PharmacyName { get; set; } = "";
    }
}
