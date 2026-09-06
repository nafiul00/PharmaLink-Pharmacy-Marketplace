using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 4. Every Admin and Customer in one DataGridView, with the
    /// pharmacy name filled in beside an owner's row through a LEFT JOIN on
    /// Pharmacies. Both filters are optional, and an empty box means no filter
    /// rather than no results.
    /// </summary>
    public partial class SuperAdminManageUsersForm : Form
    {
        private readonly AuthService _auth = new AuthService();
        private bool _loading = true;

        public SuperAdminManageUsersForm()
        {
            InitializeComponent();
        }

        private void SuperAdminManageUsersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbStatus.Items.AddRange(new object[] { "All statuses", "Pending", "Active", "Suspended" });
            cmbStatus.SelectedIndex = 0;

            cmbUserType.Items.AddRange(new object[] { "All users", "Admin", "Customer" });
            cmbUserType.SelectedIndex = 0;

            _loading = false;
            LoadGrid();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Manage Users");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnSearch);
            UiTheme.StyleSecondary(btnClear);
            UiTheme.StyleDanger(btnSuspend);
            UiTheme.StyleSuccess(btnActivate);
            UiTheme.StyleGrid(dgvUsers);
            dgvUsers.CellFormatting += dgvUsers_CellFormatting;

            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();
                string type = cmbUserType.SelectedIndex <= 0 ? "" : cmbUserType.SelectedItem.ToString();

                DataTable table = _auth.SearchUsers(txtSearch.Text.Trim(), status, type);
                dgvUsers.DataSource = table;

                if (dgvUsers.Columns.Count > 0)
                {
                    dgvUsers.Columns["UserId"].HeaderText = "ID";
                    dgvUsers.Columns["UserId"].FillWeight = 30;
                    dgvUsers.Columns["FullName"].HeaderText = "Name";
                    dgvUsers.Columns["Email"].HeaderText = "Email";
                    dgvUsers.Columns["Phone"].HeaderText = "Mobile";
                    dgvUsers.Columns["UserType"].HeaderText = "Role";
                    dgvUsers.Columns["UserType"].FillWeight = 45;
                    dgvUsers.Columns["Status"].HeaderText = "Status";
                    dgvUsers.Columns["Status"].FillWeight = 45;
                    dgvUsers.Columns["PharmacyName"].HeaderText = "Pharmacy (owners only)";
                    dgvUsers.Columns["CreatedAt"].HeaderText = "Member since";
                }

                lblStatus.Text = table.Rows.Count + " account(s) shown.";
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvUsers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvUsers.Columns.Count == 0) return;

            DataGridViewRow row = dgvUsers.Rows[e.RowIndex];
            object value = row.Cells["Status"].Value;
            if (value == null) return;

            switch (value.ToString())
            {
                case "Pending": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;
                case "Suspended": row.DefaultCellStyle.BackColor = UiTheme.LowStockBack; break;
                default: row.DefaultCellStyle.BackColor = Color.White; break;
            }
        }

        private void dgvUsers_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            DataGridViewRow row = dgvUsers.CurrentRow;
            bool hasRow = row != null && row.Cells["Status"].Value != null;
            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";

            btnSuspend.Enabled = hasRow && status != "Suspended";
            btnActivate.Enabled = hasRow && status != "Active";
        }

        private void ChangeStatus(string newStatus)
        {
            DataGridViewRow row = dgvUsers.CurrentRow;
            if (row == null) return;

            int userId = Convert.ToInt32(row.Cells["UserId"].Value);
            string name = row.Cells["FullName"].Value.ToString();

            DialogResult answer = MessageBox.Show(
                "Set " + name + "'s account to " + newStatus + "?",
                "Change account status", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            if (_auth.SetUserStatus(userId, newStatus))
            {
                lblStatus.Text = name + " is now " + newStatus + ".";
                LoadGrid();
            }
        }

        private void btnSuspend_Click(object sender, EventArgs e) => ChangeStatus("Suspended");
        private void btnActivate_Click(object sender, EventArgs e) => ChangeStatus("Active");
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();

        private void btnClear_Click(object sender, EventArgs e)
        {
            _loading = true;
            txtSearch.Clear();
            cmbStatus.SelectedIndex = 0;
            cmbUserType.SelectedIndex = 0;
            _loading = false;
            LoadGrid();
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
