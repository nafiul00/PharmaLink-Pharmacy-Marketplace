namespace PharmaLinkApp.Models
{
    /// <summary>
    /// One line of the customer's live basket, already carrying whatever
    /// discount is running today so the cart and the checkout can never
    /// disagree about the price.
    /// </summary>
    public class CartLine
    {
        // The identity column of the Cart row this object came from. It is carried so a
        // grid row can be traced back to exactly one basket row, even though the update
        // and delete methods on CartService address a line by customer and medicine.
        public int CartId { get; set; }

        // Who the basket belongs to. CartService.GetLines fills this from the argument it
        // was passed rather than from the row, because the query already filtered on it.
        public int CustomerId { get; set; }

        // Which medicine the line holds. Together with CustomerId this is the pair that
        // UQ_Cart_Line makes unique, which is what stops the same medicine appearing
        // twice in one basket instead of having its quantity increased.
        public int MedicineId { get; set; }

        // How many units are in the basket. CK_Cart_Qty keeps this above zero in the
        // database, which is why setting a quantity of zero removes the line rather than
        // storing it.
        public int Quantity { get; set; }

        // The display fields below are not Cart columns at all. They are copied in by the
        // joins in CartService.GetLinesTable so the whole cart grid can be drawn from one
        // query instead of one extra lookup per row.
        public string MedicineName { get; set; } = "";

        // Dosage, such as 500mg. Shown beside the name because two strengths of the same
        // brand are two different products with two different prices.
        public string Strength { get; set; } = "";

        // Which shop the line comes from. The cart is grouped by this, because a basket
        // that spans two pharmacies becomes two separate orders at checkout.
        public int PharmacyId { get; set; }
        public string PharmacyName { get; set; } = "";

        // The shelf price before any offer, kept alongside the discounted one so the grid
        // can strike it through and show what the customer is saving.
        public decimal ListPrice { get; set; }

        // Today's winning offer as a percentage, or 0 when no offer is running, in which
        // case ListPrice and PriceYouPay are the same number.
        public decimal DiscountPercent { get; set; }

        // The per unit price actually charged. It arrives already discounted from the SQL
        // query rather than being worked out here, so the cart, the checkout and the
        // invoice all inherit the same arithmetic and the same rounding.
        public decimal PriceYouPay { get; set; }

        // What the shop currently holds, so the grid can warn about a line whose quantity
        // now exceeds what is available.
        public int Stock { get; set; }

        // Copied from Medicines.RequiresRx. This is what makes the checkout ask for a
        // prescription photograph before the order can be confirmed.
        public bool RequiresRx { get; set; }

        // A COMPUTED PROPERTY: derived, never stored, and with no setter. The expression
        // is evaluated fresh on every read, so the total can never fall out of step with
        // Quantity or PriceYouPay. Rounded to two decimal places because money is both
        // displayed and stored to two. The cart grid binds the LineTotal column the query
        // already returns; this is the same figure for any code that is holding the typed
        // object rather than the DataTable.
        public decimal LineTotal => decimal.Round(PriceYouPay * Quantity, 2);
    }
}
