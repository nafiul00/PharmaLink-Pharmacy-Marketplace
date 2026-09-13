namespace PharmaLinkApp.Models
{
    /// <summary>
    /// One pharmacy. OwnerId is UNIQUE in the database, which is what enforces
    /// the rule that one pharmacy owner owns exactly one pharmacy.
    /// </summary>
    public class Pharmacy
    {
        public int PharmacyId { get; set; }
        public int OwnerId { get; set; }
        public string PharmacyName { get; set; } = "";
        public string LicenseNo { get; set; } = "";
        public string Area { get; set; } = "";
        public string Address { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public decimal CommissionRate { get; set; }
        public string Status { get; set; } = "";        // Pending | Approved | Suspended
        public DateTime RegisteredAt { get; set; }

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
