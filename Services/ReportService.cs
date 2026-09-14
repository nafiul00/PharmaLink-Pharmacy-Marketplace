using System.Data;                  // DataTable and DataRow, the shape every report returns to a grid
using System.Text;                  // StringBuilder and Encoding, used only by the CSV export below
using PharmaLinkApp.Database;       // DbHelper, the only class in the project that opens a SqlConnection

namespace PharmaLinkApp.Services   // all reporting SQL lives here, never inside a Form
{
    /// <summary>Every reporting query in the application.</summary>
    public class ReportService   // read only, apart from ExportToCsv which writes a file
    {
        private readonly DbHelper _db = new DbHelper();   // one round trip per call, opened and closed

        // ===== EARNINGS AND COMMISSION: JOIN + GROUP BY + SUM + AVG + COUNT =====

        /// <summary>Earnings per shop; pass pharmacyId = 0 for every shop.</summary>
        public DataTable GetEarnings(int pharmacyId, DateTime fromDate, DateTime toDate, string area, string status)
        {
            // Five arguments are the whole filter set; 0 and "" are the neutral values.
            const string sql = @"
SELECT  ph.PharmacyId,               -- the key each row is identified by
        ph.PharmacyName,             -- the shop name, which is what the report is read by
        ph.Area,                     -- carried so the Super Admin can read the table by district
        ord.TotalOrders,             -- from the ORDER level subquery: one count per order
        itm.UnitsSold,               -- from the ITEM level subquery: units only exist per line
        itm.GrossSales,              -- what the lines really sold for, at their frozen prices
        ord.PlatformCommission,      -- the frozen per-order amount, counted once per order
        -- Net is computed in the query, so the CSV and the screen cannot disagree.
        CAST(itm.GrossSales - ord.PlatformCommission AS DECIMAL(12,2)) AS NetEarnings,
        itm.AverageItemPrice,        -- the average price of an item sold, taken per LINE
        -- The CURRENT rate, shown for context: the money above comes from frozen amounts.
        ph.CommissionRate
FROM    Pharmacies ph   -- one row per shop, narrowed by the two subqueries below
        -- DERIVED TABLE 1 of 2, ORDER LEVEL: CommissionAmount is stored per ORDER.
        INNER JOIN (SELECT  o.PharmacyId,   -- INNER: a shop with no orders has nothing to report
                            COUNT(*)                                       AS TotalOrders,   -- rows in Orders
                            -- CAST at the point of aggregation, so rounding happens once here.
                            CAST(SUM(o.CommissionAmount) AS DECIMAL(12,2)) AS PlatformCommission
                    FROM    Orders o   -- headers only; OrderItems is deliberately absent here
                    WHERE   o.Status <> 'Cancelled'        -- cancelled orders earn nothing
                      AND   o.OrderDate BETWEEN @FromDate AND @ToDate   -- the reporting window
                      AND   (@Status = '' OR o.Status = @Status)   -- the optional status filter
                    -- GROUP BY is what guarantees one row per shop out of this subquery.
                    GROUP BY o.PharmacyId) ord ON ord.PharmacyId = ph.PharmacyId
        -- DERIVED TABLE 2 of 2, LINE ITEM LEVEL: these three DO need OrderItems.
        INNER JOIN (SELECT  o.PharmacyId,   -- collapses to one row per shop, like the first
                            SUM(oi.Quantity)                          AS UnitsSold,   -- units per line
                            -- Subtotal is the discounted line total frozen at checkout.
                            CAST(SUM(oi.Subtotal) AS DECIMAL(12,2))   AS GrossSales,
                            -- AVG over LINES, deliberately not GrossSales / UnitsSold.
                            CAST(AVG(oi.UnitPrice) AS DECIMAL(10,2))  AS AverageItemPrice
                    FROM    Orders o   -- the same orders, read at a different grain
                            -- The join that causes the fan-out the first subquery avoids.
                            INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId
                    -- The same three filters repeated, or the halves would differ.
                    WHERE   o.Status <> 'Cancelled'
                      AND   o.OrderDate BETWEEN @FromDate AND @ToDate   -- the same window as above
                      AND   (@Status = '' OR o.Status = @Status)   -- and the same status filter
                    GROUP BY o.PharmacyId) itm ON itm.PharmacyId = ph.PharmacyId   -- one row per shop
-- The role switch: 0 is every shop, a real id one; it comes from the login.
WHERE   (@PharmacyId = 0  OR ph.PharmacyId = @PharmacyId)
  AND   (@Area       = '' OR ph.Area       = @Area)   -- the same optional-filter pattern, on Area
ORDER BY itm.GrossSales DESC;";   // biggest sellers first, so the list reads top down

            return _db.ExecuteTable(sql,   // one round trip, bound straight to the grid
                DbHelper.P("@PharmacyId", pharmacyId),   // 0 for the platform, an id for one owner
                // .Date pins the start to midnight, so orders earlier today are included.
                DbHelper.P("@FromDate", fromDate.Date),
                // The END of the last day: BETWEEN is inclusive, so midnight drops a day.
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)),
                DbHelper.P("@Area", area ?? ""),      // null would break the = '' test and return nothing
                DbHelper.P("@Status", status ?? ""));   // the same guard on the status filter
        }

        /// <summary>The detail behind the tiles, for one pharmacy only.</summary>
        public DataTable GetSalesDetail(int pharmacyId, DateTime fromDate, DateTime toDate, int medicineId)
        {
            // No aggregation at all, so the fan-out avoided above is exactly what is wanted.
            const string sql = @"
SELECT  o.OrderId, o.OrderDate, u.FullName AS Customer,   -- which order, when, and who bought
        -- Name and strength rather than MedicineId: the owner reads this as products.
        m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice, oi.Subtotal,
        o.PaymentMethod, o.Status   -- how it was paid for and where the order has reached
FROM    Orders o   -- one row per order line once OrderItems is joined below
        INNER JOIN OrderItems oi ON oi.OrderId    = o.OrderId     -- the lines of each order
        INNER JOIN Medicines  m  ON m.MedicineId  = oi.MedicineId -- resolves the product name
        INNER JOIN Users      u  ON u.UserId      = o.CustomerId  -- resolves the buyer's name
-- The isolation rule, with no 0 = everyone case: this report shows customer names.
WHERE   o.PharmacyId = @PharmacyId
  AND   o.Status    <> 'Cancelled'                  -- a cancelled order is not a sale
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate   -- the same window as the tiles
  -- The optional filter again: 0 is every medicine, a real id narrows to one.
  AND   (@MedicineId = 0 OR m.MedicineId = @MedicineId)
ORDER BY o.OrderDate DESC, o.OrderId;";   // newest first, with one order's lines together

            return _db.ExecuteTable(sql,   // one round trip, bound straight to dgvSales
                DbHelper.P("@PharmacyId", pharmacyId),   // from UserSession, never from a control
                DbHelper.P("@FromDate", fromDate.Date),   // midnight at the start of the range
                // The same whole-day end boundary as GetEarnings, so lines match tiles.
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)),
                DbHelper.P("@MedicineId", medicineId));   // 0 means every medicine
        }

        /// <summary>The four headline figures on the owner's earnings screen.</summary>
        public void GetEarningsTotals(int pharmacyId, DateTime fromDate, DateTime toDate, int medicineId,
                                      out decimal grossSales, out decimal commission,   // the money figures
                                      out decimal netEarnings, out int unitsSold)   // net, and the countable one
        {
            // Four out parameters: the caller wants several numbers from ONE round trip.
            const string sql = @"
-- ISNULL on every aggregate: SUM over no rows is NULL, so a quiet period tiles 0.
SELECT  ISNULL(SUM(oi.Subtotal), 0)            AS GrossSales,
        ISNULL(SUM(oi.Quantity), 0)            AS UnitsSold,   -- units only exist per line
        CASE WHEN @MedicineId = 0   -- the two ways commission has to be worked out
             -- ALL MEDICINES: a SCALAR SUBQUERY, as the join repeats orders per line.
             THEN ISNULL((SELECT SUM(o2.CommissionAmount)
                          FROM   Orders o2   -- a separate alias over the same table
                          -- o2 is its own scope, so the three filters must be repeated.
                          WHERE  o2.PharmacyId = @PharmacyId
                            AND  o2.Status <> 'Cancelled'   -- cancelled orders earn nothing
                            AND  o2.OrderDate BETWEEN @FromDate AND @ToDate), 0)   -- the same window
             -- ONE MEDICINE: a Subtotal / ItemsTotal share, NULLIF guarding a zero.
             ELSE ISNULL(CAST(SUM(oi.Subtotal * o.CommissionAmount / NULLIF(o.ItemsTotal, 0)) AS DECIMAL(12,2)), 0)
        END                                    AS Commission   -- one column either way
FROM    Orders o   -- the headers, for CommissionAmount and the filters
        INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId   -- for Subtotal and Quantity
WHERE   o.PharmacyId = @PharmacyId   -- the isolation rule, taken from the login
  AND   o.Status    <> 'Cancelled'   -- a cancelled order is not a sale
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate   -- the range chosen on the form
  -- The same optional medicine filter as the detail grid, so tiles and rows agree.
  AND   (@MedicineId = 0 OR oi.MedicineId = @MedicineId);";

            DataTable table = _db.ExecuteTable(sql,   // one round trip fills all four tiles
                DbHelper.P("@PharmacyId", pharmacyId),   // whose earnings are being totalled
                DbHelper.P("@FromDate", fromDate.Date),   // midnight at the start of the range
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)),   // whole-day end boundary
                DbHelper.P("@MedicineId", medicineId));   // 0 means every medicine

            // Seeded before the read, so every out parameter is assigned even with no rows.
            grossSales = 0m; commission = 0m; unitsSold = 0;
            if (table.Rows.Count > 0)   // a zeroed tile is the honest answer for no trading
            {
                // Row 0 is the only row this can produce: aggregates with no GROUP BY.
                grossSales = DbHelper.GetDecimal(table.Rows[0], "GrossSales");
                commission = DbHelper.GetDecimal(table.Rows[0], "Commission");   // by whichever branch ran
                unitsSold = DbHelper.GetInt(table.Rows[0], "UnitsSold");   // an int, not a money value
            }
            // Derived here rather than asked of the database: both operands were just read.
            netEarnings = grossSales - commission;
        }

        // ===== SUPER ADMIN REPORTS =====

        /// <summary>Shops whose average rating is below a threshold.</summary>
        public DataTable GetLowRatedPharmacies(decimal ratingThreshold, int minimumReviews)
        {
            // Both thresholds are arguments, so the list can be tightened without a rewrite.
            const string sql = @"
SELECT  ph.PharmacyId, ph.PharmacyName, ph.Area,   -- which shop, and where it trades
        -- The owner's name and phone come along, because this report is a decision.
        u.FullName AS OwnerName, u.Phone AS OwnerPhone,
        COUNT(r.ReviewId)                                          AS TotalReviews,   -- the sample size
        -- The inner CAST widens Rating BEFORE averaging, or 4+3+3 comes back as 3.
        CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2))  AS AverageRating,
        ph.Status                                                  -- so a suspended shop is not actioned twice
FROM    Pharmacies ph   -- the report is a list of shops, so Pharmacies drives it
        INNER JOIN Users     u ON u.UserId     = ph.OwnerId     -- the owner to contact
        -- Ratings are written against a MEDICINE, so a shop average costs two joins.
        INNER JOIN Medicines m ON m.PharmacyId = ph.PharmacyId
        INNER JOIN Reviews   r ON r.MedicineId = m.MedicineId   -- the ratings themselves
-- Filtered in WHERE, not HAVING, so moderated rows stay out of the average.
WHERE   r.IsHidden = 0
-- Every non-aggregated column must appear here; PharmacyId alone identifies it.
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.Area, u.FullName, u.Phone, ph.Status
-- HAVING, not WHERE: an average does not exist until the rows have been grouped.
HAVING  AVG(CAST(r.Rating AS DECIMAL(4,2))) < @Threshold
   -- The fairness rule: one angry customer averages 1.00 and would top the list.
   AND  COUNT(r.ReviewId) >= @MinReviews
ORDER BY AverageRating ASC;";   // worst first, so the list reads as a priority order

            return _db.ExecuteTable(sql,   // one round trip behind the whole report
                DbHelper.P("@Threshold", ratingThreshold),   // the average a shop must fall below
                DbHelper.P("@MinReviews", minimumReviews));   // and how many reviews it takes to count
        }

        /// <summary>Delivered revenue grouped by area, for expansion decisions.</summary>
        public DataTable GetRevenueByArea(decimal minimumRevenue)
        {
            // HAVING drops areas that have not crossed a meaningful revenue figure yet.
            const string sql = @"
SELECT  ph.Area,   -- the district, which is what this report groups by
        -- COUNT(DISTINCT) on both, because the join repeats a pharmacy once per order.
        COUNT(DISTINCT ph.PharmacyId) AS PharmaciesInArea,
        -- DISTINCT here too: harmless now, and correct for the same reason as above.
        COUNT(DISTINCT o.OrderId)     AS Orders,
        -- SUM over the ORDER header, and OrderItems is never touched, so no fan-out.
        SUM(o.TotalAmount)            AS Revenue,
        SUM(o.CommissionAmount)       AS CommissionEarned   -- what the platform itself made there
FROM    Pharmacies ph   -- shops, grouped up to their area below
        -- INNER JOIN, so an area with shops but no orders is absent rather than zero.
        INNER JOIN Orders o ON o.PharmacyId = ph.PharmacyId
WHERE   o.Status <> 'Cancelled'                 -- cancelled orders are not revenue
-- Grouped by Area, not by shop: which districts are worth expanding into.
GROUP BY ph.Area
-- HAVING once more, because the test is on the SUM; the cut-off is a parameter.
HAVING  SUM(o.TotalAmount) > @MinRevenue
ORDER BY Revenue DESC;";   // best performing area first

            return _db.ExecuteTable(sql, DbHelper.P("@MinRevenue", minimumRevenue));   // one filter, one trip
        }

        /// <summary>The six tiles on the Super Admin dashboard.</summary>
        public void GetPlatformTotals(out int pharmacies, out int pendingPharmacies, out int customers,
                                      out int orders, out decimal revenue, out decimal commission)   // six tiles, one trip
        {
            // Six scalar subqueries in ONE statement, so the dashboard costs one trip.
            const string sql = @"
-- Not joined: the figures come from unrelated tables with no key in common.
SELECT
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Approved')             AS ApprovedPharmacies,   -- live shops
    -- Counted separately rather than derived: Suspended is a third status.
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Pending')              AS PendingPharmacies,
    -- Users holds all three roles, so the type has to be named or owners would count.
    (SELECT COUNT(*) FROM Users      WHERE UserType = 'Customer')           AS Customers,
    (SELECT COUNT(*) FROM Orders     WHERE Status <> 'Cancelled')           AS Orders,   -- real orders only
    -- ISNULL on both money figures: SUM over no rows is NULL, not zero.
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE Status <> 'Cancelled') AS Revenue,
    -- The platform's own earnings, read from the frozen per-order amount.
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE Status <> 'Cancelled') AS Commission;";

            DataTable table = _db.ExecuteTable(sql);   // no parameters: this report is platform wide
            // Rows[0] is safe only here: scalar subqueries with no FROM return one row.
            DataRow row = table.Rows[0];

            pharmacies = DbHelper.GetInt(row, "ApprovedPharmacies");   // GetInt turns a null into 0
            pendingPharmacies = DbHelper.GetInt(row, "PendingPharmacies");   // the approval queue depth
            customers = DbHelper.GetInt(row, "Customers");   // registered buyers, not owners
            orders = DbHelper.GetInt(row, "Orders");   // every order that was not cancelled
            revenue = DbHelper.GetDecimal(row, "Revenue");   // the money that moved through the platform
            commission = DbHelper.GetDecimal(row, "Commission");   // and the share PharmaLink kept
        }

        /// <summary>The four tiles on the pharmacy owner's dashboard.</summary>
        public void GetPharmacyTotals(int pharmacyId, out int orders, out decimal revenue,
                                      out decimal commission, out int pendingOrders)   // the work still waiting
        {
            // The same one-statement shape, the isolation rule in every subquery.
            const string sql = @"
SELECT   -- four subqueries, all scoped to the one shop
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled')  AS Orders,   -- real orders
    -- @Id appears four times, because each subquery filters independently.
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled') AS Revenue,
    -- What the platform takes, shown here so the deduction is never a surprise later.
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled') AS Commission,
    -- 'Placed' is the first status in the lifecycle, so an equals test means 'to do'.
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id AND Status = 'Placed')      AS PendingOrders;";

            // One parameter reused by all four, another reason to send them as one statement.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", pharmacyId));
            DataRow row = table.Rows[0];   // again exactly one row, because there is no FROM clause

            orders = DbHelper.GetInt(row, "Orders");   // this shop's orders, cancelled ones excluded
            revenue = DbHelper.GetDecimal(row, "Revenue");   // what those orders were worth
            commission = DbHelper.GetDecimal(row, "Commission");   // the platform's share of them
            pendingOrders = DbHelper.GetInt(row, "PendingOrders");   // what is still waiting to be accepted
        }

        /// <summary>The best selling medicines, shown on both dashboards.</summary>
        public DataTable GetTopSellingMedicines(int pharmacyId, int topN)
        {
            // TOP (@TopN) with brackets is what lets the row count be a PARAMETER.
            const string sql = @"
-- Without them SQL Server accepts only a literal, forcing concatenation.
SELECT  TOP (@TopN)
        m.MedicineName, m.Strength, ph.PharmacyName,   -- what sold, and which shop listed it
        SUM(oi.Quantity) AS UnitsSold,   -- how many units left the shelf
        SUM(oi.Subtotal) AS Revenue        -- the money they brought in, at the frozen line price
FROM    OrderItems oi   -- lines drive it: 'what sold' is a question about lines
        INNER JOIN Orders     o  ON o.OrderId     = oi.OrderId     -- for the status filter
        INNER JOIN Medicines  m  ON m.MedicineId  = oi.MedicineId  -- for the product name
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId   -- for the shop name
WHERE   o.Status <> 'Cancelled'   -- a cancelled order sold nothing
  -- The role switch again: 0 gives the platform's best sellers, an id one shop's.
  AND   (@PharmacyId = 0 OR m.PharmacyId = @PharmacyId)
-- Grouped by name and strength, so the platform run adds a product across shops.
GROUP BY m.MedicineName, m.Strength, ph.PharmacyName
-- ORDER BY decides WHICH rows TOP keeps, so this line is part of the filter.
ORDER BY UnitsSold DESC;";

            return _db.ExecuteTable(sql,   // one round trip behind both dashboards
                DbHelper.P("@TopN", topN),   // how many rows to keep
                DbHelper.P("@PharmacyId", pharmacyId));   // 0 for the platform, an id for one shop
        }

        // ===== CSV EXPORT =====

        /// <summary>Writes any DataTable to a CSV file.</summary>
        public static bool ExportToCsv(DataTable table, string filePath, out string message)   // static: it holds no state
        {
            // Wrapped: writing a file fails for reasons outside the application entirely.
            try
            {
                // StringBuilder, because each += in a loop would copy the whole text again.
                StringBuilder builder = new StringBuilder();

                // The header comes from the DataTable's own column names, whatever they are.
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    builder.Append(Escape(table.Columns[i].ColumnName));   // quoted only if it needs it
                    // The comma goes BETWEEN fields: a trailing one adds an empty column.
                    if (i < table.Columns.Count - 1) builder.Append(',');
                }
                builder.AppendLine();   // ends the header row

                // Then one line per row, in the order the report produced them.
                foreach (DataRow row in table.Rows)
                {
                    // Indexed by position, so the values line up with the header above.
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        // DBNull tested explicitly: ToString() on it yields text, not a blank.
                        builder.Append(Escape(row[i] == DBNull.Value ? "" : row[i].ToString()));
                        if (i < table.Columns.Count - 1) builder.Append(',');   // between fields only
                    }
                    builder.AppendLine();   // ends this data row
                }

                // One write at the end, so the file is complete or never created; UTF8 named.
                File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
                // The message names the count and the path, so the caller can say where it went.
                message = "Exported " + table.Rows.Count + " row(s) to " + filePath;
                return true;   // true means the file is on disk
            }
            catch (Exception ex)   // Exception, not IOException alone
            {
                // ArgumentException or UnauthorizedAccessException: one failure to the user.
                message = ex.Message;   // handed back rather than swallowed, so the form can show it
                return false;   // false means nothing was written
            }
        }

        // The CSV quoting rule, in one place, applied to headers and values alike.
        private static string Escape(string value)
        {
            if (value == null) return "";   // a null column name or value becomes an empty field
            // Only these three force quoting: comma, newline and quote each break a field.
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                // CSV DOUBLES an embedded quote rather than escaping it with a backslash.
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            // Everything else as it stands: quoting every field works but reads worse.
            return value;
        }
    }
}
