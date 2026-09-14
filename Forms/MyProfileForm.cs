using System.Drawing;               // Color and Font, used only by the theming pass
using System.Windows.Forms;         // Form, Label, Control, GroupBox and MessageBox
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // User, the typed object the account row becomes
using PharmaLinkApp.Services;       // AuthService, the only class here that reaches SQL

// Forms know no SQL, so this screen would survive a change of database.
namespace PharmaLinkApp.Forms
{
    /// <summary>Edit my own details and change my own password.</summary>
    public partial class MyProfileForm : Form
    {
        // One service for the form's life; DbHelper opens a connection inside each call.
        private readonly AuthService _auth = new AuthService();

        private bool _loading = true;   // true while LoadProfile is filling the boxes

        // Kept bare: a constructor that throws leaves no window to show the error in.
        public MyProfileForm()
        {
            InitializeComponent();      // build the controls from the Designer file first
        }

        // Load fires after the handle exists, so no unstyled form flashes on screen.
        private void MyProfileForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours and fonts only, no data
            LoadProfile();      // fills the personal details half from the stored row

            _loading = false;   // changes from here on are the user's, so revalidate

            ValidateProfile();    // usually passes: it judges values that came from the DB
            ValidatePassword();   // always fails first, so the button starts off
        }

        // Colours and fonts only. Nothing here reads or writes data.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Account");   // size, icon, background and caption

            panelHeader.BackColor = UiTheme.Primary;              // the green band every screen wears
            lblTitle.Font = UiTheme.FontTitle;                    // largest type, so the screen names itself
            lblTitle.ForeColor = Color.White;                     // the only pairing with enough contrast
            lblSubtitle.Font = UiTheme.FontSmall;                 // smaller, so the two read as a pair
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // pale green: legible but secondary

            // Written as a loop, so the two halves cannot drift apart when one is restyled.
            foreach (GroupBox group in new[] { grpProfile, grpPassword })
            {
                group.Font = UiTheme.FontHeading;        // the caption font; children override it below
                group.ForeColor = UiTheme.Primary;       // a green caption ties back to the header band
                group.BackColor = UiTheme.CardBack;      // an off-white card lifts off the background

                // Walked rather than named, so a Designer-added box is picked up for free.
                foreach (Control child in group.Controls)
                {
                    child.Font = UiTheme.FontBody;       // one body font, so nothing inherits heading size
                    child.ForeColor = UiTheme.TextDark;  // the default ink, overwritten for errors below
                    // Told apart by name, so the Designer stays the only place they are declared.
                    if (child is Label label && label.Name.EndsWith("Error"))
                    {
                        label.Font = UiTheme.FontSmall;      // smaller: it sits under a box and must not crowd it
                        label.ForeColor = UiTheme.Danger;    // red is the palette's only "this is wrong"
                    }
                }
            }

            txtEmail.BackColor = Color.FromArgb(240, 242, 244);   // grey, so read-only LOOKS read-only
            lblEmailNote.Font = UiTheme.FontSmall;                // the note on why email cannot be edited
            lblEmailNote.ForeColor = UiTheme.TextMuted;           // muted: an explanation, not an instruction
            lblHashNote.Font = UiTheme.FontSmall;                 // the note about hashing, by the passwords
            lblHashNote.ForeColor = UiTheme.TextMuted;            // same grey, so both notes read as one voice
            lblMemberSince.Font = UiTheme.FontSmall;              // role and join date, filled by LoadProfile
            lblMemberSince.ForeColor = UiTheme.TextMuted;         // context rather than content
            lblStatus.Font = UiTheme.FontSmall;                   // the quiet line recording the last action
            lblStatus.ForeColor = UiTheme.TextMuted;              // muted, so success never outshouts an error

            UiTheme.StyleSecondary(btnBack);              // grey: leaving is not the encouraged action
            UiTheme.StylePrimary(btnSaveProfile);         // green: the main action of the top half
            UiTheme.StyleAccent(btnChangePassword);       // a different accent, so the two are never confused
            btnSaveProfile.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);        // set after StylePrimary, which would overwrite it
            btnChangePassword.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);     // matched weight, so both read as equals
        }

        // Fills the top half from the database. Called once, from Load, and never again.
        private void LoadProfile()
        {
            // Read by session UserId: no account picker, so only the logged-in row loads.
            User user = _auth.GetUser(UserSession.UserId);

            // A null row means the account was deleted mid-session; leave the boxes empty.
            if (user == null) return;

            txtFullName.Text = user.FullName;   // the one name field, and the header uses it

            // Filled but read only: Email is the UNIQUE login id, so UpdateProfile omits it.
            txtEmail.Text = user.Email;

            txtPhone.Text = user.Phone;       // editable, checked against UQ_Users_Phone on save
            txtAddress.Text = user.Address;   // "" not null: GetUser already flattened DBNull

            // Role and join date together answer "is this the right account" at a glance.
            lblMemberSince.Text = "Signed in as " + user.UserType + "   |   Member since " +
                                  user.CreatedAt.ToString("dd MMM yyyy");   // spelled-out month, so 03/04 is unambiguous

            // Same column, different caption: only a customer gets a delivery meaning.
            lblAddress.Text = user.UserType == "Customer"
                ? "Delivery address (pre-filled at checkout)"   // says why keeping it current matters
                : "Your personal address";                      // no delivery meaning for an owner or admin
        }

        // ---- PROFILE ----

        // All three profile boxes share this handler, so there is one entry point.
        private void Profile_Changed(object sender, EventArgs e)
        {
            if (_loading) return;   // LoadProfile is filling the controls, not the user
            ValidateProfile();      // re-judge on every keystroke, so the button is never stale
        }

        // Judges the three editable boxes, painting a message under each failing field.
        private bool ValidateProfile()
        {
            bool ok = true;   // starts true and is only ever narrowed by the rules below

            // &= not &&: every rule runs, so every failing field is painted in one pass.
            ok &= Check(!Validator.IsBlank(txtFullName.Text), lblFullNameError, txtFullName,
                        "Your name cannot be empty.");   // IsBlank, so a box of spaces fails too

            // The shape rule lives in Validator, so sign up and this form cannot disagree.
            ok &= Check(Validator.IsMobile(txtPhone.Text), lblPhoneError, txtPhone,
                        "A mobile number is 11 digits and starts with 01.");   // the message states the rule

            // Required, because the checkout prefills the delivery address from this column.
            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "Please enter an address.");   // short: a presence rule has no shape to explain

            // Disabling the button is the real enforcement: an invalid form cannot be sent.
            btnSaveProfile.Enabled = ok;

            // Greyed too, or a disabled green button reads as broken rather than blocked.
            btnSaveProfile.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;   // handed back, so the click handler can repeat the test
        }

        // The Save button for the top half; its first line judges the three boxes again.
        private void btnSaveProfile_Click(object sender, EventArgs e)
        {
            // Revalidated, because a keyboard shortcut can raise a click the button did not.
            if (!ValidateProfile()) return;

            // Wrapped: the Users constraints can still refuse a row this form was happy with.
            try
            {
                // Trimmed, because the stored value was trimmed on the way in.
                if (PhoneTakenBySomeoneElse(txtPhone.Text.Trim()))
                {
                    UiTheme.ShowError(lblPhoneError, txtPhone,                                 // the message goes under the box, not in a dialog
                        "That mobile number belongs to another account (UQ_Users_Phone).");     // names the constraint, so the cause is clear
                    return;     // nothing is written and the typed values stay on screen
                }

                // Email is not passed, so this cannot change the login identifier by accident.
                if (_auth.UpdateProfile(UserSession.UserId, txtFullName.Text, txtPhone.Text, txtAddress.Text))
                {
                    // Without this the dashboard header shows the old name until next login.
                    UserSession.FullName = txtFullName.Text.Trim();

                    // Said twice: a quiet status line, plus a dialog that cannot be missed.
                    lblStatus.Text = "Your details have been saved.";
                    MessageBox.Show("Your details have been saved.", "PharmaLink",              // same wording as the status line
                        MessageBoxButtons.OK, MessageBoxIcon.Information);                      // Information: nothing went wrong
                }
                // A false return means the UPDATE matched no row, so nothing is claimed.
            }
            // Only genuine database failures land here; a duplicate phone was handled above.
            catch (Exception ex)
            {
                // Showing the message keeps the typed values on screen for a single fix.
                MessageBox.Show("Your details could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // Error icon: this really is a failure
            }
        }

        // "Is this number in use by somebody who is not me?" - the own-row problem.
        private bool PhoneTakenBySomeoneElse(string phone)
        {
            // Re-read the stored row: the text box no longer holds the old value.
            User current = _auth.GetUser(UserSession.UserId);

            // The user's OWN number is not a clash, or an address-only save would be refused.
            if (current != null && current.Phone == phone) return false;

            return _auth.PhoneExists(phone);   // only now does a match mean someone else holds it
        }

        // ---- PASSWORD ----

        // The three password boxes share this handler, so the rules apply from one place.
        private void Password_Changed(object sender, EventArgs e)
        {
            if (_loading) return;   // the same guard as the profile half: a fill is not an edit
            ValidatePassword();     // one way in, so the button always matches the boxes
        }

        // Judges the password boxes; "not filled in yet" and "wrong" are treated apart.
        private bool ValidatePassword()
        {
            bool ok = true;   // narrowed by each rule; the button needs all of them to pass

            // EMPTY IS NOT WRONG: clear the label, keep the button off, and do not nag.
            if (Validator.IsBlank(txtCurrent.Text))
            {
                UiTheme.ClearError(lblCurrentError, txtCurrent);   // an untouched box did nothing wrong
                ok = false;                                        // but it is incomplete, so no button
            }
            // Something is typed, which is as much as this form can judge.
            else
            {
                // Whether it is right is a question only the database can answer.
                UiTheme.ClearError(lblCurrentError, txtCurrent);
            }

            if (Validator.IsBlank(txtNew.Text))   // the same empty-is-not-wrong for the new one
            {
                UiTheme.ClearError(lblNewError, txtNew);   // no red label over an unreached box
                ok = false;                                // still incomplete, so still no button
            }
            // Something has been typed, so now the strength rule is worth applying.
            else
            {
                // The same rule sign up applies, so no account can hold a weaker password.
                ok &= Check(Validator.IsStrongPassword(txtNew.Text), lblNewError, txtNew,
                            "The new password needs at least 6 characters and at least one digit.");   // spells the rule out
            }

            if (Validator.IsBlank(txtConfirm.Text))   // and once more for the confirmation box
            {
                UiTheme.ClearError(lblConfirmError, txtConfirm);   // silent while it is still empty
                ok = false;                                        // but counted as incomplete
            }
            // Both new-password boxes now hold something, so they can be compared.
            else
            {
                // Once hashed, two different passwords are just two different hashes.
                ok &= Check(txtConfirm.Text == txtNew.Text, lblConfirmError, txtConfirm,
                            "The two new passwords do not match.");   // ordinal compare: case and spacing count
            }

            // Checked last, so it overwrites the strength message with the specific one.
            if (!Validator.IsBlank(txtNew.Text) && txtNew.Text == txtCurrent.Text)
            {
                UiTheme.ShowError(lblNewError, txtNew, "The new password must be different from the current one.");   // this one IS a mistake
                ok = false;   // and it blocks the write rather than only warning about it
            }

            // Off until all three agree, so the failure left over can mean one thing only.
            btnChangePassword.Enabled = ok;
            btnChangePassword.BackColor = ok ? UiTheme.Accent : Color.FromArgb(170, 190, 184);   // greyed when off
            return ok;   // returned, so the click handler can repeat the test
        }

        // The Change Password button: one service call, and much it deliberately skips.
        private void btnChangePassword_Click(object sender, EventArgs e)
        {
            if (!ValidatePassword()) return;   // revalidated: a shortcut can bypass the button

            // A wrong current password is not an exception, so this try is for DB failures.
            try
            {
                // One UPDATE verifies and writes: the old hash sits in its WHERE clause.
                if (_auth.ChangePassword(UserSession.UserId, txtCurrent.Text, txtNew.Text))
                {
                    // Cleared at once, so plain text does not sit behind an unattended screen.
                    txtCurrent.Clear();
                    txtNew.Clear();       // cleared too: a correct password is still a secret
                    txtConfirm.Clear();   // all three, or the one left is the one to be read

                    ValidatePassword();   // re-run on empty boxes, which turns the button off

                    // Names the two columns that moved, so the rest of the row is clearly safe.
                    lblStatus.Text = "Password updated. Only Users.PasswordHash and Users.PasswordSalt changed.";
                    MessageBox.Show(                                                                    // a dialog too, because this must not be missed
                        "Your password has been updated.\r\n\r\n" +                                     // the plain answer first
                        "A fresh random salt was generated and the new password was hashed with SHA-256 " +   // a new salt makes the old one useless
                        "before it reached the database.",                                              // the plain text never left this machine
                        "Password changed", MessageBoxButtons.OK, MessageBoxIcon.Information);          // Information, since nothing failed
                }
                // False, not an exception: the UPDATE simply matched no row.
                else
                {
                    // Zero rows matched, so the typed current password must be wrong.
                    UiTheme.ShowError(lblCurrentError, txtCurrent,
                        "That is not your current password, so nothing was changed.");   // says nothing changed
                    txtCurrent.SelectAll();   // select, so the retype replaces it
                    txtCurrent.Focus();       // cursor there too, so the retype needs no click
                }
            }
            // Reached only when the database itself failed, never on a wrong password.
            catch (Exception ex)
            {
                // A wrong password is the else branch above, which gets a field-level message.
                MessageBox.Show("The password could not be changed.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // ex.Message only, no stack trace
            }
        }

        // The show-passwords tick box changes what is drawn and nothing that is stored.
        private void chkShowPasswords_CheckedChanged(object sender, EventArgs e)
        {
            // '\0' means "no masking" in WinForms; one local, so all three boxes agree.
            char mask = chkShowPasswords.Checked ? '\0' : '*';

            // All three: revealing only some would defeat the point of the box.
            txtCurrent.PasswordChar = mask;
            txtNew.PasswordChar = mask;       // the same local, so the boxes cannot diverge
            txtConfirm.PasswordChar = mask;   // the confirmation too, or comparing by eye fails
        }

        // ---- SHARED ----

        // One helper for both halves, so "ok &= Check(...)" paints and counts in one line.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Clearing on success matters too, or a stale message sits under a fixed field.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);   // paints the label and tints the box
            return rulePassed;                                    // passed back, so the caller can use &=
        }

        // Close, not logout: the dashboard that opened this form keeps its session.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
