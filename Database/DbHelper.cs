using System.Configuration;         // ConfigurationManager, which reads App.config at run time
using System.Data;                  // DataTable and DataRow, the shape every read returns
using Microsoft.Data.SqlClient;     // SqlConnection, SqlCommand, SqlParameter, SqlException

// Its own namespace, so a form cannot reach a SqlConnection by accident.
namespace PharmaLinkApp.Database
{
    // Layer: data access. Forms -> Services -> DbHelper -> SQL Server.

    /// <summary>The one place that knows how to reach SQL Server.</summary>
    public class DbHelper
    {
        // Read once at construction and never reassigned, so nothing can repoint it.
        private readonly string _connectionString =
            // App.config entry named "db", so the server can change without a rebuild.
            ConfigurationManager.ConnectionStrings["db"].ConnectionString;

        // A NEW connection each call; ADO.NET pools the real sockets, so this is cheap.
        public SqlConnection GetConnection()
        {
            // The constructor only records the string - nothing is opened here.
            return new SqlConnection(_connectionString);
        }

        /// <summary>Runs a SELECT and returns the result set as a DataTable.</summary>
        public DataTable ExecuteTable(string sql, params SqlParameter[] parameters)
        {
            // params SqlParameter[] means the easiest way to call this is also the safe way.
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))   // command bound to this connection, disposed with it
            {
                // THE line that stops SQL injection: values are sent apart from the text.
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);   // one call binds the whole array, so none is skipped

                DataTable table = new DataTable();   // empty table, never null, so a bound grid shows its headers
                try   // guards only the database work, not a mistake in the caller
                {
                    // The adapter opens, runs and closes, which is why conn.Open() is absent.
                    using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(table);   // reads the whole result in one go, then lets the connection go
                    }
                }
                catch (SqlException ex)   // SqlException only: a NullReferenceException here would be a code bug
                {
                    // Translated here once, not in 28 forms; the original is the InnerException.
                    throw new DataAccessException(Describe(ex), ex);
                }
                return table;   // bound straight to a DataGridView by the calling form
            }
        }

        /// <summary>Runs an INSERT, UPDATE or DELETE; returns the row count.</summary>
        public int ExecuteNonQuery(string sql, params SqlParameter[] parameters)
        {
            // The count is what lets a service write "== 1" as proof it hit its own row.
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))   // same construction as the read path, so the two cannot drift
            {
                // Identical binding step, repeated rather than hidden in a private helper.
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);   // writes are where injection would do real damage

                try   // wraps the open as well, because a dead server fails at Open()
                {
                    conn.Open();   // opened by hand here: there is no adapter to do it
                    return cmd.ExecuteNonQuery();   // the using blocks still close the connection on the way out
                }
                catch (SqlException ex)   // constraint violations arrive here: 2627, 547, 515
                {
                    // Describe() turns each of those numbers into an instruction.
                    throw new DataAccessException(Describe(ex), ex);
                }
            }
        }

        /// <summary>Runs a query returning one value, such as a COUNT or SUM.</summary>
        public object ExecuteScalar(string sql, params SqlParameter[] parameters)
        {
            // object, because ExecuteScalar can return an int, a decimal, a string or DBNull.
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))   // the command must exist before parameters can attach to it
            {
                // Scalar queries most often carry a user-typed id, so the same guard applies.
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);   // bind every value; nothing is interpolated

                try   // the boundary: past here a caller sees DataAccessException, never SqlException
                {
                    conn.Open();   // no adapter here either, so the connection is opened explicitly
                    return cmd.ExecuteScalar();   // first column of the first row; this is how SCOPE_IDENTITY comes back
                }
                catch (SqlException ex)   // identical handling, so every path reports failures the same way
                {
                    // Re-thrown, not a sentinel: a quiet 0 would read as a genuine count.
                    throw new DataAccessException(Describe(ex), ex);
                }
            }
        }

        // The three wrappers below spare every service from testing for null and DBNull.
        public int ExecuteScalarInt(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);   // one round trip, delegated so nothing is duplicated
            // Two empty results: null for no rows, DBNull.Value for a row holding SQL NULL.
            if (value == null || value == DBNull.Value) return 0;
            // Convert, not a cast: COUNT_BIG returns a long and an (int) cast would throw.
            return Convert.ToInt32(value);
        }

        // The money twin, used for every SUM of a price or a commission.
        public decimal ExecuteScalarDecimal(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);   // same single round trip through the untyped version
            // 0m, not 0: the m suffix makes the literal a decimal, so no conversion happens.
            if (value == null || value == DBNull.Value) return 0m;
            // decimal, never double, for money: base ten, matching the DECIMAL columns.
            return Convert.ToDecimal(value);
        }

        // The text twin, for lookups that fetch a name or a status rather than a number.
        public string ExecuteScalarString(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);   // all three wrappers share one path to the database
            // string.Empty, not null, so a caller can safely call .Trim() or .Length.
            if (value == null || value == DBNull.Value) return string.Empty;
            // ToString(), not a cast: the column may be CHAR, NVARCHAR or even a number.
            return value.ToString();
        }

        // -- turning SQL Server error numbers into English --

        /// <summary>Maps SQL Server error numbers onto readable advice.</summary>
        private static string Describe(SqlException ex)
        {
            // Switching on ex.Number, not ex.Message: the number is stable across languages.
            switch (ex.Number)
            {
                case 53:        // server not found
                case -1:        // connection could not be established
                case 4060:      // cannot open database
                case 18456:     // login failed
                case 40615:     // firewall
                    // One sentence for five numbers: to the user they all have the same fix.
                    return "PharmaLink could not reach the database.\r\n\r\n" +
                           "Check that SQL Server is running, that PharmaLinkDB has been created " +   // the service may not be running
                           "from PharmaLinkDB_Setup.sql, and that the connection string in " +   // the setup script may never have run
                           "App.config points at the right server.";   // or the name points at another machine

                case -2:        // timeout
                    // Kept separate: nothing is misconfigured, so the advice is to wait.
                    return "The database took too long to answer. It may be busy - try again in a moment.";

                case 2627:      // PRIMARY KEY / UNIQUE constraint
                case 2601:      // unique index
                    // Both mean "this value is already taken", which the user cannot act on.
                    return "That record already exists.\r\n\r\n" +
                           "The database keeps this value unique, so the same entry cannot be " +   // names the rule, so it reads as designed
                           "saved twice. Change the value and try again.";   // ends on the action to take

                case 547:       // FK or CHECK constraint
                    // 547 covers two problems, so this is the one case that inspects the text.
                    return ex.Message.Contains("CHECK")
                        // A CHECK violation means a value is out of the range the database allows.
                        ? "One of the values breaks a rule the database enforces, for example a " +
                          "negative price, a commission above 30 percent or a status that is not allowed."   // three real constraints
                        // The foreign-key branch: a row is still referenced, or points at nothing.
                        : "This record is still referenced by other data, or it points at something " +
                          "that no longer exists, so the database refused the change.\r\n\r\n" +   // either side of the link may be at fault
                          "Where a record has history behind it, deactivate it instead of deleting it.";   // the designed alternative

                case 515:       // NOT NULL
                    // Names "required fields", not the column, which means nothing on screen.
                    return "A required field was left empty. Fill in every field marked as required and try again.";

                case 8152:      // string truncation (legacy)
                case 2628:      // string truncation
                    // Two numbers for one fault, so any server version behaves the same.
                    return "One of the values is too long for the field it is being saved into. Shorten it and try again.";

                default:        // every number not listed above, which is most of them
                    // Says the operation was refused, then appends the server's own text.
                    return "The database refused the last operation.\r\n\r\n" + ex.Message;
            }
        }

        // -- small helpers so the service classes read cleanly --

        // static and one letter: it appears on almost every line of every service.
        public static SqlParameter P(string name, object value)
        {
            // ?? DBNull.Value: a C# null means "not supplied" and the command would fail.
            return new SqlParameter(name, value ?? DBNull.Value);
        }

        // The five GetX helpers share one shape: exists, not NULL, else convert.
        public static int GetInt(DataRow row, string column)
        {
            // Two tests: an unknown column name throws, and a known column may be NULL.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0;
            // Convert, not a cast: the boxed value may be a byte, a short or a long.
            return Convert.ToInt32(row[column]);
        }

        // The money reader; every price and line total on a model comes through here.
        public static decimal GetDecimal(DataRow row, string column)
        {
            // Same two guards, with 0m so an absent price reads as free rather than throwing.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0m;
            // Money stays decimal end to end, so nothing is rounded by a float conversion.
            return Convert.ToDecimal(row[column]);
        }

        // The text reader, for every name, status and description the models carry.
        public static string GetString(DataRow row, string column)
        {
            // string.Empty, so a NULL Description shows as blank instead of throwing.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return string.Empty;
            // ToString() also copes with a date or a number selected into a text position.
            return row[column].ToString();
        }

        // The flag reader; a bool documents that only two values are meaningful.
        public static bool GetBool(DataRow row, string column)
        {
            // false is the safe default: IsActive, RequiresRx and IsHidden all mean "no".
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return false;
            // Convert.ToBoolean accepts a real BIT column and the 1/0 the queries compute.
            return Convert.ToBoolean(row[column]);
        }

        // The date reader, so a screen gets a DateTime it can sort rather than text.
        public static DateTime GetDate(DataRow row, string column)
        {
            // DateTime.MinValue, not Now: a missing date should look obviously wrong.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return DateTime.MinValue;
            // Convert copes with the DATE, DATETIME and DATETIME2 columns this schema mixes.
            return Convert.ToDateTime(row[column]);
        }
    }
}
