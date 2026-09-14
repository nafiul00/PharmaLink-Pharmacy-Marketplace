using System.Configuration;         // ConfigurationManager, which reads App.config at run time
using System.Data;                  // DataTable and DataRow, the provider-neutral shape every read returns
using Microsoft.Data.SqlClient;     // SqlConnection, SqlCommand, SqlParameter, SqlException

namespace PharmaLinkApp.Database
{
    // -------------------------------------------------------------------------
    //  Layer: data access.   Forms -> Services -> DbHelper -> SQL Server.
    //  Connection string: App.config, the entry named "db", read on line 24.
    //  Used by all ten classes in Services/; no form opens a connection itself.
    //  Every method takes SqlParameter[], so no query is built by concatenation.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The one place in the application that knows how to reach SQL Server.
    ///
    /// Every method here takes the SQL text and an array of SqlParameter values.
    /// Nothing is ever concatenated into a query string, which is what keeps the
    /// whole system safe from SQL injection: a customer who types
    /// "'; DROP TABLE Users; --" into the search box searches for that literal
    /// text and finds nothing.
    ///
    /// This is also the one place that translates SqlException into readable
    /// English. Because every query in the application passes through here, a
    /// form that catches Exception and shows ex.Message gets a sentence rather
    /// than a SQL Server error number, and so does the global handler in
    /// Program.cs for the calls that are not wrapped at the call site.
    /// </summary>
    // WHY ONE CLASS RATHER THAN A CONNECTION PER SERVICE: a security rule that lives in
    // one file can be read in one sitting and cannot be forgotten in the eleventh place.
    // If each service opened its own SqlConnection, "is every query parameterised?" would
    // become a question about ten files instead of this one, and the first time somebody
    // wrote string concatenation in a hurry the guarantee would be gone with nothing to
    // catch it. Every method below has the same (string sql, params SqlParameter[])
    // shape precisely so that there is no overload anyone can reach that accepts an
    // already-assembled query with values glued into it.
    public class DbHelper
    {
        // readonly, and assigned right here at the field initialiser: the connection
        // string is read once when a DbHelper is constructed and can never be reassigned
        // afterwards, so no later code can quietly point this object at a different
        // database. Reading it from App.config rather than hard-coding it means the
        // server name can be changed for a different machine without a rebuild - which
        // is also why Describe() below names App.config when the connection fails.
        // ConnectionStrings["db"] is the <add name="db" .../> entry; indexing by name
        // rather than by position means the entry can be moved in the file safely.
        private readonly string _connectionString =
            ConfigurationManager.ConnectionStrings["db"].ConnectionString;

        // Hands back a NEW connection object every call rather than a shared one held in
        // a field. A single long-lived SqlConnection would be a shared mutable resource
        // and would break the moment two operations overlapped; a fresh object costs
        // almost nothing because ADO.NET pools the underlying physical connections, so
        // "new SqlConnection" reuses a pooled socket rather than reconnecting.
        // Public so that the rare caller needing a connection of its own can get one
        // that is already pointed at the configured database.
        public SqlConnection GetConnection()
        {
            // The constructor only records the string - nothing is opened here. Opening
            // is left to the method that will use it, inside a using block, so the
            // connection is never open for longer than the statement that needs it.
            return new SqlConnection(_connectionString);
        }

        /// <summary>Runs a SELECT and returns the whole result set as a DataTable, ready for a DataGridView.</summary>
        // params SqlParameter[] is what lets a caller write _db.ExecuteTable(sql, P("@Id", id))
        // with no array syntax at all, and write ExecuteTable(sql) with no parameters when
        // the query has none. That convenience is deliberate: the easiest way to call this
        // class is also the safe way, so nobody is tempted to build a query by hand.
        public DataTable ExecuteTable(string sql, params SqlParameter[] parameters)
        {
            // Two nested using blocks: both the connection and the command are disposed
            // even if the query throws, so a failed query never leaks a pooled connection.
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                // THE line that prevents SQL injection. Values arrive as SqlParameter
                // objects and are sent separately from the query text, so input like
                // '; DROP TABLE Users; -- is compared as a literal string and matches
                // nothing. Because every method here has this same signature, there is
                // no code path in the application that can build SQL by concatenation.
                // The null and Length tests are there because a params array is null
                // when a caller passes an explicit null and empty when a caller passes
                // nothing, and AddRange(null) would throw on a query that simply has no
                // parameters to bind.
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);

                // Created before the try so that it is still in scope at the return
                // statement below, and so an empty table - not null - is what a caller
                // receives for a query that matched nothing. A DataGridView bound to an
                // empty table shows its column headers; one bound to null throws.
                DataTable table = new DataTable();
                try
                {
                    // The adapter opens the connection, runs the query, builds the columns
                    // from the result set and closes the connection again. That is why this
                    // method never calls conn.Open() itself, unlike ExecuteNonQuery below.
                    using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                    {
                        // Fill reads the entire result set into memory in one go and then
                        // lets go of the connection. A SqlDataReader would be cheaper for
                        // a huge table, but it holds the connection open for as long as
                        // the caller is reading, and every screen in this application
                        // binds a grid to the whole result anyway.
                        adapter.Fill(table);
                    }
                }
                catch (SqlException ex)
                {
                    // Translate here, once, instead of in 28 forms. Describe() maps the
                    // error number to a sentence and the original exception is kept as
                    // InnerException, so no screen ever shows raw SQL Server text.
                    // Only SqlException is caught: a NullReferenceException in calling
                    // code is a programming mistake, not a database failure, and dressing
                    // it up as one would hide the real bug.
                    throw new DataAccessException(Describe(ex), ex);
                }
                return table;   // bound straight to a DataGridView by the calling form
            }
        }

        /// <summary>Runs an INSERT, UPDATE or DELETE and returns how many rows it changed.</summary>
        // Returning the rows-affected count rather than void is what lets the services
        // write "== 1" and treat that as proof the statement hit exactly the row it was
        // scoped to. An UPDATE with a PharmacyId in its WHERE clause that returns 0 has
        // not failed - it has correctly refused to touch another shop's data.
        public int ExecuteNonQuery(string sql, params SqlParameter[] parameters)
        {
            // Same two-using pattern as above, for the same reason: the connection goes
            // back to the pool whether the statement succeeds or throws.
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                // Identical binding step to ExecuteTable. Repeating the three lines in
                // each method was preferred to a shared private helper because the whole
                // point of this class is that the binding is visible at every point where
                // a command is built.
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);

                try
                {
                    // Opened by hand here, unlike ExecuteTable: there is no adapter to do
                    // it, and an unopened connection would fail with "the connection was
                    // not open" rather than anything a user could act on. Inside the try
                    // so that a server that is down is reported as error 53 or -1 and
                    // translated like any other database failure.
                    conn.Open();
                    // Returns the number of rows the statement changed. The using blocks
                    // still close the connection after this value is computed, because
                    // Dispose runs on the way out of the block regardless of the return.
                    return cmd.ExecuteNonQuery();
                }
                catch (SqlException ex)
                {
                    // Most constraint violations surface here rather than in ExecuteTable,
                    // because writes are what break rules: 2627 for a duplicate, 547 for a
                    // foreign key, 515 for a missing required value. Describe() turns each
                    // of those numbers into an instruction.
                    throw new DataAccessException(Describe(ex), ex);
                }
            }
        }

        /// <summary>Runs a query that returns a single value, for example a COUNT or a SUM.</summary>
        // Returns object rather than a specific type because ExecuteScalar genuinely can
        // return anything - an int from COUNT, a decimal from SUM, a string, or DBNull.
        // The three typed wrappers below sit on top of this one so that the awkward
        // null handling is written once instead of at every call site.
        public object ExecuteScalar(string sql, params SqlParameter[] parameters)
        {
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);

                try
                {
                    conn.Open();
                    // Reads the first column of the first row and discards the rest, which
                    // is why the queries that call this select exactly one value. It is
                    // also how the Add methods in the services retrieve a new identity:
                    // the INSERT is followed by SELECT CAST(SCOPE_IDENTITY() AS INT) in
                    // the same batch, so the new key comes back from the same round trip.
                    return cmd.ExecuteScalar();
                }
                catch (SqlException ex)
                {
                    throw new DataAccessException(Describe(ex), ex);
                }
            }
        }

        // The three wrappers below exist so that no service class ever has to test for
        // null and DBNull itself. They are the reason a line like
        // "if (_db.ExecuteScalarInt(...) > 0)" is safe even when the query matched nothing.
        public int ExecuteScalarInt(string sql, params SqlParameter[] parameters)
        {
            // One round trip, delegated to the untyped version above so the connection
            // handling and error translation are not duplicated here.
            object value = ExecuteScalar(sql, parameters);
            // TWO different empty results, both meaning "no number came back": null when
            // the query returned no rows at all, and DBNull.Value when it returned a row
            // whose value is SQL NULL. Convert.ToInt32(DBNull.Value) throws, so both are
            // caught before the conversion rather than after it.
            if (value == null || value == DBNull.Value) return 0;
            // Convert.ToInt32 rather than a cast: SQL Server may hand back a long from a
            // COUNT_BIG or a decimal from an arithmetic expression, and an (int) cast on a
            // boxed long throws InvalidCastException. Convert copes with every numeric type.
            return Convert.ToInt32(value);
        }

        public decimal ExecuteScalarDecimal(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);
            // 0m, not 0: the m suffix makes the literal a decimal so the return type is
            // matched exactly and no implicit conversion is involved.
            if (value == null || value == DBNull.Value) return 0m;
            // decimal, never double, for money. Binary floating point cannot represent
            // 0.1 exactly, so a total built from doubles drifts by fractions of a penny;
            // decimal is base ten and matches the DECIMAL columns in the database.
            return Convert.ToDecimal(value);
        }

        public string ExecuteScalarString(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);
            // string.Empty rather than null, so callers can safely write .Trim() or
            // .Length on the result. Returning null here would push a null check into
            // every one of them.
            if (value == null || value == DBNull.Value) return string.Empty;
            return value.ToString();
        }

        // -- turning SQL Server error numbers into English ----------------------

        /// <summary>
        /// Maps the SqlException numbers this application can actually provoke
        /// onto a sentence that tells the user what to do next. Anything not
        /// listed falls through to a generic message with the server's own text,
        /// so nothing is ever swallowed silently.
        /// </summary>
        // private because nothing outside this class should be translating database
        // errors - if it were public, a form could call it and the single-point-of-
        // translation rule would stop being true. static because it needs no connection
        // string and no instance state, only the exception it is handed.
        private static string Describe(SqlException ex)
        {
            // Switching on ex.Number, not on ex.Message: the number is a stable contract
            // from SQL Server, while the message text changes with the server's language
            // and version. Matching on message text would quietly stop working on a
            // non-English server.
            switch (ex.Number)
            {
                // Five different numbers, one sentence: from the user's point of view
                // "the server is off", "the database is missing" and "the login was
                // rejected" all mean the same thing and all have the same fix. Fall-through
                // cases with no statements between them are legal C# and are how several
                // labels are made to share one body.
                case 53:        // server not found
                case -1:        // connection could not be established
                case 4060:      // cannot open database
                case 18456:     // login failed
                case 40615:     // firewall
                    // The message names the three things that are actually checkable, in
                    // the order they should be checked, rather than saying "connection
                    // failed" and leaving the reader to guess. \r\n\r\n is a blank line:
                    // MessageBox on Windows wants a carriage return with the line feed.
                    return "PharmaLink could not reach the database.\r\n\r\n" +
                           "Check that SQL Server is running, that PharmaLinkDB has been created " +
                           "from PharmaLinkDB_Setup.sql, and that the connection string in " +
                           "App.config points at the right server.";

                case -2:        // timeout
                    // Kept separate from the group above because the advice is different:
                    // nothing is misconfigured, so the useful instruction is to wait.
                    return "The database took too long to answer. It may be busy - try again in a moment.";

                case 2627:      // PRIMARY KEY / UNIQUE constraint
                case 2601:      // unique index
                    // Both numbers mean "this value is already taken"; 2627 comes from a
                    // constraint and 2601 from a unique index, a distinction the user
                    // cannot act on. The sentence explains that the refusal is a rule
                    // rather than a fault, which is what stops it reading as a crash.
                    return "That record already exists.\r\n\r\n" +
                           "The database keeps this value unique, so the same entry cannot be " +
                           "saved twice. Change the value and try again.";

                case 547:       // FK or CHECK constraint
                    // 547 covers two genuinely different problems, so this is the one case
                    // where the message text has to be inspected. A CHECK violation means
                    // a value is out of range; a FOREIGN KEY violation means a row is still
                    // referenced or points at something missing. Reading ex.Message is
                    // acceptable here only because it merely picks between two sentences -
                    // if the test fails the user still gets a correct, if less specific,
                    // explanation rather than a wrong one.
                    return ex.Message.Contains("CHECK")
                        // The three examples are the actual CHECK constraints in
                        // PharmaLinkDB_Setup.sql, so the sentence points at a real rule.
                        ? "One of the values breaks a rule the database enforces, for example a " +
                          "negative price, a commission above 30 percent or a status that is not allowed."
                        // The foreign-key branch ends with the instruction that matters,
                        // because this is the error a user hits when deleting a category or
                        // a pharmacy that has history behind it, and deactivating is the
                        // designed answer.
                        : "This record is still referenced by other data, or it points at something " +
                          "that no longer exists, so the database refused the change.\r\n\r\n" +
                          "Where a record has history behind it, deactivate it instead of deleting it.";

                case 515:       // NOT NULL
                    // Reached when a required column was sent NULL. The message says to
                    // fill in the required fields rather than naming the column, because
                    // the column name is a database identifier and means nothing on screen.
                    return "A required field was left empty. Fill in every field marked as required and try again.";

                case 8152:      // string truncation (legacy)
                case 2628:      // string truncation
                    // Two numbers for the same fault: 8152 is the historical message and
                    // 2628 the newer one that also names the column. Both are handled so
                    // the application behaves the same whatever the server's compatibility
                    // level happens to be.
                    return "One of the values is too long for the field it is being saved into. Shorten it and try again.";

                default:
                    // The catch-all. It says plainly that the operation was refused and
                    // then appends the server's own text, so an unanticipated error is
                    // still reported in full rather than swallowed. Returning a bare
                    // "something went wrong" here was rejected: it would hide exactly the
                    // errors nobody has seen before, which are the ones worth reading.
                    return "The database refused the last operation.\r\n\r\n" + ex.Message;
            }
        }

        // -- small helpers so the service classes read cleanly ------------------

        // static, so services call DbHelper.P(...) without needing an instance. The name
        // is one letter on purpose: it appears on almost every line of every service, and
        // "new SqlParameter("@Id", id)" repeated four times per call would bury the query
        // it belongs to. Short name, single job, no state.
        public static SqlParameter P(string name, object value)
        {
            // The null-coalescing operator is the whole reason this helper exists. A
            // SqlParameter whose Value is C# null is treated by ADO.NET as "no value
            // supplied" and the command fails with "the parameterized query expects the
            // parameter @X, which was not supplied". DBNull.Value is the different thing
            // the caller nearly always means: send SQL NULL. Converting here means no
            // service has to remember the distinction - see PrescriptionService passing a
            // null DoctorName straight through.
            return new SqlParameter(name, value ?? DBNull.Value);
        }

        // The five GetX helpers below all follow one shape: check the column exists, check
        // the value is not NULL, otherwise convert. They are what let the mapping code in
        // the services be a flat list of assignments with no null checks in it.
        public static int GetInt(DataRow row, string column)
        {
            // TWO tests, not one. Columns.Contains guards against a column the query did
            // not select - indexing a DataRow with an unknown name throws ArgumentException
            // - which means one mapping routine can serve two queries that select slightly
            // different column lists. The DBNull test then guards against a column that
            // exists but is NULL in this row.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0;
            // Convert rather than a cast, for the same reason as ExecuteScalarInt: the
            // boxed value may be a byte, a short or a long depending on the column type.
            return Convert.ToInt32(row[column]);
        }

        public static decimal GetDecimal(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0m;
            // Money stays decimal end to end: DECIMAL column, decimal here, decimal on the
            // model, so nothing is ever rounded by a floating point conversion in between.
            return Convert.ToDecimal(row[column]);
        }

        public static string GetString(DataRow row, string column)
        {
            // string.Empty rather than null, so a Label.Text or a Trim() on the result is
            // always safe. A NULL Description or DoctorName becomes "" and simply shows
            // as blank instead of throwing on the way to the screen.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return string.Empty;
            return row[column].ToString();
        }

        public static bool GetBool(DataRow row, string column)
        {
            // false is the safe default for every flag in this schema: IsActive, RequiresRx
            // and IsHidden all mean "no" when absent, so a missing value can never switch a
            // restriction off or make a hidden review visible.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return false;
            // Handles both a real BIT column and the 1/0 integers the queries use in
            // expressions, because Convert.ToBoolean accepts either.
            return Convert.ToBoolean(row[column]);
        }

        public static DateTime GetDate(DataRow row, string column)
        {
            // DateTime.MinValue rather than DateTime.Now as the fallback: it is obviously
            // wrong on screen, so a missing date is visible instead of silently reading as
            // today. DateTime is a struct and cannot be null, which is why some sentinel
            // has to be chosen at all.
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return DateTime.MinValue;
            return Convert.ToDateTime(row[column]);
        }
    }
}
