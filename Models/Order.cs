namespace PharmaLinkApp.Models
{
    /// <summary>
    /// One completed checkout, for one pharmacy. A basket that spans two
    /// pharmacies becomes two of these.
    /// CommissionAmount is frozen at checkout time and never recalculated.
    /// </summary>
    public class Order
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public int PharmacyId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal ItemsTotal { get; set; }
        public decimal DeliveryCharge { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal CommissionAmount { get; set; }
        public string DeliveryAddress { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public string Status { get; set; } = "";

        public string CustomerName { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string PharmacyName { get; set; } = "";
        public string PharmacyAddress { get; set; } = "";
        public string PharmacyLicense { get; set; } = "";
        // COMPOSITION: an Order HAS line items. This is the object-model counterpart of
        // the OrderItems junction table - the many-to-many between orders and medicines
        // resolved into a collection hanging off the parent.
        //
        // Initialised to an empty list at declaration, never left null, so callers can
        // foreach over it without a null check even for an order that failed to load
        // its lines. OrderService.GetOrderWithItems fills it with a second query and
        // hands back one fully assembled object, which is what lets InvoiceForm print
        // the whole bill from a single variable.
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}
