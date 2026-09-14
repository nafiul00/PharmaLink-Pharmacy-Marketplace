using System.Data;                  // DataTable, the shape MedicineService.GetForPharmacy returns
using System.Drawing;               // Color for the row colouring further down
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the event argument types
using PharmaLinkApp.Helpers;        // UiTheme: colours, fonts and the grid styling
using PharmaLinkApp.Services;       // MedicineService, the only class here that holds any SQL

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by AdminDashboard. Uses MedicineService.
    //
    //  Load order:
    //      AdminMedicineForm_Load -> ApplyTheme -> LoadGrid
    //
    //  LoadGrid calls MedicineService.GetForPharmacy(UserSession.PharmacyId,
    //  txtSearch.Text, chkShowDelisted.Checked) and assigns the DataTable it
    //  returns to dgvMedicines.DataSource. The column headers are renamed after
    //  that assignment, because binding is what creates the columns.
    //
    //  Typing in the search box raises Filter_Changed, which calls LoadGrid
    //  again, so the filtering is done by SQL rather than inside the grid.
    //  Row colours come from the CellFormatting event, not from the data.
    //  Add and Edit open MedicineEditorForm as a modal dialog; Delist calls
    //  MedicineService.Delist, which sets IsActive = 0 instead of deleting.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirement 11: the full CRUD on the owner's own medicines.
    ///
    /// Create and Update open the modal MedicineEditorForm. Delete is a soft
    /// delete - IsActive is set to 0 - which keeps the foreign keys from
    /// OrderItems intact so every old invoice still resolves, while the item
    /// disappears from the customer catalogue.
    /// </summary>
    public partial class AdminMedicineForm : Form
    {
        // One service for every read and write on this screen, so the form contains no
        // connection, no SQL and no table names of its own.
        private readonly MedicineService _medicines = new MedicineService();
        // True until Load has finished. The search box and the delisted checkbox are both
        // wired to Filter_Changed, which queries; without this flag those events could fire
        // while the form is still being built and run a query against an unstyled grid.
        private bool _loading = true;

        public AdminMedicineForm()
        {
            // Designer generated controls only. No query here: the constructor runs before the
            // window exists, so an error would have nowhere to report itself.
            InitializeComponent();
        }

        private void AdminMedicineForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours, fonts, grid styling and the CellFormatting hook up
            _loading = false;   // from here on the filter controls are allowed to query
            LoadGrid();         // one deliberate first load, now that everything is wired
        }

        // Pure presentation, called once from Load. Besides the styling it subscribes the grid
        // to its CellFormatting handler and writes the isolation hint label, which prints the
        // pharmacy id the queries actually run with so the rule is visible on screen rather
        // than only stated in a comment.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Medicines");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StyleSuccess(btnAdd);
            UiTheme.StylePrimary(btnEdit);
            UiTheme.StyleDanger(btnDelist);
            UiTheme.StyleAccent(btnRelist);
            UiTheme.StyleAccent(btnCreateOffer);
            UiTheme.StyleGrid(dgvMedicines);
            dgvMedicines.CellFormatting += dgvMedicines_CellFormatting;

            lblIsolationHint.Font = UiTheme.FontMono;
            lblIsolationHint.ForeColor = UiTheme.TextMuted;
            lblIsolationHint.Text =
                "Data isolation: every statement on this form carries  WHERE PharmacyId = " + UserSession.PharmacyId +
                "  taken from the session, so another pharmacy's rows can never appear here" + Environment.NewLine +
                "and an update or delete aimed at a row that is not yours simply changes nothing.";

            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private void LoadGrid()
        {
            // Every refresh path on this form comes through here, so one guard covers the first
            // load, the refresh button and both filter controls.
            if (_loading) return;

            try
            {
                // Three arguments, and only the last two come from the screen. The first is
                // UserSession.PharmacyId, set once at login from the Users row: there is no
                // control anywhere on this form holding a pharmacy id, so there is nothing a
                // user could edit to make this query return another shop's medicines.
                //
                // The keyword is Trimmed here rather than in the service, because trailing
                // spaces are a typing artefact of this textbox. The service turns an empty
                // string into (@Keyword = '' OR ... LIKE ...), so a blank search means no
                // filter and one query serves both cases.
                //
                // Filtering in SQL rather than through a DataView or by hiding rows keeps one
                // definition of the filter. It does mean a round trip per keystroke, which is
                // acceptable for a desktop application against a local database and is worth
                // the guarantee that what is on screen is what the database currently holds.
                DataTable table = _medicines.GetForPharmacy(
                    UserSession.PharmacyId, txtSearch.Text.Trim(), chkShowDelisted.Checked);

                // Binding is what CREATES the grid's columns, so every rename below has to come
                // after this assignment.
                dgvMedicines.DataSource = table;

                // If the bind produced no columns, indexing Columns["MedicineId"] would throw.
                // Testing the count turns that into a grid that is merely unstyled.
                if (dgvMedicines.Columns.Count > 0)
                {
                    // Headers are set here rather than aliased in SQL, so the column NAMES stay
                    // stable for the code that reads cells by name while the captions stay
                    // readable for the owner.
                    dgvMedicines.Columns["MedicineId"].HeaderText = "ID";
                    // StyleGrid sets AutoSizeColumnsMode to Fill, so FillWeight is a share of
                    // the available width rather than a pixel count. The narrow columns are
                    // given small shares so the two name columns keep the room they need.
                    dgvMedicines.Columns["MedicineId"].FillWeight = 28;
                    // Brand and generic are both shown because a customer may search for either,
                    // and the owner needs to see what the catalogue will match on.
                    dgvMedicines.Columns["MedicineName"].HeaderText = "Brand";
                    dgvMedicines.Columns["GenericName"].HeaderText = "Generic";
                    dgvMedicines.Columns["CategoryName"].HeaderText = "Category";
                    dgvMedicines.Columns["Manufacturer"].HeaderText = "Manufacturer";
                    dgvMedicines.Columns["Strength"].HeaderText = "Strength";
                    dgvMedicines.Columns["Strength"].FillWeight = 45;
                    // "(Tk)" in the header rather than formatting each cell, which keeps the
                    // value numeric and therefore still sortable by size rather than as text.
                    dgvMedicines.Columns["UnitPrice"].HeaderText = "Price (Tk)";
                    dgvMedicines.Columns["UnitPrice"].FillWeight = 45;
                    // Stock and Min sit next to each other on purpose: the low stock rule is a
                    // comparison between them, so both numbers are visible beside the verdict.
                    dgvMedicines.Columns["Stock"].HeaderText = "Stock";
                    dgvMedicines.Columns["Stock"].FillWeight = 35;
                    dgvMedicines.Columns["MinStock"].HeaderText = "Min";
                    dgvMedicines.Columns["MinStock"].FillWeight = 30;
                    // Abbreviated because it is a tick box column; the full words would take
                    // several times the width for the same information.
                    dgvMedicines.Columns["RequiresRx"].HeaderText = "Rx";
                    dgvMedicines.Columns["RequiresRx"].FillWeight = 26;
                    dgvMedicines.Columns["ExpiryDate"].HeaderText = "Expires";
                    // IsActive is the soft delete flag, captioned as what it MEANS to the owner
                    // rather than as the column name. It also drives the grey row colouring and
                    // the Delist and Relist buttons.
                    dgvMedicines.Columns["IsActive"].HeaderText = "On sale";
                    dgvMedicines.Columns["IsActive"].FillWeight = 40;
                    // A CASE expression in the query, not a stored column. The row colouring
                    // reads it instead of comparing Stock and MinStock again in C#, so there is
                    // exactly one definition of "Low Stock".
                    dgvMedicines.Columns["StockStatus"].HeaderText = "Stock state";
                }

                // The count is taken from the table rather than from a COUNT(*) query, because
                // the rows are already here. Naming the shop alongside it is a second reminder
                // of whose catalogue this is.
                lblStatus.Text = table.Rows.Count + " medicine(s) listed for " + UserSession.PharmacyName + ".";
                // Rebinding clears the selection, so the five action buttons have to be
                // re-evaluated rather than left describing a row that is no longer there.
                UpdateButtons();
            }
            catch (Exception ex)
            {
                // DbHelper has already translated the SqlException into a readable sentence, so
                // it is shown as it stands instead of being wrapped in wording that hides it.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvMedicines_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // CellFormatting fires once per CELL as it is painted, including while scrolling,
            // so it must stay cheap and must never query. The guard covers the header row,
            // whose index is -1, and the moment during a rebind when there are no columns.
            if (e.RowIndex < 0 || dgvMedicines.Columns.Count == 0) return;

            DataGridViewRow row = dgvMedicines.Rows[e.RowIndex];
            object active = row.Cells["IsActive"].Value;
            // Read from the row rather than recalculated: StockStatus is the CASE expression in
            // the query, so the grid cannot contradict the low stock report.
            object stockState = row.Cells["StockStatus"].Value;

            // Delisted is tested FIRST because it outranks the stock state. A medicine that is
            // off sale cannot be sold, so whether it is also below its minimum is irrelevant;
            // painting it red would send the owner off to reorder something customers cannot
            // see. Both a DBNull and a plain null have to be excluded before Convert.ToBoolean,
            // because the first throws and the second is what a cell holds mid rebind.
            if (active != DBNull.Value && active != null && !Convert.ToBoolean(active))
            {
                // Grey background and muted text together, so a delisted row reads as switched
                // off rather than merely tinted a different colour.
                row.DefaultCellStyle.BackColor = Color.FromArgb(238, 238, 238);
                row.DefaultCellStyle.ForeColor = UiTheme.TextMuted;
            }
            else if (stockState != null && stockState.ToString() == "Low Stock")
            {
                // The same pale red the dashboard and the inventory screen use, so red means
                // one thing everywhere in the application. The text colour is set back to dark
                // as well, because this row may previously have been a greyed one.
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
            else
            {
                // The explicit default branch is not redundant. DataGridViewRow objects are
                // REUSED as the grid scrolls, so a healthy row left unpainted would keep the
                // grey or the red of whichever row previously occupied that slot. BOTH colours
                // are reset for the same reason: setting only the background would leave the
                // muted text of a delisted row on a perfectly ordinary one.
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
        }

        // Moving the selection changes which medicine the five buttons would act on, so their
        // enabled state is recalculated rather than left describing the previous row.
        private void dgvMedicines_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            DataGridViewRow row = dgvMedicines.CurrentRow;
            // CurrentRow is null on an empty grid and its cells can still be unpopulated during
            // a rebind, so both are tested before any cell is read.
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            // hasRow is evaluated first, so the cell reads that follow only happen when there
            // is a row to read. DBNull has to be excluded separately because Convert.ToBoolean
            // throws on it rather than returning false.
            bool active = hasRow && row.Cells["IsActive"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsActive"].Value);

            // Edit works on any row: a delisted medicine can still have its price or expiry
            // corrected before it goes back on sale.
            btnEdit.Enabled = hasRow;
            // Delist and Relist are mutually exclusive by construction, driven by the same flag
            // from opposite sides. Only one of the pair is ever available, so the state of the
            // row is readable from the buttons and neither action can be applied twice.
            btnDelist.Enabled = hasRow && active;
            btnRelist.Enabled = hasRow && !active;
            // There is no point discounting something customers cannot see, so an offer can
            // only be created against a medicine that is actually on sale.
            btnCreateOffer.Enabled = hasRow && active;
        }

        private int SelectedId()
        {
            // 0 is the "nothing selected" answer. MedicineId is an IDENTITY starting at 1, so
            // no real row can collide with it and every caller can test for it without a
            // nullable int or an extra out parameter.
            if (dgvMedicines.CurrentRow == null) return 0;
            // Convert rather than a cast: the cell holds a boxed value whose exact CLR type is
            // whatever the provider chose for the column, and a cast would throw on any other
            // numeric type.
            return Convert.ToInt32(dgvMedicines.CurrentRow.Cells["MedicineId"].Value);
        }

        private string SelectedName()
        {
            // The empty string is the safe answer with no row, because the name is only ever
            // used inside a message. Returning null here would push a null check into every
            // caller that concatenates it.
            if (dgvMedicines.CurrentRow == null) return "";
            return dgvMedicines.CurrentRow.Cells["MedicineName"].Value.ToString();
        }

        // ---------------------------------------------------------------------

        private void btnAdd_Click(object sender, EventArgs e)
        {
            // Id 0 tells the editor this is an INSERT rather than an update, so one dialog
            // serves both the create and the edit case instead of two near identical forms
            // drifting apart. The second argument is false because the caret belongs in the
            // name box on a new medicine, not in the stock box.
            // The editor sets PharmacyId from the session itself, so this form does not pass
            // one and there is no route by which the wrong shop could be recorded.
            using (MedicineEditorForm editor = new MedicineEditorForm(0, false))
            {
                // Only an OK result means something was saved. Closing or cancelling leaves the
                // status line as it was rather than claiming a medicine was added.
                if (editor.ShowDialog(this) == DialogResult.OK)
                    lblStatus.Text = "Medicine added and immediately visible to customers.";
            }
            // Reloaded outside the using and on BOTH paths, because the safest assumption after
            // a modal dialog is that something changed. A cancel simply costs one query.
            LoadGrid();
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;   // nothing selected, so there is nothing to edit

            // A non zero id is what turns the same dialog into an UPDATE. The editor loads that
            // row through GetForEdit, which carries the session's pharmacy id in its WHERE
            // clause, so opening another shop's medicine id would load nothing at all.
            using (MedicineEditorForm editor = new MedicineEditorForm(id, false))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                    lblStatus.Text = "Medicine updated.";
            }
            LoadGrid();
        }

        private void dgvMedicines_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // double clicking the header must not open an editor
            // Calls the Edit handler rather than repeating its body, so the double click
            // shortcut and the button can never behave differently. The handler reads
            // CurrentRow, which the double click has already moved to the row under the
            // pointer, so no row index needs to be passed. EventArgs.Empty is used because the
            // handler ignores its arguments entirely.
            btnEdit_Click(sender, EventArgs.Empty);
        }

        private void btnDelist_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            // The dialog explains the MECHANISM, not just the consequence. "Take off sale"
            // sounds like a deletion, and an owner who believes the row has gone would not
            // expect to find it again under Show delisted, so the message says plainly that
            // nothing is removed.
            DialogResult answer = MessageBox.Show(
                "Take " + SelectedName() + " off sale?\r\n\r\n" +
                "This is a soft delete: IsActive is set to 0. The row stays in the database so the " +
                "OrderItems rows on past invoices still resolve, but customers stop seeing the medicine.",
                "Delist medicine", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Tested against Yes rather than for No, so closing the dialog with the cross or
            // Escape also means "do nothing". Only an explicit Yes writes.
            if (answer != DialogResult.Yes) return;

            // A soft delete: the service issues UPDATE ... SET IsActive = 0, never a DELETE.
            // OrderItems rows on past invoices hold a foreign key to this medicine, so a real
            // delete would either be refused by the database or, with a cascade, would destroy
            // invoice history. The session's pharmacy id is in the WHERE clause beside the
            // medicine id, so a delist aimed at another shop's row matches nothing.
            _medicines.Delist(id, UserSession.PharmacyId);
            // SelectedName still returns the right name because this line runs BEFORE the
            // reload rebinds the grid and clears the selection. The message names the flag and
            // states that no row was deleted, so the owner can see it again with Show delisted.
            lblStatus.Text = SelectedName() + " delisted (IsActive = 0). No row was deleted.";
            LoadGrid();
        }

        private void btnRelist_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            // No confirmation dialog, unlike Delist. Putting a medicine back on sale is not
            // destructive and is undone by one click of Delist, so a prompt would only train
            // the owner to dismiss prompts without reading them.
            // Relist is the exact mirror of Delist: SET IsActive = 1, same two ids in the WHERE.
            _medicines.Relist(id, UserSession.PharmacyId);
            lblStatus.Text = SelectedName() + " is on sale again.";
            LoadGrid();   // the row loses its grey colouring, which is the visible confirmation
        }

        private void btnCreateOffer_Click(object sender, EventArgs e)
        {
            int id = SelectedId();
            if (id == 0) return;

            // The medicine id is passed to the constructor so the offers screen opens already
            // pointed at this product, instead of making the owner find it again in a list they
            // have just been looking at.
            using (DiscountOffersForm offers = new DiscountOffersForm(id))
            {
                offers.ShowDialog(this);
            }
            // Reloaded on return because the offers screen can also be used to edit the
            // medicine's own figures, and because a refresh after any modal dialog is the
            // cheaper assumption.
            LoadGrid();
        }

        // Both the search box and the delisted checkbox point at this one handler, because the
        // response to either is identical: re-run the query with whatever both controls now
        // hold. Sharing it means the two filters can never fall out of step.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close, not Hide: the dashboard opened this form with ShowDialog, disposes it and then
        // refreshes itself, so closing is all this button has to do.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
