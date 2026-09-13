using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by Program.Main as the application's entry
    //  form. Uses AuthService (login), UserSession (identity), UiTheme (styling)
    //  and Validator (field rules).
    //
    //  Flow for a sign in:
    //      btnLogin_Click -> ValidateFields -> AuthService.Login
    //                     -> fill UserSession -> OpenDashboardFor(UserType)
    //
    //  There is no SQL in this file. The login query lives in AuthService.Login,
    //  which is the rule every form here follows: validate, call a service, bind
    //  the result.
    // -------------------------------------------------------------------------

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
            ApplyTheme();          // colours, fonts and the demo account list on the left panel
            UserSession.Clear();   // wipe any previous identity, so a stale PharmacyId cannot leak
                                   // into the next session if someone logs out and back in
            txtEmail.Focus();      // cursor starts in the first field
            ValidateFields();      // runs once on an empty form purely to DISABLE the button
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
            bool ok = true;      // stays true only if every rule below passes

            // -- email --------------------------------------------------------
            // Empty is not an error yet: the user has not finished typing, so clear
            // the message but still block the button. Showing red on an untouched
            // form would be shouting at someone who has done nothing wrong.
            if (Validator.IsBlank(txtEmail.Text))
            {
                UiTheme.ClearError(lblEmailError, txtEmail);
                ok = false;
            }
            // Something was typed but it is not an email shape. Validator.IsEmail
            // uses the same rule as CK_Users_Email on the Users table, so what the
            // form refuses is exactly what the database would refuse.
            else if (!Validator.IsEmail(txtEmail.Text))
            {
                UiTheme.ShowError(lblEmailError, txtEmail, "That does not look like a valid email address.");
                ok = false;
            }
            else
            {
                UiTheme.ClearError(lblEmailError, txtEmail);   // valid, so remove any old message
            }

            // -- password -----------------------------------------------------
            if (Validator.IsBlank(txtPassword.Text))
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);
                ok = false;
            }
            // Six is the minimum length the sign up form enforces, so anything
            // shorter cannot match a real account and is refused before it costs a
            // database round trip.
            else if (txtPassword.Text.Length < 6)
            {
                UiTheme.ShowError(lblPasswordError, txtPassword, "Password must be at least 6 characters.");
                ok = false;
            }
            else
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);
            }

            // The button IS the validation result: disabled and grey until every rule
            // passes, so an obviously wrong attempt never reaches the database.
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
            // Re-check the field rules even though the button is only enabled when
            // they pass: AcceptButton means Enter can also reach this handler.
            if (!ValidateFields()) return;

            Cursor = Cursors.WaitCursor;                 // this is a round trip to SQL Server
            try
            {
                string reason;                           // the service writes the refusal text here

                // One call does the whole sign in: read the Users row by email,
                // recompute the hash with that row's own salt, compare it in memory,
                // then check the account status and, for an owner, the shop status.
                // Trim the email because a trailing space would fail the = comparison.
                User user = _auth.Login(txtEmail.Text.Trim(), txtPassword.Text, out reason);

                if (user == null)                        // null means refused; reason says why
                {
                    UiTheme.ShowError(lblFormError, null, reason);
                    txtPassword.SelectAll();             // select it so the next keystroke replaces it
                    txtPassword.Focus();
                    return;                              // stay on this form, nothing was opened
                }

                // Copy the identity into the static session. THIS is the line that makes
                // data isolation work: every Admin query later reads PharmacyId from here,
                // never from a control, so there is nothing on screen a user could edit
                // to see another pharmacy's rows.
                UserSession.UserId = user.UserId;
                UserSession.FullName = user.FullName;
                UserSession.Email = user.Email;
                UserSession.UserType = user.UserType;    // decides which dashboard opens next
                UserSession.PharmacyId = user.PharmacyId;   // 0 for SuperAdmin and Customer
                UserSession.PharmacyName = user.PharmacyName;

                OpenDashboardFor(user.UserType);         // the single role routing decision
            }
            catch (Exception ex)
            {
                // Reached when the database itself is unreachable. DbHelper has already
                // turned the SqlException into a readable sentence by this point.
                UiTheme.ShowError(lblFormError, null,
                    "Could not reach the database. Run PharmaLinkDB_Setup.sql first. (" + ex.Message + ")");
            }
            finally
            {
                Cursor = Cursors.Default;                // runs whether or not the login succeeded
            }
        }

        /// <summary>
        /// The single decision node of the whole navigation diagram.
        /// One value, three destinations, and nothing else in the application
        /// ever has to ask again who the user is.
        /// </summary>
        private void OpenDashboardFor(string userType)
        {
            Form dashboard;      // declared first, assigned by exactly one branch below

            // One string, three destinations. UserType comes from the Users table and
            // is constrained there by CK_Users_Type, so only these three values can
            // ever reach this switch.
            switch (userType)
            {
                case "SuperAdmin":
                    dashboard = new SuperAdminDashboard();   // platform operator
                    break;
                case "Admin":
                    dashboard = new AdminDashboard();        // pharmacy owner
                    break;
                case "Customer":
                    dashboard = new CustomerHomeForm();      // patient
                    break;
                default:
                    // Unreachable while the CHECK constraint holds, but a default that
                    // reports the problem beats one that silently opens nothing.
                    UiTheme.ShowError(lblFormError, null, "This account has an unknown user type.");
                    return;
            }

            Hide();                                          // keep this form alive, just invisible
            // Subscribe BEFORE showing: when the dashboard closes, the handler brings
            // this form back and clears the session. That is how logging out returns
            // here without the application ever exiting.
            dashboard.FormClosed += Dashboard_FormClosed;
            dashboard.Show();                                // non modal, so this method returns
        }

        /// <summary>Logging out closes the dashboard and brings this form back, cleared.</summary>
        private void Dashboard_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Fires when ANY of the three dashboards closes, which is what "log out"
            // actually does. Clearing the session here rather than in each dashboard
            // means there is exactly one place that can forget to do it.
            UserSession.Clear();
            txtPassword.Clear();               // never leave the last password in the box
            lblFormError.Visible = false;      // drop any refusal message from last time
            Show();                            // un hide the form that was hidden on sign in
            ValidateFields();                  // password is now empty, so disable the button again
            txtEmail.Focus();
        }

        private void btnGoSignUp_Click(object sender, EventArgs e)
        {
            // using() so the dialog is disposed even if it throws. SignUpForm is modal,
            // so execution stops on ShowDialog until the user finishes or cancels.
            using (SignUpForm signUp = new SignUpForm())
            {
                Hide();
                signUp.ShowDialog();           // blocks here
                Show();

                // RegisteredEmail is a property the dialog fills only for a CUSTOMER,
                // because a customer is created Active and can sign in immediately.
                // A pharmacy owner is created Pending and cannot, so the property stays
                // empty and nothing is pre-filled for them.
                if (!string.IsNullOrEmpty(signUp.RegisteredEmail))
                {
                    txtEmail.Text = signUp.RegisteredEmail;
                    txtPassword.Clear();
                    txtPassword.Focus();       // straight to the only field still empty
                }
                ValidateFields();
            }
        }
    }
}
