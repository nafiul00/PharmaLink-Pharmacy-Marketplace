namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A row of the junction table that resolves the many to many relationship
    /// between Orders and Medicines. UnitPrice is the price on the day of the
    /// purchase, not today's price.
    /// </summary>
    public class OrderItem
    {
        // The primary key of the line itself. Orders and Medicines each have their own
        // key; this one exists because the junction row is a thing in its own right.
        public int OrderItemId { get; set; }

        // The parent order. The foreign key is declared ON DELETE CASCADE, so removing an
        // order takes its lines with it and cannot leave an item pointing at nothing.
        public int OrderId { get; set; }

        // Which medicine was bought. With OrderId it forms UQ_OrderItems_Line, so one
        // medicine appears at most once per order and a repeat is a larger quantity
        // rather than a second line.
        public int MedicineId { get; set; }

        // How many units. CK_OrderItems_Qty keeps it above zero, because an order line
        // for nothing is not a thing that should ever be stored.
        public int Quantity { get; set; }
        // The price PAID, frozen at checkout - deliberately stored rather than read from
        // Medicines. If an invoice read the live price, a pharmacy raising its prices
        // next month would silently rewrite every past bill. This is the single most
        // important reason OrderItems exists as a table rather than a list of ids.
        public decimal UnitPrice { get; set; }

        // Quantity * UnitPrice. Like Orders.TotalAmount this is a PERSISTED computed
        // column in SQL Server, so the multiplication happens once, in the database, and
        // the line total can never drift from the two numbers it is made of. The
        // application reads it and never writes it.
        public decimal Subtotal { get; set; }

        // Joined in from Medicines so the invoice can print a readable line rather than
        // a MedicineId. They describe what the product is called TODAY; the money
        // columns above are what was actually charged on the day, and that difference is
        // the point of the whole table.
        public string MedicineName { get; set; } = "";
        public string Strength { get; set; } = "";
    }
}
