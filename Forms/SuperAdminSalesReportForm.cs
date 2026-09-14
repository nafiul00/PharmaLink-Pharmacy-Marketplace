using System.Data;                  // DataTable and DataRow, because this form appends a row of its own
using System.Drawing;               // Color, Font, Point and Size, for the tiles and the bold total row
using System.Windows.Forms;         // Form, DataGridView, SaveFileDialog, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette, tile builder and money formatter
using PharmaLinkApp.Services;       // ReportService for the earnings query, PharmacyService for the area list

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by SuperAdminDashboard. Uses ReportService
    //  for the figures and PharmacyService only to fill the area filter.
    //
    //  Flow:
    //      Load -> ApplyTheme -> BuildTiles -> default 30 day range
    //           -> fill the area and status filters -> Generate
    //      Generate -> ReportService.GetEarnings -> total the rows in C#
    //               -> AppendTotalRow -> bind -> update the four tiles
    //
    //  One database round trip per Generate. The tile figures are summed from
    //  the rows already returned rather than fetched by a second query, so the
    //  tiles and the grid can never disagree about the same period.
    //
    //  There is no SQL in this file. The query lives in ReportService.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirement 5. Gross sales, platform commission, units sold and average
    /// order value across every pharmacy, filtered by date range, area and order
    /// status, with a bold total row appended to the grid and a CSV export so
    /// the figures can be reconciled against bank settlements.
    /// </summary>
    public partial class SuperAdminSalesReportForm : Form
    {
        private readonly ReportService _reports = new ReportService();
        // Used for one thing only: the distinct list of areas behind the area filter. It is
        // a separate service because areas belong to pharmacies, not to reports.
        private readonly PharmacyService _pharmacies = new PharmacyService();

        // The Labels inside the four tiles, handed back by UiTheme.BuildTile through out
        // parameters. Held in fields so regenerating the report is four text assignments
        // rather than a search through the control tree by name.
        private Label _tileGross;
        private Label _tileCommission;
        private Label _tileUnits;
        private Label _tileOrders;

        // The table currently on screen, including the appended total row. Kept so Export
        // writes exactly what the operator is looking at rather than running the query
        // again, which could return different figures if an order were placed in between.
        private DataTable _current;

        public SuperAdminSalesReportForm()
        {
            InitializeComponent();
        }

        private void SuperAdminSalesReportForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            BuildTiles();

            // A 30 day window ending today, so the report is useful the moment it opens.
            // AddDays(-30) on Today rather than on Now, because the pickers hold dates and
            // the query widens the end date to the last second of its day itself.
            dtpFrom.Value = DateTime.Today.AddDays(-30);
            dtpTo.Value = DateTime.Today;

            // Index 0 is the "no filter" entry, added before the real values so its position
            // is fixed whatever the database returns.
            cmbArea.Items.Add("All areas");
            // Areas are read from the data rather than hard coded, because a new one appears
            // as soon as a pharmacy registers there. false means do not restrict to approved
            // shops: a suspended pharmacy still has sales history worth reporting on.
            foreach (string area in _pharmacies.GetAreas(false)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;

            // The statuses ARE hard coded, because they are the values CK_Orders_Status
            // permits. Note the default: "All except cancelled" rather than plain "All",
            // because the query already excludes cancelled orders in every case - a
            // cancelled order earns no commission and must never appear in a revenue figure.
            cmbStatus.Items.AddRange(new object[] { "All except cancelled", "Placed", "Confirmed", "Delivered" });
            cmbStatus.SelectedIndex = 0;

            // Run it once so the screen opens with real numbers rather than empty tiles.
            // There is no _loading guard on this form because no filter control reloads on
            // change: Generate is only ever called deliberately.
            Generate();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Sales and Commission");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StylePrimary(btnGenerate);
            UiTheme.StyleAccent(btnExport);
            UiTheme.StyleGrid(dgvReport);
            dgvReport.CellFormatting += dgvReport_CellFormatting;

            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private void BuildTiles()
        {
            // Each tile is built once and its value Label kept, so Generate only rewrites
            // four strings. The colours are UiTheme constants rather than literals, so
            // commission is the same amber here as the warning colour everywhere else.
            Panel t1 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileGross);
            Panel t2 = UiTheme.BuildTile("PLATFORM COMMISSION", UiTheme.Warning, out _tileCommission);
            Panel t3 = UiTheme.BuildTile("UNITS SOLD", UiTheme.Accent, out _tileUnits);
            Panel t4 = UiTheme.BuildTile("ORDERS", UiTheme.Success, out _tileOrders);

            // This form has no sidebar, unlike the dashboard, so the row starts at the page
            // margin of 20 rather than clear of a menu.
            int x = 20;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                // One call replaces four lines. PlaceTile sets the location and size,
                // adds the tile to the FORM rather than to a container, and brings it to
                // the front, because the designer's own controls were added first and
                // would otherwise paint over it. Centralised in UiTheme so every
                // dashboard places its tiles the same way on a scaled display.
                UiTheme.PlaceTile(this, tile, x, 128, 288, 84);
                x += 302;
            }
        }

        // ---------------------------------------------------------------------

        private void Generate()
        {
            try
            {
                // Validate the range before spending a round trip on it. A backwards range
                // is not an error the database would catch: BETWEEN with the arguments the
                // wrong way round simply matches nothing, so the operator would get an
                // empty report and no explanation of why.
                if (dtpTo.Value.Date < dtpFrom.Value.Date)
                {
                    MessageBox.Show("The end date cannot be earlier than the start date.",
                        "Check the date range", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Index 0 means "All", which becomes an empty string rather than the caption
                // itself. The query then reads (@Area = '' OR ph.Area = @Area), so one
                // statement serves the filtered and unfiltered cases and no WHERE clause is
                // ever built by concatenation.
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();

                // THE FIRST ARGUMENT IS THE WHOLE POINT OF THIS SCREEN.
                //
                // This is the same ReportService.GetEarnings method, running the same joins,
                // the same GROUP BY and the same two derived tables that a pharmacy owner's
                // earnings screen is scoped by. The only difference between the platform
                // report and one shop's report is this one integer.
                //
                // The query's WHERE clause reads:
                //      WHERE (@PharmacyId = 0 OR ph.PharmacyId = @PharmacyId)
                // Passing 0 makes the left side true, so the OR short circuits and no
                // pharmacy filter is applied at all: every shop on the platform comes back,
                // one row each. Passing a real PharmacyId makes the left side false, so the
                // right side has to hold and exactly one shop's row survives.
                //
                // That is data isolation stated in one clause. The Super Admin is the only
                // role that can supply 0, and an owner's scope is never typed, chosen from a
                // combo box or read from a grid cell - it is taken from UserSession.
                // PharmacyId, which was set once at login from the database row and can only
                // ever be that owner's own shop. Because there is nothing on any screen a
                // user could edit to widen their scope, one query can safely serve both
                // roles instead of there being a second, unaudited copy for administrators.
                //
                // The commission figure is the frozen Orders.CommissionAmount rather than a
                // percentage applied now, which is why this report and the owner's own
                // earnings screen still agree after a rate change.
                _current = _reports.GetEarnings(0, dtpFrom.Value, dtpTo.Value, area, status);

                // The four tile figures are accumulated from the rows just returned, not
                // fetched by a second query. A second query could straddle a new order and
                // leave the tiles disagreeing with the grid beneath them.
                decimal gross = 0m, commission = 0m;
                int units = 0, orders = 0;

                // This loop runs BEFORE AppendTotalRow, and the order of those two
                // statements is load bearing: totalling after the summary row had been added
                // would count every figure twice. Adding the row last is what makes that
                // impossible rather than merely unlikely.
                foreach (DataRow row in _current.Rows)
                {
                    gross += DbHelperTotals(row, "GrossSales");
                    commission += DbHelperTotals(row, "PlatformCommission");
                    // Cast back to int because the helper normalises everything to decimal to
                    // handle DBNull in one place. UnitsSold is SUM(oi.Quantity), a whole
                    // number of boxes, so it is displayed and totalled as one.
                    units += (int)DbHelperTotals(row, "UnitsSold");
                    orders += (int)DbHelperTotals(row, "TotalOrders");
                }

                // Now that the real rows have been totalled, the summary row is appended.
                AppendTotalRow(gross, commission, units, orders);

                // Bound after the append, so the total row is part of the same DataTable and
                // is therefore carried into the CSV export as well. Exporting a grid whose
                // last line was drawn separately would produce a file that does not match
                // what was on screen.
                dgvReport.DataSource = _current;
                LabelColumns();

                // The tiles repeat the total row's figures deliberately: the row proves the
                // arithmetic, the tiles are what gets read from across a desk.
                _tileGross.Text = UiTheme.Money(gross);
                _tileCommission.Text = UiTheme.Money(commission);
                // "N0" for counts, so a five figure unit count is grouped and has no decimal
                // part, unlike the money values which always show two.
                _tileUnits.Text = units.ToString("N0");
                _tileOrders.Text = orders.ToString("N0");

                // Minus one throughout, because the appended total row is in Rows.Count but
                // is not a pharmacy. Reading the count before the append would have avoided
                // the arithmetic, at the cost of a second variable to carry down here.
                lblStatus.Text = (_current.Rows.Count - 1) + " pharmac" +
                                 ((_current.Rows.Count - 1) == 1 ? "y" : "ies") + " traded between " +
                                 // "dd MMM yyyy" spells the month out, so 03/04 cannot be read
                                 // as either the third of April or the fourth of March.
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " + dtpTo.Value.ToString("dd MMM yyyy") +
                                 ".   PharmaLink keeps " + UiTheme.Money(commission) + " and settles " +
                                 // The settlement figure is gross minus commission, computed
                                 // here from the same two totals the tiles show, so the three
                                 // numbers on screen are arithmetically consistent by
                                 // construction rather than by coincidence.
                                 UiTheme.Money(gross - commission) + " to the pharmacies.";
            }
            catch (Exception ex)
            {
                // DbHelper has already translated any SqlException into readable English.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static decimal DbHelperTotals(DataRow row, string column)
        {
            // DBNull is not null and cannot be cast, so it has to be tested for explicitly.
            // A pharmacy with no matching orders can produce a NULL in an aggregate column,
            // and treating that as zero here is what keeps the running totals from throwing
            // part way down the grid.
            if (row[column] == DBNull.Value) return 0m;
            // decimal for everything, including the two counts, because the columns arrive
            // as different SQL types and Convert handles them all. The callers cast the
            // whole numbers back, which keeps the DBNull decision in exactly one place.
            // static because it depends on nothing in the form.
            return Convert.ToDecimal(row[column]);
        }

        /// <summary>The bold total row the report ends with.</summary>
        private void AppendTotalRow(decimal gross, decimal commission, int units, int orders)
        {
            // The total row is built in C# and APPENDED to the DataTable before binding,
            // not produced by SQL. It could have been done with GROUP BY ... WITH
            // ROLLUP, but that would put a summary row inside a result set that other
            // callers (the CSV export, the tiles) treat as one-row-per-pharmacy.
            // Keeping it a presentation concern leaves the query reusable.
            //
            // NewRow() creates a row already shaped to this table's columns; it is not
            // part of the table until Rows.Add() at the end of the method.
            //
            // The row cannot accumulate across regenerations, because Generate assigns a
            // brand new DataTable to _current before this method is ever reached.
            DataRow total = _current.NewRow();
            total["PharmacyId"] = 0;                      // 0 marks it as not a real shop
            // CellFormatting looks for exactly this text to draw the row bold, so the
            // label is effectively a flag as well as a caption.
            total["PharmacyName"] = "PLATFORM TOTAL";
            // Empty rather than left unset: an unassigned column would hold DBNull and show
            // nothing, which is the same on screen but exports as an empty CSV field either
            // way. Writing it explicitly says the blank is intended.
            total["Area"] = "";
            total["TotalOrders"] = orders;
            total["UnitsSold"] = units;
            total["GrossSales"] = gross;
            total["PlatformCommission"] = commission;
            // Computed rather than summed from the rows, because it is defined as the
            // difference between the two figures immediately above it. Totalling the
            // per-shop NetEarnings column separately would give the same answer only while
            // no rounding crept in, and this way it cannot differ at all.
            total["NetEarnings"] = gross - commission;
            // DELIBERATELY NULL, both of them. An average of averages is not an average, and
            // a single commission rate does not exist across shops that are each charged a
            // different one. Printing a number here would be inventing a figure, so the
            // cells are left empty instead.
            total["AverageItemPrice"] = DBNull.Value;
            total["CommissionRate"] = DBNull.Value;
            // Only now does the row join the table. Everything above was populating a
            // detached row that the DataTable did not yet know about.
            _current.Rows.Add(total);
        }

        private void LabelColumns()
        {
            // No columns means the bind never happened, and indexing by name would throw.
            if (dgvReport.Columns.Count == 0) return;
            // Hidden rather than dropped from the SELECT: the id is needed in the DataTable
            // so the total row can be marked with 0, but an operator reading a financial
            // report has no use for a primary key.
            dgvReport.Columns["PharmacyId"].Visible = false;
            dgvReport.Columns["PharmacyName"].HeaderText = "Pharmacy";
            dgvReport.Columns["Area"].HeaderText = "Area";
            dgvReport.Columns["TotalOrders"].HeaderText = "Orders";
            dgvReport.Columns["UnitsSold"].HeaderText = "Units sold";
            // The currency is named in the heading rather than repeated in every cell, so
            // the columns stay narrow and the figures line up for comparison down the page.
            dgvReport.Columns["GrossSales"].HeaderText = "Gross sales (Tk)";
            dgvReport.Columns["PlatformCommission"].HeaderText = "Commission (Tk)";
            // "Settled to shop" rather than the schema's NetEarnings, because that is what
            // the figure means to whoever is reconciling it against a bank transfer.
            dgvReport.Columns["NetEarnings"].HeaderText = "Settled to shop (Tk)";
            dgvReport.Columns["AverageItemPrice"].HeaderText = "Avg item price";
            dgvReport.Columns["CommissionRate"].HeaderText = "Rate %";
            // FillWeight is a share of the width, not pixels, because the grid auto sizes to
            // fill. A percentage needs very little room next to the money columns.
            dgvReport.Columns["CommissionRate"].FillWeight = 40;
        }

        /// <summary>The appended total row is drawn in bold so it reads as a summary, not a pharmacy.</summary>
        private void dgvReport_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // RowIndex is -1 for the header row, which fires this event too, and the column
            // test covers the gap between a failed load and the next successful bind.
            if (e.RowIndex < 0 || dgvReport.Columns.Count == 0) return;

            DataGridViewRow row = dgvReport.Rows[e.RowIndex];
            object name = row.Cells["PharmacyName"].Value;
            // Matching on the caption works because PharmacyName is a real shop's name on
            // every other row, and no pharmacy can be called this. The alternative, testing
            // PharmacyId == 0, would be sturdier, and it is a fair point that the flag and
            // the label are the same field here.
            if (name != null && name.ToString() == "PLATFORM TOTAL")
            {
                // Bold and tinted, so the summary is unmistakably not another shop's row.
                // Set on the ROW's DefaultCellStyle, which styles all ten cells from the
                // first cell's event rather than waiting for each one in turn.
                row.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.75F, FontStyle.Bold);
                row.DefaultCellStyle.BackColor = Color.FromArgb(228, 240, 236);
            }
            // No else branch is needed here, unlike the status grids: only the font and one
            // background are being overridden, and every other row keeps the theme's own
            // alternating colours untouched.
        }

        // ---------------------------------------------------------------------

        // Generating is always an explicit press. None of the filters reloads on change,
        // so the operator can set a date range, an area and a status before paying for a
        // single round trip.
        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)
        {
            // Two separate conditions: null means Generate has never succeeded, an empty
            // table means it ran and found nothing. The || short circuits, so the second
            // test never dereferences a null reference.
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("Generate the report first.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // using(), because a SaveFileDialog holds unmanaged resources and this handler
            // may run many times in a session.
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                // Restricting the picker to .csv also supplies the extension, so the file
                // cannot be saved under a name a spreadsheet will refuse to open.
                dialog.Filter = "CSV file (*.csv)|*.csv";
                // yyyy-MM-dd so successive exports sort chronologically in a folder listing,
                // which is the opposite of the dd MMM yyyy used for reading on screen.
                dialog.FileName = "PharmaLink-Sales-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";

                // Anything other than OK means the operator cancelled, so nothing is written.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string message;
                // ExportToCsv writes _current, which still contains the appended total row,
                // so the file that is reconciled against a bank settlement ends with the
                // same figure that was on screen. It reports success through a bool and the
                // detail through an out parameter, because a file that cannot be written is
                // an ordinary outcome, not an exception this form should be catching.
                if (ReportService.ExportToCsv(_current, dialog.FileName, out message))
                {
                    // Both, on purpose: the status line keeps the path visible afterwards,
                    // the dialog confirms at the moment it happened.
                    lblStatus.Text = message;
                    MessageBox.Show(message, "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // A failure is a dialog only. Leaving a failure message in the status
                    // line would sit there looking like the result of the next export too.
                    MessageBox.Show(message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // Close, not Dispose: the dashboard opened this form inside a using block, so
        // closing hands control back there and the dashboard refreshes itself.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
