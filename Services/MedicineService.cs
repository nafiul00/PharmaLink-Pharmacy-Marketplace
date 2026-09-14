using System.Data;                  // DataTable and DataRow, the shape every read in this class returns
using PharmaLinkApp.Database;       // DbHelper, the only class in the project that opens a SqlConnection
using PharmaLinkApp.Models;         // Medicine, the typed object a catalogue row becomes

namespace PharmaLinkApp.Services
{
    // -------------------------------------------------------------------------
    //  Layer: service.  Called by AdminMedicineForm, MedicineEditorForm,
    //  AdminInventoryForm, AdminDashboard, AdminEarningsForm, CustomerHomeForm,
    //  MedicineDetailsForm and DiscountOffersForm. All access via DbHelper.
    //
    //  Customer-side reads filter on m.IsActive = 1 and ph.Status = 'Approved',
    //  so a suspended shop's stock leaves the catalogue without a row being
    //  deleted. Owner-side reads and writes all carry
    //  WHERE PharmacyId = @PharmacyId, taken from UserSession.
    //
    //  Delist sets IsActive = 0 rather than deleting the row, so the OrderItems
    //  foreign keys on past invoices keep resolving.
    // -------------------------------------------------------------------------

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
        // One helper per service instance, exactly as every other service does it. The
        // helper holds no open connection of its own: each call inside DbHelper opens a
        // connection, runs the statement and closes it again, so this single field can be
        // shared by every method below without two calls ever fighting over one connection.
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
        // Seven arguments arrive from seven controls on CustomerHomeForm: one text box and
        // five ComboBoxes plus a check box. None of them is optional at the C# level, because
        // the "not filtering" case is expressed as a neutral value (0 or "") rather than as a
        // null or an overload. One method signature therefore serves every combination the
        // form can produce, and the query below decides what each neutral value means.
        public DataTable SearchForCustomer(string keyword, int categoryId, decimal minPrice, decimal maxPrice,
                                           string area, int pharmacyId, bool inStockOnly)
        {
            // const rather than a local variable: the text never changes at runtime, so the
            // compiler places it in the assembly once. It is also proof at a glance that
            // nothing is concatenated into this string, which is what keeps it injection safe.
            const string sql = @"
SELECT  m.MedicineId,                 -- carried so the grid can open the details screen for the clicked row
        m.MedicineName,
        m.GenericName,                -- shown next to the brand, because a prescription usually names this one
        m.Strength,
        m.Manufacturer,
        c.CategoryName,               -- the readable name, not CategoryId: the grid must not show raw keys
        ph.PharmacyName,              -- the customer is buying from a named shop, not from the platform
        ph.Area,                      -- lets the customer judge delivery distance without opening the row
        m.UnitPrice,                  -- the shelf price, kept alongside the discounted one so the grid can strike it through
        ISNULL(d.Pct, 0)                                              AS DiscountPercent,
        -- The price actually charged, worked out HERE rather than in C#, so the search grid,
        -- the details screen, the cart and the invoice all inherit the same arithmetic and the
        -- same rounding. 100.0 rather than 100 forces decimal division: with an integer 100,
        -- SQL Server would floor 10/100 to 0 and every discounted item would sell at full price.
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct, 0) / 100.0) AS DECIMAL(10,2)) AS PriceYouPay,
        m.Stock,                      -- drives the Out of stock badge and the quantity box maximum
        m.RequiresRx,                 -- drives the prescription notice on the details screen
        m.ExpiryDate                  -- shown so the customer can see how much shelf life is left
FROM    Medicines m
        -- INNER JOIN, not LEFT: CategoryId and PharmacyId are NOT NULL foreign keys, so every
        -- medicine has exactly one of each and neither join can drop a row. A LEFT JOIN here
        -- would suggest a missing parent is possible and cost a needless null check per column.
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        -- OUTER APPLY, not a JOIN to Offers: it runs this subquery once per medicine row and
        -- still returns the row when there is no offer, giving NULL which ISNULL turns into 0.
        -- An INNER JOIN to Offers would silently show only discounted medicines; a LEFT JOIN
        -- would duplicate a medicine that has two overlapping offers running at once.
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1          -- an offer the owner switched off must not apply
                       -- MAX plus this date window means overlapping offers resolve to the best
                       -- one for the customer, and an offer that has ended cannot be picked up.
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
-- The three safety rules first. These are NOT optional filters - they always apply,
-- which is why a delisted medicine, a suspended shop's stock and expired stock can
-- never reach a customer screen no matter what the form sends.
WHERE   m.IsActive   = 1
  AND   ph.Status    = 'Approved'          -- this is why Pending New Life is invisible
  AND   m.ExpiryDate > CAST(GETDATE() AS DATE)
  -- Now the five optional filters. Every one uses the same trick:
  --      (@Param = <empty value> OR <real condition>)
  -- If the parameter is 0 or an empty string the left side is true, the OR
  -- short circuits and the filter does nothing. That single pattern is what lets
  -- the customer combine ANY subset of the five without the application having to
  -- build different SQL for each combination - one query, 32 possible behaviours.
  --
  -- The alternative, building the WHERE clause in C# by appending strings, is what
  -- this pattern exists to avoid: it would put customer typing into the query text
  -- and reopen the injection hole that parameters close.
  --
  -- The keyword searches THREE columns, so a prescription saying 'paracetamol'
  -- finds Napa and Ace Plus even though neither brand contains that word.
  AND   (@Keyword    = ''  OR m.MedicineName LIKE '%' + @Keyword + '%'
                           -- The wildcards are concatenated onto the PARAMETER inside the
                           -- query, never onto the SQL text in C#, so a customer typing a
                           -- quote or a semicolon is still only ever compared as literal text.
                           OR m.GenericName  LIKE '%' + @Keyword + '%'
                           OR m.Manufacturer LIKE '%' + @Keyword + '%')
  AND   (@CategoryId = 0   OR m.CategoryId = @CategoryId)   -- 0 is safe as the neutral value because IDENTITY keys start at 1
  -- Note the guard is on @MaxPrice, not @MinPrice: 'under Tk 10' is a legitimate
  -- range with a minimum of 0, so testing @MinPrice would disable that filter.
  AND   (@MaxPrice   = 0   OR m.UnitPrice BETWEEN @MinPrice AND @MaxPrice)
  AND   (@Area       = ''  OR ph.Area = @Area)              -- exact match, because Area is chosen from a ComboBox, not typed
  AND   (@PharmacyId = 0   OR ph.PharmacyId = @PharmacyId)
  -- The check box arrives as 1 or 0 rather than as a bit comparison, so the neutral
  -- value reads the same as the other four filters and the pattern stays uniform.
  AND   (@InStock    = 0   OR m.Stock > 0)
-- Cheapest first WITHIN a brand name, so the same medicine from three pharmacies
-- lists with the best price at the top.
ORDER BY m.MedicineName, PriceYouPay;";

            // Every value travels as a SqlParameter. ExecuteTable adds them to the command and
            // sends them separately from the text above, which is what makes the search box safe.
            return _db.ExecuteTable(sql,
                // ?? "" rather than passing null: a null would become DBNull inside DbHelper.P,
                // and DBNull = '' is UNKNOWN in SQL, not true, so the whole optional-filter test
                // would fail and an empty search box would return nothing at all.
                DbHelper.P("@Keyword", keyword ?? ""),
                DbHelper.P("@CategoryId", categoryId),
                DbHelper.P("@MinPrice", minPrice),
                DbHelper.P("@MaxPrice", maxPrice),
                DbHelper.P("@Area", area ?? ""),
                DbHelper.P("@PharmacyId", pharmacyId),
                // The bool is converted here rather than in the query, because SQL Server will
                // not compare a bit parameter with the integer 0 in the pattern used above.
                DbHelper.P("@InStock", inStockOnly ? 1 : 0));
        }

        /// <summary>Requirement 23: everything the details screen shows about one medicine.</summary>
        public Medicine GetDetails(int medicineId)
        {
            const string sql = @"
-- The full column list, unlike the search above: the details screen shows Description and
-- the image, and MapMedicine needs PharmacyId and CategoryId to pre-select the ComboBoxes
-- when the same object is handed to an editor.
SELECT  m.MedicineId, m.PharmacyId, m.CategoryId, m.MedicineName, m.GenericName,
        m.Manufacturer, m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx,
        m.ExpiryDate, m.Description, m.ImagePath, m.IsActive,
        c.CategoryName, ph.PharmacyName, ph.Area,     -- the readable parents, so the screen shows names rather than ids
        ISNULL(d.Pct, 0) AS DiscountPercent           -- 0 means no live offer, which the screen renders as no strike-through
FROM    Medicines m
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        -- The same OUTER APPLY as the search query, deliberately identical, so the price the
        -- details screen shows can never disagree with the price on the row the customer clicked.
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
-- The primary key alone, so this returns one row or none.
WHERE   m.MedicineId = @Id;";

            // ExecuteTable rather than a reader: the result is a single row, and using the same
            // helper as every other read keeps the SqlException translation in one place.
            DataTable table = _db.ExecuteTable(sql, DbHelper.P("@Id", medicineId));
            // No row means the id does not exist, which happens when a grid was left open while
            // the row was removed. Returning null lets the form say so; indexing Rows[0] here
            // would instead throw IndexOutOfRangeException from inside the data layer.
            if (table.Rows.Count == 0) return null;

            // One shared mapper, so the object this returns has exactly the same shape as the
            // one GetForEdit returns and neither method drifts when a column is added.
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
        m.IsActive,                   -- the owner's grid shows delisted rows greyed out rather than hiding them
        -- The label is computed in the query, not in a CellFormatting event, so the grid can
        -- bind to it directly and the same rule produces the same word on every screen.
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
-- The isolation rule, and the reason this method takes a pharmacyId at all: the value comes
-- from UserSession, never from the form, so one owner's grid cannot be made to list another
-- owner's stock by tampering with a control.
WHERE   m.PharmacyId = @PharmacyId
  -- The owner's own view is the one place delisted rows are allowed to appear, because he
  -- needs to see them to press Relist. The default is 1 = show only live rows.
  AND   (@IncludeDelisted = 1 OR m.IsActive = 1)
  -- The same optional-filter pattern as the customer search, over two columns this time:
  -- an owner looks up his own stock by brand or by generic name, not by manufacturer.
  AND   (@Keyword = '' OR m.MedicineName LIKE '%' + @Keyword + '%'
                       OR m.GenericName  LIKE '%' + @Keyword + '%')
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@IncludeDelisted", includeDelisted ? 1 : 0),   // bit converted to int, as the pattern compares against 1
                DbHelper.P("@Keyword", keyword ?? ""));                    // null would break the = '' test, as above
        }

        /// <summary>Requirement 12, the CREATE. PharmacyId comes from the session, never from the form.</summary>
        public int Insert(Medicine medicine, int pharmacyId)
        {
            const string sql = @"
-- The column list is written out in full rather than relying on table order, so adding a
-- column to Medicines later cannot silently shift every value into the wrong field.
INSERT INTO Medicines (PharmacyId, CategoryId, MedicineName, GenericName, Manufacturer,
                       Strength, UnitPrice, Stock, MinStock, RequiresRx, ExpiryDate,
                       Description, ImagePath)
VALUES (@PharmacyId, @CategoryId, @Name, @Generic, @Manufacturer,
        @Strength, @UnitPrice, @Stock, @MinStock, @RequiresRx, @ExpiryDate,
        @Description, @ImagePath);
-- Second statement in the same batch, so the new key comes back on the same round trip and
-- without a second lookup by name, which could match the wrong row. SCOPE_IDENTITY rather
-- than @@IDENTITY: @@IDENTITY would return a key generated by a trigger on another table.
-- The CAST is here because SCOPE_IDENTITY returns NUMERIC(38,0), which Convert.ToInt32
-- would have to narrow on the C# side.
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            // ExecuteScalarInt reads the first column of the first row, which is the id produced
            // by the SELECT above, and returns 0 if the value came back null.
            return _db.ExecuteScalarInt(sql,
                // Taken from the ARGUMENT, not from medicine.PharmacyId: the object was built by
                // a form, so trusting its PharmacyId would let a tampered field file stock under
                // another shop. The session value is the only accepted source.
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@CategoryId", medicine.CategoryId),
                // Trim on every free-text field, so ' Napa' and 'Napa' cannot both exist and
                // defeat the duplicate check in NameExistsInPharmacy, which compares exactly.
                DbHelper.P("@Name", medicine.MedicineName.Trim()),
                DbHelper.P("@Generic", medicine.GenericName.Trim()),
                DbHelper.P("@Manufacturer", medicine.Manufacturer.Trim()),
                // Strength is optional, so blank is stored as NULL rather than as an empty
                // string. One representation of "not supplied" means the ISNULL comparison in
                // NameExistsInPharmacy has only one case to handle instead of two.
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(medicine.Strength) ? null : medicine.Strength.Trim()),
                DbHelper.P("@UnitPrice", medicine.UnitPrice),   // decimal all the way through; CK_Medicines_Price rejects a negative
                DbHelper.P("@Stock", medicine.Stock),
                DbHelper.P("@MinStock", medicine.MinStock),
                // The bit column takes 1 or 0. Passing the bool itself works, but converting here
                // keeps every boolean in this class written the same way.
                DbHelper.P("@RequiresRx", medicine.RequiresRx ? 1 : 0),
                // .Date strips the time component a DateTimePicker carries, so the expiry
                // comparison in the customer search is a clean date-to-date test rather than one
                // that turns on the hour the row happened to be saved.
                DbHelper.P("@ExpiryDate", medicine.ExpiryDate.Date),
                DbHelper.P("@Description", medicine.Description),
                // Same null-rather-than-empty rule as Strength: an empty ImagePath would later be
                // handed to the image loader as a path, where NULL is checked for and skipped.
                DbHelper.P("@ImagePath", string.IsNullOrWhiteSpace(medicine.ImagePath) ? null : medicine.ImagePath));
        }

        /// <summary>Requirement 11, the UPDATE. The PharmacyId in the WHERE clause is the isolation rule.</summary>
        public bool Update(Medicine medicine, int pharmacyId)
        {
            const string sql = @"
UPDATE  Medicines
-- PharmacyId is deliberately absent from this SET list. A medicine cannot be moved to
-- another shop by editing it; that would hand stock, and its sales history, to a different
-- owner. The column is written once, by Insert, and never again.
SET     CategoryId = @CategoryId, MedicineName = @Name, GenericName = @Generic,
        Manufacturer = @Manufacturer, Strength = @Strength, UnitPrice = @UnitPrice,
        Stock = @Stock, MinStock = @MinStock, RequiresRx = @RequiresRx,
        ExpiryDate = @ExpiryDate, Description = @Description, ImagePath = @ImagePath
-- TWO keys in the WHERE. The medicine id says which row; PharmacyId says it must be yours.
-- An id belonging to another shop matches nothing, so the statement changes no rows and
-- the method returns false rather than quietly editing a competitor's price.
WHERE   MedicineId = @Id AND PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@CategoryId", medicine.CategoryId),
                // The same Trim and null-for-blank rules as Insert, so a row that is edited ends
                // up stored in exactly the same shape as a row that was just created.
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
            // == 1, not > 0: the WHERE names a primary key, so a correct call changes exactly one
            // row. Anything else - 0 because the id is not this owner's, or more than one because
            // the WHERE was mis-written - is reported as failure instead of being treated as success.
        }

        /// <summary>
        /// Requirement 11, the DELETE. This is a soft delete: setting IsActive to
        /// 0 keeps the foreign keys from OrderItems intact, so every old invoice
        /// still resolves, while the item disappears from the customer screens.
        /// </summary>
        public bool Delist(int medicineId, int pharmacyId)
        {
            // A SOFT delete: an UPDATE, not a DELETE. OrderItems rows on past invoices
            // hold a foreign key to this medicine, and FK_OrderItems_Medicine has no
            // cascade, so a real DELETE would either be refused by the database or, with
            // a cascade, would quietly destroy invoice history. Flipping IsActive to 0
            // removes it from every customer query (they all filter IsActive = 1) while
            // every old invoice still resolves. Relist() is simply the reverse.
            //
            // Both ids are in the WHERE clause: the medicine id says WHICH row, and
            // PharmacyId says it must be YOURS. Passing another shop's medicine id
            // matches no row, so the method returns false instead of touching it.
            //
            // The statement is short enough to sit inline rather than in a const, and it
            // still travels with its values as parameters like every other query here.
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET IsActive = 0 WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",
                DbHelper.P("@Id", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // exactly one row changed
        }

        public bool Relist(int medicineId, int pharmacyId)
        {
            // The exact mirror of Delist, which is the point of the soft delete: because
            // nothing was destroyed, undoing it is a single flag flip rather than a re-entry
            // of the whole record, and the medicine keeps its id, so its sales history and
            // any reviews written about it stay attached.
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET IsActive = 1 WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",
                DbHelper.P("@Id", medicineId),
                // The isolation rule again: relisting is as much a write as delisting, so it
                // carries the same two-key WHERE and cannot be aimed at another shop's row.
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        // Called by the editor before it saves, so a clash is reported as a sentence next to
        // the field rather than as a unique-constraint error raised by the database.
        public bool NameExistsInPharmacy(int pharmacyId, string name, string strength, int ignoreMedicineId)
        {
            return _db.ExecuteScalarInt(@"
SELECT COUNT(*) FROM Medicines
-- Scoped to one shop on purpose: two different pharmacies are expected to stock Napa 500mg,
-- and the duplicate rule only means 'not twice on the same shelf'.
WHERE  PharmacyId = @PharmacyId
  AND  MedicineName = @Name
  -- Strength is nullable, and in SQL NULL = NULL is UNKNOWN rather than true, so a plain
  -- comparison would never match two rows that both leave it blank and the duplicate would
  -- slip through. Folding both sides to '' with ISNULL makes 'blank' compare equal to 'blank'.
  AND  ISNULL(Strength, '') = ISNULL(@Strength, '')
  -- The row being edited must not count as a duplicate of itself, otherwise saving a record
  -- without renaming it would be refused. Insert passes 0, which matches no IDENTITY key, so
  -- the same query serves both the create and the edit case.
  AND  MedicineId <> @Ignore;",
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@Name", name.Trim()),                // trimmed to match what Insert and Update store
                // Blank is normalised to '' here rather than passed as null, so the parameter
                // side of the ISNULL comparison above always has a value to compare.
                DbHelper.P("@Strength", string.IsNullOrWhiteSpace(strength) ? "" : strength.Trim()),
                DbHelper.P("@Ignore", ignoreMedicineId)) > 0;    // COUNT > 0 means 'any', which is all the caller needs
        }

        /// <summary>Feeds the medicine ComboBox on the Discount Offers form.</summary>
        public List<Medicine> GetSimpleListForPharmacy(int pharmacyId)
        {
            // A typed list rather than a DataTable, because a ComboBox binds to objects and the
            // form reads MedicineId from the selected item without going through column names.
            List<Medicine> list = new List<Medicine>();
            DataTable table = _db.ExecuteTable(@"
-- Four columns only. A ComboBox needs a key, a label and the price to show beside it, so
-- selecting the full row would carry Description and the image across for nothing.
SELECT  MedicineId, MedicineName, Strength, UnitPrice
FROM    Medicines
-- IsActive = 1 as well as the isolation rule: an offer cannot sensibly be attached to a
-- medicine that is no longer listed, so delisted rows never reach this ComboBox.
WHERE   PharmacyId = @PharmacyId AND IsActive = 1
ORDER BY MedicineName;",
                DbHelper.P("@PharmacyId", pharmacyId));

            // One pass over the rows, turning each into the object the form will bind to.
            foreach (DataRow row in table.Rows)
            {
                list.Add(new Medicine
                {
                    // The DbHelper.GetX helpers each translate DBNull into the type's empty
                    // value, which is why none of these four assignments needs a null check of
                    // its own even though Strength is a nullable column.
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
-- The isolation rule applied to a READ, not just to writes. Hiding the Edit button would not
-- be enough: the check has to be in the query, so that even a call made with another shop's
-- id comes back empty and the dialog never displays a competitor's cost price.
WHERE   m.MedicineId = @Id AND m.PharmacyId = @PharmacyId;";

            DataTable table = _db.ExecuteTable(sql,
                DbHelper.P("@Id", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId));

            // Empty means either 'no such medicine' or 'not yours'. The two are deliberately
            // indistinguishable to the caller, so the dialog cannot be used to probe whether a
            // given id exists on another shop's shelf.
            if (table.Rows.Count == 0) return null;
            // The same mapper as GetDetails, even though this query selects fewer columns: the
            // GetX helpers check Columns.Contains first, so the missing PharmacyName, Area and
            // DiscountPercent simply come back empty instead of throwing.
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
-- ShortfallUnits is worked out by the QUERY, not on screen, so the grid can bind to it
-- directly and the restock box can be pre-filled with the exact number to order.
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,
        -- Both columns are carried as well as the difference, so the owner can see the rule
        -- that fired ('4 left, minimum 20') rather than only the number to buy.
        m.Stock, m.MinStock, (m.MinStock - m.Stock) AS ShortfallUnits
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId   -- the category name, for grouping the reorder list by type
WHERE   m.PharmacyId = @PharmacyId        -- the isolation rule: this shop only
  -- The alert compares TWO COLUMNS OF THE SAME ROW rather than using one fixed
  -- number. Ten boxes of a glucometer is plenty; ten strips of Napa is nothing.
  -- Each medicine carries its own MinStock, so 'low' means low for that product.
  --
  -- A single threshold held in the application would have to be wrong for one of those
  -- two products, and moving it would silently change the alert for every medicine at once.
  -- Because the comparison is between columns, the owner tunes it per product by editing
  -- MinStock, and the query itself never changes.
  AND   m.Stock      < m.MinStock
  AND   m.IsActive   = 1                  -- a delisted medicine cannot be 'low'
ORDER BY ShortfallUnits DESC;";           // worst shortage first, so it reads as a to-do list

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));
        }

        /// <summary>Units sold and units remaining for every medicine this pharmacy lists.</summary>
        public DataTable GetInventory(int pharmacyId)
        {
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,
        m.Stock                                     AS UnitsRemaining,   -- aliased, because 'Stock' alone reads ambiguously next to UnitsSold
        -- ISNULL turns 'never sold' into 0 rather than an empty cell, so the column stays
        -- numeric and the grid can sort it without pushing the blanks to one end.
        ISNULL(sold.UnitsSold, 0)                   AS UnitsSold,
        m.MinStock,                                                      -- carried so the grid can show the threshold beside the status word
        m.UnitPrice,
        -- The money tied up in each line, computed here so the owner can total the column
        -- without the form doing arithmetic on bound values.
        CAST(m.Stock * m.UnitPrice AS DECIMAL(12,2)) AS StockValue,
        -- The same two-column comparison as GetLowStock, expressed as a label. Written once
        -- as a CASE here means the inventory grid and the alert list can never disagree.
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus,
        m.ExpiryDate                                                     -- so stock about to expire can be spotted while it is still sellable
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
        -- LEFT JOIN to a DERIVED TABLE, and both halves of that matter.
        -- LEFT, because a medicine that has never sold must still appear in the inventory:
        -- an INNER JOIN would quietly drop exactly the slow-moving stock this screen exists
        -- to surface.
        -- Derived table, because the subquery groups OrderItems down to ONE row per medicine
        -- before the join happens. Joining OrderItems directly would repeat the medicine row
        -- once per sale, and Stock, UnitPrice and StockValue would each be read several times.
        LEFT JOIN (SELECT oi.MedicineId, SUM(oi.Quantity) AS UnitsSold
                   FROM   OrderItems oi
                          INNER JOIN Orders o ON o.OrderId = oi.OrderId   -- the status lives on the order header, so the join is needed to filter
                   -- Cancelled orders returned their units to the shelf, so counting them as
                   -- sold would overstate movement and understate what is really sitting there.
                   WHERE  o.Status <> 'Cancelled'
                   GROUP BY oi.MedicineId) sold ON sold.MedicineId = m.MedicineId
-- The isolation rule, plus IsActive: the inventory screen is about stock the shop is
-- currently selling, and delisted lines belong on the medicines grid instead.
WHERE   m.PharmacyId = @PharmacyId AND m.IsActive = 1
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));
        }

        public int CountLowStock(int pharmacyId)
        {
            // The dashboard tile. It repeats the Stock < MinStock rule rather than counting the
            // rows of GetLowStock in C#, because a COUNT is answered by the database without
            // sending any rows across, and the tile only ever needs the single number.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @Id AND Stock < MinStock AND IsActive = 1;",
                DbHelper.P("@Id", pharmacyId));
        }

        public int CountMedicines(int pharmacyId)
        {
            // IsActive = 1 so the tile agrees with what the owner sees listed. Counting delisted
            // rows here would show a catalogue size the medicines grid does not display.
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @Id AND IsActive = 1;",
                DbHelper.P("@Id", pharmacyId));
        }

        /// <summary>Used by the Restock button on the inventory screen.</summary>
        public bool AddStock(int medicineId, int pharmacyId, int unitsToAdd)
        {
            // Stock = Stock + @Units, not Stock = @NewTotal. The database reads and writes the
            // column inside one statement, so a sale that completes between the grid being drawn
            // and the button being pressed is still counted. Sending a total worked out from the
            // number on screen would overwrite that sale and put the units back on the shelf.
            //
            // The two-key WHERE is the isolation rule once more: restocking is a write, so it can
            // only ever be aimed at a row this pharmacy owns.
            return _db.ExecuteNonQuery(
                "UPDATE Medicines SET Stock = Stock + @Units WHERE MedicineId = @Id AND PharmacyId = @PharmacyId;",
                DbHelper.P("@Units", unitsToAdd),
                DbHelper.P("@Id", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // one row changed, or the id was not this shop's
        }

        // ---------------------------------------------------------------------

        // private and static: it belongs to the class rather than to an instance because it
        // touches no field, and it is private because a DataRow shaped by these queries is the
        // only thing it can safely read. GetDetails and GetForEdit both call it, so the two
        // cannot drift apart when a property is added to Medicine.
        private static Medicine MapMedicine(DataRow row)
        {
            return new Medicine
            {
                // Every value goes through a DbHelper.GetX helper, and each of those checks
                // Columns.Contains before reading. That is what lets ONE mapper serve two
                // queries with different column lists: GetForEdit does not select PharmacyName,
                // Area or DiscountPercent, and those three simply come back empty rather than
                // throwing ArgumentException on a missing column.
                MedicineId = DbHelper.GetInt(row, "MedicineId"),
                PharmacyId = DbHelper.GetInt(row, "PharmacyId"),
                CategoryId = DbHelper.GetInt(row, "CategoryId"),   // kept so an editor can pre-select the right item in the Category ComboBox
                MedicineName = DbHelper.GetString(row, "MedicineName"),
                GenericName = DbHelper.GetString(row, "GenericName"),
                Manufacturer = DbHelper.GetString(row, "Manufacturer"),
                Strength = DbHelper.GetString(row, "Strength"),    // nullable in the table, so the helper hands back "" rather than null
                UnitPrice = DbHelper.GetDecimal(row, "UnitPrice"), // decimal, never double: money must not carry binary rounding error
                Stock = DbHelper.GetInt(row, "Stock"),
                MinStock = DbHelper.GetInt(row, "MinStock"),
                RequiresRx = DbHelper.GetBool(row, "RequiresRx"),  // the bit column becomes a bool the form can bind to a check box
                ExpiryDate = DbHelper.GetDate(row, "ExpiryDate"),
                Description = DbHelper.GetString(row, "Description"),
                ImagePath = DbHelper.GetString(row, "ImagePath"),
                IsActive = DbHelper.GetBool(row, "IsActive"),
                // The last four come from the joins rather than from Medicines itself, which is
                // why they are the ones GetForEdit leaves empty.
                CategoryName = DbHelper.GetString(row, "CategoryName"),
                PharmacyName = DbHelper.GetString(row, "PharmacyName"),
                Area = DbHelper.GetString(row, "Area"),
                DiscountPercent = DbHelper.GetDecimal(row, "DiscountPercent")
            };
        }
    }
}
