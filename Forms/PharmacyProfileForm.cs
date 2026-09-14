using System.Drawing;               // Color and Font, used only by the theming pass
using System.Windows.Forms;         // Form, Label, Control, GroupBox, OpenFileDialog, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // Pharmacy, the typed object the shop row becomes
using PharmaLinkApp.Services;       // PharmacyService and ReviewService, the only classes that reach SQL

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 11, the shop half of the profile.
    ///
    /// Every UPDATE on this form carries WHERE PharmacyId = @PharmacyId, so an
    /// owner cannot edit another shop. The licence number is displayed read only
    /// because changing it would mean a new licence and a fresh approval, and
    /// the commission rate, status and rating are shown but belong to the Super
    /// Admin.
    /// </summary>
    public partial class PharmacyProfileForm : Form
    {
        // Two services, because the screen answers two different questions: what the shop
        // record says, and what customers think of it. Neither holds a connection of its
        // own, since DbHelper opens and closes one inside every call.
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly ReviewService _reviews = new ReviewService();

        // True while LoadPharmacy is filling the boxes. Every editable control raises
        // Field_Changed on assignment, so without this the fill would run a validation
        // pass per control and could paint errors over values straight from the database.
        private bool _loading = true;

        public PharmacyProfileForm()
        {
            InitializeComponent();      // build the controls from the Designer file first
            // Nothing else here. Reading the shop waits for the Load event, because a
            // constructor that throws leaves no window in which to show the error.
        }

        private void PharmacyProfileForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours and fonts only, no data
            LoadPharmacy();     // the one read that fills both halves of the screen

            // The fill is over, so changes from here on are the user's and must revalidate.
            _loading = false;

            // One deliberate pass, so Save starts in the state the loaded values deserve
            // rather than waiting for the first keystroke to settle it.
            ValidateAll();
        }

        // Colours and fonts only. Nothing here reads or writes data.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Pharmacy Profile");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            foreach (GroupBox group in new[] { grpShop, grpFacts })
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

            foreach (Label caption in new[] { lblStatusCaption, lblCommissionCaption, lblRatingCaption, lblRegisteredCaption })
            {
                caption.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
                caption.ForeColor = UiTheme.TextMuted;
            }

            foreach (Label value in new[] { lblStatusValue, lblCommissionValue, lblRatingValue, lblRegisteredValue })
            {
                value.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
                value.ForeColor = UiTheme.TextDark;
            }

            txtLicense.BackColor = Color.FromArgb(240, 242, 244);
            lblLicenseNote.Font = UiTheme.FontSmall;
            lblLicenseNote.ForeColor = UiTheme.TextMuted;
            lblFactsNote.Font = UiTheme.FontSmall;
            lblFactsNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnBrowseLogo);
            UiTheme.StylePrimary(btnSave);
            btnSave.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        }

        private void LoadPharmacy()
        {
            // Read by the PharmacyId held in the session, not by anything on screen. There
            // is no shop picker on this form and no id field to tamper with, so the only
            // record it can ever load or write is the one the logged in owner holds.
            Pharmacy pharmacy = _pharmacies.GetById(UserSession.PharmacyId);

            // A missing row means the session is pointing at a shop that no longer exists,
            // which nothing on this screen can recover from. Closing is the honest response;
            // carrying on would dereference a null on the very next line.
            if (pharmacy == null)
            {
                MessageBox.Show("Your pharmacy record could not be loaded.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            // The editable half. _loading is still true here, so none of these assignments
            // triggers a validation pass.
            txtShopName.Text = pharmacy.PharmacyName;

            // Filled in, but the box is read only and greyed by the theming above. A licence
            // number identifies the shop to the regulator, so changing it would mean a
            // different licence and a fresh approval by the Super Admin rather than an edit.
            // It is also UNIQUE, so letting it be retyped would invite a constraint failure
            // for a change that should never be made from here anyway.
            txtLicense.Text = pharmacy.LicenseNo;

            txtAddress.Text = pharmacy.Address;
            txtContact.Text = pharmacy.ContactPhone;
            txtLogoPath.Text = pharmacy.LogoPath;

            // Cleared first, because this method can run more than once and adding to a
            // populated list would leave every area listed twice.
            cmbArea.Items.Clear();

            // false means "not approved only", so every area already in use appears,
            // including those of shops still awaiting approval. The list exists to spare
            // the owner typing a new spelling of an area that already exists, which is what
            // would break the customer's Area filter.
            foreach (string area in _pharmacies.GetAreas(false))
                cmbArea.Items.Add(area);

            // .Text rather than .SelectedItem, because the combo allows free text: a shop in
            // an area nobody has registered yet must still be able to say so. Assigning
            // SelectedItem would silently do nothing for a value not in the list.
            cmbArea.Text = pharmacy.Area;

            // THE READ ONLY FACTS. Everything below is shown and never edited here, because
            // each of these belongs to somebody else's decision.
            lblStatusValue.Text = pharmacy.Status;

            // Green only for Approved. Pending and Suspended both mean the shop cannot
            // trade, so they share the warning colour rather than each getting their own.
            lblStatusValue.ForeColor = pharmacy.Status == "Approved" ? UiTheme.Success : UiTheme.Danger;

            // The commission the platform takes is set by the Super Admin and capped by
            // CK_Pharmacies_Comm at 30 percent. It is displayed because it decides the
            // owner's earnings, and it is not editable because letting a shop choose its own
            // commission would be letting it write its own contract.
            lblCommissionValue.Text = pharmacy.CommissionRate.ToString("N2") + " %";

            // The registration date is set once by a column default and never changes.
            lblRegisteredValue.Text = pharmacy.RegisteredAt.ToString("dd MMM yyyy");

            // The rating is customer opinion, computed from the Reviews table, so it can be
            // read here but could not be edited here under any circumstances.
            decimal rating = _reviews.GetAverageForPharmacy(UserSession.PharmacyId);
            int reviewCount = _reviews.CountForPharmacy(UserSession.PharmacyId);

            // The count is fetched as well as the average because an average over no rows is
            // 0, and "0.00 / 5" would read as a terrible rating rather than as no rating at
            // all. Naming the empty state is the only honest way to show it.
            lblRatingValue.Text = reviewCount == 0 ? "no reviews yet" : rating.ToString("N2") + " / 5";

            // Red only when there are real reviews AND they are poor. The count test is what
            // stops a brand new shop with no reviews being painted as a failing one.
            lblRatingValue.ForeColor = reviewCount > 0 && rating < 2.5m ? UiTheme.Danger : UiTheme.TextDark;

            // Says where these fields actually surface, because "shop name" reads as a label
            // until the owner knows customers search on it and invoices print it.
            lblStatus.Text = "Your shop name, area and address appear on the customer catalogue and are printed on every invoice.";
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        // The four editable controls share this handler, so there is one entry point for
        // "something changed" rather than four that could drift apart.
        private void Field_Changed(object sender, EventArgs e)
        {
            // LoadPharmacy is filling the controls, not the user, so there is nothing to
            // judge yet.
            if (_loading) return;
            ValidateAll();
        }

        private bool ValidateAll()
        {
            bool ok = true;

            // Every rule on this form is a NOT NULL column stated in plain English. The
            // columns are declared NOT NULL in the Pharmacies table, so a blank would be
            // refused there too; checking here turns that refusal into a red label under
            // the box instead of an exception dialog after the fact.
            //
            // &= rather than &&: the right hand side is evaluated every time, so all four
            // rules run and every failing field is marked on the same pass. With && the
            // first failure would short circuit the rest and the owner would meet the
            // errors one at a time.
            ok &= Check(!Validator.IsBlank(txtShopName.Text), lblShopNameError, txtShopName,
                        "The shop name cannot be empty - it is what customers search for.");

            // cmbArea is passed as the field so the combo itself tints red. The message
            // names the consequence rather than the rule: an empty area does not just fail
            // a check, it makes the shop invisible to the customer's Area filter.
            ok &= Check(!Validator.IsBlank(cmbArea.Text), lblAreaError, cmbArea,
                        "The area is what the customer's Area filter uses, so it is required.");

            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "The address is printed on every invoice, so it cannot be empty.");

            ok &= Check(!Validator.IsBlank(txtContact.Text), lblContactError, txtContact,
                        "Enter a contact number customers can reach the shop on.");

            // Disabling the button is the real enforcement: an invalid form cannot be
            // submitted at all, rather than being submitted and then rejected.
            btnSave.Enabled = ok;

            // Greyed as well, because a disabled button still painted primary green reads
            // as a broken button rather than a blocked one.
            btnSave.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        // One helper for every rule, so showing and clearing an error is written once. It
        // returns the verdict it was given, which is what lets the caller write
        // "ok &= Check(...)" and get the painting and the accumulation in a single line.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Clearing on success matters as much as showing on failure, or a message from
            // an earlier keystroke would sit under a field that is now correct.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------

        private void btnBrowseLogo_Click(object sender, EventArgs e)
        {
            // using, so the dialog's unmanaged window handle is released as soon as it
            // closes rather than waiting for the garbage collector.
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                // Restricted to the three image formats the application can display, so a
                // document or a spreadsheet cannot be chosen as a shop logo by mistake.
                dialog.Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";

                // Only OK overwrites the box, so cancelling leaves any existing logo path
                // alone rather than blanking it. Note what is stored is the PATH: the image
                // itself stays on disk and the column holds a reference to it, which keeps
                // the database small and the rows quick to read.
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtLogoPath.Text = dialog.FileName;
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            // Revalidated even though the button is only enabled when valid, because a
            // keyboard shortcut can raise a click the enabled state did not anticipate.
            if (!ValidateAll()) return;

            try
            {
                // PharmacyId comes from the session, never from the form, so an owner can
                // only ever edit their own shop - there is no id field on screen to
                // tamper with. Note which fields are NOT passed: LicenseNo, Status and
                // CommissionRate. The licence is shown read only because changing it
                // would mean a new licence and a fresh approval; the other two belong to
                // the Super Admin, so the owner's UPDATE simply cannot touch them.
                //
                // The protection is structural rather than a permission test: those columns
                // are absent from the SET list, so no amount of tampering with this screen
                // could reach them.
                if (_pharmacies.UpdateProfile(UserSession.PharmacyId, txtShopName.Text, cmbArea.Text,
                                              txtAddress.Text, txtContact.Text, txtLogoPath.Text))
                {
                    // Keep the cached session name in step with the database, or the
                    // dashboard header would keep showing the old shop name until logout.
                    // Trimmed to match exactly what the service wrote to the column.
                    UserSession.PharmacyName = txtShopName.Text.Trim();

                    // Said twice on purpose: the status line is the quiet record that stays
                    // on screen, the message box is the acknowledgement that cannot be
                    // missed. The wording points out that customers see this at once, which
                    // is true because their catalogue reads the same row rather than a copy.
                    lblStatus.Text = "Shop profile saved. Customers see the new details immediately.";
                    MessageBox.Show("Your pharmacy profile has been updated.", "PharmaLink",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                // A false return means the UPDATE matched no row. Nothing is claimed in that
                // case, which is better than announcing a save that did not happen.
            }
            catch (Exception ex)
            {
                // The constraints on Pharmacies are the real guarantee and can still refuse
                // a row this form thought was fine. Showing the message keeps the typed
                // values on screen so one field can be corrected rather than all of them
                // retyped.
                MessageBox.Show("The profile could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Close, not logout: this form was opened from the owner's dashboard and closing it
        // returns there with the session intact.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
