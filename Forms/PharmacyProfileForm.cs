using System.Drawing;               // Color and Font, used only by the theming pass
using System.Windows.Forms;         // Form, Label, GroupBox, OpenFileDialog, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // Pharmacy, the typed object the shop row becomes
using PharmaLinkApp.Services;       // the only classes on this screen that reach SQL

// Forms hold no SQL; everything here goes through the Services namespace above.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 11: the owner's own shop, and only theirs.</summary>
    public partial class PharmacyProfileForm : Form
    {
        // What the shop record says.
        private readonly PharmacyService _pharmacies = new PharmacyService();
        // What customers think of it, which is computed from Reviews, not a column.
        private readonly ReviewService _reviews = new ReviewService();

        // True while LoadPharmacy fills the boxes, so the fill cannot paint errors.
        private bool _loading = true;

        // Does the minimum; the real work waits for the Load event below.
        public PharmacyProfileForm()
        {
            InitializeComponent();      // build the controls from the Designer file first
            // A constructor that throws would leave no window to show the error in.
        }

        // Raised after the window exists, so a failed read can be shown in a dialog.
        private void PharmacyProfileForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours and fonts only, no data
            LoadPharmacy();     // the one read that fills both halves of the screen

            // The fill is over, so changes from here are the user's and must revalidate.
            _loading = false;

            // One deliberate pass, so Save starts in the state the loaded values deserve.
            ValidateAll();
        }

        // Colours and fonts only. Nothing here reads or writes data.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Pharmacy Profile");   // size, icon, background and caption

            panelHeader.BackColor = UiTheme.Primary;          // the green band every screen wears
            lblTitle.Font = UiTheme.FontTitle;                // one shared font, so headings match
            lblTitle.ForeColor = Color.White;                 // the only legible ink on that green
            lblSubtitle.Font = UiTheme.FontSmall;             // smaller: it explains, not announces
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // a pale tint, so it recedes

            // One loop, so a group added in the designer inherits the look for free.
            foreach (GroupBox group in new[] { grpShop, grpFacts })
            {
                group.Font = UiTheme.FontHeading;             // the caption text of the box itself
                group.ForeColor = UiTheme.Primary;            // brand green, so it reads as a title
                group.BackColor = UiTheme.CardBack;           // off-white, lifting it off the form

                // Children are walked rather than named, so a new control is themed too.
                foreach (Control child in group.Controls)
                {
                    child.Font = UiTheme.FontBody;            // one body font for every label and box
                    child.ForeColor = UiTheme.TextDark;       // the default ink; errors override it
                    // Error labels are recognised by their name suffix, not by a list.
                    if (child is Label label && label.Name.EndsWith("Error"))
                    {
                        label.Font = UiTheme.FontSmall;       // small, because it sits under its field
                        label.ForeColor = UiTheme.Danger;     // red is what makes it read as a problem
                    }
                }
            }

            // The four small captions above the read-only figures.
            foreach (Label caption in new[] { lblStatusCaption, lblCommissionCaption, lblRatingCaption, lblRegisteredCaption })
            {
                caption.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);   // small but bold, so it reads as a label
                caption.ForeColor = UiTheme.TextMuted;        // muted, so the eye lands on the number
            }

            // The figures themselves, big and dark, because they are the answers.
            foreach (Label value in new[] { lblStatusValue, lblCommissionValue, lblRatingValue, lblRegisteredValue })
            {
                value.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);    // the largest type on the form
                value.ForeColor = UiTheme.TextDark;           // status and rating overwrite this later
            }

            txtLicense.BackColor = Color.FromArgb(240, 242, 244);   // grey, so read-only looks disabled
            lblLicenseNote.Font = UiTheme.FontSmall;          // the note explaining the locked licence
            lblLicenseNote.ForeColor = UiTheme.TextMuted;     // muted, because it is guidance
            lblFactsNote.Font = UiTheme.FontSmall;            // the matching note over the facts panel
            lblFactsNote.ForeColor = UiTheme.TextMuted;       // same treatment, so the two read alike
            lblStatus.Font = UiTheme.FontSmall;               // the quiet status line at the foot
            lblStatus.ForeColor = UiTheme.TextMuted;          // muted until it carries a message

            UiTheme.StyleSecondary(btnBack);                  // outlined: leaving is not encouraged
            UiTheme.StyleSecondary(btnBrowseLogo);            // picking a file is a step towards Save
            UiTheme.StylePrimary(btnSave);                    // filled green, the action this form is for
            btnSave.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);   // heavier, so it carries weight
        }

        // The single read that fills both halves of the form.
        private void LoadPharmacy()
        {
            // Read by the session's PharmacyId, so only the owner's own shop can load.
            Pharmacy pharmacy = _pharmacies.GetById(UserSession.PharmacyId);

            // A missing row means the session points at a shop that no longer exists.
            if (pharmacy == null)
            {
                // Error, not Warning: an unreadable shop row is a fault, not a choice.
                MessageBox.Show("Your pharmacy record could not be loaded.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);   // one OK, there is no choice to offer
                Close();     // nothing on this form can work without a shop row
                return;      // Close only requests the shutdown, so this stops the rest
            }

            // The editable half; _loading is still true, so no validation runs yet.
            txtShopName.Text = pharmacy.PharmacyName;

            // Read only: a different licence number means a fresh approval, not an edit.
            txtLicense.Text = pharmacy.LicenseNo;

            txtAddress.Text = pharmacy.Address;        // printed on every invoice, so it is required
            txtContact.Text = pharmacy.ContactPhone;   // how a customer reaches the shop
            txtLogoPath.Text = pharmacy.LogoPath;      // a path, not an image: the file stays on disk

            // Cleared first, because a second call would list every area twice.
            cmbArea.Items.Clear();

            // false means 'not approved only', so every area already in use appears.
            foreach (string area in _pharmacies.GetAreas(false))
                cmbArea.Items.Add(area);   // one entry per distinct area on the platform

            // .Text, not .SelectedItem, because the combo allows a brand new area.
            cmbArea.Text = pharmacy.Area;

            // THE READ ONLY FACTS: each of these belongs to somebody else's decision.
            lblStatusValue.Text = pharmacy.Status;

            // Pending and Suspended both mean the shop cannot trade, so they share red.
            lblStatusValue.ForeColor = pharmacy.Status == "Approved" ? UiTheme.Success : UiTheme.Danger;

            // Set by the Super Admin and capped at 30 percent by CK_Pharmacies_Comm.
            lblCommissionValue.Text = pharmacy.CommissionRate.ToString("N2") + " %";

            // Set once by a column default and never changed afterwards.
            lblRegisteredValue.Text = pharmacy.RegisteredAt.ToString("dd MMM yyyy");

            // Customer opinion, so it can be read here but never edited here.
            decimal rating = _reviews.GetAverageForPharmacy(UserSession.PharmacyId);
            // The count too, because an average alone cannot say 'no reviews yet'.
            int reviewCount = _reviews.CountForPharmacy(UserSession.PharmacyId);

            // The empty state is named, because '0.00 / 5' would read as a bad score.
            lblRatingValue.Text = reviewCount == 0 ? "no reviews yet" : rating.ToString("N2") + " / 5";

            // The count test stops a brand new shop being painted as a failing one.
            lblRatingValue.ForeColor = reviewCount > 0 && rating < 2.5m ? UiTheme.Danger : UiTheme.TextDark;

            // Says where these fields actually surface, which the labels do not.
            lblStatus.Text = "Your shop name, area and address appear on the customer catalogue and are printed on every invoice.";
        }

        // VALIDATION

        // One entry point for 'something changed', shared by the four editable fields.
        private void Field_Changed(object sender, EventArgs e)
        {
            // LoadPharmacy is filling the controls, not the user, so nothing to judge.
            if (_loading) return;
            ValidateAll();   // every other change is the owner's, so recompute the verdict
        }

        // Returns the verdict as well as painting it, so Save can use it as its gate.
        private bool ValidateAll()
        {
            bool ok = true;   // starts true and is narrowed by each rule below

            // &= not &&, so all four rules run and every bad field is marked at once.
            ok &= Check(!Validator.IsBlank(txtShopName.Text), lblShopNameError, txtShopName,
                        "The shop name cannot be empty - it is what customers search for.");   // the consequence, not the rule

            // The combo itself tints red; an empty area hides the shop from the filter.
            ok &= Check(!Validator.IsBlank(cmbArea.Text), lblAreaError, cmbArea,
                        "The area is what the customer's Area filter uses, so it is required.");   // an empty area hides the shop

            // The address is the delivery destination that gets printed on the bill.
            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "The address is printed on every invoice, so it cannot be empty.");   // InvoiceForm prints this field

            // Presence only, no pattern: this project cannot verify a phone number.
            ok &= Check(!Validator.IsBlank(txtContact.Text), lblContactError, txtContact,
                        "Enter a contact number customers can reach the shop on.");   // no format test on purpose

            // Disabling the button is the real enforcement, not the messages above.
            btnSave.Enabled = ok;

            // Greyed too, or a disabled green button reads as broken rather than blocked.
            btnSave.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;   // handed back so the click handler can re-run the same gate
        }

        // One helper for every rule, so showing and clearing an error is written once.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Clearing on success matters too, or an old message sits under a good field.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            // ShowError writes the sentence and tints the field, so the box is findable.
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;   // passed through, so the caller can accumulate it
        }

        // Picks a logo file. It only fills a text box; nothing is written yet.
        private void btnBrowseLogo_Click(object sender, EventArgs e)
        {
            // using, so the dialog's window handle is released as soon as it closes.
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                // Only the three image formats the application can display.
                dialog.Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";

                // Only OK overwrites the box, so cancelling leaves the old path alone.
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtLogoPath.Text = dialog.FileName;   // the path is what the column stores
            }
        }

        // The one write on this form, and only of the fields the owner may change.
        private void btnSave_Click(object sender, EventArgs e)
        {
            // Revalidated, because a keyboard shortcut can raise a click regardless.
            if (!ValidateAll()) return;

            // The table's constraints can still refuse a row this form thought was fine.
            try
            {
                // No LicenseNo, Status or CommissionRate here: the owner cannot set those.
                if (_pharmacies.UpdateProfile(UserSession.PharmacyId, txtShopName.Text, cmbArea.Text,   // id from the session, never the form
                                              txtAddress.Text, txtContact.Text, txtLogoPath.Text))   // true only when the UPDATE matched the row
                {
                    // Keeps the cached name in step, or the dashboard shows the old one.
                    UserSession.PharmacyName = txtShopName.Text.Trim();

                    // The quiet record that stays on screen once the dialog is gone.
                    lblStatus.Text = "Shop profile saved. Customers see the new details immediately.";
                    // Information, not a warning: this confirms something that worked.
                    MessageBox.Show("Your pharmacy profile has been updated.", "PharmaLink",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);   // the unmissable half
                }
                // A false return means the UPDATE matched no row, so nothing is claimed.
            }
            // Reached only when the server refused the row outright.
            catch (Exception ex)
            {
                // Showing the message keeps the typed values on screen to be corrected.
                MessageBox.Show("The profile could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // the form stays open
            }
        }

        // Close, not logout: the owner's dashboard opened this form and gets it back.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
