// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>One pharmacy; a UNIQUE OwnerId makes it one shop per owner.</summary>
    public class Pharmacy
    {
        public int PharmacyId { get; set; }              // PK, and the boundary of everything one owner may see
        public int OwnerId { get; set; }                 // UQ_Pharmacies_Owner is what makes the link one to one
        public string PharmacyName { get; set; } = "";   // trading name shown to customers and on the invoice
        public string LicenseNo { get; set; } = "";      // DGDA licence, unique, and printed on every bill
        public string Area { get; set; } = "";           // kept apart from Address because customers filter on it
        public string Address { get; set; } = "";        // full street address, free text, printed not filtered
        public string ContactPhone { get; set; } = "";   // the shop's number, separate from the owner's own
        public string LogoPath { get; set; } = "";       // a path, not the image, so the database stays small
        public decimal CommissionRate { get; set; }      // CK_Pharmacies_Comm caps it at 30; the column defaults to 8.00
        public string Status { get; set; } = "";         // CK_Pharmacies_Status: Pending | Approved | Suspended
        public DateTime RegisteredAt { get; set; }       // database default, so signup time is the server's clock
        public string OwnerName { get; set; } = "";      // joined from Users so approvals can name the person

        /// <summary>Polymorphism: a ComboBox shows the shop name, not the type.</summary>
        public override string ToString() => PharmacyName;
    }
}
