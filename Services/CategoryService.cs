using System.Data;                  // DataTable and DataRow, the shape every read returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection
using PharmaLinkApp.Models;         // Category, the typed object a Categories row becomes

// Services sits between forms and database, so no form ever writes SQL of its own.
namespace PharmaLinkApp.Services
{
    /// <summary>The Super Admin's master category list (requirement 7).</summary>
    public class CategoryService
    {
        // readonly, and it holds no connection: DbHelper opens and closes one per call.
        private readonly DbHelper _db = new DbHelper();

        // The admin grid; the bool becomes 1 or 0 below, so there is one query only.
        public DataTable GetTable(bool activeOnly)
        {
            // const, so the text is folded in once; @"..." keeps the line breaks readable.
            const string sql = @"
-- Only the columns the grid shows; SELECT * would break its column order.
SELECT  c.CategoryId, c.CategoryName, c.Description, c.IsActive,
        -- correlated subquery, run once per row: how many medicines point at this category
        (SELECT COUNT(*) FROM Medicines m WHERE m.CategoryId = c.CategoryId) AS MedicineCount
-- Aliased c, so the subquery above means the OUTER row, not its own.
FROM    Categories c
-- Optional filter: 0 shows everything, 1 leaves only the active rows.
WHERE   (@ActiveOnly = 0 OR c.IsActive = 1)
-- Alphabetical, because this list is looked up by name.
ORDER BY c.CategoryName;";

            // SQL Server has no boolean, so the bool travels as a 1 or 0 parameter.
            return _db.ExecuteTable(sql, DbHelper.P("@ActiveOnly", activeOnly ? 1 : 0));
        }

        /// <summary>Feeds every category ComboBox in the application.</summary>
        public List<Category> GetActiveList()
        {
            // Built first, so no match returns an empty list; a null one throws on binding.
            List<Category> list = new List<Category>();
            // IsActive = 1 is fixed, not a parameter: a retired category is never offered.
            DataTable table = _db.ExecuteTable(
                "SELECT CategoryId, CategoryName, Description, IsActive FROM Categories WHERE IsActive = 1 ORDER BY CategoryName;");   // no parameters, so no P() overload needed

            // Typed objects, because a ComboBox can hand back the chosen Category itself.
            foreach (DataRow row in table.Rows)
            {
                // Object initializer, so no half-populated instance sits in a local first.
                list.Add(new Category
                {
                    // The GetX helpers turn a missing column or DBNull into the empty value.
                    CategoryId = DbHelper.GetInt(row, "CategoryId"),
                    CategoryName = DbHelper.GetString(row, "CategoryName"),   // what the ComboBox displays, via ToString()
                    // Description is nullable, so GetString shows NULL as blank, not a throw.
                    Description = DbHelper.GetString(row, "Description"),
                    // Always true given the WHERE above, but mapped so the object is complete.
                    IsActive = DbHelper.GetBool(row, "IsActive")
                });
            }
            return list;   // possibly empty, never null - the caller binds it without a guard
        }

        // Called before saving, so a duplicate name reads as a sentence.
        public bool NameExists(string name, int ignoreCategoryId)
        {
            return _db.ExecuteScalarInt(   // the whole method is one expression: ask, compare, answer
                // "CategoryId <> @Id" serves both cases: Add passes 0, Edit passes its own id.
                "SELECT COUNT(*) FROM Categories WHERE CategoryName = @Name AND CategoryId <> @Id;",
                // Trimmed here and in Add and Update, so the checked name is the stored name.
                DbHelper.P("@Name", name.Trim()),
                DbHelper.P("@Id", ignoreCategoryId)) > 0;   // COUNT > 0, so 'any', not 'how many'
        }

        // Returns the new CategoryId, so the caller needs no second query.
        public int Add(string name, string description)
        {
            // Verbatim again: this is genuinely two statements and they need separate lines.
            const string sql = @"
-- Two statements, one round trip: the INSERT, then the key it generated.
INSERT INTO Categories (CategoryName, Description) VALUES (@Name, @Description);   -- the write itself
-- SCOPE_IDENTITY: this batch's key, not a trigger's, as @@IDENTITY could give.
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            // ExecuteScalarInt reads the first cell, the new key, and returns 0 if none came.
            return _db.ExecuteScalarInt(sql,
                DbHelper.P("@Name", name.Trim()),   // trimmed to match NameExists, so checked name equals stored name
                // Description is NOT trimmed: free text, where leading spacing may be meant.
                DbHelper.P("@Description", description));
        }

        // Returns false when the id matched nothing: saved, or already gone.
        public bool Update(int categoryId, string name, string description)
        {
            return _db.ExecuteNonQuery(   // ExecuteNonQuery gives back the row count, the whole test below
                // The WHERE names the key, so at most one row; IsActive is SetActive's job.
                "UPDATE Categories SET CategoryName = @Name, Description = @Description WHERE CategoryId = @Id;",
                DbHelper.P("@Name", name.Trim()),           // same Trim as Add and NameExists; all three must agree
                DbHelper.P("@Description", description),    // untrimmed as in Add: it is prose, not a key
                DbHelper.P("@Id", categoryId)) == 1;   // exactly one row changed, so the id existed
        }

        /// <summary>Soft delete, so existing Medicines rows stay valid.</summary>
        public bool SetActive(int categoryId, bool active)
        {
            return _db.ExecuteNonQuery(   // a write, so the row count is the only result worth having
                // One statement for both directions; the caller already holds the bool.
                "UPDATE Categories SET IsActive = @Active WHERE CategoryId = @Id;",
                // The flag is stored as BIT, so the bool becomes 1 or 0 on the way in.
                DbHelper.P("@Active", active ? 1 : 0),
                DbHelper.P("@Id", categoryId)) == 1;   // one row, or the id is gone
        }

        // Public as well as used by Delete, so the form can grey the button out.
        public bool IsReferenced(int categoryId)
        {
            return _db.ExecuteScalarInt(   // COUNT returns one row and one column, so scalar is right
                // Counting Medicines, not Categories: the question is what points AT this row.
                "SELECT COUNT(*) FROM Medicines WHERE CategoryId = @Id;",
                DbHelper.P("@Id", categoryId)) > 0;   // "> 0" not "== 1": one medicine blocks as hard as fifty
        }

        /// <summary>Only allowed when nothing points at the category.</summary>
        public bool Delete(int categoryId)
        {
            // Checked before deleting: FK_Medicines_Category would refuse it with error 547.
            if (IsReferenced(categoryId)) return false;

            // Reached only by a category nothing points at, so in practice a mistyped one.
            return _db.ExecuteNonQuery(
                "DELETE FROM Categories WHERE CategoryId = @Id;",   // a hard delete, reached only past the guard above
                // == 1 not > 0 because the key is unique; 0 means the id had already gone.
                DbHelper.P("@Id", categoryId)) == 1;
        }
    }
}
