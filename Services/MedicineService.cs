using System.Data;
using PharmaLinkApp.Database;
using PharmaLinkApp.Models;

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// The catalogue, from both sides of the marketplace.
    ///
    /// Every Admin side method takes a pharmacyId and puts it straight into the
    /// WHERE clause. That is the data isolation rule of the whole system: one
    /// pharmacy owner can never read, update or delete another owner's rows,
    /// and it is enforced in the query rather than by hiding buttons.
    /// </summary>
    public class MedicineService
    {
        private readonly DbHelper _db = new DbHelper();

        // =====================================================================
        //  CUSTOMER SIDE
        // =====================================================================

        /// <summary>
        /// Requirements 20, 21 and 22 in one statement: the keyword search over
        /// three columns plus the five ComboBox filters.
        ///
        /// Passing 0 or an empty string for a filter means "do not filter on
        /// this", which is why the customer can combine any subset of them.
        /// Only medicines belonging to an Approved pharmacy are ever returned,
        /// so a suspended shop's stock disappears from the catalogue without a
        /// single row being deleted.
        /// </summary>
        public DataTable SearchForCustomer(string keyword, int categoryId, decimal minPrice, decimal maxPrice,
                                           string area, int pharmacyId, bool inStockOnly)
        {
            const string sql = @"
SELECT  m.MedicineId,
        m.MedicineName,
        m.GenericName,
        m.Strength,
        m.Manufacturer,
        c.CategoryName,
        ph.PharmacyName,
        ph.Area,
        m.UnitPrice,
        ISNULL(d.Pct, 0)                                              AS DiscountPercent,
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct, 0) / 100.0) AS DECIMAL(10,2)) AS PriceYouPay,
        m.Stock,
        m.RequiresRx,
        m.ExpiryDate
FROM    Medicines m
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   m.IsActive   = 1
  AND   ph.Status    = 'Approved'
  AND   m.ExpiryDate > CAST(GETDATE() AS DATE)
  AND   (@Keyword    = ''  OR m.MedicineName LIKE '%' + @Keyword + '%'
                           OR m.GenericName  LIKE '%' + @Keyword + '%'
                           OR m.Manufacturer LIKE '%' + @Keyword + '%')
  AND   (@CategoryId = 0   OR m.CategoryId = @CategoryId)
  AND   (@MaxPrice   = 0   OR m.UnitPrice BETWEEN @MinPrice AND @MaxPrice)
  AND   (@Area       = ''  OR ph.Area = @Area)
  AND   (@PharmacyId = 0   OR ph.PharmacyId = @PharmacyId)
  AND   (@InStock    = 0   OR m.Stock > 0)
ORDER BY m.MedicineName, PriceYouPay;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@CategoryId", categoryId),
                DbHelper.P("@MinPrice", minPrice),
                DbHelper.P("@MaxPrice", maxPrice),
                DbHelper.P("@Area", area ?? ""),
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@InStock", inStockOnly ? 1 : 0));
        }

        /// <summary>Requirement 23: everything the details screen shows about one medicine.</summary>
        public Medicine GetDetails(int medicineId)
        {
            const string sql = @"
SELECT  m.MedicineId, m.PharmacyId, m.CategoryId, m.MedicineName, m.GenericName,
        m.Manufacturer, m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx,
        m.ExpiryDate, m.Description, m.ImagePath, m.IsActive,
        c.CategoryName, ph.PharmacyName, ph.Area,
        ISNULL(d.Pct, 0) AS DiscountPercent
FROM    Medicines m
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   m.MedicineId = @Id;";

            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", medicineId));
            if (table.Rows.Count == 0) return null;

            return MapMedicine(table.Rows[0]);
        }

        // =====================================================================
        //  PHARMACY OWNER SIDE  -  every query carries WHERE PharmacyId = @PharmacyId
        // =====================================================================

        /// <summary>Requirement 11, the READ of the CRUD. Own pharmacy only.</summary>
        public DataTable GetForPharmacy(int pharmacyId, string keyword, bool includeDelisted)
        {
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.GenericName, c.CategoryName, m.Manufacturer,
        m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx, m.ExpiryDate,
        m.IsActive,
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
WHERE   m.PharmacyId = @PharmacyId
  AND   (@IncludeDelisted = 1 OR m.IsActive = 1)
  AND   (@Keyword = '' OR m.MedicineName LIKE '%' + @Keyword + '%'
                       OR m.GenericName  LIKE '%' + @Keyword + '%')
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@IncludeDelisted", includeDelisted ? 1 : 0),
                DbHelper.P("@Keyword", keyword ?? ""));
        }

        /// <summary>Requirement 12, the CREATE. PharmacyId comes from the session, never from the form.</summary>
        public int Insert(Medicine medicine, int pharmacyId)
        {
            const string sql = @"
INSERT INTO Medicines (PharmacyId, CategoryId, MedicineName, GenericName, Manufacturer,
                       Strength, UnitPrice, Stock, MinStock, RequiresRx, ExpiryDate,
                       Description, ImagePath)
VALUES (@PharmacyId, @CategoryId, @Name, @Generic, @Manufacturer,
        @Strength, @UnitPrice, @Stock, @MinStock, @RequiresRx, @ExpiryDate,
        @Description, @ImagePath);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@CategoryId", medicine.CategoryId),
                DbHelper.P("@Name", medicine.MedicineName.Trim()),
                DbHelper.P("@Generic", medicine.GenericName.Trim()),
                DbHelper.P("@Manufacturer", medicine.Manufacturer.Trim()),
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(medicine.Strength) ? null : medicine.Strength.Trim()),
                DbHelper.P("@UnitPrice", medicine.UnitPrice),
                DbHelper.P("@Stock", medicine.Stock),
                DbHelper.P("@MinStock", medicine.MinStock),
                DbHelper.P("@RequiresRx", medicine.RequiresRx ? 1 : 0),
                DbHelper.P("@ExpiryDate", medicine.ExpiryDate.Date),
                DbHelper.P("@Description", medicine.Description),
                DbHelper.P("@ImagePath", string.IsNullOrWhiteSpace(medicine.ImagePath) ? null : medicine.ImagePath));
        }

        /// <summary>Requirement 11, the UPDATE. The PharmacyId in the WHERE clause is the isolation rule.</summary>
        public bool Update(Medicine medicine, int pharmacyId)
        {
            const string sql = @"
UPDATE  Medicines
SET     CategoryId = @CategoryId, MedicineName = @Name, GenericName = @Generic,
        Manufacturer = @Manufacturer, Strength = @Strength, UnitPrice = @UnitPrice,
        Stock = @Stock, MinStock = @MinStock, RequiresRx = @RequiresRx,
        ExpiryDate = @ExpiryDate, Description = @Description, ImagePath = @ImagePath
WHERE   MedicineId = @Id AND PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@CategoryId", medicine.CategoryId),
                DbHelper.P("@Name", medicine.MedicineName.Trim()),
                DbHelper.P("@Generic", medicine.GenericName.Trim()),
                DbHelper.P("@Manufacturer", medicine.Manufacturer.Trim()),
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(medicine.Strength) ? null : medicine.Strength.Trim()),
                DbHelper.P("@UnitPrice", medicine.UnitPrice),
                DbHelper.P("@Stock", medicine.Stock),
                DbHelper.P("@MinStock", medicine.MinStock),
                DbHelper.P("@RequiresRx", medicine.RequiresRx ? 1 : 0),
                DbHelper.P("@ExpiryDate", medicine.ExpiryDate.Date),
                DbHelper.P("@Description", medicine.Description),
                DbHelper.P("@ImagePath", string.IsNullOrWhiteSpace(medicine.ImagePath) ? null : medicine.ImagePath),
                DbHelper.P("@Id", medicine.MedicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        /// <summary>
        /// Requirement 11, the DELETE. This is a soft delete: setting IsActive to
        /// 0 keeps the foreign keys from OrderItems intact, so every old invoice
        /// still resolves, while the item disappears from the customer screens.
        /// </summary>
        public bool Delist(int medicineId, int pharmacyId)
        {
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET IsActive = 0 WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",
                DbHelper.P("@Id", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool Relist(int medicineId, int pharmacyId)
        {
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET IsActive = 1 WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",
                DbHelper.P("@Id", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool NameExistsInPharmacy(int pharmacyId, string name, string strength, int ignoreMedicineId)
        {
            return _db.ExecuteScalarInt(@"
SELECT COUNT(*) FROM Medicines
WHERE  PharmacyId = @PharmacyId
  AND  MedicineName = @Name
  AND  ISNULL(Strength, '') = ISNULL(@Strength, '')
  AND  MedicineId <> @Ignore;",
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@Name", name.Trim()),
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(strength) ? "" : strength.Trim()),
                DbHelper.P("@Ignore", ignoreMedicineId)) > 0;
        }

        /// <summary>Feeds the medicine ComboBox on the Discount Offers form.</summary>
        public List<Medicine> GetSimpleListForPharmacy(int pharmacyId)
        {
            List<Medicine> list = new List<Medicine>();
            DataTable table = _db.ExecuteTable(@"
SELECT  MedicineId, MedicineName, Strength, UnitPrice
FROM    Medicines
WHERE   PharmacyId = @PharmacyId AND IsActive = 1
ORDER BY MedicineName;",
                DbHelper.P("@PharmacyId", pharmacyId));

            foreach (DataRow row in table.Rows)
            {
                list.Add(new Medicine
                {
                    MedicineId = DbHelper.GetInt(row, "MedicineId"),
                    MedicineName = DbHelper.GetString(row, "MedicineName"),
                    Strength = DbHelper.GetString(row, "Strength"),
                    UnitPrice = DbHelper.GetDecimal(row, "UnitPrice")
                });
            }
            return list;
        }

        /// <summary>Loads one row for the Edit dialog, refusing to load another pharmacy's medicine.</summary>
        public Medicine GetForEdit(int medicineId, int pharmacyId)
        {
            const string sql = @"
SELECT  m.MedicineId, m.PharmacyId, m.CategoryId, m.MedicineName, m.GenericName,
        m.Manufacturer, m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx,
        m.ExpiryDate, m.Description, m.ImagePath, m.IsActive, c.CategoryName
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
WHERE   m.MedicineId = @Id AND m.PharmacyId = @PharmacyId;";

            DataTable table = _db.ExecuteTable(sql,
                DbHelper.P("@Id", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId));

            if (table.Rows.Count == 0) return null;
            return MapMedicine(table.Rows[0]);
        }

        // =====================================================================
        //  STOCK AND INVENTORY  (requirement 12 / 13)
        // =====================================================================

        /// <summary>
        /// The low stock alert. Comparing two columns of the same row is what
        /// makes it useful: ten boxes of a glucometer is plenty, ten strips of a
        /// painkiller is nothing. ShortfallUnits tells the owner how much to order.
        /// </summary>
        public DataTable GetLowStock(int pharmacyId)
        {
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,
        m.Stock, m.MinStock, (m.MinStock - m.Stock) AS ShortfallUnits
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
WHERE   m.PharmacyId = @PharmacyId
  AND   m.Stock      < m.MinStock
  AND   m.IsActive   = 1
ORDER BY ShortfallUnits DESC;";

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));
        }

        /// <summary>Units sold and units remaining for every medicine this pharmacy lists.</summary>
        public DataTable GetInventory(int pharmacyId)
        {
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,
        m.Stock                                     AS UnitsRemaining,
        ISNULL(sold.UnitsSold, 0)                   AS UnitsSold,
        m.MinStock,
        m.UnitPrice,
        CAST(m.Stock * m.UnitPrice AS DECIMAL(12,2)) AS StockValue,
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus,
        m.ExpiryDate
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
        LEFT JOIN (SELECT oi.MedicineId, SUM(oi.Quantity) AS UnitsSold
                   FROM   OrderItems oi
                          INNER JOIN Orders o ON o.OrderId = oi.OrderId
                   WHERE  o.Status <> 'Cancelled'
                   GROUP BY oi.MedicineId) sold ON sold.MedicineId = m.MedicineId
WHERE   m.PharmacyId = @PharmacyId AND m.IsActive = 1
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));
        }

        public int CountLowStock(int pharmacyId)
        {
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @Id AND Stock < MinStock AND IsActive = 1;",
                DbHelper.P("@Id", pharmacyId));
        }

        public int CountMedicines(int pharmacyId)
        {
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @Id AND IsActive = 1;",
                DbHelper.P("@Id", pharmacyId));
        }

        /// <summary>Used by the Restock button on the inventory screen.</summary>
        public bool AddStock(int medicineId, int pharmacyId, int unitsToAdd)
        {
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET Stock = Stock + @Units WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",
                DbHelper.P("@Units", unitsToAdd),
                DbHelper.P("@Id", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        // ---------------------------------------------------------------------

        private static Medicine MapMedicine(DataRow row)
        {
            return new Medicine
            {
                MedicineId = DbHelper.GetInt(row, "MedicineId"),
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                CategoryId = DbHelper.GetInt(row, "CategoryId"),
                MedicineName = DbHelper.GetString(row, "MedicineName"),
                GenericName = DbHelper.GetString(row, "GenericName"),
                Manufacturer = DbHelper.GetString(row, "Manufacturer"),
                Strength = DbHelper.GetString(row, "Strength"),
                UnitPrice = DbHelper.GetDecimal(row, "UnitPrice"),
                Stock = DbHelper.GetInt(row, "Stock"),
                MinStock = DbHelper.GetInt(row, "MinStock"),
                RequiresRx = DbHelper.GetBool(row, "RequiresRx"),
                ExpiryDate = DbHelper.GetDate(row, "ExpiryDate"),
                Description = DbHelper.GetString(row, "Description"),
                ImagePath = DbHelper.GetString(row, "ImagePath"),
                IsActive = DbHelper.GetBool(row, "IsActive"),
                CategoryName = DbHelper.GetString(row, "CategoryName"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                Area = DbHelper.GetString(row, "Area"),
                DiscountPercent = DbHelper.GetDecimal(row, "DiscountPercent")
            };
        }
    }
}
