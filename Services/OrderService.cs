using System.Data;                  // DataTable, DataRow and IsolationLevel, which lives in this namespace
using Microsoft.Data.SqlClient;     // SqlConnection, SqlTransaction and SqlCommand, used by Checkout
using PharmaLinkApp.Database;       // DbHelper, the single place that knows the connection string
using PharmaLinkApp.Models;         // Order and OrderItem, the typed objects the invoice is printed from

namespace PharmaLinkApp.Services
{
    // -------------------------------------------------------------------------
    //  Layer: service.  Called by CheckoutForm, CartForm, OrderHistoryForm,
    //  InvoiceForm and AdminDashboard. Reads and writes through DbHelper.
    //
    //  Checkout() and Cancel() are the only methods that open their own
    //  SqlConnection rather than using DbHelper's helpers, because both need one
    //  explicit transaction spanning several statements while DbHelper opens and
    //  closes a connection per call. A SqlException raised here therefore does
    //  not pass through DbHelper.Describe(), which is why Program.ReportFatal
    //  carries a second SqlException branch.
    //
    //  Every statement carries @PharmacyId, taken from UserSession.
    // -------------------------------------------------------------------------

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
        // One helper for every method that does not need a transaction of its own.
        // DbHelper keeps no connection open between calls, so sharing it is safe.
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
            // This method opens its OWN connection instead of using DbHelper's helpers,
            // because all five statements below must share one transaction and DbHelper
            // deliberately opens and closes a connection per call.
            using (SqlConnection conn = _db.GetConnection())
            {
                // Opened by hand here, unlike ExecuteTable where the data adapter does it:
                // a transaction cannot be started on a connection that is still closed.
                conn.Open();
                // Serializable is the strictest isolation level: it takes range locks, so
                // no other checkout can read or change the stock rows this transaction is
                // working with until it commits. That is what makes the stock re-check
                // below trustworthy rather than merely hopeful.
                // The default, ReadCommitted, would release its read lock the instant the
                // check finished, leaving a gap in which another customer could take the
                // last unit before the UPDATE ran. The price of Serializable is that two
                // simultaneous checkouts may block or deadlock; the catch at the bottom
                // turns that into a message asking the customer to try again, which is a
                // far better outcome than selling stock that is not there.
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
-- TOP 1 because the question is 'is anything wrong', not 'how many things are wrong'.
-- The name is selected rather than a COUNT so the refusal message can say which
-- medicine caused it instead of a vague apology.
SELECT TOP 1 m.MedicineName
FROM   Cart ct
       INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
WHERE  ct.CustomerId = @CustomerId
  -- Only this shop's slice of the basket. The rest of the cart belongs to a different
  -- order and must not be able to block this one.
  AND  m.PharmacyId  = @PharmacyId
  -- Two ways a line can be unsellable, tested together: the shelf no longer holds
  -- enough units, or the pharmacy has delisted the medicine since it was added.
  AND  (ct.Quantity > m.Stock OR m.IsActive = 0);";

                        // Declared outside the using so it still exists after the command
                        // has been disposed, which is where the decision is made.
                        string problem;
                        // Every command in this transaction must be told which transaction
                        // it belongs to - that is the third constructor argument, tx.
                        // Leaving it out throws, because the connection has an open
                        // transaction that the command is not enlisted in.
                        using (SqlCommand cmd = new SqlCommand(stockCheck, conn, tx))
                        {
                            // The same two values the SQL filters on. Parameters, not
                            // concatenation, exactly as everywhere else in the project.
                            cmd.Parameters.AddWithValue("@CustomerId", customerId);
                            cmd.Parameters.AddWithValue("@PharmacyId", pharmacyId);

                            // ExecuteScalar returns the first column of the first row, or
                            // null when the query found nothing. Here "found nothing" is
                            // the GOOD outcome: no offending medicine means every line is
                            // still in stock and still on sale.
                            object result = cmd.ExecuteScalar();
                            // Both cases are tested: null means no row came back at all,
                            // DBNull would mean a row whose value was NULL. They are
                            // different things in ADO.NET and only the pair covers both.
                            problem = result == null || result == DBNull.Value ? null : result.ToString();
                        }

                        if (problem != null)
                        {
                            // Something sold out, or was delisted, between adding it to the
                            // cart and pressing Confirm. Roll back before anything is
                            // written and hand the medicine's name back so the message can
                            // name it rather than saying "something went wrong".
                            // Rolling back explicitly rather than just returning releases
                            // the Serializable range locks at once instead of holding them
                            // until the using block disposes.
                            tx.Rollback();
                            message = "'" + problem + "' is no longer available in the quantity you asked for. " +
                                      "Please update your cart and try again.";
                            return 0;      // 0 is the "no order was created" signal to the form
                        }

                        // ---- 2. the order header, commission frozen at today's rate ----
                        const string placeOrder = @"
-- Three local variables, so the total and the commission rate are each computed once
-- and then reused. Recomputing the total for the INSERT would mean running the same
-- aggregate twice and trusting the two runs to agree.
DECLARE @Total DECIMAL(12,2), @CommRate DECIMAL(5,2), @NewOrderId INT;

-- Work out what this half of the basket costs, with today's discount already applied.
-- OUTER APPLY runs the little offer lookup once per cart line and, being OUTER, keeps
-- the line even when no offer exists (d.Pct is then NULL, which ISNULL turns into 0).
-- MAX() is used because a medicine could legitimately have more than one offer running,
-- and the customer should get the best of them.
-- Computing the total HERE, in the same transaction, rather than trusting a number the
-- form calculated, means the price cannot drift between the screen and the database.
-- 100.0 rather than 100 forces decimal division; integer division would floor every
-- percentage to zero and quietly charge the full price on every discounted line.
SELECT  @Total = CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2))
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId AND o.IsActive = 1
                       -- The date window is part of the lookup, so an offer that ended
                       -- yesterday cannot be picked up by a checkout running today.
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

-- Read the shop's commission rate as it stands RIGHT NOW.
SELECT  @CommRate = CommissionRate FROM Pharmacies WHERE PharmacyId = @PharmacyId;

-- The order header. CommissionAmount is calculated once, here, and STORED - it is not a
-- computed column and nothing ever recalculates it. That is what lets the Super Admin
-- change a shop's rate next month without rewriting what was owed on this order.
-- TotalAmount is absent from the column list on purpose: it IS a computed column
-- (ItemsTotal + DeliveryCharge), so SQL Server refuses an explicit value for it.
-- Status is the literal 'Placed' rather than a parameter, because a new order has only
-- one legal starting state and CK_Orders_Status would refuse anything else.
INSERT INTO Orders (CustomerId, PharmacyId, ItemsTotal, DeliveryCharge, CommissionAmount,
                    DeliveryAddress, PaymentMethod, Status)
VALUES (@CustomerId, @PharmacyId, @Total, @DeliveryCharge,
        -- The frozen commission: today's rate applied to today's total, rounded once and
        -- written down. DECIMAL throughout, never FLOAT, so money never drifts by a paisa.
        CAST(@Total * @CommRate / 100.0 AS DECIMAL(12,2)),
        @DeliveryAddress, @PaymentMethod, 'Placed');

-- SCOPE_IDENTITY(), not @@IDENTITY: it returns the id generated by THIS statement in
-- this scope, so a trigger inserting elsewhere could never hand back the wrong number.
-- Orders starts at 1001, so the first invoice is 1001 rather than 1.
SET @NewOrderId = SCOPE_IDENTITY();

-- one line per cart row, at the discounted price the customer actually saw
-- INSERT ... SELECT rather than a loop in C#: one statement, one round trip, and every
-- line lands or none of them does. Subtotal is left out because OrderItems computes it
-- as Quantity * UnitPrice, so a line total can never contradict its own parts.
-- The price is copied ONTO the order on purpose. Reading it back from Medicines later
-- would reprice old invoices every time a pharmacy edited its catalogue.
INSERT INTO OrderItems (OrderId, MedicineId, Quantity, UnitPrice)
SELECT  @NewOrderId, ct.MedicineId, ct.Quantity,
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(10,2))
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        -- The same offer lookup as the total above, word for word, so the sum of the
        -- lines and the header's ItemsTotal are arrived at by identical arithmetic.
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId AND o.IsActive = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

-- take the stock off the shelf
-- One set based UPDATE, not a statement per line. UQ_Cart_Line makes (CustomerId,
-- MedicineId) unique, so the join matches each medicine exactly once and no quantity
-- can be deducted twice. The Serializable locks taken by the check above are still held
-- here, which is what guarantees the numbers have not moved since they were verified.
UPDATE  m SET m.Stock = m.Stock - ct.Quantity
FROM    Medicines m INNER JOIN Cart ct ON ct.MedicineId = m.MedicineId
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

-- clear only this pharmacy's lines; the rest of the basket becomes the next order
-- This statement is LAST for a reason: it destroys the Cart rows that the two
-- statements above read. Moving it earlier would leave the order with no items and the
-- stock untouched, and neither failure would raise an error.
DELETE  ct
FROM    Cart ct INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
WHERE   ct.CustomerId = @CustomerId AND m.PharmacyId = @PharmacyId;

-- The last statement in the batch, so this is what ExecuteScalar picks up: the new
-- order number, which the form uses to open the invoice.
SELECT CAST(@NewOrderId AS INT);";

                        // Declared before the using so the value survives the block.
                        int newOrderId;
                        using (SqlCommand cmd = new SqlCommand(placeOrder, conn, tx))
                        {
                            // Five parameters shared by every statement in the batch. Sending
                            // the whole thing as one command means one round trip and, more
                            // importantly, one set of values that every statement agrees on.
                            cmd.Parameters.AddWithValue("@CustomerId", customerId);
                            // The isolation column. It appears in all five statements, which
                            // is what splits a two-pharmacy basket into two separate orders.
                            cmd.Parameters.AddWithValue("@PharmacyId", pharmacyId);
                            // decimal in C# maps to DECIMAL in SQL Server, so the delivery
                            // charge arrives with its exact value rather than a float's
                            // nearest approximation.
                            cmd.Parameters.AddWithValue("@DeliveryCharge", deliveryCharge);
                            // Copied onto the order rather than read from the customer's
                            // profile later, so moving house does not rewrite where last
                            // month's parcel was actually sent.
                            cmd.Parameters.AddWithValue("@DeliveryAddress", deliveryAddress);
                            // CK_Orders_Payment restricts this to CashOnDelivery, bKash,
                            // Nagad or Card, so an unexpected value is refused by the
                            // database rather than silently stored.
                            cmd.Parameters.AddWithValue("@PaymentMethod", paymentMethod);
                            // ExecuteScalar reads the final SELECT of the batch. Convert
                            // unboxes it, because ExecuteScalar is typed as object.
                            newOrderId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        // Nothing above this line is durable. The commit is the single
                        // instant at which the order, its lines, the reduced stock and the
                        // emptied cart all become visible to everyone else at once.
                        tx.Commit();
                        message = "Order placed.";
                        // A non-zero id is the success signal; the form opens the invoice.
                        return newOrderId;
                    }
                    catch (Exception ex)
                    {
                        // Catching Exception rather than SqlException on purpose: a deadlock
                        // victim, a constraint violation and a dropped connection must all
                        // end the same way, with nothing written and a message the customer
                        // can act on.
                        // The rollback is itself wrapped, because it throws if the
                        // transaction is already doomed or the connection has gone. An
                        // exception escaping from in here would replace the real failure
                        // with a misleading one, so it is deliberately ignored.
                        try { tx.Rollback(); } catch { /* connection already gone */ }
                        // The failure is reported through the out parameter rather than
                        // rethrown, because this method's contract with the form is an id
                        // plus a message. Since the command bypassed DbHelper, ex.Message is
                        // SQL Server's own wording rather than a translated sentence.
                        message = "The order could not be placed: " + ex.Message;
                        return 0;      // same "nothing was created" signal as the stock refusal
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
        -- One row per order in the grid, so the lines are counted rather than listed.
        -- Counting the junction table's key is what the GROUP BY below exists for.
        COUNT(oi.OrderItemId)      AS Items,
        -- TotalAmount is the PERSISTED computed column (ItemsTotal + DeliveryCharge).
        -- Reading it instead of adding the two in C# means the grid and the printed
        -- invoice can never show different totals.
        o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,
        o.PaymentMethod, o.Status,
        -- CanReview: 1 only when the order has been delivered AND at least one medicine
        -- on it has not been reviewed yet. Two conditions, because a review is proof of
        -- a real purchase and because the same order must not be reviewable twice.
        CASE WHEN o.Status = 'Delivered'
              -- The outer EXISTS walks the order's items...
              AND EXISTS (SELECT 1 FROM OrderItems x
                          WHERE x.OrderId = o.OrderId
                            -- ...and the inner NOT EXISTS keeps only those with no review
                            -- row for this order and this medicine. Correlated on both
                            -- columns, so reviewing the same medicine on a DIFFERENT order
                            -- does not silently disable the button here.
                            AND NOT EXISTS (SELECT 1 FROM Reviews r
                                            WHERE r.OrderId = o.OrderId
                                              AND r.MedicineId = x.MedicineId))
             -- 1 and 0 rather than true and false, because SQL Server has no BOOLEAN type
             -- to return and an int binds cleanly to the grid column.
             THEN 1 ELSE 0 END     AS CanReview
FROM    Orders o
        INNER JOIN Pharmacies ph ON ph.PharmacyId = o.PharmacyId
        -- INNER, which would drop an order that had no lines at all. Checkout writes the
        -- header and the lines in one transaction, so such an order cannot exist.
        INNER JOIN OrderItems oi ON oi.OrderId    = o.OrderId
-- The customer's OWN orders only. This is the line that stops one person reading
-- another's purchase history by changing a number.
WHERE   o.CustomerId = @CustomerId
  -- The optional filter pattern: an empty string, or 0 for the pharmacy, means 'no
  -- filter' and the OR short circuits the comparison. One query serves every
  -- combination of the three boxes on the form.
  AND   (@Status     = '' OR o.Status = @Status)
  AND   (@PharmacyId = 0  OR o.PharmacyId = @PharmacyId)
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate
-- Every non-aggregated column has to be listed here; only COUNT() is aggregated.
GROUP BY o.OrderId, o.OrderDate, ph.PharmacyName, o.ItemsTotal, o.DeliveryCharge,
         o.TotalAmount, o.PaymentMethod, o.Status
-- Newest first, which is the order a customer looks for their last purchase in.
ORDER BY o.OrderDate DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@CustomerId", customerId),
                // ?? "" so a null from the form means 'no filter'. A NULL parameter would
                // make @Status = '' evaluate to UNKNOWN and return an empty grid instead.
                DbHelper.P("@Status", status ?? ""),
                DbHelper.P("@PharmacyId", pharmacyId),
                // .Date strips the time, so the range starts at midnight on the chosen day
                // rather than at whatever moment the date picker happened to carry.
                DbHelper.P("@FromDate", fromDate.Date),
                // The end of the chosen day, not its start. BETWEEN is inclusive of both
                // ends, so passing toDate.Date would cut the range at midnight and every
                // order placed during the final day would silently vanish from the grid.
                // OrderDate is DATETIME2(0), which stores whole seconds, so 23:59:59 is the
                // last instant that day can hold.
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)));
        }

        /// <summary>Everything the printable invoice needs, in one object.</summary>
        public Order GetOrderWithItems(int orderId)
        {
            const string header = @"
SELECT  o.OrderId, o.CustomerId, o.PharmacyId, o.OrderDate, o.ItemsTotal,
        -- CommissionAmount is read as it was frozen at checkout, never recomputed from
        -- the pharmacy's current rate, so a reprinted invoice matches the original.
        o.DeliveryCharge, o.TotalAmount, o.CommissionAmount, o.DeliveryAddress,
        o.PaymentMethod, o.Status,
        -- Aliased because Users and Pharmacies both carry names and contact columns;
        -- without the aliases the DataTable would end up with two columns called the
        -- same thing and the reader below could pick the wrong one.
        u.FullName AS CustomerName, u.Phone AS CustomerPhone,
        ph.PharmacyName, ph.Address AS PharmacyAddress, ph.LicenseNo AS PharmacyLicense
FROM    Orders o
        -- INNER on both sides: an order cannot exist without its customer or its
        -- pharmacy, because FK_Orders_Customer and FK_Orders_Pharmacy say so.
        INNER JOIN Users u       ON u.UserId       = o.CustomerId
        INNER JOIN Pharmacies ph ON ph.PharmacyId  = o.PharmacyId
WHERE   o.OrderId = @OrderId;";

            DataTable table = _db.ExecuteTable(header, DbHelper.P("@OrderId", orderId));
            // null means 'no such order', which the caller shows as a message. Returning an
            // empty Order instead would print a blank invoice and look like a real one.
            if (table.Rows.Count == 0) return null;

            DataRow row = table.Rows[0];      // OrderId is the primary key, so at most one
            Order order = new Order
            {
                OrderId = DbHelper.GetInt(row, "OrderId"),
                CustomerId = DbHelper.GetInt(row, "CustomerId"),
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                OrderDate = DbHelper.GetDate(row, "OrderDate"),
                // Every money column comes back as decimal through GetDecimal, which turns
                // DBNull into 0m, so a missing value prints as 0.00 rather than throwing
                // in the middle of building an invoice.
                ItemsTotal = DbHelper.GetDecimal(row, "ItemsTotal"),
                DeliveryCharge = DbHelper.GetDecimal(row, "DeliveryCharge"),
                // The stored computed column, copied as-is. Nothing in C# adds the two
                // numbers above together, so there is only ever one definition of a total.
                TotalAmount = DbHelper.GetDecimal(row, "TotalAmount"),
                CommissionAmount = DbHelper.GetDecimal(row, "CommissionAmount"),
                // The address as it was at the time of the order, from the Orders row
                // rather than from the customer's profile.
                DeliveryAddress = DbHelper.GetString(row, "DeliveryAddress"),
                PaymentMethod = DbHelper.GetString(row, "PaymentMethod"),
                Status = DbHelper.GetString(row, "Status"),
                // The five joined values, which turn this object into a complete invoice:
                // who it is for, and which licensed pharmacy issued it.
                CustomerName = DbHelper.GetString(row, "CustomerName"),
                CustomerPhone = DbHelper.GetString(row, "CustomerPhone"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                PharmacyAddress = DbHelper.GetString(row, "PharmacyAddress"),
                PharmacyLicense = DbHelper.GetString(row, "PharmacyLicense")
            };

            const string lines = @"
-- Subtotal is the PERSISTED computed column (Quantity * UnitPrice), so the line totals
-- on the invoice are the database's own arithmetic rather than a second calculation
-- that could round differently.
SELECT  oi.OrderItemId, oi.OrderId, oi.MedicineId, oi.Quantity, oi.UnitPrice, oi.Subtotal,
        -- Joined for display only. UnitPrice deliberately comes from OrderItems, not from
        -- Medicines, because it is the price as SOLD; the catalogue price may have
        -- changed many times since.
        m.MedicineName, m.Strength
FROM    OrderItems oi
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId
WHERE   oi.OrderId = @OrderId
-- A fixed order, so reprinting the same invoice twice produces identical paper.
ORDER BY m.MedicineName;";

            // A second query rather than one big join with the header. Joining would repeat
            // every header value once per line and invite a total to be summed twice.
            DataTable items = _db.ExecuteTable(lines, DbHelper.P("@OrderId", orderId));
            // Walk the rows once, turning each into an OrderItem hanging off the order.
            foreach (DataRow line in items.Rows)
            {
                // Items is initialised to an empty list in the model, never left null, so
                // this Add needs no guard even for an order whose lines failed to load.
                order.Items.Add(new OrderItem
                {
                    OrderItemId = DbHelper.GetInt(line, "OrderItemId"),
                    OrderId = DbHelper.GetInt(line, "OrderId"),
                    MedicineId = DbHelper.GetInt(line, "MedicineId"),
                    Quantity = DbHelper.GetInt(line, "Quantity"),
                    UnitPrice = DbHelper.GetDecimal(line, "UnitPrice"),
                    // Read, not multiplied: the database already holds Quantity * UnitPrice
                    // and copying it keeps one source of truth for the line total.
                    Subtotal = DbHelper.GetDecimal(line, "Subtotal"),
                    MedicineName = DbHelper.GetString(line, "MedicineName"),
                    Strength = DbHelper.GetString(line, "Strength")
                });
            }

            // One fully assembled object: header plus lines. InvoiceForm can print the
            // whole bill from this single variable without going back to the database.
            return order;
        }

        public DataTable GetOrderItems(int orderId)
        {
            const string sql = @"
-- The grid-binding version of the query above: only the columns a reader needs to see,
-- with no ids, because this DataTable is bound straight to a DataGridView.
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
        -- The owner needs to reach the customer about a delivery, which is why the
        -- contact columns are joined in rather than left to a second lookup.
        COUNT(oi.OrderItemId) AS Items, o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,
        o.PaymentMethod, o.Status, o.DeliveryAddress,
        -- A ready-made label for the queue, computed by the query so the form does not
        -- have to run a second question per row. It mirrors exactly the condition that
        -- Confirm() enforces below, so the badge and the button can never disagree.
        CASE WHEN EXISTS (SELECT 1 FROM Prescriptions p
                          -- Anything not yet Approved blocks: Pending and Rejected alike.
                          WHERE p.OrderId = o.OrderId AND p.VerifyStatus <> 'Approved')
             THEN 'Waiting on Rx' ELSE 'Clear' END AS RxState
FROM    Orders o
        INNER JOIN Users u       ON u.UserId    = o.CustomerId
        INNER JOIN OrderItems oi ON oi.OrderId  = o.OrderId
-- The isolation line. PharmacyId comes from UserSession, so an owner's dashboard can
-- only ever list orders placed with their own shop.
WHERE   o.PharmacyId = @PharmacyId
  AND   (@Status = '' OR o.Status = @Status)
GROUP BY o.OrderId, o.OrderDate, u.FullName, u.Phone, o.ItemsTotal, o.DeliveryCharge,
         o.TotalAmount, o.PaymentMethod, o.Status, o.DeliveryAddress
-- Newest first: the queue is worked from the top.
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
-- The literal target state. CK_Orders_Status allows only Placed, Confirmed, Delivered
-- and Cancelled, so the progression cannot be written into an invented value.
SET     Status = 'Confirmed'
WHERE   OrderId = @OrderId
  AND   PharmacyId = @PharmacyId              -- isolation: only your own orders
  -- Only a Placed order can become Confirmed. Naming the expected current status
  -- makes this update idempotent: pressing Confirm twice changes one row the first
  -- time and zero the second, instead of silently re-confirming.
  AND   Status = 'Placed'
  -- The prescription gate, enforced by the DATABASE rather than by a disabled button.
  -- NOT EXISTS returns true only when no prescription on this order is still waiting,
  -- so an order with a Pending or Rejected Rx cannot be dispatched even if the form
  -- were bypassed. The dashboard disables the button too, but that is a courtesy;
  -- THIS is the rule.
  AND   NOT EXISTS (SELECT 1 FROM Prescriptions p
                    WHERE p.OrderId = @OrderId AND p.VerifyStatus <> 'Approved');";

            // == 1 carries all four conditions at once: exactly one row changed means the
            // order existed, belonged to this shop, was still Placed and had no unapproved
            // prescription. 0 means one of those was false, and the form says so.
            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@OrderId", orderId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool MarkDelivered(int orderId, int pharmacyId)
        {
            return _db.ExecuteNonQuery(
                // "AND Status = 'Confirmed'" is what enforces the progression Placed ->
                // Confirmed -> Delivered. Without it an order could jump straight from
                // Placed to Delivered and skip the prescription check in Confirm()
                // altogether. PharmacyId keeps one shop out of another shop's queue.
                "UPDATE Orders SET Status = 'Delivered' WHERE OrderId = @OrderId AND PharmacyId = @PharmacyId AND Status = 'Confirmed';",
                DbHelper.P("@OrderId", orderId),
                // == 1 again: one row changed is the only outcome that counts as delivered.
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        /// <summary>
        /// Cancelling puts the stock back on the shelf, inside one transaction.
        ///
        /// Both statements are guarded by the same condition on purpose. An
        /// earlier version restocked whenever the order was not already
        /// cancelled but only flipped the status when it was not delivered, so
        /// cancelling a delivered order returned the units to stock and left the
        /// order reading Delivered. The eligibility test is now made once, in
        /// the database, and both statements sit behind it.
        /// </summary>
        public bool Cancel(int orderId, int pharmacyId)
        {
            // The transaction here is written in T-SQL rather than opened in C#, so the
            // whole batch still travels through DbHelper as a single command and its
            // errors are translated by DbHelper.Describe() like every other query.
            const string sql = @"
-- XACT_ABORT ON means any runtime error aborts the whole batch rather than leaving
-- a half applied transaction open. Belt and braces alongside the CATCH below.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    -- Initialised to 0 so the batch returns a definite 'nothing was cancelled' when the
    -- eligibility test below fails, instead of returning NULL.
    DECLARE @Cancelled INT = 0;

    -- THE ELIGIBILITY TEST, MADE ONCE. This is the fix for a real bug: an earlier
    -- version guarded the two statements separately, restocking whenever the order
    -- was not already Cancelled but only flipping the status when it was not
    -- Delivered. Cancelling a DELIVERED order therefore put the units back on the
    -- shelf and left the order still reading Delivered - free stock, silently.
    -- Testing once, here, means both statements share exactly one condition.
    IF EXISTS (SELECT 1 FROM Orders
               WHERE OrderId    = @OrderId
                 -- Ownership, as on every other write in this class.
                 AND PharmacyId = @PharmacyId
                 -- Delivered is too late to cancel and Cancelled is already done, so
                 -- both are excluded by the same NOT IN.
                 AND Status NOT IN ('Delivered', 'Cancelled'))
    BEGIN
        -- Put every unit on this order back. Joining OrderItems gives one row per
        -- line, so each medicine is credited with its own quantity.
        -- UQ_OrderItems_Line makes (OrderId, MedicineId) unique, which is what stops a
        -- medicine appearing twice on one order and being credited twice.
        UPDATE  m SET m.Stock = m.Stock + oi.Quantity
        FROM    Medicines m INNER JOIN OrderItems oi ON oi.MedicineId = m.MedicineId
        WHERE   oi.OrderId = @OrderId;

        -- The same conditions again, so the write cannot land on an order the test did
        -- not clear, and so the row count below means what it says.
        UPDATE  Orders SET Status = 'Cancelled'
        WHERE   OrderId    = @OrderId
          AND   PharmacyId = @PharmacyId
          AND   Status NOT IN ('Delivered', 'Cancelled');

        -- @@ROWCOUNT is read IMMEDIATELY after the UPDATE, because any later
        -- statement would overwrite it. 1 means the order really was cancelled.
        SET @Cancelled = @@ROWCOUNT;
    END

    -- The restock and the status change become visible together; neither can be seen
    -- on its own by another connection.
    COMMIT TRANSACTION;
    SELECT @Cancelled;              -- handed back to C# as the success flag
END TRY
BEGIN CATCH
    -- XACT_STATE() <> 0 means a transaction is still open and must be undone.
    -- Rolling back first and THEN rethrowing keeps the original error intact for
    -- DbHelper to translate, instead of masking it with a rollback failure.
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;";

            // ExecuteScalarInt, not ExecuteNonQuery: the rows affected would also count the
            // restocked medicines, so the batch computes its own flag and hands that back.
            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@OrderId", orderId),
                // == 1 means one order row moved to Cancelled. 0 means it was already
                // delivered, already cancelled, or not this pharmacy's to cancel.
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool OrderBelongsToCustomer(int orderId, int customerId)
        {
            // The ownership guard the invoice and review screens call before showing
            // anything, so typing another customer's order number into a dialog returns
            // false rather than somebody else's bill.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Orders WHERE OrderId = @OrderId AND CustomerId = @CustomerId;",
                DbHelper.P("@OrderId", orderId),
                // == 1 rather than > 0 because OrderId is the primary key: the count can
                // only ever be 0 or 1, and 1 is the single answer that means yes.
                DbHelper.P("@CustomerId", customerId)) == 1;
        }
    }
}
