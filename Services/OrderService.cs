using System.Data;
using Microsoft.Data.SqlClient;
using PharmaLinkApp.Database;
using PharmaLinkApp.Models;

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// Checkout, order history, invoices and order status.
    ///
    /// Checkout is the most important piece of the system. Five things have to
    /// happen together - the order header, one line per cart row, the stock
    /// reduction, the cart clean up and the frozen commission - and if any one
    /// of them failed on its own the database would be left with an order that
    /// has no items, or stock that was sold twice. Wrapping them in a single
    /// transaction is what makes the checkout safe.
    /// </summary>
    public class OrderService
    {
        private readonly DbHelper _db = new DbHelper();

        // =====================================================================
        //  CHECKOUT  (requirement 25)
        // =====================================================================

        /// <summary>
        /// Places one order for one pharmacy's slice of the basket. A cart that
        /// spans two pharmacies is checked out by calling this once per
        /// pharmacy, which is why every statement below carries @PharmacyId.
        /// Returns the new OrderId, or 0 when the order could not be placed.
        /// </summary>
        public int Checkout(int customerId, int pharmacyId, string deliveryAddress,
                            string paymentMethod, decimal deliveryCharge, out string message)
        {
            using (SqlConnection conn = _db.GetConnection())
            {
                conn.Open();
                using (SqlTransaction tx = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        // ---- 1. re-check the stock inside the transaction ----
                        // Two customers can reach the checkout for the last unit
                        // of a medicine at the same time. Checking again here,
                        // rather than trusting what the cart screen showed, is
                        // what stops the same unit being sold twice.
                        const string stockCheck = @"
SELECT TOP 1 m.MedicineName
FROM   Cart ct
       INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
WHERE  ct.CustomerId = @CustomerId
  AND  m.PharmacyId  = @PharmacyId
  AND  (ct.Quantity > m.Stock OR m.IsActive = 0);";

                        string problem;
                        using (SqlCommand cmd = new SqlCommand(stockCheck, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@CustomerId", customerId);
                            cmd.Parameters.AddWithValue("@PharmacyId", pharmacyId);
                            object result = cmd.ExecuteScalar();
                            problem = result == null || result == DBNull.Value ? null : result.ToString();
                        }

                        if (problem != null)
                        {
                            tx.Rollback();
                            message = "'" + problem + "' is no longer available in the quantity you asked for. " +
                                      "Please update your cart and try again.";
                            return 0;
                        }

                        // ---- 2. the order header, commission frozen at today's rate ----
                        const string placeOrder = @"
DECLARE @Total DECIMAL(12,2), @CommRate DECIMAL(5,2), @NewOrderId INT;

SELECT  @Total = CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2))
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId AND o.IsActive = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

SELECT  @CommRate = CommissionRate FROM Pharmacies WHERE PharmacyId = @PharmacyId;

INSERT INTO Orders (CustomerId, PharmacyId, ItemsTotal, DeliveryCharge, CommissionAmount,
                    DeliveryAddress, PaymentMethod, Status)
VALUES (@CustomerId, @PharmacyId, @Total, @DeliveryCharge,
        CAST(@Total * @CommRate / 100.0 AS DECIMAL(12,2)),
        @DeliveryAddress, @PaymentMethod, 'Placed');

SET @NewOrderId = SCOPE_IDENTITY();

-- one line per cart row, at the discounted price the customer actually saw
INSERT INTO OrderItems (OrderId, MedicineId, Quantity, UnitPrice)
SELECT  @NewOrderId, ct.MedicineId, ct.Quantity,
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(10,2))
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId AND o.IsActive = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

-- take the stock off the shelf
UPDATE  m SET m.Stock = m.Stock - ct.Quantity
FROM    Medicines m INNER JOIN Cart ct ON ct.MedicineId = m.MedicineId
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

-- clear only this pharmacy's lines; the rest of the basket becomes the next order
DELETE  ct
FROM    Cart ct INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

SELECT CAST(@NewOrderId AS INT);";

                        int newOrderId;
                        using (SqlCommand cmd = new SqlCommand(placeOrder, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@CustomerId", customerId);
                            cmd.Parameters.AddWithValue("@PharmacyId", pharmacyId);
                            cmd.Parameters.AddWithValue("@DeliveryCharge", deliveryCharge);
                            cmd.Parameters.AddWithValue("@DeliveryAddress", deliveryAddress);
                            cmd.Parameters.AddWithValue("@PaymentMethod", paymentMethod);
                            newOrderId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        tx.Commit();
                        message = "Order placed.";
                        return newOrderId;
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { /* connection already gone */ }
                        message = "The order could not be placed: " + ex.Message;
                        return 0;
                    }
                }
            }
        }

        // =====================================================================
        //  ORDER HISTORY  (requirement 27)
        // =====================================================================

        /// <summary>
        /// The customer's own orders, with the number of line items, the invoice
        /// total from the computed TotalAmount column, and a CanReview flag that
        /// the Rate and Review button binds to.
        /// </summary>
        public DataTable GetHistoryForCustomer(int customerId, string status, int pharmacyId,
                                               DateTime fromDate, DateTime toDate)
        {
            const string sql = @"
SELECT  o.OrderId, o.OrderDate, ph.PharmacyName,
        COUNT(oi.OrderItemId)      AS Items,
        o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,
        o.PaymentMethod, o.Status,
        CASE WHEN o.Status = 'Delivered'
              AND EXISTS (SELECT 1 FROM OrderItems x
                          WHERE x.OrderId = o.OrderId
                            AND NOT EXISTS (SELECT 1 FROM Reviews r
                                            WHERE r.OrderId = o.OrderId
                                              AND r.MedicineId = x.MedicineId))
             THEN 1 ELSE 0 END     AS CanReview
FROM    Orders o
        INNER JOIN Pharmacies ph ON ph.PharmacyId = o.PharmacyId
        INNER JOIN OrderItems oi ON oi.OrderId    = o.OrderId
WHERE   o.CustomerId = @CustomerId
  AND   (@Status     = '' OR o.Status = @Status)
  AND   (@PharmacyId = 0  OR o.PharmacyId = @PharmacyId)
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate
GROUP BY o.OrderId, o.OrderDate, ph.PharmacyName, o.ItemsTotal, o.DeliveryCharge,
         o.TotalAmount, o.PaymentMethod, o.Status
ORDER BY o.OrderDate DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@CustomerId", customerId),
                DbHelper.P("@Status", status ?? ""),
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@FromDate", fromDate.Date),
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)));
        }

        /// <summary>Everything the printable invoice needs, in one object.</summary>
        public Order GetOrderWithItems(int orderId)
        {
            const string header = @"
SELECT  o.OrderId, o.CustomerId, o.PharmacyId, o.OrderDate, o.ItemsTotal,
        o.DeliveryCharge, o.TotalAmount, o.CommissionAmount, o.DeliveryAddress,
        o.PaymentMethod, o.Status,
        u.FullName AS CustomerName, u.Phone AS CustomerPhone,
        ph.PharmacyName, ph.Address AS PharmacyAddress, ph.LicenseNo AS PharmacyLicense
FROM    Orders o
        INNER JOIN Users u       ON u.UserId       = o.CustomerId
        INNER JOIN Pharmacies ph ON ph.PharmacyId  = o.PharmacyId
WHERE   o.OrderId = @OrderId;";

            DataTable table = _db.ExecuteTable(header, DbHelper.P("@OrderId", orderId));
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];
            Order order = new Order
            {
                OrderId = DbHelper.GetInt(row, "OrderId"),
                CustomerId = DbHelper.GetInt(row, "CustomerId"),
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                OrderDate = DbHelper.GetDate(row, "OrderDate"),
                ItemsTotal = DbHelper.GetDecimal(row, "ItemsTotal"),
                DeliveryCharge = DbHelper.GetDecimal(row, "DeliveryCharge"),
                TotalAmount = DbHelper.GetDecimal(row, "TotalAmount"),
                CommissionAmount = DbHelper.GetDecimal(row, "CommissionAmount"),
                DeliveryAddress = DbHelper.GetString(row, "DeliveryAddress"),
                PaymentMethod = DbHelper.GetString(row, "PaymentMethod"),
                Status = DbHelper.GetString(row, "Status"),
                CustomerName = DbHelper.GetString(row, "CustomerName"),
                CustomerPhone = DbHelper.GetString(row, "CustomerPhone"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                PharmacyAddress = DbHelper.GetString(row, "PharmacyAddress"),
                PharmacyLicense = DbHelper.GetString(row, "PharmacyLicense")
            };

            const string lines = @"
SELECT  oi.OrderItemId, oi.OrderId, oi.MedicineId, oi.Quantity, oi.UnitPrice, oi.Subtotal,
        m.MedicineName, m.Strength
FROM    OrderItems oi
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId
WHERE   oi.OrderId = @OrderId
ORDER BY m.MedicineName;";

            DataTable items = _db.ExecuteTable(lines, DbHelper.P("@OrderId", orderId));
            foreach (DataRow line in items.Rows)
            {
                order.Items.Add(new OrderItem
                {
                    OrderItemId = DbHelper.GetInt(line, "OrderItemId"),
                    OrderId = DbHelper.GetInt(line, "OrderId"),
                    MedicineId = DbHelper.GetInt(line, "MedicineId"),
                    Quantity = DbHelper.GetInt(line, "Quantity"),
                    UnitPrice = DbHelper.GetDecimal(line, "UnitPrice"),
                    Subtotal = DbHelper.GetDecimal(line, "Subtotal"),
                    MedicineName = DbHelper.GetString(line, "MedicineName"),
                    Strength = DbHelper.GetString(line, "Strength")
                });
            }

            return order;
        }

        public DataTable GetOrderItems(int orderId)
        {
            const string sql = @"
SELECT  m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice, oi.Subtotal
FROM    OrderItems oi
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId
WHERE   oi.OrderId = @OrderId
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql, DbHelper.P("@OrderId", orderId));
        }

        // =====================================================================
        //  PHARMACY OWNER: ORDER QUEUE AND STATUS
        // =====================================================================

        /// <summary>Orders belonging to this pharmacy only.</summary>
        public DataTable GetOrdersForPharmacy(int pharmacyId, string status)
        {
            const string sql = @"
SELECT  o.OrderId, o.OrderDate, u.FullName AS CustomerName, u.Phone AS CustomerPhone,
        COUNT(oi.OrderItemId) AS Items, o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,
        o.PaymentMethod, o.Status, o.DeliveryAddress,
        CASE WHEN EXISTS (SELECT 1 FROM Prescriptions p
                          WHERE p.OrderId = o.OrderId AND p.VerifyStatus <> 'Approved')
             THEN 'Waiting on Rx' ELSE 'Clear' END AS RxState
FROM    Orders o
        INNER JOIN Users u       ON u.UserId    = o.CustomerId
        INNER JOIN OrderItems oi ON oi.OrderId  = o.OrderId
WHERE   o.PharmacyId = @PharmacyId
  AND   (@Status = '' OR o.Status = @Status)
GROUP BY o.OrderId, o.OrderDate, u.FullName, u.Phone, o.ItemsTotal, o.DeliveryCharge,
         o.TotalAmount, o.PaymentMethod, o.Status, o.DeliveryAddress
ORDER BY o.OrderDate DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@Status", status ?? ""));
        }

        /// <summary>
        /// An order cannot be moved to Confirmed while a prescription on it is
        /// still Pending. The NOT EXISTS clause enforces that in the database
        /// rather than trusting the form to disable a button.
        /// </summary>
        public bool Confirm(int orderId, int pharmacyId)
        {
            const string sql = @"
UPDATE  Orders
SET     Status = 'Confirmed'
WHERE   OrderId = @OrderId
  AND   PharmacyId = @PharmacyId
  AND   Status = 'Placed'
  AND   NOT EXISTS (SELECT 1 FROM Prescriptions p
                    WHERE p.OrderId = @OrderId AND p.VerifyStatus <> 'Approved');";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@OrderId", orderId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool MarkDelivered(int orderId, int pharmacyId)
        {
            return _db.ExecuteNonQuery(
                "UPDATE Orders SET Status = 'Delivered' WHERE OrderId = @OrderId AND PharmacyId = @PharmacyId AND Status = 'Confirmed';",
                DbHelper.P("@OrderId", orderId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        /// <summary>Cancelling puts the stock back on the shelf, inside one transaction.</summary>
        public bool Cancel(int orderId, int pharmacyId)
        {
            const string sql = @"
BEGIN TRANSACTION;
    UPDATE  m SET m.Stock = m.Stock + oi.Quantity
    FROM    Medicines m INNER JOIN OrderItems oi ON oi.MedicineId = m.MedicineId
    WHERE   oi.OrderId = @OrderId
      AND   EXISTS (SELECT 1 FROM Orders o
                    WHERE o.OrderId = @OrderId AND o.PharmacyId = @PharmacyId
                      AND o.Status <> 'Cancelled');

    UPDATE  Orders SET Status = 'Cancelled'
    WHERE   OrderId = @OrderId AND PharmacyId = @PharmacyId AND Status <> 'Delivered';
COMMIT TRANSACTION;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@OrderId", orderId),
                DbHelper.P("@PharmacyId", pharmacyId)) > 0;
        }

        public bool OrderBelongsToCustomer(int orderId, int customerId)
        {
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Orders WHERE OrderId = @OrderId AND CustomerId = @CustomerId;",
                DbHelper.P("@OrderId", orderId),
                DbHelper.P("@CustomerId", customerId)) == 1;
        }
    }
}
