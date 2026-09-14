namespace PharmaLinkApp.Models
{
    /// <summary>
    /// One completed checkout, for one pharmacy. A basket that spans two
    /// pharmacies becomes two of these.
    /// CommissionAmount is frozen at checkout time and never recalculated.
    /// </summary>
    public class Order
    {
        // The primary key, and the invoice number the customer is shown. The identity
        // seed starts at 1001 in the database so the very first bill does not read
        // "Invoice 1".
        public int OrderId { get; set; }

        // Who placed it, a foreign key onto Users. Every customer side query filters on
        // this, which is what keeps one customer's order history private to them.
        public int CustomerId { get; set; }

        // Which shop is fulfilling it. One order belongs to exactly one pharmacy, which
        // is the reason a two shop basket has to become two orders: the delivery charge,
        // the commission and the confirming owner are all per shop.
        public int PharmacyId { get; set; }

        // When the checkout happened. Defaulted by the database rather than sent from the
        // application, so the timestamp is the server's and cannot be back dated from a
        // machine whose clock is wrong.
        public DateTime OrderDate { get; set; }

        // The sum of the lines, discounts already applied, before delivery. It is written
        // from the same SQL expression the cart uses to total the basket, which is why
        // the figure on the checkout screen and the figure in the order always match.
        public decimal ItemsTotal { get; set; }

        // The flat delivery fee for this shop's part of the basket, defaulted to 60.00 in
        // the database. It is a delivery rule rather than a property of the pharmacy,
        // which is why it is stored per order.
        public decimal DeliveryCharge { get; set; }

        // ItemsTotal + DeliveryCharge. This one is a PERSISTED computed column in SQL
        // Server, so the addition is done by the database and there is no way for the
        // parts and the total to disagree. The application only ever reads it.
        public decimal TotalAmount { get; set; }

        // What PharmaLink earns on this order. It is frozen here at checkout time from
        // the shop's commission rate on the day, rather than recalculated on demand, so
        // an owner negotiating a new rate next month cannot rewrite what was owed on last
        // month's sales. The earnings screens sum this column.
        public decimal CommissionAmount { get; set; }

        // Copied onto the order rather than read from the customer's profile, because the
        // address the parcel went to must stay what it was even if the customer later
        // edits their profile.
        public string DeliveryAddress { get; set; } = "";

        // How the customer chose to pay. CK_Orders_Payment restricts it to
        // CashOnDelivery, bKash, Nagad or Card, so a typo cannot reach the table.
        public string PaymentMethod { get; set; } = "";

        // Where the order has got to. CK_Orders_Status restricts it to Placed, Confirmed,
        // Delivered or Cancelled, which is the whole lifecycle in one column, and the
        // Delivered value is what unlocks the customer's right to leave a review.
        public string Status { get; set; } = "";

        // The six fields below come from joins, not from the Orders table. They are what
        // the printed invoice needs: who to bill, how to reach them, and the shop's name,
        // address and DGDA licence number, which a pharmacy bill has to carry.
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
