// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>Junction row between Orders and Medicines, priced that day.</summary>
    public class OrderItem
    {
        public int OrderItemId { get; set; }   // PK; the junction row is a thing in its own right
        public int OrderId { get; set; }       // parent order, ON DELETE CASCADE so no line is orphaned
        public int MedicineId { get; set; }    // with OrderId forms UQ_OrderItems_Line: one row per medicine
        public int Quantity { get; set; }      // CK_OrderItems_Qty keeps it above zero
        // The price PAID, frozen at checkout; the live price would rewrite old bills.
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }  // PERSISTED computed column, so it cannot drift from the two parts

        // Joined in from Medicines so the invoice prints a name rather than an id.
        public string MedicineName { get; set; } = "";
        public string Strength { get; set; } = "";   // 500mg and 665mg are different products, so the bill says which
    }
}
