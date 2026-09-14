using System.Data;                  // DataTable and DataRow, the shape every read returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection
using PharmaLinkApp.Models;         // Category, the typed object a Categories row becomes

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// The master medicine category list, maintained by the Super Admin
    /// (requirement 7). A category that is already referenced by a medicine is
    /// never deleted, only deactivated, which keeps every foreign key valid.
    /// </summary>
    public class CategoryService
    {
        // One helper per service instance. It holds no connection of its own: every
        // call inside DbHelper opens a connection, runs, and closes it again, so this
        // field is safe to share across all the methods below. readonly means no method
        // can point this service at a different database part way through its life.
        private readonly DbHelper _db = new DbHelper();

        // The admin grid. The bool argument is turned into a 1 or a 0 below rather than
        // selecting between two different query strings, so there is exactly one piece of
        // SQL here to read and to get right.
        public DataTable GetTable(bool activeOnly)
        {
            const string sql = @"
-- Only the four columns the grid actually shows, plus the count below. SELECT * was
-- rejected: it would break the grid's column order the moment a column is added.
SELECT  c.CategoryId, c.CategoryName, c.Description, c.IsActive,
        -- A correlated subquery, evaluated once per category row: how many medicines
        -- point at it. This is the number the admin needs before pressing Delete, and
        -- computing it here means the screen cannot disagree with IsReferenced below,
        -- which asks the same question of the same table.
        (SELECT COUNT(*) FROM Medicines m WHERE m.CategoryId = c.CategoryId) AS MedicineCount
FROM    Categories c
-- The optional filter pattern used across this project. Passing 0 means 'show
-- everything' and the OR short circuits the second test; passing 1 leaves only the
-- active rows. One query serves both views of the screen, so the two can never drift.
WHERE   (@ActiveOnly = 0 OR c.IsActive = 1)
-- Alphabetical, because this list is looked up by name rather than read in the order
-- the rows happened to be created.
ORDER BY c.CategoryName;";

            // The bool is converted to 1/0 here because SQL Server has no boolean type
            // that a comparison like "@ActiveOnly = 0" can use directly. It still travels
            // as a parameter rather than being written into the text above.
            return _db.ExecuteTable(sql, DbHelper.P("@ActiveOnly", activeOnly ? 1 : 0));
        }

        /// <summary>Feeds every category ComboBox in the application.</summary>
        public List<Category> GetActiveList()
        {
            // Built first and returned even when the query matched nothing, so a caller
            // binding a ComboBox gets an empty list rather than null. An empty drop-down
            // is a correct screen; a null one throws on binding.
            List<Category> list = new List<Category>();
            // IsActive = 1 is fixed in the text rather than parameterised: a drop-down
            // used to CHOOSE a category must never offer a retired one, so this is a rule
            // of the method, not an option the caller can switch off.
            DataTable table = _db.ExecuteTable(
                "SELECT CategoryId, CategoryName, Description, IsActive FROM Categories WHERE IsActive = 1 ORDER BY CategoryName;");

            // Walk the rows once, turning each into a Category. The typed list exists
            // because a ComboBox bound to objects can return the chosen Category itself,
            // whereas a grid is happier bound straight to the DataTable above.
            foreach (DataRow row in table.Rows)
            {
                list.Add(new Category
                {
                    // The GetX helpers each translate a missing column or a DBNull into
                    // the type's empty value, which is why none of these four assignments
                    // needs its own null check.
                    CategoryId = DbHelper.GetInt(row, "CategoryId"),
                    CategoryName = DbHelper.GetString(row, "CategoryName"),
                    // Description is nullable in the schema, so GetString turning NULL
                    // into "" is what keeps a blank description off the screen as blank
                    // rather than throwing on the way to a Label.
                    Description = DbHelper.GetString(row, "Description"),
                    // Always true given the WHERE clause above, but mapped anyway so the
                    // object is complete and can be handed to the edit screen unchanged.
                    IsActive = DbHelper.GetBool(row, "IsActive")
                });
            }
            return list;
        }

        // Used by the add and edit screens before saving, so a duplicate name is caught
        // with a clear sentence instead of surfacing as a unique-constraint error.
        public bool NameExists(string name, int ignoreCategoryId)
        {
            return _db.ExecuteScalarInt(
                // "CategoryId <> @Id" is what makes one method serve both cases. Adding a
                // new category passes 0, which matches no real row, so every existing name
                // counts as a clash. Editing an existing one passes its own id, so the
                // category does not collide with itself and a save with the name unchanged
                // is allowed through.
                "SELECT COUNT(*) FROM Categories WHERE CategoryName = @Name AND CategoryId <> @Id;",
                // Trimmed here so that " Antibiotics" is recognised as a duplicate of
                // "Antibiotics" - and trimmed identically in Add and Update below, so the
                // value that is checked is the value that gets stored.
                DbHelper.P("@Name", name.Trim()),
                DbHelper.P("@Id", ignoreCategoryId)) > 0;   // COUNT > 0, so 'any', not 'how many'
        }

        // Returns the new CategoryId rather than a bool, so the caller can select the
        // row it just created without a second query to find it by name.
        public int Add(string name, string description)
        {
            const string sql = @"
-- Two statements in one batch, sent in a single round trip.
INSERT INTO Categories (CategoryName, Description) VALUES (@Name, @Description);
-- SCOPE_IDENTITY returns the identity generated by THIS batch. @@IDENTITY was rejected
-- because it returns the last identity generated on the connection by anything at all,
-- including a trigger on another table, which would hand back the wrong key.
-- The CAST is because SCOPE_IDENTITY is a NUMERIC(38,0); casting to INT here means the
-- value arrives as a plain integer rather than a decimal that has to be narrowed in C#.
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            // ExecuteScalarInt reads the first column of the first row, which after the
            // batch above is the new key. It returns 0 rather than throwing if nothing
            // came back, so a caller can test the result instead of catching.
            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@Name", name.Trim()),
                // Description is NOT trimmed: it is free text where leading spacing may
                // be deliberate, unlike the name, which is an identifier people search by.
                DbHelper.P("@Description", description));
        }

        public bool Update(int categoryId, string name, string description)
        {
            return _db.ExecuteNonQuery(
                // The WHERE names the primary key, so this statement can touch at most
                // one row. IsActive is deliberately absent from the SET list: activating
                // and deactivating is SetActive's job, and keeping the two apart means an
                // edit of the name can never switch a category back on by accident.
                "UPDATE Categories SET CategoryName = @Name, Description = @Description WHERE CategoryId = @Id;",
                DbHelper.P("@Name", name.Trim()),
                DbHelper.P("@Description", description),
                DbHelper.P("@Id", categoryId)) == 1;   // exactly one row changed, so the id existed
        }

        /// <summary>Soft delete: existing Medicines rows keep pointing at a valid row.</summary>
        public bool SetActive(int categoryId, bool active)
        {
            return _db.ExecuteNonQuery(
                // One statement for both directions. A separate Activate and Deactivate
                // pair would be two places to keep in step for no gain, and the caller
                // usually has the desired state as a bool already.
                "UPDATE Categories SET IsActive = @Active WHERE CategoryId = @Id;",
                // The flag is stored as BIT, so the bool becomes 1 or 0 on the way in.
                DbHelper.P("@Active", active ? 1 : 0),
                DbHelper.P("@Id", categoryId)) == 1;
        }

        // Asks the one question that decides whether a category may be deleted: does any
        // medicine still point at it? Public as well as being used by Delete, so the form
        // can grey the button out before the admin ever presses it.
        public bool IsReferenced(int categoryId)
        {
            return _db.ExecuteScalarInt(
                // Counting Medicines, not Categories: the question is about what points
                // AT this row, which is exactly what FK_Medicines_Category protects.
                "SELECT COUNT(*) FROM Medicines WHERE CategoryId = @Id;",
                DbHelper.P("@Id", categoryId)) > 0;
        }

        /// <summary>Only allowed when nothing points at the category.</summary>
        public bool Delete(int categoryId)
        {
            // Check BEFORE deleting rather than catching the failure afterwards. Without
            // this guard the DELETE would still be refused - FK_Medicines_Category has
            // no cascade, so SQL Server would raise error 547 - but the user would get a
            // translated constraint error instead of a clear "deactivate it instead".
            // The database is the guarantee; this line is the good manners.
            if (IsReferenced(categoryId)) return false;

            // Reached only for a category nothing points at, which in practice means one
            // created by mistake. Anything with history is deactivated via SetActive(false).
            return _db.ExecuteNonQuery(
                "DELETE FROM Categories WHERE CategoryId = @Id;",
                // == 1 rather than > 0 because the key is unique: one row is the only
                // correct outcome, and 0 means the id had already gone.
                DbHelper.P("@Id", categoryId)) == 1;
        }
    }
}
