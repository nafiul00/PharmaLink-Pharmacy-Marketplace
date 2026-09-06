using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 5. Gross sales, platform commission, units sold and average
    /// order value across every pharmacy, filtered by date range, area and order
    /// status, with a bold total row appended to the grid and a CSV export so
    /// the figures can be reconciled against bank settlements.
    /// </summary>
    public partial class SuperAdminSalesReportForm : Form
    {
        private readonly ReportService _reports = new ReportService();
        private readonly PharmacyService _pharmacies = new PharmacyService();

        private Label _tileGross;
        private Label _tileCommission;
        private Label _tileUnits;
        private Label _tileOrders;

        private DataTable _current;

        public SuperAdminSalesReportForm()
        {
            InitializeComponent();
        }

        private void SuperAdminSalesReportForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            BuildTiles();

            dtpFrom.Value = DateTime.Today.AddDays(-30);
            dtpTo.Value = DateTime.Today;

            cmbArea.Items.Add("All areas");
            foreach (string area in _pharmacies.GetAreas(false)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;

            cmbStatus.Items.AddRange(new object[] { "All except cancelled", "Placed", "Confirmed", "Delivered" });
            cmbStatus.SelectedIndex = 0;

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
            Panel t1 = UiTheme.BuildTile("GROSS SALES", UiTheme.Primary, out _tileGross);
            Panel t2 = UiTheme.BuildTile("PLATFORM COMMISSION", UiTheme.Warning, out _tileCommission);
            Panel t3 = UiTheme.BuildTile("UNITS SOLD", UiTheme.Accent, out _tileUnits);
            Panel t4 = UiTheme.BuildTile("ORDERS", UiTheme.Success, out _tileOrders);

            int x = 20;
            foreach (Panel tile in new[] { t1, t2, t3, t4 })
            {
                tile.Location = new Point(x, 128);
                tile.Size = new Size(288, 84);
                Controls.Add(tile);
                tile.BringToFront();
                x += 302;
            }
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

                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();
                string status = cmbStatus.SelectedIndex <= 0 ? "" : cmbStatus.SelectedItem.ToString();

                _current = _reports.GetEarnings(0, dtpFrom.Value, dtpTo.Value, area, status);

                decimal gross = 0m, commission = 0m;
                int units = 0, orders = 0;

                foreach (DataRow row in _current.Rows)
                {
                    gross += DbHelperTotals(row, "GrossSales");
                    commission += DbHelperTotals(row, "PlatformCommission");
                    units += (int)DbHelperTotals(row, "UnitsSold");
                    orders += (int)DbHelperTotals(row, "TotalOrders");
                }

                AppendTotalRow(gross, commission, units, orders);

                dgvReport.DataSource = _current;
                LabelColumns();

                _tileGross.Text = UiTheme.Money(gross);
                _tileCommission.Text = UiTheme.Money(commission);
                _tileUnits.Text = units.ToString("N0");
                _tileOrders.Text = orders.ToString("N0");

                lblStatus.Text = (_current.Rows.Count - 1) + " pharmac" +
                                 ((_current.Rows.Count - 1) == 1 ? "y" : "ies") + " traded between " +
                                 dtpFrom.Value.ToString("dd MMM yyyy") + " and " + dtpTo.Value.ToString("dd MMM yyyy") +
                                 ".   PharmaLink keeps " + UiTheme.Money(commission) + " and settles " +
                                 UiTheme.Money(gross - commission) + " to the pharmacies.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static decimal DbHelperTotals(DataRow row, string column)
        {
            if (row[column] == DBNull.Value) return 0m;
            return Convert.ToDecimal(row[column]);
        }

        /// <summary>The bold total row the report ends with.</summary>
        private void AppendTotalRow(decimal gross, decimal commission, int units, int orders)
        {
            DataRow total = _current.NewRow();
            total["PharmacyId"] = 0;
            total["PharmacyName"] = "PLATFORM TOTAL";
            total["Area"] = "";
            total["TotalOrders"] = orders;
            total["UnitsSold"] = units;
            total["GrossSales"] = gross;
            total["PlatformCommission"] = commission;
            total["NetEarnings"] = gross - commission;
            total["AverageItemPrice"] = DBNull.Value;
            total["CommissionRate"] = DBNull.Value;
            _current.Rows.Add(total);
        }

        private void LabelColumns()
        {
            if (dgvReport.Columns.Count == 0) return;
            dgvReport.Columns["PharmacyId"].Visible = false;
            dgvReport.Columns["PharmacyName"].HeaderText = "Pharmacy";
            dgvReport.Columns["Area"].HeaderText = "Area";
            dgvReport.Columns["TotalOrders"].HeaderText = "Orders";
            dgvReport.Columns["UnitsSold"].HeaderText = "Units sold";
            dgvReport.Columns["GrossSales"].HeaderText = "Gross sales (Tk)";
            dgvReport.Columns["PlatformCommission"].HeaderText = "Commission (Tk)";
            dgvReport.Columns["NetEarnings"].HeaderText = "Settled to shop (Tk)";
            dgvReport.Columns["AverageItemPrice"].HeaderText = "Avg item price";
            dgvReport.Columns["CommissionRate"].HeaderText = "Rate %";
            dgvReport.Columns["CommissionRate"].FillWeight = 40;
        }

        /// <summary>The appended total row is drawn in bold so it reads as a summary, not a pharmacy.</summary>
        private void dgvReport_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvReport.Columns.Count == 0) return;

            DataGridViewRow row = dgvReport.Rows[e.RowIndex];
            object name = row.Cells["PharmacyName"].Value;
            if (name != null && name.ToString() == "PLATFORM TOTAL")
            {
                row.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.75F, FontStyle.Bold);
                row.DefaultCellStyle.BackColor = Color.FromArgb(228, 240, 236);
            }
        }

        // ---------------------------------------------------------------------

        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)
        {
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("Generate the report first.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";
                dialog.FileName = "PharmaLink-Sales-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string message;
                if (ReportService.ExportToCsv(_current, dialog.FileName, out message))
                {
                    lblStatus.Text = message;
                    MessageBox.Show(message, "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
