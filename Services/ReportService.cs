using System.Data;                  // DataTable and DataRow, the shape every report returns to a grid
using System.Text;                  // StringBuilder and Encoding, used only by the CSV export at the bottom
using PharmaLinkApp.Database;       // DbHelper, the only class in the project that opens a SqlConnection

namespace PharmaLinkApp.Services
{
    // -------------------------------------------------------------------------
    //  Layer: service.  Called by AdminEarningsForm, SuperAdminSalesReportForm,
    //  SuperAdminLowRatedShopsForm, SuperAdminDashboard and AdminDashboard.
    //  All access via DbHelper; ExportToCsv writes a file and touches no table.
    //
    //  GetEarnings takes pharmacyId = 0 for the platform-wide report or a real
    //  PharmacyId for one owner, so a single query serves both roles.
    //
    //  Inside it, order-level and item-level figures are aggregated in two
    //  separate derived tables. Joining OrderItems multiplies the order rows, so
    //  summing the commission held on the order header in that same join would
    //  count it once per line item instead of once per order.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Every reporting query in the application.
    ///
    /// The interesting one is <see cref="GetEarnings"/>: the Super Admin runs it
    /// with pharmacyId = 0 and sees every shop, the pharmacy owner runs the same
    /// query with his own PharmacyId and sees only his own row. One query, two
    /// role scopes, which is the clearest demonstration of the data isolation
    /// rule in the system.
    /// </summary>
    public class ReportService
    {
        // The same single helper every other service holds. Reports are read-only, so this
        // class never opens a connection of its own or manages a transaction; each call below
        // is one round trip that DbHelper opens and closes.
        private readonly DbHelper _db = new DbHelper();

        // =====================================================================
        //  EARNINGS AND COMMISSION   (JOIN + GROUP BY + SUM + AVG + COUNT)
        // =====================================================================

        /// <summary>
        /// Requirements 5 and 13. Pass pharmacyId = 0 for the platform wide
        /// report, or a real PharmacyId for one owner's own report.
        ///
        /// Commission is the frozen Orders.CommissionAmount, the same figure the
        /// owner's own earnings tiles read, so the two screens can never disagree
        /// after a Super Admin changes a commission rate. Order level and item
        /// level figures are aggregated in separate derived tables because the
        /// join to OrderItems multiplies the order rows, which would inflate any
        /// SUM taken from the order header.
        /// </summary>
        // The five arguments are the report's whole filter set: who, over what period, in
        // which area and at which order status. pharmacyId = 0 and empty strings are the
        // neutral values, so the Super Admin's unfiltered run and the owner's own scoped run
        // are the same call with different values rather than two different methods.
        public DataTable GetEarnings(int pharmacyId, DateTime fromDate, DateTime toDate, string area, string status)
        {
            const string sql = @"
SELECT  ph.PharmacyId,
        ph.PharmacyName,
        ph.Area,                     -- carried so the Super Admin can read the table by district
        ord.TotalOrders,             -- from the ORDER level subquery: one count per order, not per line
        itm.UnitsSold,               -- from the ITEM level subquery: units only exist per line
        itm.GrossSales,
        ord.PlatformCommission,
        -- Net is worked out in the query rather than on the form, so the exported CSV carries
        -- the same figure the screen shows and no column has to be recomputed to reconcile it.
        CAST(itm.GrossSales - ord.PlatformCommission AS DECIMAL(12,2)) AS NetEarnings,
        itm.AverageItemPrice,
        -- The CURRENT rate, shown for context only. The money columns above come from the
        -- frozen per-order amounts, so changing this rate tomorrow does not move today's figures.
        ph.CommissionRate
FROM    Pharmacies ph
        -- DERIVED TABLE 1 of 2 - ORDER LEVEL.
        -- This is the whole point of the query and the sharpest thing in the project.
        -- CommissionAmount is stored ONCE, on the order header. If this SUM were taken
        -- in the same join as OrderItems, the order row would be duplicated once per
        -- line item and a three line order would have its commission counted three
        -- times - classic join fan-out, and the figures would silently be wrong rather
        -- than crash. Aggregating order level figures in their own subquery FIRST
        -- collapses each pharmacy to one row before anything else is joined to it.
        --
        -- INNER JOIN rather than LEFT: a shop with no orders in the period has nothing to
        -- report, so it is dropped here rather than appearing as a row of zeroes.
        INNER JOIN (SELECT  o.PharmacyId,
                            COUNT(*)                                       AS TotalOrders,   -- rows in Orders, so one per order
                            -- CAST to money precision at the point of aggregation, so rounding
                            -- happens once here instead of differently on each screen.
                            CAST(SUM(o.CommissionAmount) AS DECIMAL(12,2)) AS PlatformCommission
                    FROM    Orders o
                    WHERE   o.Status <> 'Cancelled'        -- cancelled orders earn nothing
                      AND   o.OrderDate BETWEEN @FromDate AND @ToDate
                      AND   (@Status = '' OR o.Status = @Status)
                    -- GROUP BY PharmacyId is what guarantees one row per shop coming out of
                    -- this subquery, which is the property the join below depends on.
                    GROUP BY o.PharmacyId) ord ON ord.PharmacyId = ph.PharmacyId
        -- DERIVED TABLE 2 of 2 - LINE ITEM LEVEL.
        -- These three figures genuinely need OrderItems, because units sold and gross
        -- sales only exist per line. Joining OrderItems is correct HERE and wrong in
        -- the block above; separating them is what lets both be right in one result.
        -- Each subquery also collapses to one row per pharmacy, so joining them to
        -- each other multiplies nothing.
        INNER JOIN (SELECT  o.PharmacyId,
                            SUM(oi.Quantity)                          AS UnitsSold,
                            -- Subtotal is the discounted line total frozen at checkout, so this
                            -- sum reports what was really charged, not today's shelf price.
                            CAST(SUM(oi.Subtotal) AS DECIMAL(12,2))   AS GrossSales,
                            -- AVG over LINES, which is the average price of an item sold; it is
                            -- deliberately not GrossSales / UnitsSold, a quantity weighted figure.
                            CAST(AVG(oi.UnitPrice) AS DECIMAL(10,2))  AS AverageItemPrice
                    FROM    Orders o
                            -- The join that causes the fan-out the first subquery avoids: one
                            -- order row becomes one row per line item here, which is exactly
                            -- what these three aggregates need and what a header SUM cannot survive.
                            INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId
                    -- The same three filters as above, deliberately repeated: each
                    -- subquery must see the same set of orders or the two halves of a
                    -- row would describe different date ranges.
                    WHERE   o.Status <> 'Cancelled'
                      AND   o.OrderDate BETWEEN @FromDate AND @ToDate
                      AND   (@Status = '' OR o.Status = @Status)
                    GROUP BY o.PharmacyId) itm ON itm.PharmacyId = ph.PharmacyId
-- The role switch, and the reason one query serves both screens: 0 means every shop for the
-- Super Admin, a real id means one shop for its owner. The value comes from UserSession, so
-- an owner's report cannot be widened by anything the form sends.
WHERE   (@PharmacyId = 0  OR ph.PharmacyId = @PharmacyId)
  AND   (@Area       = '' OR ph.Area       = @Area)   -- the same optional-filter pattern, on the area ComboBox
-- Biggest sellers first, because the platform-wide run is read top down to see who matters.
ORDER BY itm.GrossSales DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                // .Date pins the start to midnight, so a period that begins today includes
                // orders placed earlier this morning rather than only those after the moment
                // the form happened to be opened.
                DbHelper.P("@FromDate", fromDate.Date),
                // The end of the LAST day, not its midnight. BETWEEN is inclusive, so passing
                // toDate.Date would stop at 00:00:00 and silently drop every order placed
                // during the final day of the range. Adding a day and stepping back one second
                // is the whole-day boundary that avoids that off-by-one.
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)),
                DbHelper.P("@Area", area ?? ""),      // null would break the = '' test and return nothing
                DbHelper.P("@Status", status ?? ""));
        }

        /// <summary>
        /// Requirement 13, the detail behind the tiles: who bought what, on which
        /// date and at what price, for one pharmacy only.
        /// </summary>
        public DataTable GetSalesDetail(int pharmacyId, DateTime fromDate, DateTime toDate, int medicineId)
        {
            const string sql = @"
-- No aggregation at all here, which is why the fan-out that had to be avoided above is
-- wanted: this report is meant to show one row per line item.
SELECT  o.OrderId, o.OrderDate, u.FullName AS Customer,
        -- Name and strength rather than MedicineId, because the owner reads this as a list
        -- of products, and the frozen UnitPrice and Subtotal rather than today's price.
        m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice, oi.Subtotal,
        o.PaymentMethod, o.Status
FROM    Orders o
        INNER JOIN OrderItems oi ON oi.OrderId    = o.OrderId     -- the lines of each order
        INNER JOIN Medicines  m  ON m.MedicineId  = oi.MedicineId -- resolves the product name; works because Delist never removes the row
        INNER JOIN Users      u  ON u.UserId      = o.CustomerId  -- resolves the buyer's name from the single Users table
-- The isolation rule. Unlike GetEarnings there is no 0 = everyone case: this report shows
-- customer names, so it is scoped to one shop with no way to widen it.
WHERE   o.PharmacyId = @PharmacyId
  AND   o.Status    <> 'Cancelled'                  -- a cancelled order is not a sale
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate
  -- The optional filter again: 0 means every medicine, a real id narrows the report to one
  -- product without the form needing a second query.
  AND   (@MedicineId = 0 OR m.MedicineId = @MedicineId)
-- Newest first, then by order, so the lines of one order stay together on the screen.
ORDER BY o.OrderDate DESC, o.OrderId;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@FromDate", fromDate.Date),
                // The same whole-day end boundary as GetEarnings, so the detail rows add up to
                // the totals on the tiles instead of missing the final day.
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)),
                DbHelper.P("@MedicineId", medicineId));
        }

        /// <summary>The three headline figures on the pharmacy owner's earnings screen.</summary>
        // Four out parameters rather than a return value, because the caller wants several
        // numbers from ONE round trip. Returning a small object would work equally well; out
        // parameters keep the tile code a straight sequence of assignments with no extra type.
        public void GetEarningsTotals(int pharmacyId, DateTime fromDate, DateTime toDate,
                                      out decimal grossSales, out decimal commission,
                                      out decimal netEarnings, out int unitsSold)
        {
            const string sql = @"
-- ISNULL on every aggregate: SUM over no rows returns NULL, not 0, so a period with no
-- trading would otherwise hand the C# side a DBNull for each tile.
SELECT  ISNULL(SUM(oi.Subtotal), 0)            AS GrossSales,
        ISNULL(SUM(oi.Quantity), 0)            AS UnitsSold,
        -- Commission is taken in a SCALAR SUBQUERY, not from the join below, and for the same
        -- reason the earnings report uses two derived tables: the join to OrderItems repeats
        -- each order once per line, so summing the header's CommissionAmount there would count
        -- a three line order's commission three times. Read on its own over Orders, it is
        -- counted exactly once per order.
        ISNULL((SELECT SUM(o2.CommissionAmount)
                FROM   Orders o2
                -- o2 is a separate alias over the same table, so this subquery must repeat the
                -- three filters rather than inherit them; if it did not, the commission would
                -- cover a different set of orders from the sales figures beside it.
                WHERE  o2.PharmacyId = @PharmacyId
                  AND  o2.Status <> 'Cancelled'
                  AND  o2.OrderDate BETWEEN @FromDate AND @ToDate), 0) AS Commission
FROM    Orders o
        INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId   -- needed for Subtotal and Quantity, which only exist per line
WHERE   o.PharmacyId = @PharmacyId
  AND   o.Status    <> 'Cancelled'
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate;";

            DataTable table = _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@FromDate", fromDate.Date),
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)));   // whole-day end boundary, as above

            // Seeded before the read, so every out parameter is definitely assigned even on the
            // path where the query comes back with no rows at all. C# refuses to compile a
            // method that leaves an out parameter unset on any path, and a zeroed tile is the
            // honest answer for a period with no trading.
            grossSales = 0m; commission = 0m; unitsSold = 0;
            if (table.Rows.Count > 0)
            {
                // Row 0 is the only row this query can produce: it has aggregates and no GROUP BY.
                grossSales = DbHelper.GetDecimal(table.Rows[0], "GrossSales");
                commission = DbHelper.GetDecimal(table.Rows[0], "Commission");
                unitsSold = DbHelper.GetInt(table.Rows[0], "UnitsSold");
            }
            // Derived in C# rather than asked of the database, because both operands were just
            // read in this method and subtracting them here cannot disagree with the report.
            netEarnings = grossSales - commission;
        }

        // =====================================================================
        //  SUPER ADMIN REPORTS
        // =====================================================================

        /// <summary>
        /// Requirement 6. Ratings sit on medicines, not on pharmacies, so the
        /// average has to be built by joining three tables and grouping back up
        /// to the pharmacy. HAVING is the right clause because the condition is
        /// on the aggregate itself, and the second condition,
        /// COUNT(ReviewId) >= 2, is a deliberate fairness rule: one angry
        /// customer should not be enough to put a shop on the suspension list.
        /// </summary>
        // Both thresholds are arguments rather than constants, so the Super Admin can tighten
        // or loosen the list from the form without the query being rewritten.
        public DataTable GetLowRatedPharmacies(decimal ratingThreshold, int minimumReviews)
        {
            const string sql = @"
SELECT  ph.PharmacyId, ph.PharmacyName, ph.Area,
        -- The owner's name and phone number come along so the shop can be contacted straight
        -- from this screen; the point of the report is a decision, not a number.
        u.FullName AS OwnerName, u.Phone AS OwnerPhone,
        COUNT(r.ReviewId)                                          AS TotalReviews,
        -- The inner CAST widens the integer Rating BEFORE averaging. Without it SQL Server
        -- averages integers with integer arithmetic and 4 + 3 + 3 would come back as 3 rather
        -- than 3.33, which would push shops over and under the threshold incorrectly.
        CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2))  AS AverageRating,
        ph.Status                                                  -- so an already suspended shop is not actioned twice
FROM    Pharmacies ph
        INNER JOIN Users     u ON u.UserId     = ph.OwnerId     -- the owner to contact
        -- Ratings are written against a MEDICINE, so reaching a pharmacy average means going
        -- down to medicines and back up again. These two joins are what that costs.
        INNER JOIN Medicines m ON m.PharmacyId = ph.PharmacyId
        INNER JOIN Reviews   r ON r.MedicineId = m.MedicineId
-- Hidden reviews are moderated out, so a shop cannot be listed on the strength of abuse that
-- has already been taken down. Filtering here, in WHERE, keeps those rows out of the average
-- itself; putting the test in HAVING would average them in first and then judge the result.
WHERE   r.IsHidden = 0
-- Every non-aggregated column in the SELECT has to appear here. PharmacyId alone identifies
-- the shop, so the other five add no rows; they are listed because SQL requires it.
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.Area, u.FullName, u.Phone, ph.Status
-- HAVING, not WHERE: both conditions are about the GROUP, and an average does not exist until
-- the rows have been grouped. WHERE is evaluated before that and could not see either figure.
HAVING  AVG(CAST(r.Rating AS DECIMAL(4,2))) < @Threshold
   -- The fairness rule. A single one-star review gives a perfect average of 1.00, which would
   -- top this list on the strength of one bad day. Requiring a minimum number of reviews means
   -- the shops listed have a pattern behind them.
   AND  COUNT(r.ReviewId) >= @MinReviews
ORDER BY AverageRating ASC;";           // worst first, so the list reads as a priority order

            return _db.ExecuteTable(sql,
                DbHelper.P("@Threshold", ratingThreshold),
                DbHelper.P("@MinReviews", minimumReviews));
        }

        /// <summary>
        /// Which parts of the city are worth expanding into: delivered orders
        /// grouped by pharmacy area, with HAVING dropping areas that have not
        /// crossed a meaningful revenue figure yet.
        /// </summary>
        public DataTable GetRevenueByArea(decimal minimumRevenue)
        {
            const string sql = @"
SELECT  ph.Area,
        -- COUNT(DISTINCT ...) on both counts, because the join to Orders repeats each pharmacy
        -- once per order it has taken. A plain COUNT would report the number of orders twice
        -- over and claim an area has forty pharmacies when it has four.
        COUNT(DISTINCT ph.PharmacyId) AS PharmaciesInArea,
        -- DISTINCT here too: harmless while one order joins to one pharmacy row, and correct
        -- for the same reason the line above needs it.
        COUNT(DISTINCT o.OrderId)     AS Orders,
        -- SUM is taken over the ORDER header, and the query never touches OrderItems, so there
        -- is no fan-out to inflate these two totals.
        SUM(o.TotalAmount)            AS Revenue,
        SUM(o.CommissionAmount)       AS CommissionEarned   -- what the platform itself made in that area
FROM    Pharmacies ph
        -- INNER JOIN, so an area with registered shops but no orders yet is absent rather than
        -- shown as zero. This report is about where money is already moving.
        INNER JOIN Orders o ON o.PharmacyId = ph.PharmacyId
WHERE   o.Status <> 'Cancelled'                 -- cancelled orders are not revenue
-- Grouped by Area, not by pharmacy: the question is which districts are worth expanding into.
GROUP BY ph.Area
-- HAVING once more, because the test is on the SUM. The threshold is a parameter so the same
-- report can be run at different cut-offs without editing the query.
HAVING  SUM(o.TotalAmount) > @MinRevenue
ORDER BY Revenue DESC;";                        // best performing area first

            return _db.ExecuteTable(sql, DbHelper.P("@MinRevenue", minimumRevenue));
        }

        /// <summary>The four tiles on the Super Admin dashboard.</summary>
        public void GetPlatformTotals(out int pharmacies, out int pendingPharmacies, out int customers,
                                      out int orders, out decimal revenue, out decimal commission)
        {
            const string sql = @"
-- Six independent scalar subqueries in ONE statement, so the dashboard costs a single round
-- trip instead of six. They are deliberately not joined: the six figures come from three
-- unrelated tables and have no key in common, so a join would either multiply rows or need
-- cross joins to line them up.
SELECT
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Approved')             AS ApprovedPharmacies,
    -- Counted separately rather than derived from the total, because Suspended is a third
    -- status: approved plus pending does not account for every row.
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Pending')              AS PendingPharmacies,
    -- Users holds all three roles in one table, so the type has to be named explicitly or the
    -- count would include owners and the Super Admin.
    (SELECT COUNT(*) FROM Users      WHERE UserType = 'Customer')           AS Customers,
    (SELECT COUNT(*) FROM Orders     WHERE Status <> 'Cancelled')           AS Orders,
    -- ISNULL on both money figures: SUM over no rows is NULL, and a brand new database would
    -- otherwise show an empty tile rather than zero.
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE Status <> 'Cancelled') AS Revenue,
    -- The platform's own earnings, read from the frozen per-order amount rather than
    -- recalculated from today's commission rates.
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE Status <> 'Cancelled') AS Commission;";

            // No parameters: this report is platform wide by definition and has nothing to filter on.
            DataTable table = _db.ExecuteTable(sql);
            // Rows[0] without a count check is safe here and only here: a SELECT of scalar
            // subqueries with no FROM clause always returns exactly one row, even on an empty
            // database. The methods above that read from a table do check, because they can come
            // back with nothing.
            DataRow row = table.Rows[0];

            // Each out parameter is filled through the DbHelper helper for its type, so a null
            // arriving from the database becomes 0 rather than throwing on the conversion.
            pharmacies = DbHelper.GetInt(row, "ApprovedPharmacies");
            pendingPharmacies = DbHelper.GetInt(row, "PendingPharmacies");
            customers = DbHelper.GetInt(row, "Customers");
            orders = DbHelper.GetInt(row, "Orders");
            revenue = DbHelper.GetDecimal(row, "Revenue");
            commission = DbHelper.GetDecimal(row, "Commission");
        }

        /// <summary>The four tiles on the pharmacy owner's dashboard.</summary>
        public void GetPharmacyTotals(int pharmacyId, out int orders, out decimal revenue,
                                      out decimal commission, out int pendingOrders)
        {
            const string sql = @"
-- The same one-statement, four-subquery shape as the platform tiles, with the isolation rule
-- repeated in every subquery: @Id appears four times because each one filters independently.
SELECT
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled')  AS Orders,
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled') AS Revenue,
    -- What the platform takes, shown to the owner on his own dashboard so the deduction is
    -- visible rather than a surprise on the earnings screen.
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled') AS Commission,
    -- 'Placed' is the first status in the order lifecycle, so this counts work waiting to be
    -- accepted. It is an equals test, not <>, because only that one status means 'to do'.
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id AND Status = 'Placed')      AS PendingOrders;";

            // One parameter reused by all four subqueries, which is another reason to send them
            // as one statement rather than four calls.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", pharmacyId));
            DataRow row = table.Rows[0];   // again exactly one row, because there is no FROM clause

            orders = DbHelper.GetInt(row, "Orders");
            revenue = DbHelper.GetDecimal(row, "Revenue");
            commission = DbHelper.GetDecimal(row, "Commission");
            pendingOrders = DbHelper.GetInt(row, "PendingOrders");
        }

        /// <summary>The best selling medicines, shown on both dashboards.</summary>
        public DataTable GetTopSellingMedicines(int pharmacyId, int topN)
        {
            const string sql = @"
-- TOP (@TopN) with brackets, which is what lets the row count be a PARAMETER. Without them
-- SQL Server accepts only a literal, and the number would have to be concatenated into the
-- text - the one thing this project never does.
SELECT  TOP (@TopN)
        m.MedicineName, m.Strength, ph.PharmacyName,
        SUM(oi.Quantity) AS UnitsSold,
        SUM(oi.Subtotal) AS Revenue        -- the money the units actually brought in, at the frozen line price
FROM    OrderItems oi
        INNER JOIN Orders     o  ON o.OrderId     = oi.OrderId     -- for the status filter, which lives on the header
        INNER JOIN Medicines  m  ON m.MedicineId  = oi.MedicineId  -- for the product name
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId   -- for the shop name on the platform-wide run
WHERE   o.Status <> 'Cancelled'
  -- The role switch again: 0 gives the Super Admin the platform's best sellers, a real id
  -- gives one owner his own. Note it filters on m.PharmacyId, the shop that LISTS the
  -- medicine, which is the same shop the order was placed with.
  AND   (@PharmacyId = 0 OR m.PharmacyId = @PharmacyId)
-- Grouped by name and strength rather than by MedicineId, so the platform-wide run adds up
-- the same product across shops; PharmacyName is in the list because it is selected.
GROUP BY m.MedicineName, m.Strength, ph.PharmacyName
-- ORDER BY decides WHICH rows TOP keeps, so this line is part of the filter, not decoration.
ORDER BY UnitsSold DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@TopN", topN),
                DbHelper.P("@PharmacyId", pharmacyId));
        }

        // =====================================================================
        //  CSV EXPORT
        // =====================================================================

        /// <summary>
        /// Writes any DataTable to a CSV file so a report can be reconciled
        /// against bank settlements outside the application.
        /// </summary>
        // static, because it touches no field and no database: it works on whatever DataTable
        // it is handed, which is why every report on every screen can be exported by the same
        // method rather than each one growing its own writer.
        public static bool ExportToCsv(DataTable table, string filePath, out string message)
        {
            // The whole body is wrapped, because writing a file is the one operation in this
            // class that fails for reasons outside the application: the path may be read only,
            // the drive full, or the file already open in a spreadsheet.
            try
            {
                // StringBuilder rather than string concatenation in a loop: each += would copy
                // the whole text again, so a report of a few thousand rows would spend most of
                // its time reallocating strings.
                StringBuilder builder = new StringBuilder();

                // The header row is written from the DataTable's own column names, so the file
                // describes whichever report was passed in without this method knowing any of them.
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    builder.Append(Escape(table.Columns[i].ColumnName));
                    // The comma goes BETWEEN fields, never after the last one: a trailing comma
                    // would tell a spreadsheet there is one more, empty, column on every row.
                    if (i < table.Columns.Count - 1) builder.Append(',');
                }
                builder.AppendLine();

                // Then one line per row, in the order the report produced them, so the file
                // matches what was on screen.
                foreach (DataRow row in table.Rows)
                {
                    // Indexed by position rather than by name, so the values line up with the
                    // header written above whatever the columns happen to be called.
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        // DBNull has to be tested for explicitly: calling ToString() on it
                        // yields the literal text of the null placeholder rather than a blank
                        // cell, and an empty field is what a missing value means in CSV.
                        builder.Append(Escape(row[i] == DBNull.Value ? "" : row[i].ToString()));
                        if (i < table.Columns.Count - 1) builder.Append(',');
                    }
                    builder.AppendLine();
                }

                // One write at the end rather than a stream held open across the loop, so the
                // file is either complete or was never created. UTF8 is named explicitly so
                // that names outside the ASCII range survive the round trip into a spreadsheet.
                File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
                // The message names the row count and the path, so the caller can show where
                // the file went rather than only that something happened.
                message = "Exported " + table.Rows.Count + " row(s) to " + filePath;
                return true;
            }
            catch (Exception ex)
            {
                // Exception rather than IOException alone: an invalid path throws
                // ArgumentException and a protected folder throws UnauthorizedAccessException,
                // and to the person pressing Export all three are the same failure. The reason
                // is handed back through the out parameter instead of being swallowed, so the
                // form can show it.
                message = ex.Message;
                return false;
            }
        }

        // The CSV quoting rule, in one place, applied to headers and values alike.
        private static string Escape(string value)
        {
            if (value == null) return "";
            // Only these three characters force quoting. A comma would otherwise split one
            // field into two, a newline would split one row into two, and a quote would be read
            // as the start of a quoted field.
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                // The CSV convention doubles an embedded quote rather than escaping it with a
                // backslash, and the whole field is then wrapped in quotes of its own.
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            // Everything else is written as it stands: quoting every field would work but would
            // make the file harder to read and larger than it needs to be.
            return value;
        }
    }
}
