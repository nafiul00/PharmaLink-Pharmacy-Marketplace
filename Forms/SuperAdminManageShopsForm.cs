using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirements 2, 3 and 9.
    ///
    /// Approve flips two rows, suspend flips three, and the commission rate is
    /// a column on Pharmacies rather than a constant in the code, so it can be
    /// changed for one shop without touching anyone else.
    /// </summary>
    public partial class SuperAdminManageShopsForm : Form
    {
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly int _preselectPharmacyId;
        private bool _loading = true;

        public SuperAdminManageShopsForm(int preselectPharmacyId)
        {
            InitializeComponent();
            _preselectPharmacyId = preselectPharmacyId;
        }

        private void SuperAdminManageShopsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadFilters();
            _loading = false;
            LoadGrid();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Manage Pharmacies");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);
            UiTheme.StyleSecondary(btnBack);

            UiTheme.StyleGrid(dgvPharmacies);
            dgvPharmacies.CellFormatting += dgvPharmacies_CellFormatting;

            UiTheme.StyleSuccess(btnApprove);
            UiTheme.StyleDanger(btnSuspend);
            UiTheme.StyleAccent(btnReinstate);
            UiTheme.StyleDanger(btnDelete);
            UiTheme.StylePrimary(btnSetCommission);
            UiTheme.StyleSecondary(btnSearch);
            UiTheme.StyleSecondary(btnClear);

            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
            lblCommissionError.Font = UiTheme.FontSmall;
            lblCommissionError.ForeColor = UiTheme.Danger;
        }

        private void LoadFilters()
        {
            cmbStatus.Items.Clear();
            cmbStatus.Items.AddRange(new object[] { "All statuses", "Pending", "Approved", "Suspended" });
            cmbStatus.SelectedIndex = 0;

            cmbArea.Items.Clear();
            cmbArea.Items.Add("All areas");
            foreach (string area in _pharmacies.GetAreas(false)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;
        }

        // ---------------------------------------------------------------------

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();

                DataTable table = _pharmacies.Search(txtSearch.Text.Trim(), status, area);
                dgvPharmacies.DataSource = table;
                LabelColumns();

                lblStatus.Text = table.Rows.Count + " pharmac" + (table.Rows.Count == 1 ? "y" : "ies") +
                                 " shown" + DescribeFilters(status, area);

                if (_preselectPharmacyId > 0) SelectPharmacy(_preselectPharmacyId);
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string DescribeFilters(string status, string area)
        {
            int active = 0;
            if (!string.IsNullOrEmpty(txtSearch.Text.Trim())) active++;
            if (!string.IsNullOrEmpty(status)) active++;
            if (!string.IsNullOrEmpty(area)) active++;
            return active == 0 ? "  (no filters applied)" : "  (" + active + " filter(s) applied)";
        }

        private void LabelColumns()
        {
            if (dgvPharmacies.Columns.Count == 0) return;
            dgvPharmacies.Columns["PharmacyId"].HeaderText = "ID";
            dgvPharmacies.Columns["PharmacyId"].FillWeight = 30;
            dgvPharmacies.Columns["PharmacyName"].HeaderText = "Pharmacy";
            dgvPharmacies.Columns["OwnerName"].HeaderText = "Owner";
            dgvPharmacies.Columns["OwnerEmail"].HeaderText = "Owner email";
            dgvPharmacies.Columns["LicenseNo"].HeaderText = "DGDA licence";
            dgvPharmacies.Columns["Area"].HeaderText = "Area";
            dgvPharmacies.Columns["ContactPhone"].HeaderText = "Contact";
            dgvPharmacies.Columns["CommissionRate"].HeaderText = "Comm %";
            dgvPharmacies.Columns["CommissionRate"].FillWeight = 45;
            dgvPharmacies.Columns["Status"].HeaderText = "Status";
            dgvPharmacies.Columns["Status"].FillWeight = 55;
            dgvPharmacies.Columns["Medicines"].HeaderText = "Items";
            dgvPharmacies.Columns["Medicines"].FillWeight = 40;
            dgvPharmacies.Columns["AverageRating"].HeaderText = "Rating";
            dgvPharmacies.Columns["AverageRating"].FillWeight = 45;
            dgvPharmacies.Columns["RegisteredAt"].HeaderText = "Registered";
        }

        /// <summary>Status colour coding, so the queue reads at a glance.</summary>
        private void dgvPharmacies_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvPharmacies.Columns.Count == 0) return;

            DataGridViewRow row = dgvPharmacies.Rows[e.RowIndex];
            object value = row.Cells["Status"].Value;
            if (value == null) return;

            switch (value.ToString())
            {
                case "Pending":
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224);
                    break;
                case "Suspended":
                    row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
                    break;
                default:
                    row.DefaultCellStyle.BackColor = Color.White;
                    break;
            }
        }

        private void SelectPharmacy(int pharmacyId)
        {
            foreach (DataGridViewRow row in dgvPharmacies.Rows)
            {
                if (Convert.ToInt32(row.Cells["PharmacyId"].Value) == pharmacyId)
                {
                    row.Selected = true;
                    dgvPharmacies.CurrentCell = row.Cells[1];
                    break;
                }
            }
        }

        // ---------------------------------------------------------------------
        //  SELECTION AND BUTTON STATE
        //  With no row selected every action button stays disabled, which is the
        //  rule the navigation diagram describes.
        // ---------------------------------------------------------------------

        private void dgvPharmacies_SelectionChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            DataGridViewRow row = dgvPharmacies.CurrentRow;
            bool hasRow = row != null && row.Cells["Status"].Value != null;

            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";

            btnApprove.Enabled = hasRow && status == "Pending";
            btnSuspend.Enabled = hasRow && status == "Approved";
            btnReinstate.Enabled = hasRow && status == "Suspended";
            btnDelete.Enabled = hasRow;
            btnSetCommission.Enabled = hasRow;
            txtCommission.Enabled = hasRow;

            txtCommission.Text = hasRow ? row.Cells["CommissionRate"].Value.ToString() : "";
            UiTheme.ClearError(lblCommissionError, txtCommission);
        }

        private int SelectedId()
        {
            if (dgvPharmacies.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvPharmacies.CurrentRow.Cells["PharmacyId"].Value);
        }

        private string SelectedName()
        {
            if (dgvPharmacies.CurrentRow == null) return "";
            return dgvPharmacies.CurrentRow.Cells["PharmacyName"].Value.ToString();
        }

        // ---------------------------------------------------------------------
        //  ACTIONS
        // ---------------------------------------------------------------------

        private void btnApprove_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            string licence = dgvPharmacies.CurrentRow.Cells["LicenseNo"].Value.ToString();

            DialogResult answer = MessageBox.Show(
                "Approve " + SelectedName() + "?\r\n\r\nDGDA licence: " + licence + "\r\n\r\n" +
                "Pharmacies.Status becomes 'Approved' and Users.Status becomes 'Active', " +
                "after which the owner can log in and the shop's medicines become visible to customers.",
                "Approve pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            _pharmacies.Approve(id);
            lblStatus.Text = SelectedName() + " approved.";
            LoadGrid();
        }

        private void btnSuspend_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            string rating = dgvPharmacies.CurrentRow.Cells["AverageRating"].Value.ToString();

            DialogResult answer = MessageBox.Show(
                "Suspend " + SelectedName() + "?\r\n\r\nAverage customer rating: " + rating + "\r\n\r\n" +
                "Three updates run inside one transaction: the pharmacy becomes Suspended, the owner's " +
                "account becomes Suspended, and every medicine this pharmacy lists has IsActive set to 0.\r\n\r\n" +
                "Nothing is deleted, so the sales history and the invoices customers already hold stay valid.",
                "Suspend pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            _pharmacies.Suspend(id);
            lblStatus.Text = SelectedName() + " suspended. Its medicines are no longer visible to customers.";
            LoadGrid();
        }

        private void btnReinstate_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            DialogResult answer = MessageBox.Show(
                "Put " + SelectedName() + " back on the platform?",
                "Reinstate pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            _pharmacies.Reinstate(id);
            LoadGrid();
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            DialogResult answer = MessageBox.Show(
                "Permanently delete " + SelectedName() + " and its owner account?\r\n\r\n" +
                "This is only possible for a shop that has never taken an order. " +
                "A shop with order history must be suspended instead.",
                "Delete pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            string message;
            if (_pharmacies.Delete(id, out message))
            {
                lblStatus.Text = message;
                LoadGrid();
            }
            else
            {
                MessageBox.Show(message, "Cannot delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ---------------------------------------------------------------------
        //  COMMISSION RATE  (requirement 9)
        // ---------------------------------------------------------------------

        private void txtCommission_TextChanged(object sender, EventArgs e)
        {
            decimal rate;
            bool valid = Validator.IsCommissionRate(txtCommission.Text, out rate);

            if (string.IsNullOrWhiteSpace(txtCommission.Text))
            {
                UiTheme.ClearError(lblCommissionError, txtCommission);
                btnSetCommission.Enabled = false;
                return;
            }

            if (valid) UiTheme.ClearError(lblCommissionError, txtCommission);
            else UiTheme.ShowError(lblCommissionError, txtCommission,
                    "The rate must be a number between 0 and 30 (CK_Pharmacies_Comm).");

            btnSetCommission.Enabled = valid && dgvPharmacies.CurrentRow != null;
        }

        private void btnSetCommission_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            decimal rate;
            if (!Validator.IsCommissionRate(txtCommission.Text, out rate))
            {
                UiTheme.ShowError(lblCommissionError, txtCommission,
                    "The rate must be a number between 0 and 30 (CK_Pharmacies_Comm).");
                return;
            }

            if (_pharmacies.SetCommissionRate(id, rate))
            {
                lblStatus.Text = SelectedName() + " will be charged " + rate.ToString("N2") +
                                 "% commission on orders placed from now on. Past orders keep the rate they were sold at.";
                LoadGrid();
            }
        }

        // ---------------------------------------------------------------------

        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();

        private void btnClear_Click(object sender, EventArgs e)
        {
            _loading = true;
            txtSearch.Clear();
            cmbStatus.SelectedIndex = 0;
            cmbArea.SelectedIndex = 0;
            _loading = false;
            LoadGrid();
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
