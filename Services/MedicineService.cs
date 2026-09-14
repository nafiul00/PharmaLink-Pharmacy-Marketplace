using System.Data;                  // DataTable and DataRow, the shape every read returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection
using PharmaLinkApp.Models;         // Medicine, the typed object a catalogue row becomes

// Services get their own namespace, which is what keeps SQL out of the forms.
namespace PharmaLinkApp.Services
{
    /// <summary>Catalogue reads; Admin queries filter on pharmacyId.</summary>
    public class MedicineService
    {
        // One helper per service; DbHelper opens and closes a connection per call.
        private readonly DbHelper _db = new DbHelper();

        // ===== CUSTOMER SIDE =====

        /// <summary>Keyword search on three columns plus five optional filters.</summary>
        public DataTable SearchForCustomer(string keyword, int categoryId, decimal minPrice, decimal maxPrice,
                                           string area, int pharmacyId, bool inStockOnly)   // 0 or '' means do not filter
        {
            // const, so it is clear nothing from the form is concatenated into the text.
            const string sql = @"
SELECT  m.MedicineId,                 -- so the grid can open the details screen for a row
        m.MedicineName,               -- the brand the customer typed, and the first ORDER BY
        m.GenericName,                -- the ingredient, which is what a prescription names
        m.Strength,                   -- 500mg and 665mg are separate rows at separate prices
        m.Manufacturer,               -- the third column the keyword below searches
        c.CategoryName,               -- the readable name, so the grid shows no raw keys
        ph.PharmacyName,              -- the customer buys from a named shop, not the platform
        ph.Area,                      -- lets the customer judge delivery distance
        m.UnitPrice,                  -- shelf price, kept so the grid can strike it through
        ISNULL(d.Pct, 0)                                              AS DiscountPercent,   -- turns 'no offer today' into 0
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct, 0) / 100.0) AS DECIMAL(10,2)) AS PriceYouPay,  -- 100.0 forces decimal division
        m.Stock,                      -- drives the out of stock badge and the quantity box
        m.RequiresRx,                 -- drives the prescription notice on the details screen
        m.ExpiryDate                  -- last column, so no comma; shows the shelf life left
FROM    Medicines m                   -- the driving table: one result row per medicine
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId       -- NOT NULL FK, so INNER cannot drop a row
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId       -- gives the shop name, and ph.Status below
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct              -- APPLY keeps the row when no offer exists
                     FROM   Offers o                                   -- read once per outer medicine row
                     WHERE  o.MedicineId = m.MedicineId                -- pins the MAX to this medicine
                       AND  o.IsActive   = 1          -- an offer the owner switched off must not apply
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d   -- MAX plus the window picks the best live offer
WHERE   m.IsActive   = 1                 -- a delisted medicine never reaches a customer
  AND   ph.Status    = 'Approved'          -- suspending a shop removes its whole shelf at once
  AND   m.ExpiryDate > CAST(GETDATE() AS DATE)   -- CAST drops the time, so the rule turns at midnight
  AND   (@Keyword    = ''  OR m.MedicineName LIKE '%' + @Keyword + '%'   -- '' short circuits the filter away
                           OR m.GenericName  LIKE '%' + @Keyword + '%'   -- wildcards go on the PARAMETER, not the text
                           OR m.Manufacturer LIKE '%' + @Keyword + '%')  -- the bracket keeps all three ORs in one AND
  AND   (@CategoryId = 0   OR m.CategoryId = @CategoryId)   -- 0 is safe: IDENTITY keys start at 1
  AND   (@MaxPrice   = 0   OR m.UnitPrice BETWEEN @MinPrice AND @MaxPrice)   -- guard on Max: 'under Tk 10' has a 0 minimum
  AND   (@Area       = ''  OR ph.Area = @Area)              -- exact match, because Area comes from a ComboBox
  AND   (@PharmacyId = 0   OR ph.PharmacyId = @PharmacyId)  -- pins the search to one shop, 0 meaning every shop
  AND   (@InStock    = 0   OR m.Stock > 0)                  -- the check box arrives as 1 or 0, keeping the pattern uniform
ORDER BY m.MedicineName, PriceYouPay;";   // cheapest first within a brand name

            // Every value travels as a SqlParameter, which is what makes the search box safe.
            return _db.ExecuteTable(sql,
                DbHelper.P("@Keyword", keyword ?? ""),   // null would become DBNull, and DBNull = '' is UNKNOWN
                DbHelper.P("@CategoryId", categoryId),   // 0 when the category ComboBox sits on its All entry
                DbHelper.P("@MinPrice", minPrice),       // the bottom of the band; 0 is a real value here
                DbHelper.P("@MaxPrice", maxPrice),       // 0 here IS the neutral value, hence the guard above
                DbHelper.P("@Area", area ?? ""),         // '' when the area ComboBox is on All
                DbHelper.P("@PharmacyId", pharmacyId),   // 0 unless the search was pinned to one shop
                DbHelper.P("@InStock", inStockOnly ? 1 : 0));   // converted here: SQL will not compare a bit with 0
        }

        /// <summary>Everything the details screen shows about one medicine.</summary>
        public Medicine GetDetails(int medicineId)
        {
            // A separate query from the search, so each screen carries only what it paints.
            const string sql = @"
SELECT  m.MedicineId, m.PharmacyId, m.CategoryId, m.MedicineName, m.GenericName,   -- the ids preselect an editor's ComboBoxes
        m.Manufacturer, m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx,   -- MinStock, because the editor shows it
        m.ExpiryDate, m.Description, m.ImagePath, m.IsActive,   -- IsActive, so a delisted row can be shown as delisted
        c.CategoryName, ph.PharmacyName, ph.Area,     -- the readable parents, so the screen shows names not ids
        ISNULL(d.Pct, 0) AS DiscountPercent           -- 0 means no live offer, hence no strike-through
FROM    Medicines m                                   -- the same driving table as the search
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId   -- NOT NULL FK, so the medicine cannot be lost
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId   -- the two seller facts this screen shows
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct          -- identical to the search, so prices cannot disagree
                     FROM   Offers o                               -- correlated to the outer row, which is what APPLY is for
                     WHERE  o.MedicineId = m.MedicineId            -- restricts the MAX to this one medicine
                       AND  o.IsActive   = 1                       -- a paused campaign must not move the price
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d   -- read every time, so no job starts an offer
WHERE   m.MedicineId = @Id;";   // the primary key alone, so this returns one row or none

            // ExecuteTable, so the SqlException translation stays in one place.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", medicineId));
            // No row means the id is gone; returning null lets the form say so.
            if (table.Rows.Count == 0) return null;

            // One shared mapper, so GetDetails and GetForEdit cannot drift apart.
            return MapMedicine(table.Rows[0]);
        }

        // ===== PHARMACY OWNER SIDE: every query carries PharmacyId =====

        /// <summary>The READ of the CRUD, for this owner's own shelf only.</summary>
        public DataTable GetForPharmacy(int pharmacyId, string keyword, bool includeDelisted)
        {
            // The owner's grid needs MinStock and IsActive, not the discounted price.
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.GenericName, c.CategoryName, m.Manufacturer,   -- one row per medicine on this shelf
        m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx, m.ExpiryDate,   -- MinStock is half of the test below
        m.IsActive,                   -- the grid greys delisted rows out rather than hiding them
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus   -- computed here, so every screen agrees
FROM    Medicines m                   -- no Pharmacies join: the WHERE already pinned the shop
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId   -- the only join this grid needs
WHERE   m.PharmacyId = @PharmacyId    -- the isolation rule; the value comes from UserSession
  AND   (@IncludeDelisted = 1 OR m.IsActive = 1)   -- the owner's own view is where delisted rows may show
  AND   (@Keyword = '' OR m.MedicineName LIKE '%' + @Keyword + '%'   -- the same neutral value trick as the search
                       OR m.GenericName  LIKE '%' + @Keyword + '%')  -- two columns: an owner looks up his own stock
ORDER BY m.MedicineName;";   // alphabetical, because this is a list to find one row in

            // All three values travel as parameters, exactly as in the customer search.
            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),                     // from UserSession, the only accepted source
                DbHelper.P("@IncludeDelisted", includeDelisted ? 1 : 0),   // bit to int, since the pattern compares with 1
                DbHelper.P("@Keyword", keyword ?? ""));                    // null would break the = '' test above
        }

        /// <summary>The CREATE; PharmacyId comes from the session, not the form.</summary>
        public int Insert(Medicine medicine, int pharmacyId)
        {
            // const again, so nothing the owner typed can reach the statement text.
            const string sql = @"
INSERT INTO Medicines (PharmacyId, CategoryId, MedicineName, GenericName, Manufacturer,   -- columns listed, so a new one cannot shift values
                       Strength, UnitPrice, Stock, MinStock, RequiresRx, ExpiryDate,   -- IsActive is absent: the column defaults to 1
                       Description, ImagePath)   -- the last two, both nullable, both normalised below
VALUES (@PharmacyId, @CategoryId, @Name, @Generic, @Manufacturer,   -- placeholders, never literals
        @Strength, @UnitPrice, @Stock, @MinStock, @RequiresRx, @ExpiryDate,   -- matching the column list one for one
        @Description, @ImagePath);   -- the semicolon ends the INSERT, so the SELECT is separate
SELECT CAST(SCOPE_IDENTITY() AS INT);";   // SCOPE_IDENTITY, not @@IDENTITY, which a trigger could hijack

            // ExecuteScalarInt reads the new key the SELECT above produced.
            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@PharmacyId", pharmacyId),   // the ARGUMENT, not the object: a tampered field must not refile stock
                DbHelper.P("@CategoryId", medicine.CategoryId),   // safe from the object: the FK rejects an invented id
                DbHelper.P("@Name", medicine.MedicineName.Trim()),   // trimmed, or ' Napa' would defeat the duplicate check
                DbHelper.P("@Generic", medicine.GenericName.Trim()),             // trimmed too: the search LIKEs on it
                DbHelper.P("@Manufacturer", medicine.Manufacturer.Trim()),       // the third searchable column
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(medicine.Strength) ? null : medicine.Strength.Trim()),   // blank stored as NULL, one shape only
                DbHelper.P("@UnitPrice", medicine.UnitPrice),   // decimal throughout; CK_Medicines_Price rejects a negative
                DbHelper.P("@Stock", medicine.Stock),         // opening quantity; CK_Medicines_Stock refuses a negative
                DbHelper.P("@MinStock", medicine.MinStock),   // the per product reorder line, which makes the alert mean something
                DbHelper.P("@RequiresRx", medicine.RequiresRx ? 1 : 0),   // bool to bit, written the same way everywhere here
                DbHelper.P("@ExpiryDate", medicine.ExpiryDate.Date),   // .Date drops the picker's time, so expiry is a date test
                DbHelper.P("@Description", medicine.Description),   // untrimmed: free prose, where spacing may be deliberate
                DbHelper.P("@ImagePath", string.IsNullOrWhiteSpace(medicine.ImagePath) ? null : medicine.ImagePath));   // NULL gives the loader one empty case
        }

        /// <summary>The UPDATE; PharmacyId in the WHERE is the isolation rule.</summary>
        public bool Update(Medicine medicine, int pharmacyId)
        {
            // const, so only the parameter values change between calls.
            const string sql = @"
UPDATE  Medicines   -- one statement: a SELECT first would leave a gap before the write
SET     CategoryId = @CategoryId, MedicineName = @Name, GenericName = @Generic,   -- PharmacyId is absent: stock cannot change shop
        Manufacturer = @Manufacturer, Strength = @Strength, UnitPrice = @UnitPrice,   -- every assignment takes a parameter
        Stock = @Stock, MinStock = @MinStock, RequiresRx = @RequiresRx,   -- an outright count; Restock adds instead
        ExpiryDate = @ExpiryDate, Description = @Description, ImagePath = @ImagePath   -- IsActive absent: Delist and Relist own it
WHERE   MedicineId = @Id AND PharmacyId = @PharmacyId;";   // two keys: which row, and that it is yours

            // ExecuteNonQuery hands back rows affected, which the == 1 below reads.
            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@CategoryId", medicine.CategoryId),   // a medicine may move category, so this one IS in the SET
                DbHelper.P("@Name", medicine.MedicineName.Trim()),   // the same Trim rules as Insert, so rows store alike
                DbHelper.P("@Generic", medicine.GenericName.Trim()),        // trimmed, because the search LIKEs on it
                DbHelper.P("@Manufacturer", medicine.Manufacturer.Trim()),  // trimmed: the third searchable column
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(medicine.Strength) ? null : medicine.Strength.Trim()),   // blank becomes NULL, as Insert stores it
                DbHelper.P("@UnitPrice", medicine.UnitPrice),               // future sales only; OrderItems froze past bills
                DbHelper.P("@Stock", medicine.Stock),                       // a correction, not AddStock's increment
                DbHelper.P("@MinStock", medicine.MinStock),                 // retunes what 'low' means for this product
                DbHelper.P("@RequiresRx", medicine.RequiresRx ? 1 : 0),     // bool to bit, as everywhere else here
                DbHelper.P("@ExpiryDate", medicine.ExpiryDate.Date),        // .Date keeps the expiry test date to date
                DbHelper.P("@Description", medicine.Description),           // free prose, passed through untrimmed
                DbHelper.P("@ImagePath", string.IsNullOrWhiteSpace(medicine.ImagePath) ? null : medicine.ImagePath),   // NULL, not '', for the image loader
                DbHelper.P("@Id", medicine.MedicineId),                     // which row to change
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;                // and the proof it is yours, from the session
            // == 1, not > 0: the WHERE names a primary key, so anything else is a failure.
        }

        /// <summary>A soft delete: IsActive = 0 keeps old invoices resolving.</summary>
        public bool Delist(int medicineId, int pharmacyId)
        {
            // An UPDATE, not a DELETE: OrderItems still points here, so the row must stay.
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET IsActive = 0 WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",   // two keys: which row, and whose
                DbHelper.P("@Id", medicineId),                 // which medicine to withdraw
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // exactly one row changed
        }

        // Same two arguments as Delist, in the same order, because it is the undo.
        public bool Relist(int medicineId, int pharmacyId)
        {
            // Undoing a soft delete is one flag flip, so the id, reviews and sales survive.
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET IsActive = 1 WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",   // identical to Delist apart from the 1
                DbHelper.P("@Id", medicineId),   // the withdrawn medicine, which kept its key
                // Relisting is a write too, so it carries the same two key WHERE.
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        // Called by the editor before saving, so a clash reads as a sentence.
        public bool NameExistsInPharmacy(int pharmacyId, string name, string strength, int ignoreMedicineId)
        {
            // ExecuteScalarInt: the answer is one number, counted inside the database.
            return _db.ExecuteScalarInt(@"
SELECT COUNT(*) FROM Medicines   -- COUNT, because the caller only wants 'any or none'
WHERE  PharmacyId = @PharmacyId  -- one shelf only: two shops may both stock Napa 500mg
  AND  MedicineName = @Name      -- exact, not LIKE: Napa must not clash with Napa Extra
  AND  ISNULL(Strength, '') = ISNULL(@Strength, '')   -- NULL = NULL is UNKNOWN, so both sides fold
  AND  MedicineId <> @Ignore;",   // the row being edited must not count as its own duplicate
                DbHelper.P("@PharmacyId", pharmacyId),           // the same session value every method here uses
                DbHelper.P("@Name", name.Trim()),                // trimmed to match what Insert and Update store
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(strength) ? "" : strength.Trim()),   // '' so the ISNULL has a value
                DbHelper.P("@Ignore", ignoreMedicineId)) > 0;    // COUNT > 0 means 'any', all the caller needs
        }

        /// <summary>Feeds the medicine ComboBox on the Discount Offers form.</summary>
        public List<Medicine> GetSimpleListForPharmacy(int pharmacyId)
        {
            // A typed list, because a ComboBox binds to objects rather than to columns.
            List<Medicine> list = new List<Medicine>();
            // Inline SQL: used once, and the parameter still travels separately.
            DataTable table = _db.ExecuteTable(@"
SELECT  MedicineId, MedicineName, Strength, UnitPrice   -- four columns: a dropdown needs no more
FROM    Medicines   -- no join at all, since the dropdown shows no category and no shop
WHERE   PharmacyId = @PharmacyId AND IsActive = 1   -- an offer cannot hang on a delisted medicine
ORDER BY MedicineName;",   // alphabetical, because a dropdown is scanned by eye
                DbHelper.P("@PharmacyId", pharmacyId));   // an owner attaches offers to his own stock only

            // One pass over the rows, turning each into the object the form will bind to.
            foreach (DataRow row in table.Rows)
            {
                // Object initialiser: built and filled in one expression, never half full.
                list.Add(new Medicine
                {
                    MedicineId = DbHelper.GetInt(row, "MedicineId"),   // the GetX helpers turn DBNull into an empty value
                    MedicineName = DbHelper.GetString(row, "MedicineName"),         // what the dropdown shows, via DisplayMember
                    Strength = DbHelper.GetString(row, "Strength"),                 // appended, so two strengths are distinguishable
                    UnitPrice = DbHelper.GetDecimal(row, "UnitPrice")                // shown beside the name while a discount is typed
                });
            }
            return list;   // bound straight to a ComboBox; an empty list gives an empty dropdown
        }

        /// <summary>Loads one row for the editor, refusing another shop's row.</summary>
        public Medicine GetForEdit(int medicineId, int pharmacyId)
        {
            // Like GetDetails minus the shop and the discount: an owner needs neither.
            const string sql = @"
SELECT  m.MedicineId, m.PharmacyId, m.CategoryId, m.MedicineName, m.GenericName,   -- the ids preselect the editor's ComboBoxes
        m.Manufacturer, m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx,   -- second line, wrapped for width only
        m.ExpiryDate, m.Description, m.ImagePath, m.IsActive, c.CategoryName   -- the name last, shown while the ComboBox loads
FROM    Medicines m   -- the driving table, as in every other query in this class
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId   -- no Pharmacies: the WHERE fixes the shop
WHERE   m.MedicineId = @Id AND m.PharmacyId = @PharmacyId;";   // isolation on a READ, not only on writes

            // One round trip; the result is one row or none, and the count is checked below.
            DataTable table = _db.ExecuteTable(sql,
                DbHelper.P("@Id", medicineId),           // the row the owner clicked in his grid
                DbHelper.P("@PharmacyId", pharmacyId));  // from UserSession, so the check cannot be bypassed

            // Empty means 'no such row' or 'not yours', deliberately indistinguishable.
            if (table.Rows.Count == 0) return null;
            // The same mapper: GetX checks Columns.Contains before it reads.
            return MapMedicine(table.Rows[0]);
        }

        // ===== STOCK AND INVENTORY =====

        /// <summary>The low stock alert, and how many units to reorder.</summary>
        public DataTable GetLowStock(int pharmacyId)
        {
            // const, like the rest; only the parameter differs between owners.
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,   -- what the row is, before the numbers
        m.Stock, m.MinStock, (m.MinStock - m.Stock) AS ShortfallUnits   -- the shortfall, so the restock box can prefill
FROM    Medicines m   -- the alert is about medicines; every other table here is decoration
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId   -- the category name, for grouping the list
WHERE   m.PharmacyId = @PharmacyId        -- the isolation rule: this shop only
  AND   m.Stock      < m.MinStock         -- TWO columns of one row, so 'low' is per product
  AND   m.IsActive   = 1                  -- a delisted medicine cannot be 'low'
ORDER BY ShortfallUnits DESC;";           // worst shortage first, so it reads as a to-do list

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));   // one parameter: the rule needs no configuring
        }

        /// <summary>Units sold and units left for every medicine this shop lists.</summary>
        public DataTable GetInventory(int pharmacyId)
        {
            // The widest query here: movement, value and expiry all on the same row.
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,   -- identity columns first
        m.Stock                                     AS UnitsRemaining,   -- aliased: 'Stock' reads oddly beside UnitsSold
        ISNULL(sold.UnitsSold, 0)                   AS UnitsSold,   -- 'never sold' becomes 0, so the column still sorts
        m.MinStock,                                                      -- so the grid shows the threshold beside the label
        m.UnitPrice,   -- needed on its own as well as inside StockValue below
        CAST(m.Stock * m.UnitPrice AS DECIMAL(12,2)) AS StockValue,   -- the money tied up in each line
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus,   -- the same rule as GetLowStock
        m.ExpiryDate                                                     -- stock about to expire is a loss still avoidable
FROM    Medicines m   -- drives the result, which is what makes 'never sold' a visible row
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId   -- for the category label only
        LEFT JOIN (SELECT oi.MedicineId, SUM(oi.Quantity) AS UnitsSold   -- LEFT, so slow moving stock still appears
                   FROM   OrderItems oi   -- the junction table, the only place unit counts live
                          INNER JOIN Orders o ON o.OrderId = oi.OrderId   -- the status lives on the order header
                   WHERE  o.Status <> 'Cancelled'   -- cancelled orders put their units back on the shelf
                   GROUP BY oi.MedicineId) sold ON sold.MedicineId = m.MedicineId   -- grouped BEFORE the join, so no row repeats
WHERE   m.PharmacyId = @PharmacyId AND m.IsActive = 1   -- isolation, plus: this screen is stock on sale
ORDER BY m.MedicineName;";   // a stocktaking list, walked down in order

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));   // everything else the screen shows is derived
        }

        // Returns an int, because the dashboard tile shows a single figure.
        public int CountLowStock(int pharmacyId)
        {
            // A COUNT is answered without sending any rows across, unlike GetLowStock.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @Id AND Stock < MinStock AND IsActive = 1;",   // the same rule as the grid
                DbHelper.P("@Id", pharmacyId));   // named @Id because this statement has only one parameter
        }

        // The companion tile to CountLowStock, so the dashboard can read '3 of 48 low'.
        public int CountMedicines(int pharmacyId)
        {
            // IsActive = 1, so the tile agrees with what the owner sees listed.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @Id AND IsActive = 1;",   // the same shape minus the threshold
                DbHelper.P("@Id", pharmacyId));   // the owner's shop, from the session
        }

        /// <summary>Used by the Restock button on the inventory screen.</summary>
        public bool AddStock(int medicineId, int pharmacyId, int unitsToAdd)
        {
            // Stock = Stock + @Units, so a sale made since the grid was drawn still counts.
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET Stock = Stock + @Units WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",   // read and write in one statement
                DbHelper.P("@Units", unitsToAdd),   // a delta, not a total, which is what makes it safe
                DbHelper.P("@Id", medicineId),      // the medicine being restocked
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // one row changed, or the id was not this shop's
        }

        // private static: it touches no field, and GetDetails and GetForEdit both call it.
        private static Medicine MapMedicine(DataRow row)
        {
            // Object initialiser, so no partly filled Medicine can be handed back early.
            return new Medicine
            {
                MedicineId = DbHelper.GetInt(row, "MedicineId"),   // every GetX checks the column exists before reading
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),   // the owning shop, already proved to match the session
                CategoryId = DbHelper.GetInt(row, "CategoryId"),   // kept so an editor can preselect the Category ComboBox
                MedicineName = DbHelper.GetString(row, "MedicineName"),   // the brand, the heading of the details screen
                GenericName = DbHelper.GetString(row, "GenericName"),     // the active ingredient, shown under the brand
                Manufacturer = DbHelper.GetString(row, "Manufacturer"),   // how two identically named products are told apart
                Strength = DbHelper.GetString(row, "Strength"),    // nullable, so the helper hands back '' rather than null
                UnitPrice = DbHelper.GetDecimal(row, "UnitPrice"), // decimal, never double: money carries no binary error
                Stock = DbHelper.GetInt(row, "Stock"),         // 0 becomes the out of stock badge on the details screen
                MinStock = DbHelper.GetInt(row, "MinStock"),   // what Medicine.IsLowStock compares Stock against
                RequiresRx = DbHelper.GetBool(row, "RequiresRx"),  // the bit becomes a bool a check box can bind to
                ExpiryDate = DbHelper.GetDate(row, "ExpiryDate"),         // a null becomes DateTime.MinValue, never a nullable
                Description = DbHelper.GetString(row, "Description"),     // nullable, so the screen prints nothing for ''
                ImagePath = DbHelper.GetString(row, "ImagePath"),         // '' means show the placeholder picture
                IsActive = DbHelper.GetBool(row, "IsActive"),             // false means delisted, which the editor can show
                CategoryName = DbHelper.GetString(row, "CategoryName"),   // the last four come from joins, not from Medicines
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),   // empty from GetForEdit, which joins no Pharmacies
                Area = DbHelper.GetString(row, "Area"),                   // same: only the customer query joins for it
                DiscountPercent = DbHelper.GetDecimal(row, "DiscountPercent")   // 0 from GetForEdit: an owner edits the shelf price
            };
        }
    }
}
