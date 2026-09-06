using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 29. Every offer that is actually running today.
    ///
    /// The query returns only offers where today falls between StartDate and
    /// EndDate, on a medicine that is in stock, at a pharmacy that is Approved.
    /// Expired offers are excluded by the database rather than by this form, so
    /// a stale discount can never be displayed by mistake.
    /// </summary>
    public partial class CustomerOffersForm : Form
    {
        private readonly OfferService _offers = new OfferService();
        private readonly CategoryService _categories = new CategoryService();
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly CartService _cart = new CartService();

        private bool _loading = true;

        public CustomerOffersForm()
        {
            InitializeComponent();
        }

        private void CustomerOffersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbCategory.Items.Add("All categories");
            foreach (Category category in _categories.GetActiveList())
                cmbCategory.Items.Add(category.CategoryId + " - " + category.CategoryName);
            cmbCategory.SelectedIndex = 0;

            cmbArea.Items.Add("All areas");
            foreach (string area in _pharmacies.GetAreas(true)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;

            lblToday.Text = "Showing offers valid on " + DateTime.Today.ToString("dd MMM yyyy");

            _loading = false;
            LoadGrid();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Offers and Packages");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Success;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(215, 240, 225);

            lblToday.Font = UiTheme.FontSmall;
            lblToday.ForeColor = UiTheme.TextMuted;
            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StylePrimary(btnAddToCart);
            UiTheme.StyleAccent(btnViewDetails);
            UiTheme.StyleGrid(dgvOffers);
            dgvOffers.CellFormatting += dgvOffers_CellFormatting;
        }

        private int SelectedCategoryId()
        {
            if (cmbCategory.SelectedIndex <= 0) return 0;
            string text = cmbCategory.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();
                DataTable table = _offers.GetActiveOffers(SelectedCategoryId(), area);
                dgvOffers.DataSource = table;

                if (dgvOffers.Columns.Count > 0)
                {
                    dgvOffers.Columns["OfferId"].HeaderText = "ID";
                    dgvOffers.Columns["OfferId"].FillWeight = 26;
                    dgvOffers.Columns["OfferTitle"].HeaderText = "Offer";
                    dgvOffers.Columns["OfferTitle"].FillWeight = 130;
                    dgvOffers.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvOffers.Columns["Strength"].HeaderText = "Strength";
                    dgvOffers.Columns["Strength"].FillWeight = 42;
                    dgvOffers.Columns["CategoryName"].HeaderText = "Category";
                    dgvOffers.Columns["PharmacyName"].HeaderText = "Sold by";
                    dgvOffers.Columns["Area"].HeaderText = "Area";
                    dgvOffers.Columns["Area"].FillWeight = 42;
                    dgvOffers.Columns["OriginalPrice"].HeaderText = "Was (Tk)";
                    dgvOffers.Columns["OriginalPrice"].FillWeight = 44;
                    dgvOffers.Columns["DiscountPercent"].HeaderText = "Off %";
                    dgvOffers.Columns["DiscountPercent"].FillWeight = 32;
                    dgvOffers.Columns["DiscountedPrice"].HeaderText = "You pay (Tk)";
                    dgvOffers.Columns["DiscountedPrice"].FillWeight = 50;
                    dgvOffers.Columns["YouSave"].HeaderText = "Save (Tk)";
                    dgvOffers.Columns["YouSave"].FillWeight = 44;
                    dgvOffers.Columns["EndDate"].HeaderText = "Valid until";
                    dgvOffers.Columns["MedicineId"].Visible = false;
                    dgvOffers.Columns["Stock"].HeaderText = "Stock";
                    dgvOffers.Columns["Stock"].FillWeight = 34;
                }

                lblStatus.Text = table.Rows.Count == 0
                    ? "No offer is running today for that combination of filters."
                    : table.Rows.Count + " offer(s) running today. Add one to your cart and the discounted price " +
                      "follows it all the way to the invoice.";

                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvOffers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            dgvOffers.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
        }

        private void dgvOffers_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            bool hasRow = dgvOffers.CurrentRow != null && dgvOffers.CurrentRow.Cells["MedicineId"].Value != null;
            btnAddToCart.Enabled = hasRow;
            btnViewDetails.Enabled = hasRow;
        }

        private int SelectedMedicineId()
        {
            if (dgvOffers.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvOffers.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---------------------------------------------------------------------

        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            int quantity;
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))
            {
                MessageBox.Show("Enter a whole quantity of one or more.", "Check the quantity",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string message;
            if (_cart.AddOrIncrease(UserSession.UserId, medicineId, quantity, out message))
            {
                string name = dgvOffers.CurrentRow.Cells["MedicineName"].Value.ToString();
                string save = dgvOffers.CurrentRow.Cells["YouSave"].Value.ToString();

                lblStatus.Text = quantity + " x " + name + " added to your cart at the offer price. " +
                                 "You are saving Tk " + save + " per unit.";
                txtQuantity.Text = "1";
            }
            else
            {
                MessageBox.Show(message, "Cannot add to cart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnViewDetails_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            using (MedicineDetailsForm details = new MedicineDetailsForm(medicineId))
            {
                details.ShowDialog(this);
            }
            LoadGrid();
        }

        private void dgvOffers_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            btnViewDetails_Click(sender, EventArgs.Empty);
        }

        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
