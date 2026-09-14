using System.Drawing;               // Font and Color, for ApplyTheme and the disabled button's grey
using System.Windows.Forms;         // Form, Label, TextBox, ComboBox, MessageBox, Cursors
using PharmaLinkApp.Helpers;        // UiTheme (styling and the error helpers) and Validator (every field rule)
using PharmaLinkApp.Models;         // User and Pharmacy, the two objects this form fills in
using PharmaLinkApp.Services;       // AuthService (writes the rows) and PharmacyService (area suggestions)

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation, modal dialog opened by LoginForm. Uses AuthService
    //  and PharmacyService (only to suggest existing areas).
    //
    //  Load order:
    //      SignUpForm_Load -> ApplyTheme -> fill the "Register as" ComboBox
    //                      -> LoadAreaSuggestions -> ValidateAll
    //
    //  The "Register as" ComboBox decides which path runs: index 1 is a pharmacy
    //  owner, which enables grpPharmacy and changes the button text. Every field
    //  is validated as it is typed through Field_Changed -> ValidateAll, and the
    //  Create button stays disabled until every rule passes.
    //
    //  btnCreate_Click checks EmailExists, PhoneExists and, for an owner,
    //  LicenseExists before writing, so the user gets a message under the right
    //  field instead of a UNIQUE violation. Those database constraints are what
    //  actually enforce uniqueness; these checks only make the failure readable.
    //  It then calls RegisterCustomer, or RegisterPharmacyOwner which writes the
    //  Users row and the Pharmacies row inside one transaction.
    //
    //  RegisteredEmail is read back by LoginForm to pre-fill the email box.
    // -------------------------------------------------------------------------

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
        // Two services, both stateless, both created once for the life of the dialog.
        // AuthService does the writing; PharmacyService is used for one read only, and a
        // failure in that read is not allowed to stop registration - see LoadAreaSuggestions.
        private readonly AuthService _auth = new AuthService();
        private readonly PharmacyService _pharmacies = new PharmacyService();

        /// <summary>Read by LoginForm so the new user's email is pre-filled after registration.</summary>
        // private set: the dialog is the only thing allowed to fill this in, but the caller
        // may read it after the window has closed. That is how a modal dialog returns a value
        // without the two forms having to know anything else about each other.
        public string RegisteredEmail { get; private set; } = "";

        // The whole two-path design turns on this one expression. Writing it once, as a
        // property, means the enable/disable code, the validation and the save path can never
        // disagree about which kind of account is being created. Index 1 is hard wired to the
        // second item added in SignUpForm_Load, so those two places must stay in step.
        private bool IsPharmacyOwner => cmbRegisterAs.SelectedIndex == 1;

        public SignUpForm()
        {
            // Designer generated: builds the controls. Everything that reads the database or
            // assumes a visible window is deferred to the Load handler below.
            InitializeComponent();
        }

        private void SignUpForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            // The two items are added in code rather than in the designer so that the order -
            // and therefore the meaning of index 1 in IsPharmacyOwner - is visible in the same
            // file as the code that depends on it.
            cmbRegisterAs.Items.Add("Customer  (patient buying medicine)");
            cmbRegisterAs.Items.Add("Pharmacy Owner  (shop selling medicine)");
            // Defaulting to Customer, the commoner case, means the pharmacy group starts
            // disabled and the form opens in its simplest state. Assigning SelectedIndex also
            // raises SelectedIndexChanged, which is what actually greys out grpPharmacy and
            // sets the button caption, so no duplicate of that code is needed here.
            cmbRegisterAs.SelectedIndex = 0;

            LoadAreaSuggestions();
            ValidateAll();     // on an empty form this exists only to leave the Create button disabled
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

        /// <summary>
        /// Fills the Area dropdown with the areas existing pharmacies are already in, then
        /// tops it up with the common Dhaka areas. The box stays editable, so this is a list
        /// of suggestions rather than a constraint - Pharmacies.Area is plain NVARCHAR and
        /// accepts anything, which is why nothing here is treated as a rule.
        /// </summary>
        private void LoadAreaSuggestions()
        {
            // Clear first: this method could be called twice, and Items.Add appends rather
            // than replaces, so without this the list would grow duplicates.
            cmbArea.Items.Clear();
            try
            {
                // false means "not approved only", so a Pending shop's area still shows up as a
                // suggestion. A new owner registering in the same area as another pending one
                // should not have to type it out because nobody has been approved there yet.
                foreach (string area in _pharmacies.GetAreas(false))
                    cmbArea.Items.Add(area);
            }
            catch
            {
                // The area list is only a convenience; a fresh database has none yet.
                //
                // Swallowing the exception is justified precisely because nothing downstream
                // depends on it: the fallback list below still runs, the box is editable, and
                // registration works with an empty dropdown. This is the one shape of empty
                // catch that is defensible - an optional read whose failure has a working
                // fallback. The same silence around the INSERT below would hide a lost account.
            }

            // The fallback list, added second so a real area from the database keeps its place
            // near the top. Contains() stops an area appearing twice when the database already
            // holds a pharmacy in, say, Mitford.
            foreach (string area in new[] { "Mitford", "Dhanmondi", "Mirpur", "Uttara", "Banani", "Gulshan", "Mohammadpur" })
            {
                if (!cmbArea.Items.Contains(area)) cmbArea.Items.Add(area);
            }
        }

        private void cmbRegisterAs_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Switching the dropdown reshapes the whole form. Enabled, not Visible: the shop
            // fields stay on screen, greyed out, so the user can see what registering as a
            // pharmacy would ask for before choosing it. Hiding them would make the window
            // jump in size and hide the difference between the two paths.
            grpPharmacy.Enabled = IsPharmacyOwner;
            grpPharmacy.ForeColor = IsPharmacyOwner ? UiTheme.Primary : UiTheme.TextMuted;
            // The group's own caption is dimmed too, because a disabled GroupBox does not grey
            // its title text by itself and a green heading over dead fields reads as a bug.
            lblAddress.Text = IsPharmacyOwner ? "Your personal address" : "Delivery address";
            // Same database column, two meanings. For a customer Users.Address is where orders
            // are delivered; for an owner it is a personal address and the shop's address is a
            // separate field. Relabelling is cheaper and clearer than a second column.
            btnCreate.Text = IsPharmacyOwner ? "Submit for approval" : "Create account";
            // The caption tells the truth about what the click will do: a customer account is
            // created Active and usable, a pharmacy is created Pending and is not.
            ValidateAll();
            // Re-validate immediately, because the rule set itself has just changed. Switching
            // to Pharmacy Owner adds five required fields that are all empty, so the button
            // must go back to grey without the user touching anything.
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        //  Each failure shows a red label directly under the offending field and
        //  keeps the Create button disabled. Every rule here is enforced again by
        //  a CHECK or UNIQUE constraint in the database.
        // ---------------------------------------------------------------------

        private void Field_Changed(object sender, EventArgs e)
        {
            // Wired to the TextChanged event of every box on the form, so one method keeps the
            // whole form's verdict up to date on every keystroke. The user never presses a
            // Validate button and never meets a wall of errors at the end.
            lblFormMessage.Visible = false;
            // Hide the form level message, which reports the outcome of the LAST save attempt.
            // Once a character has been typed that sentence describes a state that no longer
            // exists, and leaving it up would contradict what the field labels now say.
            ValidateAll();
        }

        private bool ValidateAll()
        {
            bool ok = true;

            // Note &= and not &&=. C# has no &&= operator, and rewriting these as
            // ok = ok && Check(...) would SHORT CIRCUIT: once one field failed, every Check
            // after it would be skipped and those fields would never get their red label. With
            // &= every Check runs on every keystroke, so the form shows all of its problems at
            // once rather than one per correction.

            // -- full name ----------------------------------------------------
            // Non-blank only: names have no computable shape, and any length or character rule
            // here would refuse somebody's real name. Users.FullName is NVARCHAR(100) NOT NULL,
            // which stops a MISSING value but would happily store an empty string, so this
            // check is what actually keeps a nameless account out.
            ok &= Check(!Validator.IsBlank(txtFullName.Text), lblFullNameError, txtFullName,
                        "Please enter your full name.");

            // -- email --------------------------------------------------------
            // Blank is handled separately from wrong, here and for every field below. An empty
            // box means "not finished yet", so the message is cleared and the button stays
            // disabled; a filled box that breaks the rule earns red text. Without this split,
            // the form would light up in red before a single character had been typed.
            if (Validator.IsBlank(txtEmail.Text))
            {
                UiTheme.ClearError(lblEmailError, txtEmail);
                ok = false;
            }
            else
            {
                // Validator.IsEmail is the same rule as CK_Users_Email (Email LIKE '%_@_%._%')
                // on the Users table, so this message appears in exactly the cases the database
                // would have refused the row. Uniqueness is a separate matter and is checked
                // against the database in btnCreate_Click, because no regular expression can
                // know what is already stored.
                ok &= Check(Validator.IsEmail(txtEmail.Text), lblEmailError, txtEmail,
                            "Enter a valid email address, for example name@example.com.");
            }

            // -- mobile number ------------------------------------------------
            if (Validator.IsBlank(txtPhone.Text))
            {
                UiTheme.ClearError(lblPhoneError, txtPhone);
                ok = false;
            }
            else
            {
                // Eleven digits starting 01 is the Bangladeshi mobile format. This is the one
                // rule on this form that is enforced only ONCE: Users.Phone carries
                // UQ_Users_Phone, so a duplicate is refused by the database, but there is no
                // CHECK on its shape. A CK_Users_Phone would close that gap; until then this
                // method is the only thing standing between a landline number and the column.
                ok &= Check(Validator.IsMobile(txtPhone.Text), lblPhoneError, txtPhone,
                            "A mobile number is 11 digits and starts with 01.");
            }

            // -- address ------------------------------------------------------
            // Users.Address is NULLable, so the database has no opinion here at all. The rule
            // exists because a delivery address is needed later at checkout, and collecting it
            // once at sign up is friendlier than blocking a customer mid-order. Checkout
            // re-validates it anyway, because the box there is editable.
            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "Please enter an address.");

            // -- password -----------------------------------------------------
            if (Validator.IsBlank(txtPassword.Text))
            {
                UiTheme.ClearError(lblPasswordError, txtPassword);
                ok = false;
            }
            else
            {
                // Six characters and at least one digit. This rule CANNOT be enforced in the
                // database: only the salted hash is ever stored, and a hash of a weak password
                // is indistinguishable from a hash of a strong one. It is therefore a genuine
                // exception to the "validated twice" pattern, and the reason the rule lives in
                // Validator rather than inline - one definition, shared with the password
                // change screen, so the two cannot drift apart.
                ok &= Check(Validator.IsStrongPassword(txtPassword.Text), lblPasswordError, txtPassword,
                            "Password needs at least 6 characters and at least one digit.");
            }

            // -- confirm password ---------------------------------------------
            if (Validator.IsBlank(txtConfirm.Text))
            {
                UiTheme.ClearError(lblConfirmError, txtConfirm);
                ok = false;
            }
            else
            {
                // A pure user interface rule: the second box is never sent anywhere, and the
                // database could not check it if it wanted to. It exists because a typed
                // password is masked, so a typo would otherwise lock the new owner out of the
                // account they just created. Ordinal string equality is correct here - two
                // passwords that differ only in case or accent ARE different passwords.
                ok &= Check(txtConfirm.Text == txtPassword.Text, lblConfirmError, txtConfirm,
                            "The two passwords do not match.");
            }

            if (IsPharmacyOwner)
            {
                // These five rules apply ONLY on the owner path. Running them for a customer
                // would leave five red labels under a disabled group of boxes the customer is
                // not being asked to fill in, and the Create button could never go green.

                // Pharmacies.PharmacyName is NOT NULL; as with the personal name, NOT NULL
                // stops a missing value and this stops an empty one.
                ok &= Check(!Validator.IsBlank(txtShopName.Text), lblShopNameError, txtShopName,
                            "Enter the trading name of your pharmacy.");
                // The licence is the whole point of the approval step: the Super Admin checks
                // this number against the DGDA register before the shop may trade.
                // Validator.IsLicenseNo only requires six characters, deliberately loosely,
                // because the real check is a human one. UQ_Pharmacies_License then guarantees
                // no two shops can claim the same licence - that half IS enforced twice, here
                // for the message and in the database for the rule.
                ok &= Check(Validator.IsLicenseNo(txtLicenseNo.Text), lblLicenseError, txtLicenseNo,
                            "Enter your DGDA licence number, for example DGDA-DH-10021.");
                // cmbArea.Text, not SelectedItem: the box is editable, so an area typed in by
                // hand is as valid as one picked from the list. Reading SelectedItem would
                // reject every area that is not already in the database.
                ok &= Check(!Validator.IsBlank(cmbArea.Text), lblAreaError, cmbArea,
                            "Choose or type the area your shop is in.");
                // The shop's postal address is NOT NULL in Pharmacies and is what a customer
                // sees on the pharmacy profile, so it is separate from the owner's personal
                // address collected above.
                ok &= Check(!Validator.IsBlank(txtShopAddress.Text), lblShopAddressError, txtShopAddress,
                            "Enter the full postal address of the shop.");
                // A shop contact number, not the owner's mobile, so IsMobile is not applied:
                // pharmacies often publish a landline.
                ok &= Check(!Validator.IsBlank(txtShopPhone.Text), lblShopPhoneError, txtShopPhone,
                            "Enter a contact number for the shop.");
            }
            else
            {
                // Switching back to Customer has to UNDO whatever the owner path painted.
                // Without this the red labels and pink boxes from a half filled pharmacy
                // section would stay on screen under a disabled group, describing rules that
                // are no longer being applied.
                foreach (Control child in grpPharmacy.Controls)
                {
                    // The loop is driven by the control collection rather than naming each
                    // label, so a field added to the group later is cleaned up automatically.
                    // The "Error" name suffix is the convention that identifies a message label
                    // - the same convention ApplyTheme uses to colour them.
                    if (child is Label label && label.Name.EndsWith("Error")) label.Visible = false;
                    // Reset the pink tint ShowError left behind. ClearError is not used here
                    // because it needs a matching label, and this loop deliberately does not
                    // try to pair each box with its own message.
                    if (child is TextBox || child is ComboBox) child.BackColor = Color.White;
                }
            }

            // Same two lines as every other form in the project: the button state IS the
            // verdict, and the colour is set explicitly because Enabled alone leaves the fill
            // looking ready to press.
            btnCreate.Enabled = ok;
            btnCreate.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        /// <summary>Shows or clears one field's error label and returns the rule's result.</summary>
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Returning the same bool that was passed in is what lets a rule be written as one
            // line at the call site: evaluate, paint the result, feed the verdict into ok.
            // Fifteen of these would otherwise be fifteen four-line if/else blocks, and the
            // rule itself would be buried in the middle of each one.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------
        //  SAVE
        // ---------------------------------------------------------------------

        private void btnCreate_Click(object sender, EventArgs e)
        {
            // Re-validate on the way in. The button is only enabled when every rule passes, but
            // a disabled button stops the mouse and not the keyboard, and this also re-runs the
            // owner rules if the dropdown was switched after the last keystroke.
            if (!ValidateAll()) return;

            Cursor = Cursors.WaitCursor;     // up to four round trips to SQL Server follow
            try
            {
                // The UNIQUE constraints on Email and Phone are what actually stop
                // a duplicate account; checking first is only so the user gets a
                // friendly message instead of a database exception.
                //
                // There is a gap between this SELECT and the INSERT below in which another
                // registration could take the same email. That is exactly why the constraint,
                // not this check, is the rule: if the race is lost the INSERT fails, the catch
                // at the bottom reports it, and no duplicate row exists either way.
                if (_auth.EmailExists(txtEmail.Text))
                {
                    UiTheme.ShowError(lblEmailError, txtEmail, "An account with this email already exists.");
                    // Disable the button by hand. ValidateAll cannot know this, because the
                    // email is perfectly well formed - it is only unavailable. Field_Changed
                    // re-enables it the moment the user edits any box, so this is a lock that
                    // releases itself as soon as the problem is being addressed.
                    btnCreate.Enabled = false;
                    return;     // nothing has been written yet, so there is nothing to undo
                }

                if (_auth.PhoneExists(txtPhone.Text))
                {
                    // Checked separately from the email so the message lands under the right
                    // box. A single "those details are taken" sentence would leave the user
                    // guessing which of the two to change.
                    UiTheme.ShowError(lblPhoneError, txtPhone, "This mobile number is already registered.");
                    btnCreate.Enabled = false;
                    return;
                }

                // Build the model object from the boxes. Nothing is trimmed here: AuthService
                // trims every value as it binds the parameters, so the cleaning rule lives with
                // the INSERT it protects rather than being repeated on each form that writes a
                // user. Both paths below share this object, because both write a Users row.
                User user = new User
                {
                    FullName = txtFullName.Text,
                    Email = txtEmail.Text,
                    Phone = txtPhone.Text,
                    Address = txtAddress.Text
                };

                if (IsPharmacyOwner)
                {
                    // Third uniqueness check, owner path only. Done before the Pharmacy object
                    // is built so nothing is constructed for a registration that cannot be
                    // written, and once again the real guarantee is UQ_Pharmacies_License.
                    if (_auth.LicenseExists(txtLicenseNo.Text))
                    {
                        UiTheme.ShowError(lblLicenseError, txtLicenseNo,
                            "This licence number is already registered to another pharmacy.");
                        btnCreate.Enabled = false;
                        return;
                    }

                    // The shop. Notice what is NOT set here: Status, CommissionRate and
                    // RegisteredAt. All three have DEFAULT constraints in the table -
                    // 'Pending', 8.00 and the current time - so the database decides them and
                    // no form can register a shop that is approved from the start or set its
                    // own commission rate.
                    Pharmacy pharmacy = new Pharmacy
                    {
                        PharmacyName = txtShopName.Text,
                        LicenseNo = txtLicenseNo.Text,
                        Area = cmbArea.Text,
                        Address = txtShopAddress.Text,
                        ContactPhone = txtShopPhone.Text
                    };

                    // One call, one transaction, two rows. The Users row and the Pharmacies row
                    // are written together inside RegisterPharmacyOwner, so the application can
                    // never end up with an owner who has no shop or a shop with no owner. The
                    // plain password is passed rather than a hash because hashing needs a fresh
                    // salt, and creating the salt is the service's job, not the form's.
                    _auth.RegisterPharmacyOwner(user, pharmacy, txtPassword.Text);

                    // The message explains the Pending state in the user's own terms, and names
                    // the licence number back to them so they can tell at a glance whether they
                    // typed it correctly. Reading these values off the pharmacy object rather
                    // than the textboxes keeps the message tied to what was actually saved.
                    MessageBox.Show(
                        "Your pharmacy registration has been submitted.\r\n\r\n" +
                        "Both your account and " + pharmacy.PharmacyName + " are held at status Pending. " +
                        "The Super Admin will check licence " + pharmacy.LicenseNo + " and approve the shop, " +
                        "after which you will be able to log in and list your medicines.",
                        "Submitted for approval", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // RegisteredEmail is deliberately left empty on this path. A Pending account
                    // is refused by AuthService.Login, so pre-filling the login form with it
                    // would only invite an attempt that is certain to be turned away.
                }
                else
                {
                    // A customer is written with Status 'Active' inside RegisterCustomer and can
                    // sign in immediately, which is the whole difference between the two paths.
                    _auth.RegisterCustomer(user, txtPassword.Text);
                    // Setting this property is what makes LoginForm pre-fill the email box when
                    // the dialog closes. Trimmed to match what the INSERT actually stored, so
                    // the pre-filled text is guaranteed to match the row by the = comparison the
                    // login query uses.
                    RegisteredEmail = user.Email.Trim();

                    MessageBox.Show(
                        "Welcome to PharmaLink, " + user.FullName + ".\r\n\r\n" +
                        "Your account is active. You can sign in and start ordering right away.",
                        "Account created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                // Reached only when a registration was actually written. Assigning DialogResult
                // on a form shown with ShowDialog is itself what closes the window and what the
                // caller reads to tell success from cancel; Close() is kept for clarity and is
                // harmless because a form only closes once.
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                // The outermost handler for this user action. It catches both a database that
                // cannot be reached and a UNIQUE violation from a lost race, and turns either
                // into a sentence on the form instead of an unhandled exception that would take
                // the application down with the registration only half explained.
                UiTheme.ShowError(lblFormMessage, null, "The account could not be created: " + ex.Message);
                // null as the field: the fault belongs to the form as a whole, not to any one
                // box, so nothing is tinted pink.
                lblFormMessage.Visible = true;
                // ShowError has already made it visible; the repeat is harmless and makes the
                // intent explicit at the point where the dialog stays open on a failure.
                // Note there is no Close() here - failing must leave everything the user typed
                // on screen so they can correct it rather than retype it.
            }
            finally
            {
                // Runs on all three paths - success, an early return from a duplicate, and an
                // exception - so the wait cursor can never be left spinning over a form that
                // is waiting for the user.
                Cursor = Cursors.Default;
            }
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            // Cancel, not OK, is what tells LoginForm that nothing was registered. Nothing has
            // been written at this point, so there is nothing to roll back, and RegisteredEmail
            // is still the empty string it was initialised to - which is precisely the flag
            // LoginForm tests before pre-filling its email box.
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
