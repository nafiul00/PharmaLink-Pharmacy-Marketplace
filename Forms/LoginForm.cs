using System.Drawing;               // Font and Color, used by ApplyTheme
using System.Windows.Forms;         // Form, Cursors and the control types touched here
using PharmaLinkApp.Helpers;        // UiTheme for colours and errors, Validator for rules
using PharmaLinkApp.Models;         // User, the object AuthService.Login hands back
using PharmaLinkApp.Services;       // AuthService, which owns the login query

// All screens share one namespace so each form can use another's short name.
namespace PharmaLinkApp.Forms
{
    /// <summary>Entry form: all three roles sign in here.</summary>
    public partial class LoginForm : Form
    {
        // AuthService keeps no state and no open connection, so one field is enough.
        private readonly AuthService _auth = new AuthService();

        // The constructor only builds the window; anything that can fail waits for Load.
        public LoginForm()
        {
            InitializeComponent();   // designer generated: creates every control
        }

        // Load fires after every control exists, so touching them here is safe.
        private void LoginForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();          // colours, fonts and the demo account list
            UserSession.Clear();   // drop the old identity so a stale PharmacyId cannot leak
            txtEmail.Focus();      // cursor starts in the first field
            ValidateFields();      // on an empty form this only disables the button
        }

        /// <summary>Applies the shared palette and makes Enter submit the form.</summary>
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Sign in");   // one call sets title, background and window rules

            // ---- branding panel on the left ----
            panelBrand.BackColor = UiTheme.Primary;

            lblBrandMark.Font = new Font("Segoe UI", 30F, FontStyle.Bold);       // the large glyph
            lblBrandMark.ForeColor = Color.FromArgb(150, 220, 200);              // pale mint on the dark fill

            lblBrandName.Font = new Font("Segoe UI Semibold", 28F, FontStyle.Bold);   // largest text on screen
            lblBrandName.ForeColor = Color.White;                                     // white for maximum contrast

            lblTagline.Font = new Font("Segoe UI", 11F);          // one step below the product name
            lblTagline.ForeColor = Color.FromArgb(185, 225, 212); // dimmer: supporting text

            lblBrandBlurb.Font = UiTheme.FontBody;                   // the shared body font
            lblBrandBlurb.ForeColor = Color.FromArgb(210, 235, 226); // brightest grey, meant to be read

            lblDemoTitle.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);   // heading for the list below
            lblDemoTitle.ForeColor = Color.FromArgb(150, 220, 200);                    // same mint as the brand mark

            lblDemoAccounts.Font = UiTheme.FontMono;                    // fixed width, so the columns line up
            lblDemoAccounts.ForeColor = Color.FromArgb(220, 240, 234);  // near white, these get copied by eye
            lblDemoAccounts.Text =                                                                   // the seeded demo logins
                "Super Admin  admin@pharmalink.com.bd   Admin@123" + Environment.NewLine +           // the only role that can approve a shop
                "Pharmacy     kamrul@mitfordpharma.com  Pharma@123" + Environment.NewLine +          // already Approved, reaches its dashboard
                "Pharmacy     shirin@dhanmondimedico.com" + Environment.NewLine +                    // too long to share a line with its password
                "                                       Pharma@123" + Environment.NewLine +          // the wrapped password for the row above
                "Pharmacy     tanvir@lazzcare.com       Pharma@123" + Environment.NewLine +          // a third shop, for comparing two owners
                "Customer     rahim@gmail.com           Cust@123" + Environment.NewLine +            // a customer, who owns no PharmacyId
                "Customer     nusrat@gmail.com          Cust@123" + Environment.NewLine +            // a second customer, so two baskets differ
                Environment.NewLine +                                                                // blank line before the account meant to fail
                "imran@newlifepharmacy.com is still Pending and" + Environment.NewLine +             // naming it makes the refusal reproducible
                "is refused until the Super Admin approves it.";                                     // that refusal comes from AuthService

            // ---- login card on the right ----
            panelCard.BackColor = UiTheme.CardBack;
            panelCard.BorderStyle = BorderStyle.FixedSingle;   // a hairline lifts the card off the background

            lblTitle.Font = UiTheme.FontTitle;         // the shared title font, so headings match
            lblTitle.ForeColor = UiTheme.TextDark;     // the darkest token, reserved for headings
            lblSubtitle.Font = UiTheme.FontSmall;      // one size down, so it reads as secondary
            lblSubtitle.ForeColor = UiTheme.TextMuted; // muted, never competes with the title

            lblEmail.Font = UiTheme.FontBody;          // field captions use the body font
            lblPassword.Font = UiTheme.FontBody;       // same treatment, so both read as one group
            lblEmailError.Font = UiTheme.FontSmall;    // small, so showing it does not shift the layout
            lblEmailError.ForeColor = UiTheme.Danger;  // one Danger token, so every error is the same red
            lblPasswordError.Font = UiTheme.FontSmall; // same size, because both errors can show at once
            lblPasswordError.ForeColor = UiTheme.Danger;   // the same red, not a second severity
            lblFormError.Font = UiTheme.FontSmall;         // the message for faults owned by no one field
            lblFormError.ForeColor = UiTheme.Danger;       // styled once, so the handler only sets Text

            lblNoAccount.Font = UiTheme.FontSmall;         // the quiet prompt beside the sign up button
            lblNoAccount.ForeColor = UiTheme.TextMuted;    // muted: an aside, not an instruction

            UiTheme.StylePrimary(btnLogin);                                         // filled look for the main action
            btnLogin.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);   // after StylePrimary, which sets its own font
            UiTheme.StyleSecondary(btnGoSignUp);                                    // secondary, so the buttons do not compete

            AcceptButton = btnLogin;   // Enter now raises btnLogin.Click, which is why it re-validates
        }

        // Wired to TextChanged on both boxes, so it runs on every keystroke in either.
        private void Field_Changed(object sender, EventArgs e)
        {
            lblFormError.Visible = false;   // last attempt's refusal is stale once a key is pressed
            ValidateFields();               // re-decide the button state after every keystroke
        }

        // Returns the verdict and paints it, so labels and button never disagree.
        private bool ValidateFields()
        {
            bool ok = true;      // stays true only if every rule below passes

            // ---- email ----
            if (Validator.IsBlank(txtEmail.Text))
            {
                UiTheme.ClearError(lblEmailError, txtEmail);   // blank is not an error, just not ready
                ok = false;                                    // blank still blocks the button
            }
            // Validator.IsEmail uses the same shape rule as CK_Users_Email on the table.
            else if (!Validator.IsEmail(txtEmail.Text))
            {
                // ShowError writes the label, shows it and tints the box in one call.
                UiTheme.ShowError(lblEmailError, txtEmail, "That does not look like a valid email address.");
                ok = false;                                    // a bad shape matches no Users row
            }
            else   // typed, and it satisfies the shape rule the Users table enforces
            {
                UiTheme.ClearError(lblEmailError, txtEmail);   // valid, so remove any old message
            }

            // ---- password ----
            if (Validator.IsBlank(txtPassword.Text))
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);   // nothing typed, nothing to report
                ok = false;                                          // an empty password is never correct
            }
            // Six is the minimum sign up enforces, so shorter cannot match an account.
            else if (txtPassword.Text.Length < 6)
            {
                // Length only: sign up owns the full strength rule; login rejects the impossible.
                UiTheme.ShowError(lblPasswordError, txtPassword, "Password must be at least 6 characters.");
                ok = false;                                          // too short to be any stored password
            }
            else   // six characters or more, which is all login needs to establish
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);   // clear the length message
            }

            // The button is the verdict: disabled until both rules pass.
            btnLogin.Enabled = ok;
            // Enabled greys the text only, so set BackColor too or it still looks pressable.
            btnLogin.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;   // hand the same verdict the user can see back to btnLogin_Click
        }

        // Wired to CheckedChanged, so it fires on both the tick and the untick.
        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            txtPassword.PasswordChar = chkShowPassword.Checked ? '\0' : '*';   // '\0' means no mask
        }

        // The only handler that calls a service and the only one that leaves this form.
        private void btnLogin_Click(object sender, EventArgs e)
        {
            if (!ValidateFields()) return;   // Enter also reaches here via AcceptButton, so re-check

            Cursor = Cursors.WaitCursor;                 // this is a round trip to SQL Server
            try   // everything below reaches the network, so a failure must become a message
            {
                string reason;                           // the service writes its refusal text here

                // Reads the row, rehashes with its salt, compares; email trimmed, password not.
                User user = _auth.Login(txtEmail.Text.Trim(), txtPassword.Text, out reason);

                if (user == null)                        // null means refused; reason says why
                {
                    UiTheme.ShowError(lblFormError, null, reason);   // null: no one box to blame
                    txtPassword.SelectAll();             // select it, so the next keystroke replaces it
                    txtPassword.Focus();                 // caret inside that selection, no click needed
                    return;                              // stay on this form, nothing was opened
                }

                // Filling UserSession here is what scopes every later query to this user.
                UserSession.UserId = user.UserId;
                UserSession.FullName = user.FullName;    // shown in headers, never used in a WHERE
                UserSession.Email = user.Email;          // so My Profile shows the address signed in with
                UserSession.UserType = user.UserType;    // decides which dashboard opens next
                UserSession.PharmacyId = user.PharmacyId;   // 0 for SuperAdmin and Customer
                UserSession.PharmacyName = user.PharmacyName;   // header caption for a pharmacy owner

                OpenDashboardFor(user.UserType);         // the single role routing decision
            }
            catch (Exception ex)   // the outermost layer of a click: nothing may escape it
            {
                // Reached when the database is unreachable, not when a login is refused.
                UiTheme.ShowError(lblFormError, null,
                    "Could not reach the database. Run PharmaLinkDB_Setup.sql first. (" + ex.Message + ")");   // ex.Message names the real fault
            }
            finally   // runs on success, on the early return and on the caught exception alike
            {
                Cursor = Cursors.Default;                // restore the pointer whatever happened
            }
        }

        /// <summary>The one place a role is turned into a destination screen.</summary>
        private void OpenDashboardFor(string userType)
        {
            Form dashboard;      // the base type, so one variable holds any of the three

            // UserType comes from the Users table, where CK_Users_Type allows three values.
            switch (userType)
            {
                case "SuperAdmin":                           // the platform operator, owns no shop
                    dashboard = new SuperAdminDashboard();   // platform operator
                    break;                                   // every case breaks: no fall through
                case "Admin":                                // a pharmacy owner, scoped by PharmacyId
                    dashboard = new AdminDashboard();        // pharmacy owner
                    break;                                   // leaves the switch with dashboard set
                case "Customer":                             // a patient, who browses every shop
                    dashboard = new CustomerHomeForm();      // patient
                    break;                                   // the third value the CHECK permits
                default:                                     // only reachable if that CHECK were dropped
                    // Unreachable in practice, but reporting beats opening nothing at all.
                    UiTheme.ShowError(lblFormError, null, "This account has an unknown user type.");
                    return;                                  // returns, so the unassigned variable is never read
            }

            Hide();                                          // not Close: this form owns the message loop
            // Subscribe before showing, so closing the dashboard brings this form back.
            dashboard.FormClosed += Dashboard_FormClosed;
            dashboard.Show();                                // Show, not ShowDialog, so this method returns
        }

        /// <summary>Closing a dashboard returns to this form, cleared.</summary>
        private void Dashboard_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Fires however a dashboard closes: the Log out button, the red X or Alt+F4.
            UserSession.Clear();
            txtPassword.Clear();               // never leave the last password in the box
            lblFormError.Visible = false;      // drop the refusal message from last time
            Show();                            // un hide the form that was hidden at sign in
            ValidateFields();                  // password is empty again, so grey the button
            txtEmail.Focus();                  // ready for the next sign in
        }

        // Opens the sign up screen and picks up the email it registered, if any.
        private void btnGoSignUp_Click(object sender, EventArgs e)
        {
            // using(), because a form shown with ShowDialog is not disposed when it closes.
            using (SignUpForm signUp = new SignUpForm())
            {
                Hide();                        // hidden, never closed
                signUp.ShowDialog();           // modal, so execution stops on this line
                Show();                        // reached only once the dialog has closed

                // RegisteredEmail is filled for a customer only; an owner starts out Pending.
                if (!string.IsNullOrEmpty(signUp.RegisteredEmail))
                {
                    txtEmail.Text = signUp.RegisteredEmail;   // pre-fill, so only a password is typed
                    txtPassword.Clear();                      // never pre-fill a password box
                    txtPassword.Focus();       // straight to the only field still empty
                }
                ValidateFields();              // outside the if, so cancelling resets the button too
            }
        }
    }
}
