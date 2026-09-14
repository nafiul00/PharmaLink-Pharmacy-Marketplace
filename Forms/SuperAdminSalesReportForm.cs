using System.Data;                  // DataTable and DataRow, because this form appends a row of its own
using System.Drawing;               // Color, Font, Point and Size, for the tiles and the bold total row
using System.Windows.Forms;         // Form, DataGridView, SaveFileDialog, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette, tile builder and money formatter
using PharmaLinkApp.Services;       // ReportService for the figures, PharmacyService for the areas

namespace PharmaLinkApp.Forms       // presentation only; the query lives one namespace over
{
    // Opened by SuperAdminDashboard. One round trip per Generate, no SQL in this file.

    /// <summary>Platform sales and commission report.</summary>
    public partial class SuperAdminSalesReportForm : Form
    {
        private readonly ReportService _reports = new ReportService();          // produces every figure on screen
        private readonly PharmacyService _pharmacies = new PharmacyService();   // only fills the area filter

        private Label _tileGross;        // the four value Labels, so Generate rewrites text not controls
        private Label _tileCommission;   // the amber tile: what PharmaLink itself keeps
        private Label _tileUnits;        // a count, not money, so it is formatted differently below
        private Label _tileOrders;       // the denominator behind average order value

        private DataTable _current;   // the table on screen, so Export writes exactly what is displayed

        public SuperAdminSalesReportForm()   // no parameters: a Super Admin's scope is the whole platform
        {
            InitializeComponent();   // designer controls only; data work waits for Load
        }

        private void SuperAdminSalesReportForm_Load(object sender, EventArgs e)   // styling, tiles, defaults, query
        {
            ApplyTheme();     // colours and fonts, so nothing flashes in default styling
            BuildTiles();     // builds the four panels once; Generate only rewrites their text

            dtpFrom.Value = DateTime.Today.AddDays(-30);   // a 30 day window, so the report opens useful
            dtpTo.Value = DateTime.Today;   // today is the upper bound: sales cannot be in the future

            cmbArea.Items.Add("All areas");   // index 0 is the sentinel, added first so its position is fixed
            // false means include suspended shops, which still have sales history.
            foreach (string area in _pharmacies.GetAreas(false)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;   // open on the sentinel, so the first report covers every area

            // Hard coded because these are the values CK_Orders_Status permits.
            cmbStatus.Items.AddRange(new object[] { "All except cancelled", "Placed", "Confirmed", "Delivered" });
            cmbStatus.SelectedIndex = 0;   // the sentinel becomes the empty string the query treats as no filter

            Generate();   // run once, so the screen opens with real numbers rather than empty tiles
        }

        private void ApplyTheme()   // presentation only, so it can be re-read without worrying about data
        {
            UiTheme.StyleForm(this, "Sales and Commission");   // shared window chrome plus the caption

            panelHeader.BackColor = UiTheme.Primary;                  // the brand band every screen carries
            lblTitle.Font = UiTheme.FontTitle;                        // the shared title font, not a new Font
            lblTitle.ForeColor = Color.White;                         // the only colour with contrast on green
            lblSubtitle.Font = UiTheme.FontSmall;                     // one size down, so the two read as a pair
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);    // a pale tint: visible but secondary

            UiTheme.StyleSecondary(btnBack);        // grey: leaving the screen changes nothing
            UiTheme.StylePrimary(btnGenerate);      // green, the button this screen is organised around
            UiTheme.StyleAccent(btnExport);         // accent: exporting copies figures, it does not make them
            UiTheme.StyleGrid(dgvReport);           // read-only, full-row select and alternating rows in one call
            dgvReport.CellFormatting += dgvReport_CellFormatting;   // wired here, beside the grid it paints

            lblNote.Font = UiTheme.FontSmall;          // the standing explanation under the grid
            lblNote.ForeColor = UiTheme.TextMuted;     // muted, so it never competes with the status line
            lblStatus.Font = UiTheme.FontSmall;        // the line Generate rewrites with the settlement split
            lblStatus.ForeColor = UiTheme.TextMuted;   // muted too, so a new message does not read as an error
        }

        private void BuildTiles()   // called once from Load; Generate only rewrites the tile text
        {
            Panel t1 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileGross);                 // out gives back the Label
            Panel t2 = UiTheme.BuildTile("PLATFORM COMMISSION", UiTheme.Warning, out _tileCommission);    // amber: money the shops never see
            Panel t3 = UiTheme.BuildTile("UNITS SOLD", UiTheme.Accent, out _tileUnits);                   // another colour family, so counts stand apart
            Panel t4 = UiTheme.BuildTile("ORDERS", UiTheme.Success, out _tileOrders);                     // each Label field is assigned exactly once

            int x = 20;   // this form has no sidebar, so the row starts at the page margin
            foreach (Panel tile in new[] { t1, t2, t3, t4 })   // the four tiles differ only in placement
            {
                UiTheme.PlaceTile(this, tile, x, 128, 288, 84);   // locates, sizes and brings the tile to the front
                x += 302;   // the tile width plus a 14 pixel gutter, so the four sit evenly
            }
        }

        // ---------------------------------------------------------------------

        private void Generate()   // one press, one round trip, every figure derived from it
        {
            try   // a failure leaves the previous report on screen instead of a half-updated one
            {
                // BETWEEN with the dates reversed matches nothing, so check the range here.
                if (dtpTo.Value.Date < dtpFrom.Value.Date)
                {
                    MessageBox.Show("The end date cannot be earlier than the start date.",      // says what is wrong
                        "Check the date range", MessageBoxButtons.OK, MessageBoxIcon.Warning);  // a warning: it is fixable
                    return;   // nothing is queried, so the old report stays valid
                }

                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();            // "" means no area filter
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();      // the same sentinel trick

                // PharmacyId 0 switches the shop filter off, so every pharmacy comes back.
                _current = _reports.GetEarnings(0, dtpFrom.Value, dtpTo.Value, area, status);

                decimal gross = 0m, commission = 0m;   // tile figures are summed from the rows just returned
                int units = 0, orders = 0;   // int for the counts, because boxes and orders are whole things

                foreach (DataRow row in _current.Rows)   // totalled before AppendTotalRow, so nothing counts twice
                {
                    gross += DbHelperTotals(row, "GrossSales");                  // the helper turns DBNull into 0m
                    commission += DbHelperTotals(row, "PlatformCommission");     // totalled separately from gross
                    units += (int)DbHelperTotals(row, "UnitsSold");              // cast back: units are whole boxes
                    orders += (int)DbHelperTotals(row, "TotalOrders");           // COUNT never returns a fraction
                }

                AppendTotalRow(gross, commission, units, orders);   // the summary row is added last, on purpose

                dgvReport.DataSource = _current;   // bound after the append, so the total row exports too
                LabelColumns();   // headers can only be set once the bind has created the columns

                _tileGross.Text = UiTheme.Money(gross);             // the tiles repeat the total row deliberately
                _tileCommission.Text = UiTheme.Money(commission);   // the same Money helper, so both match
                _tileUnits.Text = units.ToString("N0");             // "N0" groups a count and drops the decimals
                _tileOrders.Text = orders.ToString("N0");           // formatted the same, since both are counts

                lblStatus.Text = (_current.Rows.Count - 1) + " pharmac" +                                    // minus the appended total row
                                 ((_current.Rows.Count - 1) == 1 ? "y" : "ies") + " traded between " +       // so it never reads "1 pharmacies"
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " + dtpTo.Value.ToString("dd MMM yyyy") +   // the month is spelled out
                                 ".   PharmaLink keeps " + UiTheme.Money(commission) + " and settles " +     // the platform's own cut first
                                 UiTheme.Money(gross - commission) + " to the pharmacies.";                  // from the same two totals the tiles show
            }
            catch (Exception ex)   // ex carries the real reason rather than a generic failure
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // DbHelper already made it readable
            }
        }

        // The one place a possibly-NULL aggregate is turned into a number.
        private static decimal DbHelperTotals(DataRow row, string column)
        {
            if (row[column] == DBNull.Value) return 0m;   // DBNull is not null and cannot be cast
            return Convert.ToDecimal(row[column]);        // decimal for everything; callers cast counts back
        }

        /// <summary>The bold total row the report ends with.</summary>
        private void AppendTotalRow(decimal gross, decimal commission, int units, int orders)
        {
            DataRow total = _current.NewRow();            // shaped to the table's columns but not yet in it
            total["PharmacyId"] = 0;                      // 0 is no real shop, so this row reads as the summary
            total["PharmacyName"] = "PLATFORM TOTAL";     // CellFormatting matches this text to draw it bold
            total["Area"] = "";                           // written out, so the blank is clearly intended
            total["TotalOrders"] = orders;                // the figures come from the loop in Generate
            total["UnitsSold"] = units;                   // so the row and the tiles are the same four numbers
            total["GrossSales"] = gross;                  // every shop's gross, before the platform takes a cut
            total["PlatformCommission"] = commission;     // frozen per-order amounts, never a rate applied now
            total["NetEarnings"] = gross - commission;    // computed, so it cannot drift from the two above
            total["AverageItemPrice"] = DBNull.Value;     // blank: an average of averages is not an average
            total["CommissionRate"] = DBNull.Value;       // blank too, since the shops are on different rates
            _current.Rows.Add(total);                     // only now does the detached row join the table
        }

        private void LabelColumns()   // headers only; no value on screen is changed here
        {
            if (dgvReport.Columns.Count == 0) return;   // no columns means the bind never happened
            dgvReport.Columns["PharmacyId"].Visible = false;              // hidden, not dropped: the total row needs it
            dgvReport.Columns["PharmacyName"].HeaderText = "Pharmacy";    // the column the total row is identified by
            dgvReport.Columns["Area"].HeaderText = "Area";                // renamed to itself, so no raw name slips through
            dgvReport.Columns["TotalOrders"].HeaderText = "Orders";       // shorter, because the column is narrow
            dgvReport.Columns["UnitsSold"].HeaderText = "Units sold";     // two words, so it is not read as orders
            dgvReport.Columns["GrossSales"].HeaderText = "Gross sales (Tk)";          // currency in the heading, not per cell
            dgvReport.Columns["PlatformCommission"].HeaderText = "Commission (Tk)";   // the same (Tk) convention
            dgvReport.Columns["NetEarnings"].HeaderText = "Settled to shop (Tk)";     // what it means to whoever reconciles it
            dgvReport.Columns["AverageItemPrice"].HeaderText = "Avg item price";      // per shop; the total row leaves it blank
            dgvReport.Columns["CommissionRate"].HeaderText = "Rate %";                // the % is in the header, cells stay bare
            dgvReport.Columns["CommissionRate"].FillWeight = 40;                      // a share of the width, not pixels
        }

        /// <summary>Draws the appended total row in bold.</summary>
        private void dgvReport_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvReport.Columns.Count == 0) return;   // -1 is the header row, which fires this too

            DataGridViewRow row = dgvReport.Rows[e.RowIndex];      // the style below applies to the row, not the cell
            object name = row.Cells["PharmacyName"].Value;         // object, since a bound table hands values back boxed
            if (name != null && name.ToString() == "PLATFORM TOTAL")   // no real pharmacy can carry this name
            {
                row.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.75F, FontStyle.Bold);   // set once for all ten cells
                row.DefaultCellStyle.BackColor = Color.FromArgb(228, 240, 236);   // pale brand green: a summary, not a warning
            }
        }

        // ---------------------------------------------------------------------

        // Nothing reloads on change, so every filter is set before one round trip.
        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)   // writes what is in memory, so it never re-queries
        {
            // null means Generate never ran; an empty table means it found nothing.
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("Generate the report first.", "PharmaLink",        // names the missing step
                    MessageBoxButtons.OK, MessageBoxIcon.Information);             // information: nothing went wrong
                return;   // no file dialog, so nobody is asked where to save nothing
            }

            using (SaveFileDialog dialog = new SaveFileDialog())   // holds unmanaged resources, so it is disposed
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";   // restricting the picker also supplies the extension
                // yyyy-MM-dd so successive exports sort chronologically in a folder listing.
                dialog.FileName = "PharmaLink-Sales-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;   // anything but OK means cancelled

                string message;   // filled by the out parameter below and used by both branches
                // _current still holds the total row, so the file ends with the on-screen figure.
                if (ReportService.ExportToCsv(_current, dialog.FileName, out message))
                {
                    lblStatus.Text = message;   // the status line keeps the path visible afterwards
                    MessageBox.Show(message, "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);   // and the dialog confirms it now
                }
                else   // false, so message now holds the reason rather than the path
                {
                    MessageBox.Show(message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);   // a dialog only, so no stale failure lingers
                }
            }
        }

        // Close, not Dispose: the dashboard opened this form inside a using block.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
