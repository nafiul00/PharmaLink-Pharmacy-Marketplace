using System.Data;                  // DataTable, the shape PharmacyService.Search returns
using System.Drawing;               // Color, for the status colour coding in CellFormatting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme for styling, Validator for the rate rule
using PharmaLinkApp.Services;       // PharmacyService, which owns every statement this screen runs

// No using for PharmaLinkApp.Database, which is why this file contains no SQL.
namespace PharmaLinkApp.Forms
{
    // Layer: presentation. Load -> ApplyTheme -> LoadFilters -> LoadGrid.

    /// <summary>Requirements 2, 3 and 9: approve, suspend and commission.</summary>
    public partial class SuperAdminManageShopsForm : Form
    {
        // One service for the whole form, readonly so no handler can swap it out.
        private readonly PharmacyService _pharmacies = new PharmacyService();

        // The shop to select once the grid has loaded, or 0 for "no preselection".
        private readonly int _preselectPharmacyId;

        // Re-entrancy guard: filling a combo box would otherwise fire a query.
        private bool _loading = true;

        // The only constructor, so a caller must name the shop to preselect, or 0.
        public SuperAdminManageShopsForm(int preselectPharmacyId)
        {
            InitializeComponent();   // the designer's own method: builds every control first
            // Stored, not acted on: the grid has no rows to select yet.
            _preselectPharmacyId = preselectPharmacyId;
        }

        // Load, not the constructor: the handle exists, so the theme can measure.
        private void SuperAdminManageShopsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // appearance first, so nothing visibly reflows when rows arrive
            LoadFilters();  // then the combo boxes, because LoadGrid reads what they hold
            _loading = false;   // cleared only after the boxes are filled, so their events are swallowed
            // The first and only intentional load, now that the filters hold real values.
            LoadGrid();
        }

        // All the appearance in one method, so a style change happens in one place.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Manage Pharmacies");   // window defaults plus the title bar text

            panelHeader.BackColor = UiTheme.Primary;   // the brand colour band, taken from the theme
            lblTitle.Font = UiTheme.FontTitle;   // the shared title size, so headings do not vary
            lblTitle.ForeColor = Color.White;   // white on the primary band, the pairing the theme expects
            lblSubtitle.Font = UiTheme.FontSmall;   // smaller than the title, so the hierarchy is legible
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // a muted tint of the band, clearly secondary
            UiTheme.StyleSecondary(btnBack);   // Back navigates and never writes, so it must not compete

            UiTheme.StyleGrid(dgvPharmacies);   // shared grid styling, so every grid reads the same
            dgvPharmacies.CellFormatting += dgvPharmacies_CellFormatting;   // wired here, beside the styling it belongs to

            UiTheme.StyleSuccess(btnApprove);   // green: the one action that lets a shop start trading
            UiTheme.StyleDanger(btnSuspend);   // red: it takes a live shop out of service
            UiTheme.StyleAccent(btnReinstate);   // accent, not green: reinstating is a correction
            UiTheme.StyleDanger(btnDelete);   // red too, because this one is irreversible
            UiTheme.StylePrimary(btnSetCommission);   // primary: the routine action of the screen
            UiTheme.StyleSecondary(btnSearch);   // secondary: it only re-reads, so it is not a decision
            UiTheme.StyleSecondary(btnClear);   // likewise - clearing filters changes nothing

            lblNote.Font = UiTheme.FontSmall;   // standing guidance text, quieter than the data
            lblNote.ForeColor = UiTheme.TextMuted;   // muted, so help text does not compete with the grid
            lblStatus.Font = UiTheme.FontSmall;   // the running result line at the foot of the form
            lblStatus.ForeColor = UiTheme.TextMuted;   // muted as well: it reports, it does not warn
            lblCommissionError.Font = UiTheme.FontSmall;   // same size, so a shown error does not reflow
            lblCommissionError.ForeColor = UiTheme.Danger;   // red, because it appears only when something is wrong
        }

        // Fills the two filter combo boxes, once, from Load while _loading is true.
        private void LoadFilters()
        {
            cmbStatus.Items.Clear();   // clear first, so calling this twice cannot double the list
            // The three values CK_Pharmacies_Status permits, typed out rather than queried.
            cmbStatus.Items.AddRange(new object[] { "All statuses", "Pending", "Approved", "Suspended" });
            cmbStatus.SelectedIndex = 0;   // index 0 is the "no filter" entry every read treats as ""

            cmbArea.Items.Clear();   // same defensive clear, for the same repeat-call reason
            cmbArea.Items.Add("All areas");   // added BEFORE the query, so the sentinel is always index 0
            // Areas are data, not a constraint; false = do not restrict to approved shops.
            foreach (string area in _pharmacies.GetAreas(false)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;   // lands on "All areas", so the first load is unfiltered
        }

        // ---------------------------------------------------------------------

        // The single read path: every filter control and every action ends here.
        private void LoadGrid()
        {
            if (_loading) return;   // the guard on the _loading field, covering every filter at once

            try   // a database failure must leave the form usable rather than tearing it down
            {
                // Index 0 means "All", sent as "" so one query serves filtered and unfiltered.
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();   // <= 0, so an unselected box (-1) also reads as no filter

                // Trim, because a trailing space would become part of the LIKE pattern.
                DataTable table = _pharmacies.Search(txtSearch.Text.Trim(), status, area);
                dgvPharmacies.DataSource = table;   // one assignment rebinds every row and rebuilds the columns
                LabelColumns();   // columns exist only after DataSource is assigned, so labelling follows it

                lblStatus.Text = table.Rows.Count + " pharmac" + (table.Rows.Count == 1 ? "y" : "ies") +   // singular or plural, because "1 pharmacies" reads as a bug
                                 " shown" + DescribeFilters(status, area);   // the filter summary explains a small count

                // > 0, not != 0: 0 is the "opened from the menu" case and must not search.
                if (_preselectPharmacyId > 0) SelectPharmacy(_preselectPharmacyId);
                UpdateButtons();   // a reload can leave a different row current, or none at all
            }
            catch (Exception ex)   // Exception, not SqlException: faults arrive here as DataAccessException
            {
                // DbHelper has already turned any SqlException into readable English.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Builds the parenthesised tail of the status line, kept out of LoadGrid.
        private string DescribeFilters(string status, string area)
        {
            int active = 0;   // a count, not a list, so the line stays one short sentence
            // Read again here rather than passed in, so this sees what the query saw.
            if (!string.IsNullOrEmpty(txtSearch.Text.Trim())) active++;
            // status and area are already "" for the All entries, so emptiness is enough.
            if (!string.IsNullOrEmpty(status)) active++;
            if (!string.IsNullOrEmpty(area)) active++;   // the third filter, counted the same way
            return active == 0 ? "  (no filters applied)" : "  (" + active + " filter(s) applied)";   // two leading spaces separate this from the count
        }

        // Turns the query's aliases into readable headings. Called after every bind.
        private void LabelColumns()
        {
            if (dgvPharmacies.Columns.Count == 0) return;   // no columns means the bind never happened
            // These keys are the aliases in PharmacyService.Search's SELECT list.
            dgvPharmacies.Columns["PharmacyId"].HeaderText = "ID";
            dgvPharmacies.Columns["PharmacyId"].FillWeight = 30;   // a proportion, not pixels, because the grid fills
            dgvPharmacies.Columns["PharmacyName"].HeaderText = "Pharmacy";   // no FillWeight, so it takes the slack
            // OwnerName and OwnerEmail come from the INNER JOIN to Users on OwnerId.
            dgvPharmacies.Columns["OwnerName"].HeaderText = "Owner";
            dgvPharmacies.Columns["OwnerEmail"].HeaderText = "Owner email";   // the credential the owner signs in with
            // The column is LicenseNo, but the regulator's name for it is the DGDA licence.
            dgvPharmacies.Columns["LicenseNo"].HeaderText = "DGDA licence";
            dgvPharmacies.Columns["Area"].HeaderText = "Area";   // set explicitly, so no column shows a raw alias
            dgvPharmacies.Columns["ContactPhone"].HeaderText = "Contact";   // shortened, because the column is narrow
            // Abbreviated: the full wording is on the controls beneath the grid.
            dgvPharmacies.Columns["CommissionRate"].HeaderText = "Comm %";
            dgvPharmacies.Columns["CommissionRate"].FillWeight = 45;   // never wider than "30.00"
            dgvPharmacies.Columns["Status"].HeaderText = "Status";   // what the buttons and the row colouring key off
            dgvPharmacies.Columns["Status"].FillWeight = 55;   // sized for "Suspended", the longest permitted value
            // Medicines is a correlated COUNT subquery, so it cannot multiply the row.
            dgvPharmacies.Columns["Medicines"].HeaderText = "Items";
            dgvPharmacies.Columns["Medicines"].FillWeight = 40;   // a small integer needs little room
            // AverageRating carries IsHidden = 0, so it agrees with the low rated report.
            dgvPharmacies.Columns["AverageRating"].HeaderText = "Rating";
            dgvPharmacies.Columns["AverageRating"].FillWeight = 45;   // one decimal out of five
            dgvPharmacies.Columns["RegisteredAt"].HeaderText = "Registered";   // shows how long a Pending shop has waited
        }

        /// <summary>Status colour coding, so the queue reads at a glance.</summary>
        private void dgvPharmacies_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // RowIndex is -1 for the header, and the column may not exist mid-rebind.
            if (e.RowIndex < 0 || dgvPharmacies.Columns.Count == 0) return;

            DataGridViewRow row = dgvPharmacies.Rows[e.RowIndex];   // the whole row is coloured, not just one cell
            // Value is null on the placeholder row and during teardown, so test it first.
            object value = row.Cells["Status"].Value;
            if (value == null) return;   // nothing to key a colour off, so the theme's own colour stands

            // Colour carries the same meaning as the buttons: amber waits, red is offline.
            switch (value.ToString())
            {
                case "Pending":   // waiting on the operator, the state this screen exists to clear
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224);   // pale amber, so black text stays readable
                    break;   // one case, one colour: no fall-through
                case "Suspended":   // out of service, the other state that needs to stand out
                    row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;   // the theme's warning wash, used application-wide
                    break;   // stops here rather than falling into default, which would repaint it white
                default:   // 'Approved', the healthy majority
                    // White is assigned explicitly: recycled rows would keep the old colour.
                    row.DefaultCellStyle.BackColor = Color.White;
                    break;   // required by C#: a switch section must not fall through
            }
        }

        // Finds and selects one shop after a bind, for the dashboard's preselection.
        private void SelectPharmacy(int pharmacyId)
        {
            // A linear scan: the id is not a key into Rows, and the order is the query's.
            foreach (DataGridViewRow row in dgvPharmacies.Rows)
            {
                if (Convert.ToInt32(row.Cells["PharmacyId"].Value) == pharmacyId)   // Convert, because the cell holds a boxed value
                {
                    row.Selected = true;   // highlights it, which is what the operator sees
                    // CurrentCell moves CurrentRow; column 1, so the scroll lands on the name.
                    dgvPharmacies.CurrentCell = row.Cells[1];
                    break;   // PharmacyId is the primary key, so a second match is impossible
                }
            }
        }

        // -- selection and button state --

        // Fires on every selection change, including those LoadGrid causes.
        private void dgvPharmacies_SelectionChanged(object sender, EventArgs e)
        {
            // The selection is the only input to the button states, so one rule, one place.
            UpdateButtons();
        }

        // The whole button-state rule in one method, so illegal moves are unreachable.
        private void UpdateButtons()
        {
            DataGridViewRow row = dgvPharmacies.CurrentRow;   // CurrentRow, because it is what the actions read
            // CurrentRow is null on an empty grid, and its cell is null mid-rebind.
            bool hasRow = row != null && row.Cells["Status"].Value != null;

            // "" when nothing is selected, so all three tests below are false at once.
            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";

            // The three are mutually exclusive: CK_Pharmacies_Status allows no fourth value.
            btnApprove.Enabled = hasRow && status == "Pending";      // Pending -> Approved
            btnSuspend.Enabled = hasRow && status == "Approved";     // Approved -> Suspended
            btnReinstate.Enabled = hasRow && status == "Suspended";  // Suspended -> Approved

            // Delete stays enabled for any status: the real test is order history.
            btnDelete.Enabled = hasRow;
            btnSetCommission.Enabled = hasRow;   // a rate is meaningless without a shop to apply it to
            // Disabled too, so nobody types a rate with no shop selected and loses it.
            txtCommission.Enabled = hasRow;

            // Prefill with the CURRENT rate, so the operator edits a real value.
            txtCommission.Text = hasRow ? row.Cells["CommissionRate"].Value.ToString() : "";
            // Assigning Text raised TextChanged; a stored value is valid, so clear the error.
            UiTheme.ClearError(lblCommissionError, txtCommission);
        }

        // The id every action works from, read fresh at each click rather than cached.
        private int SelectedId()
        {
            // 0 cannot collide with a real row: PharmacyId is an IDENTITY starting at 1.
            if (dgvPharmacies.CurrentRow == null) return 0;
            // Convert, not a cast: the provider chooses the CLR type of a boxed int.
            return Convert.ToInt32(dgvPharmacies.CurrentRow.Cells["PharmacyId"].Value);
        }

        // The display name, for prompts and status lines only - never to identify a row.
        private string SelectedName()
        {
            // "" is safe because every action still travels by id, never by name.
            if (dgvPharmacies.CurrentRow == null) return "";
            return dgvPharmacies.CurrentRow.Cells["PharmacyName"].Value.ToString();   // ToString because the cell is typed as object
        }

        // -- actions --

        // Pending -> Approved: the only action that lets a shop begin trading.
        private void btnApprove_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // read fresh, so a row that is no longer current cannot be acted on
            if (id == 0) return;   // silent: the button is only enabled for a Pending row

            string licence = dgvPharmacies.CurrentRow.Cells["LicenseNo"].Value.ToString();   // read from the grid, not re-queried

            // The prompt names both rows that change and quotes the licence being checked.
            DialogResult answer = MessageBox.Show(
                "Approve " + SelectedName() + "?\r\n\r\nDGDA licence: " + licence + "\r\n\r\n" +   // name and licence, so the right shop is confirmed
                "Pharmacies.Status becomes 'Approved' and Users.Status becomes 'Active', " +   // the second row is the easy one to forget
                "after which the owner can log in and the shop's medicines become visible to customers.",   // the real-world effect
                "Approve pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // YesNo, because the caption is a question

            // Testing for Yes, so closing the dialog any other way counts as a refusal.
            if (answer != DialogResult.Yes) return;

            // TWO updates in ONE transaction: Pharmacies.Status and the owner's Users.Status.
            _pharmacies.Approve(id);
            // Read the name again: the message is composed before the grid is rebound.
            lblStatus.Text = SelectedName() + " approved.";
            LoadGrid();   // status, row colour and buttons all move together
        }

        // Approved -> Suspended: the reversible alternative to deletion.
        private void btnSuspend_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // same fresh read as Approve
            if (id == 0) return;   // silent, because the button is only enabled for an Approved row

            // The rating is the evidence, so the prompt quotes it back.
            string rating = dgvPharmacies.CurrentRow.Cells["AverageRating"].Value.ToString();

            // The prompt describes the three updates and what does NOT happen.
            DialogResult answer = MessageBox.Show(
                "Suspend " + SelectedName() + "?\r\n\r\nAverage customer rating: " + rating + "\r\n\r\n" +   // the evidence sits beside the question
                "Three updates run inside one transaction: the pharmacy becomes Suspended, the owner's " +   // 'one transaction', so partial suspension is impossible
                "account becomes Suspended, and every medicine this pharmacy lists has IsActive set to 0.\r\n\r\n" +   // the update customers actually see
                "Nothing is deleted, so the sales history and the invoices customers already hold stay valid.",   // why this beats Delete
                "Suspend pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);   // Warning: this takes a trading shop offline

            if (answer != DialogResult.Yes) return;   // anything but Yes leaves the shop trading

            // THREE UPDATEs in ONE transaction, no DELETE: shop, owner, its medicines.
            _pharmacies.Suspend(id);
            lblStatus.Text = SelectedName() + " suspended. Its medicines are no longer visible to customers.";   // names the customer-visible consequence
            LoadGrid();   // the row turns red and the buttons swap over
        }

        // Suspended -> Approved: the way back, and the reason Suspend deletes nothing.
        private void btnReinstate_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // fresh read, as in the two handlers above
            if (id == 0) return;   // only reachable if the selection vanished after enabling

            // A short prompt, because Suspend beside it undoes this in one click.
            DialogResult answer = MessageBox.Show(
                "Put " + SelectedName() + " back on the platform?",   // one sentence, because nothing here is destructive
                "Reinstate pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // Question: this restores service

            if (answer != DialogResult.Yes) return;   // the same Yes-only test, so a stray Escape changes nothing

            // The exact mirror of Suspend: the same three rows, set back in one transaction.
            _pharmacies.Reinstate(id);
            // No status message: the row turning white and the buttons swapping say it.
            LoadGrid();
        }

        // The irreversible one, and the only action with an explicit refusal path.
        private void btnDelete_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // fresh read again; this handler is enabled for every status
            if (id == 0) return;   // nothing selected, so there is nothing to delete

            // The rule is stated before the commit, so a refusal afterwards is expected.
            DialogResult answer = MessageBox.Show(
                "Permanently delete " + SelectedName() + " and its owner account?\r\n\r\n" +   // 'permanently', and the owner goes too
                "This is only possible for a shop that has never taken an order. " +   // the rule, stated before the click
                "A shop with order history must be suspended instead.",   // points at the alternative
                "Delete pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);   // Warning, matching Suspend

            if (answer != DialogResult.Yes) return;   // the last chance to back out

            // out message, not an exception: a refusal is a normal outcome with a reason.
            string message;
            if (_pharmacies.Delete(id, out message))   // true means no orders, and every dependent row went in one transaction
            {
                lblStatus.Text = message;   // success is quiet: the row disappearing is the confirmation
                LoadGrid();   // the deleted shop is gone from the grid, which is the visible proof
            }
            else   // the shop has order history, so the service refused and wrote nothing
            {
                // A dialog, not the status line: the operator's intent was not carried out.
                MessageBox.Show(message, "Cannot delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // -- commission rate (requirement 9) --

        // Runs on every keystroke, so feedback arrives while they can still see it.
        private void txtCommission_TextChanged(object sender, EventArgs e)
        {
            decimal rate;   // filled by the validator below
            // Called before the emptiness test, because C# needs the out parameter assigned.
            bool valid = Validator.IsCommissionRate(txtCommission.Text, out rate);

            // An empty box is an unfinished edit, not an error, so it says "not yet".
            if (string.IsNullOrWhiteSpace(txtCommission.Text))
            {
                UiTheme.ClearError(lblCommissionError, txtCommission);   // removes any red label from the last keystroke
                btnSetCommission.Enabled = false;   // nothing to save yet, so the action is withdrawn
                return;   // so the "between 0 and 30" branch never fires on an empty box
            }

            // Checked here for the red label, and again by CK_Pharmacies_Comm.
            if (valid) UiTheme.ClearError(lblCommissionError, txtCommission);
            else UiTheme.ShowError(lblCommissionError, txtCommission,   // ShowError also tints the box, not just the label
                    "The rate must be a number between 0 and 30 (CK_Pharmacies_Comm).");   // naming the constraint ties screen to database

            // Both halves: a valid number is no use with no selection, and vice versa.
            btnSetCommission.Enabled = valid && dgvPharmacies.CurrentRow != null;
        }

        // Writes the rate. Requirement 9: the rate is per shop, so this changes one row.
        private void btnSetCommission_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // the shop the rate applies to, read at click time
            if (id == 0) return;   // the button is disabled without a selection, so this is belt and braces

            decimal rate;   // declared out here so it is still in scope after the if
            // Validated again: the parse must happen anyway, so testing its result is free.
            if (!Validator.IsCommissionRate(txtCommission.Text, out rate))
            {
                UiTheme.ShowError(lblCommissionError, txtCommission,   // re-shown, because the box changed since the last check
                    "The rate must be a number between 0 and 30 (CK_Pharmacies_Comm).");   // identical wording, so the operator is told one thing
                return;   // nothing is written, and the box keeps the text to be corrected
            }

            // The rate is a COLUMN on Pharmacies, so one shop can differ without a rebuild.
            if (_pharmacies.SetCommissionRate(id, rate))
            {
                // Only future orders: each order froze its own CommissionAmount at checkout.
                lblStatus.Text = SelectedName() + " will be charged " + rate.ToString("N2") +
                                 "% commission on orders placed from now on. Past orders keep the rate they were sold at.";   // says plainly that the change is not retrospective
                // Reload, so Comm % shows the stored value read back from the database.
                LoadGrid();
            }
        }

        // ---------------------------------------------------------------------

        // One handler for both combo boxes, because the response is identical.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Search is an explicit button press, not one query per character typed.
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();

        // Resets all three filters at once, which is why it needs a guard.
        private void btnClear_Click(object sender, EventArgs e)
        {
            _loading = true;   // raised by hand, so three resets do not cause three queries
            txtSearch.Clear();   // Clear() rather than "", so the caret and selection go too
            cmbStatus.SelectedIndex = 0;   // back to "All statuses", which LoadGrid reads as no filter
            cmbArea.SelectedIndex = 0;   // back to "All areas", the same sentinel for the second box
            _loading = false;   // lowered before the one reload this handler intends
            // One deliberate reload, now that all three filters are back at their defaults.
            LoadGrid();
        }

        // Close, not Dispose: the dashboard opened this form in a using block.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
