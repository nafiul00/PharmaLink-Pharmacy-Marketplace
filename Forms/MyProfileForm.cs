using System.Drawing;               // Color and Font, used only by the theming pass
using System.Windows.Forms;         // Form, Label, Control, GroupBox and MessageBox
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // User, the typed object the account row becomes
using PharmaLinkApp.Services;       // AuthService, the only class here that reaches SQL

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
        // One service for the life of the form. It holds no connection of its own:
        // DbHelper opens and closes one inside every call.
        private readonly AuthService _auth = new AuthService();

        // True while LoadProfile is filling the boxes. Every field raises its changed
        // handler on assignment, so without this the fill would run a validation pass per
        // control and could paint red labels over values straight from the database.
        private bool _loading = true;

        public MyProfileForm()
        {
            InitializeComponent();      // build the controls from the Designer file first
            // Nothing else here. Reading the account waits for the Load event, because a
            // constructor that throws leaves no window in which to show the error.
        }

        private void MyProfileForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours and fonts only, no data
            LoadProfile();      // fills the personal details half from the stored row

            // The fill is over, so changes from here on are the user's and must revalidate.
            _loading = false;

            // Both halves are validated once, deliberately. The profile pass usually
            // succeeds, because it is judging values that came out of the database, and
            // leaves Save enabled. The password pass always fails at first, because all
            // three password boxes are empty, and that is what leaves the Change Password
            // button off until something is actually typed.
            ValidateProfile();
            ValidatePassword();
        }

        // Colours and fonts only. Nothing here reads or writes data.
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
            // Read by UserId from the session, not by anything on screen. There is no
            // account picker on this form and no id field to tamper with, so the only
            // account it can ever load or write is the one that logged in.
            //
            // Note what this query does NOT select: PasswordHash and PasswordSalt. The form
            // has no use for them, so they never travel to the client at all.
            User user = _auth.GetUser(UserSession.UserId);

            // A missing row would mean the account was deleted while the session was open.
            // Returning leaves the boxes empty rather than dereferencing a null.
            if (user == null) return;

            txtFullName.Text = user.FullName;

            // Email is filled in but the box is read only, set in the Designer and reinforced
            // by the grey background applied above. It is the login identifier and it is
            // UNIQUE, so letting it be edited here would mean letting someone change what
            // they log in with, and UpdateProfile deliberately does not include the column.
            txtEmail.Text = user.Email;

            txtPhone.Text = user.Phone;
            txtAddress.Text = user.Address;

            // Role and join date shown together, because they answer "is this the right
            // account" at a glance without needing another screen.
            lblMemberSince.Text = "Signed in as " + user.UserType + "   |   Member since " +
                                  user.CreatedAt.ToString("dd MMM yyyy");

            // The same field means two different things depending on who is looking at it.
            // For a customer the address is the one the checkout will prefill as the
            // delivery destination, which is worth saying so it is kept current; for an
            // owner or the Super Admin it is simply personal contact detail. One column,
            // one form, and only the caption changes.
            lblAddress.Text = user.UserType == "Customer"
                ? "Delivery address (pre-filled at checkout)"
                : "Your personal address";
        }

        // ---------------------------------------------------------------------
        //  PROFILE
        // ---------------------------------------------------------------------

        // The three editable profile boxes share this handler, so there is one entry
        // point for "something changed" rather than three that could drift apart.
        private void Profile_Changed(object sender, EventArgs e)
        {
            // LoadProfile is filling the controls, not the user, so there is nothing to
            // judge yet.
            if (_loading) return;
            ValidateProfile();
        }

        private bool ValidateProfile()
        {
            bool ok = true;

            // &= rather than &&: the right hand side is evaluated every time, so every rule
            // runs and every failing field gets its red label on the same pass. With &&
            // the first failure would short circuit the rest and the user would fix one
            // field only to discover the next.
            ok &= Check(!Validator.IsBlank(txtFullName.Text), lblFullNameError, txtFullName,
                        "Your name cannot be empty.");

            // The shape rule lives in Validator so the sign up form and this one cannot
            // disagree about what a mobile number is. Worth being honest about this one:
            // unlike the price and percentage rules elsewhere, Phone has a UNIQUE
            // constraint but no CHECK on its shape, so this rule is enforced here alone
            // rather than twice.
            ok &= Check(Validator.IsMobile(txtPhone.Text), lblPhoneError, txtPhone,
                        "A mobile number is 11 digits and starts with 01.");

            // Required because the customer's checkout prefills the delivery address from
            // this column, and an order with nowhere to deliver to is not an order.
            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "Please enter an address.");

            // Disabling the button is the real enforcement: an invalid form cannot be
            // submitted at all, rather than being submitted and then rejected.
            btnSaveProfile.Enabled = ok;

            // Greyed as well, because a disabled button still painted primary green reads
            // as a broken button rather than a blocked one.
            btnSaveProfile.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private void btnSaveProfile_Click(object sender, EventArgs e)
        {
            // Revalidated even though the button is only enabled when valid, because a
            // keyboard shortcut can raise a click the enabled state did not anticipate.
            if (!ValidateProfile()) return;

            try
            {
                // The UNIQUE constraint on Phone stops two accounts sharing a number,
                // so the check here is only to give a friendly message first.
                // Trimmed before the comparison, because the stored value was trimmed on
                // the way in and " 01712345678" would otherwise read as a different number.
                if (PhoneTakenBySomeoneElse(txtPhone.Text.Trim()))
                {
                    UiTheme.ShowError(lblPhoneError, txtPhone,
                        "That mobile number belongs to another account (UQ_Users_Phone).");
                    return;     // nothing is written, and the typed values stay on screen
                }

                // UserId from the session again. Email is not passed, so the statement
                // cannot change the login identifier even by accident: the column is simply
                // not in the SET list.
                if (_auth.UpdateProfile(UserSession.UserId, txtFullName.Text, txtPhone.Text, txtAddress.Text))
                {
                    // The session carries a cached copy of the name for the dashboard
                    // header. Without this line the header would keep showing the old name
                    // until the next login, and the screen would look as if the save failed.
                    // Trimmed to match what the service wrote to the column.
                    UserSession.FullName = txtFullName.Text.Trim();

                    // Said twice on purpose: the status line is the quiet record that stays
                    // on screen, the message box is the acknowledgement that cannot be missed.
                    lblStatus.Text = "Your details have been saved.";
                    MessageBox.Show("Your details have been saved.", "PharmaLink",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                // A false return means the UPDATE matched no row, which for a session that
                // is logged in should not happen. Nothing is claimed in that case, which is
                // better than announcing a save that did not occur.
            }
            catch (Exception ex)
            {
                // The constraints on Users are the real guarantee and can still refuse a row
                // this form thought was fine. Showing the message keeps the typed values on
                // screen so one field can be corrected rather than all three retyped.
                MessageBox.Show("Your details could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool PhoneTakenBySomeoneElse(string phone)
        {
            // Re-read rather than compared against the text box, because the box is what is
            // being changed and cannot say what the stored value was.
            User current = _auth.GetUser(UserSession.UserId);

            // The user's OWN number is not a clash. Without this test, saving an address
            // change while leaving the phone alone would be refused as a duplicate of
            // itself, which is the same "ignore my own row" problem the category and
            // medicine name checks solve by passing an id to ignore.
            if (current != null && current.Phone == phone) return false;

            // Only now does a match mean someone else holds it.
            return _auth.PhoneExists(phone);
        }

        // ---------------------------------------------------------------------
        //  PASSWORD
        // ---------------------------------------------------------------------

        // The three password boxes share this handler, so the rules below are applied to
        // all of them from one place.
        private void Password_Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            ValidatePassword();
        }

        private bool ValidatePassword()
        {
            bool ok = true;

            // EMPTY IS NOT WRONG, it is merely incomplete. Each of the three blank branches
            // below CLEARS the error and sets ok to false, so the button stays off but no
            // red label nags at a box the user has not reached yet. That is the deliberate
            // difference from the profile half, where a blank name really is a mistake.
            if (Validator.IsBlank(txtCurrent.Text))
            {
                UiTheme.ClearError(lblCurrentError, txtCurrent);
                ok = false;
            }
            else
            {
                // The current password is never judged here for strength or shape. Whether
                // it is right is a question only the database can answer, and it is asked
                // once, at the moment of the write.
                UiTheme.ClearError(lblCurrentError, txtCurrent);
            }

            if (Validator.IsBlank(txtNew.Text))
            {
                UiTheme.ClearError(lblNewError, txtNew);
                ok = false;
            }
            else
            {
                // Only once something has been typed is it worth saying it is too weak. The
                // same rule the sign up form applies, from the same method, so an account
                // cannot end up with a password this screen would have refused.
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
                // The confirmation box exists because the new password is masked and a typing
                // mistake would otherwise lock the account out of itself. Comparing the two
                // in the form is the only place this can be caught: once hashed, two
                // different passwords are simply two different hashes with nothing to
                // compare them against.
                ok &= Check(txtConfirm.Text == txtNew.Text, lblConfirmError, txtConfirm,
                            "The two new passwords do not match.");
            }

            // Checked last so it can overwrite the strength message with the more specific
            // one. Without it a user could "change" a password to itself, see a success
            // message, and reasonably believe something happened. The UPDATE would even
            // report one row changed, because the new salt makes the stored hash different.
            if (!Validator.IsBlank(txtNew.Text) && txtNew.Text == txtCurrent.Text)
            {
                UiTheme.ShowError(lblNewError, txtNew, "The new password must be different from the current one.");
                ok = false;
            }

            // The button is off until all three boxes are filled and consistent, which is
            // what makes the failure the user eventually sees mean one thing only: the
            // current password was wrong.
            btnChangePassword.Enabled = ok;
            btnChangePassword.BackColor = ok ? UiTheme.Accent : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private void btnChangePassword_Click(object sender, EventArgs e)
        {
            // Revalidated even though the button is only enabled when valid.
            if (!ValidatePassword()) return;

            try
            {
                // THE WHOLE PASSWORD CHANGE IS THIS ONE CALL, and what it does not do
                // matters as much as what it does.
                //
                // The plain text of both passwords goes no further than this method. Inside
                // ChangePassword the typed current password is hashed with the salt already
                // stored for this user, and it is that HASH that travels to SQL Server as a
                // parameter, in the WHERE clause of the UPDATE. So the verification and the
                // write are a single statement: a wrong current password matches no row,
                // changes nothing and comes back as false. There is no separate "check the
                // password" query first, which means there is no window between checking and
                // writing in which the row could change underneath.
                //
                // The new password is hashed with a BRAND NEW salt before it is sent, so
                // neither password is ever stored, transmitted or written to a log in plain
                // text, and nothing readable is left behind if the table is ever dumped.
                if (_auth.ChangePassword(UserSession.UserId, txtCurrent.Text, txtNew.Text))
                {
                    // Cleared immediately on success, so the plain text does not sit in three
                    // controls behind an unattended screen. This is also why nothing is kept
                    // in a field: the only copy was in the boxes, and now there is none.
                    txtCurrent.Clear();
                    txtNew.Clear();
                    txtConfirm.Clear();

                    // Re-run against the now empty boxes, which turns the button back off.
                    // Without it the button would stay enabled over three empty fields.
                    ValidatePassword();

                    // Names exactly which two columns moved, so it is clear the rest of the
                    // account was untouched by a password change.
                    lblStatus.Text = "Password updated. Only Users.PasswordHash and Users.PasswordSalt changed.";
                    MessageBox.Show(
                        "Your password has been updated.\r\n\r\n" +
                        "A fresh random salt was generated and the new password was hashed with SHA-256 " +
                        "before it reached the database.",
                        "Password changed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // ChangePassword returned false, which means the UPDATE matched zero
                    // rows. The UPDATE carried "AND PasswordHash = @OldHash", so the only
                    // way to match nothing is for the typed current password to be wrong.
                    //
                    // Worth noting what did NOT happen: no exception was thrown and no
                    // separate "check the password" query ran. The verification and the
                    // write were the same statement, so there was never a moment between
                    // them where the row could change.
                    //
                    // The new password boxes are deliberately left alone: only the box that
                    // was wrong is challenged, so a correct new password does not have to be
                    // typed twice again.
                    UiTheme.ShowError(lblCurrentError, txtCurrent,
                        "That is not your current password, so nothing was changed.");
                    txtCurrent.SelectAll();   // select so the retype replaces it
                    txtCurrent.Focus();       // and put the cursor there, so the retype needs no click
                }
            }
            catch (Exception ex)
            {
                // Only genuine failures reach here. A wrong password is not an exception, it
                // is the false branch above, which is why that case gets a field level
                // message and this one gets a dialog.
                MessageBox.Show("The password could not be changed.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void chkShowPasswords_CheckedChanged(object sender, EventArgs e)
        {
            // '\0' is how WinForms is told "no masking at all"; any other character becomes
            // the mask. Computed once into a local so all three boxes are guaranteed to
            // agree, rather than three separate conditionals that could be edited apart.
            char mask = chkShowPasswords.Checked ? '\0' : '*';

            // All three, not just the new one. Revealing only some of them would leave the
            // user comparing a visible string against a masked one, which defeats the point
            // of the checkbox. The text is only ever unmasked on screen, at the user's own
            // request, and this changes nothing about what is sent or stored.
            txtCurrent.PasswordChar = mask;
            txtNew.PasswordChar = mask;
            txtConfirm.PasswordChar = mask;
        }

        // ---------------------------------------------------------------------

        // One helper shared by both halves of the form, so showing and clearing an error
        // is written once. It returns the verdict it was given, which is what lets callers
        // write "ok &= Check(...)" and get the painting and the accumulation in one line.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Clearing on success matters as much as showing on failure, or a message from
            // an earlier keystroke would sit under a field that is now correct.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // Close, not logout: this form was opened from a dashboard and closing it returns
        // there with the session intact. Any password text still in the boxes goes with the
        // form when it is disposed.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
