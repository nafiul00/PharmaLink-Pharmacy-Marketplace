using System.Data;                  // DataTable and DataRow, what the grids are bound to
using System.Drawing;               // Color, for the status pill and the disabled button
using System.Windows.Forms;         // Form, DataGridView, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, including Money for the "Tk 0.00" formatting
using PharmaLinkApp.Models;         // Pharmacy, used to fill the pharmacy filter
using PharmaLinkApp.Services;       // OrderService and PharmacyService, the two data sources here

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
        // Two services because this screen draws on two areas: the orders themselves and
        // the list of pharmacies used by the filter. Each service owns its own queries,
        // so neither form nor service has to know about the other's tables.
        private readonly OrderService _orders = new OrderService();
        private readonly PharmacyService _pharmacies = new PharmacyService();

        // Starts true, and every filter change is ignored until the Load handler has
        // finished. Assigning SelectedIndex on a ComboBox raises SelectedIndexChanged,
        // which is wired to Filter_Changed, so without this flag simply populating the
        // two dropdowns would fire several queries against half-configured filters.
        private bool _loading = true;

        public OrderHistoryForm()
        {
            // Builds the controls from the designer file. Nothing else belongs in the
            // constructor: the data work is done in Load, where an exception can be shown
            // to the customer instead of breaking construction of the window.
            InitializeComponent();
        }

        private void OrderHistoryForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            // "All statuses" sits at index 0 as a sentinel, and the four that follow are
            // exactly the values CK_Orders_Status allows in the database. Typing them here
            // rather than querying DISTINCT Status means an empty status still appears in
            // the filter, which is how a customer discovers they have no cancelled orders
            // rather than wondering where the option went.
            cmbStatus.Items.AddRange(new object[] { "All statuses", "Placed", "Confirmed", "Delivered", "Cancelled" });
            cmbStatus.SelectedIndex = 0;   // default to showing everything

            // The same sentinel-at-zero pattern for the pharmacy filter.
            cmbPharmacy.Items.Add("All pharmacies");

            // Only APPROVED pharmacies are listed, which is what the service's name
            // promises. A pharmacy that was later suspended still appears on the customer's
            // old orders in the grid; it simply stops being offered as a filter.
            foreach (Pharmacy pharmacy in _pharmacies.GetApprovedList())
                // The id leads the text so SelectedPharmacyId can read it back. A ComboBox
                // of strings has no hidden value column, so the id travels in the text.
                cmbPharmacy.Items.Add(pharmacy.PharmacyId + " - " + pharmacy.PharmacyName);

            cmbPharmacy.SelectedIndex = 0;

            // Six months is a deliberate default: wide enough that a customer sees recent
            // orders immediately, narrow enough that a long history does not all load at
            // once. Refresh widens it to three years when someone is looking further back.
            dtpFrom.Value = DateTime.Today.AddMonths(-6);
            dtpTo.Value = DateTime.Today;

            // Released only now that every filter holds its intended value, so the single
            // load below is the first query this screen runs.
            _loading = false;
            LoadOrders();
        }

        // Presentation only: fonts, colours, grid styling and the one event subscription
        // that has to be made in code. Kept apart from the data methods so a change of
        // appearance cannot alter behaviour.
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
            // Index 0 is the "All pharmacies" sentinel and -1 is no selection, so both mean
            // no filter. 0 is returned for that case because the query treats 0 as "every
            // pharmacy" through its (@PharmacyId = 0 OR ...) clause.
            if (cmbPharmacy.SelectedIndex <= 0) return 0;

            string text = cmbPharmacy.SelectedItem.ToString();

            // The item was built as "3 - Lazz Pharma", so everything before the first space
            // is the id. Substring by position rather than Split, because the pharmacy name
            // itself contains spaces.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void LoadOrders()
        {
            // The guard that makes the filter handlers safe to wire up. Every combo box and
            // date picker on this screen calls this method, including while the Load
            // handler is still setting their initial values.
            if (_loading) return;

            // A failed query here should leave the window open with whatever it was already
            // showing, so it is caught and reported rather than allowed to close the form.
            try
            {
                // An empty string, not null and not "All statuses", because the query uses
                // the optional-filter pattern (@Status = '' OR o.Status = @Status). One
                // query therefore serves both the filtered and the unfiltered case.
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();

                // All four filters go to the database, not to a DataView. Filtering in SQL
                // means the totals below are computed over exactly the rows on screen, and
                // a customer with years of orders never pulls them all across the wire.
                // The customer id comes from the session, so no filter on this screen can
                // widen the result beyond the signed-in customer's own orders.
                DataTable table = _orders.GetHistoryForCustomer(
                    UserSession.UserId, status, SelectedPharmacyId(), dtpFrom.Value, dtpTo.Value);

                // Binding replaces the previous contents outright, so there is nothing to
                // clear first, and the grid generates one column per column in the table.
                dgvOrders.DataSource = table;

                // The columns only exist once a DataSource has been set, so every rename
                // below has to happen after the assignment above. The count is checked
                // because an unbound grid would throw on the first indexer.
                if (dgvOrders.Columns.Count > 0)
                {
                    // Headers are renamed rather than aliased in SQL, so the query keeps
                    // returning the column names the C# code indexes by.
                    dgvOrders.Columns["OrderId"].HeaderText = "Invoice";

                    // FillWeight is a proportion, not a pixel width. The grid is in Fill
                    // mode, so narrowing the short numeric columns hands the space to the
                    // pharmacy name, which is the one that actually needs it.
                    dgvOrders.Columns["OrderId"].FillWeight = 45;
                    dgvOrders.Columns["OrderDate"].HeaderText = "Placed on";
                    dgvOrders.Columns["PharmacyName"].HeaderText = "Pharmacy";

                    // "Lines" rather than "Items": the figure is COUNT(OrderItemId), so two
                    // boxes of one medicine count once. Naming it Items would read as units.
                    dgvOrders.Columns["Items"].HeaderText = "Lines";
                    dgvOrders.Columns["Items"].FillWeight = 34;

                    // The currency is put in the header instead of formatting every cell,
                    // which keeps the figures themselves plain and comparable down a column.
                    dgvOrders.Columns["ItemsTotal"].HeaderText = "Items (Tk)";
                    dgvOrders.Columns["DeliveryCharge"].HeaderText = "Delivery";
                    dgvOrders.Columns["DeliveryCharge"].FillWeight = 45;
                    dgvOrders.Columns["TotalAmount"].HeaderText = "Paid (Tk)";
                    dgvOrders.Columns["PaymentMethod"].HeaderText = "Method";
                    dgvOrders.Columns["Status"].HeaderText = "Status";
                    dgvOrders.Columns["Status"].FillWeight = 50;

                    // Hidden, not dropped from the query. CanReview is carried for the
                    // button decision in UpdateSelection, and a customer has no use for a
                    // column of 1s and 0s. Selecting it and hiding it is what lets the
                    // button follow the database's own answer instead of a local guess.
                    dgvOrders.Columns["CanReview"].Visible = false;
                }

                // The period total, accumulated in C# rather than asked of the database,
                // because the rows are already here and a second round trip would only
                // re-read what is on screen. 0m, not 0, so the sum stays decimal throughout
                // and never picks up the rounding error a double would introduce to money.
                decimal lifetime = 0m;
                foreach (DataRow row in table.Rows)
                    // Two conditions. DBNull is checked because Convert.ToDecimal would
                    // throw on it, and cancelled orders are left out because the customer
                    // never paid for them: including them would overstate what was spent.
                    if (row["TotalAmount"] != DBNull.Value && row["Status"].ToString() != "Cancelled")
                        lifetime += Convert.ToDecimal(row["TotalAmount"]);

                // The status line names the PERIOD as well as the figure, so a small total
                // reads as the effect of the date filter rather than as missing orders.
                lblStatus.Text = table.Rows.Count + " order(s) between " +
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " +
                                 dtpTo.Value.ToString("dd MMM yyyy") +
                                 ".   Total spent in this period: " + UiTheme.Money(lifetime) + ".";

                // Rebinding moves the selection, which does not reliably raise
                // SelectionChanged, so the detail grid and the two buttons are brought back
                // into step explicitly. Without this they would still describe the row that
                // was selected before the reload.
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
            // A RowIndex below zero is the header row, which has no data row behind it, and
            // the column check covers the moment before anything is bound. CellFormatting
            // is raised very often, including during binding, so both guards matter.
            if (e.RowIndex < 0 || dgvOrders.Columns.Count == 0) return;

            DataGridViewRow row = dgvOrders.Rows[e.RowIndex];

            // Read from the Status CELL rather than from the DataTable, so the colour
            // always matches the text the customer can actually see in that row.
            object status = row.Cells["Status"].Value;
            if (status == null) return;   // a row still being built has no value yet

            // The colour is set on the ROW's DefaultCellStyle, not on e.CellStyle, so one
            // pass paints the whole row. Doing it per cell would tint whichever cell
            // happened to be formatted and leave the rest of the row white.
            switch (status.ToString())
            {
                // Green for finished business, and the same green the low-stock and
                // verified screens use, so one colour means one thing across the project.
                case "Delivered": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;

                // Grey reads as inactive rather than as a warning: a cancelled order is not
                // a problem to act on, it is simply no longer live.
                case "Cancelled": row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); break;

                // Amber for an order that has been placed but not yet confirmed, which is
                // the state where the customer may still be waiting on something.
                case "Placed": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;

                // Confirmed falls through to here. It is explicitly repainted white rather
                // than left alone, because a recycled row object would otherwise keep the
                // colour of whatever status it displayed before.
                default: row.DefaultCellStyle.BackColor = Color.White; break;
            }
        }

        // Every change of the selected order has to redraw the lines and re-decide both
        // buttons, so the handler simply forwards to the one method that does it.
        private void dgvOrders_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            // CurrentRow rather than SelectedRows[0], because the grid is in full-row
            // single-select mode and CurrentRow is null rather than throwing when empty.
            DataGridViewRow row = dgvOrders.CurrentRow;

            // Nothing selected, or a row that has no order behind it yet. Everything is
            // put back to its empty state in one place, so there is no path on which a
            // stale list of lines is left beside a disabled button.
            if (row == null || row.Cells["OrderId"].Value == null)
            {
                dgvOrderItems.DataSource = null;   // clear the lines, do not leave the last order's
                btnViewInvoice.Enabled = false;
                btnRateReview.Enabled = false;
                btnRateReview.Text = "Rate and review";   // back to the neutral caption
                return;
            }

            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);

            // The lines are fetched on selection rather than loaded with the list. A
            // customer looks at one order at a time, so this is one small query instead of
            // every line of every order in the period.
            dgvOrderItems.DataSource = _orders.GetOrderItems(orderId);

            // Again, the columns exist only after binding.
            if (dgvOrderItems.Columns.Count > 0)
            {
                dgvOrderItems.Columns["MedicineName"].HeaderText = "Medicine";
                dgvOrderItems.Columns["Strength"].HeaderText = "Strength";
                dgvOrderItems.Columns["Quantity"].HeaderText = "Qty";

                // "paid", not "price": OrderItems.UnitPrice is what was charged at
                // checkout, so a bill from before a price change still shows the old
                // figure. The header says so to stop it being read as today's price.
                dgvOrderItems.Columns["UnitPrice"].HeaderText = "Unit price paid (Tk)";
                dgvOrderItems.Columns["Subtotal"].HeaderText = "Line total (Tk)";
            }

            // An invoice exists for every order whatever its status, including a cancelled
            // one, so this button needs no condition beyond having a row selected.
            btnViewInvoice.Enabled = true;

            // CanReview is NOT worked out here. It is a column the query itself
            // computed with a CASE plus a correlated NOT EXISTS: the order must be
            // Delivered AND still contain at least one medicine this customer has not
            // reviewed. Doing it in SQL means the button cannot disagree with what
            // ReviewService.AddReview will actually allow, because both ask the
            // database the same question.
            //
            // The column is hidden in the grid (Visible = false) - it is carried for
            // this decision, not for display.
            //
            // The button is a convenience, not the rule. What actually stops a second
            // review of the same purchase is the UNIQUE constraint UQ_Reviews_OneEach on
            // (CustomerId, MedicineId, OrderId), which holds even if two copies of this
            // screen are open at once and both still show the button as enabled.
            bool canReview = row.Cells["CanReview"].Value != DBNull.Value &&
                             Convert.ToInt32(row.Cells["CanReview"].Value) == 1;

            // The status is read separately because it is what distinguishes the two
            // reasons a review is not possible, and the caption below has to say which.
            string status = row.Cells["Status"].Value.ToString();

            btnRateReview.Enabled = canReview;

            // Set alongside Enabled, because a button given a custom BackColor by the theme
            // keeps that colour when disabled and would otherwise still look pressable.
            btnRateReview.BackColor = canReview ? UiTheme.Primary : Color.FromArgb(170, 190, 184);

            // Three captions for three states. A disabled button with no explanation reads
            // as a fault, so the caption itself carries the reason: not delivered yet
            // (wait), or already reviewed (nothing left to do).
            if (canReview) btnRateReview.Text = "Rate and review";
            else if (status != "Delivered") btnRateReview.Text = "Not delivered yet";
            else btnRateReview.Text = "Already reviewed";
        }

        private int SelectedOrderId()
        {
            // 0 stands for "no order", which is why the callers can test the result rather
            // than repeating the null check on CurrentRow themselves. OrderId is an
            // IDENTITY starting at 1001, so 0 can never collide with a real order.
            if (dgvOrders.CurrentRow == null) return 0;

            // Convert.ToInt32 rather than a cast, because the cell holds a boxed value
            // whose exact numeric type comes from the provider.
            return Convert.ToInt32(dgvOrders.CurrentRow.Cells["OrderId"].Value);
        }

        // ---------------------------------------------------------------------

        private void btnViewInvoice_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();

            // Re-checked rather than trusted, because this handler is also called from the
            // double click below, where no button state stood in the way.
            if (orderId == 0) return;

            // using, so the invoice window's resources are released when it closes. A
            // modal dialog is not disposed for you simply because it was closed.
            using (InvoiceForm invoice = new InvoiceForm(orderId))
            {
                // ShowDialog(this) rather than Show(), so the history cannot be changed
                // underneath the bill while it is open, and the bill stays on top of its
                // parent instead of getting lost behind it.
                invoice.ShowDialog(this);
            }
        }

        private void dgvOrders_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // Double clicking the header would arrive with RowIndex -1, which is not a row
            // and must not open anything.
            if (e.RowIndex < 0) return;

            // Deliberately calls the button's own handler rather than repeating its body,
            // so the double click and the button can never drift apart. EventArgs.Empty is
            // passed because neither argument is used by that handler.
            btnViewInvoice_Click(sender, EventArgs.Empty);
        }

        private void btnRateReview_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;   // nothing selected, so there is nothing to rate

            using (GiveRatingForm rating = new GiveRatingForm(orderId))
            {
                // OK is returned only when a review was actually written. The dialog
                // reports its own outcome in a message box, so this line adds the one
                // thing it cannot say: where the review has now appeared.
                if (rating.ShowDialog(this) == DialogResult.OK)
                    lblStatus.Text = "Thank you - your review is now visible on the medicine's details screen.";
            }

            // Reloaded unconditionally, outside the if. The list has to be re-queried so
            // CanReview is recomputed, which is what turns the button into "Already
            // reviewed" without the customer refreshing. Running it on cancel too costs
            // one query and keeps a single exit path.
            LoadOrders();
        }

        // The four filters share one handler, so the reload rule lives in a single place.
        // The _loading guard inside LoadOrders is what makes it safe to attach these
        // before the dropdowns have been populated.
        private void Filter_Changed(object sender, EventArgs e) => LoadOrders();

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            // Raised before the four assignments below, because each of them raises a
            // change event. Without it this handler would run five queries instead of one,
            // and four of them against half-reset filters.
            _loading = true;

            // Three years, not the six months used at startup. Refresh is what a customer
            // reaches for when an order they expected is missing, so it widens the window
            // rather than restoring the narrower default.
            dtpFrom.Value = DateTime.Today.AddYears(-3);
            dtpTo.Value = DateTime.Today;

            // Both dropdowns go back to their sentinel, so Refresh clears every filter
            // rather than only the dates.
            cmbStatus.SelectedIndex = 0;
            cmbPharmacy.SelectedIndex = 0;

            // Lowered again, then one deliberate query with all four filters settled.
            _loading = false;
            LoadOrders();
        }

        // Closing only. This window is opened with ShowDialog by the customer's home
        // screen, so closing it hands control back there with nothing to tidy up.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
