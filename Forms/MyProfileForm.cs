using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirements 17, 27 and 30. The same form serves all three roles,
    /// because "edit my own details and change my own password" is the same job
    /// whoever is doing it.
    ///
    /// Email is read only: it is the login identifier. The password change
    /// verifies the current password inside the same UPDATE statement, so a
    /// wrong entry updates no rows and the form reports failure without the
    /// stored hash ever being compared in memory.
    /// </summary>
    public partial class MyProfileForm : Form
    {
        private readonly AuthService _auth = new AuthService();
        private bool _loading = true;

        public MyProfileForm()
        {
            InitializeComponent();
        }

        private void MyProfileForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadProfile();
            _loading = false;
            ValidateProfile();
            ValidatePassword();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Account");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            foreach (GroupBox group in new[] { grpProfile, grpPassword })
            {
                group.Font = UiTheme.FontHeading;
                group.ForeColor = UiTheme.Primary;
                group.BackColor = UiTheme.CardBack;

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

            txtEmail.BackColor = Color.FromArgb(240, 242, 244);
            lblEmailNote.Font = UiTheme.FontSmall;
            lblEmailNote.ForeColor = UiTheme.TextMuted;
            lblHashNote.Font = UiTheme.FontSmall;
            lblHashNote.ForeColor = UiTheme.TextMuted;
            lblMemberSince.Font = UiTheme.FontSmall;
            lblMemberSince.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StylePrimary(btnSaveProfile);
            UiTheme.StyleAccent(btnChangePassword);
            btnSaveProfile.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            btnChangePassword.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        }

        private void LoadProfile()
        {
            User user = _auth.GetUser(UserSession.UserId);
            if (user == null) return;

            txtFullName.Text = user.FullName;
            txtEmail.Text = user.Email;
            txtPhone.Text = user.Phone;
            txtAddress.Text = user.Address;

            lblMemberSince.Text = "Signed in as " + user.UserType + "   |   Member since " +
                                  user.CreatedAt.ToString("dd MMM yyyy");

            lblAddress.Text = user.UserType == "Customer"
                ? "Delivery address (pre-filled at checkout)"
                : "Your personal address";
        }

        // ---------------------------------------------------------------------
        //  PROFILE
        // ---------------------------------------------------------------------

        private void Profile_Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            ValidateProfile();
        }

        private bool ValidateProfile()
        {
            bool ok = true;

            ok &= Check(!Validator.IsBlank(txtFullName.Text), lblFullNameError, txtFullName,
                        "Your name cannot be empty.");

            ok &= Check(Validator.IsMobile(txtPhone.Text), lblPhoneError, txtPhone,
                        "A mobile number is 11 digits and starts with 01.");

            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "Please enter an address.");

            btnSaveProfile.Enabled = ok;
            btnSaveProfile.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private void btnSaveProfile_Click(object sender, EventArgs e)
        {
            if (!ValidateProfile()) return;

            try
            {
                // The UNIQUE constraint on Phone stops two accounts sharing a number,
                // so the check here is only to give a friendly message first.
                if (PhoneTakenBySomeoneElse(txtPhone.Text.Trim()))
                {
                    UiTheme.ShowError(lblPhoneError, txtPhone,
                        "That mobile number belongs to another account (UQ_Users_Phone).");
                    return;
                }

                if (_auth.UpdateProfile(UserSession.UserId, txtFullName.Text, txtPhone.Text, txtAddress.Text))
                {
                    UserSession.FullName = txtFullName.Text.Trim();
                    lblStatus.Text = "Your details have been saved.";
                    MessageBox.Show("Your details have been saved.", "PharmaLink",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Your details could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool PhoneTakenBySomeoneElse(string phone)
        {
            User current = _auth.GetUser(UserSession.UserId);
            if (current != null && current.Phone == phone) return false;
            return _auth.PhoneExists(phone);
        }

        // ---------------------------------------------------------------------
        //  PASSWORD
        // ---------------------------------------------------------------------

        private void Password_Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            ValidatePassword();
        }

        private bool ValidatePassword()
        {
            bool ok = true;

            if (Validator.IsBlank(txtCurrent.Text))
            {
                UiTheme.ClearError(lblCurrentError, txtCurrent);
                ok = false;
            }
            else
            {
                UiTheme.ClearError(lblCurrentError, txtCurrent);
            }

            if (Validator.IsBlank(txtNew.Text))
            {
                UiTheme.ClearError(lblNewError, txtNew);
                ok = false;
            }
            else
            {
                ok &= Check(Validator.IsStrongPassword(txtNew.Text), lblNewError, txtNew,
                            "The new password needs at least 6 characters and at least one digit.");
            }

            if (Validator.IsBlank(txtConfirm.Text))
            {
                UiTheme.ClearError(lblConfirmError, txtConfirm);
                ok = false;
            }
            else
            {
                ok &= Check(txtConfirm.Text == txtNew.Text, lblConfirmError, txtConfirm,
                            "The two new passwords do not match.");
            }

            if (!Validator.IsBlank(txtNew.Text) && txtNew.Text == txtCurrent.Text)
            {
                UiTheme.ShowError(lblNewError, txtNew, "The new password must be different from the current one.");
                ok = false;
            }

            btnChangePassword.Enabled = ok;
            btnChangePassword.BackColor = ok ? UiTheme.Accent : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private void btnChangePassword_Click(object sender, EventArgs e)
        {
            if (!ValidatePassword()) return;

            try
            {
                if (_auth.ChangePassword(UserSession.UserId, txtCurrent.Text, txtNew.Text))
                {
                    txtCurrent.Clear();
                    txtNew.Clear();
                    txtConfirm.Clear();
                    ValidatePassword();

                    lblStatus.Text = "Password updated. Only Users.PasswordHash and Users.PasswordSalt changed.";
                    MessageBox.Show(
                        "Your password has been updated.\r\n\r\n" +
                        "A fresh random salt was generated and the new password was hashed with SHA-256 " +
                        "before it reached the database.",
                        "Password changed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // The UPDATE carried "AND PasswordHash = @OldHash", so a wrong
                    // current password simply changes no rows.
                    UiTheme.ShowError(lblCurrentError, txtCurrent,
                        "That is not your current password, so nothing was changed.");
                    txtCurrent.SelectAll();
                    txtCurrent.Focus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("The password could not be changed.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void chkShowPasswords_CheckedChanged(object sender, EventArgs e)
        {
            char mask = chkShowPasswords.Checked ? '\0' : '*';
            txtCurrent.PasswordChar = mask;
            txtNew.PasswordChar = mask;
            txtConfirm.PasswordChar = mask;
        }

        // ---------------------------------------------------------------------

        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
