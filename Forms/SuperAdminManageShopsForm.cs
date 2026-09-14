using System.Data;                  // DataTable, the shape PharmacyService.Search returns
using System.Drawing;               // Color, for the status colour coding in CellFormatting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme for styling and error labels, Validator for the rate rule
using PharmaLinkApp.Services;       // PharmacyService, which owns every statement this screen runs

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by SuperAdminDashboard, either from the menu
    //  with 0, or from a low rated row with a real PharmacyId to preselect.
    //  Uses PharmacyService for the grid and for all five actions.
    //
    //  Flow:
    //      Load -> ApplyTheme -> LoadFilters -> _loading = false -> LoadGrid
    //      any filter changes -> LoadGrid
    //      any action -> confirm -> PharmacyService -> LoadGrid
    //
    //  The lifecycle this form administers is Pending -> Approved -> Suspended,
    //  with Suspended -> Approved as the way back. CK_Pharmacies_Status allows
    //  no fourth value, so the three buttons below cover the whole state machine.
    //
    //  There is no SQL in this file. Approve runs two UPDATEs in one
    //  transaction, Suspend runs three, and neither deletes anything.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirements 2, 3 and 9.
    ///
    /// Approve flips two rows, suspend flips three, and the commission rate is
    /// a column on Pharmacies rather than a constant in the code, so it can be
    /// changed for one shop without touching anyone else.
    /// </summary>
    public partial class SuperAdminManageShopsForm : Form
    {
        private readonly PharmacyService _pharmacies = new PharmacyService();

        // The shop to select once the grid has loaded, or 0 for "no preselection".
        // readonly because it is a fact about why this form was opened and must not change
        // while it is open. 0 is safe as the sentinel because PharmacyId is an IDENTITY
        // column starting at 1, so it can never collide with a real shop.
        private readonly int _preselectPharmacyId;

        // The re-entrancy guard. Setting cmbStatus.SelectedIndex in code raises
        // SelectedIndexChanged exactly as a user click does, and that handler calls
        // LoadGrid. Without this flag, filling the two combo boxes during Load would fire
        // the query once per assignment before the form is even ready. It starts true and
        // is cleared once at the end of Load, which is why LoadGrid begins by testing it.
        private bool _loading = true;

        public SuperAdminManageShopsForm(int preselectPharmacyId)
        {
            InitializeComponent();
            // Stored rather than acted on here: the grid does not exist yet, so the
            // selection cannot be made until LoadGrid has bound some rows.
            _preselectPharmacyId = preselectPharmacyId;
        }

        private void SuperAdminManageShopsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadFilters();
            // Cleared only AFTER the combo boxes are populated, so every change event they
            // raised while filling has already been swallowed by the guard in LoadGrid.
            _loading = false;
            // The first and only intentional load, now that the filters hold real values.
            LoadGrid();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Manage Pharmacies");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);
            UiTheme.StyleSecondary(btnBack);

            UiTheme.StyleGrid(dgvPharmacies);
            dgvPharmacies.CellFormatting += dgvPharmacies_CellFormatting;

            UiTheme.StyleSuccess(btnApprove);
            UiTheme.StyleDanger(btnSuspend);
            UiTheme.StyleAccent(btnReinstate);
            UiTheme.StyleDanger(btnDelete);
            UiTheme.StylePrimary(btnSetCommission);
            UiTheme.StyleSecondary(btnSearch);
            UiTheme.StyleSecondary(btnClear);

            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
            lblCommissionError.Font = UiTheme.FontSmall;
            lblCommissionError.ForeColor = UiTheme.Danger;
        }

        private void LoadFilters()
        {
            // Clear first, so calling this method twice could never double the list.
            cmbStatus.Items.Clear();
            // The three status values are typed out because they are the three values
            // CK_Pharmacies_Status permits, and nothing else can be in the column. A
            // DISTINCT query over Pharmacies.Status would have been the alternative, but it
            // would silently lose a status the moment no shop currently held it.
            cmbStatus.Items.AddRange(new object[] { "All statuses", "Pending", "Approved", "Suspended" });
            // Index 0 is the "no filter" entry, which is why every read of this box treats
            // index 0 as an empty string rather than as a value to match.
            cmbStatus.SelectedIndex = 0;

            cmbArea.Items.Clear();
            cmbArea.Items.Add("All areas");
            // Areas ARE read from the database, unlike statuses, because they are data
            // rather than a constraint: a new area appears as soon as a shop registers in
            // one. false means "do not restrict to approved shops", which matters here
            // because this screen must be able to filter to a pending shop's area too.
            foreach (string area in _pharmacies.GetAreas(false)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;
        }

        // ---------------------------------------------------------------------

        private void LoadGrid()
        {
            // The guard described on the _loading field. Every filter control routes here,
            // so this one line covers all of them at once rather than each caller having to
            // remember to check.
            if (_loading) return;

            try
            {
                // Index 0 means "All", which the service treats as no filter at all. Sending
                // an empty string rather than the literal text "All statuses" is what lets
                // the query say (@Status = '' OR p.Status = @Status): one query serves the
                // filtered and unfiltered cases, with no branching in C# and no string
                // concatenation building a different WHERE clause per combination.
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();

                // Trim, because a trailing space typed into the search box would otherwise
                // become part of the LIKE pattern and match nothing. All three filters are
                // optional and travel as parameters, so what the operator types is compared
                // as literal text and can never become query syntax.
                DataTable table = _pharmacies.Search(txtSearch.Text.Trim(), status, area);
                dgvPharmacies.DataSource = table;
                // Columns exist only after DataSource is assigned, so labelling follows it.
                LabelColumns();

                lblStatus.Text = table.Rows.Count + " pharmac" + (table.Rows.Count == 1 ? "y" : "ies") +
                                 " shown" + DescribeFilters(status, area);

                // Preselection happens after binding for the obvious reason that there were
                // no rows to select before it. The test is > 0 rather than != 0 because 0 is
                // the "opened from the menu" case and must not trigger a search.
                if (_preselectPharmacyId > 0) SelectPharmacy(_preselectPharmacyId);
                // Reload can leave a different row current, or none, so the button states
                // are recomputed from whatever is now selected rather than assumed.
                UpdateButtons();
            }
            catch (Exception ex)
            {
                // DbHelper has already turned any SqlException into readable English.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string DescribeFilters(string status, string area)
        {
            // Counting the active filters rather than listing them keeps the status line to
            // one short sentence whatever combination is applied. The point of the line is
            // to explain a surprisingly small row count, and a number does that.
            int active = 0;
            // The search box is read again here rather than being passed in, so this method
            // sees exactly what the query saw: the same Trim is applied to the same text.
            if (!string.IsNullOrEmpty(txtSearch.Text.Trim())) active++;
            // status and area are already "" for the All entries, which is why a plain
            // emptiness test is enough and no comparison against "All statuses" is needed.
            if (!string.IsNullOrEmpty(status)) active++;
            if (!string.IsNullOrEmpty(area)) active++;
            return active == 0 ? "  (no filters applied)" : "  (" + active + " filter(s) applied)";
        }

        private void LabelColumns()
        {
            // No columns means the bind never happened, and indexing by name would throw.
            if (dgvPharmacies.Columns.Count == 0) return;
            // These keys are the aliases from PharmacyService.Search. Binding by name makes
            // the grid describe itself from the query, at the price of these strings having
            // to follow any rename in that SELECT list.
            dgvPharmacies.Columns["PharmacyId"].HeaderText = "ID";
            // FillWeight is a proportion of the width, not pixels, because the grid is set
            // to AutoSize Fill. Narrow things get small weights so names get the space.
            dgvPharmacies.Columns["PharmacyId"].FillWeight = 30;
            dgvPharmacies.Columns["PharmacyName"].HeaderText = "Pharmacy";
            // OwnerName and OwnerEmail come from the INNER JOIN to Users on OwnerId. They
            // are shown because approving a shop also activates that person's login, so the
            // operator should see who they are letting in.
            dgvPharmacies.Columns["OwnerName"].HeaderText = "Owner";
            dgvPharmacies.Columns["OwnerEmail"].HeaderText = "Owner email";
            // The column is LicenseNo, but the regulator's name for it is the DGDA licence.
            dgvPharmacies.Columns["LicenseNo"].HeaderText = "DGDA licence";
            dgvPharmacies.Columns["Area"].HeaderText = "Area";
            dgvPharmacies.Columns["ContactPhone"].HeaderText = "Contact";
            // Abbreviated because the column is narrow, and the full wording is on the
            // controls beneath the grid where the rate is actually edited.
            dgvPharmacies.Columns["CommissionRate"].HeaderText = "Comm %";
            dgvPharmacies.Columns["CommissionRate"].FillWeight = 45;
            dgvPharmacies.Columns["Status"].HeaderText = "Status";
            dgvPharmacies.Columns["Status"].FillWeight = 55;
            // Medicines is a correlated COUNT subquery in the SELECT list, not a join, so
            // counting a shop's stock cannot multiply its row. It is shown because it is
            // what a suspension is about to take out of the catalogue.
            dgvPharmacies.Columns["Medicines"].HeaderText = "Items";
            dgvPharmacies.Columns["Medicines"].FillWeight = 40;
            // AverageRating is the same figure the low rated report groups on, computed by
            // a subquery that also carries IsHidden = 0, so a hidden review does not count
            // here either and the two screens agree.
            dgvPharmacies.Columns["AverageRating"].HeaderText = "Rating";
            dgvPharmacies.Columns["AverageRating"].FillWeight = 45;
            dgvPharmacies.Columns["RegisteredAt"].HeaderText = "Registered";
        }

        /// <summary>Status colour coding, so the queue reads at a glance.</summary>
        private void dgvPharmacies_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Two guards in one test: RowIndex is -1 for the header row, and the column
            // check covers the instant between a failed load and the next bind, when the
            // event can still fire against a grid that has no "Status" column to read.
            if (e.RowIndex < 0 || dgvPharmacies.Columns.Count == 0) return;

            DataGridViewRow row = dgvPharmacies.Rows[e.RowIndex];
            // Value comes back as object and is null on the new-row placeholder and during
            // teardown, so it is read once into a local and tested before use.
            object value = row.Cells["Status"].Value;
            if (value == null) return;

            // Colour carries the same meaning as the buttons: amber is waiting for a
            // decision, red is out of service, white is trading normally.
            switch (value.ToString())
            {
                case "Pending":
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224);
                    break;
                case "Suspended":
                    row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
                    break;
                default:
                    // 'Approved' is the only value left, but writing it as the default and
                    // assigning white explicitly matters: rows are recycled as the grid
                    // scrolls, so a row that is not repainted keeps the previous row's
                    // colour. Doing nothing here would leave amber smeared down the grid.
                    row.DefaultCellStyle.BackColor = Color.White;
                    break;
            }
        }

        private void SelectPharmacy(int pharmacyId)
        {
            // A linear scan is correct here rather than a lookup: the grid holds one screen
            // of pharmacies, the id is not a key into the Rows collection, and the DataTable
            // row order is the query's ORDER BY rather than anything indexable.
            foreach (DataGridViewRow row in dgvPharmacies.Rows)
            {
                if (Convert.ToInt32(row.Cells["PharmacyId"].Value) == pharmacyId)
                {
                    row.Selected = true;
                    // Setting CurrentCell is what actually moves CurrentRow, which is what
                    // every action on this form reads. Selected alone highlights the row
                    // without making it current, and the buttons would stay disabled.
                    // Column 1 rather than 0, so the scroll lands on the name, not the id.
                    dgvPharmacies.CurrentCell = row.Cells[1];
                    // Stop at the first match. PharmacyId is the primary key, so a second
                    // match is impossible and continuing would only cost a full scan.
                    break;
                }
            }
        }

        // ---------------------------------------------------------------------
        //  SELECTION AND BUTTON STATE
        //  With no row selected every action button stays disabled, which is the
        //  rule the navigation diagram describes.
        // ---------------------------------------------------------------------

        private void dgvPharmacies_SelectionChanged(object sender, EventArgs e)
        {
            // The selection is the only input to the button states, so recomputing them
            // here means there is one rule, applied from one place, rather than each
            // action having to decide for itself whether it is currently legal.
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            DataGridViewRow row = dgvPharmacies.CurrentRow;
            // Both halves matter: CurrentRow is null on an empty grid, and its Status cell
            // can still be null while the grid is being rebound. Reading the cell in the
            // next line without this test would throw during a refresh.
            bool hasRow = row != null && row.Cells["Status"].Value != null;

            // Empty string when nothing is selected, so the three comparisons below are all
            // false and every button ends up disabled without a separate branch.
            string status = hasRow ? row.Cells["Status"].Value.ToString() : "";

            // The three status buttons are mutually exclusive by construction: a shop is
            // Pending, Approved or Suspended (CK_Pharmacies_Status allows nothing else),
            // so exactly one of these three can ever be enabled at a time. Encoding the
            // lifecycle in the buttons means an illegal transition cannot be attempted.
            btnApprove.Enabled = hasRow && status == "Pending";      // Pending -> Approved
            btnSuspend.Enabled = hasRow && status == "Approved";     // Approved -> Suspended
            btnReinstate.Enabled = hasRow && status == "Suspended";  // Suspended -> Approved

            // Delete stays enabled for ANY status, because whether it is allowed depends
            // on order history, not on status - and that question needs a database round
            // trip. PharmacyService.Delete counts the orders and refuses with an
            // explanation, which is better than a permanently greyed button the user
            // cannot understand.
            btnDelete.Enabled = hasRow;
            btnSetCommission.Enabled = hasRow;
            // The text box is disabled as well, not just the button, so there is no way to
            // type a rate while no shop is selected and then wonder where it went.
            txtCommission.Enabled = hasRow;

            // Prefill with the shop's CURRENT rate, so the operator edits a real value
            // rather than typing into a blank box and guessing what it is replacing.
            txtCommission.Text = hasRow ? row.Cells["CommissionRate"].Value.ToString() : "";
            // Assigning Text raises TextChanged, which validates and may show an error. This
            // clears it again, because a value that came from the database is by definition
            // valid and must not greet a fresh selection with a red label.
            UiTheme.ClearError(lblCommissionError, txtCommission);
        }

        private int SelectedId()
        {
            // 0 is the "nothing selected" answer and cannot collide with a real row,
            // because PharmacyId is an IDENTITY column that begins at 1.
            if (dgvPharmacies.CurrentRow == null) return 0;
            // Convert rather than a cast, because the value is boxed out of the DataTable
            // as whatever CLR type the provider picked for a SQL int.
            return Convert.ToInt32(dgvPharmacies.CurrentRow.Cells["PharmacyId"].Value);
        }

        private string SelectedName()
        {
            // The name is only ever used in messages, never to identify the record, so an
            // empty string is a safe answer when nothing is selected. Every action still
            // travels by id.
            if (dgvPharmacies.CurrentRow == null) return "";
            return dgvPharmacies.CurrentRow.Cells["PharmacyName"].Value.ToString();
        }

        // ---------------------------------------------------------------------
        //  ACTIONS
        // ---------------------------------------------------------------------

        private void btnApprove_Click(object sender, EventArgs e)
        {
            // Read the id fresh at the moment of the click rather than caching it when the
            // selection changed, so the action cannot act on a row that is no longer current.
            int id = SelectedId();
            // A silent return, not a message: this button is only enabled for a Pending row,
            // so reaching this line means the selection disappeared between the two events.
            if (id == 0) return;

            string licence = dgvPharmacies.CurrentRow.Cells["LicenseNo"].Value.ToString();

            // The prompt names the two columns that change, because approval is the moment
            // a real pharmacy is allowed to dispense to the public. Quoting the licence
            // number gives the operator the thing they are actually meant to be checking.
            DialogResult answer = MessageBox.Show(
                "Approve " + SelectedName() + "?\r\n\r\nDGDA licence: " + licence + "\r\n\r\n" +
                "Pharmacies.Status becomes 'Approved' and Users.Status becomes 'Active', " +
                "after which the owner can log in and the shop's medicines become visible to customers.",
                "Approve pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Testing against Yes rather than for No, so closing the dialog by any other
            // means also counts as a refusal.
            if (answer != DialogResult.Yes) return;

            // TWO updates inside one transaction, in PharmacyService.Approve: the pharmacy
            // becomes 'Approved' and the OWNER's Users row becomes 'Active'. They are two
            // tables because the shop and the login are separate concerns, and one
            // transaction because either outcome alone is broken - an active owner with an
            // invisible shop, or a live shop whose owner cannot sign in to stock it.
            // The owner is found through a subquery on the Pharmacies row, so this call
            // needs only the PharmacyId.
            _pharmacies.Approve(id);
            // Read the name again rather than reusing a local: the message is composed
            // after the write, from the row still on screen, which has not yet been rebound.
            lblStatus.Text = SelectedName() + " approved.";
            // Reload so Status, the row colour and the three buttons all move together. The
            // approved row does not vanish here, unlike on the dashboard queue - this grid
            // shows every shop whatever its status.
            LoadGrid();
        }

        private void btnSuspend_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            // The rating is quoted back because this is usually the action taken after the
            // low rated report, and it is the evidence for the decision being made.
            string rating = dgvPharmacies.CurrentRow.Cells["AverageRating"].Value.ToString();

            // The prompt describes the three updates and, just as importantly, what does
            // NOT happen. Suspension is the alternative to deletion, so the operator needs
            // to know that choosing it does not destroy anything.
            DialogResult answer = MessageBox.Show(
                "Suspend " + SelectedName() + "?\r\n\r\nAverage customer rating: " + rating + "\r\n\r\n" +
                "Three updates run inside one transaction: the pharmacy becomes Suspended, the owner's " +
                "account becomes Suspended, and every medicine this pharmacy lists has IsActive set to 0.\r\n\r\n" +
                "Nothing is deleted, so the sales history and the invoices customers already hold stay valid.",
                "Suspend pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            // THREE UPDATEs IN ONE TRANSACTION, and no DELETE anywhere.
            // PharmacyService.Suspend wraps all three in BEGIN TRANSACTION ... COMMIT:
            //   1. Pharmacies.Status  = 'Suspended'  so the shop stops trading,
            //   2. Users.Status       = 'Suspended'  so the owner can no longer sign in,
            //   3. Medicines.IsActive = 0            for every medicine the shop lists,
            //      which is what actually removes the stock from the customer catalogue,
            //      because every customer facing query filters on IsActive = 1.
            //
            // One transaction, because any two of the three without the third is a broken
            // state: a suspended shop whose medicines are still on sale, or a delisted
            // catalogue belonging to an owner who can still sign in and relist it. Running
            // them as three separate calls would leave exactly those gaps if the second
            // failed, and the failure would be silent because each statement succeeds on
            // its own terms.
            //
            // Note what is absent. There is no DELETE, and no attempt to cancel or hide the
            // shop's past orders. Orders, OrderItems and the invoices customers already
            // hold are historical fact, and a customer who bought last month must still be
            // able to open their invoice after their pharmacy is suspended today. Every one
            // of the three statements is reversible, which is exactly what Reinstate does.
            _pharmacies.Suspend(id);
            lblStatus.Text = SelectedName() + " suspended. Its medicines are no longer visible to customers.";
            LoadGrid();
        }

        private void btnReinstate_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            // A short prompt, because this is the harmless direction. Putting a shop back is
            // undone by the Suspend button beside it, so it needs no warning.
            DialogResult answer = MessageBox.Show(
                "Put " + SelectedName() + " back on the platform?",
                "Reinstate pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            // The exact mirror of Suspend: the same three rows in the same one transaction,
            // set back to 'Approved', 'Active' and IsActive = 1. It restores rather than
            // recreates, and it only works because suspension deleted nothing in the first
            // place. Had the medicines been removed instead of delisted, there would be
            // nothing here to switch back on.
            _pharmacies.Reinstate(id);
            // No status message: the row turning from red to white, and the buttons
            // swapping over, already say it.
            LoadGrid();
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            // The prompt states the rule before the operator commits, so a refusal
            // afterwards is expected rather than surprising. Delete is enabled for every
            // status precisely because the real test is order history, not status.
            DialogResult answer = MessageBox.Show(
                "Permanently delete " + SelectedName() + " and its owner account?\r\n\r\n" +
                "This is only possible for a shop that has never taken an order. " +
                "A shop with order history must be suspended instead.",
                "Delete pharmacy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            // out parameter rather than a thrown exception, because a refusal here is a
            // normal outcome with a reason attached, not an error. The service counts the
            // shop's orders first and returns false with an explanation if there are any;
            // only when the count is zero does it delete the offers, cart lines, medicines,
            // the pharmacy and finally the owner, in that order, inside one transaction.
            string message;
            if (_pharmacies.Delete(id, out message))
            {
                // Success is quiet: the row disappearing is the confirmation, and the status
                // line carries the wording the service chose.
                lblStatus.Text = message;
                LoadGrid();
            }
            else
            {
                // A refusal gets a dialog rather than the status line, because it means the
                // operator's intent was not carried out and the message tells them to
                // suspend instead. Nothing was written, so there is nothing to reload.
                MessageBox.Show(message, "Cannot delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ---------------------------------------------------------------------
        //  COMMISSION RATE  (requirement 9)
        // ---------------------------------------------------------------------

        private void txtCommission_TextChanged(object sender, EventArgs e)
        {
            // Validation runs on every keystroke rather than on the button press, so the
            // operator is told about a bad value while they can still see what they typed.
            decimal rate;
            // Called before the emptiness test below because C# requires the out parameter
            // to be definitely assigned on every path that reads it.
            bool valid = Validator.IsCommissionRate(txtCommission.Text, out rate);

            // An empty box is not an error, it is an unfinished edit. Clearing the message
            // but disabling the button says "not yet" instead of shouting at someone who
            // has only just deleted the old value in order to type a new one.
            if (string.IsNullOrWhiteSpace(txtCommission.Text))
            {
                UiTheme.ClearError(lblCommissionError, txtCommission);
                btnSetCommission.Enabled = false;
                return;
            }

            // The message names the constraint, CK_Pharmacies_Comm, because that is the
            // rule that would refuse the row anyway. The 0 to 30 bounds are checked here so
            // the operator sees a red label, and again by the database so a bad rate cannot
            // be stored even if this form were bypassed entirely.
            if (valid) UiTheme.ClearError(lblCommissionError, txtCommission);
            else UiTheme.ShowError(lblCommissionError, txtCommission,
                    "The rate must be a number between 0 and 30 (CK_Pharmacies_Comm).");

            // Both conditions: a valid number is not enough if the selection has gone, and
            // a selected shop is not enough if the number is nonsense.
            btnSetCommission.Enabled = valid && dgvPharmacies.CurrentRow != null;
        }

        private void btnSetCommission_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            decimal rate;
            // Validated a second time even though the button is only enabled when the text
            // is valid. The parse has to happen here anyway to obtain the decimal, so
            // testing its result costs nothing and closes the gap if the button were ever
            // enabled by another path.
            if (!Validator.IsCommissionRate(txtCommission.Text, out rate))
            {
                UiTheme.ShowError(lblCommissionError, txtCommission,
                    "The rate must be a number between 0 and 30 (CK_Pharmacies_Comm).");
                return;
            }

            // The rate is a COLUMN on Pharmacies, not a constant in the code, which is what
            // allows one shop to be charged differently from the rest without a rebuild and
            // without touching anybody else's row. The service re-checks the same 0 to 30
            // bounds and returns false unless exactly one row changed.
            if (_pharmacies.SetCommissionRate(id, rate))
            {
                // The wording is the important part. Changing the rate affects only future
                // orders, because every order froze its own CommissionAmount on the header
                // at checkout. Past orders therefore keep the rate they were sold at, and
                // yesterday's sales report cannot change because of a decision made today.
                lblStatus.Text = SelectedName() + " will be charged " + rate.ToString("N2") +
                                 "% commission on orders placed from now on. Past orders keep the rate they were sold at.";
                // Reload so the grid's Comm % column shows the stored value, read back from
                // the database rather than echoed from the text box.
                LoadGrid();
            }
        }

        // ---------------------------------------------------------------------

        // One handler wired to both combo boxes, because the response is identical: rebuild
        // the grid from whatever the three filters now hold.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // The search box deliberately does NOT reload on every keystroke. Searching is an
        // explicit button press, so a long name does not fire one query per character.
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();

        private void btnClear_Click(object sender, EventArgs e)
        {
            // The guard is raised by hand around the three resets. Each assignment below
            // raises a change event that would call LoadGrid, so without this the grid
            // would be queried three times to reach one state.
            _loading = true;
            txtSearch.Clear();
            cmbStatus.SelectedIndex = 0;
            cmbArea.SelectedIndex = 0;
            _loading = false;
            // One deliberate reload, now that all three filters are back at their defaults.
            LoadGrid();
        }

        // Close, not Dispose: the dashboard opened this form inside a using block, so
        // closing hands control back there and the dashboard refreshes itself.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
