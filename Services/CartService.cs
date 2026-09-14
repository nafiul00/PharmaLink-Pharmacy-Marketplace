using System.Data;                  // DataTable and DataRow, the shape every read returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection
using PharmaLinkApp.Models;         // CartLine, the typed object a basket row becomes

namespace PharmaLinkApp.Services   // all SQL lives here, never inside a Form
{
    /// <summary>The customer's live basket, priced with today's offer.</summary>
    public class CartService   // requirement 24
    {
        private readonly DbHelper _db = new DbHelper();   // opens and closes a connection per call

        /// <summary>Adds a medicine, or increases the quantity if already there.</summary>
        public bool AddOrIncrease(int customerId, int medicineId, int quantity, out string message)
        {
            // First read: the shelf count. IsActive = 1 makes a delisted medicine return 0.
            int stock = _db.ExecuteScalarInt(
                "SELECT Stock FROM Medicines WHERE MedicineId = @Id AND IsActive = 1;",   // one scalar comes back, so not a DataTable
                DbHelper.P("@Id", medicineId));   // the id travels as a parameter, never glued into the text

            // Second read: how many of this medicine the customer already holds.
            int alreadyInCart = _db.ExecuteScalarInt(
                "SELECT ISNULL(Quantity, 0) FROM Cart WHERE CustomerId = @Cust AND MedicineId = @Med;",   // ISNULL makes 'no row yet' read as zero
                DbHelper.P("@Cust", customerId),   // scopes the read to this customer's own basket
                DbHelper.P("@Med", medicineId));   // and to the single medicine being added

            // Nothing on the shelf: refuse before writing anything.
            if (stock <= 0)
            {
                message = "This medicine is out of stock.";   // the caller shows this text
                return false;                                 // false means nothing was written
            }

            // Tested against what the basket would BECOME, not against the amount added.
            if (alreadyInCart + quantity > stock)
            {
                // Naming both numbers lets the customer work out the gap themselves.
                message = "Only " + stock + " unit(s) are available and you already have " +
                          alreadyInCart + " in your cart.";   // second half of the sentence begun above
                return false;   // nothing was written, so the caller must not say 'added'
            }

            // const, so the compiler embeds the text once and nobody can reassign it.
            const string sql = @"
MERGE Cart AS target   -- insert-or-update in ONE statement, so there is no gap to race in
-- MERGE needs a table expression to match against, so parameters become a row.
USING (SELECT @CustomerId AS CustomerId, @MedicineId AS MedicineId, @Quantity AS Quantity) AS source
    ON  target.CustomerId = source.CustomerId   -- first of the two UQ_Cart_Line columns
    AND target.MedicineId = source.MedicineId   -- the pair is what 'already in the basket' means
WHEN MATCHED THEN   -- the line already exists
    UPDATE SET target.Quantity = target.Quantity + source.Quantity   -- ADD, never SET
WHEN NOT MATCHED THEN   -- no line yet, so create one
    INSERT (CustomerId, MedicineId, Quantity)   -- columns listed, not left to table order
    -- The values come from the matched source row.
    VALUES (source.CustomerId, source.MedicineId, source.Quantity);";

            // The three values travel as SqlParameters, never glued into the text above.
            _db.ExecuteNonQuery(sql,
                DbHelper.P("@CustomerId", customerId),   // whose basket the line belongs to
                DbHelper.P("@MedicineId", medicineId),   // which medicine the line is for
                DbHelper.P("@Quantity", quantity));   // how many to ADD, read as an increment

            message = "Added to cart.";   // success text for the caller
            return true;                  // true means the basket changed
        }

        /// <summary>Setting a quantity of zero or less removes the line.</summary>
        public bool SetQuantity(int customerId, int medicineId, int quantity, out string message)
        {
            // Zero is not an error, it is the delete gesture typed into the quantity box.
            if (quantity <= 0)
            {
                Remove(customerId, medicineId);        // reuse the delete, don't repeat the SQL
                message = "Line removed from the cart.";   // past tense: it has already happened
                return true;                           // removing IS success, not failure
            }

            // Re-read the stock: another customer's checkout may have taken units since.
            int stock = _db.ExecuteScalarInt(
                "SELECT Stock FROM Medicines WHERE MedicineId = @Id;",   // no IsActive test: the line is already in the basket
                DbHelper.P("@Id", medicineId));   // the medicine whose line is being resized

            // Compared against the typed quantity alone, because this REPLACES the line.
            if (quantity > stock)
            {
                message = "Only " + stock + " unit(s) are in stock.";   // names the number so a fitting figure can be retyped
                return false;   // refused, and the stored quantity is left untouched
            }

            // CustomerId in the WHERE is what stops one customer editing another's line.
            _db.ExecuteNonQuery(
                "UPDATE Cart SET Quantity = @Qty WHERE CustomerId = @Cust AND MedicineId = @Med;",   // SET, not an increment
                DbHelper.P("@Qty", quantity),   // the new quantity, already proved to fit the shelf
                DbHelper.P("@Cust", customerId),   // half of the composite key, and the ownership check
                DbHelper.P("@Med", medicineId));   // the other half; together they address one row

            message = "Quantity updated.";   // shown by the caller once it has rebound the grid
            return true;   // true, so the caller knows the basket really changed
        }

        // Removes one basket line outright; an abandoned line has no history.
        public bool Remove(int customerId, int medicineId)
        {
            // ExecuteNonQuery returns rows affected, so "== 1" is the delete and its proof.
            return _db.ExecuteNonQuery(
                "DELETE FROM Cart WHERE CustomerId = @Cust AND MedicineId = @Med;",   // a hard DELETE, not a soft one
                DbHelper.P("@Cust", customerId),   // ownership, so nobody deletes another's line
                DbHelper.P("@Med", medicineId)) == 1;   // turns the affected-row count into yes or no
        }

        // The whole-basket delete, used after a checkout covering every shop.
        public void ClearAll(int customerId)
        {
            // A partial checkout uses the targeted DELETE in OrderService instead.
            _db.ExecuteNonQuery("DELETE FROM Cart WHERE CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));   // every line belonging to this customer
        }

        // The number painted on the cart badge, returned as an int for the navigation bar.
        public int CountLines(int customerId)
        {
            // Counts LINES, not units: two boxes of one medicine is still one line.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Cart WHERE CustomerId = @Cust;",   // COUNT(*) is the cheapest way to get this
                DbHelper.P("@Cust", customerId));   // one customer only, which makes the badge personal
        }

        /// <summary>Every basket line, with today's discount applied.</summary>
        public DataTable GetLinesTable(int customerId)
        {
            // One const shared with the typed GetLines below, so they cannot disagree.
            const string sql = @"
SELECT  ct.CartId,          -- the basket line's own key, so the grid can identify a row
        ct.MedicineId,      -- what the quantity box and the Remove button send back
        m.MedicineName,     -- read from Medicines, so a rename shows in every basket
        m.Strength,         -- 250mg against 500mg, or two products look like one row
        ph.PharmacyId,      -- drives the per-shop grouping at checkout
        ph.PharmacyName,    -- so a two-shop basket announces two separate orders
        ct.Quantity,        -- the only column the customer edits
        -- The shelf price, kept so the grid can strike it through.
        m.UnitPrice                                                          AS ListPrice,
        -- 0 when no offer runs today, which collapses both CASTs to the list price.
        ISNULL(d.Pct, 0)                                                     AS DiscountPercent,
        -- 100.0 forces decimal division; 100 would floor every percentage to zero.
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(10,2))     AS PriceYouPay,
        -- Computed here so cart, checkout and invoice inherit the same rounding.
        CAST(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(12,2)) AS LineTotal,
        m.Stock,          -- lets the grid warn when a line now exceeds what is available
        m.RequiresRx      -- drives the prescription prompt at checkout
FROM    Cart ct   -- Cart drives it: the question is 'what is in this basket'
        INNER JOIN Medicines  m  ON m.MedicineId  = ct.MedicineId   -- INNER: a nameless line is worse than none
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId   -- the shop the medicine belongs to
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct   -- APPLY still returns rows with no offer
                     FROM   Offers o   -- read only in here, so it cannot multiply basket rows
                     WHERE  o.MedicineId = m.MedicineId   -- correlated, so it runs once per line
                       AND  o.IsActive   = 1   -- a withdrawn offer keeps its row but flips this
                       -- MAX plus this window picks the best offer that is live today.
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId   -- the isolation rule: one customer's basket only
-- Grouped by shop, so a two-pharmacy basket visibly becomes two orders.
ORDER BY ph.PharmacyName, m.MedicineName;";

            return _db.ExecuteTable(sql, DbHelper.P("@CustomerId", customerId));   // one round trip; binds straight to the grid
        }

        // The typed twin of GetLinesTable: checkout wants objects, not rows.
        public List<CartLine> GetLines(int customerId)
        {
            List<CartLine> lines = new List<CartLine>();   // the list this method fills and returns
            DataTable table = GetLinesTable(customerId);   // one round trip, reused below

            // Walk the rows once, turning each into a CartLine.
            foreach (DataRow row in table.Rows)
            {
                lines.Add(new CartLine   // an object initialiser, so each property sits by its column
                {
                    CartId = DbHelper.GetInt(row, "CartId"),   // the key the grid sends back
                    CustomerId = customerId,   // from the argument: the query already filtered on it
                    MedicineId = DbHelper.GetInt(row, "MedicineId"),   // the medicine each row is about
                    MedicineName = DbHelper.GetString(row, "MedicineName"),   // GetString turns DBNull into ""
                    Strength = DbHelper.GetString(row, "Strength"),   // 250mg against 500mg
                    PharmacyId = DbHelper.GetInt(row, "PharmacyId"),   // carried for the per-shop grouping
                    PharmacyName = DbHelper.GetString(row, "PharmacyName"),   // the shop name shown beside the medicine
                    Quantity = DbHelper.GetInt(row, "Quantity"),   // the one field the customer can edit
                    ListPrice = DbHelper.GetDecimal(row, "ListPrice"),   // kept so the grid can strike it through
                    DiscountPercent = DbHelper.GetDecimal(row, "DiscountPercent"),   // 0 when no offer applies
                    PriceYouPay = DbHelper.GetDecimal(row, "PriceYouPay"),   // already discounted by the query
                    Stock = DbHelper.GetInt(row, "Stock"),   // live stock, so the grid can warn
                    RequiresRx = DbHelper.GetBool(row, "RequiresRx")   // drives the prescription prompt
                });
            }
            return lines;   // one CartLine per basket row, in query order
        }

        /// <summary>The basket grouped by pharmacy - one group per order.</summary>
        public DataTable GetPharmacyGroups(int customerId, decimal deliveryCharge)
        {
            // SQL, not a C# GroupBy, so the totals use the expression the checkout stores.
            const string sql = @"
SELECT  ph.PharmacyId,      -- becomes Orders.PharmacyId when this group turns into an order
        ph.PharmacyName,    -- the heading of each group on the checkout screen
        -- COUNT(*) counts basket LINES, since GROUP BY has narrowed rows to one shop.
        COUNT(*)                                                                     AS Lines,
        -- Units is the sum of the quantities, the other number checked before paying.
        SUM(ct.Quantity)                                                             AS Units,
        -- What the basket would cost with no offers, so the panel can show the saving.
        CAST(SUM(ct.Quantity * m.UnitPrice) AS DECIMAL(12,2))                        AS BeforeDiscount,
        -- The checkout writes exactly this number into Orders.ItemsTotal.
        CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)) AS ItemsTotal,
        -- Passed in, because the charge is a delivery rule and not a shop property.
        @DeliveryCharge                                                              AS DeliveryCharge,
        -- Carried so the caller gets commission per shop without a second query.
        ph.CommissionRate
FROM    Cart ct   -- the same three tables as the line query, so the two cannot disagree
        INNER JOIN Medicines  m  ON m.MedicineId  = ct.MedicineId   -- supplies UnitPrice
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId   -- supplies name and commission rate
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct   -- repeated deliberately, to match line prices
                     FROM   Offers o   -- inside the APPLY, so two promotions cannot double a row
                     WHERE  o.MedicineId = m.MedicineId   -- each line priced against its own offers
                       AND  o.IsActive   = 1   -- keeps a withdrawn promotion out of the total
                       -- An offer that ended yesterday cannot lower a total paid today.
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId   -- the same scope as the line query, so the money agrees
-- CommissionRate is grouped only because it is SELECTed; it adds no rows.
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.CommissionRate
-- Alphabetical, so the grouped panel and the line grid list shops in one order.
ORDER BY ph.PharmacyName;";

            return _db.ExecuteTable(sql,   // the same helper, this time with two parameters
                DbHelper.P("@CustomerId", customerId),   // whose basket is being grouped
                DbHelper.P("@DeliveryCharge", deliveryCharge));   // a rule passed in, not a column read out
        }

        /// <summary>True when any basket line needs a prescription.</summary>
        public bool ContainsPrescriptionItem(int customerId, int pharmacyId)
        {
            // A COUNT compared to zero, because the caller only wants a yes or a no.
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)   -- counting avoids shipping the rows themselves back
FROM    Cart ct   -- Cart drives it, so the question stays 'what is in this basket'
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId   -- reaches RequiresRx
WHERE   ct.CustomerId = @Cust   -- without this, another basket could answer the question
  AND   m.RequiresRx  = 1   -- the flag set on products that may not be sold over the counter
  -- Passing 0 means 'every pharmacy', so one query serves both callers.
  AND   (@PharmacyId = 0 OR m.PharmacyId = @PharmacyId);",
                DbHelper.P("@Cust", customerId),   // the customer whose basket is being checked
                DbHelper.P("@PharmacyId", pharmacyId)) > 0;   // COUNT > 0, so 'any', not 'how many'
        }

        /// <summary>Basket total with discounts applied, before delivery.</summary>
        public decimal GetItemsTotal(int customerId)
        {
            // ExecuteScalarDecimal, because one number comes back and not a grid of them.
            return _db.ExecuteScalarDecimal(@"
-- SUM over no rows is NULL, so ISNULL makes an empty basket mean zero.
SELECT  ISNULL(CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)), 0)
FROM    Cart ct   -- an empty basket produces no rows, which the ISNULL above turns into 0
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId   -- Cart stores only the quantity
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct   -- the same offer lookup as every method here
                     FROM   Offers o   -- read inside the APPLY, never joined in the FROM
                     WHERE  o.MedicineId = m.MedicineId   -- correlated per medicine
                       AND  o.IsActive   = 1   -- so the total matches what checkout will charge
                       -- Judged by the server, so a stale client clock cannot change a price.
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
-- One customer's basket, the same scope as every other read in this class.
WHERE   ct.CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));   // one customer, one number, one round trip
        }

        /// <summary>How much the discounts are saving the customer right now.</summary>
        public decimal GetDiscountTotal(int customerId)
        {
            // A separate query, not a C# subtraction, so rounding cannot unbalance the panel.
            return _db.ExecuteScalarDecimal(@"
-- The mirror of GetItemsTotal: that uses (1 - pct), this one uses pct.
SELECT  ISNULL(CAST(SUM(ct.Quantity * m.UnitPrice * (ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)), 0)
FROM    Cart ct   -- identical to GetItemsTotal, so both cover the same set of lines
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId   -- the saving is a percentage of list price
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct   -- one shared expression is why the numbers reconcile
                     FROM   Offers o   -- confined to the APPLY, so two live offers give one row
                     WHERE  o.MedicineId = m.MedicineId   -- each line discounted by its own best offer
                       AND  o.IsActive   = 1   -- no saving the customer will not actually receive
                       -- An expired promotion must not inflate the saving shown on screen.
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
-- One customer, so this plus GetItemsTotal is the undiscounted total.
WHERE   ct.CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));   // the same scope as the total above
        }
    }
}
