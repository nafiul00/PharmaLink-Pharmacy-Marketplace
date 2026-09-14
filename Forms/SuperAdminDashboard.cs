using System.Data;                  // DataTable, the shape every service read hands back
using System.Drawing;               // Color, Font, Point and Size, needed because the tiles are built in code
using System.Windows.Forms;         // Form, Label, Panel, DataGridView and MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the one palette and control style the whole application shares
using PharmaLinkApp.Services;       // PharmacyService and ReportService, the only route this form has to the data

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by LoginForm when the Users row says
    //  UserType = 'SuperAdmin'. Uses ReportService for the figures and
    //  PharmacyService for the approval queue.
    //
    //  Load order:
    //      SuperAdminDashboard_Load -> ApplyTheme -> BuildTiles -> LoadEverything
    //
    //  LoadEverything is the only method that reads data, and every action on
    //  this screen finishes by calling it again. That is deliberate: there is
    //  exactly one refresh path, so no button can leave the tiles agreeing with
    //  a grid that has since changed underneath them.
    //
    //  There is no SQL in this file. UserSession is resolved from the enclosing
    //  PharmaLinkApp namespace rather than from a using directive, because it
    //  sits at the root of the project beside Program.cs.
    // -------------------------------------------------------------------------

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
        // One service object per form, created once and reused by every handler below.
        // readonly says the reference is never reassigned after construction, which is
        // what stops a later edit from quietly pointing _reports at a second instance.
        // Neither service holds a connection of its own, so keeping them alive for the
        // lifetime of the form costs nothing: DbHelper opens and closes a connection
        // inside each individual call.
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly ReportService _reports = new ReportService();

        // The four Labels that hold the tile NUMBERS, handed back by UiTheme.BuildTile
        // through out parameters. They are kept in fields rather than looked up from
        // Controls by name, so refreshing the dashboard is four text assignments instead
        // of a search through the control tree, and a typo in a control name becomes a
        // compile error rather than a run time one.
        private Label _tilePharmacies;
        private Label _tileCustomers;
        private Label _tileOrders;
        private Label _tileCommission;

        public SuperAdminDashboard()
        {
            // InitializeComponent is the designer generated method that creates every
            // control and sets its position. Nothing else belongs in a constructor here:
            // reading the database at construction time would run before the window has
            // a handle, so a failure would have nowhere to show a message.
            InitializeComponent();
        }

        private void SuperAdminDashboard_Load(object sender, EventArgs e)
        {
            // Load fires once, after the window exists but before it is painted, which is
            // the right moment for all three of these. Styling first so the user never
            // sees the default grey, then the tiles, then the data that fills them.
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
            // BuildTile returns the Panel and, through the out parameter, the Label inside
            // it that holds the number. Taking both back in one call is what lets the tile
            // be styled once here and refreshed cheaply for the rest of the session.
            //
            // The colour is the only thing that differs between the four, and each one is
            // a named UiTheme constant rather than a literal Color, so the stripe on this
            // screen matches the button of the same meaning everywhere else in the product.
            Panel t1 = UiTheme.BuildTile("APPROVED PHARMACIES", UiTheme.Primary, out _tilePharmacies);
            Panel t2 = UiTheme.BuildTile("REGISTERED CUSTOMERS", UiTheme.Accent, out _tileCustomers);
            Panel t3 = UiTheme.BuildTile("ORDERS PLACED", UiTheme.Success, out _tileOrders);
            Panel t4 = UiTheme.BuildTile("COMMISSION EARNED", UiTheme.Warning, out _tileCommission);

            // 250 is where the content column starts: the sidebar is 230 wide, so this
            // clears it with a 20 pixel gutter and lines the tiles up with the grid
            // headings below them, which the designer also places at x = 250.
            int x = 250;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                // One call replaces four lines. PlaceTile sets the location and size,
                // adds the tile to the FORM rather than to a container, and brings it to
                // the front, because the designer's own controls were added first and
                // would otherwise paint over it. Centralised in UiTheme so every
                // dashboard places its tiles the same way on a scaled display.
                UiTheme.PlaceTile(this, tile, x, 88, 236, 84);
                x += 250;
            }
        }

        // ---------------------------------------------------------------------
        //  DATA
        // ---------------------------------------------------------------------

        private void LoadEverything()
        {
            // An hourglass for the whole method, because everything below is a round trip
            // to SQL Server and a window that does not acknowledge the click reads as
            // frozen. It is restored in the finally block so it cannot be left behind.
            Cursor = Cursors.WaitCursor;
            try
            {
                // Six figures, ONE database round trip. GetPlatformTotals runs a single
                // SELECT containing six scalar subqueries rather than six separate
                // queries, and hands the results back through out parameters because a
                // method can only return one value. The alternative - a small class or
                // a tuple - would be tidier OOP, and that is a fair criticism to accept
                // if it is raised.
                //
                // out parameters must be declared before the call, and unlike ordinary
                // locals they need no initial value: the compiler proves the method
                // assigns all six before it returns.
                int pharmacies, pending, customers, orders;
                decimal revenue, commission;
                _reports.GetPlatformTotals(out pharmacies, out pending, out customers,
                                           out orders, out revenue, out commission);

                // Tiles are Labels created in BuildTiles() and kept in fields, so
                // refreshing the dashboard only rewrites their text instead of
                // rebuilding the whole panel.
                _tilePharmacies.Text = pharmacies.ToString();
                _tileCustomers.Text = customers.ToString();
                _tileOrders.Text = orders.ToString();
                _tileCommission.Text = UiTheme.Money(commission);   // formats as "Tk 1,234.00"

                // The pending COUNT is shown here in the subtitle rather than as a fifth
                // tile, because it is a call to action rather than a headline figure, and
                // it came back from the same single query as the four above it.
                lblHeaderSub.Text = "Gross platform revenue " + UiTheme.Money(revenue) +
                                    "   |   " + pending + " pharmacy registration(s) waiting for approval";

                // DataSource does the whole binding: the DataGridView creates one column
                // per column of the DataTable and one row per row. That is why the header
                // texts have to be fixed AFTERWARDS, in the method on the next line, and
                // not before - the columns do not exist until this assignment runs.
                dgvPending.DataSource = _pharmacies.GetPending();
                LabelPendingColumns();

                // The same low rated query the dedicated report form runs, called here
                // with the two fixed defaults so the dashboard needs no controls of its
                // own: an average below 2.5, over at least 2 reviews. The thresholds are
                // arguments rather than constants inside the SQL, which is what lets the
                // report form offer them as spinners without a second copy of the query.
                dgvLowRated.DataSource = _reports.GetLowRatedPharmacies(2.5m, 2);
                LabelLowRatedColumns();

                // Approve and Reject act on the CURRENT row, so with an empty queue there
                // is nothing for them to act on. Disabling both is better than letting
                // them be pressed and then explaining that nothing was selected.
                bool hasPending = dgvPending.Rows.Count > 0;
                btnApprove.Enabled = hasPending;
                btnReject.Enabled = hasPending;
                // An empty queue is good news, so it is worded as such rather than left as
                // a blank grid the operator has to interpret.
                lblPendingTitle.Text = hasPending
                    ? "Pharmacies waiting for approval  (" + dgvPending.Rows.Count + ")"
                    : "Pharmacies waiting for approval  -  none right now";
            }
            catch (Exception ex)
            {
                // DbHelper has already turned any SqlException into a readable sentence by
                // the time it arrives here, so ex.Message is safe to show. Catching at the
                // whole-method level is right for a dashboard: if any part of the load
                // failed the screen is not trustworthy, and there is nothing useful to do
                // with a half filled set of tiles.
                MessageBox.Show("Could not load the dashboard.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // finally, not the end of try: an exception must not leave the operator
                // looking at a permanent hourglass on a window that is actually usable.
                Cursor = Cursors.Default;
            }
        }

        private void LabelPendingColumns()
        {
            // Guard first. Binding an empty DataTable still produces columns, but a failed
            // load leaves none at all, and indexing Columns["PharmacyId"] on an empty
            // collection throws. One early return covers that for the whole method.
            if (dgvPending.Columns.Count == 0) return;
            // The grid is bound to a DataTable, so the column keys are the SQL aliases from
            // PharmacyService.GetPending. Renaming a column in that query without changing
            // the string here would throw at run time, which is the cost of binding by name
            // and the reason the aliases in the service are left alone once written.
            dgvPending.Columns["PharmacyId"].HeaderText = "ID";
            // FillWeight is a proportion, not a pixel width. AutoSizeColumnsMode is Fill for
            // every grid in the application, so the columns share the width in these ratios
            // and the layout survives the window being resized. An ID needs far less room
            // than a pharmacy name, hence 30 against the default 100.
            dgvPending.Columns["PharmacyId"].FillWeight = 30;
            dgvPending.Columns["PharmacyName"].HeaderText = "Pharmacy";
            dgvPending.Columns["OwnerName"].HeaderText = "Owner";
            // The database column is LicenseNo, but the operator knows it as the DGDA
            // licence, so the heading uses their vocabulary rather than the schema's.
            dgvPending.Columns["LicenseNo"].HeaderText = "DGDA licence";
            dgvPending.Columns["Area"].HeaderText = "Area";
            dgvPending.Columns["ContactPhone"].HeaderText = "Contact";
            dgvPending.Columns["RegisteredAt"].HeaderText = "Applied on";
        }

        private void LabelLowRatedColumns()
        {
            // Same guard and the same reasoning as above, on the second grid.
            if (dgvLowRated.Columns.Count == 0) return;
            dgvLowRated.Columns["PharmacyId"].HeaderText = "ID";
            dgvLowRated.Columns["PharmacyId"].FillWeight = 30;
            dgvLowRated.Columns["PharmacyName"].HeaderText = "Pharmacy";
            dgvLowRated.Columns["Area"].HeaderText = "Area";
            // OwnerName and OwnerPhone are aliases the low rated query builds by joining
            // Users to Pharmacies. They are carried so the operator can telephone the shop
            // about its rating without leaving this screen to look the number up.
            dgvLowRated.Columns["OwnerName"].HeaderText = "Owner";
            dgvLowRated.Columns["OwnerPhone"].HeaderText = "Owner phone";
            // TotalReviews is the COUNT the HAVING clause tested. Showing it matters: it is
            // the evidence that the average beside it rests on more than one opinion.
            dgvLowRated.Columns["TotalReviews"].HeaderText = "Reviews";
            dgvLowRated.Columns["AverageRating"].HeaderText = "Avg rating";
            dgvLowRated.Columns["Status"].HeaderText = "Status";
        }

        /// <summary>Poor performers are tinted red through the CellFormatting event.</summary>
        private void dgvLowRated_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // CellFormatting fires per cell as the grid paints, including for the header
            // row, where RowIndex is -1. Indexing Rows[-1] would throw, so the guard is not
            // optional defensiveness - it is the documented contract of this event.
            if (e.RowIndex < 0) return;
            // Every row in THIS grid is by definition a low rated shop, because the HAVING
            // clause already filtered them, so the whole grid is tinted with no condition
            // to test. Setting the row's DefaultCellStyle rather than e.CellStyle colours
            // the entire row from one cell's event instead of waiting for all nine.
            dgvLowRated.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        // ---------------------------------------------------------------------
        //  ACTIONS ON THE PENDING QUEUE
        // ---------------------------------------------------------------------

        private int SelectedPendingPharmacyId()
        {
            // 0 is the "nothing selected" answer, and it is safe to use as one because
            // PharmacyId is an IDENTITY column that starts at 1, so no real shop can ever
            // have it. That is what lets the callers test a single int instead of being
            // handed a nullable value to unwrap.
            if (dgvPending.CurrentRow == null) return 0;
            // The cell value arrives boxed as object out of the DataTable, so it has to be
            // converted. Convert.ToInt32 rather than a cast, because the underlying type is
            // whatever the provider chose for a SQL int and a hard cast would depend on it.
            return Convert.ToInt32(dgvPending.CurrentRow.Cells["PharmacyId"].Value);
        }

        private void btnApprove_Click(object sender, EventArgs e)
        {
            // Re-read the selection at the moment of the click rather than tracking it in a
            // field. The row under the cursor is the only thing that can have changed since
            // the grid was loaded, and this is the one authoritative place to read it.
            int pharmacyId = SelectedPendingPharmacyId();
            if (pharmacyId == 0)
            {
                // The button is already disabled on an empty queue, so this catches the
                // other case: rows exist but the grid has no current row.
                MessageBox.Show("Select a pharmacy first.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Both values are read from the grid rather than re-queried, because they are
            // only used to build the confirmation text. The decision itself travels as the
            // id alone, so a stale name on screen cannot approve the wrong shop.
            string name = dgvPending.CurrentRow.Cells["PharmacyName"].Value.ToString();
            string licence = dgvPending.CurrentRow.Cells["LicenseNo"].Value.ToString();

            // The prompt states the consequence in full, including the licence number,
            // because approval is the moment a real pharmacy is allowed to sell medicine
            // to the public and the operator should be checking that licence, not the name.
            DialogResult answer = MessageBox.Show(
                "Approve " + name + "?\r\n\r\nDGDA licence: " + licence + "\r\n\r\n" +
                "The pharmacy becomes Approved, the owner's account becomes Active and can log in, " +
                "and the shop's medicines become visible to customers.",
                "Approve pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Testing for anything other than Yes, rather than testing for No, so closing
            // the dialog with the window control also counts as a refusal.
            if (answer != DialogResult.Yes) return;

            // Approve runs two UPDATEs in one transaction: Pharmacies.Status becomes
            // 'Approved' and the owner's Users.Status becomes 'Active'. It returns true
            // when rows were affected, so nothing is announced that did not happen.
            if (_pharmacies.Approve(pharmacyId))
            {
                MessageBox.Show(name + " has been approved.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                // Reload rather than removing the row by hand. The approved shop leaves the
                // pending queue, the approved pharmacies tile goes up by one and the header
                // count goes down by one, and one re-read keeps all three consistent.
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

            // The wording explains the design rather than just asking for confirmation: a
            // rejected registration is held at Suspended, never deleted. Deleting would
            // free the licence number for re-registration and lose the fact that this
            // application was ever refused, so the row stays and the decision is reversible.
            DialogResult answer = MessageBox.Show(
                "Reject " + name + "?\r\n\r\nThe registration is held at Suspended rather than deleted, " +
                "so the licence number stays taken and the decision can be reversed.",
                "Reject registration", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            // PharmacyService.Reject simply calls Suspend, because rejecting a new
            // application and suspending a trading shop end in the same database state.
            // One method means the two paths cannot drift apart later.
            if (_pharmacies.Reject(pharmacyId))
            {
                // No success box here, unlike Approve. Rejection is visible in the queue
                // emptying, and a confirmation dialog for a refusal adds a click without
                // adding information.
                LoadEverything();
            }
        }

        private void dgvLowRated_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // Double clicking the header row would otherwise open a form for nothing.
            if (e.RowIndex < 0) return;
            // Read the id from the ROW THAT WAS CLICKED, using e.RowIndex, rather than from
            // CurrentRow. They are normally the same, but taking it from the event argument
            // is what guarantees the form that opens is the shop the operator pointed at.
            int pharmacyId = Convert.ToInt32(dgvLowRated.Rows[e.RowIndex].Cells["PharmacyId"].Value);
            // Passing the id into the constructor is what pre-selects the record on the
            // other screen, so the operator lands on the offending shop with its Suspend
            // button already live instead of having to search for it again.
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
            // using() disposes the child form and its window handle even if it throws,
            // which matters here because this method is called from seven different
            // buttons and a leak would accumulate over a session.
            using (child)
            {
                // ShowDialog, not Show. Modal means execution stops on this line until the
                // child closes, so the line after it is guaranteed to run afterwards rather
                // than immediately. Passing "this" as the owner also keeps the child in
                // front of the dashboard instead of behind it.
                child.ShowDialog(this);
            }
            // The child may have approved a shop, changed a commission rate or hidden a
            // review, so the dashboard behind it is now stale. Refreshing here, once, means
            // no child form has to know that a dashboard exists or how to update it.
            LoadEverything();
        }

        // Expression bodied members for the seven menu handlers, because each one really is
        // a single expression. Every child is constructed fresh on each click rather than
        // being cached in a field, so a form that was closed and reopened cannot show data
        // it loaded minutes ago.
        private void btnManagePharmacies_Click(object sender, EventArgs e) => OpenChild(new SuperAdminManageShopsForm(0));
        private void btnManageUsers_Click(object sender, EventArgs e) => OpenChild(new SuperAdminManageUsersForm());
        private void btnCategories_Click(object sender, EventArgs e) => OpenChild(new ManageCategoriesForm());
        private void btnSalesReport_Click(object sender, EventArgs e) => OpenChild(new SuperAdminSalesReportForm());
        private void btnLowRated_Click(object sender, EventArgs e) => OpenChild(new SuperAdminLowRatedShopsForm());
        private void btnModerateReviews_Click(object sender, EventArgs e) => OpenChild(new ModerateReviewsForm());
        // Refresh alone skips OpenChild: there is no child to open, only the reload.
        private void btnRefresh_Click(object sender, EventArgs e) => LoadEverything();

        private void btnLogout_Click(object sender, EventArgs e)
        {
            // Logging out is confirmed because a stray click on a menu button would
            // otherwise end the session and lose whichever queue was on screen.
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                // Clear the identity BEFORE closing. UserSession is static and outlives this
                // form, so leaving it populated would let a stale UserId or PharmacyId be
                // read by whatever opens next.
                UserSession.Clear();
                Close();      // LoginForm is watching FormClosed and shows itself again
            }
        }
    }
}
