using System.Data;                  // DataTable, held in _current so Export can write what is on screen
using System.Drawing;               // Color, Point and Size for the tile layout below
using System.Windows.Forms;         // Form, Label, DataGridView, SaveFileDialog
using PharmaLinkApp.Helpers;        // UiTheme: colours, fonts, tiles and the money formatter
using PharmaLinkApp.Models;         // Medicine, the typed row the filter combo is built from
using PharmaLinkApp.Services;       // ReportService and MedicineService; no SQL is written in a form

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by AdminDashboard. Uses ReportService for
    //  the figures and MedicineService to fill the medicine filter.
    //
    //  Load order:
    //      AdminEarningsForm_Load -> ApplyTheme -> BuildTiles
    //                             -> set the default 30 day range
    //                             -> LoadMedicineFilter -> Generate
    //
    //  Generate goes to the database twice. GetEarningsTotals returns the four
    //  tile figures through out parameters, and GetSalesDetail returns the
    //  DataTable assigned to dgvSales.DataSource. btnGenerate_Click just calls
    //  Generate again with whatever the date pickers now hold.
    //
    //  The medicine ComboBox holds "id - name strength" strings, so
    //  SelectedMedicineId parses the id back off the front of the selected text
    //  and passes 0 when "All my medicines" is chosen.
    //  Export writes the DataTable already on screen through
    //  ReportService.ExportToCsv; it does not re-query.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirement 13.
    ///
    /// This form runs the same JOIN plus GROUP BY query as the Super Admin's
    /// platform report, with one extra condition: WHERE PharmacyId =
    /// UserSession.PharmacyId. One query, two role scopes, which is the clearest
    /// demonstration of the data isolation rule in the whole system.
    /// </summary>
    public partial class AdminEarningsForm : Form
    {
        // Two services, created once with the form. ReportService answers the money
        // questions and MedicineService only fills the filter list; keeping them separate
        // means the reporting queries live next to each other rather than being scattered
        // through whichever service happened to be open.
        private readonly ReportService _reports = new ReportService();
        private readonly MedicineService _medicines = new MedicineService();

        // Only the number Labels are kept. The Panels around them are placed once in
        // BuildTiles and never change; the figures inside are rewritten on every Generate.
        private Label _tileGross;
        private Label _tileCommission;
        private Label _tileNet;
        private Label _tileUnits;

        // The table currently on screen, kept so Export can write exactly what the owner is
        // looking at. Re-querying at export time would be a second round trip that could
        // return different rows if an order changed in between, and the file would then not
        // match the screen it was exported from.
        private DataTable _current;

        public AdminEarningsForm()
        {
            // Designer generated controls only; no database work before the form exists.
            InitializeComponent();
        }

        private void AdminEarningsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();     // colours, fonts and the grid styling
            BuildTiles();     // the four figures, built in code so every dashboard tile matches

            // A default range rather than an empty one, so the form opens with real numbers
            // on it instead of four zeros the owner has to click Generate to replace. Thirty
            // days back is the period a shop actually reconciles against, and .Today means the
            // range always ends at the current day whenever the form is opened.
            dtpFrom.Value = DateTime.Today.AddDays(-30);
            dtpTo.Value = DateTime.Today;

            // The filter is populated BEFORE the first Generate, because Generate reads the
            // selected medicine and would otherwise run against an empty combo box.
            LoadMedicineFilter();
            Generate();
        }

        // Pure presentation, called once from Load.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Sales and Earnings");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StylePrimary(btnGenerate);
            UiTheme.StyleAccent(btnExport);
            UiTheme.StyleGrid(dgvSales);

            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private void BuildTiles()
        {
            // The four tiles are the whole settlement sum in one line: gross sales, minus what
            // PharmaLink keeps, leaves what the shop is owed, and units sold says how much
            // trade produced it. The colours carry the meaning, so commission is amber because
            // it is money leaving and net is green because it is money arriving.
            Panel t1 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileGross);
            Panel t2 = UiTheme.BuildTile("PHARMALINK COMMISSION", UiTheme.Warning, out _tileCommission);
            Panel t3 = UiTheme.BuildTile("NET EARNINGS (YOURS)", UiTheme.Success, out _tileNet);
            Panel t4 = UiTheme.BuildTile("UNITS SOLD", UiTheme.Accent, out _tileUnits);

            // Laid out by a loop with a single step value, so the row stays evenly spaced no
            // matter which tile is edited later. This form has no sidebar, so it starts at 20
            // rather than clear of one, and the tiles are wider than the dashboard's.
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

        private void LoadMedicineFilter()
        {
            // Scoped by the session's pharmacy id like everything else on the Admin side, so
            // the filter cannot even offer another shop's medicine to filter by. The service
            // also restricts it to IsActive = 1, so delisted products do not clutter the list.
            List<Medicine> list = _medicines.GetSimpleListForPharmacy(UserSession.PharmacyId);

            // Cleared first, so calling this method twice cannot produce a duplicated list.
            cmbMedicine.Items.Clear();
            // Index 0 is the "no filter" entry, added before the data so its position is fixed
            // and SelectedMedicineId can rely on it.
            cmbMedicine.Items.Add("All my medicines");
            foreach (Medicine medicine in list)
                // The id is put FIRST and separated by " - ", which is what lets
                // SelectedMedicineId read it back off the front. The name and strength are
                // there for the human; the id is there for the code.
                cmbMedicine.Items.Add(medicine.MedicineId + " - " + medicine.MedicineName + " " + medicine.Strength);

            cmbMedicine.SelectedIndex = 0;   // default to every medicine, matching the tiles
        }

        private int SelectedMedicineId()
        {
            // 0 means "all medicines". The service reads it as (@MedicineId = 0 OR
            // m.MedicineId = @MedicineId), so one query serves both cases and there is no
            // second statement to keep in step. <= 0 also covers -1, the value SelectedIndex
            // holds when nothing has been chosen.
            if (cmbMedicine.SelectedIndex <= 0) return 0;
            string text = cmbMedicine.SelectedItem.ToString();
            // The id is everything before the first space, which the " - " separator
            // guarantees exists. int.Parse rather than TryParse is safe here because this form
            // wrote the string itself in LoadMedicineFilter; the combo is not editable, so no
            // user typed text can reach this line.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void Generate()
        {
            try
            {
                // Validated before either query runs. An inverted range is not an error the
                // database would report: BETWEEN with the bounds the wrong way round simply
                // matches nothing, so the owner would see four zeros and no explanation.
                // .Date on both sides compares the days, ignoring the time part the picker
                // carries, so choosing the same day twice is a valid one day range.
                if (dtpTo.Value.Date < dtpFrom.Value.Date)
                {
                    MessageBox.Show("The end date cannot be earlier than the start date.",
                        "Check the date range", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;   // nothing is queried and the previous figures stay on screen
                }

                // out parameters, so one round trip fills all four tiles and they cannot
                // describe different instants. Inside the service, gross is SUM(oi.Subtotal)
                // over the joined order lines while commission comes from a SEPARATE subquery
                // over Orders: CommissionAmount is stored once per ORDER, so summing it beside
                // the joined OrderItems would multiply it by the number of lines on each order.
                // Net is then gross minus commission, worked out once in the service so this
                // form and the CSV can never disagree about it.
                decimal gross, commission, net;
                int units;
                // UserSession.PharmacyId again, never a control. This is the same report the
                // Super Admin runs across the platform; the only difference is this one extra
                // WHERE condition, and it is taken from the login rather than from the screen.
                // The selected medicine is passed as well, so the tiles describe the same
                // lines as the grid below; 0 means every medicine.
                _reports.GetEarningsTotals(UserSession.PharmacyId, dtpFrom.Value, dtpTo.Value,
                                           SelectedMedicineId(),
                                           out gross, out commission, out net, out units);

                _tileGross.Text = UiTheme.Money(gross);
                _tileCommission.Text = UiTheme.Money(commission);
                _tileNet.Text = UiTheme.Money(net);
                // "N0" gives a thousands separated whole number. Units are countable things,
                // so they must not appear with the decimal places money carries.
                _tileUnits.Text = units.ToString("N0");

                // The detail query covers the same range and pharmacy as the totals above, so
                // the lines on screen add up to the tiles. The service turns the end date into
                // the last second of that day, which is why an order placed at half past two
                // in the afternoon on the closing date is still inside the range.
                // The table is kept in the field, because Export writes this object rather
                // than asking the database again.
                _current = _reports.GetSalesDetail(UserSession.PharmacyId, dtpFrom.Value, dtpTo.Value,
                                                   SelectedMedicineId());
                dgvSales.DataSource = _current;

                // Binding is what creates the columns, so the renames have to come after the
                // assignment, and the guard keeps a failed or empty bind from throwing on the
                // first lookup by name.
                if (dgvSales.Columns.Count > 0)
                {
                    dgvSales.Columns["OrderId"].HeaderText = "Order";
                    // StyleGrid sets AutoSizeColumnsMode to Fill, so FillWeight is a share of
                    // the width rather than a pixel count. An order number needs little room;
                    // the medicine name is left at the default and therefore gets more.
                    dgvSales.Columns["OrderId"].FillWeight = 42;
                    dgvSales.Columns["OrderDate"].HeaderText = "Date and time";
                    dgvSales.Columns["Customer"].HeaderText = "Bought by";
                    dgvSales.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvSales.Columns["Strength"].HeaderText = "Strength";
                    dgvSales.Columns["Strength"].FillWeight = 45;
                    dgvSales.Columns["Quantity"].HeaderText = "Qty";
                    dgvSales.Columns["Quantity"].FillWeight = 32;
                    // UnitPrice and Subtotal are the values STORED on the order line, not
                    // today's catalogue price, so an old sale still reports what was actually
                    // charged even after the medicine's price has been edited since.
                    dgvSales.Columns["UnitPrice"].HeaderText = "Unit price (Tk)";
                    dgvSales.Columns["Subtotal"].HeaderText = "Line total (Tk)";
                    dgvSales.Columns["PaymentMethod"].HeaderText = "Payment";
                    dgvSales.Columns["Status"].HeaderText = "Status";
                    dgvSales.Columns["Status"].FillWeight = 48;
                }

                // The status line states the range in words as well as the row count, so a
                // small figure can be read as "a quiet month" rather than "the report is
                // broken". Naming the commission and the net beside the shop's own name makes
                // the settlement explicit: this is what is kept and this is what is paid out.
                lblStatus.Text = _current.Rows.Count + " sale line(s) between " +
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " +
                                 dtpTo.Value.ToString("dd MMM yyyy") +
                                 ".   PharmaLink keeps " + UiTheme.Money(commission) +
                                 " and settles " + UiTheme.Money(net) + " to " + UserSession.PharmacyName + ".";
            }
            catch (Exception ex)
            {
                // One catch around both queries. DbHelper has already translated the raw
                // SqlException into a sentence, so the message is shown as it stands rather
                // than being wrapped in wording that would hide it.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Generate takes no arguments and reads the controls itself, so the button handler is
        // a single expression and the Load path can call exactly the same code.
        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)
        {
            // Checked before the file dialog opens, not after. Asking the owner to choose a
            // filename and only then saying there was nothing to write would waste the
            // interaction, and a zero row CSV on disk looks like a failed export later.
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("There is nothing to export for this date range.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // using, because SaveFileDialog holds an unmanaged dialog that should be released
            // as soon as the choice is made rather than at the next collection.
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";
                // A suggested name with the date in ISO order, so a folder of exports sorts
                // chronologically by filename and two exports on different days cannot
                // silently overwrite one another.
                dialog.FileName = "PharmaLink-Earnings-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                // Anything other than OK means the owner cancelled, so nothing is written.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                // _current is written, not a fresh query, so the file matches the rows on
                // screen exactly. ExportToCsv is static because it needs no service state: it
                // walks any DataTable, quoting values that contain a comma or a quote.
                string message;
                // The bool result is not read. The out message carries either the row count on
                // success or the exception text on failure, and showing it in the status line
                // covers both without a dialog interrupting a routine save.
                ReportService.ExportToCsv(_current, dialog.FileName, out message);
                lblStatus.Text = message;
            }
        }

        // Close, not Hide or Dispose: the dashboard opened this form with ShowDialog and
        // disposes it, then refreshes itself, so closing is all this button has to do.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
