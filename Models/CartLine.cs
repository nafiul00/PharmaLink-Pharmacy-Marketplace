// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>One basket line, already carrying today's discount.</summary>
    public class CartLine
    {
        public int CartId { get; set; }                  // identity of the Cart row, so a grid row traces back to one
        public int CustomerId { get; set; }              // whose basket; GetLines fills it from the argument passed in
        public int MedicineId { get; set; }              // with CustomerId forms UQ_Cart_Line: one row per medicine
        public int Quantity { get; set; }                // CK_Cart_Qty keeps it above zero, so zero removes the line

        // The rest are joined in, so the whole grid comes from one query.
        public string MedicineName { get; set; } = "";   // what the customer is buying
        public string Strength { get; set; } = "";       // two strengths of a brand are two products at two prices
        public int PharmacyId { get; set; }              // the cart groups on this: two shops means two orders
        public string PharmacyName { get; set; } = "";   // the readable form, printed in the group header
        public decimal ListPrice { get; set; }           // shelf price, so the grid can strike it through
        public decimal DiscountPercent { get; set; }     // today's offer, or 0 when nothing is running
        public decimal PriceYouPay { get; set; }         // already discounted by SQL, so cart and invoice agree
        public int Stock { get; set; }                   // lets the grid warn when a quantity now exceeds supply
        public bool RequiresRx { get; set; }             // makes the checkout ask for a prescription photograph

        // A computed property: no setter, evaluated on every read, so it cannot go stale.
        public decimal LineTotal => decimal.Round(PriceYouPay * Quantity, 2);
    }
}
