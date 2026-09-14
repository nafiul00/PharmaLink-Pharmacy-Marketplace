// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>One checkout, for one pharmacy. Two shops means two orders.</summary>
    public class Order
    {
        public int OrderId { get; set; }                    // PK and invoice number; identity seed starts at 1001
        public int CustomerId { get; set; }                 // FK onto Users; every customer query filters on it
        public int PharmacyId { get; set; }                 // one order, one shop: fee, commission and owner are per shop
        public DateTime OrderDate { get; set; }             // database default, so the clock is the server's
        public decimal ItemsTotal { get; set; }             // lines after discount, before delivery
        public decimal DeliveryCharge { get; set; }         // flat fee per shop, defaulted to 60.00 in SQL
        public decimal TotalAmount { get; set; }            // PERSISTED computed column; the app only reads it
        public decimal CommissionAmount { get; set; }       // frozen at checkout, so a new rate cannot rewrite old sales
        public string DeliveryAddress { get; set; } = "";   // copied here so editing the profile cannot move a past parcel
        public string PaymentMethod { get; set; } = "";     // CK_Orders_Payment: CashOnDelivery, bKash, Nagad or Card
        public string Status { get; set; } = "";            // CK_Orders_Status: Placed, Confirmed, Delivered or Cancelled

        // The five below come from joins; they are what the printed invoice needs.
        public string CustomerName { get; set; } = "";      // Users.FullName, the 'billed to' line
        public string CustomerPhone { get; set; } = "";     // the rider's contact number
        public string PharmacyName { get; set; } = "";      // the shop's trading name, the invoice header
        public string PharmacyAddress { get; set; } = "";   // shows where the order was dispensed
        public string PharmacyLicense { get; set; } = "";   // Pharmacies.LicenseNo; a pharmacy bill needs it

        // COMPOSITION: an Order HAS line items, never null, so callers can foreach safely.
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}
