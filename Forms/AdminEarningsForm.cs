using System.Data;                  // DataTable, held in _current so Export can write what is on screen
using System.Drawing;               // Color, Point and Size for the tile layout below
using System.Windows.Forms;         // Form, Label, DataGridView, SaveFileDialog
using PharmaLinkApp.Helpers;        // UiTheme: colours, fonts, tiles and the money formatter
using PharmaLinkApp.Models;         // Medicine, the typed row the filter combo is built from
using PharmaLinkApp.Services;       // ReportService and MedicineService; no SQL is written in a form

namespace PharmaLinkApp.Forms   // presentation only; opened by AdminDashboard
{
    /// <summary>One shop's sales and settlement, for a chosen date range.</summary>
    public partial class AdminEarningsForm : Form   // requirement 13
    {
        // The Super Admin's report plus one condition: PharmacyId = the session's own.
        private readonly ReportService _reports = new ReportService();
        private readonly MedicineService _medicines = new MedicineService();   // fills the filter only

        private Label _tileGross;        // gross sales, written on every Generate
        private Label _tileCommission;   // what PharmaLink keeps out of that
        private Label _tileNet;          // what the shop is actually owed
        private Label _tileUnits;        // how much trade produced it

        // The table on screen, kept so Export writes exactly that and never re-queries.
        private DataTable _current;

        public AdminEarningsForm()   // runs before the form exists, so no database work here
        {
            InitializeComponent();   // designer generated controls only
        }

        private void AdminEarningsForm_Load(object sender, EventArgs e)   // runs once, after the window exists
        {
            ApplyTheme();     // colours, fonts and the grid styling
            BuildTiles();     // the four figures, built in code so every dashboard tile matches

            // A default range, so the form opens with real numbers rather than four zeros.
            dtpFrom.Value = DateTime.Today.AddDays(-30);   // thirty days is what a shop reconciles
            dtpTo.Value = DateTime.Today;   // .Today, so the range always ends on the current day

            LoadMedicineFilter();   // populated BEFORE Generate, which reads the selected medicine
            Generate();             // one deliberate first report
        }

        private void ApplyTheme()   // pure presentation, called once from Load
        {
            UiTheme.StyleForm(this, "Sales and Earnings");   // window background and title bar text

            panelHeader.BackColor = UiTheme.Primary;                 // the brand green band
            lblTitle.Font = UiTheme.FontTitle;                       // the page name, the largest text here
            lblTitle.ForeColor = Color.White;                        // the only colour legible on the green band
            lblSubtitle.Font = UiTheme.FontSmall;                    // the fixed one-line explanation
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // pale green, so it supports the title

            UiTheme.StyleSecondary(btnBack);       // grey: leaves the screen and changes nothing
            UiTheme.StylePrimary(btnGenerate);     // brand colour: the main action on this screen
            UiTheme.StyleAccent(btnExport);        // the secondary action, writing a file rather than reading
            UiTheme.StyleGrid(dgvSales);           // shared grid styling, including Fill column sizing

            lblNote.Font = UiTheme.FontSmall;        // the standing note about frozen prices
            lblNote.ForeColor = UiTheme.TextMuted;   // grey, so guidance never reads as live feedback
            lblStatus.Font = UiTheme.FontSmall;      // the outcome line Generate and Export write to
            lblStatus.ForeColor = UiTheme.TextMuted; // grey too: failures use a dialog instead
        }

        private void BuildTiles()   // the settlement sum, read left to right
        {
            Panel t1 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileGross);   // what was sold
            Panel t2 = UiTheme.BuildTile("PHARMALINK COMMISSION", UiTheme.Warning, out _tileCommission);   // amber: money leaving
            Panel t3 = UiTheme.BuildTile("NET EARNINGS (YOURS)", UiTheme.Success, out _tileNet);   // green: money arriving
            Panel t4 = UiTheme.BuildTile("UNITS SOLD", UiTheme.Accent, out _tileUnits);   // the trade behind the figures

            // Laid out by a loop with one step value, so the row stays evenly spaced.
            int x = 20;   // no sidebar on this form, so the row starts at the left margin
            foreach (Panel tile in new[] { t1, t2, t3, t4 })   // the four in display order
            {
                // PlaceTile locates, sizes, adds to the FORM and brings it in front in one call.
                UiTheme.PlaceTile(this, tile, x, 128, 288, 84);
                x += 302;   // one step, so editing a tile cannot unbalance the row
            }
        }

        private void LoadMedicineFilter()   // fills the combo the report is narrowed by
        {
            // Scoped by the session's pharmacy id, so another shop is never offered.
            List<Medicine> list = _medicines.GetSimpleListForPharmacy(UserSession.PharmacyId);

            cmbMedicine.Items.Clear();   // cleared first, so calling this twice cannot duplicate
            // Index 0 is the "no filter" entry, added first so its position is fixed.
            cmbMedicine.Items.Add("All my medicines");
            foreach (Medicine medicine in list)   // one entry per active medicine this shop lists
                // The id goes FIRST, separated by " - ", for SelectedMedicineId to read back.
                cmbMedicine.Items.Add(medicine.MedicineId + " - " + medicine.MedicineName + " " + medicine.Strength);

            cmbMedicine.SelectedIndex = 0;   // default to every medicine, matching the tiles
        }

        private int SelectedMedicineId()   // the combo caption turned back into an id
        {
            // 0 means "all", read by the service as (@MedicineId = 0 OR m.MedicineId = ...).
            if (cmbMedicine.SelectedIndex <= 0) return 0;   // <= 0 also covers -1, nothing chosen
            string text = cmbMedicine.SelectedItem.ToString();   // the string this form wrote itself
            // int.Parse, not TryParse: the combo is not editable, so no typed text arrives.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        private void Generate()   // the two queries behind the tiles and the grid
        {
            try   // one catch around both reads
            {
                // Validated first: inverted bounds match nothing, so four zeros with no reason.
                if (dtpTo.Value.Date < dtpFrom.Value.Date)
                {
                    MessageBox.Show("The end date cannot be earlier than the start date.",   // names the problem
                        "Check the date range", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // a warning, not an error
                    return;   // nothing is queried and the previous figures stay on screen
                }

                // out parameters, so one round trip fills all four tiles from the same instant.
                decimal gross, commission, net;   // the three money figures
                int units;                        // and the countable one
                // Commission comes from a separate subquery: it is stored once per ORDER.
                _reports.GetEarningsTotals(UserSession.PharmacyId, dtpFrom.Value, dtpTo.Value,   // from the login, never a control
                                           SelectedMedicineId(),   // 0 means every medicine
                                           out gross, out commission, out net, out units);   // all four at once

                _tileGross.Text = UiTheme.Money(gross);             // formatted by the shared helper
                _tileCommission.Text = UiTheme.Money(commission);   // so every screen shows money alike
                _tileNet.Text = UiTheme.Money(net);                 // gross minus commission, worked out in the service
                _tileUnits.Text = units.ToString("N0");   // N0: units are countable, so no decimal places

                // The same range and shop as the tiles, so the lines add up to the figures.
                _current = _reports.GetSalesDetail(UserSession.PharmacyId, dtpFrom.Value, dtpTo.Value,   // the same scope
                                                   SelectedMedicineId());   // and the same medicine filter
                dgvSales.DataSource = _current;   // binding is what CREATES the columns below

                if (dgvSales.Columns.Count > 0)   // guards a failed bind, where a name lookup throws
                {
                    dgvSales.Columns["OrderId"].HeaderText = "Order";   // the order each line belongs to
                    dgvSales.Columns["OrderId"].FillWeight = 42;        // FillWeight is a share of width, not pixels
                    dgvSales.Columns["OrderDate"].HeaderText = "Date and time";   // when it was placed
                    dgvSales.Columns["Customer"].HeaderText = "Bought by";        // the buyer's name from Users
                    dgvSales.Columns["MedicineName"].HeaderText = "Medicine";     // left at the default width
                    dgvSales.Columns["Strength"].HeaderText = "Strength";   // 250mg against 500mg
                    dgvSales.Columns["Strength"].FillWeight = 45;           // a short column, so a small share
                    dgvSales.Columns["Quantity"].HeaderText = "Qty";   // units on this one line
                    dgvSales.Columns["Quantity"].FillWeight = 32;      // a small number needs little room
                    // The values STORED on the line, never today's catalogue price.
                    dgvSales.Columns["UnitPrice"].HeaderText = "Unit price (Tk)";
                    dgvSales.Columns["Subtotal"].HeaderText = "Line total (Tk)";   // quantity times that price
                    dgvSales.Columns["PaymentMethod"].HeaderText = "Payment";      // how the order was paid for
                    dgvSales.Columns["Status"].HeaderText = "Status";   // where the order has reached
                    dgvSales.Columns["Status"].FillWeight = 48;         // one short word per row
                }

                // The range in words as well as a count, so a small figure reads as quiet.
                lblStatus.Text = _current.Rows.Count + " sale line(s) between " +   // how many lines
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " +  // the start of the range
                                 dtpTo.Value.ToString("dd MMM yyyy") +              // and its end
                                 ".   PharmaLink keeps " + UiTheme.Money(commission) +   // what is deducted
                                 " and settles " + UiTheme.Money(net) + " to " + UserSession.PharmacyName + ".";   // and paid out
            }
            catch (Exception ex)   // either query can fail for a database reason
            {
                // DbHelper has already turned the SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Generate reads the controls itself, so Load and this button run the same code.
        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)   // writes the grid to a CSV file
        {
            // Checked BEFORE the dialog opens: a zero row CSV looks like a failed export.
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("There is nothing to export for this date range.", "PharmaLink",   // said plainly
                    MessageBoxButtons.OK, MessageBoxIcon.Information);   // information, not an error
                return;   // no dialog is opened and no file is written
            }

            // using, because SaveFileDialog holds an unmanaged dialog to release at once.
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";   // one format, so the extension is certain
                // ISO dated, so a folder of exports sorts chronologically and cannot overwrite.
                dialog.FileName = "PharmaLink-Earnings-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;   // anything but OK is a cancel

                // _current is written, not a fresh query, so the file matches the screen exactly.
                string message;   // filled by ExportToCsv either way
                // The bool is not read: the message carries the count or the failure text.
                ReportService.ExportToCsv(_current, dialog.FileName, out message);
                lblStatus.Text = message;   // success or failure, shown in the same place
            }
        }

        // Close, not Hide: the dashboard opened this with ShowDialog and disposes it.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
