using System.Data;
using System.Text;
using PharmaLinkApp.Database;

namespace PharmaLinkApp.Services
{
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
        private readonly DbHelper _db = new DbHelper();

        // =====================================================================
        //  EARNINGS AND COMMISSION   (JOIN + GROUP BY + SUM + AVG + COUNT)
        // =====================================================================

        /// <summary>
        /// Requirements 5 and 13. Pass pharmacyId = 0 for the platform wide
        /// report, or a real PharmacyId for one owner's own report.
        ///
        /// Commission is recomputed from the pharmacy's rate rather than summed
        /// from Orders, because the join to OrderItems multiplies the order rows
        /// and a plain SUM of the header column would inflate it.
        /// </summary>
        public DataTable GetEarnings(int pharmacyId, DateTime fromDate, DateTime toDate, string area, string status)
        {
            const string sql = @"
SELECT  ph.PharmacyId,
        ph.PharmacyName,
        ph.Area,
        COUNT(DISTINCT o.OrderId)                                            AS TotalOrders,
        SUM(oi.Quantity)                                                     AS UnitsSold,
        SUM(oi.Subtotal)                                                     AS GrossSales,
        CAST(SUM(oi.Subtotal) * ph.CommissionRate / 100.0 AS DECIMAL(12,2))  AS PlatformCommission,
        CAST(SUM(oi.Subtotal) * (1 - ph.CommissionRate / 100.0) AS DECIMAL(12,2)) AS NetEarnings,
        CAST(AVG(oi.UnitPrice) AS DECIMAL(10,2))                             AS AverageItemPrice,
        ph.CommissionRate
FROM    Pharmacies ph
        INNER JOIN Orders     o  ON o.PharmacyId = ph.PharmacyId
        INNER JOIN OrderItems oi ON oi.OrderId   = o.OrderId
WHERE   o.Status <> 'Cancelled'
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate
  AND   (@PharmacyId = 0  OR ph.PharmacyId = @PharmacyId)
  AND   (@Area       = '' OR ph.Area       = @Area)
  AND   (@Status     = '' OR o.Status      = @Status)
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.Area, ph.CommissionRate
ORDER BY GrossSales DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@FromDate", fromDate.Date),
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)),
                DbHelper.P("@Area", area ?? ""),
                DbHelper.P("@Status", status ?? ""));
        }

        /// <summary>
        /// Requirement 13, the detail behind the tiles: who bought what, on which
        /// date and at what price, for one pharmacy only.
        /// </summary>
        public DataTable GetSalesDetail(int pharmacyId, DateTime fromDate, DateTime toDate, int medicineId)
        {
            const string sql = @"
SELECT  o.OrderId, o.OrderDate, u.FullName AS Customer,
        m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice, oi.Subtotal,
        o.PaymentMethod, o.Status
FROM    Orders o
        INNER JOIN OrderItems oi ON oi.OrderId    = o.OrderId
        INNER JOIN Medicines  m  ON m.MedicineId  = oi.MedicineId
        INNER JOIN Users      u  ON u.UserId      = o.CustomerId
WHERE   o.PharmacyId = @PharmacyId
  AND   o.Status    <> 'Cancelled'
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate
  AND   (@MedicineId = 0 OR m.MedicineId = @MedicineId)
ORDER BY o.OrderDate DESC, o.OrderId;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@FromDate", fromDate.Date),
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)),
                DbHelper.P("@MedicineId", medicineId));
        }

        /// <summary>The three headline figures on the pharmacy owner's earnings screen.</summary>
        public void GetEarningsTotals(int pharmacyId, DateTime fromDate, DateTime toDate,
                                      out decimal grossSales, out decimal commission,
                                      out decimal netEarnings, out int unitsSold)
        {
            const string sql = @"
SELECT  ISNULL(SUM(oi.Subtotal), 0)            AS GrossSales,
        ISNULL(SUM(oi.Quantity), 0)            AS UnitsSold,
        ISNULL((SELECT SUM(o2.CommissionAmount)
                FROM   Orders o2
                WHERE  o2.PharmacyId = @PharmacyId
                  AND  o2.Status <> 'Cancelled'
                  AND  o2.OrderDate BETWEEN @FromDate AND @ToDate), 0) AS Commission
FROM    Orders o
        INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId
WHERE   o.PharmacyId = @PharmacyId
  AND   o.Status    <> 'Cancelled'
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate;";

            DataTable table = _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@FromDate", fromDate.Date),
                DbHelper.P("@ToDate", toDate.Date.AddDays(1).AddSeconds(-1)));

            grossSales = 0m; commission = 0m; unitsSold = 0;
            if (table.Rows.Count > 0)
            {
                grossSales = DbHelper.GetDecimal(table.Rows[0], "GrossSales");
                commission = DbHelper.GetDecimal(table.Rows[0], "Commission");
                unitsSold = DbHelper.GetInt(table.Rows[0], "UnitsSold");
            }
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
        public DataTable GetLowRatedPharmacies(decimal ratingThreshold, int minimumReviews)
        {
            const string sql = @"
SELECT  ph.PharmacyId, ph.PharmacyName, ph.Area,
        u.FullName AS OwnerName, u.Phone AS OwnerPhone,
        COUNT(r.ReviewId)                                          AS TotalReviews,
        CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2))  AS AverageRating,
        ph.Status
FROM    Pharmacies ph
        INNER JOIN Users     u ON u.UserId     = ph.OwnerId
        INNER JOIN Medicines m ON m.PharmacyId = ph.PharmacyId
        INNER JOIN Reviews   r ON r.MedicineId = m.MedicineId
WHERE   r.IsHidden = 0
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.Area, u.FullName, u.Phone, ph.Status
HAVING  AVG(CAST(r.Rating AS DECIMAL(4,2))) < @Threshold
   AND  COUNT(r.ReviewId) >= @MinReviews
ORDER BY AverageRating ASC;";

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
        COUNT(DISTINCT ph.PharmacyId) AS PharmaciesInArea,
        COUNT(DISTINCT o.OrderId)     AS Orders,
        SUM(o.TotalAmount)            AS Revenue,
        SUM(o.CommissionAmount)       AS CommissionEarned
FROM    Pharmacies ph
        INNER JOIN Orders o ON o.PharmacyId = ph.PharmacyId
WHERE   o.Status <> 'Cancelled'
GROUP BY ph.Area
HAVING  SUM(o.TotalAmount) > @MinRevenue
ORDER BY Revenue DESC;";

            return _db.ExecuteTable(sql, DbHelper.P("@MinRevenue", minimumRevenue));
        }

        /// <summary>The four tiles on the Super Admin dashboard.</summary>
        public void GetPlatformTotals(out int pharmacies, out int pendingPharmacies, out int customers,
                                      out int orders, out decimal revenue, out decimal commission)
        {
            const string sql = @"
SELECT
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Approved')             AS ApprovedPharmacies,
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Pending')              AS PendingPharmacies,
    (SELECT COUNT(*) FROM Users      WHERE UserType = 'Customer')           AS Customers,
    (SELECT COUNT(*) FROM Orders     WHERE Status <> 'Cancelled')           AS Orders,
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE Status <> 'Cancelled') AS Revenue,
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE Status <> 'Cancelled') AS Commission;";

            DataTable table = _db.ExecuteTable(sql);
            DataRow row = table.Rows[0];

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
SELECT
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled')  AS Orders,
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled') AS Revenue,
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE PharmacyId = @Id AND Status <> 'Cancelled') AS Commission,
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @Id AND Status = 'Placed')      AS PendingOrders;";

            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", pharmacyId));
            DataRow row = table.Rows[0];

            orders = DbHelper.GetInt(row, "Orders");
            revenue = DbHelper.GetDecimal(row, "Revenue");
            commission = DbHelper.GetDecimal(row, "Commission");
            pendingOrders = DbHelper.GetInt(row, "PendingOrders");
        }

        /// <summary>The best selling medicines, shown on both dashboards.</summary>
        public DataTable GetTopSellingMedicines(int pharmacyId, int topN)
        {
            const string sql = @"
SELECT  TOP (@TopN)
        m.MedicineName, m.Strength, ph.PharmacyName,
        SUM(oi.Quantity) AS UnitsSold,
        SUM(oi.Subtotal) AS Revenue
FROM    OrderItems oi
        INNER JOIN Orders     o  ON o.OrderId     = oi.OrderId
        INNER JOIN Medicines  m  ON m.MedicineId  = oi.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   o.Status <> 'Cancelled'
  AND   (@PharmacyId = 0 OR m.PharmacyId = @PharmacyId)
GROUP BY m.MedicineName, m.Strength, ph.PharmacyName
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
        public static bool ExportToCsv(DataTable table, string filePath, out string message)
        {
            try
            {
                StringBuilder builder = new StringBuilder();

                for (int i = 0; i < table.Columns.Count; i++)
                {
                    builder.Append(Escape(table.Columns[i].ColumnName));
                    if (i < table.Columns.Count - 1) builder.Append(',');
                }
                builder.AppendLine();

                foreach (DataRow row in table.Rows)
                {
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        builder.Append(Escape(row[i] == DBNull.Value ? "" : row[i].ToString()));
                        if (i < table.Columns.Count - 1) builder.Append(',');
                    }
                    builder.AppendLine();
                }

                File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
                message = "Exported " + table.Rows.Count + " row(s) to " + filePath;
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static string Escape(string value)
        {
            if (value == null) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
