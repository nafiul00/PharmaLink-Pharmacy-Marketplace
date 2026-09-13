using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

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
        private readonly ReportService _reports = new ReportService();
        private readonly MedicineService _medicines = new MedicineService();

        private Label _tileGross;
        private Label _tileCommission;
        private Label _tileNet;
        private Label _tileUnits;

        private DataTable _current;

        public AdminEarningsForm()
        {
            InitializeComponent();
        }

        private void AdminEarningsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            BuildTiles();

            dtpFrom.Value = DateTime.Today.AddDays(-30);
            dtpTo.Value = DateTime.Today;

            LoadMedicineFilter();
            Generate();
        }

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
            Panel t1 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileGross);
            Panel t2 = UiTheme.BuildTile("PHARMALINK COMMISSION", UiTheme.Warning, out _tileCommission);
            Panel t3 = UiTheme.BuildTile("NET EARNINGS (YOURS)", UiTheme.Success, out _tileNet);
            Panel t4 = UiTheme.BuildTile("UNITS SOLD", UiTheme.Accent, out _tileUnits);

            int x = 20;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                UiTheme.PlaceTile(this, tile, x, 128, 288, 84);
                x += 302;
            }
        }

        private void LoadMedicineFilter()
        {
            List<Medicine> list = _medicines.GetSimpleListForPharmacy(UserSession.PharmacyId);

            cmbMedicine.Items.Clear();
            cmbMedicine.Items.Add("All my medicines");
            foreach (Medicine medicine in list)
                cmbMedicine.Items.Add(medicine.MedicineId + " - " + medicine.MedicineName + " " + medicine.Strength);

            cmbMedicine.SelectedIndex = 0;
        }

        private int SelectedMedicineId()
        {
            if (cmbMedicine.SelectedIndex <= 0) return 0;
            string text = cmbMedicine.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void Generate()
        {
            try
            {
                if (dtpTo.Value.Date < dtpFrom.Value.Date)
                {
                    MessageBox.Show("The end date cannot be earlier than the start date.",
                        "Check the date range", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                decimal gross, commission, net;
                int units;
                _reports.GetEarningsTotals(UserSession.PharmacyId, dtpFrom.Value, dtpTo.Value,
                                           out gross, out commission, out net, out units);

                _tileGross.Text = UiTheme.Money(gross);
                _tileCommission.Text = UiTheme.Money(commission);
                _tileNet.Text = UiTheme.Money(net);
                _tileUnits.Text = units.ToString("N0");

                _current = _reports.GetSalesDetail(UserSession.PharmacyId, dtpFrom.Value, dtpTo.Value,
                                                   SelectedMedicineId());
                dgvSales.DataSource = _current;

                if (dgvSales.Columns.Count > 0)
                {
                    dgvSales.Columns["OrderId"].HeaderText = "Order";
                    dgvSales.Columns["OrderId"].FillWeight = 42;
                    dgvSales.Columns["OrderDate"].HeaderText = "Date and time";
                    dgvSales.Columns["Customer"].HeaderText = "Bought by";
                    dgvSales.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvSales.Columns["Strength"].HeaderText = "Strength";
                    dgvSales.Columns["Strength"].FillWeight = 45;
                    dgvSales.Columns["Quantity"].HeaderText = "Qty";
                    dgvSales.Columns["Quantity"].FillWeight = 32;
                    dgvSales.Columns["UnitPrice"].HeaderText = "Unit price (Tk)";
                    dgvSales.Columns["Subtotal"].HeaderText = "Line total (Tk)";
                    dgvSales.Columns["PaymentMethod"].HeaderText = "Payment";
                    dgvSales.Columns["Status"].HeaderText = "Status";
                    dgvSales.Columns["Status"].FillWeight = 48;
                }

                lblStatus.Text = _current.Rows.Count + " sale line(s) between " +
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " +
                                 dtpTo.Value.ToString("dd MMM yyyy") +
                                 ".   PharmaLink keeps " + UiTheme.Money(commission) +
                                 " and settles " + UiTheme.Money(net) + " to " + UserSession.PharmacyName + ".";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)
        {
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("There is nothing to export for this date range.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";
                dialog.FileName = "PharmaLink-Earnings-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string message;
                ReportService.ExportToCsv(_current, dialog.FileName, out message);
                lblStatus.Text = message;
            }
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
