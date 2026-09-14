using System.Data;                  // DataTable and DataRow, the shape every read returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection
using PharmaLinkApp.Models;         // CartLine, the typed object a basket row becomes

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// The customer's live basket (requirement 24).
    ///
    /// Every price the cart shows is calculated by the query with today's offer
    /// already applied, so the cart, the medicine details screen and the invoice
    /// can never disagree about what an item costs.
    /// </summary>
    public class CartService
    {
        // One helper per service instance. It holds no connection of its own: every
        // call inside DbHelper opens a connection, runs, and closes it again, so this
        // field is safe to share across all the methods below.
        private readonly DbHelper _db = new DbHelper();

        /// <summary>
        /// Adding a medicine that is already in the basket increases the
        /// quantity instead of creating a duplicate line. MERGE does both cases
        /// in one statement, which is what keeps the UNIQUE constraint on
        /// (CustomerId, MedicineId) from ever being violated.
        /// </summary>
        public bool AddOrIncrease(int customerId, int medicineId, int quantity, out string message)
        {
            // FIRST READ: how many units the shop actually has. IsActive = 1 is part of
            // the WHERE rather than a separate check, so a delisted medicine returns 0
            // here and is refused below by the same test that catches genuine sell-outs.
            int stock = _db.ExecuteScalarInt(
                "SELECT Stock FROM Medicines WHERE MedicineId = @Id AND IsActive = 1;",
                DbHelper.P("@Id", medicineId));

            // SECOND READ: how many of this medicine the customer is already holding.
            // ISNULL(...,0) turns "no row at all" into 0, so the arithmetic below works
            // the same whether or not the line exists yet.
            int alreadyInCart = _db.ExecuteScalarInt(
                "SELECT ISNULL(Quantity, 0) FROM Cart WHERE CustomerId = @Cust AND MedicineId = @Med;",
                DbHelper.P("@Cust", customerId),
                DbHelper.P("@Med", medicineId));

            // Nothing on the shelf: refuse before writing anything.
            if (stock <= 0)
            {
                message = "This medicine is out of stock.";   // the caller shows this text
                return false;                                 // false means nothing was written
            }

            // The test is against what the basket would BECOME, not against the quantity
            // being added. Someone holding 3 of the last 5 units may add 2 more, not 5.
            if (alreadyInCart + quantity > stock)
            {
                // The message names both numbers so the customer can work out the gap
                // themselves instead of guessing what "not enough" means.
                message = "Only " + stock + " unit(s) are available and you already have " +
                          alreadyInCart + " in your cart.";
                return false;
            }

            const string sql = @"
-- MERGE does insert-or-update in ONE statement. Doing it as a SELECT followed by
-- either an INSERT or an UPDATE would leave a gap between the two in which another
-- request could insert the same line, and UQ_Cart_Line (CustomerId, MedicineId)
-- would then reject it. One statement means one atomic decision.
MERGE Cart AS target
-- The source is a one row inline table built from the parameters, because MERGE
-- requires a table expression to match against rather than bare values.
USING (SELECT @CustomerId AS CustomerId, @MedicineId AS MedicineId, @Quantity AS Quantity) AS source
    -- Match on exactly the columns UQ_Cart_Line makes unique, so 'already in the
    -- basket' means precisely what the constraint means.
    ON  target.CustomerId = source.CustomerId
    AND target.MedicineId = source.MedicineId
-- Already there: ADD to the quantity rather than replacing it, so pressing Add twice
-- with quantity 2 leaves 4, which is what the customer expects.
WHEN MATCHED THEN
    UPDATE SET target.Quantity = target.Quantity + source.Quantity
-- Not there yet: create the line.
WHEN NOT MATCHED THEN
    INSERT (CustomerId, MedicineId, Quantity)
    VALUES (source.CustomerId, source.MedicineId, source.Quantity);";

            // The three values travel as SqlParameters, never glued into the text above.
            // That is what makes the statement safe no matter what the customer typed.
            _db.ExecuteNonQuery(sql,
                DbHelper.P("@CustomerId", customerId),
                DbHelper.P("@MedicineId", medicineId),
                DbHelper.P("@Quantity", quantity));

            message = "Added to cart.";   // success text for the caller
            return true;                  // true means the basket changed
        }

        /// <summary>Setting a quantity of zero or less removes the line.</summary>
        public bool SetQuantity(int customerId, int medicineId, int quantity, out string message)
        {
            // Zero is not an error, it is the delete gesture. Treating it here means the
            // form's quantity box needs no separate Remove path when it is typed down to 0.
            if (quantity <= 0)
            {
                Remove(customerId, medicineId);        // reuse the delete, don't repeat the SQL
                message = "Line removed from the cart.";
                return true;                           // removing IS success, not failure
            }

            // Re-read the stock. The number the form last displayed may be minutes old and
            // another customer's checkout may have taken units off the shelf since then.
            int stock = _db.ExecuteScalarInt(
                "SELECT Stock FROM Medicines WHERE MedicineId = @Id;",
                DbHelper.P("@Id", medicineId));

            // Note this compares against the typed quantity alone, not a running total:
            // SetQuantity REPLACES the line quantity, it does not add to it.
            if (quantity > stock)
            {
                message = "Only " + stock + " unit(s) are in stock.";
                return false;
            }

            // Both key columns are in the WHERE. CustomerId is what stops one customer
            // editing another customer's basket line by guessing a MedicineId.
            _db.ExecuteNonQuery(
                "UPDATE Cart SET Quantity = @Qty WHERE CustomerId = @Cust AND MedicineId = @Med;",
                DbHelper.P("@Qty", quantity),
                DbHelper.P("@Cust", customerId),
                DbHelper.P("@Med", medicineId));

            message = "Quantity updated.";
            return true;
        }

        public bool Remove(int customerId, int medicineId)
        {
            // ExecuteNonQuery returns the number of rows affected, so "== 1" is both the
            // delete and the proof it hit exactly one line. A return of 0 means the line
            // was already gone, which the caller can treat as a harmless double click.
            return _db.ExecuteNonQuery(
                "DELETE FROM Cart WHERE CustomerId = @Cust AND MedicineId = @Med;",
                DbHelper.P("@Cust", customerId),
                DbHelper.P("@Med", medicineId)) == 1;
        }

        public void ClearAll(int customerId)
        {
            // Empties the whole basket in one statement. Called after a checkout that
            // covered every pharmacy in the cart; a partial checkout uses the targeted
            // DELETE inside OrderService instead, which carries a PharmacyId as well.
            _db.ExecuteNonQuery("DELETE FROM Cart WHERE CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));
        }

        public int CountLines(int customerId)
        {
            // Counts LINES, not units: two boxes of one medicine is one line. This is the
            // number painted on the cart badge in the customer's navigation bar.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Cart WHERE CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));
        }

        /// <summary>Every basket line, with today's discount applied by the query.</summary>
        public DataTable GetLinesTable(int customerId)
        {
            const string sql = @"
SELECT  ct.CartId,
        ct.MedicineId,
        m.MedicineName,
        m.Strength,
        ph.PharmacyId,
        ph.PharmacyName,
        ct.Quantity,
        -- The shelf price, kept alongside the discounted one so the grid can strike it through.
        m.UnitPrice                                                          AS ListPrice,
        -- 0 when no offer is running today, which makes the two CASTs below collapse to the list price.
        ISNULL(d.Pct, 0)                                                     AS DiscountPercent,
        -- Per unit price after the discount. 100.0 (not 100) forces decimal division;
        -- integer division would floor every percentage to 0 and silently sell at full price.
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(10,2))     AS PriceYouPay,
        -- The line total, computed here rather than in C# so the cart, the checkout and
        -- the invoice all inherit the same rounding from the same expression.
        CAST(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(12,2)) AS LineTotal,
        m.Stock,          -- lets the grid warn when a line now exceeds what is available
        m.RequiresRx      -- drives the prescription prompt at checkout
FROM    Cart ct
        INNER JOIN Medicines  m  ON m.MedicineId  = ct.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        -- OUTER APPLY, not a JOIN: it runs the subquery once PER medicine row and still
        -- returns the row when there is no offer. An INNER JOIN to Offers would silently
        -- drop every undiscounted medicine out of the basket.
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       -- MAX plus this date window means overlapping offers resolve to the
                       -- best one for the customer, and an expired offer cannot be picked.
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId
-- Grouped by shop so the customer sees at a glance that a two-pharmacy basket
-- is going to become two separate orders.
ORDER BY ph.PharmacyName, m.MedicineName;";

            return _db.ExecuteTable(sql, DbHelper.P("@CustomerId", customerId));
        }

        public List<CartLine> GetLines(int customerId)
        {
            // The typed version of the query above. The grid binds to the DataTable
            // directly; the checkout logic wants objects it can loop over and total up.
            List<CartLine> lines = new List<CartLine>();
            DataTable table = GetLinesTable(customerId);   // one round trip, reused below

            // Walk the rows once, turning each into a CartLine.
            foreach (DataRow row in table.Rows)
            {
                lines.Add(new CartLine
                {
                    CartId = DbHelper.GetInt(row, "CartId"),
                    // Taken from the ARGUMENT, not the row: the query filtered on it, so
                    // every row belongs to this customer and the column need not be selected.
                    CustomerId = customerId,
                    MedicineId = DbHelper.GetInt(row, "MedicineId"),
                    // The GetX helpers each translate DBNull into the type's empty value,
                    // which is why none of these assignments needs its own null check.
                    MedicineName = DbHelper.GetString(row, "MedicineName"),
                    Strength = DbHelper.GetString(row, "Strength"),
                    PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                    PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                    Quantity = DbHelper.GetInt(row, "Quantity"),
                    ListPrice = DbHelper.GetDecimal(row, "ListPrice"),
                    DiscountPercent = DbHelper.GetDecimal(row, "DiscountPercent"),
                    // Already discounted by the query. Nothing in C# recalculates a price.
                    PriceYouPay = DbHelper.GetDecimal(row, "PriceYouPay"),
                    Stock = DbHelper.GetInt(row, "Stock"),
                    RequiresRx = DbHelper.GetBool(row, "RequiresRx")
                });
            }
            return lines;
        }

        /// <summary>
        /// The basket grouped by pharmacy. This is exactly how many orders the
        /// checkout will create, because a cart that spans two pharmacies
        /// becomes two orders, each with its own delivery charge.
        /// </summary>
        public DataTable GetPharmacyGroups(int customerId, decimal deliveryCharge)
        {
            const string sql = @"
SELECT  ph.PharmacyId,
        ph.PharmacyName,
        COUNT(*)                                                                     AS Lines,
        SUM(ct.Quantity)                                                             AS Units,
        -- What the basket would cost with no offers, so the summary panel can show the saving.
        CAST(SUM(ct.Quantity * m.UnitPrice) AS DECIMAL(12,2))                        AS BeforeDiscount,
        -- What this shop's slice actually costs. The checkout writes exactly this number
        -- into Orders.ItemsTotal, which is why both use the same expression.
        CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)) AS ItemsTotal,
        -- Passed in rather than stored: the charge is a delivery rule, not a property of the shop.
        @DeliveryCharge                                                              AS DeliveryCharge,
        -- Carried so the caller can compute commission per shop without a second query.
        ph.CommissionRate
FROM    Cart ct
        INNER JOIN Medicines  m  ON m.MedicineId  = ct.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId
-- CommissionRate is in the GROUP BY only because it is SELECTed. It is functionally
-- dependent on PharmacyId, so grouping by it adds no rows.
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.CommissionRate
ORDER BY ph.PharmacyName;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@CustomerId", customerId),
                DbHelper.P("@DeliveryCharge", deliveryCharge));
        }

        /// <summary>True when the basket contains a medicine whose RequiresRx flag is set.</summary>
        public bool ContainsPrescriptionItem(int customerId, int pharmacyId)
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
WHERE   ct.CustomerId = @Cust
  AND   m.RequiresRx  = 1
  -- The optional filter pattern used across this project: passing 0 means 'every
  -- pharmacy', and the OR short circuits the second test. One query serves both
  -- the whole-basket check and the per-shop check at checkout.
  AND   (@PharmacyId = 0 OR m.PharmacyId = @PharmacyId);",
                DbHelper.P("@Cust", customerId),
                DbHelper.P("@PharmacyId", pharmacyId)) > 0;   // COUNT > 0, so 'any', not 'how many'
        }

        /// <summary>Sum of every line in the basket, discounts applied, before delivery.</summary>
        public decimal GetItemsTotal(int customerId)
        {
            return _db.ExecuteScalarDecimal(@"
-- SUM over no rows is NULL, not 0, so an empty basket would otherwise return NULL and
-- the decimal conversion on the C# side would throw. ISNULL makes empty mean zero.
SELECT  ISNULL(CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)), 0)
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));
        }

        /// <summary>How much the discounts are saving the customer right now.</summary>
        public decimal GetDiscountTotal(int customerId)
        {
            return _db.ExecuteScalarDecimal(@"
-- The mirror of GetItemsTotal: that one multiplies by (1 - pct), this one by pct alone,
-- so the two always add up to the undiscounted total and the summary panel balances.
SELECT  ISNULL(CAST(SUM(ct.Quantity * m.UnitPrice * (ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)), 0)
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));
        }
    }
}
