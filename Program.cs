using Microsoft.Data.SqlClient;     // SqlException, so a database failure can be named
using PharmaLinkApp.Database;       // DataAccessException, the translated form of one
using PharmaLinkApp.Forms;          // LoginForm, the window the application opens with

namespace PharmaLinkApp
{
    // internal and static: this class is not part of any public surface and is never
    // instantiated. It exists only to hold Main, the method the runtime calls first.
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        ///  LoginForm is the single entry point for all three roles
        ///  (SuperAdmin, Admin / pharmacy owner and Customer).
        /// </summary>
        // [STAThread] declares the entry point single threaded apartment, which Windows
        // requires for the COM components behind common dialogs such as the file picker
        // used to choose a prescription image. Without it those dialogs fail at runtime.
        [STAThread]
        static void Main()
        {
            // Anything a form fails to catch arrives here instead of killing the
            // process with the .NET crash dialog. By far the most likely cause is
            // SQL Server not running, so that case gets its own message.
            // Route exceptions the UI thread failed to catch to our own handler instead
            // of letting .NET show its crash dialog and kill the process.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // A lambda, so the handler is written where it is wired up. Both parameters
            // are required by the event's signature; only the exception is of interest.
            Application.ThreadException += (sender, e) => ReportFatal(e.Exception);

            // The second net, for anything thrown OFF the UI thread. Both are wired
            // because they cover different cases and neither catches the other's.
            // ExceptionObject is typed as object here rather than Exception, so it is cast
            // with "as": that yields null instead of throwing if it is something else,
            // and ReportFatal is written to cope with null.
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportFatal(e.ExceptionObject as Exception);

            ApplicationConfiguration.Initialize();   // high DPI and default font settings

            // LoginForm is the single entry point for all three roles, and passing it to
            // Run() makes it the MAIN form: when it finally closes the message loop ends
            // and the application exits. That is why logging out hides this form and
            // shows it again rather than closing it.
            Application.Run(new LoginForm());
        }

        /// <summary>
        /// Turns an unhandled exception into one message the user can act on.
        /// </summary>
        private static void ReportFatal(Exception ex)
        {
            // Built up in three branches and shown once at the end, so there is exactly
            // one message box however the failure arrived.
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
                // "is SqlException sql" is a pattern match: it tests the type and gives
                // the typed variable in one step, which is what makes sql.Number readable
                // on the next line without a separate cast.
                //
                // Those five numbers are the connection level failures: 53 is server not
                // found, 4060 is the database missing, 18456 is login rejected, and the
                // two negatives are timeouts. They all mean the same thing to the user -
                // the database could not be reached - so they share one message naming
                // the three things worth checking. Anything else is a statement the
                // database understood and refused, which is a different problem.
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
                // Everything else. The null check matters because the AppDomain handler
                // above can pass null when what was thrown was not an Exception at all,
                // and reading ex.Message there would throw inside the crash handler.
                message = "PharmaLink hit an unexpected problem.\r\n\r\n" +
                          (ex == null ? "No further detail is available." : ex.Message);
            }

            // One dialog, with the error icon, and no Cancel button: there is nothing for
            // the user to decide here, only something to read.
            MessageBox.Show(message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
