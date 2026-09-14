using System.Data;                  // DataTable and DataRow: the tiles are totalled by walking rows
using System.Drawing;               // Color, Point and Size for the tiles and the row colouring
using System.Windows.Forms;         // Form, Label, DataGridView and the event argument types
using PharmaLinkApp.Helpers;        // UiTheme and Validator: styling, the money format, input checks
using PharmaLinkApp.Services;       // MedicineService, the only class here that knows any SQL

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by AdminDashboard. Uses MedicineService.
    //
    //  Load order:
    //      AdminInventoryForm_Load -> ApplyTheme -> BuildTiles -> LoadEverything
    //
    //  LoadEverything runs two separate queries: GetLowStock fills dgvLowStock
    //  and GetInventory fills dgvInventory, each by assigning the returned
    //  DataTable to that grid's DataSource. The four tile figures are totalled
    //  in C# by walking the inventory table, because they are sums of a result
    //  set that has already been fetched.
    //
    //  Stock is compared against MinStock inside the SQL, so ShortfallUnits
    //  arrives already calculated rather than being worked out on screen.
    //  Restock reads txtRestockUnits through Validator.IsPositiveInt, calls
    //  MedicineService.AddStock and then reloads both grids.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirement 12. Four summary tiles, the low stock alert panel and the
    /// full inventory grid.
    ///
    /// The alert compares two columns of the same row - Stock against MinStock -
    /// rather than using a fixed threshold, because ten boxes of a glucometer is
    /// plenty while ten strips of Napa is nothing. The shortfall column tells
    /// the owner how many units to order.
    /// </summary>
    public partial class AdminInventoryForm : Form
    {
        // One service for the whole form. Every read and the restock write go through it,
        // so this screen contains no connection, no SQL and no table names.
        private readonly MedicineService _medicines = new MedicineService();

        // Only the number Labels are kept; BuildTiles places the Panels once and they never
        // change afterwards, while these four are rewritten on every reload.
        private Label _tileItems;
        private Label _tileLowStock;
        private Label _tileUnitsInStock;
        private Label _tileStockValue;

        public AdminInventoryForm()
        {
            // Designer generated controls only. No query here: the constructor runs before
            // the window exists, so an error would have nowhere to be reported.
            InitializeComponent();
        }

        private void AdminInventoryForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();        // colours, fonts, grid styling and the CellFormatting hook ups
            BuildTiles();        // the four panels, built in code so they match every other screen
            LoadEverything();    // first and only load; every later refresh calls the same method
        }

        // Pure presentation, called once from Load. It also subscribes both grids to their
        // CellFormatting handlers, which is what makes the red alert colouring below live.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Stock and Inventory");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            lblAlertTitle.Font = UiTheme.FontHeading;
            lblAlertTitle.ForeColor = UiTheme.Danger;
            lblInventoryTitle.Font = UiTheme.FontHeading;
            lblInventoryTitle.ForeColor = UiTheme.TextDark;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
            lblRestockError.Font = UiTheme.FontSmall;
            lblRestockError.ForeColor = UiTheme.Danger;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSuccess(btnRestock);
            UiTheme.StyleSecondary(btnOpenEditor);
            UiTheme.StyleGrid(dgvLowStock);
            UiTheme.StyleGrid(dgvInventory);
            dgvLowStock.CellFormatting += dgvLowStock_CellFormatting;
            dgvInventory.CellFormatting += dgvInventory_CellFormatting;
        }

        private void BuildTiles()
        {
            // The four tiles answer the questions an owner asks about stock in order: how many
            // products do I list, how many need reordering, how many units am I holding, and
            // what is that worth. Red is given to the count that demands action and nothing
            // else, so a red figure on this screen always means the same thing.
            Panel t1 = UiTheme.BuildTile("MEDICINES ON SALE", UiTheme.Primary, out _tileItems);
            Panel t2 = UiTheme.BuildTile("BELOW MINIMUM STOCK", UiTheme.Danger, out _tileLowStock);
            Panel t3 = UiTheme.BuildTile("UNITS ON THE SHELF", UiTheme.Accent, out _tileUnitsInStock);
            Panel t4 = UiTheme.BuildTile("VALUE OF STOCK HELD", UiTheme.Warning, out _tileStockValue);

            // One starting position and one step, so the row stays evenly spaced however the
            // tiles are edited later. Writing four Location lines by hand would work until one
            // of them was changed and the spacing quietly went wrong.
            int x = 20;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                // One call replaces four lines. PlaceTile sets the location and size,
                // adds the tile to the FORM rather than to a container, and brings it to
                // the front, because the designer's own controls were added first and
                // would otherwise paint over it. Centralised in UiTheme so every
                // dashboard places its tiles the same way on a scaled display.
                UiTheme.PlaceTile(this, tile, x, 84, 278, 84);
                x += 294;
            }
        }

        private void LoadEverything()
        {
            try
            {
                // FIRST QUERY: only the medicines that are actually below their own minimum.
                // The filtering is done by the WHERE clause rather than by fetching everything
                // and testing it here, so the alert panel and the count beside it can never
                // disagree about what "low" means.
                // UserSession.PharmacyId, taken from the login and never from a control on the
                // screen: that is the whole of the isolation rule. There is no textbox holding
                // a pharmacy id anywhere on this form, so there is nothing for a user to edit
                // in order to see another shop's shelves.
                DataTable lowStock = _medicines.GetLowStock(UserSession.PharmacyId);
                dgvLowStock.DataSource = lowStock;

                // Binding is what creates the columns, so the renames come after the assignment
                // and the guard stops a lookup by name from throwing if the bind produced none.
                if (dgvLowStock.Columns.Count > 0)
                {
                    dgvLowStock.Columns["MedicineId"].HeaderText = "ID";
                    // StyleGrid sets AutoSizeColumnsMode to Fill, so FillWeight is a share of
                    // the available width rather than a pixel count.
                    dgvLowStock.Columns["MedicineId"].FillWeight = 30;
                    dgvLowStock.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvLowStock.Columns["Strength"].HeaderText = "Strength";
                    dgvLowStock.Columns["CategoryName"].HeaderText = "Category";
                    dgvLowStock.Columns["Stock"].HeaderText = "In stock";
                    // The two columns side by side are the point of the panel: the alert is a
                    // comparison between them, so both are shown rather than only the verdict.
                    dgvLowStock.Columns["MinStock"].HeaderText = "Minimum level";
                    // (MinStock - Stock), computed by the query. It is shown as the number to
                    // order because that is the decision the owner has to make, and the restock
                    // box below is pre-filled from this same column.
                    dgvLowStock.Columns["ShortfallUnits"].HeaderText = "Shortfall (units to order)";
                }

                // SECOND QUERY: every listed medicine, not just the low ones, with units sold
                // brought in by a LEFT JOIN so a product that has never sold still appears with
                // a zero rather than dropping out of the list.
                DataTable inventory = _medicines.GetInventory(UserSession.PharmacyId);
                dgvInventory.DataSource = inventory;

                if (dgvInventory.Columns.Count > 0)
                {
                    dgvInventory.Columns["MedicineId"].HeaderText = "ID";
                    dgvInventory.Columns["MedicineId"].FillWeight = 28;
                    dgvInventory.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvInventory.Columns["Strength"].HeaderText = "Strength";
                    dgvInventory.Columns["Strength"].FillWeight = 45;
                    dgvInventory.Columns["CategoryName"].HeaderText = "Category";
                    dgvInventory.Columns["UnitsRemaining"].HeaderText = "Units left";
                    dgvInventory.Columns["UnitsSold"].HeaderText = "Units sold";
                    dgvInventory.Columns["MinStock"].HeaderText = "Min level";
                    dgvInventory.Columns["UnitPrice"].HeaderText = "Price (Tk)";
                    // Stock * UnitPrice, cast to DECIMAL(12,2) by the query rather than
                    // multiplied here, so the column stays numeric and the tile below can sum it.
                    dgvInventory.Columns["StockValue"].HeaderText = "Stock value (Tk)";
                    // A CASE expression in SQL, not a stored column. The row colouring reads it
                    // instead of comparing the numbers again in C#, so there is one definition
                    // of "Low Stock" and the grid cannot contradict the alert panel.
                    dgvInventory.Columns["StockStatus"].HeaderText = "State";
                    dgvInventory.Columns["ExpiryDate"].HeaderText = "Expires";
                }

                // The two totals are worked out by walking the table that is ALREADY in memory.
                // Asking the database for SUM(Stock) and SUM(StockValue) would be a third round
                // trip for numbers that are sums of the rows just fetched, and the two answers
                // could differ if a sale landed in between, leaving the tile disagreeing with
                // the grid directly beneath it.
                int units = 0;
                decimal value = 0m;
                foreach (DataRow row in inventory.Rows)
                {
                    units += DbHelperInt(row, "UnitsRemaining");
                    value += DbHelperDecimal(row, "StockValue");
                }

                // Rows.Count is used for the two counts rather than a COUNT(*) query, for the
                // same reason: the rows are already here, so counting them costs nothing and
                // guarantees the tile matches the grid.
                _tileItems.Text = inventory.Rows.Count.ToString();
                _tileLowStock.Text = lowStock.Rows.Count.ToString();
                _tileUnitsInStock.Text = units.ToString("N0");   // a count, so no decimal places
                _tileStockValue.Text = UiTheme.Money(value);     // one money format for the whole application

                // The panel heading carries the state. An empty alert grid under a heading that
                // still said "Low stock alert" would read as a screen that failed to load, so
                // the zero case says in words that nothing needs ordering.
                lblAlertTitle.Text = lowStock.Rows.Count == 0
                    ? "Low stock alert  -  nothing is below its minimum level right now"
                    : "Low stock alert  (" + lowStock.Rows.Count + " medicine(s) need reordering)";

                // The status line names the shortcut, which is otherwise invisible, and prints
                // the actual pharmacy id the queries ran with. Showing the value makes the
                // isolation rule observable on screen instead of being a claim in a comment.
                lblStatus.Text = "Double click any inventory row to open the medicine editor. " +
                                 "Every query on this form carries WHERE PharmacyId = " + UserSession.PharmacyId + ".";

                // The selection may have moved or vanished during the rebind, so the restock
                // controls are re-evaluated against whatever is selected now.
                UpdateRestockButton();
            }
            catch (Exception ex)
            {
                // One catch around both queries and the totalling. DbHelper has already turned
                // the SqlException into a readable sentence, so it is shown unwrapped.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // These two mirror DbHelper.GetInt and GetDecimal but live here, so this form keeps its
        // dependency on the data layer down to the service it calls. They exist because
        // Convert.ToInt32(DBNull.Value) throws rather than returning zero: any column reached
        // through an outer join can arrive null, and a tile must show 0 rather than the screen
        // failing on one empty cell. static because neither touches the form's state.
        private static int DbHelperInt(DataRow row, string column)
        {
            return row[column] == DBNull.Value ? 0 : Convert.ToInt32(row[column]);
        }

        private static decimal DbHelperDecimal(DataRow row, string column)
        {
            // Convert rather than a cast, because the boxed value's exact CLR type depends on
            // what the provider chose for the column, and a cast on a boxed value of any other
            // numeric type throws at run time.
            return row[column] == DBNull.Value ? 0m : Convert.ToDecimal(row[column]);
        }

        /// <summary>Rows in the alert panel are painted red through the CellFormatting event.</summary>
        private void dgvLowStock_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // CellFormatting fires once per CELL as it is painted, including during scrolling,
            // so the handler must be cheap and must never query. RowIndex is -1 for the header
            // row, which has nothing to colour.
            if (e.RowIndex < 0) return;

            // No condition, unlike the inventory grid below: the query behind this grid already
            // filtered to Stock < MinStock, so every row present IS a low stock row. Re-testing
            // the numbers here would be a second copy of the rule that could drift away from
            // the WHERE clause and leave a red panel with a row in it that is no longer low.
            //
            // Colouring is done here rather than by setting a colour column in the data, because
            // presentation belongs to the form: the same DataTable is also totalled into tiles,
            // and adding display concerns to it would push them into the service.
            //
            // LowStockBack is a pale red rather than a saturated one, so the row reads as an
            // alert while the black text on top of it stays legible. It is the same colour the
            // dashboard and the medicines screen use for the same meaning, so red is never
            // ambiguous anywhere in the application.
            dgvLowStock.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        private void dgvInventory_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Two guards: the header row has index -1, and the event can fire mid rebind when
            // there are no columns to index by name.
            if (e.RowIndex < 0 || dgvInventory.Columns.Count == 0) return;

            DataGridViewRow row = dgvInventory.Rows[e.RowIndex];
            // The verdict is READ from the row, not recalculated. StockStatus is the CASE
            // expression in the query, so the colour on screen and the alert panel are driven
            // by one rule in one place.
            object state = row.Cells["StockStatus"].Value;
            if (state == null) return;   // cells can still be unpopulated while the grid binds

            row.DefaultCellStyle.BackColor = state.ToString() == "Low Stock"
                ? UiTheme.LowStockBack
                // The explicit white branch is not redundant. DataGridViewRow objects are
                // REUSED as the grid scrolls, so a healthy row left unpainted would keep the
                // red of whichever low stock row previously occupied that slot, and the alert
                // would appear to be scattered over products that are perfectly well stocked.
                : Color.White;
        }

        // ---------------------------------------------------------------------
        //  RESTOCK
        // ---------------------------------------------------------------------

        // Moving the selection changes which medicine would be restocked, so the button state
        // and the pre-filled quantity are both recalculated.
        private void dgvLowStock_SelectionChanged(object sender, EventArgs e) => UpdateRestockButton();

        private void txtRestockUnits_TextChanged(object sender, EventArgs e)
        {
            int units;
            // Validation runs on every keystroke rather than only on the button press, so the
            // owner is told about a bad value while the caret is still in the box instead of
            // after committing to an action.
            if (string.IsNullOrWhiteSpace(txtRestockUnits.Text))
            {
                // An EMPTY box is not an error, it is the starting state. Showing a red message
                // before anything has been typed would make a fresh form look broken, which is
                // why the empty case is handled separately from the invalid one.
                UiTheme.ClearError(lblRestockError, txtRestockUnits);
            }
            else if (!Validator.IsPositiveInt(txtRestockUnits.Text, out units))
            {
                // IsPositiveInt is int.TryParse plus a greater than zero test, so this one call
                // rejects letters, decimals, negatives and zero. Adding zero units would be a
                // pointless write, and a negative would silently REMOVE stock through the same
                // Stock = Stock + @Units statement, which is the real reason zero is refused
                // here rather than merely ignored.
                UiTheme.ShowError(lblRestockError, txtRestockUnits,
                    "Enter a whole number of units greater than zero.");
            }
            else
            {
                // Valid again, so the red message and the tinted background are both removed.
                // Clearing here rather than only on a successful save stops a stale error from
                // sitting under a box that has since been corrected.
                UiTheme.ClearError(lblRestockError, txtRestockUnits);
            }

            UpdateRestockButton();   // the typed value is half of what enables the button
        }

        private void UpdateRestockButton()
        {
            int units;
            // A restock needs BOTH a selected medicine and a valid quantity, so each is tested
            // separately and the button reflects the pair.
            bool hasRow = dgvLowStock.CurrentRow != null && dgvLowStock.CurrentRow.Cells["MedicineId"].Value != null;
            bool unitsOk = Validator.IsPositiveInt(txtRestockUnits.Text, out units);

            // Disabled rather than enabled-then-refused: an action that cannot succeed should
            // not be offered. The same checks are repeated inside the click handler, because a
            // disabled button is a courtesy and the guard there is the guarantee.
            btnRestock.Enabled = hasRow && unitsOk;
            // The editor only needs a row; it does its own validation, so the quantity is
            // irrelevant to it.
            btnOpenEditor.Enabled = hasRow;

            // Pre-fill the shortfall so the obvious amount is one click away.
            // Only when the box is BLANK. Overwriting a number the owner has already typed
            // every time the selection changed would silently replace a deliberate order
            // quantity with the minimum, and the first sign of it would be the wrong amount of
            // stock arriving.
            if (hasRow && string.IsNullOrWhiteSpace(txtRestockUnits.Text))
            {
                object shortfall = dgvLowStock.CurrentRow.Cells["ShortfallUnits"].Value;
                // Both null and DBNull are possible: the first while the grid is binding, the
                // second if the column ever arrives empty. Neither can be turned into text.
                if (shortfall != null && shortfall != DBNull.Value)
                    txtRestockUnits.Text = shortfall.ToString();
            }
        }

        private void btnRestock_Click(object sender, EventArgs e)
        {
            // Re-checked even though the button is disabled without a row. A handler must not
            // depend on the enabled state being correct, because the selection can change
            // between the state being set and the click arriving.
            if (dgvLowStock.CurrentRow == null) return;

            int units;
            // Parsed again rather than reusing a stored value, so the number written is exactly
            // what is in the box at the moment the button is pressed.
            if (!Validator.IsPositiveInt(txtRestockUnits.Text, out units)) return;

            // Convert.ToInt32 rather than a cast, because the cell holds a boxed value whose
            // CLR type is decided by the provider.
            int medicineId = Convert.ToInt32(dgvLowStock.CurrentRow.Cells["MedicineId"].Value);
            // Captured BEFORE the write and the reload, because the reload rebinds the grid and
            // this row may no longer be selected, or may no longer be in the alert list at all
            // once the stock is above its minimum.
            string name = dgvLowStock.CurrentRow.Cells["MedicineName"].Value.ToString();

            // AddStock writes Stock = Stock + @Units, a RELATIVE change. Reading the stock,
            // adding in C# and writing the result back would overwrite anything a concurrent
            // sale had done in between, quietly putting sold units back on the shelf. The
            // database does the addition, so no update can be lost.
            // The session's pharmacy id is in the WHERE clause as well as the medicine id, so
            // a restock aimed at another shop's medicine matches no row and returns false
            // rather than adding stock to somebody else's shelf.
            if (_medicines.AddStock(medicineId, UserSession.PharmacyId, units))
            {
                lblStatus.Text = units + " unit(s) added to " + name + ".";
                txtRestockUnits.Clear();   // so the next selection pre-fills its own shortfall
                // Reloaded because the write changes both grids, all four tiles and the alert
                // heading. A restocked medicine usually disappears from the alert panel, which
                // is the visible confirmation that the write worked.
                LoadEverything();
            }
            // There is deliberately no else branch. AddStock returns false only when the UPDATE
            // matched no row, which here means the medicine is not this pharmacy's, and in that
            // case nothing was written and nothing on screen should change.
        }

        private void btnOpenEditor_Click(object sender, EventArgs e)
        {
            if (dgvLowStock.CurrentRow == null) return;
            // Opens the medicine behind the currently selected ALERT row. The inventory grid
            // has its own double click route into the same method below.
            OpenEditor(Convert.ToInt32(dgvLowStock.CurrentRow.Cells["MedicineId"].Value));
        }

        private void dgvInventory_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // double clicking the header must not open an editor
            // The row is taken from e.RowIndex rather than from CurrentRow, so the medicine
            // opened is the one actually under the pointer even if the selection has not caught
            // up with the double click yet.
            OpenEditor(Convert.ToInt32(dgvInventory.Rows[e.RowIndex].Cells["MedicineId"].Value));
        }

        private void OpenEditor(int medicineId)
        {
            // One method for both routes in, so the dialog is opened and the screen refreshed
            // in exactly one place. The second argument tells the editor it was opened for
            // stock work, so it puts the caret in the stock box.
            // using disposes the dialog once it closes; ShowDialog(this) blocks and names this
            // form as the owner, keeping the editor in front of it.
            using (MedicineEditorForm editor = new MedicineEditorForm(medicineId, true))
            {
                editor.ShowDialog(this);
            }
            // Refreshed unconditionally rather than only on DialogResult.OK: the editor can
            // change the price, the minimum level or the stock, all of which move figures on
            // this screen, and re-reading is cheaper than working out which ones.
            LoadEverything();
        }

        // Close, not Hide: the dashboard opened this form with ShowDialog, disposes it and
        // then refreshes itself, so closing is all this button needs to do.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
