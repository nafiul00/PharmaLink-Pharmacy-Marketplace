using System.Data;                  // DataTable, the shape every service read hands back
using System.Drawing;               // Color, Font, Point and Size, used by the styling below
using System.Windows.Forms;         // Form, Label, Button, DataGridView and the event argument types
using PharmaLinkApp.Helpers;        // UiTheme, the one place colours, fonts and tiles are defined
using PharmaLinkApp.Services;       // the four services this screen reads through: no SQL is written in a form

// One namespace for every window, so a form can open any other by name.
namespace PharmaLinkApp.Forms
{
    /// <summary>The pharmacy owner's hub (requirements 10 to 18).</summary>
    public partial class AdminDashboard : Form   // every query here carries WHERE PharmacyId = UserSession.PharmacyId (req 18)
    {
        // One service per concern; none holds a connection, and no form writes SQL.
        private readonly OrderService _orders = new OrderService();
        private readonly MedicineService _medicines = new MedicineService();          // the catalogue: low stock rows, and the two counts in the subtitle
        private readonly ReportService _reports = new ReportService();                // the four tile figures, all from one statement so they agree
        private readonly PrescriptionService _prescriptions = new PrescriptionService();   // only the pending count is needed here; verifying happens on its own screen

        // Only the inner Label is kept: the panel never changes, the number does.
        private Label _tileOrders;
        private Label _tileRevenue;      // gross sales, via UiTheme.Money so currency reads the same everywhere
        private Label _tileCommission;   // what PharmaLink keeps; shown beside the gross so the two are read together
        private Label _tileLowStock;     // the one count that means work to do, which is why its tile is the red one

        // True until Load finishes: it swallows the first SelectedIndexChanged query.
        private bool _loading = true;

        public AdminDashboard()   // parameterless: the shop comes from UserSession, never from a caller
        {
            // Designer controls only: no database work before the window exists.
            InitializeComponent();
        }

        // Load, not the constructor: by now there is a window to show an error on.
        private void AdminDashboard_Load(object sender, EventArgs e)
        {
            ApplyTheme();    // colours, fonts, grid styling and the CellFormatting hook ups
            BuildTiles();    // the four summary panels, created in code rather than in the designer

            // Built here, not in the designer: index 0 is the "no filter" entry.
            cmbOrderStatus.Items.AddRange(new object[] { "All orders", "Placed", "Confirmed", "Delivered", "Cancelled" });
            cmbOrderStatus.SelectedIndex = 0;   // raises SelectedIndexChanged, which _loading swallows

            _loading = false;   // from here on, changing the filter is allowed to query
            LoadEverything();   // one deliberate first load, now that every control is ready
        }

        // Presentation only, and where both CellFormatting handlers are subscribed.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Pharmacy Owner");   // window background, icon and the title bar suffix naming the role

            panelSide.BackColor = UiTheme.Sidebar;                                  // the dark column; its colour is what separates navigation from content
            lblBrand.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);     // the product name, the largest text on the sidebar
            lblBrand.ForeColor = Color.White;                                       // maximum contrast on the dark panel, so the brand reads first
            lblRole.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);       // small but bold: a caption, not a heading
            lblRole.ForeColor = Color.FromArgb(140, 205, 185);                      // muted green, so the role label sits below the brand in the hierarchy
            lblShopName.Font = UiTheme.FontSmall;                                   // from the theme, so this label matches every other small line in the app
            lblShopName.ForeColor = Color.FromArgb(190, 205, 216);                  // dimmer still: the shop name is context, not a control
            lblShopName.Text = UserSession.PharmacyName + Environment.NewLine + UserSession.FullName;   // shop over person, so the scope of the screen is named before the user is

            // One loop, so the eight buttons look identical by construction.
            foreach (Button button in new[] { btnMedicines, btnInventory, btnPrescriptions, btnEarnings,   // the array is built inline; it exists only for this loop
                                              btnOffers, btnReviews, btnShopProfile, btnMyProfile })      // order matches their top-to-bottom order on the sidebar
            {
                UiTheme.StyleSidebarButton(button);   // flat, left aligned, full width, with the theme's hover colour
            }

            UiTheme.StyleSidebarButton(btnLogout);                                     // styled with the others first, so it keeps the same shape and alignment
            btnLogout.BackColor = UiTheme.Danger;                                      // then overridden to red: it is the one button that ends the session
            btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 60, 60); // a deeper red on hover; the base red would look dead on mouse-over

            panelHeader.BackColor = UiTheme.Primary;                       // the brand green band across the top of the content area
            lblHeaderTitle.Font = UiTheme.FontTitle;                       // the largest content font; this is the page name
            lblHeaderTitle.ForeColor = Color.White;                        // white on the green band, the same pairing as the sidebar brand
            lblHeaderSub.Font = UiTheme.FontSmall;                         // the subtitle line rewritten on every load with the live counts
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);        // pale green: readable on the band without competing with the title
            UiTheme.StyleSecondary(btnRefresh);                            // secondary, not primary: refreshing is available, not the thing to do

            lblOrdersTitle.Font = UiTheme.FontHeading;      // section heading above the orders grid
            lblOrdersTitle.ForeColor = UiTheme.TextDark;    // near-black from the theme rather than pure black, which reads harsh on white
            lblOrdersHint.Font = UiTheme.FontSmall;         // the one-line instruction under the heading
            lblOrdersHint.ForeColor = UiTheme.TextMuted;    // grey, so the hint is findable but never mistaken for data
            lblLowStockTitle.Font = UiTheme.FontHeading;    // the matching heading for the second grid, deliberately the same size
            lblLowStockTitle.ForeColor = UiTheme.TextDark;  // and the same colour, so the two sections read as equals

            UiTheme.StyleGrid(dgvOrders);                                  // row height, header style, alternating rows, selection colour: all from one place
            UiTheme.StyleGrid(dgvLowStock);                                // one call, so two grids on a screen cannot look like two controls
            dgvOrders.CellFormatting += dgvOrders_CellFormatting;          // subscribed here rather than in the designer, next to the handler it points at
            dgvLowStock.CellFormatting += dgvLowStock_CellFormatting;      // subscribing ONCE matters: ApplyTheme runs from Load only

            UiTheme.StyleSuccess(btnConfirmOrder);    // green: the step that moves an order forward
            UiTheme.StylePrimary(btnDeliverOrder);    // brand colour: the ordinary end of the lifecycle
            UiTheme.StyleDanger(btnCancelOrder);      // red: the only button here that puts stock back and cannot be undone
            UiTheme.StyleSecondary(btnViewInvoice);   // grey: read-only, so it must not compete with the three that change data
        }

        // Called once from Load; the tiles are containers, LoadEverything fills them.
        private void BuildTiles()
        {
            // Built in code, so every dashboard gets identical tiles from one method.
            Panel t1 = UiTheme.BuildTile("ORDERS RECEIVED", UiTheme.Accent, out _tileOrders);   // colour matches meaning: red is kept for the count that needs action
            Panel t2 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileRevenue);              // brand green: the headline figure, but nothing to act on
            Panel t3 = UiTheme.BuildTile("PHARMALINK COMMISSION", UiTheme.Warning, out _tileCommission); // amber: money leaving, so it is marked but not alarming
            Panel t4 = UiTheme.BuildTile("MEDICINES BELOW MIN STOCK", UiTheme.Danger, out _tileLowStock);   // red, and the only red: this is the count that needs work

            // Spacing is one increment, not four Location lines that could drift apart.
            int x = 250;   // 250 is the starting x, clear of the sidebar
            foreach (Panel tile in new[] { t1, t2, t3, t4 })   // left to right in the order the four were built above
            {
                // PlaceTile sets location and size, adds to the FORM, and brings it to front.
                UiTheme.PlaceTile(this, tile, x, 86, 236, 84);
                x += 250;   // 236 wide plus a 14 pixel gutter; one number keeps the row evenly spaced
            }
        }

        // ----------  DATA  ----------

        // The single refresh path: every button and child form ends up here.
        private void LoadEverything()
        {
            // One guard for every refresh path, while the form is still being built.
            if (_loading) return;

            Cursor = Cursors.WaitCursor;   // several round trips follow; the pointer says the form is busy
            try   // wraps the whole load, so a failure cannot leave half the screen updated
            {
                // out parameters: ONE statement, so all four tiles describe one instant.
                int orders, pendingOrders;
                decimal revenue, commission;   // decimal, never double: money must not carry binary rounding error
                // UserSession.PharmacyId, never a control: nothing on screen holds it.
                _reports.GetPharmacyTotals(UserSession.PharmacyId, out orders, out revenue,
                                           out commission, out pendingOrders);   // four values, one round trip, one instant in time

                // Counted in SQL, so one number comes back instead of unused rows.
                int lowStock = _medicines.CountLowStock(UserSession.PharmacyId);
                int pendingRx = _prescriptions.CountPending(UserSession.PharmacyId);   // feeds the subtitle only; it has no tile of its own

                _tileOrders.Text = orders.ToString();   // a plain count, so no currency or thousands formatting is wanted
                // UiTheme.Money, so every figure reads "Tk 1,234.00" on any machine.
                _tileRevenue.Text = UiTheme.Money(revenue);
                _tileCommission.Text = UiTheme.Money(commission);   // same formatter as the tile beside it, so the two figures compare at a glance
                _tileLowStock.Text = lowStock.ToString();           // the same number the heading below repeats, both taken from this one variable

                // The subtitle carries the counts that do not deserve a tile of their own.
                lblHeaderSub.Text = UserSession.PharmacyName +
                                    "   |   " + _medicines.CountMedicines(UserSession.PharmacyId) + " medicines listed" +   // the catalogue size: context, not a task
                                    "   |   " + pendingOrders + " order(s) waiting to be confirmed" +   // a queue length, so it says what is waiting rather than a bare number
                                    "   |   " + pendingRx + " prescription(s) to verify";   // "(s)" avoids a singular/plural branch for a line read at a glance

                // Its own method, so the status filter can reload the grid alone.
                LoadOrders();

                // Binding CREATES the columns, so every rename below must come after it.
                dgvLowStock.DataSource = _medicines.GetLowStock(UserSession.PharmacyId);
                // Guard: indexing Columns["MedicineId"] would throw if the bind gave none.
                if (dgvLowStock.Columns.Count > 0)
                {
                    // Headers in code, so the column NAMES stay stable for cell lookups.
                    dgvLowStock.Columns["MedicineId"].HeaderText = "ID";
                    // Fill mode, so FillWeight is a share of the width, not a pixel count.
                    dgvLowStock.Columns["MedicineId"].FillWeight = 30;
                    dgvLowStock.Columns["MedicineName"].HeaderText = "Medicine";   // no FillWeight: the name gets the default share, which is the largest
                    dgvLowStock.Columns["Strength"].HeaderText = "Strength";       // "500mg" and the like; part of the identity of a medicine, not a detail
                    dgvLowStock.Columns["CategoryName"].HeaderText = "Category";   // joined in by the query, so the grid shows the name rather than the id
                    dgvLowStock.Columns["Stock"].HeaderText = "In stock";          // plain English for what the schema calls Stock
                    dgvLowStock.Columns["MinStock"].HeaderText = "Minimum";        // the threshold this row fell below, shown beside the actual so the gap is visible
                    // ShortfallUnits is (MinStock - Stock), computed by the query.
                    dgvLowStock.Columns["ShortfallUnits"].HeaderText = "Order at least";
                }

                // The title carries the state, so an empty grid does not read as a failure.
                lblLowStockTitle.Text = lowStock == 0
                    ? "Low stock alert  -  every medicine is above its minimum level"   // the all-clear, stated positively so an empty grid reads as good news
                    : "Low stock alert  (" + lowStock + ")  -  double click a row to restock";   // the count plus the gesture, because the grid itself cannot advertise it
            }
            catch (Exception ex)   // Exception, not SqlException: survive anything the load throws
            {
                // One catch for the whole load: stale figures must not read as current.
                MessageBox.Show("Could not load the dashboard.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // OK only: there is nothing for the owner to decide, just something to read
            }
            finally   // runs on the success and failure paths alike
            {
                // finally, so a throw cannot leave an hourglass over an idle screen.
                Cursor = Cursors.Default;
            }
        }

        // Split out so the filter reloads this grid alone; no dialog of its own.
        private void LoadOrders()
        {
            // Index 0 becomes "", which the query reads as (@Status = '' OR ...).
            string status = cmbOrderStatus.SelectedIndex <= 0 ? "" : cmbOrderStatus.SelectedItem.ToString();   // <= 0 also covers -1, the no-selection value
            // Only the status is user input, and it travels as a parameter.
            dgvOrders.DataSource = _orders.GetOrdersForPharmacy(UserSession.PharmacyId, status);

            // Same rule as the low stock grid: columns exist only once the DataSource is set.
            if (dgvOrders.Columns.Count > 0)
            {
                dgvOrders.Columns["OrderId"].HeaderText = "Order";       // the number the owner quotes back to a customer on the phone
                dgvOrders.Columns["OrderId"].FillWeight = 42;            // a small share: ids are short, and the space belongs to the name beside it
                dgvOrders.Columns["OrderDate"].HeaderText = "Placed";    // "Placed" rather than "Date": it says WHICH date this column holds
                dgvOrders.Columns["CustomerName"].HeaderText = "Customer";   // joined from Users by the query; the grid never sees a CustomerId
                dgvOrders.Columns["CustomerPhone"].HeaderText = "Mobile";    // on the row because the usual next action is to ring about a delivery
                // Items is COUNT(OrderItemId): lines on the order, not units.
                dgvOrders.Columns["Items"].HeaderText = "Lines";
                dgvOrders.Columns["Items"].FillWeight = 34;   // the narrowest column on the grid; it never holds more than two digits
                // "(Tk)" in the header, so the cells stay numeric and sortable.
                dgvOrders.Columns["ItemsTotal"].HeaderText = "Items (Tk)";
                dgvOrders.Columns["DeliveryCharge"].HeaderText = "Delivery";     // kept as its own column so Items plus Delivery visibly makes the Total
                dgvOrders.Columns["DeliveryCharge"].FillWeight = 45;             // a flat charge, so it is always short and needs little room
                dgvOrders.Columns["TotalAmount"].HeaderText = "Total (Tk)";      // the figure the customer actually pays, and the one the tiles sum
                dgvOrders.Columns["PaymentMethod"].HeaderText = "Payment";       // cash on delivery or card, which changes what dispatch has to collect
                dgvOrders.Columns["Status"].HeaderText = "Status";               // the lifecycle value the four action buttons below are enabled from
                dgvOrders.Columns["Status"].FillWeight = 50;                     // wide enough for "Confirmed", the longest value it can hold
                // RxState is a CASE expression, read by the colouring and by Confirm.
                dgvOrders.Columns["RxState"].HeaderText = "Prescription";
                // Hidden, not dropped: the row still carries the address for other callers.
                dgvOrders.Columns["DeliveryAddress"].Visible = false;
            }

            // Rebinding clears the selection, so the four buttons must be re-evaluated.
            UpdateOrderButtons();
        }

        // Wired in ApplyTheme; sender is unused, the grid is already a field.
        private void dgvOrders_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Fires per CELL while scrolling, so it stays cheap; -1 is the header row.
            if (e.RowIndex < 0 || dgvOrders.Columns.Count == 0) return;

            DataGridViewRow row = dgvOrders.Rows[e.RowIndex];   // the whole row: the colour goes on the line, not on e.CellStyle
            object status = row.Cells["Status"].Value;          // typed as object: a cell holds a boxed value that may still be DBNull
            object rx = row.Cells["RxState"].Value;             // the CASE expression from the query, not a stored column
            // A row can be mid-bind with empty cells, and ToString below would throw.
            if (status == null) return;

            // Order IS priority: a waiting prescription outranks the order status.
            if (rx != null && rx.ToString() == "Waiting on Rx")
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224);   // amber: needs a decision
            else if (status.ToString() == "Delivered")   // reached only when the prescription is settled, so the status is free to speak
                row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;           // green: finished, nothing to do
            else if (status.ToString() == "Cancelled")   // after Delivered, because the two never overlap and this is the rarer case
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);   // grey: dead row, kept for history
            else   // Placed and Confirmed both land here, the ordinary rows
                // Rows are REUSED as the grid scrolls, so white must be set explicitly.
                row.DefaultCellStyle.BackColor = Color.White;

            // Row-level styling, so the status reads as a band across the grid.
        }

        // The low stock painter: simpler, because the query already chose the rows.
        private void dgvLowStock_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;   // the header row has index -1 and has nothing to colour

            // No condition: the query already filtered to Stock < MinStock, so all rows.
            dgvLowStock.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;   // pale red, the same alert colour the inventory screens use
        }

        // ----------  ORDER ACTIONS  ----------

        // Moving the selection changes which order the four buttons would act on.
        private void dgvOrders_SelectionChanged(object sender, EventArgs e) => UpdateOrderButtons();

        // Recomputed from scratch, so no path can leave a stale button enabled.
        private void UpdateOrderButtons()
        {
            DataGridViewRow row = dgvOrders.CurrentRow;   // CurrentRow, not SelectedRows[0], which throws when the selection is empty
            // Null on an empty grid, and cells can be unpopulated during a rebind.
            bool hasRow = row != null && row.Cells["Status"].Value != null;

            // "" stands in when there is no row, so the tests below need no null checks.
            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";
            string rxState = hasRow && row.Cells["RxState"].Value != null   // RxState gets its own null test: unlike Status it can legitimately be absent
                ? row.Cells["RxState"].Value.ToString() : "";               // "" is a value no CASE branch produces, so it can never be mistaken for "Clear"

            // Placed -> Confirmed -> Delivered; each button is enabled only for its step.
            btnConfirmOrder.Enabled = hasRow && status == "Placed" && rxState == "Clear";   // Confirm also needs rxState Clear; OrderService.Confirm enforces it too
            btnDeliverOrder.Enabled = hasRow && status == "Confirmed";   // only the step after Confirm, so Placed can never jump straight to Delivered
            // Cancel restocks, so it must not be possible once the goods are delivered.
            btnCancelOrder.Enabled = hasRow && status != "Delivered" && status != "Cancelled";
            btnViewInvoice.Enabled = hasRow;   // an invoice exists for any order, any status

            // A disabled button with no reason reads as broken, so it names the blocker.
            if (hasRow && status == "Placed" && rxState != "Clear")
                btnConfirmOrder.Text = "Rx not verified";   // names the blocker on the button itself, where the owner is already looking
            else   // every other case, including no selection at all, gets the ordinary label back
                btnConfirmOrder.Text = "Confirm order";     // always restored, so the label from the previous row cannot linger
        }

        // One place that answers "which order is selected", used by all four buttons.
        private int SelectedOrderId()
        {
            // 0 means nothing selected: OrderId is an IDENTITY starting at 1.
            if (dgvOrders.CurrentRow == null) return 0;
            // Convert.ToInt32, not a cast: an (int) cast on another boxed type throws.
            return Convert.ToInt32(dgvOrders.CurrentRow.Cells["OrderId"].Value);
        }

        // Placed -> Confirmed, and the only one of the four that reports its failure.
        private void btnConfirmOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();   // read once, so the message and the call cannot name different orders
            if (orderId == 0) return;   // nothing selected; the button should be disabled anyway

            // Both ids: another shop's order id simply matches no row and changes nothing.
            if (_orders.Confirm(orderId, UserSession.PharmacyId))
            {
                MessageBox.Show("Order " + orderId + " confirmed and is ready for dispatch.",   // names the id, so a mis-click on the wrong row is visible immediately
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Information);   // the information icon, because nothing went wrong here
            }
            else   // false means zero rows changed, which is the database's own report, not a guess
            {
                // The message names the clause that refused it, not just "it failed".
                MessageBox.Show(
                    "Order " + orderId + " could not be confirmed.\r\n\r\n" +   // the blank line separates what happened from why
                    "The UPDATE carries NOT EXISTS (SELECT 1 FROM Prescriptions WHERE VerifyStatus <> 'Approved'), " +   // quotes the actual clause, so the reason can be checked rather than believed
                    "so an order with an unverified prescription is refused by the database, not just by this form.",   // the point: the guard survives even if this screen were bypassed
                    "Cannot confirm", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // warning, not error: the action was refused, nothing went wrong
            }
            // Reloaded on BOTH paths: a stale grid is what let the attempt be made.
            LoadEverything();
        }

        // Confirmed -> Delivered, the last step of the lifecycle and the quietest one.
        private void btnDeliverOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();   // the same helper as the other three, so "which row" means one thing
            if (orderId == 0) return;          // defensive: a keyboard shortcut could still land here

            // No prompt: this is the routine end of the lifecycle, unlike Cancel.
            _orders.MarkDelivered(orderId, UserSession.PharmacyId);   // the result is not read; the reload below shows whether it changed
            LoadEverything();   // the colour and the four buttons both depend on the status just changed
        }

        // The destructive one: it ends the order AND returns the units to the shelf.
        private void btnCancelOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();   // captured before the dialog, so the prompt names the row the user was looking at
            if (orderId == 0) return;          // no selection, nothing to cancel, and no dialog is worth showing

            // Cancel is the one action that moves stock, so it asks first.
            DialogResult answer = MessageBox.Show(
                "Cancel order " + orderId + "?\r\n\r\n" +   // the id is in the question itself, so the wrong row cannot be cancelled by reflex
                "The units on this order are put back on the shelf inside the same transaction.",   // states the side effect, which is the part the grid cannot show
                "Cancel order", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);   // Yes/No rather than OK/Cancel: the question is a decision, not an acknowledgement

            // Tested against Yes, so Escape and the close cross also mean do nothing.
            if (answer != DialogResult.Yes) return;

            // Restock and status change share one transaction inside the service.
            _orders.Cancel(orderId, UserSession.PharmacyId);
            LoadEverything();   // the tiles change too: a cancelled order leaves the revenue total
        }

        // The only read-only action here, which is why every status enables it.
        private void btnViewInvoice_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();   // the invoice is built from the id alone; InvoiceForm re-reads the order itself
            if (orderId == 0) return;          // nothing selected, so there is no invoice to open

            // using releases the handles; ShowDialog(this) blocks and keeps it in front.
            using (InvoiceForm invoice = new InvoiceForm(orderId))
            {
                invoice.ShowDialog(this);   // modal, so the owner cannot act on a different order while reading this one
            }
            // No reload: an invoice is read only, so nothing behind it changed.
        }

        // Double click opens that medicine's editor, the row already pointed at.
        private void dgvLowStock_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // double clicking the header must not open an editor
            int medicineId = Convert.ToInt32(dgvLowStock.Rows[e.RowIndex].Cells["MedicineId"].Value);   // e.RowIndex, not CurrentRow: the double click names its own row

            // The second argument says "opened to restock", so the caret goes there.
            using (MedicineEditorForm editor = new MedicineEditorForm(medicineId, true))
            {
                editor.ShowDialog(this);   // blocks here, so the reload below cannot run against a half-finished edit
            }
            // The edit may move the stock, the alert grid and the tile, so re-read all.
            LoadEverything();
        }

        // ----------  NAVIGATION  ----------

        // Typed as Form, which is what lets all eight sidebar buttons share it.
        private void OpenChild(Form child)
        {
            // The open-modally-then-refresh pattern, written once instead of eight times.
            using (child)
            {
                child.ShowDialog(this);   // "this" as the owner keeps the child in front of the dashboard
            }
            // Any child may have changed what this reports, so refresh unconditionally.
            LoadEverything();
        }

        // One line each: open modally, then refresh. None passes a pharmacy id.
        private void btnMedicines_Click(object sender, EventArgs e) => OpenChild(new AdminMedicineForm());   // every child reads UserSession.PharmacyId itself
        private void btnInventory_Click(object sender, EventArgs e) => OpenChild(new AdminInventoryForm());        // stock in and out; changes the low stock grid, hence the reload
        private void btnPrescriptions_Click(object sender, EventArgs e) => OpenChild(new VerifyPrescriptionForm());   // approving here is what unblocks Confirm on an order
        private void btnEarnings_Click(object sender, EventArgs e) => OpenChild(new AdminEarningsForm());          // the same money the tiles summarise, broken down by period
        private void btnOffers_Click(object sender, EventArgs e) => OpenChild(new DiscountOffersForm());           // discounts change displayed prices, so the totals are re-read on return
        private void btnReviews_Click(object sender, EventArgs e) => OpenChild(new AdminReviewsForm());            // read-only for the owner; moderation belongs to the Super Admin
        private void btnShopProfile_Click(object sender, EventArgs e) => OpenChild(new PharmacyProfileForm());     // editing the shop name changes the sidebar and the subtitle
        private void btnMyProfile_Click(object sender, EventArgs e) => OpenChild(new MyProfileForm());             // the person rather than the shop; the second line of the sidebar
        private void btnRefresh_Click(object sender, EventArgs e) => LoadEverything();                             // the manual path into the same reload every other handler uses
        // Only the grid reloads: the tiles count every order, whatever the filter.
        private void cmbOrderStatus_SelectedIndexChanged(object sender, EventArgs e) { if (!_loading) LoadOrders(); }

        // The one button that ends the session, hence the only red one on the sidebar.
        private void btnLogout_Click(object sender, EventArgs e)
        {
            // Logging out is one click next to seven navigation buttons, so it asks first.
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // the question icon: nothing has gone wrong and nothing is warned about

            if (answer == DialogResult.Yes)   // tested for Yes, so Escape and the close cross both mean stay logged in
            {
                // Clear BEFORE Close: the session is static and outlives this form.
                UserSession.Clear();
                // Closing does not end the app: LoginForm reappears on FormClosed.
                Close();
            }
        }
    }
}
