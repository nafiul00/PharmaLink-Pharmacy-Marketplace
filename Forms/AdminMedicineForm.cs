using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 11: the full CRUD on the owner's own medicines.
    ///
    /// Create and Update open the modal MedicineEditorForm. Delete is a soft
    /// delete - IsActive is set to 0 - which keeps the foreign keys from
    /// OrderItems intact so every old invoice still resolves, while the item
    /// disappears from the customer catalogue.
    /// </summary>
    public partial class AdminMedicineForm : Form
    {
        private readonly MedicineService _medicines = new MedicineService();
        private bool _loading = true;

        public AdminMedicineForm()
        {
            InitializeComponent();
        }

        private void AdminMedicineForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            _loading = false;
            LoadGrid();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Medicines");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StyleSuccess(btnAdd);
            UiTheme.StylePrimary(btnEdit);
            UiTheme.StyleDanger(btnDelist);
            UiTheme.StyleAccent(btnRelist);
            UiTheme.StyleAccent(btnCreateOffer);
            UiTheme.StyleGrid(dgvMedicines);
            dgvMedicines.CellFormatting += dgvMedicines_CellFormatting;

            lblIsolationHint.Font = UiTheme.FontMono;
            lblIsolationHint.ForeColor = UiTheme.TextMuted;
            lblIsolationHint.Text =
                "Data isolation: every statement on this form carries  WHERE PharmacyId = " + UserSession.PharmacyId +
                "  taken from the session, so another pharmacy's rows can never appear here" + Environment.NewLine +
                "and an update or delete aimed at a row that is not yours simply changes nothing.";

            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                DataTable table = _medicines.GetForPharmacy(
                    UserSession.PharmacyId, txtSearch.Text.Trim(), chkShowDelisted.Checked);

                dgvMedicines.DataSource = table;

                if (dgvMedicines.Columns.Count > 0)
                {
                    dgvMedicines.Columns["MedicineId"].HeaderText = "ID";
                    dgvMedicines.Columns["MedicineId"].FillWeight = 28;
                    dgvMedicines.Columns["MedicineName"].HeaderText = "Brand";
                    dgvMedicines.Columns["GenericName"].HeaderText = "Generic";
                    dgvMedicines.Columns["CategoryName"].HeaderText = "Category";
                    dgvMedicines.Columns["Manufacturer"].HeaderText = "Manufacturer";
                    dgvMedicines.Columns["Strength"].HeaderText = "Strength";
                    dgvMedicines.Columns["Strength"].FillWeight = 45;
                    dgvMedicines.Columns["UnitPrice"].HeaderText = "Price (Tk)";
                    dgvMedicines.Columns["UnitPrice"].FillWeight = 45;
                    dgvMedicines.Columns["Stock"].HeaderText = "Stock";
                    dgvMedicines.Columns["Stock"].FillWeight = 35;
                    dgvMedicines.Columns["MinStock"].HeaderText = "Min";
                    dgvMedicines.Columns["MinStock"].FillWeight = 30;
                    dgvMedicines.Columns["RequiresRx"].HeaderText = "Rx";
                    dgvMedicines.Columns["RequiresRx"].FillWeight = 26;
                    dgvMedicines.Columns["ExpiryDate"].HeaderText = "Expires";
                    dgvMedicines.Columns["IsActive"].HeaderText = "On sale";
                    dgvMedicines.Columns["IsActive"].FillWeight = 40;
                    dgvMedicines.Columns["StockStatus"].HeaderText = "Stock state";
                }

                lblStatus.Text = table.Rows.Count + " medicine(s) listed for " + UserSession.PharmacyName + ".";
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvMedicines_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvMedicines.Columns.Count == 0) return;

            DataGridViewRow row = dgvMedicines.Rows[e.RowIndex];
            object active = row.Cells["IsActive"].Value;
            object stockState = row.Cells["StockStatus"].Value;

            if (active != DBNull.Value && active != null && !Convert.ToBoolean(active))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(238, 238, 238);
                row.DefaultCellStyle.ForeColor = UiTheme.TextMuted;
            }
            else if (stockState != null && stockState.ToString() == "Low Stock")
            {
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
        }

        private void dgvMedicines_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            DataGridViewRow row = dgvMedicines.CurrentRow;
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            bool active = hasRow && row.Cells["IsActive"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsActive"].Value);

            btnEdit.Enabled = hasRow;
            btnDelist.Enabled = hasRow && active;
            btnRelist.Enabled = hasRow && !active;
            btnCreateOffer.Enabled = hasRow && active;
        }

        private int SelectedId()
        {
            if (dgvMedicines.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvMedicines.CurrentRow.Cells["MedicineId"].Value);
        }

        private string SelectedName()
        {
            if (dgvMedicines.CurrentRow == null) return "";
            return dgvMedicines.CurrentRow.Cells["MedicineName"].Value.ToString();
        }

        // ---------------------------------------------------------------------

        private void btnAdd_Click(object sender, EventArgs e)
        {
            using (MedicineEditorForm editor = new MedicineEditorForm(0, false))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                    lblStatus.Text = "Medicine added and immediately visible to customers.";
            }
            LoadGrid();
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            using (MedicineEditorForm editor = new MedicineEditorForm(id, false))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                    lblStatus.Text = "Medicine updated.";
            }
            LoadGrid();
        }

        private void dgvMedicines_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            btnEdit_Click(sender, EventArgs.Empty);
        }

        private void btnDelist_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            DialogResult answer = MessageBox.Show(
                "Take " + SelectedName() + " off sale?\r\n\r\n" +
                "This is a soft delete: IsActive is set to 0. The row stays in the database so the " +
                "OrderItems rows on past invoices still resolve, but customers stop seeing the medicine.",
                "Delist medicine", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            _medicines.Delist(id, UserSession.PharmacyId);
            lblStatus.Text = SelectedName() + " delisted (IsActive = 0). No row was deleted.";
            LoadGrid();
        }

        private void btnRelist_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            _medicines.Relist(id, UserSession.PharmacyId);
            lblStatus.Text = SelectedName() + " is on sale again.";
            LoadGrid();
        }

        private void btnCreateOffer_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            using (DiscountOffersForm offers = new DiscountOffersForm(id))
            {
                offers.ShowDialog(this);
            }
            LoadGrid();
        }

        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
