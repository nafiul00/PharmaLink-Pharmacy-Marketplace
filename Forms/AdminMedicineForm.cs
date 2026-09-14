using System.Data;                  // DataTable, what MedicineService.GetForPharmacy returns
using System.Drawing;               // Color, for the row colouring further down
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the event arg types
using PharmaLinkApp.Helpers;        // UiTheme: colours, fonts and the grid styling
using PharmaLinkApp.Services;       // MedicineService, the only class here holding any SQL

// All screens share one namespace, so forms open each other by short name.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 11: full CRUD on the owner's own medicines.</summary>
    public partial class AdminMedicineForm : Form
    {
        // One service for every read and write, so this form holds no SQL of its own.
        private readonly MedicineService _medicines = new MedicineService();
        // True until Load finishes, so the filter controls cannot query a half built form.
        private bool _loading = true;

        // The constructor runs before the window exists, so every query is left to Load.
        public AdminMedicineForm()
        {
            InitializeComponent();   // controls only: an error here would have nowhere to report
        }

        // Load fires once every control exists, so the first query is safe here.
        private void AdminMedicineForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours, fonts, grid styling and the CellFormatting hook up
            _loading = false;   // from here the filter controls are allowed to query
            LoadGrid();         // one deliberate first load, now that everything is wired
        }

        // Pure presentation, plus the isolation hint that prints the id the queries use.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Medicines");   // title, background and window rules

            panelHeader.BackColor = UiTheme.Primary;                 // the dark band above the work area
            lblTitle.Font = UiTheme.FontTitle;                       // the shared title font
            lblTitle.ForeColor = Color.White;                        // white is the readable pairing on Primary
            lblSubtitle.Font = UiTheme.FontSmall;                    // smaller: a subtitle, not a second heading
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // dimmed white, so it supports the title

            UiTheme.StyleSecondary(btnBack);        // outline: leaving is not what this screen is for
            UiTheme.StyleSecondary(btnRefresh);     // also secondary, re-reading is a convenience
            UiTheme.StyleSuccess(btnAdd);           // green: adding is the one purely additive action
            UiTheme.StylePrimary(btnEdit);          // filled: editing is the most used action here
            UiTheme.StyleDanger(btnDelist);         // red, warning that customers lose sight of it
            UiTheme.StyleAccent(btnRelist);         // accent, so Relist is not mistaken for Add
            UiTheme.StyleAccent(btnCreateOffer);    // the same accent, grouping the secondary actions
            UiTheme.StyleGrid(dgvMedicines);        // the same grid rules as every other screen
            dgvMedicines.CellFormatting += dgvMedicines_CellFormatting;   // wired here, beside the grid it colours

            lblIsolationHint.Font = UiTheme.FontMono;        // monospaced: the line quotes a WHERE clause
            lblIsolationHint.ForeColor = UiTheme.TextMuted;  // muted: an explanation, not a status
            // Printed on screen, so the scoping rule can be read rather than taken on trust.
            lblIsolationHint.Text =
                "Data isolation: every statement on this form carries  WHERE PharmacyId = " + UserSession.PharmacyId +      // the real session value
                "  taken from the session, so another pharmacy's rows can never appear here" + Environment.NewLine +        // names the source of the id
                "and an update or delete aimed at a row that is not yours simply changes nothing.";                         // the failure mode is a no-op

            lblStatus.Font = UiTheme.FontSmall;         // the live row count, rewritten each load
            lblStatus.ForeColor = UiTheme.TextMuted;    // neutral: the wording carries any tone
        }

        // The single read path: Load, Refresh and both filters all come through here.
        private void LoadGrid()
        {
            if (_loading) return;   // one guard covering the first load, Refresh and the filters

            try   // the query crosses the network, so a failure must become a message
            {
                // Only the last two arguments come from the screen; the id comes from the session.
                DataTable table = _medicines.GetForPharmacy(      // filtering in SQL keeps one definition of the filter
                    UserSession.PharmacyId, txtSearch.Text.Trim(), chkShowDelisted.Checked);   // all three travel as parameters

                // Binding is what CREATES the columns, so every rename below must follow it.
                dgvMedicines.DataSource = table;

                if (dgvMedicines.Columns.Count > 0)   // with no columns, indexing one would throw
                {
                    // Headers are set here, not aliased in SQL, so the column names stay stable.
                    dgvMedicines.Columns["MedicineId"].HeaderText = "ID";
                    // Fill mode, so FillWeight is a share of the width rather than pixels.
                    dgvMedicines.Columns["MedicineId"].FillWeight = 28;
                    // Both names are shown, because a customer may search on either one.
                    dgvMedicines.Columns["MedicineName"].HeaderText = "Brand";
                    dgvMedicines.Columns["GenericName"].HeaderText = "Generic";           // the name a prescription uses
                    dgvMedicines.Columns["CategoryName"].HeaderText = "Category";         // the joined name, not CategoryId
                    dgvMedicines.Columns["Manufacturer"].HeaderText = "Manufacturer";     // tells two look alike strips apart
                    dgvMedicines.Columns["Strength"].HeaderText = "Strength";             // 500mg and 665mg are different products
                    dgvMedicines.Columns["Strength"].FillWeight = 45;                     // narrow: a few characters
                    // "(Tk)" in the header keeps the cell numeric, so it still sorts by size.
                    dgvMedicines.Columns["UnitPrice"].HeaderText = "Price (Tk)";
                    dgvMedicines.Columns["UnitPrice"].FillWeight = 45;                    // the same share as Strength
                    // Stock sits beside Min, because the low stock rule compares the two.
                    dgvMedicines.Columns["Stock"].HeaderText = "Stock";
                    dgvMedicines.Columns["Stock"].FillWeight = 35;                        // rarely more than three digits
                    dgvMedicines.Columns["MinStock"].HeaderText = "Min";                  // abbreviated to save width for names
                    dgvMedicines.Columns["MinStock"].FillWeight = 30;                     // the narrowest, read only beside Stock
                    // Abbreviated because it is a tick box: the words would cost real width.
                    dgvMedicines.Columns["RequiresRx"].HeaderText = "Rx";
                    dgvMedicines.Columns["RequiresRx"].FillWeight = 26;                   // just wide enough for the tick
                    dgvMedicines.Columns["ExpiryDate"].HeaderText = "Expires";            // a fact about the stock, not a field name
                    // IsActive is the soft delete flag, captioned as what it means to the owner.
                    dgvMedicines.Columns["IsActive"].HeaderText = "On sale";
                    dgvMedicines.Columns["IsActive"].FillWeight = 40;                     // wide enough for the header itself
                    // A CASE expression in the query, so "Low Stock" has exactly one definition.
                    dgvMedicines.Columns["StockStatus"].HeaderText = "Stock state";
                }

                // Counted from the table, since the rows are already here; the shop is named too.
                lblStatus.Text = table.Rows.Count + " medicine(s) listed for " + UserSession.PharmacyName + ".";
                UpdateButtons();   // rebinding clears the selection, so the buttons are re-decided
            }
            catch (Exception ex)   // a failed read must not close a screen mid task
            {
                // DbHelper already turned the SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Raised as each cell is painted, so colours come from the row's own data.
        private void dgvMedicines_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Fires per cell, including while scrolling, so it stays cheap and never queries.
            if (e.RowIndex < 0 || dgvMedicines.Columns.Count == 0) return;

            DataGridViewRow row = dgvMedicines.Rows[e.RowIndex];   // the row being painted, not the selected one
            object active = row.Cells["IsActive"].Value;           // object, because a cast would throw on DBNull
            // Read, not recalculated: StockStatus is the CASE expression from the query.
            object stockState = row.Cells["StockStatus"].Value;

            // Delisted is tested first: an off sale row cannot be sold anyway.
            if (active != DBNull.Value && active != null && !Convert.ToBoolean(active))
            {
                // Grey fill and muted text together, so the row reads as switched off.
                row.DefaultCellStyle.BackColor = Color.FromArgb(238, 238, 238);
                row.DefaultCellStyle.ForeColor = UiTheme.TextMuted;   // dimming the text is what sells it
            }
            else if (stockState != null && stockState.ToString() == "Low Stock")   // only rows still on sale
            {
                // The same pale red the dashboard uses, so red means one thing everywhere.
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;   // restored, since this row may have been grey
            }
            else   // on sale and adequately stocked: the ordinary case, still painted
            {
                // Rows are REUSED while scrolling, so an unpainted one keeps old colours.
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;   // both reset, or muted text would linger
            }
        }

        // Moving the selection changes which medicine the buttons would act on.
        private void dgvMedicines_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        // The one place the five buttons are switched, so no two describe two rows.
        private void UpdateButtons()
        {
            DataGridViewRow row = dgvMedicines.CurrentRow;   // CurrentRow is null on an empty grid
            // CurrentRow can also hold unpopulated cells mid-rebind, so both are tested.
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            // hasRow first, so the cell reads only happen when there is a row to read.
            bool active = hasRow && row.Cells["IsActive"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsActive"].Value);   // DBNull excluded: Convert throws on it

            // Edit works on any row: a delisted medicine can still have its price corrected.
            btnEdit.Enabled = hasRow;
            // Delist and Relist are driven by the same flag from opposite sides.
            btnDelist.Enabled = hasRow && active;
            btnRelist.Enabled = hasRow && !active;   // the exact negation, so only one is ever live
            // No point discounting something customers cannot see.
            btnCreateOffer.Enabled = hasRow && active;
        }

        // The id every action works from, so no handler reads a cell itself.
        private int SelectedId()
        {
            // 0 means nothing selected; MedicineId is an IDENTITY starting at 1.
            if (dgvMedicines.CurrentRow == null) return 0;
            // Convert, not a cast: the cell holds a boxed value of the provider's own type.
            return Convert.ToInt32(dgvMedicines.CurrentRow.Cells["MedicineId"].Value);
        }

        // The display name for messages only, which is why "" is a safe answer.
        private string SelectedName()
        {
            if (dgvMedicines.CurrentRow == null) return "";   // "" saves a null check in every caller
            return dgvMedicines.CurrentRow.Cells["MedicineName"].Value.ToString();   // NOT NULL, so no DBNull guard
        }

        // Create: opens the shared editor in insert mode, then reloads whatever it did.
        private void btnAdd_Click(object sender, EventArgs e)
        {
            // Id 0 means INSERT, so one dialog serves both create and edit.
            using (MedicineEditorForm editor = new MedicineEditorForm(0, false))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)   // only OK means something was saved
                    lblStatus.Text = "Medicine added and immediately visible to customers.";   // no publish step to hunt for
            }
            // Reloaded on both paths: after a modal dialog, assuming a change is safest.
            LoadGrid();
        }

        // Update: the same editor as Add, distinguished only by the id it is handed.
        private void btnEdit_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // read once, so a moved selection cannot redirect the dialog
            if (id == 0) return;   // nothing selected, so there is nothing to edit

            // A non zero id turns the dialog into an UPDATE of that one row.
            using (MedicineEditorForm editor = new MedicineEditorForm(id, false))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)                 // ShowDialog(this) sets the owner window
                    lblStatus.Text = "Medicine updated.";                       // short: the grid is about to show it
            }
            LoadGrid();   // reloaded on both paths, since a cancel costs only one query
        }

        // A double click is the same gesture as Edit, so it routes to that handler.
        private void dgvMedicines_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // double clicking the header must not open an editor
            // Calls the handler rather than repeating it, so the two can never differ.
            btnEdit_Click(sender, EventArgs.Empty);
        }

        // The soft delete: a flag, never a DELETE, so old invoices still resolve.
        private void btnDelist_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // captured before the dialog, so a rebind cannot redirect it
            if (id == 0) return;     // nothing selected, so there is nothing to take off sale

            // The prompt explains the mechanism: "off sale" otherwise sounds like a deletion.
            DialogResult answer = MessageBox.Show(
                "Take " + SelectedName() + " off sale?\r\n\r\n" +                                                        // names the medicine, so the row is confirmed
                "This is a soft delete: IsActive is set to 0. The row stays in the database so the " +                   // says what actually happens
                "OrderItems rows on past invoices still resolve, but customers stop seeing the medicine.",               // why it is a flag, not a DELETE
                "Delist medicine", MessageBoxButtons.YesNo, MessageBoxIcon.Question);                                    // Yes/No: the prompt is a question

            if (answer != DialogResult.Yes) return;   // the cross and Escape also mean "do nothing"

            // UPDATE ... SET IsActive = 0, with the session's pharmacy id in the WHERE.
            _medicines.Delist(id, UserSession.PharmacyId);
            // Read before the reload clears the selection, and it says nothing was deleted.
            lblStatus.Text = SelectedName() + " delisted (IsActive = 0). No row was deleted.";
            LoadGrid();   // the row greys out or disappears: the visible confirmation
        }

        // The exact inverse of Delist, and cheaper: putting a row back destroys nothing.
        private void btnRelist_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // the same read-once pattern as every other action here
            if (id == 0) return;     // nothing selected, so there is nothing to put back on sale

            // No prompt: this is not destructive, and one click of Delist undoes it.
            _medicines.Relist(id, UserSession.PharmacyId);
            lblStatus.Text = SelectedName() + " is on sale again.";   // read while the selection still holds
            LoadGrid();   // the row loses its grey, which is the visible confirmation
        }

        // Opens the offers screen already pointed at the selected medicine.
        private void btnCreateOffer_Click(object sender, EventArgs e)
        {
            int id = SelectedId();   // the id the offers screen will open against
            if (id == 0) return;     // nothing selected, so there is no product to discount

            // Passed to the constructor, so the owner does not have to find the row twice.
            using (DiscountOffersForm offers = new DiscountOffersForm(id))
            {
                offers.ShowDialog(this);   // modal and owned, so the grid cannot move underneath it
            }
            // Reloaded on return, because the offers screen can change this medicine too.
            LoadGrid();
        }

        // Both the search box and the delisted tick box share this, so they cannot drift.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close, not Hide: the dashboard used ShowDialog and refreshes itself after.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
