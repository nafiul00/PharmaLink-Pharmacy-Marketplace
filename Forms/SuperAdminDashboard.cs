using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// The platform operator's hub (requirements 1 to 9).
    ///
    /// Four summary tiles, the queue of pharmacies waiting for approval, and the
    /// low rated pharmacy panel that comes straight from the
    /// HAVING AVG(Rating) &lt; 2.5 query. The left menu is the entry point to
    /// every other Super Admin form, and every one of them has a Back button.
    /// </summary>
    public partial class SuperAdminDashboard : Form
    {
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly ReportService _reports = new ReportService();

        private Label _tilePharmacies;
        private Label _tileCustomers;
        private Label _tileOrders;
        private Label _tileCommission;

        public SuperAdminDashboard()
        {
            InitializeComponent();
        }

        private void SuperAdminDashboard_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            BuildTiles();
            LoadEverything();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Super Admin");

            panelSide.BackColor = UiTheme.Sidebar;
            lblBrand.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
            lblBrand.ForeColor = Color.White;
            lblRole.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            lblRole.ForeColor = Color.FromArgb(140, 205, 185);
            lblUserName.Font = UiTheme.FontSmall;
            lblUserName.ForeColor = Color.FromArgb(190, 205, 216);
            lblUserName.Text = UserSession.FullName;

            foreach (Button button in new[] { btnManagePharmacies, btnManageUsers, btnCategories,
                                              btnSalesReport, btnLowRated, btnModerateReviews })
            {
                UiTheme.StyleSidebarButton(button);
            }

            UiTheme.StyleSidebarButton(btnLogout);
            btnLogout.BackColor = UiTheme.Danger;
            btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 60, 60);

            panelHeader.BackColor = UiTheme.Primary;
            lblHeaderTitle.Font = UiTheme.FontTitle;
            lblHeaderTitle.ForeColor = Color.White;
            lblHeaderSub.Font = UiTheme.FontSmall;
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);
            UiTheme.StyleSecondary(btnRefresh);

            lblPendingTitle.Font = UiTheme.FontHeading;
            lblPendingTitle.ForeColor = UiTheme.TextDark;
            lblPendingHint.Font = UiTheme.FontSmall;
            lblPendingHint.ForeColor = UiTheme.TextMuted;

            lblLowRatedTitle.Font = UiTheme.FontHeading;
            lblLowRatedTitle.ForeColor = UiTheme.TextDark;
            lblLowRatedHint.Font = UiTheme.FontSmall;
            lblLowRatedHint.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleGrid(dgvPending);
            UiTheme.StyleGrid(dgvLowRated);
            dgvLowRated.CellFormatting += dgvLowRated_CellFormatting;

            UiTheme.StyleSuccess(btnApprove);
            UiTheme.StyleDanger(btnReject);
            UiTheme.StyleSecondary(btnOpenPharmacies);
        }

        /// <summary>The four headline figures, built in code so the layout stays in one place.</summary>
        private void BuildTiles()
        {
            Panel t1 = UiTheme.BuildTile("APPROVED PHARMACIES", UiTheme.Primary, out _tilePharmacies);
            Panel t2 = UiTheme.BuildTile("REGISTERED CUSTOMERS", UiTheme.Accent, out _tileCustomers);
            Panel t3 = UiTheme.BuildTile("ORDERS PLACED", UiTheme.Success, out _tileOrders);
            Panel t4 = UiTheme.BuildTile("COMMISSION EARNED", UiTheme.Warning, out _tileCommission);

            int x = 250;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                tile.Location = new Point(x, 88);
                tile.Size = new Size(236, 84);
                Controls.Add(tile);
                tile.BringToFront();
                x += 250;
            }
        }

        // ---------------------------------------------------------------------
        //  DATA
        // ---------------------------------------------------------------------

        private void LoadEverything()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                int pharmacies, pending, customers, orders;
                decimal revenue, commission;
                _reports.GetPlatformTotals(out pharmacies, out pending, out customers,
                                           out orders, out revenue, out commission);

                _tilePharmacies.Text = pharmacies.ToString();
                _tileCustomers.Text = customers.ToString();
                _tileOrders.Text = orders.ToString();
                _tileCommission.Text = UiTheme.Money(commission);

                lblHeaderSub.Text = "Gross platform revenue " + UiTheme.Money(revenue) +
                                    "   |   " + pending + " pharmacy registration(s) waiting for approval";

                dgvPending.DataSource = _pharmacies.GetPending();
                LabelPendingColumns();

                dgvLowRated.DataSource = _reports.GetLowRatedPharmacies(2.5m, 2);
                LabelLowRatedColumns();

                bool hasPending = dgvPending.Rows.Count > 0;
                btnApprove.Enabled = hasPending;
                btnReject.Enabled = hasPending;
                lblPendingTitle.Text = hasPending
                    ? "Pharmacies waiting for approval  (" + dgvPending.Rows.Count + ")"
                    : "Pharmacies waiting for approval  -  none right now";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load the dashboard.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void LabelPendingColumns()
        {
            if (dgvPending.Columns.Count == 0) return;
            dgvPending.Columns["PharmacyId"].HeaderText = "ID";
            dgvPending.Columns["PharmacyId"].FillWeight = 30;
            dgvPending.Columns["PharmacyName"].HeaderText = "Pharmacy";
            dgvPending.Columns["OwnerName"].HeaderText = "Owner";
            dgvPending.Columns["LicenseNo"].HeaderText = "DGDA licence";
            dgvPending.Columns["Area"].HeaderText = "Area";
            dgvPending.Columns["ContactPhone"].HeaderText = "Contact";
            dgvPending.Columns["RegisteredAt"].HeaderText = "Applied on";
        }

        private void LabelLowRatedColumns()
        {
            if (dgvLowRated.Columns.Count == 0) return;
            dgvLowRated.Columns["PharmacyId"].HeaderText = "ID";
            dgvLowRated.Columns["PharmacyId"].FillWeight = 30;
            dgvLowRated.Columns["PharmacyName"].HeaderText = "Pharmacy";
            dgvLowRated.Columns["Area"].HeaderText = "Area";
            dgvLowRated.Columns["OwnerName"].HeaderText = "Owner";
            dgvLowRated.Columns["OwnerPhone"].HeaderText = "Owner phone";
            dgvLowRated.Columns["TotalReviews"].HeaderText = "Reviews";
            dgvLowRated.Columns["AverageRating"].HeaderText = "Avg rating";
            dgvLowRated.Columns["Status"].HeaderText = "Status";
        }

        /// <summary>Poor performers are tinted red through the CellFormatting event.</summary>
        private void dgvLowRated_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            dgvLowRated.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        // ---------------------------------------------------------------------
        //  ACTIONS ON THE PENDING QUEUE
        // ---------------------------------------------------------------------

        private int SelectedPendingPharmacyId()
        {
            if (dgvPending.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvPending.CurrentRow.Cells["PharmacyId"].Value);
        }

        private void btnApprove_Click(object sender, EventArgs e)
        {
            int pharmacyId = SelectedPendingPharmacyId();
            if (pharmacyId == 0)
            {
                MessageBox.Show("Select a pharmacy first.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = dgvPending.CurrentRow.Cells["PharmacyName"].Value.ToString();
            string licence = dgvPending.CurrentRow.Cells["LicenseNo"].Value.ToString();

            DialogResult answer = MessageBox.Show(
                "Approve " + name + "?\r\n\r\nDGDA licence: " + licence + "\r\n\r\n" +
                "The pharmacy becomes Approved, the owner's account becomes Active and can log in, " +
                "and the shop's medicines become visible to customers.",
                "Approve pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            if (_pharmacies.Approve(pharmacyId))
            {
                MessageBox.Show(name + " has been approved.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadEverything();
            }
        }

        private void btnReject_Click(object sender, EventArgs e)
        {
            int pharmacyId = SelectedPendingPharmacyId();
            if (pharmacyId == 0)
            {
                MessageBox.Show("Select a pharmacy first.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = dgvPending.CurrentRow.Cells["PharmacyName"].Value.ToString();

            DialogResult answer = MessageBox.Show(
                "Reject " + name + "?\r\n\r\nThe registration is held at Suspended rather than deleted, " +
                "so the licence number stays taken and the decision can be reversed.",
                "Reject registration", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            if (_pharmacies.Reject(pharmacyId))
            {
                LoadEverything();
            }
        }

        private void dgvLowRated_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            int pharmacyId = Convert.ToInt32(dgvLowRated.Rows[e.RowIndex].Cells["PharmacyId"].Value);
            OpenChild(new SuperAdminManageShopsForm(pharmacyId));
        }

        // ---------------------------------------------------------------------
        //  NAVIGATION
        // ---------------------------------------------------------------------

        /// <summary>
        /// Every child form is opened as a modal dialog and the dashboard
        /// refreshes when it closes, which is how the navigation diagram's
        /// "labelled Back arrow" is implemented in practice.
        /// </summary>
        private void OpenChild(Form child)
        {
            using (child)
            {
                child.ShowDialog(this);
            }
            LoadEverything();
        }

        private void btnManagePharmacies_Click(object sender, EventArgs e) => OpenChild(new SuperAdminManageShopsForm(0));
        private void btnManageUsers_Click(object sender, EventArgs e) => OpenChild(new SuperAdminManageUsersForm());
        private void btnCategories_Click(object sender, EventArgs e) => OpenChild(new ManageCategoriesForm());
        private void btnSalesReport_Click(object sender, EventArgs e) => OpenChild(new SuperAdminSalesReportForm());
        private void btnLowRated_Click(object sender, EventArgs e) => OpenChild(new SuperAdminLowRatedShopsForm());
        private void btnModerateReviews_Click(object sender, EventArgs e) => OpenChild(new ModerateReviewsForm());
        private void btnRefresh_Click(object sender, EventArgs e) => LoadEverything();

        private void btnLogout_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                UserSession.Clear();
                Close();      // LoginForm is watching FormClosed and shows itself again
            }
        }
    }
}
