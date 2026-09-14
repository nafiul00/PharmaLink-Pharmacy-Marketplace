using System.Data;                  // DataTable, what AuthService.SearchUsers returns
using System.Drawing;               // Color, for the status colours in CellFormatting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette and control styles
using PharmaLinkApp.Services;       // AuthService: both the search and the status update

// Forms call services; a form has no way to reach a connection of its own.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 4. Every Admin and Customer in one DataGridView.</summary>
    public partial class SuperAdminManageUsersForm : Form // Both filters are optional.
    {
        // One instance for the form; AuthService holds no connection, so this is free.
        private readonly AuthService _auth = new AuthService();

        // Assigning SelectedIndex raises the same event a click does, so guard Load.
        private bool _loading = true;

        // Parameterless: the list is the whole user table, nothing to preselect.
        public SuperAdminManageUsersForm()
        {
            InitializeComponent();   // designer controls only; a read here would have no window
        }

        // Fires once after the window exists; both dropdowns are filled from code.
        private void SuperAdminManageUsersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // appearance first, and it wires up the CellFormatting handler

            // Typed out, not queried: these are the values CK_Users_Status permits.
            cmbStatus.Items.AddRange(new object[] { "All statuses", "Pending", "Active", "Suspended" });
            cmbStatus.SelectedIndex = 0;   // index 0 is the "no filter" entry

            // No 'SuperAdmin' entry: SearchUsers excludes it, so it would show an empty grid.
            cmbUserType.Items.AddRange(new object[] { "All users", "Admin", "Customer" });
            cmbUserType.SelectedIndex = 0;   // the placeholder, so both roles are shown

            _loading = false;   // cleared only after both boxes are populated
            LoadGrid();         // the single deliberate first read
        }

        // Appearance only, called once; nothing in here reads or writes a single field.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Manage Users");        // shared window setup and caption

            panelHeader.BackColor = UiTheme.Primary;        // the standard header strip
            lblTitle.Font = UiTheme.FontTitle;              // the shared heading font
            lblTitle.ForeColor = Color.White;               // the only colour that holds up on Primary
            lblSubtitle.Font = UiTheme.FontSmall;           // smaller, it explains the title
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // pale tint, part of the strip

            UiTheme.StyleSecondary(btnBack);                // leaving is not worth emphasising
            UiTheme.StyleSecondary(btnSearch);              // safe and repeatable, so it stays quiet
            UiTheme.StyleSecondary(btnClear);               // the other half of the same pair
            UiTheme.StyleDanger(btnSuspend);                // red: it takes somebody's access away
            UiTheme.StyleSuccess(btnActivate);              // green: the exact reverse
            UiTheme.StyleGrid(dgvUsers);                    // read-only, full-row select, Fill columns
            dgvUsers.CellFormatting += dgvUsers_CellFormatting;   // in code, designer untouched

            lblNote.Font = UiTheme.FontSmall;               // the note about what the buttons do
            lblNote.ForeColor = UiTheme.TextMuted;          // muted, a caption rather than news
            lblStatus.Font = UiTheme.FontSmall;             // the line LoadGrid and ChangeStatus use
            lblStatus.ForeColor = UiTheme.TextMuted;        // it reports, it does not instruct
        }

        // The single read path: both dropdowns, Search and Clear all end here.
        private void LoadGrid()
        {
            if (_loading) return;   // one guard for every filter control, so none must remember

            try   // the whole read is wrapped: a partly filled grid would be read as fact
            {
                // Index 0 becomes "", which the query reads as (@Status = '' OR ...).
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();
                string type = cmbUserType.SelectedIndex <= 0 ? "" : cmbUserType.SelectedItem.ToString();   // the same trick for the role

                // Trim, because a trailing space joins the LIKE pattern and matches nothing.
                DataTable table = _auth.SearchUsers(txtSearch.Text.Trim(), status, type);
                dgvUsers.DataSource = table;   // this assignment creates the columns below

                // Columns exist only after DataSource is set, and the guard covers a failed load.
                if (dgvUsers.Columns.Count > 0)
                {
                    // The keys are the aliases from the SELECT list in AuthService.
                    dgvUsers.Columns["UserId"].HeaderText = "ID";
                    // FillWeight is a share of the width, not pixels; 30 against a default of 100.
                    dgvUsers.Columns["UserId"].FillWeight = 30;
                    dgvUsers.Columns["FullName"].HeaderText = "Name";     // default weight, a name needs room
                    dgvUsers.Columns["Email"].HeaderText = "Email";       // the longest value in the row
                    // Phone in the schema, Mobile on screen: these are all mobile numbers.
                    dgvUsers.Columns["Phone"].HeaderText = "Mobile";
                    // UserType is what LoginForm switches on, so "Role" says what it decides.
                    dgvUsers.Columns["UserType"].HeaderText = "Role";
                    dgvUsers.Columns["UserType"].FillWeight = 45;       // "Customer" is the longest value
                    dgvUsers.Columns["Status"].HeaderText = "Status";   // drives the colours and both buttons
                    dgvUsers.Columns["Status"].FillWeight = 45;         // "Suspended" is the longest of four
                    // A LEFT JOIN, so a customer's row survives and ISNULL puts a dash in it.
                    dgvUsers.Columns["PharmacyName"].HeaderText = "Pharmacy (owners only)";
                    dgvUsers.Columns["CreatedAt"].HeaderText = "Member since";   // reads as a date
                }

                lblStatus.Text = table.Rows.Count + " account(s) shown.";   // "(s)" avoids a branch
                // The reload can leave a different row current, so recompute the buttons.
                UpdateButtons();
            }
            catch (Exception ex)   // ex.Message is already a sentence by the time it arrives
            {
                // DbHelper already translated the SqlException, so this is safe to show.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Colours each row by Status. Cheap on purpose: it runs per cell, per repaint.
        private void dgvUsers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // -1 is the header row, and the column test covers the gap after a failed load.
            if (e.RowIndex < 0 || dgvUsers.Columns.Count == 0) return;

            DataGridViewRow row = dgvUsers.Rows[e.RowIndex];   // the row, so one event paints the line
            // Read once into a local: the value is absent during teardown and on placeholders.
            object value = row.Cells["Status"].Value;
            if (value == null) return;   // nothing to decide a colour from, so leave the row

            // The same vocabulary as the pharmacy grid: amber waiting, red locked out.
            switch (value.ToString())
            {
                case "Pending": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;   // amber: not yet decided
                case "Suspended": row.DefaultCellStyle.BackColor = UiTheme.LowStockBack; break;          // red: act on this
                // White must be assigned: rows are recycled and would keep the old colour.
                default: row.DefaultCellStyle.BackColor = Color.White; break;
            }
        }

        // The selection is the only input to the button states, so one method decides.
        private void dgvUsers_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        // Decides which of Suspend and Activate is available on the current row.
        private void UpdateButtons()
        {
            DataGridViewRow row = dgvUsers.CurrentRow;   // read once, the guard and the cell need it
            // Both halves matter: null on an empty grid, and null again during a rebind.
            bool hasRow = row != null && row.Cells["Status"].Value != null;
            // "" when nothing is selected, so both tests below fail and both buttons disable.
            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";

            // Each button is offered only where it would do something; Pending allows both.
            btnSuspend.Enabled = hasRow && status != "Suspended";
            btnActivate.Enabled = hasRow && status != "Active";   // the mirror image of the above
        }

        // The one write path, parameterised by the status being moved to.
        private void ChangeStatus(string newStatus)
        {
            // One method for both buttons, so the two cannot drift apart in any step.
            DataGridViewRow row = dgvUsers.CurrentRow;
            if (row == null) return;   // silent: only reachable if the row went away after the click

            // The id is what the UPDATE travels on; the name is only for the message.
            int userId = Convert.ToInt32(row.Cells["UserId"].Value);
            string name = row.Cells["FullName"].Value.ToString();   // used in both messages below

            // Confirmed, because this takes access away immediately and names the person.
            DialogResult answer = MessageBox.Show(
                "Set " + name + "'s account to " + newStatus + "?",   // a wrong row is obvious here
                "Change account status", MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // a decision, not a warning

            // Tested against Yes, so closing the dialog any other way also counts as no.
            if (answer != DialogResult.Yes) return;

            // One UPDATE on Users.Status: orders, reviews and any shop stay as they are.
            if (_auth.SetUserStatus(userId, newStatus))   // its WHERE refuses a SuperAdmin id
            {
                lblStatus.Text = name + " is now " + newStatus + ".";   // a line, not a dialog
                LoadGrid();   // re-query, so the colours, buttons and stored value share a source
            }
        }

        // The two buttons differ only in the string, so the work lives in ChangeStatus.
        private void btnSuspend_Click(object sender, EventArgs e) => ChangeStatus("Suspended");
        private void btnActivate_Click(object sender, EventArgs e) => ChangeStatus("Active");   // the reverse
        // Both combo boxes share this: the response to either changing is the same.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // The search box does not reload as it is typed; searching is a press.
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();

        // Puts all three filters back to their defaults in one press, then reads once.
        private void btnClear_Click(object sender, EventArgs e)
        {
            // Raised by hand, because each of the three resets raises a change event.
            _loading = true;
            txtSearch.Clear();               // the typed search text
            cmbStatus.SelectedIndex = 0;     // back to "All statuses", read as no filter
            cmbUserType.SelectedIndex = 0;   // back to "All users", the other placeholder
            _loading = false;                // lowered, so the single reload below is allowed
            LoadGrid();   // one deliberate reload, now the filters are back at their defaults
        }

        // Close, not Dispose: the dashboard's using block around ShowDialog disposes this.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
