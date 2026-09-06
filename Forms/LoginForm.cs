using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// The single entry point of the application.
    ///
    /// There is no separate administrator login. All three roles type their
    /// email and password into this one form; the login query returns the
    /// UserType and that single value decides which of the three dashboards
    /// opens. No arrow in the navigation diagram ever crosses from one role
    /// branch into another.
    /// </summary>
    public partial class LoginForm : Form
    {
        private readonly AuthService _auth = new AuthService();

        public LoginForm()
        {
            InitializeComponent();
        }

        private void LoginForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            UserSession.Clear();
            txtEmail.Focus();
            ValidateFields();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Sign in");

            // ---- the branding panel on the left ----
            panelBrand.BackColor = UiTheme.Primary;

            lblBrandMark.Font = new Font("Segoe UI", 30F, FontStyle.Bold);
            lblBrandMark.ForeColor = Color.FromArgb(150, 220, 200);

            lblBrandName.Font = new Font("Segoe UI Semibold", 28F, FontStyle.Bold);
            lblBrandName.ForeColor = Color.White;

            lblTagline.Font = new Font("Segoe UI", 11F);
            lblTagline.ForeColor = Color.FromArgb(185, 225, 212);

            lblBrandBlurb.Font = UiTheme.FontBody;
            lblBrandBlurb.ForeColor = Color.FromArgb(210, 235, 226);

            lblDemoTitle.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
            lblDemoTitle.ForeColor = Color.FromArgb(150, 220, 200);

            lblDemoAccounts.Font = UiTheme.FontMono;
            lblDemoAccounts.ForeColor = Color.FromArgb(220, 240, 234);
            lblDemoAccounts.Text =
                "Super Admin  admin@pharmalink.com.bd   Admin@123" + Environment.NewLine +
                "Pharmacy     kamrul@mitfordpharma.com  Pharma@123" + Environment.NewLine +
                "Pharmacy     shirin@dhanmondimedico.com" + Environment.NewLine +
                "                                       Pharma@123" + Environment.NewLine +
                "Pharmacy     tanvir@lazzcare.com       Pharma@123" + Environment.NewLine +
                "Customer     rahim@gmail.com           Cust@123" + Environment.NewLine +
                "Customer     nusrat@gmail.com          Cust@123" + Environment.NewLine +
                Environment.NewLine +
                "imran@newlifepharmacy.com is still Pending and" + Environment.NewLine +
                "is refused until the Super Admin approves it.";

            // ---- the login card on the right ----
            panelCard.BackColor = UiTheme.CardBack;
            panelCard.BorderStyle = BorderStyle.FixedSingle;

            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = UiTheme.TextDark;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = UiTheme.TextMuted;

            lblEmail.Font = UiTheme.FontBody;
            lblPassword.Font = UiTheme.FontBody;
            lblEmailError.Font = UiTheme.FontSmall;
            lblEmailError.ForeColor = UiTheme.Danger;
            lblPasswordError.Font = UiTheme.FontSmall;
            lblPasswordError.ForeColor = UiTheme.Danger;
            lblFormError.Font = UiTheme.FontSmall;
            lblFormError.ForeColor = UiTheme.Danger;

            lblNoAccount.Font = UiTheme.FontSmall;
            lblNoAccount.ForeColor = UiTheme.TextMuted;

            UiTheme.StylePrimary(btnLogin);
            btnLogin.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnGoSignUp);

            AcceptButton = btnLogin;
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        //  Both rules are checked as the user types. The Login button stays
        //  disabled until the email looks like an email and the password is at
        //  least six characters long, so an obviously wrong attempt never even
        //  reaches the database.
        // ---------------------------------------------------------------------

        private void Field_Changed(object sender, EventArgs e)
        {
            lblFormError.Visible = false;
            ValidateFields();
        }

        private bool ValidateFields()
        {
            bool ok = true;

            if (Validator.IsBlank(txtEmail.Text))
            {
                UiTheme.ClearError(lblEmailError, txtEmail);
                ok = false;
            }
            else if (!Validator.IsEmail(txtEmail.Text))
            {
                UiTheme.ShowError(lblEmailError, txtEmail, "That does not look like a valid email address.");
                ok = false;
            }
            else
            {
                UiTheme.ClearError(lblEmailError, txtEmail);
            }

            if (Validator.IsBlank(txtPassword.Text))
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);
                ok = false;
            }
            else if (txtPassword.Text.Length < 6)
            {
                UiTheme.ShowError(lblPasswordError, txtPassword, "Password must be at least 6 characters.");
                ok = false;
            }
            else
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);
            }

            btnLogin.Enabled = ok;
            btnLogin.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            txtPassword.PasswordChar = chkShowPassword.Checked ? '\0' : '*';
        }

        // ---------------------------------------------------------------------
        //  LOGIN AND ROLE ROUTING
        // ---------------------------------------------------------------------

        private void btnLogin_Click(object sender, EventArgs e)
        {
            if (!ValidateFields()) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                string reason;
                User user = _auth.Login(txtEmail.Text.Trim(), txtPassword.Text, out reason);

                if (user == null)
                {
                    UiTheme.ShowError(lblFormError, null, reason);
                    txtPassword.SelectAll();
                    txtPassword.Focus();
                    return;
                }

                // The session is what every later query filters on.
                UserSession.UserId = user.UserId;
                UserSession.FullName = user.FullName;
                UserSession.Email = user.Email;
                UserSession.UserType = user.UserType;
                UserSession.PharmacyId = user.PharmacyId;
                UserSession.PharmacyName = user.PharmacyName;

                OpenDashboardFor(user.UserType);
            }
            catch (Exception ex)
            {
                UiTheme.ShowError(lblFormError, null,
                    "Could not reach the database. Run PharmaLinkDB_Setup.sql first. (" + ex.Message + ")");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// The single decision node of the whole navigation diagram.
        /// One value, three destinations, and nothing else in the application
        /// ever has to ask again who the user is.
        /// </summary>
        private void OpenDashboardFor(string userType)
        {
            Form dashboard;

            switch (userType)
            {
                case "SuperAdmin":
                    dashboard = new SuperAdminDashboard();
                    break;
                case "Admin":
                    dashboard = new AdminDashboard();
                    break;
                case "Customer":
                    dashboard = new CustomerHomeForm();
                    break;
                default:
                    UiTheme.ShowError(lblFormError, null, "This account has an unknown user type.");
                    return;
            }

            Hide();
            dashboard.FormClosed += Dashboard_FormClosed;
            dashboard.Show();
        }

        /// <summary>Logging out closes the dashboard and brings this form back, cleared.</summary>
        private void Dashboard_FormClosed(object sender, FormClosedEventArgs e)
        {
            UserSession.Clear();
            txtPassword.Clear();
            lblFormError.Visible = false;
            Show();
            ValidateFields();
            txtEmail.Focus();
        }

        private void btnGoSignUp_Click(object sender, EventArgs e)
        {
            using (SignUpForm signUp = new SignUpForm())
            {
                Hide();
                signUp.ShowDialog();
                Show();

                // A brand new customer lands straight back here with the email
                // already typed in, so the first login is one click away.
                if (!string.IsNullOrEmpty(signUp.RegisteredEmail))
                {
                    txtEmail.Text = signUp.RegisteredEmail;
                    txtPassword.Clear();
                    txtPassword.Focus();
                }
                ValidateFields();
            }
        }
    }
}
