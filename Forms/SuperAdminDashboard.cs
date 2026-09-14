using System.Data;                  // DataTable, the shape every service read hands back
using System.Drawing;               // Color, Font, Point and Size, because the tiles are built in code
using System.Windows.Forms;         // Form, Label, Panel, DataGridView and MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the one palette the whole application shares
using PharmaLinkApp.Services;       // PharmacyService and ReportService, this form's only route to data

namespace PharmaLinkApp.Forms   // UserSession sits one level up, beside Program.cs
{
    // Presentation layer: no SQL here. Opened by LoginForm for UserType 'SuperAdmin'.

    /// <summary>The platform operator's hub (requirements 1 to 9).</summary>
    public partial class SuperAdminDashboard : Form
    {
        private readonly PharmacyService _pharmacies = new PharmacyService();   // one service per form, reused by every handler
        private readonly ReportService _reports = new ReportService();   // kept beside it so both are created once per window

        // The four tile NUMBER labels, kept in fields so a refresh is four assignments.
        private Label _tilePharmacies;   // the approved pharmacies figure
        private Label _tileCustomers;   // the registered customers figure
        private Label _tileOrders;   // the orders placed figure
        private Label _tileCommission;   // the commission earned figure

        // Nothing that can fail belongs here; that goes in Load, where a window exists.
        public SuperAdminDashboard()
        {
            InitializeComponent();   // designer-generated: creates every control and places it
        }

        // Wired to the form's Load event by the designer, so it runs once per instance.
        private void SuperAdminDashboard_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // styling first, so the user never sees the default grey
            BuildTiles();   // then the tiles, which must exist before anything can write into them
            LoadEverything();   // and only then the data, so nothing is written to a missing control
        }

        // All the styling in one place, so a palette change is a single edit here.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Super Admin");   // title, icon and background in one shared call

            panelSide.BackColor = UiTheme.Sidebar;   // the dark rail behind the menu
            lblBrand.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);   // Semibold at 15 reads as a logo, not a caption
            lblBrand.ForeColor = Color.White;   // the only colour with enough contrast on the dark rail
            lblRole.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);   // small and bold, so it reads as a label
            lblRole.ForeColor = Color.FromArgb(140, 205, 185);   // a muted green, legible without competing with the brand
            lblUserName.Font = UiTheme.FontSmall;   // the signed-in name shares the small font with the role
            lblUserName.ForeColor = Color.FromArgb(190, 205, 216);   // a soft grey-blue: quiet until looked for
            lblUserName.Text = UserSession.FullName;   // read from UserSession, so it is whoever logged in

            foreach (Button button in new[] { btnManagePharmacies, btnManageUsers, btnCategories,   // one array, so no button is styled differently by accident
                                              btnSalesReport, btnLowRated, btnModerateReviews })   // the order here is the order on screen
            {
                UiTheme.StyleSidebarButton(button);   // one shared style, so a new item only joins the array
            }

            UiTheme.StyleSidebarButton(btnLogout);   // the same base style first, so it sits on the same grid
            btnLogout.BackColor = UiTheme.Danger;   // then red, because logout is the destructive item
            btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 60, 60);   // darker red on hover, acknowledging the pointer

            panelHeader.BackColor = UiTheme.Primary;   // the header band picks up the brand colour
            lblHeaderTitle.Font = UiTheme.FontTitle;   // the largest font in the theme, used once per screen
            lblHeaderTitle.ForeColor = Color.White;   // white, because the band behind it is the brand colour
            lblHeaderSub.Font = UiTheme.FontSmall;   // the subtitle carries figures, not identity
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);   // a pale green that sits on the band without shouting
            UiTheme.StyleSecondary(btnRefresh);   // secondary, since Refresh only repeats what Load did

            lblPendingTitle.Font = UiTheme.FontHeading;   // section headings share one font, so the panels read as equals
            lblPendingTitle.ForeColor = UiTheme.TextDark;   // near-black on white, the body colour of the application
            lblPendingHint.Font = UiTheme.FontSmall;   // the hint is one step down in size
            lblPendingHint.ForeColor = UiTheme.TextMuted;   // and one step down in contrast

            lblLowRatedTitle.Font = UiTheme.FontHeading;   // the low rated heading, styled identically
            lblLowRatedTitle.ForeColor = UiTheme.TextDark;   // so neither panel looks more important
            lblLowRatedHint.Font = UiTheme.FontSmall;   // its hint matches too
            lblLowRatedHint.ForeColor = UiTheme.TextMuted;   // keeping both panels visually parallel

            UiTheme.StyleGrid(dgvPending);   // centralised, so every table scrolls and selects alike
            UiTheme.StyleGrid(dgvLowRated);   // the second grid gets exactly the same treatment
            dgvLowRated.CellFormatting += dgvLowRated_CellFormatting;   // wired in code, beside the styling it belongs with

            UiTheme.StyleSuccess(btnApprove);   // green, because Approve is the affirmative action
            UiTheme.StyleDanger(btnReject);   // red, so the colour carries the meaning before the label
            UiTheme.StyleSecondary(btnOpenPharmacies);   // neutral, because opening a list is neither
        }

        /// <summary>The four headline figures, built in code.</summary>
        private void BuildTiles()
        {
            // BuildTile returns the Panel and, through out, the Label holding the number.
            Panel t1 = UiTheme.BuildTile("APPROVED PHARMACIES", UiTheme.Primary, out _tilePharmacies);
            Panel t2 = UiTheme.BuildTile("REGISTERED CUSTOMERS", UiTheme.Accent, out _tileCustomers);   // Accent, so customers read as a different measure
            Panel t3 = UiTheme.BuildTile("ORDERS PLACED", UiTheme.Success, out _tileOrders);   // Success green: orders mean the platform is working
            Panel t4 = UiTheme.BuildTile("COMMISSION EARNED", UiTheme.Warning, out _tileCommission);   // Warning amber, the figure watched most closely

            int x = 250;   // the sidebar is 230 wide, so this clears it with a 20 pixel gutter
            foreach (Panel tile in new[] { t1, t2, t3, t4 })   // an array literal, so the rule below is written once
            {
                UiTheme.PlaceTile(this, tile, x, 88, 236, 84);   // sets bounds, adds to the form, brings to front
                x += 250;   // 236 wide plus a 14 pixel gap, which ends the row flush with the grids
            }
        }

        // ------------------------------  DATA  ------------------------------

        // The single refresh path; every button on this screen ends by calling it.
        private void LoadEverything()
        {
            Cursor = Cursors.WaitCursor;   // everything below is a round trip, so acknowledge the click
            try   // a half read dashboard is worse than an error, so the whole load is guarded
            {
                // Six figures, ONE round trip: one SELECT of six scalar subqueries.
                int pharmacies, pending, customers, orders;
                decimal revenue, commission;   // the two money figures, separate only because the type differs
                _reports.GetPlatformTotals(out pharmacies, out pending, out customers,   // one call fills all six
                                           out orders, out revenue, out commission);   // in the order the method declares them

                _tilePharmacies.Text = pharmacies.ToString();   // a refresh only rewrites the tile text
                _tileCustomers.Text = customers.ToString();   // plain ToString: counts are whole numbers
                _tileOrders.Text = orders.ToString();   // the same, so all three count tiles follow one rule
                _tileCommission.Text = UiTheme.Money(commission);   // formats as "Tk 1,234.00"

                // The pending count is a call to action, so it goes in the subtitle.
                lblHeaderSub.Text = "Gross platform revenue " + UiTheme.Money(revenue) +
                                    "   |   " + pending + " pharmacy registration(s) waiting for approval";   // worded as work waiting, which is what it is

                dgvPending.DataSource = _pharmacies.GetPending();   // binding creates the columns
                LabelPendingColumns();   // immediately after the bind, because that is when the columns exist

                dgvLowRated.DataSource = _reports.GetLowRatedPharmacies(2.5m, 2);   // below 2.5 over at least 2 reviews
                LabelLowRatedColumns();   // the same pattern on the second grid, for the same reason

                bool hasPending = dgvPending.Rows.Count > 0;   // Approve and Reject act on the current row
                btnApprove.Enabled = hasPending;   // disabled rather than pressed and then explained away
                btnReject.Enabled = hasPending;   // and Reject with it, since both act on the same row
                // An empty queue is good news, so it is worded rather than left blank.
                lblPendingTitle.Text = hasPending
                    ? "Pharmacies waiting for approval  (" + dgvPending.Rows.Count + ")"   // the size of the queue, without counting rows
                    : "Pharmacies waiting for approval  -  none right now";   // reassurance, not a blank grid
            }
            catch (Exception ex)   // caught by base type because every failure here has one remedy
            {
                // DbHelper has already turned a SqlException into a readable sentence.
                MessageBox.Show("Could not load the dashboard.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // OK only: there is nothing to decide
            }
            finally   // so the cursor is restored on the success and failure paths alike
            {
                Cursor = Cursors.Default;   // an exception must not leave a permanent hourglass
            }
        }

        // Headers are set in code because the columns only exist once DataSource is set.
        private void LabelPendingColumns()
        {
            if (dgvPending.Columns.Count == 0) return;   // a failed load leaves no columns to index
            dgvPending.Columns["PharmacyId"].HeaderText = "ID";   // the keys are the SQL aliases from GetPending
            dgvPending.Columns["PharmacyId"].FillWeight = 30;   // a proportion, not pixels; an ID needs less room
            dgvPending.Columns["PharmacyName"].HeaderText = "Pharmacy";   // the trading name the operator scans
            dgvPending.Columns["OwnerName"].HeaderText = "Owner";   // an alias built by joining Users
            dgvPending.Columns["LicenseNo"].HeaderText = "DGDA licence";   // the operator's vocabulary, not the schema's
            dgvPending.Columns["Area"].HeaderText = "Area";   // the district, which says whether a shop is needed
            dgvPending.Columns["ContactPhone"].HeaderText = "Contact";   // the number to ring to check the licence
            dgvPending.Columns["RegisteredAt"].HeaderText = "Applied on";   // makes a queue left to grow visible
        }

        // The same job for the second grid; the two grids share no column names.
        private void LabelLowRatedColumns()
        {
            if (dgvLowRated.Columns.Count == 0) return;   // the same guard and reasoning as above
            dgvLowRated.Columns["PharmacyId"].HeaderText = "ID";   // the same short heading as the pending grid
            dgvLowRated.Columns["PharmacyId"].FillWeight = 30;   // an id is the narrowest thing on the row
            dgvLowRated.Columns["PharmacyName"].HeaderText = "Pharmacy";   // the shop being complained about
            dgvLowRated.Columns["Area"].HeaderText = "Area";   // so a pattern across one area is visible
            dgvLowRated.Columns["OwnerName"].HeaderText = "Owner";   // an alias from joining Users to Pharmacies
            dgvLowRated.Columns["OwnerPhone"].HeaderText = "Owner phone";   // the whole reason that join is there
            dgvLowRated.Columns["TotalReviews"].HeaderText = "Reviews";   // the COUNT the HAVING clause tested
            dgvLowRated.Columns["AverageRating"].HeaderText = "Avg rating";   // rounded to two places by the query
            dgvLowRated.Columns["Status"].HeaderText = "Status";   // so a suspended shop is not suspended twice
        }

        /// <summary>Low rated rows are tinted red as the grid paints.</summary>
        private void dgvLowRated_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;   // the event fires for the header row too, where RowIndex is -1
            // Every row here is already low rated, so the whole grid is tinted.
            dgvLowRated.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        // --------------------  ACTIONS ON THE PENDING QUEUE  --------------------

        // One helper, so Approve and Reject cannot disagree on 'the selected pharmacy'.
        private int SelectedPendingPharmacyId()
        {
            if (dgvPending.CurrentRow == null) return 0;   // 0 is safe: PharmacyId is an IDENTITY from 1
            // Convert.ToInt32, not a cast: the boxed type is whatever the provider chose.
            return Convert.ToInt32(dgvPending.CurrentRow.Cells["PharmacyId"].Value);
        }

        // Approval feels irreversible, so this confirms with the licence number first.
        private void btnApprove_Click(object sender, EventArgs e)
        {
            int pharmacyId = SelectedPendingPharmacyId();   // read at click time, not tracked in a field
            if (pharmacyId == 0)   // 0 is the no-selection answer, never a real PharmacyId
            {
                // The button is already disabled on an empty queue; this catches no current row.
                MessageBox.Show("Select a pharmacy first.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);   // information: nothing was attempted
                return;   // return, so the handler ends without touching the database
            }

            string name = dgvPending.CurrentRow.Cells["PharmacyName"].Value.ToString();   // used only for the prompt text
            string licence = dgvPending.CurrentRow.Cells["LicenseNo"].Value.ToString();   // the thing actually being checked

            // The prompt states the consequence in full, so the licence gets checked.
            DialogResult answer = MessageBox.Show(
                "Approve " + name + "?\r\n\r\nDGDA licence: " + licence + "\r\n\r\n" +   // the licence is in the first line, not buried
                "The pharmacy becomes Approved, the owner's account becomes Active and can log in, " +   // approval also unlocks the owner's login
                "and the shop's medicines become visible to customers.",   // the customer-facing consequence
                "Approve pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // a decision, not a warning

            // Testing for anything but Yes, so closing the dialog also counts as a refusal.
            if (answer != DialogResult.Yes) return;

            // Approve runs two UPDATEs in one transaction and returns true on rows affected.
            if (_pharmacies.Approve(pharmacyId))
            {
                MessageBox.Show(name + " has been approved.", "PharmaLink",   // named, so the right shop is visibly acted on
                    MessageBoxButtons.OK, MessageBoxIcon.Information);   // OK only: the decision is already made
                LoadEverything();   // one re-read keeps queue, tile and header count consistent
            }
        }

        // Rejection is the mirror image, and shorter: it changes less and is reversible.
        private void btnReject_Click(object sender, EventArgs e)
        {
            int pharmacyId = SelectedPendingPharmacyId();   // the same helper Approve uses
            if (pharmacyId == 0)   // and the same guard, because the empty-grid case is identical
            {
                MessageBox.Show("Select a pharmacy first.", "PharmaLink",   // the same wording, so one situation reads one way
                    MessageBoxButtons.OK, MessageBoxIcon.Information);   // information, since nothing was attempted
                return;   // return without touching the database
            }

            string name = dgvPending.CurrentRow.Cells["PharmacyName"].Value.ToString();   // the prompt does not repeat the licence

            // A rejected registration is held at Suspended, never deleted.
            DialogResult answer = MessageBox.Show(
                "Reject " + name + "?\r\n\r\nThe registration is held at Suspended rather than deleted, " +   // 'reject' sounds more final than it is
                "so the licence number stays taken and the decision can be reversed.",   // says why the record stays
                "Reject registration", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);   // a warning: this refuses a real business

            if (answer != DialogResult.Yes) return;   // anything but Yes is a refusal, closing included

            // Reject simply calls Suspend, so the two paths cannot drift apart later.
            if (_pharmacies.Reject(pharmacyId))
            {
                LoadEverything();   // no success box: the queue emptying is the confirmation
            }
        }

        // Double click, because this panel is a reading surface: 'take me to this shop'.
        private void dgvLowRated_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // a double click on the header would open a form for nothing
            // Read from the ROW THAT WAS CLICKED, so the form matches what was pointed at.
            int pharmacyId = Convert.ToInt32(dgvLowRated.Rows[e.RowIndex].Cells["PharmacyId"].Value);
            OpenChild(new SuperAdminManageShopsForm(pharmacyId));   // the id pre-selects the shop on the other screen
        }

        // ------------------------------  NAVIGATION  ------------------------------

        /// <summary>Opens a child modally, then refreshes the dashboard.</summary>
        private void OpenChild(Form child)
        {
            using (child)   // disposes the child and its handle even if it throws
            {
                child.ShowDialog(this);   // modal, so the next line runs only once the child closes
            }
            LoadEverything();   // the child may have changed data, so refresh here, once
        }

        // Each child is constructed fresh per click, so none can show minutes-old data.
        private void btnManagePharmacies_Click(object sender, EventArgs e) => OpenChild(new SuperAdminManageShopsForm(0));
        private void btnManageUsers_Click(object sender, EventArgs e) => OpenChild(new SuperAdminManageUsersForm());   // unscoped: the operator manages every role
        private void btnCategories_Click(object sender, EventArgs e) => OpenChild(new ManageCategoriesForm());   // shared by every pharmacy on the platform
        private void btnSalesReport_Click(object sender, EventArgs e) => OpenChild(new SuperAdminSalesReportForm());   // opens with its own date and area filters
        private void btnLowRated_Click(object sender, EventArgs e) => OpenChild(new SuperAdminLowRatedShopsForm());   // the full version of the panel on this screen
        private void btnModerateReviews_Click(object sender, EventArgs e) => OpenChild(new ModerateReviewsForm());   // the only screen that can hide a review
        // Refresh alone skips OpenChild: there is no child to open, only the reload.
        private void btnRefresh_Click(object sender, EventArgs e) => LoadEverything();

        // The only handler that touches static state, so the only one to clear it.
        private void btnLogout_Click(object sender, EventArgs e)
        {
            // Confirmed, because a stray click would otherwise end the session.
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // YesNo, so closing the dialog keeps the session

            if (answer == DialogResult.Yes)   // only an explicit Yes ends the session
            {
                UserSession.Clear();   // static and outlives the form, so clear it BEFORE closing
                Close();      // LoginForm is watching FormClosed and shows itself again
            }
        }
    }
}
