namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A row of the junction table that resolves the many to many relationship
    /// between Orders and Medicines. UnitPrice is the price on the day of the
    /// purchase, not today's price.
    /// </summary>
    public class OrderItem
    {
        public int OrderItemId { get; set; }
        public int OrderId { get; set; }
        public int MedicineId { get; set; }
        public int Quantity { get; set; }
        // The price PAID, frozen at checkout - deliberately stored rather than read from
        // Medicines. If an invoice read the live price, a pharmacy raising its prices
        // next month would silently rewrite every past bill. This is the single most
        // important reason OrderItems exists as a table rather than a list of ids.
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }

        public string MedicineName { get; set; } = "";
        public string Strength { get; set; } = "";
    }
}
