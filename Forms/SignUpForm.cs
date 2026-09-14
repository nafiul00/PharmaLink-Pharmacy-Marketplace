using System.Drawing;               // Font and Color, for ApplyTheme and the disabled button's grey
using System.Windows.Forms;         // Form, Label, TextBox, ComboBox, MessageBox, Cursors
using PharmaLinkApp.Helpers;        // UiTheme for styling and error labels, Validator for field rules
using PharmaLinkApp.Models;         // User and Pharmacy, the two objects this form fills in
using PharmaLinkApp.Services;       // AuthService writes the rows, PharmacyService suggests areas

// Forms is the presentation layer: a form gathers input and hands it to a service.
namespace PharmaLinkApp.Forms
{
    /// <summary>Registration for a customer or a pharmacy owner.</summary>
    public partial class SignUpForm : Form   // requirements 10 and 19; opened by LoginForm
    {
        private readonly AuthService _auth = new AuthService();                 // does all the writing
        private readonly PharmacyService _pharmacies = new PharmacyService();   // one read only, GetAreas

        /// <summary>Read by LoginForm to pre-fill the email box.</summary>
        public string RegisteredEmail { get; private set; } = "";   // private set: only this dialog fills it

        // Index 1 is the second item added in Load, so those two places must stay in step.
        private bool IsPharmacyOwner => cmbRegisterAs.SelectedIndex == 1;

        // Bare on purpose: no window handle yet, so database work waits for Load.
        public SignUpForm()
        {
            InitializeComponent();   // designer generated: builds the controls and nothing else
        }

        // Raised once, after the controls exist and the window is about to be shown.
        private void SignUpForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();      // colours and fonts first, so the window never flashes unstyled

            cmbRegisterAs.Items.Add("Customer  (patient buying medicine)");        // index 0, the default
            cmbRegisterAs.Items.Add("Pharmacy Owner  (shop selling medicine)");    // added SECOND: index 1 means owner
            cmbRegisterAs.SelectedIndex = 0;   // this assignment also raises the handler that shapes the form

            LoadAreaSuggestions();   // the one database read on this load path, and an optional one
            ValidateAll();     // on an empty form this only leaves the Create button disabled
        }

        // Colours and fonts live here, never in the file the designer rewrites.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Create account");   // page colour, body font and the title bar format

            panelHeader.BackColor = UiTheme.Primary;     // the same green strip every other screen opens with
            lblHeader.Font = UiTheme.FontTitle;          // the screen name, at the size reserved for it
            lblHeader.ForeColor = Color.White;           // white on the green, the only legible pairing
            lblHeaderSub.Font = UiTheme.FontSmall;       // the explanatory second line, deliberately smaller
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);   // a pale tint, so it recedes behind the title

            grpPersonal.Font = UiTheme.FontHeading;      // sets the GROUP CAPTION only; children are done below
            grpPersonal.ForeColor = UiTheme.Primary;     // a green caption shows this group is the active one
            grpPersonal.BackColor = UiTheme.CardBack;    // white, so the group reads as a card on the page

            grpPharmacy.Font = UiTheme.FontHeading;      // identical treatment, because the two groups are peers
            grpPharmacy.ForeColor = UiTheme.Primary;     // only the STARTING state; the dropdown handler dims it
            grpPharmacy.BackColor = UiTheme.CardBack;    // the same white card even while the group is disabled

            // One loop instead of naming thirty controls, so a field added later is covered.
            foreach (Control group in new Control[] { grpPersonal, grpPharmacy })
            {
                // Direct children only, which is all this form has: nothing here is nested.
                foreach (Control child in group.Controls)
                {
                    child.Font = UiTheme.FontBody;      // undoes the FontHeading inherited from the caption
                    child.ForeColor = UiTheme.TextDark; // the standard near black, before the error labels
                    // The "Error" name suffix is the same convention ValidateAll matches on.
                    if (child is Label label && label.Name.EndsWith("Error"))
                    {
                        label.Font = UiTheme.FontSmall; // smaller, so a full sentence fits without wrapping
                        label.ForeColor = UiTheme.Danger;   // red already, so ShowError need only supply words
                    }
                }
            }

            lblPendingNote.Font = UiTheme.FontSmall;    // the standing note about approval, sized as a caption
            lblPendingNote.ForeColor = UiTheme.Warning; // amber, not red: be aware, nothing has gone wrong

            lblFormMessage.Font = UiTheme.FontSmall;    // the form level message, styled once here
            lblFormMessage.ForeColor = UiTheme.Danger;  // red: only a failure from btnCreate_Click lands in it

            UiTheme.StylePrimary(btnCreate);            // the confirming action, so it takes the brand green
            // The one theme override on this form: a heavier face for the single decision.
            btnCreate.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnBack);            // quiet white, so leaving is not the main action
        }

        /// <summary>Fills the Area box with known areas plus common Dhaka ones.</summary>
        private void LoadAreaSuggestions()
        {
            cmbArea.Items.Clear();   // Add appends, so without this a second call would grow duplicates
            // The try wraps the database call only; the fallback list below sits outside it.
            try
            {
                // false means "not approved only", so a Pending shop's area still suggests.
                foreach (string area in _pharmacies.GetAreas(false))
                    cmbArea.Items.Add(area);   // real areas go in first, above the hard coded fallbacks
            }
            // No exception type and no variable: every failure of this read has one answer.
            catch
            {
                // The area list is only a convenience, and a fresh database has none yet.
            }

            // Added second, so a real area from the database keeps its place near the top.
            foreach (string area in new[] { "Mitford", "Dhanmondi", "Mirpur", "Uttara", "Banani", "Gulshan", "Mohammadpur" })
            {
                if (!cmbArea.Items.Contains(area)) cmbArea.Items.Add(area);   // Contains stops Mitford appearing twice
            }
        }

        // Raised on every change of the dropdown, including the assignment made in Load.
        private void cmbRegisterAs_SelectedIndexChanged(object sender, EventArgs e)
        {
            grpPharmacy.Enabled = IsPharmacyOwner;   // Enabled, not Visible, so the window never jumps size
            grpPharmacy.ForeColor = IsPharmacyOwner ? UiTheme.Primary : UiTheme.TextMuted;   // a disabled box will not dim its own caption
            lblAddress.Text = IsPharmacyOwner ? "Your personal address" : "Delivery address";   // one column, two meanings
            btnCreate.Text = IsPharmacyOwner ? "Submit for approval" : "Create account";   // the caption tells the truth about the click
            ValidateAll();   // the rule set itself just changed, so the button must go back to grey
        }

        // -- VALIDATION: a red label under the field, and the button stays disabled

        // One handler for every box, because the verdict is recomputed for the whole form.
        private void Field_Changed(object sender, EventArgs e)
        {
            lblFormMessage.Visible = false;   // the last save attempt's message no longer describes reality
            ValidateAll();                    // keeps the verdict current on every keystroke
        }

        // Returns the verdict as well as painting it: btnCreate_Click needs it too.
        private bool ValidateAll()
        {
            bool ok = true;    // optimistic: every rule below can only ever clear it, never set it

            // &= and not &&=, because && would short circuit and skip the later red labels.

            // -- full name: non-blank only, since a real name has no computable shape
            ok &= Check(!Validator.IsBlank(txtFullName.Text), lblFullNameError, txtFullName,
                        "Please enter your full name.");   // says what to do, not what is wrong

            // -- email: blank means unfinished, so it is cleared rather than shown as wrong
            if (Validator.IsBlank(txtEmail.Text))
            {
                UiTheme.ClearError(lblEmailError, txtEmail);   // an empty box is unfinished, not wrong
                ok = false;                                    // it still blocks the button, silently
            }
            // Something was typed, so the shape rule is finally worth applying.
            else
            {
                // Validator.IsEmail is the same rule as CK_Users_Email on the Users table.
                ok &= Check(Validator.IsEmail(txtEmail.Text), lblEmailError, txtEmail,
                            "Enter a valid email address, for example name@example.com.");   // the example shows the shape
            }

            // -- mobile number: the same blank-is-not-wrong split as the email above
            if (Validator.IsBlank(txtPhone.Text))
            {
                UiTheme.ClearError(lblPhoneError, txtPhone);   // unfinished, so no accusation is made
                ok = false;                                    // but the button stays grey
            }
            // Something was typed, so the format rule applies.
            else
            {
                // 11 digits starting 01. UQ_Users_Phone stops duplicates; no CHECK on shape.
                ok &= Check(Validator.IsMobile(txtPhone.Text), lblPhoneError, txtPhone,
                            "A mobile number is 11 digits and starts with 01.");   // both halves of the rule are stated
            }

            // -- address: NULLable in the table, but checkout needs one, so collect it here
            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "Please enter an address.");   // a non-empty address is the entire rule

            // -- password
            if (Validator.IsBlank(txtPassword.Text))
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);   // an untouched box is empty, not weak
                ok = false;                                          // but it still cannot be submitted
            }
            // A password was typed, so its strength can finally be judged.
            else
            {
                // The one rule the database cannot enforce: only the salted hash is stored.
                ok &= Check(Validator.IsStrongPassword(txtPassword.Text), lblPasswordError, txtPassword,
                            "Password needs at least 6 characters and at least one digit.");   // both conditions are named
            }

            // -- confirm password
            if (Validator.IsBlank(txtConfirm.Text))
            {
                UiTheme.ClearError(lblConfirmError, txtConfirm);   // "do not match" is useless against an empty box
                ok = false;                                        // unfinished, so still not submittable
            }
            // Both boxes hold something now, so comparing them means something.
            else
            {
                // Interface only: the second box is never sent anywhere. Ordinal is correct.
                ok &= Check(txtConfirm.Text == txtPassword.Text, lblConfirmError, txtConfirm,
                            "The two passwords do not match.");   // neither password is quoted back
            }

            // The fork: everything below applies to a pharmacy owner and to nobody else.
            if (IsPharmacyOwner)
            {
                // Pharmacies.PharmacyName is NOT NULL; this is what stops an empty one.
                ok &= Check(!Validator.IsBlank(txtShopName.Text), lblShopNameError, txtShopName,
                            "Enter the trading name of your pharmacy.");   // the name customers will search on
                // IsLicenseNo is loose on purpose: the Super Admin does the real DGDA check.
                ok &= Check(Validator.IsLicenseNo(txtLicenseNo.Text), lblLicenseError, txtLicenseNo,
                            "Enter your DGDA licence number, for example DGDA-DH-10021.");   // a worked example, since the rule is loose
                // cmbArea.Text, not SelectedItem: a typed area is as valid as a listed one.
                ok &= Check(!Validator.IsBlank(cmbArea.Text), lblAreaError, cmbArea,
                            "Choose or type the area your shop is in.");   // "or type": the list is not a closed set
                // The shop's postal address, separate from the owner's personal one above.
                ok &= Check(!Validator.IsBlank(txtShopAddress.Text), lblShopAddressError, txtShopAddress,
                            "Enter the full postal address of the shop.");   // "of the shop" separates the two
                // A shop line rather than the owner's mobile, so IsMobile is not applied.
                ok &= Check(!Validator.IsBlank(txtShopPhone.Text), lblShopPhoneError, txtShopPhone,
                            "Enter a contact number for the shop.");   // wording matches the looser rule applied
            }
            // The customer path has no rules of its own; it cleans up after the owner.
            else
            {
                // Switching back must undo the red labels and pink boxes the owner path left.
                foreach (Control child in grpPharmacy.Controls)
                {
                    // Driven by the collection, so a field added to the group later is covered.
                    if (child is Label label && label.Name.EndsWith("Error")) label.Visible = false;
                    // ClearError is not used here: it needs a label paired with each box.
                    if (child is TextBox || child is ComboBox) child.BackColor = Color.White;
                }
            }

            btnCreate.Enabled = ok;   // the button state IS the verdict, on this form as on every other
            btnCreate.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);   // a muted green, so it reads as the same control
            return ok;   // handed back for btnCreate_Click, which re-runs this before writing
        }

        /// <summary>Paints one field's error label and returns the verdict.</summary>
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Returning the argument unchanged lets a rule be one line at the call site.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);   // the wording stays with the rule
            return rulePassed;   // this method paints the result; it decides nothing itself
        }

        // -- SAVE: the only method on this form that writes anything
        private void btnCreate_Click(object sender, EventArgs e)
        {
            // A disabled button stops the mouse but not the keyboard, so re-validate here.
            if (!ValidateAll()) return;

            Cursor = Cursors.WaitCursor;     // up to four round trips to SQL Server follow
            // The try opens after the cursor, so the finally always puts the cursor back.
            try
            {
                // The UNIQUE constraint is the real rule; this only makes the failure readable.
                if (_auth.EmailExists(txtEmail.Text))
                {
                    // Under the email box, so the user is pointed at the one field to change.
                    UiTheme.ShowError(lblEmailError, txtEmail, "An account with this email already exists.");
                    btnCreate.Enabled = false;   // a lock Field_Changed releases on the next keystroke
                    return;     // nothing has been written yet, so there is nothing to undo
                }

                // Only reached if the email was free, so one problem is fixed at a time.
                if (_auth.PhoneExists(txtPhone.Text))
                {
                    // Checked apart from the email so the message lands under the right box.
                    UiTheme.ShowError(lblPhoneError, txtPhone, "This mobile number is already registered.");
                    btnCreate.Enabled = false;   // the same self releasing lock as the email path
                    return;                      // again nothing written, so nothing to undo
                }

                // Nothing is trimmed here: AuthService trims as it binds the parameters.
                User user = new User
                {
                    FullName = txtFullName.Text,   // straight from the box; the service trims it
                    Email = txtEmail.Text,         // the value both uniqueness checks ran against
                    Phone = txtPhone.Text,         // already known to be free and 11 digits long
                    Address = txtAddress.Text      // delivery address for a customer, personal for an owner
                };

                // The property that drove the rules now picks the path, so the two agree.
                if (IsPharmacyOwner)
                {
                    // Done before the Pharmacy object is built; UQ_Pharmacies_License is the rule.
                    if (_auth.LicenseExists(txtLicenseNo.Text))
                    {
                        // Worded as belonging to someone else, because the number is not invalid.
                        UiTheme.ShowError(lblLicenseError, txtLicenseNo,
                            "This licence number is already registered to another pharmacy.");   // a dead end until it is changed
                        btnCreate.Enabled = false;   // held grey until the user edits something
                        return;                      // nothing was built for a write that cannot happen
                    }

                    // Status, CommissionRate and RegisteredAt are left to the table DEFAULTs.
                    Pharmacy pharmacy = new Pharmacy
                    {
                        PharmacyName = txtShopName.Text,     // the trading name customers see and search
                        LicenseNo = txtLicenseNo.Text,       // just checked free, and UNIQUE in the table
                        Area = cmbArea.Text,                 // .Text, so a hand typed area survives
                        Address = txtShopAddress.Text,       // the shop's postal address, not the owner's
                        ContactPhone = txtShopPhone.Text     // a shop line, never held to the mobile format
                    };

                    // One transaction, two rows: never an owner with no shop, or a shop with no owner.
                    _auth.RegisterPharmacyOwner(user, pharmacy, txtPassword.Text);

                    // Read off the saved object, so the message matches what was actually stored.
                    MessageBox.Show(
                        "Your pharmacy registration has been submitted.\r\n\r\n" +   // a blank line before the explanation
                        "Both your account and " + pharmacy.PharmacyName + " are held at status Pending. " +   // BOTH: either one alone would confuse
                        "The Super Admin will check licence " + pharmacy.LicenseNo + " and approve the shop, " +   // names who acts next
                        "after which you will be able to log in and list your medicines.",   // and names what unblocks
                        "Submitted for approval", MessageBoxButtons.OK, MessageBoxIcon.Information);   // Information: queued, not broken

                    // RegisteredEmail stays empty here, because Login refuses a Pending account.
                }
                // The customer path: one row, one call, and the account is usable at once.
                else
                {
                    // RegisterCustomer writes Status 'Active', which is the whole difference.
                    _auth.RegisterCustomer(user, txtPassword.Text);
                    // Trimmed to match what the INSERT stored, so LoginForm's = comparison hits.
                    RegisteredEmail = user.Email.Trim();

                    // Shown AFTER the write, so it can only appear for an account that exists.
                    MessageBox.Show(
                        "Welcome to PharmaLink, " + user.FullName + ".\r\n\r\n" +   // read off the saved object, not the box
                        "Your account is active. You can sign in and start ordering right away.",   // mirrors the Pending message opposite
                        "Account created", MessageBoxButtons.OK, MessageBoxIcon.Information);   // the caption alone answers "did that work?"
                }

                // Assigning DialogResult on a modal dialog is itself what closes the window.
                DialogResult = DialogResult.OK;
                Close();   // written out as well, so the intent to shut the dialog is explicit
            }
            // Exception itself, because every failure at this level gets the same treatment.
            catch (Exception ex)
            {
                // An unreachable database or a lost UNIQUE race becomes a sentence here.
                UiTheme.ShowError(lblFormMessage, null, "The account could not be created: " + ex.Message);
                lblFormMessage.Visible = true;   // null as the field above, so nothing is tinted pink
                // No Close() here: a failure must leave what the user typed on screen.
            }
            // finally, because two of the three exits from this method are early returns.
            finally
            {
                Cursor = Cursors.Default;   // so a wait cursor is never left spinning over the form
            }
        }

        // The cancel path: it writes nothing, so it needs no try block and no wait cursor.
        private void btnBack_Click(object sender, EventArgs e)
        {
            // Cancel, not OK, is what tells LoginForm that nothing was registered.
            DialogResult = DialogResult.Cancel;
            Close();   // matches the success path, so the dialog always ends the same way
        }
    }
}
