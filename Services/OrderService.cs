using System.Data;                  // DataTable, DataRow and IsolationLevel all live here
using Microsoft.Data.SqlClient;     // SqlConnection, SqlTransaction and SqlCommand, used by Checkout
using PharmaLinkApp.Database;       // DbHelper, the single place that knows the connection string
using PharmaLinkApp.Models;         // Order and OrderItem, the objects the invoice is printed from

// Beside CartService: checkout consumes what the cart built.
namespace PharmaLinkApp.Services
{
    // Layer: service. Checkout and Cancel run their own explicit transaction.

    /// <summary>Checkout, order history, invoices and order status.</summary>
    public class OrderService
    {
        // One helper for every method that does not need a transaction of its own.
        private readonly DbHelper _db = new DbHelper();

        // == checkout (requirement 25) ==

        /// <summary>Places one order for one pharmacy's slice of the basket.</summary>
        public int Checkout(int customerId, int pharmacyId, string deliveryAddress,
                            string paymentMethod, decimal deliveryCharge, out string message)   // out, not an exception: a sold-out basket is the customer's to fix
        {
            // Its own connection, because all five statements share one transaction.
            using (SqlConnection conn = _db.GetConnection())
            {
                // Opened by hand: a transaction cannot start on a closed connection.
                conn.Open();
                // Serializable: stops another checkout taking the last unit mid-transaction.
                using (SqlTransaction tx = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try   // stock check to commit, so any failure lands on one rollback
                    {
                        // 1. Re-check stock INSIDE the transaction, not on the cart screen.
                        const string stockCheck = @"
SELECT TOP 1 m.MedicineName   -- the name, not a COUNT, so the refusal can say which medicine
FROM   Cart ct   -- the basket drives it: a medicine nobody asked for cannot block a checkout
       INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId   -- Medicines carries Stock and IsActive
WHERE  ct.CustomerId = @CustomerId   -- this customer's basket only
  AND  m.PharmacyId  = @PharmacyId   -- and only this shop's slice of it
-- Two ways a line is unsellable: too few units, or the medicine was delisted.
  AND  (ct.Quantity > m.Stock OR m.IsActive = 0);";

                        string problem;   // declared outside the using, so it survives the command
                        // Every command must be told its transaction - the third argument, tx.
                        using (SqlCommand cmd = new SqlCommand(stockCheck, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@CustomerId", customerId);   // parameters, not concatenation, as everywhere else
                            cmd.Parameters.AddWithValue("@PharmacyId", pharmacyId);   // the slice of the basket this one order covers
                            object result = cmd.ExecuteScalar();   // null here is the GOOD outcome: nothing is out of stock
                            // null and DBNull are different things in ADO.NET, so both are tested.
                            problem = result == null || result == DBNull.Value ? null : result.ToString();
                        }

                        if (problem != null)   // non-null means the query named a medicine
                        {
                            // Roll back before anything is written; it also frees the range locks.
                            tx.Rollback();
                            message = "'" + problem + "' is no longer available in the quantity you asked for. " +   // names the line the customer must edit
                                      "Please update your cart and try again.";   // ends on the action, because the basket is still there
                            return 0;      // 0 is the "no order was created" signal to the form
                        }

                        // 2. The order header, with the commission frozen at today's rate.
                        const string placeOrder = @"
-- Three locals, so the total and the rate are each computed once and then reused.
DECLARE @Total DECIMAL(12,2), @CommRate DECIMAL(5,2), @NewOrderId INT;

-- Priced HERE, in the transaction, so it cannot drift from what the form showed.
SELECT  @Total = CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2))
FROM    Cart ct   -- the rows just verified as sellable, and the ones the DELETE will remove
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId   -- UnitPrice lives on Medicines
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct   -- MAX, so the customer gets the best offer
                     FROM   Offers o   -- read, never written: a discount is not consumed
                     WHERE  o.MedicineId = m.MedicineId AND o.IsActive = 1   -- an offer can be switched off by hand
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d   -- and must be inside its dates
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;   -- this customer, this shop only

SELECT  @CommRate = CommissionRate FROM Pharmacies WHERE PharmacyId = @PharmacyId;   -- the rate as it stands right now

-- TotalAmount is omitted: a computed column refuses an explicit value.
INSERT INTO Orders (CustomerId, PharmacyId, ItemsTotal, DeliveryCharge, CommissionAmount,
                    DeliveryAddress, PaymentMethod, Status)   -- address and method are copied onto the order
VALUES (@CustomerId, @PharmacyId, @Total, @DeliveryCharge,   -- values line up with the column list above
        CAST(@Total * @CommRate / 100.0 AS DECIMAL(12,2)),   -- the commission is FROZEN here, never recomputed
        @DeliveryAddress, @PaymentMethod, 'Placed');   -- 'Placed' is a literal: a new order has one legal state

-- SCOPE_IDENTITY(), not @@IDENTITY, so a trigger cannot hand back its own id.
SET @NewOrderId = SCOPE_IDENTITY();

-- One line per cart row, at the discounted price the customer actually saw.
INSERT INTO OrderItems (OrderId, MedicineId, Quantity, UnitPrice)   -- Subtotal is computed by the table
SELECT  @NewOrderId, ct.MedicineId, ct.Quantity,   -- the same id on every row ties the lines to the header
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(10,2))   -- price copied ONTO the order, so old invoices never reprice
FROM    Cart ct   -- the second read of Cart, which is why the DELETE has to come last
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId   -- the catalogue again, for the price
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct   -- word for word the same offer lookup as the total
                     FROM   Offers o   -- edit this block and the one above together, or they stop agreeing
                     WHERE  o.MedicineId = m.MedicineId AND o.IsActive = 1   -- same two guards as before
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d   -- today inside the window
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;   -- exactly the lines priced into the header

-- One set-based UPDATE: UQ_Cart_Line makes the join match each medicine once.
UPDATE  m SET m.Stock = m.Stock - ct.Quantity   -- take the stock off the shelf
FROM    Medicines m INNER JOIN Cart ct ON ct.MedicineId = m.MedicineId   -- Medicines is written, Cart supplies amounts
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;   -- the units removed match the units sold

-- Cart DELETE must be last - the INSERT and UPDATE above read these rows.
DELETE  ct   -- 'ct' after DELETE, so no row of Medicines is touched
FROM    Cart ct INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId   -- joined only to reach PharmacyId
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;   -- the other shop's lines become the next order

-- The last statement of the batch, so this is what ExecuteScalar picks up.
SELECT CAST(@NewOrderId AS INT);";

                        int newOrderId;   // declared before the using, so the value survives the block
                        using (SqlCommand cmd = new SqlCommand(placeOrder, conn, tx))   // tx again: every command must be enlisted
                        {
                            cmd.Parameters.AddWithValue("@CustomerId", customerId);   // five parameters shared by every statement
                            cmd.Parameters.AddWithValue("@PharmacyId", pharmacyId);   // the isolation column that splits a two-shop basket
                            cmd.Parameters.AddWithValue("@DeliveryCharge", deliveryCharge);   // decimal to DECIMAL, so money arrives exact
                            cmd.Parameters.AddWithValue("@DeliveryAddress", deliveryAddress);   // copied on, so moving house does not rewrite old parcels
                            cmd.Parameters.AddWithValue("@PaymentMethod", paymentMethod);   // CK_Orders_Payment refuses anything but four known words
                            newOrderId = Convert.ToInt32(cmd.ExecuteScalar());   // reads the final SELECT of the batch, boxed as object
                        }

                        // Nothing above this line is durable; the commit makes it visible at once.
                        tx.Commit();
                        message = "Order placed.";   // short on purpose: the invoice that opens next carries the detail
                        return newOrderId;   // a non-zero id is the success signal, and the form opens the invoice
                    }
                    catch (Exception ex)   // not just SqlException: a deadlock and a dropped connection end the same way
                    {
                        // Wrapped, because Rollback itself throws on a doomed transaction.
                        try { tx.Rollback(); } catch { /* connection already gone */ }
                        // Reported through out, not rethrown, and the wording is SQL Server's own.
                        message = "The order could not be placed: " + ex.Message;
                        return 0;      // the same "nothing was created" signal as the stock refusal
                    }
                }
            }
        }

        // == order history (requirement 27) ==

        /// <summary>The customer's own orders, with a CanReview flag for each.</summary>
        public DataTable GetHistoryForCustomer(int customerId, string status, int pharmacyId,
                                               DateTime fromDate, DateTime toDate)   // five filters, one query
        {
            // const, so the filters vary through parameters and never through the text.
            const string sql = @"
SELECT  o.OrderId, o.OrderDate, ph.PharmacyName,   -- which order, when, and from which shop
        COUNT(oi.OrderItemId)      AS Items,   -- one row per order, so the lines are counted
        o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,   -- TotalAmount is the persisted computed column
        o.PaymentMethod, o.Status,   -- plain text, so the grid needs no lookup table
        CASE WHEN o.Status = 'Delivered'   -- CanReview: delivered AND something still unreviewed
              AND EXISTS (SELECT 1 FROM OrderItems x   -- the outer EXISTS walks this order's lines
                          WHERE x.OrderId = o.OrderId   -- correlated, so only this order counts
                            AND NOT EXISTS (SELECT 1 FROM Reviews r   -- keep only lines with no review yet
                                            WHERE r.OrderId = o.OrderId   -- the same order...
                                              AND r.MedicineId = x.MedicineId))   -- ...and the same medicine on it
             THEN 1 ELSE 0 END     AS CanReview   -- 1 and 0, because SQL Server has no BOOLEAN to return
FROM    Orders o   -- Orders drives the query; the joins only decorate it
        INNER JOIN Pharmacies ph ON ph.PharmacyId = o.PharmacyId   -- the shop's name, not just its id
        INNER JOIN OrderItems oi ON oi.OrderId    = o.OrderId   -- INNER is safe: header and lines are written together
WHERE   o.CustomerId = @CustomerId   -- the line that stops one person reading another's history
  AND   (@Status     = '' OR o.Status = @Status)   -- '' means no filter, so one query serves every tab
  AND   (@PharmacyId = 0  OR o.PharmacyId = @PharmacyId)   -- 0 is safe: identity columns start at 1
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate   -- BETWEEN is inclusive, which the C# below relies on
GROUP BY o.OrderId, o.OrderDate, ph.PharmacyName, o.ItemsTotal, o.DeliveryCharge,   -- every non-aggregated column
         o.TotalAmount, o.PaymentMethod, o.Status   -- leaving one out is a compile error, not a wrong answer
-- Newest first, which is where a customer looks for their last purchase.
ORDER BY o.OrderDate DESC;";

            return _db.ExecuteTable(sql,   // a grid-bound read, so an empty history returns an empty table
                DbHelper.P("@CustomerId", customerId),   // from UserSession, never from the screen
                DbHelper.P("@Status", status ?? ""),   // ?? "" because a NULL parameter would match nothing
                DbHelper.P("@PharmacyId", pharmacyId),   // 0 is the form's "All pharmacies" entry
                DbHelper.P("@FromDate", fromDate.Date),   // .Date, so the range starts at midnight
                // The END of the chosen day: toDate.Date would cut the range at midnight.
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)));
        }

        /// <summary>Everything the printable invoice needs, in one object.</summary>
        public Order GetOrderWithItems(int orderId)
        {
            // Two queries: the header here, the lines further down.
            const string header = @"
SELECT  o.OrderId, o.CustomerId, o.PharmacyId, o.OrderDate, o.ItemsTotal,   -- ids carried through for a re-check
        o.DeliveryCharge, o.TotalAmount, o.CommissionAmount, o.DeliveryAddress,   -- commission as frozen, never recomputed
        o.PaymentMethod, o.Status,   -- both printed: how it was paid, and where it has got to
        u.FullName AS CustomerName, u.Phone AS CustomerPhone,   -- aliased, because Pharmacies has names too
        ph.PharmacyName, ph.Address AS PharmacyAddress, ph.LicenseNo AS PharmacyLicense   -- the licensed dispenser
FROM    Orders o   -- one row, since OrderId is the primary key
        INNER JOIN Users u       ON u.UserId       = o.CustomerId   -- an order cannot exist without its customer
        INNER JOIN Pharmacies ph ON ph.PharmacyId  = o.PharmacyId   -- nor without its pharmacy
-- Ownership is checked separately by OrderBelongsToCustomer, not here.
WHERE   o.OrderId = @OrderId;";

            DataTable table = _db.ExecuteTable(header, DbHelper.P("@OrderId", orderId));   // one round trip for the header
            // null means "no such order"; an empty Order would print a blank invoice.
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];      // OrderId is the primary key, so at most one
            Order order = new Order   // an object initialiser, so every property is set in one expression
            {
                OrderId = DbHelper.GetInt(row, "OrderId"),   // the invoice number the customer quotes back
                CustomerId = DbHelper.GetInt(row, "CustomerId"),   // kept so the caller can re-verify the owner
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),   // which shop owes the commission below
                OrderDate = DbHelper.GetDate(row, "OrderDate"),   // a NULL becomes an obviously wrong MinValue
                // Every money column arrives through GetDecimal, which turns DBNull into 0m.
                ItemsTotal = DbHelper.GetDecimal(row, "ItemsTotal"),
                DeliveryCharge = DbHelper.GetDecimal(row, "DeliveryCharge"),   // separate, because it is not commissionable
                TotalAmount = DbHelper.GetDecimal(row, "TotalAmount"),   // the computed column, copied as-is
                CommissionAmount = DbHelper.GetDecimal(row, "CommissionAmount"),   // frozen at checkout, not today's rate
                DeliveryAddress = DbHelper.GetString(row, "DeliveryAddress"),   // as it was then, not from the profile now
                PaymentMethod = DbHelper.GetString(row, "PaymentMethod"),   // one of four words the CHECK constraint allows
                Status = DbHelper.GetString(row, "Status"),   // so a reprint shows 'Cancelled' rather than looking live
                CustomerName = DbHelper.GetString(row, "CustomerName"),   // who the bill is for
                CustomerPhone = DbHelper.GetString(row, "CustomerPhone"),   // the delivery contact, so a courier can call
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),   // the trading name at the head of the bill
                PharmacyAddress = DbHelper.GetString(row, "PharmacyAddress"),   // where the medicine was dispensed from
                PharmacyLicense = DbHelper.GetString(row, "PharmacyLicense")   // no trailing comma: the last member
            };

            // The second query, separate from the header for the reason given below it.
            const string lines = @"
SELECT  oi.OrderItemId, oi.OrderId, oi.MedicineId, oi.Quantity, oi.UnitPrice, oi.Subtotal,   -- Subtotal is persisted
        m.MedicineName, m.Strength   -- joined for display only; the price stays as sold
FROM    OrderItems oi   -- one row here is one printed line on the invoice
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId   -- name and strength only, nothing priced
WHERE   oi.OrderId = @OrderId   -- the one order whose header was just read
-- A fixed order, so reprinting the same invoice twice produces identical paper.
ORDER BY m.MedicineName;";

            // A second query rather than one big join, which would repeat every header value.
            DataTable items = _db.ExecuteTable(lines, DbHelper.P("@OrderId", orderId));
            // Walk the rows once, turning each into an OrderItem hanging off the order.
            foreach (DataRow line in items.Rows)
            {
                // Items is an empty list in the model, never null, so Add needs no guard.
                order.Items.Add(new OrderItem
                {
                    OrderItemId = DbHelper.GetInt(line, "OrderItemId"),   // the line's own key, kept for completeness
                    OrderId = DbHelper.GetInt(line, "OrderId"),   // redundant, but it makes an OrderItem readable alone
                    MedicineId = DbHelper.GetInt(line, "MedicineId"),   // what the review screen needs to know
                    Quantity = DbHelper.GetInt(line, "Quantity"),   // how many units, as sold
                    UnitPrice = DbHelper.GetDecimal(line, "UnitPrice"),   // the price AT THE TIME, from OrderItems
                    Subtotal = DbHelper.GetDecimal(line, "Subtotal"),   // read, not multiplied: one source of truth
                    MedicineName = DbHelper.GetString(line, "MedicineName"),   // display only, joined in
                    Strength = DbHelper.GetString(line, "Strength")   // '500mg' and the like, so two products differ
                });
            }

            // One assembled object: header plus lines, so InvoiceForm needs no second read.
            return order;
        }

        // The grid version of the invoice lines: bound and displayed, no rules.
        public DataTable GetOrderItems(int orderId)
        {
            // const, so it cannot be rebuilt at run time with a value pasted into it.
            const string sql = @"
SELECT  m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice, oi.Subtotal   -- no ids: this binds straight to a grid
FROM    OrderItems oi   -- the order's lines are the rows of the grid, one for one
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId   -- names and strengths only
WHERE   oi.OrderId = @OrderId   -- the caller has already established this order is the viewer's
-- Alphabetical, so the same order always renders in the same sequence.
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql, DbHelper.P("@OrderId", orderId));   // one parameter, so the call fits on a line
        }

        // == pharmacy owner: order queue and status ==

        /// <summary>Orders belonging to this pharmacy only.</summary>
        public DataTable GetOrdersForPharmacy(int pharmacyId, string status)
        {
            // The owner's work queue: every column answers "what do I do with this next".
            const string sql = @"
SELECT  o.OrderId, o.OrderDate, u.FullName AS CustomerName, u.Phone AS CustomerPhone,   -- who placed it, and how to reach them
        COUNT(oi.OrderItemId) AS Items, o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,   -- how big the order is
        o.PaymentMethod, o.Status, o.DeliveryAddress,   -- enough to label a parcel without opening the order
        CASE WHEN EXISTS (SELECT 1 FROM Prescriptions p   -- a ready-made badge, computed once per row
                          WHERE p.OrderId = o.OrderId AND p.VerifyStatus <> 'Approved')   -- Pending and Rejected both block
             THEN 'Waiting on Rx' ELSE 'Clear' END AS RxState   -- two words the owner can act on
FROM    Orders o   -- Orders is the queue; the joins only add names and counts
        INNER JOIN Users u       ON u.UserId    = o.CustomerId   -- the customer's name and phone
        INNER JOIN OrderItems oi ON oi.OrderId  = o.OrderId   -- joined so COUNT has something to count
WHERE   o.PharmacyId = @PharmacyId   -- from UserSession, so an owner sees only their own orders
  AND   (@Status = '' OR o.Status = @Status)   -- the same empty-means-everything filter, for the status tabs
GROUP BY o.OrderId, o.OrderDate, u.FullName, u.Phone, o.ItemsTotal, o.DeliveryCharge,   -- required by the COUNT above
         o.TotalAmount, o.PaymentMethod, o.Status, o.DeliveryAddress   -- RxState is derived from a grouped column
-- Newest first: the queue is worked from the top.
ORDER BY o.OrderDate DESC;";

            return _db.ExecuteTable(sql,   // bound to the dashboard grid, so an empty queue must not be null
                DbHelper.P("@PharmacyId", pharmacyId),   // the signed-in owner's shop
                DbHelper.P("@Status", status ?? ""));   // ?? "" as everywhere else: NULL would match nothing
        }

        /// <summary>No order reaches Confirmed while an Rx is unapproved.</summary>
        public bool Confirm(int orderId, int pharmacyId)
        {
            // Four conditions in one statement, so no connection can slip between them.
            const string sql = @"
UPDATE  Orders   -- single-table: every condition reads Orders or a correlated subquery
SET     Status = 'Confirmed'   -- a literal, because CK_Orders_Status allows only four words
WHERE   OrderId = @OrderId   -- the primary key picks the row
  AND   PharmacyId = @PharmacyId              -- isolation: only your own orders
  AND   Status = 'Placed'   -- naming the expected state makes pressing Confirm twice harmless
  AND   NOT EXISTS (SELECT 1 FROM Prescriptions p   -- the Rx gate, enforced here and not by a disabled button
-- <> 'Approved' catches Pending and Rejected, so a rejected image blocks too.
                    WHERE p.OrderId = @OrderId AND p.VerifyStatus <> 'Approved');";

            // == 1 means all four held: it existed, was this shop's, was Placed, Rx clear.
            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@OrderId", orderId),   // the row picked from the owner's queue
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // from the session, so ownership cannot be spoofed
        }

        // The second step, Confirmed -> Delivered, so no caller can invent a transition.
        public bool MarkDelivered(int orderId, int pharmacyId)
        {
            return _db.ExecuteNonQuery(   // rows affected again: 1 is the only outcome that means it happened
                // "AND Status = 'Confirmed'" enforces Placed -> Confirmed -> Delivered.
                "UPDATE Orders SET Status = 'Delivered' WHERE OrderId = @OrderId AND PharmacyId = @PharmacyId AND Status = 'Confirmed';",
                DbHelper.P("@OrderId", orderId),   // which order the owner ticked off
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // and PharmacyId keeps one shop out of another's queue
        }

        /// <summary>Cancelling puts the stock back, inside one transaction.</summary>
        public bool Cancel(int orderId, int pharmacyId)
        {
            // The transaction is written in T-SQL, so DbHelper still translates its errors.
            const string sql = @"
SET XACT_ABORT ON;   -- any runtime error aborts the whole batch, belt and braces with the CATCH
-- TRY/CATCH in T-SQL, because the rollback has to happen on the server.
BEGIN TRY
    BEGIN TRANSACTION;   -- both writes below are inside it, so neither can land alone

    DECLARE @Cancelled INT = 0;   -- 0, so a refused cancel returns a definite answer, not NULL

    -- THE ELIGIBILITY TEST, MADE ONCE, so both writes share exactly one condition.
    IF EXISTS (SELECT 1 FROM Orders
               WHERE OrderId    = @OrderId   -- the primary key, so one order is tested
                 AND PharmacyId = @PharmacyId   -- ownership, as on every other write here
                 AND Status NOT IN ('Delivered', 'Cancelled'))   -- too late to cancel, or already done
    BEGIN   -- groups both writes behind the single test above, which is the bug fix
        UPDATE  m SET m.Stock = m.Stock + oi.Quantity   -- put every unit on this order back
        FROM    Medicines m INNER JOIN OrderItems oi ON oi.MedicineId = m.MedicineId   -- the mirror of checkout's deduction
        WHERE   oi.OrderId = @OrderId;   -- this order's lines only; the EXISTS proved ownership

        -- The same conditions again, so the UPDATE is self-guarding.
        UPDATE  Orders SET Status = 'Cancelled'
        WHERE   OrderId    = @OrderId   -- the primary key picks the row...
          AND   PharmacyId = @PharmacyId   -- ...ownership is re-asserted on the write...
          AND   Status NOT IN ('Delivered', 'Cancelled');   -- ...and the status test is repeated

        SET @Cancelled = @@ROWCOUNT;   -- read IMMEDIATELY, because any later statement overwrites it
    END   -- an order that failed the test skips both writes and leaves @Cancelled at 0

    COMMIT TRANSACTION;   -- the restock and the status change become visible together
    SELECT @Cancelled;              -- handed back to C# as the success flag
END TRY   -- anything that threw above jumps straight to the CATCH
-- Reached only on a runtime error, and its job is to leave nothing half-applied.
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- non-zero means a transaction is still open
    THROW;   -- re-raises the ORIGINAL error, so DbHelper can translate it as usual
-- Closes the handler and the batch.
END CATCH;";

            // ExecuteScalarInt: rows affected would also count the restocked medicines.
            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@OrderId", orderId),   // the order the owner chose to cancel
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // 0 means already delivered, already cancelled, or not theirs
        }

        // The ownership question as its own method, so every screen asks it the same way.
        public bool OrderBelongsToCustomer(int orderId, int customerId)
        {
            return _db.ExecuteScalarInt(   // a COUNT, so the answer is a number rather than a row
                "SELECT COUNT(*) FROM Orders WHERE OrderId = @OrderId AND CustomerId = @CustomerId;",   // both columns, so 1 means BOTH matched
                DbHelper.P("@OrderId", orderId),   // the order being opened, which may have been typed
                DbHelper.P("@CustomerId", customerId)) == 1;   // == 1, not > 0: OrderId is the primary key
        }
    }
}
