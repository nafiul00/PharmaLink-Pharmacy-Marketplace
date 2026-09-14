using Microsoft.Data.SqlClient;     // SqlException, so a database failure can be named
using PharmaLinkApp.Database;       // DataAccessException, the translated form of one
using PharmaLinkApp.Forms;          // LoginForm, the window the application opens with

// Root namespace: the entry point starts every feature, so it lives here.
namespace PharmaLinkApp
{
    // internal static: never instantiated, it only holds Main.
    internal static class Program
    {
        /// <summary>Entry point; LoginForm serves all three roles.</summary>
        [STAThread]   // single threaded apartment, required by the COM file-picker dialog
        static void Main()   // no parameters and no return: nothing here reads a command line
        {
            // Send what the UI thread failed to catch here, not to the .NET crash dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // A lambda, so the handler sits where it is wired up; only the exception matters.
            Application.ThreadException += (sender, e) => ReportFatal(e.Exception);

            // Second net, for throws OFF the UI thread; "as" gives null instead of throwing.
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportFatal(e.ExceptionObject as Exception);

            ApplicationConfiguration.Initialize();   // high DPI and default font settings

            // Passed to Run, LoginForm is the MAIN form: closing it ends the message loop.
            Application.Run(new LoginForm());
        }

        /// <summary>One actionable message from an unhandled exception.</summary>
        private static void ReportFatal(Exception ex)
        {
            // Set by three branches and shown once, so only one message box ever appears.
            string message;

            // Most specific first: DbHelper already translated this one, so reuse its text.
            if (ex is DataAccessException)
            {
                message = ex.Message;   // already plain English, nothing to re-derive
            }
            else if (ex is SqlException sql)   // a raw driver error that never passed through DbHelper
            {
                // Pattern match types the variable in one step, so sql.Number needs no cast.
                message = sql.Number == 53 || sql.Number == 4060 ||                          // 53 no server, 4060 no database
                          sql.Number == 18456 || sql.Number == -2 || sql.Number == -1   // login rejected, then the two timeouts
                    ? "PharmaLink could not reach the database.\r\n\r\n" +                // reachability branch: the server was never spoken to
                      "Check that SQL Server is running, that PharmaLinkDB has been created from " +   // first two things to check, in failure order
                      "PharmaLinkDB_Setup.sql, and that the connection string in App.config points " + // names the script, so the fix is one step
                      "at the right server.\r\n\r\n" + sql.Message                        // the driver's own text last, for anyone who can read it
                    : "The database refused the last operation.\r\n\r\n" + sql.Message;   // connected fine, the statement itself was rejected
            }
            // Catch-all arm, so message is always assigned before it is read.
            else
            {
                message = "PharmaLink hit an unexpected problem.\r\n\r\n" +   // null is possible: the AppDomain handler may pass a non-Exception
                          (ex == null ? "No further detail is available." : ex.Message);   // say nothing is known rather than crash inside the handler
            }

            // One dialog, error icon, OK only: there is nothing here for the user to decide.
            MessageBox.Show(message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
