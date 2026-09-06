using System.Configuration;
using System.Data;
using Microsoft.Data.SqlClient;

namespace PharmaLinkApp.Database
{
    /// <summary>
    /// The one place in the application that knows how to reach SQL Server.
    ///
    /// Every method here takes the SQL text and an array of SqlParameter values.
    /// Nothing is ever concatenated into a query string, which is what keeps the
    /// whole system safe from SQL injection: a customer who types
    /// "'; DROP TABLE Users; --" into the search box searches for that literal
    /// text and finds nothing.
    /// </summary>
    public class DbHelper
    {
        private readonly string _connectionString =
            ConfigurationManager.ConnectionStrings["db"].ConnectionString;

        public SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }

        /// <summary>Runs a SELECT and returns the whole result set as a DataTable, ready for a DataGridView.</summary>
        public DataTable ExecuteTable(string sql, params SqlParameter[] parameters)
        {
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);

                DataTable table = new DataTable();
                using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                {
                    adapter.Fill(table);
                }
                return table;
            }
        }

        /// <summary>Runs an INSERT, UPDATE or DELETE and returns how many rows it changed.</summary>
        public int ExecuteNonQuery(string sql, params SqlParameter[] parameters)
        {
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);

                conn.Open();
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Runs a query that returns a single value, for example a COUNT or a SUM.</summary>
        public object ExecuteScalar(string sql, params SqlParameter[] parameters)
        {
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);

                conn.Open();
                return cmd.ExecuteScalar();
            }
        }

        public int ExecuteScalarInt(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);
            if (value == null || value == DBNull.Value) return 0;
            return Convert.ToInt32(value);
        }

        public decimal ExecuteScalarDecimal(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);
            if (value == null || value == DBNull.Value) return 0m;
            return Convert.ToDecimal(value);
        }

        public string ExecuteScalarString(string sql, params SqlParameter[] parameters)
        {
            object value = ExecuteScalar(sql, parameters);
            if (value == null || value == DBNull.Value) return string.Empty;
            return value.ToString();
        }

        // -- small helpers so the service classes read cleanly ------------------

        public static SqlParameter P(string name, object value)
        {
            return new SqlParameter(name, value ?? DBNull.Value);
        }

        public static int GetInt(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0;
            return Convert.ToInt32(row[column]);
        }

        public static decimal GetDecimal(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0m;
            return Convert.ToDecimal(row[column]);
        }

        public static string GetString(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return string.Empty;
            return row[column].ToString();
        }

        public static bool GetBool(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return false;
            return Convert.ToBoolean(row[column]);
        }

        public static DateTime GetDate(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return DateTime.MinValue;
            return Convert.ToDateTime(row[column]);
        }
    }
}
