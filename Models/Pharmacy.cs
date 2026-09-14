namespace PharmaLinkApp.Models
{
    /// <summary>
    /// One pharmacy. OwnerId is UNIQUE in the database, which is what enforces
    /// the rule that one pharmacy owner owns exactly one pharmacy.
    /// </summary>
    public class Pharmacy
    {
        // The primary key, and the value that ends up in UserSession.PharmacyId at login.
        // Medicines, Orders and Offers are all reached through it, so this single number
        // is the boundary of everything one owner is allowed to see.
        public int PharmacyId { get; set; }

        // The Users row that owns the shop. UQ_Pharmacies_Owner makes it unique, and that
        // constraint alone is what turns the relationship into one to one: without it an
        // owner could be linked to a second shop and PharmacyId would stop being a single
        // answer at login.
        public int OwnerId { get; set; }

        // The trading name shown to customers and printed on the invoice.
        public string PharmacyName { get; set; } = "";

        // The DGDA licence, for example DGDA-DH-10021. UQ_Pharmacies_License makes it
        // unique, so one licence cannot be registered by two shops, and the invoice
        // prints it because a pharmacy bill has to carry it.
        public string LicenseNo { get; set; } = "";

        // The neighbourhood, held separately from the full address because customers
        // filter the marketplace by area and a free text address cannot be filtered on.
        public string Area { get; set; } = "";
        public string Address { get; set; } = "";
        public string ContactPhone { get; set; } = "";

        // A path to the shop's logo file rather than the image itself, on the same
        // reasoning as Medicine.ImagePath: the database stays small and the picture can
        // be replaced on disk.
        public string LogoPath { get; set; } = "";

        // The platform's cut, as a percentage, set per shop by the Super Admin.
        // CK_Pharmacies_Comm caps it at 30 and the column defaults to 8.00, which is the
        // same ceiling Validator.IsCommissionRate applies in the form. The rate is copied
        // into each order's CommissionAmount at checkout, so changing it here affects
        // future orders only.
        public decimal CommissionRate { get; set; }

        public string Status { get; set; } = "";        // Pending | Approved | Suspended
        // The three values above are enforced by CK_Pharmacies_Status, and the column
        // defaults to Pending: a newly registered shop is invisible to customers until
        // the Super Admin approves it, which is the platform's gatekeeping in one field.

        // When the shop signed up. Defaulted by the database to the server's clock.
        public DateTime RegisteredAt { get; set; }

        // Joined in from Users so an approvals screen can name the person behind the
        // shop without a second query.
        public string OwnerName { get; set; } = "";
        // RUN TIME POLYMORPHISM - one of only two overrides in the whole project.
        // Every object inherits ToString() from System.Object, which returns the type
        // name; overriding it means a ComboBox or ListBox bound to Pharmacy objects
        // displays the shop name instead of "PharmaLinkApp.Models.Pharmacy".
        // The caller still just calls ToString() - the runtime picks THIS version
        // because the object really is a Pharmacy. That is the polymorphism.
        public override string ToString() => PharmacyName;
    }
}
