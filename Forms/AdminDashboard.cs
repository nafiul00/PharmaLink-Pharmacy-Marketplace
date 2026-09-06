using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// The pharmacy owner's hub (requirements 10 to 18).
    ///
    /// The rule that governs this whole branch of the application is
    /// requirement 18: every query behind every form reachable from here
    /// carries WHERE PharmacyId = UserSession.PharmacyId, taken from the login,
    /// so one pharmacy owner can never read another owner's medicines, orders
    /// or earnings. The rule lives in the queries, not in hidden buttons.
    /// </summary>
    public partial class AdminDashboard : Form
    {
        private readonly OrderService _orders = new OrderService();
        private readonly MedicineService _medicines = new MedicineService();
        private readonly ReportService _reports = new ReportService();
        private readonly PrescriptionService _prescriptions = new PrescriptionService();

        private Label _tileOrders;
        private Label _tileRevenue;
        private Label _tileCommission;
        private Label _tileLowStock;

        private bool _loading = true;

        public AdminDashboard()
        {
            InitializeComponent();
        }

        private void AdminDashboard_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            BuildTiles();

            cmbOrderStatus.Items.AddRange(new object[] { "All orders", "Placed", "Confirmed", "Delivered", "Cancelled" });
            cmbOrderStatus.SelectedIndex = 0;

            _loading = false;
            LoadEverything();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Pharmacy Owner");

            panelSide.BackColor = UiTheme.Sidebar;
            lblBrand.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
            lblBrand.ForeColor = Color.White;
            lblRole.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            lblRole.ForeColor = Color.FromArgb(140, 205, 185);
            lblShopName.Font = UiTheme.FontSmall;
            lblShopName.ForeColor = Color.FromArgb(190, 205, 216);
            lblShopName.Text = UserSession.PharmacyName + Environment.NewLine + UserSession.FullName;

            foreach (Button button in new[] { btnMedicines, btnInventory, btnPrescriptions, btnEarnings,
                                              btnOffers, btnReviews, btnShopProfile, btnMyProfile })
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

            lblOrdersTitle.Font = UiTheme.FontHeading;
            lblOrdersTitle.ForeColor = UiTheme.TextDark;
            lblOrdersHint.Font = UiTheme.FontSmall;
            lblOrdersHint.ForeColor = UiTheme.TextMuted;
            lblLowStockTitle.Font = UiTheme.FontHeading;
            lblLowStockTitle.ForeColor = UiTheme.TextDark;

            UiTheme.StyleGrid(dgvOrders);
            UiTheme.StyleGrid(dgvLowStock);
            dgvOrders.CellFormatting += dgvOrders_CellFormatting;
            dgvLowStock.CellFormatting += dgvLowStock_CellFormatting;

            UiTheme.StyleSuccess(btnConfirmOrder);
            UiTheme.StylePrimary(btnDeliverOrder);
            UiTheme.StyleDanger(btnCancelOrder);
            UiTheme.StyleSecondary(btnViewInvoice);
        }

        private void BuildTiles()
        {
            Panel t1 = UiTheme.BuildTile("ORDERS RECEIVED", UiTheme.Accent, out _tileOrders);
            Panel t2 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileRevenue);
            Panel t3 = UiTheme.BuildTile("PHARMALINK COMMISSION", UiTheme.Warning, out _tileCommission);
            Panel t4 = UiTheme.BuildTile("MEDICINES BELOW MIN STOCK", UiTheme.Danger, out _tileLowStock);

            int x = 250;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                tile.Location = new Point(x, 86);
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
            if (_loading) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                int orders, pendingOrders;
                decimal revenue, commission;
                _reports.GetPharmacyTotals(UserSession.PharmacyId, out orders, out revenue,
                                           out commission, out pendingOrders);

                int lowStock = _medicines.CountLowStock(UserSession.PharmacyId);
                int pendingRx = _prescriptions.CountPending(UserSession.PharmacyId);

                _tileOrders.Text = orders.ToString();
                _tileRevenue.Text = UiTheme.Money(revenue);
                _tileCommission.Text = UiTheme.Money(commission);
                _tileLowStock.Text = lowStock.ToString();

                lblHeaderSub.Text = UserSession.PharmacyName +
                                    "   |   " + _medicines.CountMedicines(UserSession.PharmacyId) + " medicines listed" +
                                    "   |   " + pendingOrders + " order(s) waiting to be confirmed" +
                                    "   |   " + pendingRx + " prescription(s) to verify";

                LoadOrders();

                dgvLowStock.DataSource = _medicines.GetLowStock(UserSession.PharmacyId);
                if (dgvLowStock.Columns.Count > 0)
                {
                    dgvLowStock.Columns["MedicineId"].HeaderText = "ID";
                    dgvLowStock.Columns["MedicineId"].FillWeight = 30;
                    dgvLowStock.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvLowStock.Columns["Strength"].HeaderText = "Strength";
                    dgvLowStock.Columns["CategoryName"].HeaderText = "Category";
                    dgvLowStock.Columns["Stock"].HeaderText = "In stock";
                    dgvLowStock.Columns["MinStock"].HeaderText = "Minimum";
                    dgvLowStock.Columns["ShortfallUnits"].HeaderText = "Order at least";
                }

                lblLowStockTitle.Text = lowStock == 0
                    ? "Low stock alert  -  every medicine is above its minimum level"
                    : "Low stock alert  (" + lowStock + ")  -  double click a row to restock";
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

        private void LoadOrders()
        {
            string status = cmbOrderStatus.SelectedIndex <= 0 ? "" : cmbOrderStatus.SelectedItem.ToString();
            dgvOrders.DataSource = _orders.GetOrdersForPharmacy(UserSession.PharmacyId, status);

            if (dgvOrders.Columns.Count > 0)
            {
                dgvOrders.Columns["OrderId"].HeaderText = "Order";
                dgvOrders.Columns["OrderId"].FillWeight = 42;
                dgvOrders.Columns["OrderDate"].HeaderText = "Placed";
                dgvOrders.Columns["CustomerName"].HeaderText = "Customer";
                dgvOrders.Columns["CustomerPhone"].HeaderText = "Mobile";
                dgvOrders.Columns["Items"].HeaderText = "Lines";
                dgvOrders.Columns["Items"].FillWeight = 34;
                dgvOrders.Columns["ItemsTotal"].HeaderText = "Items (Tk)";
                dgvOrders.Columns["DeliveryCharge"].HeaderText = "Delivery";
                dgvOrders.Columns["DeliveryCharge"].FillWeight = 45;
                dgvOrders.Columns["TotalAmount"].HeaderText = "Total (Tk)";
                dgvOrders.Columns["PaymentMethod"].HeaderText = "Payment";
                dgvOrders.Columns["Status"].HeaderText = "Status";
                dgvOrders.Columns["Status"].FillWeight = 50;
                dgvOrders.Columns["RxState"].HeaderText = "Prescription";
                dgvOrders.Columns["DeliveryAddress"].Visible = false;
            }

            UpdateOrderButtons();
        }

        private void dgvOrders_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvOrders.Columns.Count == 0) return;

            DataGridViewRow row = dgvOrders.Rows[e.RowIndex];
            object status = row.Cells["Status"].Value;
            object rx = row.Cells["RxState"].Value;
            if (status == null) return;

            if (rx != null && rx.ToString() == "Waiting on Rx")
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224);
            else if (status.ToString() == "Delivered")
                row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            else if (status.ToString() == "Cancelled")
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
            else
                row.DefaultCellStyle.BackColor = Color.White;
        }

        private void dgvLowStock_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            dgvLowStock.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        // ---------------------------------------------------------------------
        //  ORDER ACTIONS
        // ---------------------------------------------------------------------

        private void dgvOrders_SelectionChanged(object sender, EventArgs e) => UpdateOrderButtons();

        private void UpdateOrderButtons()
        {
            DataGridViewRow row = dgvOrders.CurrentRow;
            bool hasRow = row != null && row.Cells["Status"].Value != null;

            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";
            string rxState = hasRow && row.Cells["RxState"].Value != null
                ? row.Cells["RxState"].Value.ToString() : "";

            // An order whose prescription is still Pending cannot be confirmed,
            // and the button explains why rather than failing silently.
            btnConfirmOrder.Enabled = hasRow && status == "Placed" && rxState == "Clear";
            btnDeliverOrder.Enabled = hasRow && status == "Confirmed";
            btnCancelOrder.Enabled = hasRow && status != "Delivered" && status != "Cancelled";
            btnViewInvoice.Enabled = hasRow;

            if (hasRow && status == "Placed" && rxState != "Clear")
                btnConfirmOrder.Text = "Rx not verified";
            else
                btnConfirmOrder.Text = "Confirm order";
        }

        private int SelectedOrderId()
        {
            if (dgvOrders.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvOrders.CurrentRow.Cells["OrderId"].Value);
        }

        private void btnConfirmOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            if (_orders.Confirm(orderId, UserSession.PharmacyId))
            {
                MessageBox.Show("Order " + orderId + " confirmed and is ready for dispatch.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    "Order " + orderId + " could not be confirmed.\r\n\r\n" +
                    "The UPDATE carries NOT EXISTS (SELECT 1 FROM Prescriptions WHERE VerifyStatus <> 'Approved'), " +
                    "so an order with an unverified prescription is refused by the database, not just by this form.",
                    "Cannot confirm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            LoadEverything();
        }

        private void btnDeliverOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            _orders.MarkDelivered(orderId, UserSession.PharmacyId);
            LoadEverything();
        }

        private void btnCancelOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            DialogResult answer = MessageBox.Show(
                "Cancel order " + orderId + "?\r\n\r\n" +
                "The units on this order are put back on the shelf inside the same transaction.",
                "Cancel order", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            _orders.Cancel(orderId, UserSession.PharmacyId);
            LoadEverything();
        }

        private void btnViewInvoice_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            using (InvoiceForm invoice = new InvoiceForm(orderId))
            {
                invoice.ShowDialog(this);
            }
        }

        private void dgvLowStock_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            int medicineId = Convert.ToInt32(dgvLowStock.Rows[e.RowIndex].Cells["MedicineId"].Value);

            using (MedicineEditorForm editor = new MedicineEditorForm(medicineId, true))
            {
                editor.ShowDialog(this);
            }
            LoadEverything();
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
            LoadEverything();
        }

        private void btnMedicines_Click(object sender, EventArgs e) => OpenChild(new AdminMedicineForm());
        private void btnInventory_Click(object sender, EventArgs e) => OpenChild(new AdminInventoryForm());
        private void btnPrescriptions_Click(object sender, EventArgs e) => OpenChild(new VerifyPrescriptionForm());
        private void btnEarnings_Click(object sender, EventArgs e) => OpenChild(new AdminEarningsForm());
        private void btnOffers_Click(object sender, EventArgs e) => OpenChild(new DiscountOffersForm());
        private void btnReviews_Click(object sender, EventArgs e) => OpenChild(new AdminReviewsForm());
        private void btnShopProfile_Click(object sender, EventArgs e) => OpenChild(new PharmacyProfileForm());
        private void btnMyProfile_Click(object sender, EventArgs e) => OpenChild(new MyProfileForm());
        private void btnRefresh_Click(object sender, EventArgs e) => LoadEverything();
        private void cmbOrderStatus_SelectedIndexChanged(object sender, EventArgs e) { if (!_loading) LoadOrders(); }

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
