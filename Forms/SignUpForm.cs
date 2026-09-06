using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Registration for both kinds of account (requirements 10 and 19).
    ///
    /// The "Register as" dropdown decides which of the two paths runs. A
    /// customer is created Active and can order immediately; a pharmacy owner
    /// is created Pending together with a Pending Pharmacies row, and neither
    /// becomes usable until the Super Admin has checked the drug licence.
    /// </summary>
    public partial class SignUpForm : Form
    {
        private readonly AuthService _auth = new AuthService();
        private readonly PharmacyService _pharmacies = new PharmacyService();

        /// <summary>Read by LoginForm so the new user's email is pre-filled after registration.</summary>
        public string RegisteredEmail { get; private set; } = "";

        private bool IsPharmacyOwner => cmbRegisterAs.SelectedIndex == 1;

        public SignUpForm()
        {
            InitializeComponent();
        }

        private void SignUpForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbRegisterAs.Items.Add("Customer  (patient buying medicine)");
            cmbRegisterAs.Items.Add("Pharmacy Owner  (shop selling medicine)");
            cmbRegisterAs.SelectedIndex = 0;

            LoadAreaSuggestions();
            ValidateAll();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Create account");

            panelHeader.BackColor = UiTheme.Primary;
            lblHeader.Font = UiTheme.FontTitle;
            lblHeader.ForeColor = Color.White;
            lblHeaderSub.Font = UiTheme.FontSmall;
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);

            grpPersonal.Font = UiTheme.FontHeading;
            grpPersonal.ForeColor = UiTheme.Primary;
            grpPersonal.BackColor = UiTheme.CardBack;

            grpPharmacy.Font = UiTheme.FontHeading;
            grpPharmacy.ForeColor = UiTheme.Primary;
            grpPharmacy.BackColor = UiTheme.CardBack;

            foreach (Control group in new Control[] { grpPersonal, grpPharmacy })
            {
                foreach (Control child in group.Controls)
                {
                    child.Font = UiTheme.FontBody;
                    child.ForeColor = UiTheme.TextDark;
                    if (child is Label label && label.Name.EndsWith("Error"))
                    {
                        label.Font = UiTheme.FontSmall;
                        label.ForeColor = UiTheme.Danger;
                    }
                }
            }

            lblPendingNote.Font = UiTheme.FontSmall;
            lblPendingNote.ForeColor = UiTheme.Warning;

            lblFormMessage.Font = UiTheme.FontSmall;
            lblFormMessage.ForeColor = UiTheme.Danger;

            UiTheme.StylePrimary(btnCreate);
            btnCreate.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnBack);
        }

        private void LoadAreaSuggestions()
        {
            cmbArea.Items.Clear();
            try
            {
                foreach (string area in _pharmacies.GetAreas(false))
                    cmbArea.Items.Add(area);
            }
            catch
            {
                // The area list is only a convenience; a fresh database has none yet.
            }

            foreach (string area in new[] { "Mitford", "Dhanmondi", "Mirpur", "Uttara", "Banani", "Gulshan", "Mohammadpur" })
            {
                if (!cmbArea.Items.Contains(area)) cmbArea.Items.Add(area);
            }
        }

        private void cmbRegisterAs_SelectedIndexChanged(object sender, EventArgs e)
        {
            grpPharmacy.Enabled = IsPharmacyOwner;
            grpPharmacy.ForeColor = IsPharmacyOwner ? UiTheme.Primary : UiTheme.TextMuted;
            lblAddress.Text = IsPharmacyOwner ? "Your personal address" : "Delivery address";
            btnCreate.Text = IsPharmacyOwner ? "Submit for approval" : "Create account";
            ValidateAll();
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        //  Each failure shows a red label directly under the offending field and
        //  keeps the Create button disabled. Every rule here is enforced again by
        //  a CHECK or UNIQUE constraint in the database.
        // ---------------------------------------------------------------------

        private void Field_Changed(object sender, EventArgs e)
        {
            lblFormMessage.Visible = false;
            ValidateAll();
        }

        private bool ValidateAll()
        {
            bool ok = true;

            ok &= Check(!Validator.IsBlank(txtFullName.Text), lblFullNameError, txtFullName,
                        "Please enter your full name.");

            if (Validator.IsBlank(txtEmail.Text))
            {
                UiTheme.ClearError(lblEmailError, txtEmail);
                ok = false;
            }
            else
            {
                ok &= Check(Validator.IsEmail(txtEmail.Text), lblEmailError, txtEmail,
                            "Enter a valid email address, for example name@example.com.");
            }

            if (Validator.IsBlank(txtPhone.Text))
            {
                UiTheme.ClearError(lblPhoneError, txtPhone);
                ok = false;
            }
            else
            {
                ok &= Check(Validator.IsMobile(txtPhone.Text), lblPhoneError, txtPhone,
                            "A mobile number is 11 digits and starts with 01.");
            }

            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "Please enter an address.");

            if (Validator.IsBlank(txtPassword.Text))
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);
                ok = false;
            }
            else
            {
                ok &= Check(Validator.IsStrongPassword(txtPassword.Text), lblPasswordError, txtPassword,
                            "Password needs at least 6 characters and at least one digit.");
            }

            if (Validator.IsBlank(txtConfirm.Text))
            {
                UiTheme.ClearError(lblConfirmError, txtConfirm);
                ok = false;
            }
            else
            {
                ok &= Check(txtConfirm.Text == txtPassword.Text, lblConfirmError, txtConfirm,
                            "The two passwords do not match.");
            }

            if (IsPharmacyOwner)
            {
                ok &= Check(!Validator.IsBlank(txtShopName.Text), lblShopNameError, txtShopName,
                            "Enter the trading name of your pharmacy.");
                ok &= Check(Validator.IsLicenseNo(txtLicenseNo.Text), lblLicenseError, txtLicenseNo,
                            "Enter your DGDA licence number, for example DGDA-DH-10021.");
                ok &= Check(!Validator.IsBlank(cmbArea.Text), lblAreaError, cmbArea,
                            "Choose or type the area your shop is in.");
                ok &= Check(!Validator.IsBlank(txtShopAddress.Text), lblShopAddressError, txtShopAddress,
                            "Enter the full postal address of the shop.");
                ok &= Check(!Validator.IsBlank(txtShopPhone.Text), lblShopPhoneError, txtShopPhone,
                            "Enter a contact number for the shop.");
            }
            else
            {
                foreach (Control child in grpPharmacy.Controls)
                {
                    if (child is Label label && label.Name.EndsWith("Error")) label.Visible = false;
                    if (child is TextBox || child is ComboBox) child.BackColor = Color.White;
                }
            }

            btnCreate.Enabled = ok;
            btnCreate.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        /// <summary>Shows or clears one field's error label and returns the rule's result.</summary>
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------
        //  SAVE
        // ---------------------------------------------------------------------

        private void btnCreate_Click(object sender, EventArgs e)
        {
            if (!ValidateAll()) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                // The UNIQUE constraints on Email and Phone are what actually stop
                // a duplicate account; checking first is only so the user gets a
                // friendly message instead of a database exception.
                if (_auth.EmailExists(txtEmail.Text))
                {
                    UiTheme.ShowError(lblEmailError, txtEmail, "An account with this email already exists.");
                    btnCreate.Enabled = false;
                    return;
                }

                if (_auth.PhoneExists(txtPhone.Text))
                {
                    UiTheme.ShowError(lblPhoneError, txtPhone, "This mobile number is already registered.");
                    btnCreate.Enabled = false;
                    return;
                }

                User user = new User
                {
                    FullName = txtFullName.Text,
                    Email = txtEmail.Text,
                    Phone = txtPhone.Text,
                    Address = txtAddress.Text
                };

                if (IsPharmacyOwner)
                {
                    if (_auth.LicenseExists(txtLicenseNo.Text))
                    {
                        UiTheme.ShowError(lblLicenseError, txtLicenseNo,
                            "This licence number is already registered to another pharmacy.");
                        btnCreate.Enabled = false;
                        return;
                    }

                    Pharmacy pharmacy = new Pharmacy
                    {
                        PharmacyName = txtShopName.Text,
                        LicenseNo = txtLicenseNo.Text,
                        Area = cmbArea.Text,
                        Address = txtShopAddress.Text,
                        ContactPhone = txtShopPhone.Text
                    };

                    _auth.RegisterPharmacyOwner(user, pharmacy, txtPassword.Text);

                    MessageBox.Show(
                        "Your pharmacy registration has been submitted.\r\n\r\n" +
                        "Both your account and " + pharmacy.PharmacyName + " are held at status Pending. " +
                        "The Super Admin will check licence " + pharmacy.LicenseNo + " and approve the shop, " +
                        "after which you will be able to log in and list your medicines.",
                        "Submitted for approval", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _auth.RegisterCustomer(user, txtPassword.Text);
                    RegisteredEmail = user.Email.Trim();

                    MessageBox.Show(
                        "Welcome to PharmaLink, " + user.FullName + ".\r\n\r\n" +
                        "Your account is active. You can sign in and start ordering right away.",
                        "Account created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                UiTheme.ShowError(lblFormMessage, null, "The account could not be created: " + ex.Message);
                lblFormMessage.Visible = true;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
