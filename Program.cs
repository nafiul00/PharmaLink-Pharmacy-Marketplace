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
            ApplicationConfiguration.Initialize();
            Application.Run(new LoginForm());
        }
    }
}
