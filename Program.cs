using Microsoft.Data.SqlClient;
using PharmaLinkApp.Database;
using PharmaLinkApp.Forms;

namespace PharmaLinkApp
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        ///  LoginForm is the single entry point for all three roles
        ///  (SuperAdmin, Admin / pharmacy owner and Customer).
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Anything a form fails to catch arrives here instead of killing the
            // process with the .NET crash dialog. By far the most likely cause is
            // SQL Server not running, so that case gets its own message.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ReportFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportFatal(e.ExceptionObject as Exception);

            ApplicationConfiguration.Initialize();
            Application.Run(new LoginForm());
        }

        /// <summary>
        /// Turns an unhandled exception into one message the user can act on.
        /// </summary>
        private static void ReportFatal(Exception ex)
        {
            string message;

            if (ex is DataAccessException)
            {
                // DbHelper has already translated the SqlException into English.
                message = ex.Message;
            }
            else if (ex is SqlException sql)
            {
                // A SqlException that did not come through DbHelper, which means
                // a service opened its own connection for a transaction.
                message = sql.Number == 53 || sql.Number == 4060 ||
                          sql.Number == 18456 || sql.Number == -2 || sql.Number == -1
                    ? "PharmaLink could not reach the database.\r\n\r\n" +
                      "Check that SQL Server is running, that PharmaLinkDB has been created from " +
                      "PharmaLinkDB_Setup.sql, and that the connection string in App.config points " +
                      "at the right server.\r\n\r\n" + sql.Message
                    : "The database refused the last operation.\r\n\r\n" + sql.Message;
            }
            else
            {
                message = "PharmaLink hit an unexpected problem.\r\n\r\n" +
                          (ex == null ? "No further detail is available." : ex.Message);
            }

            MessageBox.Show(message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
