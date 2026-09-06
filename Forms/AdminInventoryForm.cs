using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 12. Four summary tiles, the low stock alert panel and the
    /// full inventory grid.
    ///
    /// The alert compares two columns of the same row - Stock against MinStock -
    /// rather than using a fixed threshold, because ten boxes of a glucometer is
    /// plenty while ten strips of Napa is nothing. The shortfall column tells
    /// the owner how many units to order.
    /// </summary>
    public partial class AdminInventoryForm : Form
    {
        private readonly MedicineService _medicines = new MedicineService();

        private Label _tileItems;
        private Label _tileLowStock;
        private Label _tileUnitsInStock;
        private Label _tileStockValue;

        public AdminInventoryForm()
        {
            InitializeComponent();
        }

        private void AdminInventoryForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            BuildTiles();
            LoadEverything();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Stock and Inventory");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            lblAlertTitle.Font = UiTheme.FontHeading;
            lblAlertTitle.ForeColor = UiTheme.Danger;
            lblInventoryTitle.Font = UiTheme.FontHeading;
            lblInventoryTitle.ForeColor = UiTheme.TextDark;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
            lblRestockError.Font = UiTheme.FontSmall;
            lblRestockError.ForeColor = UiTheme.Danger;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSuccess(btnRestock);
            UiTheme.StyleSecondary(btnOpenEditor);
            UiTheme.StyleGrid(dgvLowStock);
            UiTheme.StyleGrid(dgvInventory);
            dgvLowStock.CellFormatting += dgvLowStock_CellFormatting;
            dgvInventory.CellFormatting += dgvInventory_CellFormatting;
        }

        private void BuildTiles()
        {
            Panel t1 = UiTheme.BuildTile("MEDICINES ON SALE", UiTheme.Primary, out _tileItems);
            Panel t2 = UiTheme.BuildTile("BELOW MINIMUM STOCK", UiTheme.Danger, out _tileLowStock);
            Panel t3 = UiTheme.BuildTile("UNITS ON THE SHELF", UiTheme.Accent, out _tileUnitsInStock);
            Panel t4 = UiTheme.BuildTile("VALUE OF STOCK HELD", UiTheme.Warning, out _tileStockValue);

            int x = 20;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                tile.Location = new Point(x, 84);
                tile.Size = new Size(278, 84);
                Controls.Add(tile);
                tile.BringToFront();
                x += 294;
            }
        }

        private void LoadEverything()
        {
            try
            {
                DataTable lowStock = _medicines.GetLowStock(UserSession.PharmacyId);
                dgvLowStock.DataSource = lowStock;

                if (dgvLowStock.Columns.Count > 0)
                {
                    dgvLowStock.Columns["MedicineId"].HeaderText = "ID";
                    dgvLowStock.Columns["MedicineId"].FillWeight = 30;
                    dgvLowStock.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvLowStock.Columns["Strength"].HeaderText = "Strength";
                    dgvLowStock.Columns["CategoryName"].HeaderText = "Category";
                    dgvLowStock.Columns["Stock"].HeaderText = "In stock";
                    dgvLowStock.Columns["MinStock"].HeaderText = "Minimum level";
                    dgvLowStock.Columns["ShortfallUnits"].HeaderText = "Shortfall (units to order)";
                }

                DataTable inventory = _medicines.GetInventory(UserSession.PharmacyId);
                dgvInventory.DataSource = inventory;

                if (dgvInventory.Columns.Count > 0)
                {
                    dgvInventory.Columns["MedicineId"].HeaderText = "ID";
                    dgvInventory.Columns["MedicineId"].FillWeight = 28;
                    dgvInventory.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvInventory.Columns["Strength"].HeaderText = "Strength";
                    dgvInventory.Columns["Strength"].FillWeight = 45;
                    dgvInventory.Columns["CategoryName"].HeaderText = "Category";
                    dgvInventory.Columns["UnitsRemaining"].HeaderText = "Units left";
                    dgvInventory.Columns["UnitsSold"].HeaderText = "Units sold";
                    dgvInventory.Columns["MinStock"].HeaderText = "Min level";
                    dgvInventory.Columns["UnitPrice"].HeaderText = "Price (Tk)";
                    dgvInventory.Columns["StockValue"].HeaderText = "Stock value (Tk)";
                    dgvInventory.Columns["StockStatus"].HeaderText = "State";
                    dgvInventory.Columns["ExpiryDate"].HeaderText = "Expires";
                }

                int units = 0;
                decimal value = 0m;
                foreach (DataRow row in inventory.Rows)
                {
                    units += DbHelperInt(row, "UnitsRemaining");
                    value += DbHelperDecimal(row, "StockValue");
                }

                _tileItems.Text = inventory.Rows.Count.ToString();
                _tileLowStock.Text = lowStock.Rows.Count.ToString();
                _tileUnitsInStock.Text = units.ToString("N0");
                _tileStockValue.Text = UiTheme.Money(value);

                lblAlertTitle.Text = lowStock.Rows.Count == 0
                    ? "Low stock alert  -  nothing is below its minimum level right now"
                    : "Low stock alert  (" + lowStock.Rows.Count + " medicine(s) need reordering)";

                lblStatus.Text = "Double click any inventory row to open the medicine editor. " +
                                 "Every query on this form carries WHERE PharmacyId = " + UserSession.PharmacyId + ".";

                UpdateRestockButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static int DbHelperInt(DataRow row, string column)
        {
            return row[column] == DBNull.Value ? 0 : Convert.ToInt32(row[column]);
        }

        private static decimal DbHelperDecimal(DataRow row, string column)
        {
            return row[column] == DBNull.Value ? 0m : Convert.ToDecimal(row[column]);
        }

        /// <summary>Rows in the alert panel are painted red through the CellFormatting event.</summary>
        private void dgvLowStock_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            dgvLowStock.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        private void dgvInventory_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvInventory.Columns.Count == 0) return;

            DataGridViewRow row = dgvInventory.Rows[e.RowIndex];
            object state = row.Cells["StockStatus"].Value;
            if (state == null) return;

            row.DefaultCellStyle.BackColor = state.ToString() == "Low Stock"
                ? UiTheme.LowStockBack
                : Color.White;
        }

        // ---------------------------------------------------------------------
        //  RESTOCK
        // ---------------------------------------------------------------------

        private void dgvLowStock_SelectionChanged(object sender, EventArgs e) => UpdateRestockButton();

        private void txtRestockUnits_TextChanged(object sender, EventArgs e)
        {
            int units;
            if (string.IsNullOrWhiteSpace(txtRestockUnits.Text))
            {
                UiTheme.ClearError(lblRestockError, txtRestockUnits);
            }
            else if (!Validator.IsPositiveInt(txtRestockUnits.Text, out units))
            {
                UiTheme.ShowError(lblRestockError, txtRestockUnits,
                    "Enter a whole number of units greater than zero.");
            }
            else
            {
                UiTheme.ClearError(lblRestockError, txtRestockUnits);
            }

            UpdateRestockButton();
        }

        private void UpdateRestockButton()
        {
            int units;
            bool hasRow = dgvLowStock.CurrentRow != null && dgvLowStock.CurrentRow.Cells["MedicineId"].Value != null;
            bool unitsOk = Validator.IsPositiveInt(txtRestockUnits.Text, out units);

            btnRestock.Enabled = hasRow && unitsOk;
            btnOpenEditor.Enabled = hasRow;

            // Pre-fill the shortfall so the obvious amount is one click away.
            if (hasRow && string.IsNullOrWhiteSpace(txtRestockUnits.Text))
            {
                object shortfall = dgvLowStock.CurrentRow.Cells["ShortfallUnits"].Value;
                if (shortfall != null && shortfall != DBNull.Value)
                    txtRestockUnits.Text = shortfall.ToString();
            }
        }

        private void btnRestock_Click(object sender, EventArgs e)
        {
            if (dgvLowStock.CurrentRow == null) return;

            int units;
            if (!Validator.IsPositiveInt(txtRestockUnits.Text, out units)) return;

            int medicineId = Convert.ToInt32(dgvLowStock.CurrentRow.Cells["MedicineId"].Value);
            string name = dgvLowStock.CurrentRow.Cells["MedicineName"].Value.ToString();

            if (_medicines.AddStock(medicineId, UserSession.PharmacyId, units))
            {
                lblStatus.Text = units + " unit(s) added to " + name + ".";
                txtRestockUnits.Clear();
                LoadEverything();
            }
        }

        private void btnOpenEditor_Click(object sender, EventArgs e)
        {
            if (dgvLowStock.CurrentRow == null) return;
            OpenEditor(Convert.ToInt32(dgvLowStock.CurrentRow.Cells["MedicineId"].Value));
        }

        private void dgvInventory_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            OpenEditor(Convert.ToInt32(dgvInventory.Rows[e.RowIndex].Cells["MedicineId"].Value));
        }

        private void OpenEditor(int medicineId)
        {
            using (MedicineEditorForm editor = new MedicineEditorForm(medicineId, true))
            {
                editor.ShowDialog(this);
            }
            LoadEverything();
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
