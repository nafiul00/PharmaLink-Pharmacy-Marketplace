using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 23. Everything about one medicine: manufacturer, strength,
    /// expiry date, selling pharmacy, whether a prescription is needed, and the
    /// full list of visible reviews.
    ///
    /// When an offer is running today the original price is struck through and
    /// the discounted price is shown beside it. That number comes from the same
    /// query the cart and the invoice use, so the three can never disagree.
    /// </summary>
    public partial class MedicineDetailsForm : Form
    {
        private readonly MedicineService _medicines = new MedicineService();
        private readonly ReviewService _reviews = new ReviewService();
        private readonly CartService _cart = new CartService();

        private readonly int _medicineId;
        private Medicine _medicine;

        public MedicineDetailsForm(int medicineId)
        {
            InitializeComponent();
            _medicineId = medicineId;
        }

        private void MedicineDetailsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadMedicine();
            LoadReviews();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Medicine Details");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblMedicineName.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
            lblMedicineName.ForeColor = Color.White;
            lblGenericName.Font = UiTheme.FontSmall;
            lblGenericName.ForeColor = Color.FromArgb(200, 230, 220);

            foreach (GroupBox group in new[] { grpFacts, grpPrice })
            {
                group.Font = UiTheme.FontHeading;
                group.ForeColor = UiTheme.Primary;
                group.BackColor = UiTheme.CardBack;
            }

            foreach (Label caption in new[] { lblManufacturerCaption, lblStrengthCaption, lblCategoryCaption,
                                              lblExpiryCaption, lblPharmacyCaption, lblStockCaption })
            {
                caption.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
                caption.ForeColor = UiTheme.TextMuted;
            }

            foreach (Label value in new[] { lblManufacturer, lblStrength, lblCategory,
                                            lblExpiry, lblPharmacy, lblStock })
            {
                value.Font = new Font("Segoe UI", 10.5F);
                value.ForeColor = UiTheme.TextDark;
            }

            lblDescription.Font = UiTheme.FontBody;
            lblDescription.ForeColor = UiTheme.TextMuted;
            lblRxBadge.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);

            lblReviewsTitle.Font = UiTheme.FontHeading;
            lblReviewsTitle.ForeColor = UiTheme.TextDark;
            lblAverageRating.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            lblAverageRating.ForeColor = UiTheme.TextDark;
            lblReviewNote.Font = UiTheme.FontSmall;
            lblReviewNote.ForeColor = UiTheme.TextMuted;
            lblAddMessage.Font = UiTheme.FontSmall;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StylePrimary(btnAddToCart);
            btnAddToCart.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            UiTheme.StyleGrid(dgvReviews);
            dgvReviews.CellFormatting += dgvReviews_CellFormatting;
        }

        private void LoadMedicine()
        {
            _medicine = _medicines.GetDetails(_medicineId);

            if (_medicine == null)
            {
                MessageBox.Show("That medicine is no longer available.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }

            lblMedicineName.Text = _medicine.MedicineName + "  " + _medicine.Strength;
            lblGenericName.Text = "Generic name: " + _medicine.GenericName;

            lblManufacturer.Text = _medicine.Manufacturer;
            lblStrength.Text = string.IsNullOrWhiteSpace(_medicine.Strength) ? "-" : _medicine.Strength;
            lblCategory.Text = _medicine.CategoryName;
            lblExpiry.Text = _medicine.ExpiryDate.ToString("dd MMM yyyy");
            lblPharmacy.Text = _medicine.PharmacyName + "   (" + _medicine.Area + ")";

            lblStock.Text = _medicine.Stock > 0
                ? _medicine.Stock + " unit(s) on the shelf"
                : "Out of stock";
            lblStock.ForeColor = _medicine.Stock > 0 ? UiTheme.Success : UiTheme.Danger;

            lblDescription.Text = string.IsNullOrWhiteSpace(_medicine.Description)
                ? "" : _medicine.Description;

            if (_medicine.RequiresRx)
            {
                lblRxBadge.Text = "  Rx  -  prescription only. You will be asked to upload a photograph of your " +
                                  "doctor's prescription at checkout, and the pharmacy must approve it before dispatch.";
                lblRxBadge.ForeColor = UiTheme.Warning;
            }
            else
            {
                lblRxBadge.Text = "  Over the counter  -  no prescription needed.";
                lblRxBadge.ForeColor = UiTheme.Success;
            }

            ShowPrice();

            btnAddToCart.Enabled = _medicine.Stock > 0;
            btnAddToCart.Text = _medicine.Stock > 0 ? "Add to cart" : "Out of stock";
            if (_medicine.Stock == 0) btnAddToCart.BackColor = Color.FromArgb(170, 190, 184);
        }

        private void ShowPrice()
        {
            if (_medicine.DiscountPercent > 0m)
            {
                // Struck through original beside the discounted price, exactly as
                // the report describes.
                lblOriginalPrice.Text = UiTheme.Money(_medicine.UnitPrice);
                lblOriginalPrice.Font = new Font("Segoe UI", 11F, FontStyle.Strikeout);
                lblOriginalPrice.ForeColor = UiTheme.TextMuted;

                lblFinalPrice.Text = UiTheme.Money(_medicine.PriceAfterDiscount);
                lblFinalPrice.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);
                lblFinalPrice.ForeColor = UiTheme.Success;

                decimal saving = _medicine.UnitPrice - _medicine.PriceAfterDiscount;
                lblDiscountBadge.Text = _medicine.DiscountPercent.ToString("N0") + "% off today  -  " +
                                        "you save " + UiTheme.Money(saving) + " per unit.";
                lblDiscountBadge.ForeColor = UiTheme.Success;
                lblDiscountBadge.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            }
            else
            {
                lblOriginalPrice.Text = "";
                lblFinalPrice.Text = UiTheme.Money(_medicine.UnitPrice);
                lblFinalPrice.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);
                lblFinalPrice.ForeColor = UiTheme.TextDark;

                lblDiscountBadge.Text = "No offer is running on this medicine today.";
                lblDiscountBadge.ForeColor = UiTheme.TextMuted;
                lblDiscountBadge.Font = UiTheme.FontSmall;
            }
        }

        private void LoadReviews()
        {
            DataTable table = _reviews.GetForMedicine(_medicineId);
            dgvReviews.DataSource = table;

            if (dgvReviews.Columns.Count > 0)
            {
                dgvReviews.Columns["ReviewId"].Visible = false;
                dgvReviews.Columns["ReviewerName"].HeaderText = "Reviewer";
                dgvReviews.Columns["ReviewerName"].FillWeight = 55;
                dgvReviews.Columns["Rating"].HeaderText = "Stars";
                dgvReviews.Columns["Rating"].FillWeight = 25;
                dgvReviews.Columns["Comment"].HeaderText = "Comment";
                dgvReviews.Columns["Comment"].FillWeight = 180;
                dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";
                dgvReviews.Columns["ReviewDate"].FillWeight = 50;
            }

            decimal average = _reviews.GetAverageForMedicine(_medicineId);

            lblAverageRating.Text = table.Rows.Count == 0
                ? "No reviews yet"
                : average.ToString("N2") + " / 5   from " + table.Rows.Count + " review(s)";

            lblReviewsTitle.Text = table.Rows.Count == 0
                ? "What other patients said  -  nobody has reviewed this yet"
                : "What other patients said";
        }

        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;

            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];
            object rating = row.Cells["Rating"].Value;
            if (rating == null || rating == DBNull.Value) return;

            int stars = Convert.ToInt32(rating);
            if (stars <= 2) row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
            else if (stars >= 4) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        // ---------------------------------------------------------------------

        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            int quantity;
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))
            {
                UiTheme.ShowError(lblAddMessage, txtQuantity, "Enter a whole quantity of one or more.");
                lblAddMessage.Visible = true;
                return;
            }

            string message;
            if (_cart.AddOrIncrease(UserSession.UserId, _medicineId, quantity, out message))
            {
                txtQuantity.BackColor = Color.White;
                lblAddMessage.Text = quantity + " added to your cart.";
                lblAddMessage.ForeColor = UiTheme.Success;
                lblAddMessage.Visible = true;
            }
            else
            {
                lblAddMessage.Text = message;
                lblAddMessage.ForeColor = UiTheme.Danger;
                lblAddMessage.Visible = true;
            }
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
