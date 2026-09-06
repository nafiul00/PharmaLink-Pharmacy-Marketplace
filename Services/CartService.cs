using System.Data;
using PharmaLinkApp.Database;
using PharmaLinkApp.Models;

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
        private readonly DbHelper _db = new DbHelper();

        /// <summary>
        /// Adding a medicine that is already in the basket increases the
        /// quantity instead of creating a duplicate line. MERGE does both cases
        /// in one statement, which is what keeps the UNIQUE constraint on
        /// (CustomerId, MedicineId) from ever being violated.
        /// </summary>
        public bool AddOrIncrease(int customerId, int medicineId, int quantity, out string message)
        {
            int stock = _db.ExecuteScalarInt(
                "SELECT Stock FROM Medicines WHERE MedicineId = @Id AND IsActive = 1;",
                DbHelper.P("@Id", medicineId));

            int alreadyInCart = _db.ExecuteScalarInt(
                "SELECT ISNULL(Quantity, 0) FROM Cart WHERE CustomerId = @Cust AND MedicineId = @Med;",
                DbHelper.P("@Cust", customerId),
                DbHelper.P("@Med", medicineId));

            if (stock <= 0)
            {
                message = "This medicine is out of stock.";
                return false;
            }

            if (alreadyInCart + quantity > stock)
            {
                message = "Only " + stock + " unit(s) are available and you already have " +
                          alreadyInCart + " in your cart.";
                return false;
            }

            const string sql = @"
MERGE Cart AS target
USING (SELECT @CustomerId AS CustomerId, @MedicineId AS MedicineId, @Quantity AS Quantity) AS source
    ON  target.CustomerId = source.CustomerId
    AND target.MedicineId = source.MedicineId
WHEN MATCHED THEN
    UPDATE SET target.Quantity = target.Quantity + source.Quantity
WHEN NOT MATCHED THEN
    INSERT (CustomerId, MedicineId, Quantity)
    VALUES (source.CustomerId, source.MedicineId, source.Quantity);";

            _db.ExecuteNonQuery(sql,
                DbHelper.P("@CustomerId", customerId),
                DbHelper.P("@MedicineId", medicineId),
                DbHelper.P("@Quantity", quantity));

            message = "Added to cart.";
            return true;
        }

        /// <summary>Setting a quantity of zero or less removes the line.</summary>
        public bool SetQuantity(int customerId, int medicineId, int quantity, out string message)
        {
            if (quantity <= 0)
            {
                Remove(customerId, medicineId);
                message = "Line removed from the cart.";
                return true;
            }

            int stock = _db.ExecuteScalarInt(
                "SELECT Stock FROM Medicines WHERE MedicineId = @Id;",
                DbHelper.P("@Id", medicineId));

            if (quantity > stock)
            {
                message = "Only " + stock + " unit(s) are in stock.";
                return false;
            }

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
            return _db.ExecuteNonQuery(
                "DELETE FROM Cart WHERE CustomerId = @Cust AND MedicineId = @Med;",
                DbHelper.P("@Cust", customerId),
                DbHelper.P("@Med", medicineId)) == 1;
        }

        public void ClearAll(int customerId)
        {
            _db.ExecuteNonQuery("DELETE FROM Cart WHERE CustomerId = @Cust;",
                DbHelper.P("@Cust", customerId));
        }

        public int CountLines(int customerId)
        {
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
        m.UnitPrice                                                          AS ListPrice,
        ISNULL(d.Pct, 0)                                                     AS DiscountPercent,
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(10,2))     AS PriceYouPay,
        CAST(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(12,2)) AS LineTotal,
        m.Stock,
        m.RequiresRx
FROM    Cart ct
        INNER JOIN Medicines  m  ON m.MedicineId  = ct.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId
ORDER BY ph.PharmacyName, m.MedicineName;";

            return _db.ExecuteTable(sql, DbHelper.P("@CustomerId", customerId));
        }

        public List<CartLine> GetLines(int customerId)
        {
            List<CartLine> lines = new List<CartLine>();
            DataTable table = GetLinesTable(customerId);

            foreach (DataRow row in table.Rows)
            {
                lines.Add(new CartLine
                {
                    CartId = DbHelper.GetInt(row, "CartId"),
                    CustomerId = customerId,
                    MedicineId = DbHelper.GetInt(row, "MedicineId"),
                    MedicineName = DbHelper.GetString(row, "MedicineName"),
                    Strength = DbHelper.GetString(row, "Strength"),
                    PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                    PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                    Quantity = DbHelper.GetInt(row, "Quantity"),
                    ListPrice = DbHelper.GetDecimal(row, "ListPrice"),
                    DiscountPercent = DbHelper.GetDecimal(row, "DiscountPercent"),
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
        CAST(SUM(ct.Quantity * m.UnitPrice) AS DECIMAL(12,2))                        AS BeforeDiscount,
        CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)) AS ItemsTotal,
        @DeliveryCharge                                                              AS DeliveryCharge,
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
  AND   (@PharmacyId = 0 OR m.PharmacyId = @PharmacyId);",
                DbHelper.P("@Cust", customerId),
                DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        /// <summary>Sum of every line in the basket, discounts applied, before delivery.</summary>
        public decimal GetItemsTotal(int customerId)
        {
            return _db.ExecuteScalarDecimal(@"
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
