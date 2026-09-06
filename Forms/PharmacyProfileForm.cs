using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

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
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly ReviewService _reviews = new ReviewService();
        private bool _loading = true;

        public PharmacyProfileForm()
        {
            InitializeComponent();
        }

        private void PharmacyProfileForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadPharmacy();
            _loading = false;
            ValidateAll();
        }

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
            Pharmacy pharmacy = _pharmacies.GetById(UserSession.PharmacyId);

            if (pharmacy == null)
            {
                MessageBox.Show("Your pharmacy record could not be loaded.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            txtShopName.Text = pharmacy.PharmacyName;
            txtLicense.Text = pharmacy.LicenseNo;
            txtAddress.Text = pharmacy.Address;
            txtContact.Text = pharmacy.ContactPhone;
            txtLogoPath.Text = pharmacy.LogoPath;

            cmbArea.Items.Clear();
            foreach (string area in _pharmacies.GetAreas(false))
                cmbArea.Items.Add(area);
            cmbArea.Text = pharmacy.Area;

            lblStatusValue.Text = pharmacy.Status;
            lblStatusValue.ForeColor = pharmacy.Status == "Approved" ? UiTheme.Success : UiTheme.Danger;

            lblCommissionValue.Text = pharmacy.CommissionRate.ToString("N2") + " %";
            lblRegisteredValue.Text = pharmacy.RegisteredAt.ToString("dd MMM yyyy");

            decimal rating = _reviews.GetAverageForPharmacy(UserSession.PharmacyId);
            int reviewCount = _reviews.CountForPharmacy(UserSession.PharmacyId);

            lblRatingValue.Text = reviewCount == 0 ? "no reviews yet" : rating.ToString("N2") + " / 5";
            lblRatingValue.ForeColor = reviewCount > 0 && rating < 2.5m ? UiTheme.Danger : UiTheme.TextDark;

            lblStatus.Text = "Your shop name, area and address appear on the customer catalogue and are printed on every invoice.";
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        private void Field_Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            ValidateAll();
        }

        private bool ValidateAll()
        {
            bool ok = true;

            ok &= Check(!Validator.IsBlank(txtShopName.Text), lblShopNameError, txtShopName,
                        "The shop name cannot be empty - it is what customers search for.");

            ok &= Check(!Validator.IsBlank(cmbArea.Text), lblAreaError, cmbArea,
                        "The area is what the customer's Area filter uses, so it is required.");

            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "The address is printed on every invoice, so it cannot be empty.");

            ok &= Check(!Validator.IsBlank(txtContact.Text), lblContactError, txtContact,
                        "Enter a contact number customers can reach the shop on.");

            btnSave.Enabled = ok;
            btnSave.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------

        private void btnBrowseLogo_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtLogoPath.Text = dialog.FileName;
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateAll()) return;

            try
            {
                if (_pharmacies.UpdateProfile(UserSession.PharmacyId, txtShopName.Text, cmbArea.Text,
                                              txtAddress.Text, txtContact.Text, txtLogoPath.Text))
                {
                    UserSession.PharmacyName = txtShopName.Text.Trim();
                    lblStatus.Text = "Shop profile saved. Customers see the new details immediately.";
                    MessageBox.Show("Your pharmacy profile has been updated.", "PharmaLink",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("The profile could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
