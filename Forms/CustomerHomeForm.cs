using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// The customer's home screen (requirements 20, 21 and 22).
    ///
    /// One search box over three columns - brand name, generic name and
    /// manufacturer - so a patient holding a doctor's chit that says
    /// "paracetamol" finds Napa and Ace Plus even though neither brand contains
    /// that word. Five ComboBox filters narrow the result: category, price
    /// range, area, pharmacy and availability. They are combined in a single
    /// query, and only medicines belonging to an Approved pharmacy are ever
    /// returned.
    /// </summary>
    public partial class CustomerHomeForm : Form
    {
        private readonly MedicineService _medicines = new MedicineService();
        private readonly CategoryService _categories = new CategoryService();
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly CartService _cart = new CartService();

        private bool _loading = true;

        public CustomerHomeForm()
        {
            InitializeComponent();
        }

        private void CustomerHomeForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadFilters();
            _loading = false;
            LoadGrid();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Browse Medicines");

            panelSide.BackColor = UiTheme.Sidebar;
            lblBrand.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
            lblBrand.ForeColor = Color.White;
            lblRole.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            lblRole.ForeColor = Color.FromArgb(140, 205, 185);
            lblUserName.Font = UiTheme.FontSmall;
            lblUserName.ForeColor = Color.FromArgb(190, 205, 216);
            lblUserName.Text = UserSession.FullName;

            foreach (Button button in new[] { btnBrowse, btnCart, btnOffers, btnOrders, btnMyAccount })
                UiTheme.StyleSidebarButton(button);

            btnBrowse.BackColor = UiTheme.SidebarHover;   // the screen we are on

            UiTheme.StyleSidebarButton(btnLogout);
            btnLogout.BackColor = UiTheme.Danger;
            btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 60, 60);

            panelHeader.BackColor = UiTheme.Primary;
            lblHeaderTitle.Font = UiTheme.FontTitle;
            lblHeaderTitle.ForeColor = Color.White;
            lblHeaderSub.Font = UiTheme.FontSmall;
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);
            lblCartSummary.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            lblCartSummary.ForeColor = Color.White;

            UiTheme.StyleSecondary(btnSearch);
            UiTheme.StyleSecondary(btnClearFilters);
            UiTheme.StyleAccent(btnDetails);
            UiTheme.StylePrimary(btnAddToCart);
            UiTheme.StyleSecondary(btnOpenCart);
            btnAddToCart.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);

            UiTheme.StyleGrid(dgvMedicines);
            dgvMedicines.CellFormatting += dgvMedicines_CellFormatting;

            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private void LoadFilters()
        {
            cmbCategory.Items.Add("All categories");
            foreach (Category category in _categories.GetActiveList())
                cmbCategory.Items.Add(category.CategoryId + " - " + category.CategoryName);
            cmbCategory.SelectedIndex = 0;

            cmbPriceRange.Items.AddRange(new object[]
            {
                "Any price",
                "Under Tk 10",
                "Tk 10 - 50",
                "Tk 50 - 200",
                "Tk 200 - 1000",
                "Over Tk 1000"
            });
            cmbPriceRange.SelectedIndex = 0;

            cmbArea.Items.Add("All areas");
            foreach (string area in _pharmacies.GetAreas(true)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;

            cmbPharmacy.Items.Add("All pharmacies");
            foreach (Pharmacy pharmacy in _pharmacies.GetApprovedList())
                cmbPharmacy.Items.Add(pharmacy.PharmacyId + " - " + pharmacy.PharmacyName);
            cmbPharmacy.SelectedIndex = 0;

            cmbAvailability.Items.AddRange(new object[] { "Any", "In stock" });
            cmbAvailability.SelectedIndex = 1;   // in stock only is the sensible default
        }

        // ---------------------------------------------------------------------
        //  READING THE FILTERS
        // ---------------------------------------------------------------------

        private int SelectedCategoryId()
        {
            if (cmbCategory.SelectedIndex <= 0) return 0;
            string text = cmbCategory.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        private int SelectedPharmacyId()
        {
            if (cmbPharmacy.SelectedIndex <= 0) return 0;
            string text = cmbPharmacy.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        private void SelectedPriceRange(out decimal minPrice, out decimal maxPrice)
        {
            switch (cmbPriceRange.SelectedIndex)
            {
                case 1: minPrice = 0m; maxPrice = 10m; break;
                case 2: minPrice = 10m; maxPrice = 50m; break;
                case 3: minPrice = 50m; maxPrice = 200m; break;
                case 4: minPrice = 200m; maxPrice = 1000m; break;
                case 5: minPrice = 1000m; maxPrice = 999999m; break;
                default: minPrice = 0m; maxPrice = 0m; break;   // 0 max means "do not filter"
            }
        }

        private int ActiveFilterCount()
        {
            int count = 0;
            if (!string.IsNullOrWhiteSpace(txtSearch.Text)) count++;
            if (cmbCategory.SelectedIndex > 0) count++;
            if (cmbPriceRange.SelectedIndex > 0) count++;
            if (cmbArea.SelectedIndex > 0) count++;
            if (cmbPharmacy.SelectedIndex > 0) count++;
            if (cmbAvailability.SelectedIndex == 1) count++;
            return count;
        }

        // ---------------------------------------------------------------------

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                decimal minPrice, maxPrice;
                SelectedPriceRange(out minPrice, out maxPrice);

                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();

                DataTable table = _medicines.SearchForCustomer(
                    txtSearch.Text.Trim(),
                    SelectedCategoryId(),
                    minPrice, maxPrice,
                    area,
                    SelectedPharmacyId(),
                    cmbAvailability.SelectedIndex == 1);

                dgvMedicines.DataSource = table;
                LabelColumns();

                int cartLines = _cart.CountLines(UserSession.UserId);
                lblCartSummary.Text = cartLines == 0 ? "Cart is empty" : "Cart:  " + cartLines + " item(s)";

                lblStatus.Text = table.Rows.Count + " medicine(s) found across every approved pharmacy   |   " +
                                 ActiveFilterCount() + " filter(s) active" + Environment.NewLine +
                                 "Rx marks a prescription only medicine - you will be asked to upload a doctor's prescription at checkout. " +
                                 "A discount percentage means an offer is running today and the price shown is already the discounted one.";

                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load the catalogue.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LabelColumns()
        {
            if (dgvMedicines.Columns.Count == 0) return;

            dgvMedicines.Columns["MedicineId"].HeaderText = "ID";
            dgvMedicines.Columns["MedicineId"].FillWeight = 26;
            dgvMedicines.Columns["MedicineName"].HeaderText = "Brand";
            dgvMedicines.Columns["GenericName"].HeaderText = "Generic";
            dgvMedicines.Columns["Strength"].HeaderText = "Strength";
            dgvMedicines.Columns["Strength"].FillWeight = 42;
            dgvMedicines.Columns["Manufacturer"].HeaderText = "Made by";
            dgvMedicines.Columns["CategoryName"].HeaderText = "Category";
            dgvMedicines.Columns["PharmacyName"].HeaderText = "Sold by";
            dgvMedicines.Columns["Area"].HeaderText = "Area";
            dgvMedicines.Columns["Area"].FillWeight = 42;
            dgvMedicines.Columns["UnitPrice"].HeaderText = "List (Tk)";
            dgvMedicines.Columns["UnitPrice"].FillWeight = 40;
            dgvMedicines.Columns["DiscountPercent"].HeaderText = "Off %";
            dgvMedicines.Columns["DiscountPercent"].FillWeight = 32;
            dgvMedicines.Columns["PriceYouPay"].HeaderText = "You pay (Tk)";
            dgvMedicines.Columns["PriceYouPay"].FillWeight = 50;
            dgvMedicines.Columns["Stock"].HeaderText = "Stock";
            dgvMedicines.Columns["Stock"].FillWeight = 32;
            dgvMedicines.Columns["RequiresRx"].HeaderText = "Rx";
            dgvMedicines.Columns["RequiresRx"].FillWeight = 24;
            dgvMedicines.Columns["ExpiryDate"].HeaderText = "Expires";
        }

        /// <summary>Discounted rows are tinted green and prescription only rows amber.</summary>
        private void dgvMedicines_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvMedicines.Columns.Count == 0) return;

            DataGridViewRow row = dgvMedicines.Rows[e.RowIndex];
            object discount = row.Cells["DiscountPercent"].Value;
            object rx = row.Cells["RequiresRx"].Value;

            bool discounted = discount != null && discount != DBNull.Value && Convert.ToDecimal(discount) > 0m;
            bool needsRx = rx != null && rx != DBNull.Value && Convert.ToBoolean(rx);

            if (discounted) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            else if (needsRx) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 232);
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        private void dgvMedicines_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            DataGridViewRow row = dgvMedicines.CurrentRow;
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            bool inStock = hasRow && Convert.ToInt32(row.Cells["Stock"].Value) > 0;

            btnDetails.Enabled = hasRow;
            btnAddToCart.Enabled = hasRow && inStock;
            btnAddToCart.Text = hasRow && !inStock ? "Out of stock" : "Add to cart";
        }

        private int SelectedMedicineId()
        {
            if (dgvMedicines.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvMedicines.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---------------------------------------------------------------------
        //  ACTIONS
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
                txtQuantity.Focus();
                txtQuantity.SelectAll();
                return;
            }

            string message;
            if (_cart.AddOrIncrease(UserSession.UserId, medicineId, quantity, out message))
            {
                string name = dgvMedicines.CurrentRow.Cells["MedicineName"].Value.ToString();
                lblStatus.Text = quantity + " x " + name + " added to your cart. " +
                                 "Adding the same medicine again increases the quantity instead of creating a second line, " +
                                 "which is what the UNIQUE constraint on (CustomerId, MedicineId) guarantees.";
                txtQuantity.Text = "1";
                LoadGrid();
            }
            else
            {
                MessageBox.Show(message, "Cannot add to cart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnDetails_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            using (MedicineDetailsForm details = new MedicineDetailsForm(medicineId))
            {
                details.ShowDialog(this);
            }
            LoadGrid();
        }

        private void dgvMedicines_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            btnDetails_Click(sender, EventArgs.Empty);
        }

        // ---------------------------------------------------------------------
        //  NAVIGATION
        // ---------------------------------------------------------------------

        private void OpenChild(Form child)
        {
            using (child)
            {
                child.ShowDialog(this);
            }
            LoadGrid();
        }

        private void btnBrowse_Click(object sender, EventArgs e) => LoadGrid();
        private void btnCart_Click(object sender, EventArgs e) => OpenChild(new CartForm());
        private void btnOffers_Click(object sender, EventArgs e) => OpenChild(new CustomerOffersForm());
        private void btnOrders_Click(object sender, EventArgs e) => OpenChild(new OrderHistoryForm());
        private void btnMyAccount_Click(object sender, EventArgs e) => OpenChild(new MyProfileForm());
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();

        private void btnClearFilters_Click(object sender, EventArgs e)
        {
            _loading = true;
            txtSearch.Clear();
            cmbCategory.SelectedIndex = 0;
            cmbPriceRange.SelectedIndex = 0;
            cmbArea.SelectedIndex = 0;
            cmbPharmacy.SelectedIndex = 0;
            cmbAvailability.SelectedIndex = 0;
            _loading = false;
            LoadGrid();
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                UserSession.Clear();
                Close();
            }
        }
    }
}
