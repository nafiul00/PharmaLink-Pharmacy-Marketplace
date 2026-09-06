using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 27. Past orders with a status pill, the invoice and the Rate
    /// and Review action.
    ///
    /// The CanReview flag comes from the query itself: an order qualifies only
    /// when its status is 'Delivered' and at least one of its medicines has not
    /// been reviewed yet. The button follows that flag rather than guessing.
    /// </summary>
    public partial class OrderHistoryForm : Form
    {
        private readonly OrderService _orders = new OrderService();
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private bool _loading = true;

        public OrderHistoryForm()
        {
            InitializeComponent();
        }

        private void OrderHistoryForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbStatus.Items.AddRange(new object[] { "All statuses", "Placed", "Confirmed", "Delivered", "Cancelled" });
            cmbStatus.SelectedIndex = 0;

            cmbPharmacy.Items.Add("All pharmacies");
            foreach (Pharmacy pharmacy in _pharmacies.GetApprovedList())
                cmbPharmacy.Items.Add(pharmacy.PharmacyId + " - " + pharmacy.PharmacyName);
            cmbPharmacy.SelectedIndex = 0;

            dtpFrom.Value = DateTime.Today.AddMonths(-6);
            dtpTo.Value = DateTime.Today;

            _loading = false;
            LoadOrders();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Orders");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            lblItemsTitle.Font = UiTheme.FontHeading;
            lblItemsTitle.ForeColor = UiTheme.TextDark;
            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StyleAccent(btnViewInvoice);
            UiTheme.StylePrimary(btnRateReview);
            UiTheme.StyleGrid(dgvOrders);
            UiTheme.StyleGrid(dgvOrderItems);
            dgvOrders.CellFormatting += dgvOrders_CellFormatting;
        }

        private int SelectedPharmacyId()
        {
            if (cmbPharmacy.SelectedIndex <= 0) return 0;
            string text = cmbPharmacy.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void LoadOrders()
        {
            if (_loading) return;

            try
            {
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();

                DataTable table = _orders.GetHistoryForCustomer(
                    UserSession.UserId, status, SelectedPharmacyId(), dtpFrom.Value, dtpTo.Value);

                dgvOrders.DataSource = table;

                if (dgvOrders.Columns.Count > 0)
                {
                    dgvOrders.Columns["OrderId"].HeaderText = "Invoice";
                    dgvOrders.Columns["OrderId"].FillWeight = 45;
                    dgvOrders.Columns["OrderDate"].HeaderText = "Placed on";
                    dgvOrders.Columns["PharmacyName"].HeaderText = "Pharmacy";
                    dgvOrders.Columns["Items"].HeaderText = "Lines";
                    dgvOrders.Columns["Items"].FillWeight = 34;
                    dgvOrders.Columns["ItemsTotal"].HeaderText = "Items (Tk)";
                    dgvOrders.Columns["DeliveryCharge"].HeaderText = "Delivery";
                    dgvOrders.Columns["DeliveryCharge"].FillWeight = 45;
                    dgvOrders.Columns["TotalAmount"].HeaderText = "Paid (Tk)";
                    dgvOrders.Columns["PaymentMethod"].HeaderText = "Method";
                    dgvOrders.Columns["Status"].HeaderText = "Status";
                    dgvOrders.Columns["Status"].FillWeight = 50;
                    dgvOrders.Columns["CanReview"].Visible = false;
                }

                decimal lifetime = 0m;
                foreach (DataRow row in table.Rows)
                    if (row["TotalAmount"] != DBNull.Value && row["Status"].ToString() != "Cancelled")
                        lifetime += Convert.ToDecimal(row["TotalAmount"]);

                lblStatus.Text = table.Rows.Count + " order(s) between " +
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " +
                                 dtpTo.Value.ToString("dd MMM yyyy") +
                                 ".   Total spent in this period: " + UiTheme.Money(lifetime) + ".";

                UpdateSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>The status pill: delivered green, cancelled grey, waiting amber.</summary>
        private void dgvOrders_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvOrders.Columns.Count == 0) return;

            DataGridViewRow row = dgvOrders.Rows[e.RowIndex];
            object status = row.Cells["Status"].Value;
            if (status == null) return;

            switch (status.ToString())
            {
                case "Delivered": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;
                case "Cancelled": row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); break;
                case "Placed": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;
                default: row.DefaultCellStyle.BackColor = Color.White; break;
            }
        }

        private void dgvOrders_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            DataGridViewRow row = dgvOrders.CurrentRow;

            if (row == null || row.Cells["OrderId"].Value == null)
            {
                dgvOrderItems.DataSource = null;
                btnViewInvoice.Enabled = false;
                btnRateReview.Enabled = false;
                btnRateReview.Text = "Rate and review";
                return;
            }

            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);
            dgvOrderItems.DataSource = _orders.GetOrderItems(orderId);

            if (dgvOrderItems.Columns.Count > 0)
            {
                dgvOrderItems.Columns["MedicineName"].HeaderText = "Medicine";
                dgvOrderItems.Columns["Strength"].HeaderText = "Strength";
                dgvOrderItems.Columns["Quantity"].HeaderText = "Qty";
                dgvOrderItems.Columns["UnitPrice"].HeaderText = "Unit price paid (Tk)";
                dgvOrderItems.Columns["Subtotal"].HeaderText = "Line total (Tk)";
            }

            btnViewInvoice.Enabled = true;

            bool canReview = row.Cells["CanReview"].Value != DBNull.Value &&
                             Convert.ToInt32(row.Cells["CanReview"].Value) == 1;
            string status = row.Cells["Status"].Value.ToString();

            btnRateReview.Enabled = canReview;
            btnRateReview.BackColor = canReview ? UiTheme.Primary : Color.FromArgb(170, 190, 184);

            if (canReview) btnRateReview.Text = "Rate and review";
            else if (status != "Delivered") btnRateReview.Text = "Not delivered yet";
            else btnRateReview.Text = "Already reviewed";
        }

        private int SelectedOrderId()
        {
            if (dgvOrders.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvOrders.CurrentRow.Cells["OrderId"].Value);
        }

        // ---------------------------------------------------------------------

        private void btnViewInvoice_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            using (InvoiceForm invoice = new InvoiceForm(orderId))
            {
                invoice.ShowDialog(this);
            }
        }

        private void dgvOrders_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            btnViewInvoice_Click(sender, EventArgs.Empty);
        }

        private void btnRateReview_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            using (GiveRatingForm rating = new GiveRatingForm(orderId))
            {
                if (rating.ShowDialog(this) == DialogResult.OK)
                    lblStatus.Text = "Thank you - your review is now visible on the medicine's details screen.";
            }

            LoadOrders();
        }

        private void Filter_Changed(object sender, EventArgs e) => LoadOrders();

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            _loading = true;
            dtpFrom.Value = DateTime.Today.AddYears(-3);
            dtpTo.Value = DateTime.Today;
            cmbStatus.SelectedIndex = 0;
            cmbPharmacy.SelectedIndex = 0;
            _loading = false;
            LoadOrders();
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
