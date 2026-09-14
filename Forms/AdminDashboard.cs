using System.Data;                  // DataTable, the shape every service read hands back
using System.Drawing;               // Color, Font, Point and Size, used by the styling below
using System.Windows.Forms;         // Form, Label, Button, DataGridView and the event argument types
using PharmaLinkApp.Helpers;        // UiTheme, the one place colours, fonts and tiles are defined
using PharmaLinkApp.Services;       // the four services this screen reads through: no SQL is written in a form

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
        // One service object per concern, created once with the form and shared by every
        // handler on it. None of them holds a connection or any state between calls, so
        // a field costs nothing and saves constructing a service inside each click.
        // Note what is NOT here: no SqlConnection, no SQL text. The form asks a service
        // a question; the service is the only layer that knows what a table is called.
        private readonly OrderService _orders = new OrderService();
        private readonly MedicineService _medicines = new MedicineService();
        private readonly ReportService _reports = new ReportService();
        private readonly PrescriptionService _prescriptions = new PrescriptionService();

        // Only the Label inside each tile is kept, not the Panel around it. The panel is
        // placed once and never changes; the number inside it is rewritten on every
        // refresh, so the Label is the only reference the form actually needs. UiTheme
        // hands it back through an out parameter for exactly this reason.
        private Label _tileOrders;
        private Label _tileRevenue;
        private Label _tileCommission;
        private Label _tileLowStock;

        // True until Load has finished wiring the screen up. Setting
        // cmbOrderStatus.SelectedIndex below raises SelectedIndexChanged, and that
        // handler queries the database; without this flag the first query would run
        // against a grid that has not been styled and before the tiles exist.
        private bool _loading = true;

        public AdminDashboard()
        {
            // Designer generated control creation only. Deliberately no database work
            // here: the constructor runs before the window exists, so an error would
            // have no form to display its message on.
            InitializeComponent();
        }

        private void AdminDashboard_Load(object sender, EventArgs e)
        {
            ApplyTheme();    // colours, fonts, grid styling and the CellFormatting hook ups
            BuildTiles();    // the four summary panels, created in code rather than in the designer

            // The filter list is built here, next to the code that reads it, rather than
            // in the designer where the two could drift apart. The order matters: index 0
            // is the entry LoadOrders treats as "no filter", and the remaining four strings
            // are exactly the values the Status column is allowed to hold.
            cmbOrderStatus.Items.AddRange(new object[] { "All orders", "Placed", "Confirmed", "Delivered", "Cancelled" });
            cmbOrderStatus.SelectedIndex = 0;   // raises SelectedIndexChanged, which _loading swallows

            _loading = false;   // from here on, changing the filter is allowed to query
            LoadEverything();   // one deliberate first load, now that every control is ready
        }

        // Pure presentation, called once from Load. It also subscribes both grids to
        // their CellFormatting handlers, which is why the row colouring further down
        // starts working the moment the form opens.
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
            // BuildTile returns the finished Panel and hands the inner Label back through
            // the out parameter, so the form holds a reference to the one control it will
            // rewrite and none of the decoration. Building them in code rather than on the
            // designer surface means every dashboard in the application gets identical
            // tiles from one method instead of four hand placed copies drifting apart.
            //
            // The colour is the stripe down the left edge and is chosen to match what the
            // figure MEANS: red is reserved for the count that needs action, so a glance
            // at the row of tiles is enough to know whether anything is wrong.
            Panel t1 = UiTheme.BuildTile("ORDERS RECEIVED", UiTheme.Accent, out _tileOrders);
            Panel t2 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileRevenue);
            Panel t3 = UiTheme.BuildTile("PHARMALINK COMMISSION", UiTheme.Warning, out _tileCommission);
            Panel t4 = UiTheme.BuildTile("MEDICINES BELOW MIN STOCK", UiTheme.Danger, out _tileLowStock);

            // 250 is the starting x, clear of the sidebar; the loop then steps by the same
            // amount, which is the tile width plus the gutter. Writing four explicit
            // Location lines would work until one of them was edited and the row stopped
            // being evenly spaced, so the spacing is expressed once as an increment.
            int x = 250;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                // One call replaces four lines. PlaceTile sets the location and size,
                // adds the tile to the FORM rather than to a container, and brings it to
                // the front, because the designer's own controls were added first and
                // would otherwise paint over it. Centralised in UiTheme so every
                // dashboard places its tiles the same way on a scaled display.
                UiTheme.PlaceTile(this, tile, x, 86, 236, 84);
                x += 250;
            }
        }

        // ---------------------------------------------------------------------
        //  DATA
        // ---------------------------------------------------------------------

        private void LoadEverything()
        {
            // Every refresh path in this form funnels through here, so this one guard
            // stops all of them from firing while the form is still being built.
            if (_loading) return;

            Cursor = Cursors.WaitCursor;   // several round trips follow; the pointer says the form is busy
            try
            {
                // out parameters rather than four separate calls: GetPharmacyTotals runs ONE
                // statement containing four scalar subqueries, so the four tiles describe the
                // same instant. Four separate queries could be interleaved with a checkout and
                // show an order count that does not match the revenue beside it.
                int orders, pendingOrders;
                decimal revenue, commission;
                // UserSession.PharmacyId, never a value read off a control. This is the
                // isolation rule in practice: the id is written once at login from the Users
                // row and there is no textbox, combo box or grid cell on any screen holding
                // it, so there is nothing a user could edit to widen their own scope.
                _reports.GetPharmacyTotals(UserSession.PharmacyId, out orders, out revenue,
                                           out commission, out pendingOrders);

                // Two more counts, each scoped by the same session value for the same reason.
                // Counting in SQL rather than fetching rows and counting them in C# means the
                // database returns a single number instead of a result set nobody displays.
                int lowStock = _medicines.CountLowStock(UserSession.PharmacyId);
                int pendingRx = _prescriptions.CountPending(UserSession.PharmacyId);

                _tileOrders.Text = orders.ToString();
                // UiTheme.Money is used rather than a local ToString("C"), so every money
                // figure in the application reads "Tk 1,234.00" and none of them depends on
                // the machine's regional settings.
                _tileRevenue.Text = UiTheme.Money(revenue);
                _tileCommission.Text = UiTheme.Money(commission);
                _tileLowStock.Text = lowStock.ToString();

                // The subtitle carries the counts that are worth knowing but do not deserve a
                // tile of their own. Naming both queues in one line means the owner sees
                // outstanding work without opening the orders or prescriptions screens.
                lblHeaderSub.Text = UserSession.PharmacyName +
                                    "   |   " + _medicines.CountMedicines(UserSession.PharmacyId) + " medicines listed" +
                                    "   |   " + pendingOrders + " order(s) waiting to be confirmed" +
                                    "   |   " + pendingRx + " prescription(s) to verify";

                // The order grid is a separate method because the status filter reloads it on
                // its own, without re-running the tile queries above.
                LoadOrders();

                // Binding the DataTable is what CREATES the grid's columns, so every rename
                // below has to come after this line, not before it.
                dgvLowStock.DataSource = _medicines.GetLowStock(UserSession.PharmacyId);
                // The guard matters: if the bind failed or produced no columns, indexing
                // Columns["MedicineId"] throws. Testing the count first turns a crash into a
                // grid that is merely unstyled.
                if (dgvLowStock.Columns.Count > 0)
                {
                    // Headers are set in code rather than by aliasing in SQL, so the column
                    // NAMES stay stable for the code that reads cells by name further down
                    // while the captions stay readable for the owner.
                    dgvLowStock.Columns["MedicineId"].HeaderText = "ID";
                    // StyleGrid sets AutoSizeColumnsMode to Fill, which makes FillWeight a
                    // share of the available width rather than a pixel count. An id needs far
                    // less room than a medicine name, so it is given a small share.
                    dgvLowStock.Columns["MedicineId"].FillWeight = 30;
                    dgvLowStock.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvLowStock.Columns["Strength"].HeaderText = "Strength";
                    dgvLowStock.Columns["CategoryName"].HeaderText = "Category";
                    dgvLowStock.Columns["Stock"].HeaderText = "In stock";
                    dgvLowStock.Columns["MinStock"].HeaderText = "Minimum";
                    // ShortfallUnits is computed by the query as (MinStock - Stock), so the
                    // grid shows the number to order without any arithmetic on this side.
                    dgvLowStock.Columns["ShortfallUnits"].HeaderText = "Order at least";
                }

                // The panel title carries the state. An empty grid with a heading that still
                // said "Low stock alert" would read as a screen that had failed to load, so
                // the zero case says in words that nothing is wrong.
                lblLowStockTitle.Text = lowStock == 0
                    ? "Low stock alert  -  every medicine is above its minimum level"
                    : "Low stock alert  (" + lowStock + ")  -  double click a row to restock";
            }
            catch (Exception ex)
            {
                // One catch around the whole load rather than one per call. A dashboard that
                // cannot reach the database must SAY so; leaving the previous figures on
                // screen would present stale numbers as current ones. DbHelper has already
                // turned the raw SqlException into a sentence worth showing.
                MessageBox.Show("Could not load the dashboard.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // finally, not the end of try: an exception must not leave the form stuck
                // showing an hourglass over a screen that is no longer doing anything.
                Cursor = Cursors.Default;
            }
        }

        private void LoadOrders()
        {
            // Index 0 is "All orders". It is turned into an empty string because the service
            // query reads it as (@Status = '' OR o.Status = @Status), so one query serves
            // both the filtered and the unfiltered case and there is no second SQL statement
            // to keep in step. Testing <= 0 rather than == 0 also covers -1, which is what
            // SelectedIndex holds when nothing has been chosen yet.
            string status = cmbOrderStatus.SelectedIndex <= 0 ? "" : cmbOrderStatus.SelectedItem.ToString();
            // The pharmacy id comes from the session and the status from the screen. Only the
            // second is user input, and it travels as a parameter, so the filter cannot widen
            // the scope the first one sets.
            dgvOrders.DataSource = _orders.GetOrdersForPharmacy(UserSession.PharmacyId, status);

            // Same rule as the low stock grid: columns exist only once the DataSource is set.
            if (dgvOrders.Columns.Count > 0)
            {
                dgvOrders.Columns["OrderId"].HeaderText = "Order";
                dgvOrders.Columns["OrderId"].FillWeight = 42;
                dgvOrders.Columns["OrderDate"].HeaderText = "Placed";
                dgvOrders.Columns["CustomerName"].HeaderText = "Customer";
                dgvOrders.Columns["CustomerPhone"].HeaderText = "Mobile";
                // Items is COUNT(OrderItemId) from the query, so it is the number of lines on
                // the order rather than the number of units.
                dgvOrders.Columns["Items"].HeaderText = "Lines";
                dgvOrders.Columns["Items"].FillWeight = 34;
                // Both money columns say "(Tk)" in the header rather than formatting each cell,
                // which keeps the underlying values numeric and therefore still sortable.
                dgvOrders.Columns["ItemsTotal"].HeaderText = "Items (Tk)";
                dgvOrders.Columns["DeliveryCharge"].HeaderText = "Delivery";
                dgvOrders.Columns["DeliveryCharge"].FillWeight = 45;
                dgvOrders.Columns["TotalAmount"].HeaderText = "Total (Tk)";
                dgvOrders.Columns["PaymentMethod"].HeaderText = "Payment";
                dgvOrders.Columns["Status"].HeaderText = "Status";
                dgvOrders.Columns["Status"].FillWeight = 50;
                // RxState is a CASE expression in the query, not a stored column. It is what
                // the row colouring and the Confirm button both read.
                dgvOrders.Columns["RxState"].HeaderText = "Prescription";
                // Hidden, not dropped from the SELECT. The address is far too long for a grid
                // row, but keeping it in the DataTable means the row still carries it and the
                // service query stays as it is for anything else that needs the column.
                dgvOrders.Columns["DeliveryAddress"].Visible = false;
            }

            // Rebinding replaces the rows, so whatever was selected is gone and the four
            // action buttons have to be re-evaluated against the new selection.
            UpdateOrderButtons();
        }

        private void dgvOrders_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // CellFormatting fires once per CELL as it is painted, including while the user
            // scrolls, so this handler must stay cheap and must never touch the database.
            // Everything it needs is already on the row.
            //
            // The guard covers two real cases: RowIndex is -1 for the header row, and the
            // event can fire while the grid is being rebound, when there are no columns to
            // index by name yet.
            if (e.RowIndex < 0 || dgvOrders.Columns.Count == 0) return;

            DataGridViewRow row = dgvOrders.Rows[e.RowIndex];
            object status = row.Cells["Status"].Value;
            object rx = row.Cells["RxState"].Value;
            // A row can be mid-bind with its cells not yet populated; painting would then
            // throw on the ToString calls below.
            if (status == null) return;

            // The order of the tests IS the priority. A prescription still waiting outranks
            // the order status, because that is the row the owner has to act on, so it wins
            // even when the order is otherwise a perfectly ordinary Placed order.
            if (rx != null && rx.ToString() == "Waiting on Rx")
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224);   // amber: needs a decision
            else if (status.ToString() == "Delivered")
                row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;           // green: finished, nothing to do
            else if (status.ToString() == "Cancelled")
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);   // grey: dead row, kept for history
            else
                // The explicit white branch is not redundant. DataGridViewRow objects are
                // REUSED as the grid scrolls, so a row left unpainted keeps the colour of
                // whichever row previously occupied that slot, and cancelled orders would
                // appear to be scattered anywhere in the list.
                row.DefaultCellStyle.BackColor = Color.White;

            // Setting DefaultCellStyle on the ROW rather than e.CellStyle on the cell colours
            // the whole line in one assignment, which is what makes the status readable as a
            // band across the grid instead of a single tinted cell.
        }

        private void dgvLowStock_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;   // the header row has index -1 and has nothing to colour

            // No condition here, unlike the orders grid: the query behind this grid already
            // filtered to Stock < MinStock, so every row present IS a low stock row and the
            // whole grid is painted. Re-testing the numbers in C# would be a second copy of
            // the rule that could disagree with the WHERE clause.
            //
            // LowStockBack is a pale red rather than a saturated one. The row has to read as
            // an alert while black text on top of it stays legible, and it is the same colour
            // the inventory and medicine screens use, so red means one thing everywhere.
            dgvLowStock.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        // ---------------------------------------------------------------------
        //  ORDER ACTIONS
        // ---------------------------------------------------------------------

        // Moving the selection changes which order the four buttons would act on, so their
        // enabled state is recalculated rather than left describing the previous row.
        private void dgvOrders_SelectionChanged(object sender, EventArgs e) => UpdateOrderButtons();

        private void UpdateOrderButtons()
        {
            DataGridViewRow row = dgvOrders.CurrentRow;
            // CurrentRow is null on an empty grid, and its cells can still be unpopulated
            // during a rebind, so both conditions are needed before any cell is read.
            bool hasRow = row != null && row.Cells["Status"].Value != null;

            // hasRow is tested before each read, and the empty string stands in when there is
            // no row, so the comparisons below are plain string tests with no null checks
            // scattered through them.
            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";
            string rxState = hasRow && row.Cells["RxState"].Value != null
                ? row.Cells["RxState"].Value.ToString() : "";

            // An order whose prescription is still Pending cannot be confirmed,
            // and the button explains why rather than failing silently.
            // The four buttons encode the order lifecycle:
            //      Placed -> Confirmed -> Delivered,  with Cancelled available until
            //      the order has actually been delivered.
            // Each button is enabled only for the status it can legally act on, so an
            // illegal transition is unreachable rather than merely refused.
            //
            // Confirm additionally needs rxState == "Clear". That value is computed by
            // the ORDER QUERY, not here: a CASE with an EXISTS against Prescriptions.
            // The same rule is enforced again inside OrderService.Confirm, so disabling
            // the button is a courtesy and the database is the guarantee.
            btnConfirmOrder.Enabled = hasRow && status == "Placed" && rxState == "Clear";
            btnDeliverOrder.Enabled = hasRow && status == "Confirmed";
            // Cancel restocks, so it must not be possible once the goods are delivered.
            btnCancelOrder.Enabled = hasRow && status != "Delivered" && status != "Cancelled";
            btnViewInvoice.Enabled = hasRow;   // an invoice exists for any order, any status

            // A disabled button with no explanation reads as a broken screen. Renaming it
            // turns "why can I not press this" into the actual reason, and the text goes
            // back on the next selection because the else branch always restores it.
            if (hasRow && status == "Placed" && rxState != "Clear")
                btnConfirmOrder.Text = "Rx not verified";
            else
                btnConfirmOrder.Text = "Confirm order";
        }

        private int SelectedOrderId()
        {
            // 0 is the "nothing is selected" answer. No order can have id 0 because OrderId
            // is an IDENTITY starting at 1, so every caller can test for it without needing a
            // nullable int or a second out parameter.
            if (dgvOrders.CurrentRow == null) return 0;
            // Convert.ToInt32 rather than a cast: the cell holds a boxed value whose exact
            // CLR type depends on the column type the provider chose, and an (int) cast on a
            // boxed value of any other numeric type throws at run time.
            return Convert.ToInt32(dgvOrders.CurrentRow.Cells["OrderId"].Value);
        }

        private void btnConfirmOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;   // nothing selected; the button should be disabled anyway

            // Both ids are passed. The order id says WHICH row and the session's pharmacy id
            // says it must be one of yours, so aiming this at another shop's order id simply
            // matches no row and changes nothing. Confirm returns true only when exactly one
            // row was updated, which is the database reporting what it actually did.
            if (_orders.Confirm(orderId, UserSession.PharmacyId))
            {
                MessageBox.Show("Order " + orderId + " confirmed and is ready for dispatch.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                // false means the UPDATE matched nothing. The message names the clause that
                // refused it, because the most likely cause is a prescription that was
                // approved on another screen a moment ago and is no longer approved, and an
                // unexplained "could not be confirmed" would send the owner looking at the
                // wrong screen.
                MessageBox.Show(
                    "Order " + orderId + " could not be confirmed.\r\n\r\n" +
                    "The UPDATE carries NOT EXISTS (SELECT 1 FROM Prescriptions WHERE VerifyStatus <> 'Approved'), " +
                    "so an order with an unverified prescription is refused by the database, not just by this form.",
                    "Cannot confirm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            // Reloaded on BOTH paths. After a success the row must show its new status, and
            // after a failure the screen has to be re-read from the database, because a stale
            // grid is exactly what let the attempt be made.
            LoadEverything();
        }

        private void btnDeliverOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            // No confirmation dialog: marking an order delivered is the routine end of the
            // lifecycle and prompting for every one of them would train the owner to click
            // Yes without reading. Cancel, which moves stock, does prompt.
            //
            // The return value is deliberately not read here, so a refused update, which
            // happens when the order is no longer Confirmed, produces no message. The reload
            // on the next line is what shows that the status did not change.
            _orders.MarkDelivered(orderId, UserSession.PharmacyId);
            LoadEverything();
        }

        private void btnCancelOrder_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            // Cancel is the one action that moves stock, so it asks first. The message says
            // what will happen to the units rather than only "are you sure", because the
            // restock is the part the owner cannot see on this screen.
            DialogResult answer = MessageBox.Show(
                "Cancel order " + orderId + "?\r\n\r\n" +
                "The units on this order are put back on the shelf inside the same transaction.",
                "Cancel order", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            // Tested against Yes rather than for No, so closing the dialog with the cross or
            // the Escape key also counts as "do nothing". Any answer that is not an explicit
            // Yes leaves the order alone.
            if (answer != DialogResult.Yes) return;

            // The restock and the status change happen inside one transaction in the service,
            // so the form cannot end up with the units returned and the order still open.
            _orders.Cancel(orderId, UserSession.PharmacyId);
            LoadEverything();   // the tiles change too: a cancelled order leaves the revenue total
        }

        private void btnViewInvoice_Click(object sender, EventArgs e)
        {
            int orderId = SelectedOrderId();
            if (orderId == 0) return;

            // using, so the dialog's window handles are released as soon as it closes rather
            // than waiting for a collection. ShowDialog(this) both blocks until the invoice is
            // closed and names this form as the owner, which keeps the invoice in front of the
            // dashboard instead of disappearing behind it.
            using (InvoiceForm invoice = new InvoiceForm(orderId))
            {
                invoice.ShowDialog(this);
            }
            // No reload afterwards: an invoice is read only, so nothing behind it can have changed.
        }

        private void dgvLowStock_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // double clicking the header must not open an editor
            int medicineId = Convert.ToInt32(dgvLowStock.Rows[e.RowIndex].Cells["MedicineId"].Value);

            // Double click is the shortcut the panel title advertises. The second argument
            // tells the editor this was opened to restock, so it puts the caret in the stock
            // box rather than making the owner find it.
            using (MedicineEditorForm editor = new MedicineEditorForm(medicineId, true))
            {
                editor.ShowDialog(this);
            }
            // The editor may have changed the stock, which moves the low stock count, the
            // alert grid and the tile above it, so the whole dashboard is re-read.
            LoadEverything();
        }

        // ---------------------------------------------------------------------
        //  NAVIGATION
        // ---------------------------------------------------------------------

        private void OpenChild(Form child)
        {
            // One method for every sidebar destination, so the open-modally-then-refresh
            // pattern exists once instead of eight times. using disposes the child form after
            // it closes; ShowDialog blocks, so the reload below runs only once the user is
            // finished with it.
            using (child)
            {
                child.ShowDialog(this);
            }
            // Any child screen may have changed something this dashboard reports: adding a
            // medicine, restocking, verifying a prescription. Refreshing unconditionally on
            // return is cheaper than working out which screens matter, and it means the
            // figures can never be left describing the state before the child was opened.
            LoadEverything();
        }

        // Each sidebar button is a one line expression body because the whole behaviour is
        // "open this form modally and then refresh". None of them passes a pharmacy id: every
        // child form reads UserSession.PharmacyId itself, so there is no route by which a
        // caller could hand a child the wrong shop.
        private void btnMedicines_Click(object sender, EventArgs e) => OpenChild(new AdminMedicineForm());
        private void btnInventory_Click(object sender, EventArgs e) => OpenChild(new AdminInventoryForm());
        private void btnPrescriptions_Click(object sender, EventArgs e) => OpenChild(new VerifyPrescriptionForm());
        private void btnEarnings_Click(object sender, EventArgs e) => OpenChild(new AdminEarningsForm());
        private void btnOffers_Click(object sender, EventArgs e) => OpenChild(new DiscountOffersForm());
        private void btnReviews_Click(object sender, EventArgs e) => OpenChild(new AdminReviewsForm());
        private void btnShopProfile_Click(object sender, EventArgs e) => OpenChild(new PharmacyProfileForm());
        private void btnMyProfile_Click(object sender, EventArgs e) => OpenChild(new MyProfileForm());
        private void btnRefresh_Click(object sender, EventArgs e) => LoadEverything();
        // Only the order grid is reloaded when the filter changes, not the tiles: the tiles
        // count every order regardless of status, so re-running those queries would cost four
        // round trips to display exactly the same four numbers.
        private void cmbOrderStatus_SelectedIndexChanged(object sender, EventArgs e) { if (!_loading) LoadOrders(); }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            // Logging out is one click next to seven navigation buttons, so it asks first.
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                // Clear BEFORE Close. The session is static and lives as long as the process,
                // so leaving PharmacyId set would carry this shop's scope into whoever logs in
                // next. LoginForm also clears it in its FormClosed handler, which is the one
                // place that catches every way a dashboard can end; doing it here as well
                // costs nothing and does not depend on that handler being wired.
                UserSession.Clear();
                // Closing this form does not end the application. LoginForm hid itself when it
                // opened this dashboard and subscribed to its FormClosed event, so closing
                // here brings the login screen back rather than stopping the message loop.
                Close();
            }
        }
    }
}
