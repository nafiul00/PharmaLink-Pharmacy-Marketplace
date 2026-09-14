using System.Data;                  // DataTable and DataRow: the tiles total by walking rows
using System.Drawing;               // Color, Point and Size for the tiles and row colouring
using System.Windows.Forms;         // Form, Label, DataGridView and the event argument types
using PharmaLinkApp.Helpers;        // UiTheme styling, Validator input checks, Money format
using PharmaLinkApp.Services;       // MedicineService, the only class here that knows SQL

// One namespace for every screen, and how the Designer half finds this half.
namespace PharmaLinkApp.Forms
{
    // Presentation. Load -> ApplyTheme -> BuildTiles -> LoadEverything.

    /// <summary>Requirement 12. Tiles, low stock alerts, inventory.</summary>
    public partial class AdminInventoryForm : Form
    {
        // One service for the whole form, so this screen holds no SQL and no table names.
        private readonly MedicineService _medicines = new MedicineService();

        // Only the number Labels are kept; BuildTiles places the Panels once, for good.
        private Label _tileItems;
        private Label _tileLowStock;        // medicines below their own minimum, the red figure
        private Label _tileUnitsInStock;    // the sum of UnitsRemaining over every listed row
        private Label _tileStockValue;      // the same rows valued at UnitPrice, so money format

        // Designer controls only: a throw here leaves no window for the error.
        public AdminInventoryForm()
        {
            // No query here: the constructor runs before the window exists.
            InitializeComponent();
        }

        // Fires after the handle exists, so all database work is reached from here.
        private void AdminInventoryForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();        // colours, fonts, grid styling and the CellFormatting hook ups
            BuildTiles();        // the four panels, built in code to match every other screen
            LoadEverything();    // the first load; every later refresh calls the same method
        }

        // Pure presentation, and where both grids are subscribed to their formatters.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Stock and Inventory");   // title and background

            panelHeader.BackColor = UiTheme.Primary;          // the same green band everywhere
            lblTitle.Font = UiTheme.FontTitle;                // the largest font, once per screen
            lblTitle.ForeColor = Color.White;                 // white stays legible on the band
            lblSubtitle.Font = UiTheme.FontSmall;             // a title and its caption
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // a pale tint of Primary

            lblAlertTitle.Font = UiTheme.FontHeading;         // a section heading, not a title
            lblAlertTitle.ForeColor = UiTheme.Danger;         // red: it sits over the reorder list
            lblInventoryTitle.Font = UiTheme.FontHeading;     // same size, so the two read as peers
            lblInventoryTitle.ForeColor = UiTheme.TextDark;   // dark: the full list is not an alert
            lblStatus.Font = UiTheme.FontSmall;               // the footer line, what just happened
            lblStatus.ForeColor = UiTheme.TextMuted;          // muted, it demands no action
            lblRestockError.Font = UiTheme.FontSmall;         // the line under the units box
            lblRestockError.ForeColor = UiTheme.Danger;       // red is kept for blockers only

            UiTheme.StyleSecondary(btnBack);                  // grey: it changes nothing
            UiTheme.StyleSuccess(btnRestock);                 // green: the only button that writes
            UiTheme.StyleSecondary(btnOpenEditor);            // grey: it just opens another screen
            UiTheme.StyleGrid(dgvLowStock);                   // Fill mode is what makes FillWeight work
            UiTheme.StyleGrid(dgvInventory);                  // same styling, so both read as one screen
            dgvLowStock.CellFormatting += dgvLowStock_CellFormatting;       // wired here, not in the Designer
            dgvInventory.CellFormatting += dgvInventory_CellFormatting;     // this grid colours conditionally
        }

        // Built in code, not on the Designer surface, so every dashboard's tiles match.
        private void BuildTiles()
        {
            // The four questions in order: how many, how many low, how many units, worth what.
            Panel t1 = UiTheme.BuildTile("MEDICINES ON SALE", UiTheme.Primary, out _tileItems);
            Panel t2 = UiTheme.BuildTile("BELOW MINIMUM STOCK", UiTheme.Danger, out _tileLowStock);          // out: Panel and Label made together
            Panel t3 = UiTheme.BuildTile("UNITS ON THE SHELF", UiTheme.Accent, out _tileUnitsInStock);       // accent: informative, not an alert
            Panel t4 = UiTheme.BuildTile("VALUE OF STOCK HELD", UiTheme.Warning, out _tileStockValue);       // amber: notable, not a problem

            // One start and one step, so the row stays evenly spaced if a tile is edited.
            int x = 20;
            // An inline array, so the placement rule below is written once for all four.
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                // PlaceTile sets bounds, adds to the FORM, and brings the tile to the front.
                UiTheme.PlaceTile(this, tile, x, 84, 278, 84);
                x += 294;   // width plus gap, so spacing follows the size, not a second number
            }
        }

        // The single load path: two queries, four tiles and the alert heading.
        private void LoadEverything()
        {
            // One try around both queries and the totalling, so a failure is reported once.
            try
            {
                // FIRST QUERY. PharmacyId comes from the login, never from a control.
                DataTable lowStock = _medicines.GetLowStock(UserSession.PharmacyId);
                dgvLowStock.DataSource = lowStock;   // the assignment is what creates the columns

                // Binding creates the columns, so every rename below comes after it.
                if (dgvLowStock.Columns.Count > 0)
                {
                    dgvLowStock.Columns["MedicineId"].HeaderText = "ID";        // restock and editor both act on it
                    // StyleGrid sets Fill mode, so FillWeight is a share, not a pixel count.
                    dgvLowStock.Columns["MedicineId"].FillWeight = 30;
                    dgvLowStock.Columns["MedicineName"].HeaderText = "Medicine";   // the raw name runs two words together
                    dgvLowStock.Columns["Strength"].HeaderText = "Strength";       // renamed anyway, headers in one place
                    dgvLowStock.Columns["CategoryName"].HeaderText = "Category";   // from a join: Medicines stores the id
                    dgvLowStock.Columns["Stock"].HeaderText = "In stock";          // in words, so it is not read as the minimum
                    // Both columns are shown, because the alert IS the comparison between them.
                    dgvLowStock.Columns["MinStock"].HeaderText = "Minimum level";
                    // (MinStock - Stock) from the query; the restock box pre-fills from it.
                    dgvLowStock.Columns["ShortfallUnits"].HeaderText = "Shortfall (units to order)";
                }

                // SECOND QUERY: every medicine, with a LEFT JOIN so unsold ones show 0.
                DataTable inventory = _medicines.GetInventory(UserSession.PharmacyId);
                dgvInventory.DataSource = inventory;   // kept local too: the tiles total these rows

                // The same guard: no columns means the bind produced nothing.
                if (dgvInventory.Columns.Count > 0)
                {
                    dgvInventory.Columns["MedicineId"].HeaderText = "ID";              // the double click handler reads it
                    dgvInventory.Columns["MedicineId"].FillWeight = 28;                // an id is a few digits
                    dgvInventory.Columns["MedicineName"].HeaderText = "Medicine";      // read first, so a plain heading
                    dgvInventory.Columns["Strength"].HeaderText = "Strength";          // what separates two rows of one medicine
                    dgvInventory.Columns["Strength"].FillWeight = 45;                  // a number and a unit, so narrow
                    dgvInventory.Columns["CategoryName"].HeaderText = "Category";      // from the join, not from Medicines
                    dgvInventory.Columns["UnitsRemaining"].HeaderText = "Units left";  // Stock, named for the question asked here
                    dgvInventory.Columns["UnitsSold"].HeaderText = "Units sold";       // a LEFT JOIN sum, so 0 instead of vanishing
                    dgvInventory.Columns["MinStock"].HeaderText = "Min level";         // shortened, this grid is wider
                    dgvInventory.Columns["UnitPrice"].HeaderText = "Price (Tk)";       // currency named here, so cells need no symbol
                    // Stock * UnitPrice cast in SQL, so the column stays numeric to sum.
                    dgvInventory.Columns["StockValue"].HeaderText = "Stock value (Tk)";
                    // A CASE expression in SQL, so "Low Stock" has exactly one definition.
                    dgvInventory.Columns["StockStatus"].HeaderText = "State";
                    dgvInventory.Columns["ExpiryDate"].HeaderText = "Expires";         // left as the provider formatted it
                }

                // Totalled from the table already in memory: a third query could disagree.
                int units = 0;
                decimal value = 0m;   // decimal, never double: this is money
                // One pass for both totals, rather than two passes or two queries.
                foreach (DataRow row in inventory.Rows)
                {
                    units += DbHelperInt(row, "UnitsRemaining");        // helper, because a joined column can be DBNull
                    value += DbHelperDecimal(row, "StockValue");        // the decimal twin of the same guard
                }

                // Rows.Count, not COUNT(*): the rows are here, so the tile matches the grid.
                _tileItems.Text = inventory.Rows.Count.ToString();
                _tileLowStock.Text = lowStock.Rows.Count.ToString();   // the first query's count, so tile and panel agree
                _tileUnitsInStock.Text = units.ToString("N0");   // a count, so no decimal places
                _tileStockValue.Text = UiTheme.Money(value);     // one money format for the app

                // The heading carries the state, or an empty grid would read as a failed load.
                lblAlertTitle.Text = lowStock.Rows.Count == 0
                    ? "Low stock alert  -  nothing is below its minimum level right now"           // the good news branch
                    : "Low stock alert  (" + lowStock.Rows.Count + " medicine(s) need reordering)";   // count visible even when scrolled

                // Printing the id makes the isolation rule observable rather than claimed.
                lblStatus.Text = "Double click any inventory row to open the medicine editor. " +
                                 "Every query on this form carries WHERE PharmacyId = " + UserSession.PharmacyId + ".";   // the value the queries used

                // The selection may have moved during the rebind, so re-evaluate restock.
                UpdateRestockButton();
            }
            // ex already carries the sentence DbHelper built from the SqlException.
            catch (Exception ex)
            {
                // Shown unwrapped, because DbHelper already made it readable.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // A local twin of DbHelper.GetInt: Convert.ToInt32(DBNull.Value) throws.
        private static int DbHelperInt(DataRow row, string column)
        {
            return row[column] == DBNull.Value ? 0 : Convert.ToInt32(row[column]);   // the ternary IS the null check
        }

        // Kept separate rather than generic, because 0 is an int and 0m is a decimal.
        private static decimal DbHelperDecimal(DataRow row, string column)
        {
            // Convert, not a cast: the boxed CLR type is whatever the provider chose.
            return row[column] == DBNull.Value ? 0m : Convert.ToDecimal(row[column]);
        }

        /// <summary>Alert rows are painted red through the CellFormatting event.</summary>
        private void dgvLowStock_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Fires once per cell, even while scrolling, and -1 is the header row.
            if (e.RowIndex < 0) return;

            // No condition: the query filtered to Stock < MinStock, so all rows are low.
            dgvLowStock.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        // The conditional twin: this grid holds every medicine, so each row is judged.
        private void dgvInventory_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The header row is -1, and the event can fire mid rebind with no columns.
            if (e.RowIndex < 0 || dgvInventory.Columns.Count == 0) return;

            DataGridViewRow row = dgvInventory.Rows[e.RowIndex];   // the row being painted now
            // READ from the row, never recalculated: StockStatus is the query's CASE.
            object state = row.Cells["StockStatus"].Value;
            if (state == null) return;   // cells can be unpopulated while the grid binds

            // A ternary because both branches must assign - see the white branch below.
            row.DefaultCellStyle.BackColor = state.ToString() == "Low Stock"
                ? UiTheme.LowStockBack     // the same pale red the alert panel uses
                : Color.White;             // required: reused rows would keep the old red
        }

        // Restock: a selected alert row plus a valid quantity, then one relative UPDATE.

        // Moving the selection changes which medicine would be restocked.
        private void dgvLowStock_SelectionChanged(object sender, EventArgs e) => UpdateRestockButton();

        // Live validation only; the real guard is the re-parse inside btnRestock_Click.
        private void txtRestockUnits_TextChanged(object sender, EventArgs e)
        {
            int units;   // only serves the out parameter below; its value is unused here
            // Runs on every keystroke, so a bad value is reported while typing.
            if (string.IsNullOrWhiteSpace(txtRestockUnits.Text))
            {
                // An EMPTY box is the starting state, not an error.
                UiTheme.ClearError(lblRestockError, txtRestockUnits);
            }
            // Reached only with something in the box, so this branch judges real input.
            else if (!Validator.IsPositiveInt(txtRestockUnits.Text, out units))
            {
                // Zero and negatives are refused: Stock = Stock + @Units would subtract.
                UiTheme.ShowError(lblRestockError, txtRestockUnits,
                    "Enter a whole number of units greater than zero.");   // all three rules in one message
            }
            // A whole number above zero, the only input the write accepts.
            else
            {
                // Cleared here, so a corrected box does not keep an old red line under it.
                UiTheme.ClearError(lblRestockError, txtRestockUnits);
            }

            UpdateRestockButton();   // the typed value is half of what enables the button
        }

        // Decides both buttons and pre-fills the quantity, from all three callers.
        private void UpdateRestockButton()
        {
            int units;   // only satisfies the out parameter; the verdict is the bool
            // A restock needs BOTH a selected medicine and a valid quantity.
            bool hasRow = dgvLowStock.CurrentRow != null && dgvLowStock.CurrentRow.Cells["MedicineId"].Value != null;
            bool unitsOk = Validator.IsPositiveInt(txtRestockUnits.Text, out units);   // the same rule the message uses

            // Disabled rather than offered-then-refused; the click handler re-checks anyway.
            btnRestock.Enabled = hasRow && unitsOk;
            // The editor needs only a row, since it does its own validation.
            btnOpenEditor.Enabled = hasRow;

            // Only when BLANK, or a deliberate order quantity would be silently overwritten.
            if (hasRow && string.IsNullOrWhiteSpace(txtRestockUnits.Text))
            {
                object shortfall = dgvLowStock.CurrentRow.Cells["ShortfallUnits"].Value;   // computed by the query
                // null while binding, DBNull if the column arrives empty; neither is text.
                if (shortfall != null && shortfall != DBNull.Value)
                    txtRestockUnits.Text = shortfall.ToString();   // raises TextChanged, which validates it
            }
        }

        // The only write here. Guards before the call, a full reload after it.
        private void btnRestock_Click(object sender, EventArgs e)
        {
            // Re-checked: the selection can change between the button state and the click.
            if (dgvLowStock.CurrentRow == null) return;

            int units;   // filled by the out parameter below, then sent to the service
            // Parsed again, so the number written is what is in the box right now.
            if (!Validator.IsPositiveInt(txtRestockUnits.Text, out units)) return;

            // Convert, not a cast: the cell holds a boxed value typed by the provider.
            int medicineId = Convert.ToInt32(dgvLowStock.CurrentRow.Cells["MedicineId"].Value);
            // Captured BEFORE the reload, which may drop this row out of the alert list.
            string name = dgvLowStock.CurrentRow.Cells["MedicineName"].Value.ToString();

            // AddStock writes Stock = Stock + @Units, a RELATIVE change, so no update is lost.
            if (_medicines.AddStock(medicineId, UserSession.PharmacyId, units))
            {
                lblStatus.Text = units + " unit(s) added to " + name + ".";   // the name captured above
                txtRestockUnits.Clear();   // so the next selection pre-fills its own shortfall
                // The write moves both grids, all four tiles and the alert heading.
                LoadEverything();
            }
            // No else: false means the UPDATE matched no row, so nothing changed.
        }

        // Route one into the editor: the button under the alert panel.
        private void btnOpenEditor_Click(object sender, EventArgs e)
        {
            if (dgvLowStock.CurrentRow == null) return;   // never trust the enabled state alone
            // Opens the medicine behind the selected ALERT row.
            OpenEditor(Convert.ToInt32(dgvLowStock.CurrentRow.Cells["MedicineId"].Value));
        }

        // Route two: a double click on the inventory grid, named in the status line.
        private void dgvInventory_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // double clicking the header must open nothing
            // e.RowIndex, not CurrentRow, so it opens the row actually under the pointer.
            OpenEditor(Convert.ToInt32(dgvInventory.Rows[e.RowIndex].Cells["MedicineId"].Value));
        }

        // Both routes funnel here, so the dialog is opened and refreshed in one place.
        private void OpenEditor(int medicineId)
        {
            // true tells the editor it was opened for stock work, so it focuses that box.
            using (MedicineEditorForm editor = new MedicineEditorForm(medicineId, true))
            {
                editor.ShowDialog(this);   // modal, so the reload below waits for it to close
            }
            // Unconditional, not only on OK: price, minimum and stock all move this screen.
            LoadEverything();
        }

        // Close, not Hide: the dashboard opened this with ShowDialog and disposes it.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
