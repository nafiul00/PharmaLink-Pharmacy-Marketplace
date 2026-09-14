using System.Data;                  // DataTable and DataRow, what the grids are bound to
using System.Drawing;               // Color, for the status pill and the disabled button
using System.Windows.Forms;         // Form, DataGridView, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, including Money for the "Tk 0.00" formatting
using PharmaLinkApp.Models;         // Pharmacy, used to fill the pharmacy filter
using PharmaLinkApp.Services;       // OrderService and PharmacyService, the two data sources

namespace PharmaLinkApp.Forms       // presentation, kept apart so the dependency runs one way
{
    /// <summary>Past orders, the invoice, and the Rate and Review action.</summary>
    public partial class OrderHistoryForm : Form
    {
        private readonly OrderService _orders = new OrderService();             // the orders themselves
        private readonly PharmacyService _pharmacies = new PharmacyService();   // used once, to fill the filter

        // Starts true: assigning SelectedIndex raises the same event a click does.
        private bool _loading = true;

        public OrderHistoryForm()   // no arguments: the customer id comes from UserSession
        {
            InitializeComponent();   // designer controls only; data work waits for Load
        }

        private void OrderHistoryForm_Load(object sender, EventArgs e)   // runs once the window exists
        {
            ApplyTheme();   // styling first, so the grids are themed before any row arrives

            // Index 0 is the sentinel; the rest are the values CK_Orders_Status allows.
            cmbStatus.Items.AddRange(new object[] { "All statuses", "Placed", "Confirmed", "Delivered", "Cancelled" });
            cmbStatus.SelectedIndex = 0;   // default to showing everything

            cmbPharmacy.Items.Add("All pharmacies");   // the same sentinel-at-zero pattern

            // Approved shops only; a later-suspended one still shows on old orders.
            foreach (Pharmacy pharmacy in _pharmacies.GetApprovedList())
                // The id leads the text, because a string ComboBox has no hidden value column.
                cmbPharmacy.Items.Add(pharmacy.PharmacyId + " - " + pharmacy.PharmacyName);

            cmbPharmacy.SelectedIndex = 0;   // land on the sentinel, so the screen opens unfiltered

            // Six months: wide enough to be useful, narrow enough not to load a whole history.
            dtpFrom.Value = DateTime.Today.AddMonths(-6);
            dtpTo.Value = DateTime.Today;   // today, so the range reads as a real period

            _loading = false;   // released now that every filter holds its intended value
            LoadOrders();   // the one deliberate query of the whole start-up sequence
        }

        // Presentation only, so a change of appearance cannot alter behaviour.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Orders");              // shared chrome and the window caption
            StartPosition = FormStartPosition.CenterParent;    // opened with ShowDialog, so centre it

            panelHeader.BackColor = UiTheme.Primary;                  // the brand band, identical everywhere
            lblTitle.Font = UiTheme.FontTitle;                        // the shared title font, not a new one
            lblTitle.ForeColor = Color.White;                         // the only colour with contrast on green
            lblSubtitle.Font = UiTheme.FontSmall;                     // a size down, so the two read as a pair
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);    // pale green: legible but secondary

            lblItemsTitle.Font = UiTheme.FontHeading;      // heads the lower grid, so two grids stay distinct
            lblItemsTitle.ForeColor = UiTheme.TextDark;    // dark, because it sits on the white body
            lblNote.Font = UiTheme.FontSmall;              // the fixed explanation under the grids
            lblNote.ForeColor = UiTheme.TextMuted;         // muted, so it never competes with the status line
            lblStatus.Font = UiTheme.FontSmall;            // the line reporting the count and period total
            lblStatus.ForeColor = UiTheme.TextMuted;       // muted too, so an update does not read as an error

            UiTheme.StyleSecondary(btnBack);          // grey: leaving the screen changes nothing
            UiTheme.StyleSecondary(btnRefresh);       // grey too, because re-running a query is not a decision
            UiTheme.StyleAccent(btnViewInvoice);      // accent: viewing a bill is useful but writes nothing
            UiTheme.StylePrimary(btnRateReview);      // primary, the only action here that creates a record
            UiTheme.StyleGrid(dgvOrders);             // read-only, full-row select, alternating rows
            UiTheme.StyleGrid(dgvOrderItems);         // the detail grid gets the same treatment
            dgvOrders.CellFormatting += dgvOrders_CellFormatting;   // wired in code, beside the grid it paints
        }

        private int SelectedPharmacyId()   // turns the dropdown text back into the id the query wants
        {
            if (cmbPharmacy.SelectedIndex <= 0) return 0;   // 0 and -1 both mean no filter, and so does 0

            string text = cmbPharmacy.SelectedItem.ToString();   // safe now: index 0 and -1 were handled above

            // Built as "3 - Lazz Pharma", so Substring not Split: the name contains spaces.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void LoadOrders()   // the single read path every filter and button ends at
        {
            if (_loading) return;   // the guard that makes the filter handlers safe to wire up

            // A failure should leave the window open showing whatever it already had.
            try
            {
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();   // "" is the optional-filter value

                // All four filters go to SQL, so the totals below cover exactly the rows shown.
                DataTable table = _orders.GetHistoryForCustomer(
                    UserSession.UserId, status, SelectedPharmacyId(), dtpFrom.Value, dtpTo.Value);   // the session id leads, so ownership is settled first

                dgvOrders.DataSource = table;   // binding replaces the previous contents outright

                if (dgvOrders.Columns.Count > 0)   // the columns exist only after the bind above
                {
                    // Headers are renamed here, so the query keeps the names the C# indexes by.
                    dgvOrders.Columns["OrderId"].HeaderText = "Invoice";

                    dgvOrders.Columns["OrderId"].FillWeight = 45;                   // a proportion, not pixels: the grid is in Fill mode
                    dgvOrders.Columns["OrderDate"].HeaderText = "Placed on";        // plain English, not a column name
                    dgvOrders.Columns["PharmacyName"].HeaderText = "Pharmacy";      // the longest column, and the one given the room

                    // "Lines", because the figure is COUNT(OrderItemId), not a count of units.
                    dgvOrders.Columns["Items"].HeaderText = "Lines";
                    dgvOrders.Columns["Items"].FillWeight = 34;   // one or two digits, so the narrowest column

                    // The currency sits in the header, so the figures stay plain and comparable.
                    dgvOrders.Columns["ItemsTotal"].HeaderText = "Items (Tk)";
                    dgvOrders.Columns["DeliveryCharge"].HeaderText = "Delivery";      // a per-order charge, not a product price
                    dgvOrders.Columns["DeliveryCharge"].FillWeight = 45;              // a small fixed figure, so it stays narrow
                    dgvOrders.Columns["TotalAmount"].HeaderText = "Paid (Tk)";        // what actually left the customer's pocket
                    dgvOrders.Columns["PaymentMethod"].HeaderText = "Method";         // short: the values are already short words
                    dgvOrders.Columns["Status"].HeaderText = "Status";                // renamed to itself, so no raw name slips through
                    dgvOrders.Columns["Status"].FillWeight = 50;                      // one word, and the colour does the rest

                    // Hidden, not dropped: UpdateSelection needs CanReview to set the button.
                    dgvOrders.Columns["CanReview"].Visible = false;
                }

                decimal lifetime = 0m;   // 0m, so money stays decimal and never picks up double rounding
                // Walks the bound table, not the grid, so the total covers the data itself.
                foreach (DataRow row in table.Rows)
                    // DBNull would throw in Convert, and cancelled orders were never paid for.
                    if (row["TotalAmount"] != DBNull.Value && row["Status"].ToString() != "Cancelled")
                        lifetime += Convert.ToDecimal(row["TotalAmount"]);   // Convert, since the provider picks the type

                // Naming the period stops a small total reading as missing orders.
                lblStatus.Text = table.Rows.Count + " order(s) between " +
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " +      // spelled out, so 03/04 is unambiguous
                                 dtpTo.Value.ToString("dd MMM yyyy") +                  // the same format at both ends
                                 ".   Total spent in this period: " + UiTheme.Money(lifetime) + ".";   // Money adds Tk and two decimals

                UpdateSelection();   // rebinding moves the selection without reliably raising the event
            }
            catch (Exception ex)   // the screen is the layer that can tell the customer
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // DbHelper already made it readable
            }
        }

        /// <summary>Status pill: delivered green, cancelled grey, waiting amber.</summary>
        private void dgvOrders_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvOrders.Columns.Count == 0) return;   // -1 is the header; this fires during binding too

            DataGridViewRow row = dgvOrders.Rows[e.RowIndex];   // the tint is applied to the row, not one cell

            // Read from the CELL, so the colour always matches the text the customer sees.
            object status = row.Cells["Status"].Value;
            if (status == null) return;   // a row still being built has no value yet

            switch (status.ToString())   // set on the row's style, so one pass paints the whole row
            {
                // Green for finished business, the same green the other screens use.
                case "Delivered": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;

                // Grey reads as inactive: a cancelled order is not a problem to act on.
                case "Cancelled": row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); break;

                // Amber for placed but not yet confirmed, where the customer may be waiting.
                case "Placed": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;

                // Confirmed lands here, repainted white because grid rows are recycled.
                default: row.DefaultCellStyle.BackColor = Color.White; break;
            }
        }

        // Changing the selected order redraws the lines and re-decides both buttons.
        private void dgvOrders_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()   // also called from LoadOrders, so a click and a rebind agree
        {
            DataGridViewRow row = dgvOrders.CurrentRow;   // null rather than throwing when nothing is selected

            // The empty state is restored in one place, so nothing stale is left over.
            if (row == null || row.Cells["OrderId"].Value == null)
            {
                dgvOrderItems.DataSource = null;   // clear the lines, do not leave the last order's
                btnViewInvoice.Enabled = false;    // no order selected, so there is no bill to open
                btnRateReview.Enabled = false;     // and nothing to review either
                btnRateReview.Text = "Rate and review";   // back to the neutral caption
                return;   // early exit, so everything below can assume a real order row
            }

            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);   // the key the detail query travels on

            // Fetched on selection: one small query rather than every line of every order.
            dgvOrderItems.DataSource = _orders.GetOrderItems(orderId);

            if (dgvOrderItems.Columns.Count > 0)   // again, the columns exist only after binding
            {
                dgvOrderItems.Columns["MedicineName"].HeaderText = "Medicine";   // the name the customer recognises
                dgvOrderItems.Columns["Strength"].HeaderText = "Strength";       // 500mg and 250mg are different products
                dgvOrderItems.Columns["Quantity"].HeaderText = "Qty";            // abbreviated: one or two digits

                // "paid", because OrderItems.UnitPrice is what was charged at checkout.
                dgvOrderItems.Columns["UnitPrice"].HeaderText = "Unit price paid (Tk)";
                dgvOrderItems.Columns["Subtotal"].HeaderText = "Line total (Tk)";   // stored on the row, so it cannot drift
            }

            btnViewInvoice.Enabled = true;   // every order has an invoice, cancelled ones included

            // CanReview comes from the query, so the button cannot disagree with it.
            bool canReview = row.Cells["CanReview"].Value != DBNull.Value &&
                             Convert.ToInt32(row.Cells["CanReview"].Value) == 1;   // the CASE returns 1 or 0, not a bool

            string status = row.Cells["Status"].Value.ToString();   // read separately, because the caption names the reason

            btnRateReview.Enabled = canReview;   // the database's answer drives the button directly

            // Set alongside Enabled, or a themed button still looks pressable when disabled.
            btnRateReview.BackColor = canReview ? UiTheme.Primary : Color.FromArgb(170, 190, 184);

            // Three captions, so a disabled button carries its own reason.
            if (canReview) btnRateReview.Text = "Rate and review";
            else if (status != "Delivered") btnRateReview.Text = "Not delivered yet";   // tested first: it outranks the other reason
            else btnRateReview.Text = "Already reviewed";   // delivered, but every medicine is already rated
        }

        private int SelectedOrderId()   // one answer to "which order", so the handlers below stay short
        {
            if (dgvOrders.CurrentRow == null) return 0;   // OrderId starts at 1001, so 0 can never collide

            // Convert, not a cast: the cell holds a boxed value whose type the provider picks.
            return Convert.ToInt32(dgvOrders.CurrentRow.Cells["OrderId"].Value);
        }

        // ---------------------------------------------------------------------

        private void btnViewInvoice_Click(object sender, EventArgs e)   // also serves the double click below
        {
            int orderId = SelectedOrderId();   // 0 when nothing is selected, which the guard turns into a no-op

            // Re-checked: the double click reaches here with no button state in the way.
            if (orderId == 0) return;

            using (InvoiceForm invoice = new InvoiceForm(orderId))   // a closed modal dialog is still not disposed
            {
                // ShowDialog, so the history cannot change underneath the open bill.
                invoice.ShowDialog(this);
            }
        }

        private void dgvOrders_CellDoubleClick(object sender, DataGridViewCellEventArgs e)   // the shortcut a grid invites
        {
            if (e.RowIndex < 0) return;   // the header arrives as -1, which is not a row

            // Calls the button's handler rather than repeating it, so the two cannot drift.
            btnViewInvoice_Click(sender, EventArgs.Empty);
        }

        private void btnRateReview_Click(object sender, EventArgs e)   // enabled only when the query allowed it
        {
            int orderId = SelectedOrderId();   // the same helper the invoice button uses
            if (orderId == 0) return;   // nothing selected, so there is nothing to rate

            using (GiveRatingForm rating = new GiveRatingForm(orderId))   // a modal dialog still needs disposing
            {
                // OK only when a review was written; this line adds where it now appears.
                if (rating.ShowDialog(this) == DialogResult.OK)
                    lblStatus.Text = "Thank you - your review is now visible on the medicine's details screen.";   // names the destination screen
            }

            // Reloaded unconditionally, so CanReview is recomputed and the caption catches up.
            LoadOrders();
        }

        // The four filters share one handler, and the _loading guard makes that safe.
        private void Filter_Changed(object sender, EventArgs e) => LoadOrders();

        private void btnRefresh_Click(object sender, EventArgs e)   // also widens and clears the filters
        {
            _loading = true;   // raised first, or the four assignments below would run five queries

            // Three years, not six months: Refresh is reached for when an order seems missing.
            dtpFrom.Value = DateTime.Today.AddYears(-3);
            dtpTo.Value = DateTime.Today;   // the upper end stays today, so it widens backwards only

            // Both dropdowns go back to their sentinel, so Refresh clears every filter.
            cmbStatus.SelectedIndex = 0;
            cmbPharmacy.SelectedIndex = 0;   // "All pharmacies", which SelectedPharmacyId turns back into 0

            _loading = false;   // lowered again, with all four filters now settled
            LoadOrders();   // the single query this whole handler exists to run
        }

        // Opened with ShowDialog by the home screen, so closing hands control back there.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
