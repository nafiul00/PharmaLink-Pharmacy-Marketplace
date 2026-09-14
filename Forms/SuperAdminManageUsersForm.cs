using System.Data;                  // DataTable, the shape AuthService.SearchUsers returns
using System.Drawing;               // Color, for the status colour coding in CellFormatting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette and control styles
using PharmaLinkApp.Services;       // AuthService, which owns both the search and the status update

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by SuperAdminDashboard. Uses AuthService for
    //  the grid (SearchUsers) and for the only write on this screen
    //  (SetUserStatus). It opens no connection itself.
    //
    //  Flow:
    //      Load -> ApplyTheme -> fill both combo boxes -> _loading = false
    //           -> LoadGrid
    //      Suspend / Activate -> ChangeStatus -> AuthService -> LoadGrid
    //
    //  Both the query and the update carry u.UserType <> 'SuperAdmin', so the
    //  platform operator's own account can neither be listed here nor changed
    //  from here. That rule is enforced in the SQL, not by hiding a button.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirement 4. Every Admin and Customer in one DataGridView, with the
    /// pharmacy name filled in beside an owner's row through a LEFT JOIN on
    /// Pharmacies. Both filters are optional, and an empty box means no filter
    /// rather than no results.
    /// </summary>
    public partial class SuperAdminManageUsersForm : Form
    {
        // One service instance for the form. readonly so the reference cannot be swapped
        // later; AuthService holds no connection of its own, so keeping it alive is free.
        private readonly AuthService _auth = new AuthService();

        // The re-entrancy guard. Assigning SelectedIndex in code raises
        // SelectedIndexChanged exactly as a user click does, and that handler calls
        // LoadGrid. Starting true means the two assignments during Load are ignored, and
        // the grid is queried once, deliberately, at the end.
        private bool _loading = true;

        public SuperAdminManageUsersForm()
        {
            // Only the designer's control construction belongs here. Reading data in the
            // constructor would run before the window has a handle, leaving an error with
            // nowhere to be shown.
            InitializeComponent();
        }

        private void SuperAdminManageUsersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            // The four status values are typed out rather than queried, because they are
            // the values CK_Users_Status permits. A SELECT DISTINCT would silently drop a
            // status whenever no account happened to hold it at that moment.
            cmbStatus.Items.AddRange(new object[] { "All statuses", "Pending", "Active", "Suspended" });
            // Index 0 is the "no filter" entry, which is why every read below treats index
            // 0 as an empty string rather than as a value to match on.
            cmbStatus.SelectedIndex = 0;

            // Two roles, not three. 'SuperAdmin' is deliberately absent because
            // SearchUsers excludes it in the WHERE clause, so offering it as a filter
            // would produce an empty grid and imply something was broken.
            cmbUserType.Items.AddRange(new object[] { "All users", "Admin", "Customer" });
            cmbUserType.SelectedIndex = 0;

            // Cleared only after both boxes are populated, so every change event they
            // raised while filling has already been swallowed by the guard in LoadGrid.
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
            // Every filter control routes here, so this one guard covers all of them and no
            // individual handler has to remember to check whether the form is still loading.
            if (_loading) return;

            try
            {
                // Index 0 means "All", which becomes an empty string rather than the literal
                // caption. That is what lets the query say (@Status = '' OR u.Status =
                // @Status): one statement serves the filtered and unfiltered cases, with no
                // branching in C# and no WHERE clause built by concatenation.
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();
                string type = cmbUserType.SelectedIndex <= 0 ? "" : cmbUserType.SelectedItem.ToString();

                // Trim, because a trailing space would become part of the LIKE pattern and
                // match nothing. All three values travel as SqlParameters, so whatever is
                // typed is compared as literal text and can never become query syntax.
                DataTable table = _auth.SearchUsers(txtSearch.Text.Trim(), status, type);
                dgvUsers.DataSource = table;

                // Columns exist only once DataSource has been assigned, so the headers are
                // set here rather than in ApplyTheme. The guard covers a failed load, where
                // there are no columns and indexing by name would throw.
                if (dgvUsers.Columns.Count > 0)
                {
                    // The keys are the aliases from the SELECT list in AuthService.
                    dgvUsers.Columns["UserId"].HeaderText = "ID";
                    // FillWeight is a share of the width, not pixels, because every grid in
                    // the application uses AutoSize Fill. An ID gets 30 against a default of
                    // 100, so the name and email columns take the space instead.
                    dgvUsers.Columns["UserId"].FillWeight = 30;
                    dgvUsers.Columns["FullName"].HeaderText = "Name";
                    dgvUsers.Columns["Email"].HeaderText = "Email";
                    // Phone in the schema, Mobile on screen, because every account here
                    // holds a Bangladeshi mobile number rather than a landline.
                    dgvUsers.Columns["Phone"].HeaderText = "Mobile";
                    // UserType is the column LoginForm switches on to choose a dashboard, so
                    // "Role" is the word that describes what it actually decides.
                    dgvUsers.Columns["UserType"].HeaderText = "Role";
                    dgvUsers.Columns["UserType"].FillWeight = 45;
                    dgvUsers.Columns["Status"].HeaderText = "Status";
                    dgvUsers.Columns["Status"].FillWeight = 45;
                    // PharmacyName comes from a LEFT JOIN, so a customer's row is kept and
                    // the query's ISNULL puts a dash in the cell. An INNER JOIN here would
                    // have silently dropped every customer from the list, which is exactly
                    // the bug the heading's "(owners only)" warns the reader about.
                    dgvUsers.Columns["PharmacyName"].HeaderText = "Pharmacy (owners only)";
                    dgvUsers.Columns["CreatedAt"].HeaderText = "Member since";
                }

                lblStatus.Text = table.Rows.Count + " account(s) shown.";
                // The reload can leave a different row current, or none at all, so the two
                // buttons are recomputed from the new selection rather than left as they were.
                UpdateButtons();
            }
            catch (Exception ex)
            {
                // DbHelper has already turned any SqlException into a readable sentence, so
                // ex.Message is safe to show directly.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvUsers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // RowIndex is -1 for the header row, which fires this event too, and the column
            // test covers the moment between a failed load and the next bind when there is
            // no "Status" column to read.
            if (e.RowIndex < 0 || dgvUsers.Columns.Count == 0) return;

            DataGridViewRow row = dgvUsers.Rows[e.RowIndex];
            // Read once into a local and test for null: the value is absent during teardown
            // and on any placeholder row.
            object value = row.Cells["Status"].Value;
            if (value == null) return;

            // The same three colours as the pharmacy grid, carrying the same meanings:
            // amber is waiting for a decision, red is locked out, white is normal. Using one
            // vocabulary across both screens is why these colours live in UiTheme.
            switch (value.ToString())
            {
                case "Pending": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;
                case "Suspended": row.DefaultCellStyle.BackColor = UiTheme.LowStockBack; break;
                // 'Active' is the only remaining value, but white must be assigned rather
                // than left alone: grid rows are recycled as the list scrolls, so a row that
                // is not repainted would keep the previous row's colour.
                default: row.DefaultCellStyle.BackColor = Color.White; break;
            }
        }

        // The selection is the only input to the button states, so one handler recomputing
        // them keeps that rule in a single place.
        private void dgvUsers_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            DataGridViewRow row = dgvUsers.CurrentRow;
            // Both halves are needed: CurrentRow is null on an empty grid, and the Status
            // cell can still be null while the grid is being rebound.
            bool hasRow = row != null && row.Cells["Status"].Value != null;
            // Empty string when nothing is selected, so both comparisons below are false and
            // both buttons end up disabled without a separate branch.
            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";

            // Each button is hidden only for the state it would be a no-op in, so
            // Suspend is available for Pending and Active accounts, and Activate for
            // Pending and Suspended ones. That is deliberate: a Pending owner can be
            // activated directly from here without going through pharmacy approval.
            //
            // Note the SuperAdmin's own account cannot appear in this grid at all -
            // SearchUsers filters it out with "WHERE u.UserType <> 'SuperAdmin'", and
            // SetUserStatus repeats that condition, so the platform operator cannot
            // suspend themselves and lock everybody out.
            //
            // Repeating the condition in the UPDATE rather than trusting the SELECT is the
            // point worth noticing: the grid is a convenience, the WHERE clause is the
            // guarantee. A UserId reaching SetUserStatus by any other route is refused by
            // the statement itself, which returns 0 rows changed and therefore false.
            btnSuspend.Enabled = hasRow && status != "Suspended";
            btnActivate.Enabled = hasRow && status != "Active";
        }

        private void ChangeStatus(string newStatus)
        {
            // One method for both buttons, parameterised by the target status, so Suspend
            // and Activate cannot drift apart in their confirmation, their write or their
            // refresh. The two handlers below are the only callers.
            DataGridViewRow row = dgvUsers.CurrentRow;
            // A silent return: the buttons are already disabled with no selection, so this
            // only fires if the row went away between the click and this line.
            if (row == null) return;

            // The id is what the update travels on; the name is only for the message. Taking
            // the id from the row rather than from a field means the action always applies
            // to the row the operator can see highlighted.
            int userId = Convert.ToInt32(row.Cells["UserId"].Value);
            string name = row.Cells["FullName"].Value.ToString();

            // Confirmed because suspending an account takes somebody's access away
            // immediately, and the same gesture in reverse gives it back. Naming both the
            // person and the new status means a misclicked row is caught here.
            DialogResult answer = MessageBox.Show(
                "Set " + name + "'s account to " + newStatus + "?",
                "Change account status", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Testing against Yes rather than for No, so dismissing the dialog any other
            // way also counts as a refusal.
            if (answer != DialogResult.Yes) return;

            // A single UPDATE on Users.Status. Nothing is deleted and no other table is
            // touched: the person's orders, reviews and, for an owner, their shop all stay
            // exactly as they are, so suspending an account is entirely reversible by the
            // other button. SetUserStatus returns true only when exactly one row changed,
            // which is also false when the id belonged to a SuperAdmin and the WHERE clause
            // refused it.
            if (_auth.SetUserStatus(userId, newStatus))
            {
                lblStatus.Text = name + " is now " + newStatus + ".";
                // Re-query rather than editing the cell in place, so the colour coding, the
                // two buttons and the stored value all come from one source.
                LoadGrid();
            }
        }

        // The two status buttons differ only in the string they pass, which is why the work
        // lives in ChangeStatus and these are single expressions.
        private void btnSuspend_Click(object sender, EventArgs e) => ChangeStatus("Suspended");
        private void btnActivate_Click(object sender, EventArgs e) => ChangeStatus("Active");
        // Both combo boxes share this handler: the response to either changing is the same.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // The search box deliberately does not reload as it is typed, so a long email does
        // not fire one query per keystroke. Searching is an explicit press.
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();

        private void btnClear_Click(object sender, EventArgs e)
        {
            // The guard is raised by hand around the three resets, because each assignment
            // raises a change event that would otherwise query the database on its way to
            // the same final state.
            _loading = true;
            txtSearch.Clear();
            cmbStatus.SelectedIndex = 0;
            cmbUserType.SelectedIndex = 0;
            _loading = false;
            // One deliberate reload, now that all three filters are back at their defaults.
            LoadGrid();
        }

        // Close, not Dispose: the dashboard opened this form inside a using block, so
        // closing returns control there and the dashboard refreshes itself.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
