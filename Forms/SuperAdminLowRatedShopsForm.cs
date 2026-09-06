using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 6.
    ///
    /// Ratings sit on medicines, not on pharmacies, so the average has to be
    /// built by joining Pharmacies, Medicines and Reviews and grouping back up
    /// to the pharmacy. HAVING is the right clause because the condition is on
    /// the aggregate itself, and the second condition, COUNT(ReviewId) >= 2, is
    /// a deliberate fairness rule: one angry customer should not be enough to
    /// put a shop on the suspension list.
    /// </summary>
    public partial class SuperAdminLowRatedShopsForm : Form
    {
        private readonly ReportService _reports = new ReportService();
        private DataTable _current;

        public SuperAdminLowRatedShopsForm()
        {
            InitializeComponent();
        }

        private void SuperAdminLowRatedShopsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            Generate();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Low Rated Pharmacies");

            panelHeader.BackColor = UiTheme.Danger;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(250, 220, 220);

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StylePrimary(btnGenerate);
            UiTheme.StyleAccent(btnExport);
            UiTheme.StyleSecondary(btnOpenPharmacy);
            UiTheme.StyleGrid(dgvLowRated);
            dgvLowRated.CellFormatting += dgvLowRated_CellFormatting;

            lblSqlNote.Font = UiTheme.FontMono;
            lblSqlNote.ForeColor = UiTheme.TextMuted;
            lblSqlNote.Text =
                "SELECT ph.PharmacyName, COUNT(r.ReviewId), AVG(CAST(r.Rating AS DECIMAL(4,2)))" + Environment.NewLine +
                "FROM   Pharmacies ph JOIN Medicines m ON m.PharmacyId = ph.PharmacyId" + Environment.NewLine +
                "                     JOIN Reviews   r ON r.MedicineId = m.MedicineId" + Environment.NewLine +
                "WHERE  r.IsHidden = 0   GROUP BY ph.PharmacyId, ph.PharmacyName" + Environment.NewLine +
                "HAVING AVG(CAST(r.Rating AS DECIMAL(4,2))) < 2.5 AND COUNT(r.ReviewId) >= 2;";

            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private void Generate()
        {
            try
            {
                _current = _reports.GetLowRatedPharmacies(numThreshold.Value, (int)numMinReviews.Value);
                dgvLowRated.DataSource = _current;

                if (dgvLowRated.Columns.Count > 0)
                {
                    dgvLowRated.Columns["PharmacyId"].HeaderText = "ID";
                    dgvLowRated.Columns["PharmacyId"].FillWeight = 30;
                    dgvLowRated.Columns["PharmacyName"].HeaderText = "Pharmacy";
                    dgvLowRated.Columns["Area"].HeaderText = "Area";
                    dgvLowRated.Columns["OwnerName"].HeaderText = "Owner";
                    dgvLowRated.Columns["OwnerPhone"].HeaderText = "Owner phone";
                    dgvLowRated.Columns["TotalReviews"].HeaderText = "Reviews";
                    dgvLowRated.Columns["TotalReviews"].FillWeight = 45;
                    dgvLowRated.Columns["AverageRating"].HeaderText = "Average rating";
                    dgvLowRated.Columns["AverageRating"].FillWeight = 55;
                    dgvLowRated.Columns["Status"].HeaderText = "Status";
                    dgvLowRated.Columns["Status"].FillWeight = 55;
                }

                btnOpenPharmacy.Enabled = _current.Rows.Count > 0;

                lblStatus.Text = _current.Rows.Count == 0
                    ? "No pharmacy is rated below " + numThreshold.Value.ToString("N1") +
                      " with at least " + numMinReviews.Value + " review(s). Nothing needs your attention."
                    : _current.Rows.Count + " pharmac" + (_current.Rows.Count == 1 ? "y" : "ies") +
                      " rated below " + numThreshold.Value.ToString("N1") +
                      ". Double click a row to open it in Manage Pharmacies with the record already selected.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvLowRated_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            dgvLowRated.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        private void OpenSelectedPharmacy()
        {
            if (dgvLowRated.CurrentRow == null) return;
            int pharmacyId = Convert.ToInt32(dgvLowRated.CurrentRow.Cells["PharmacyId"].Value);

            using (SuperAdminManageShopsForm form = new SuperAdminManageShopsForm(pharmacyId))
            {
                form.ShowDialog(this);
            }
            Generate();
        }

        private void dgvLowRated_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            OpenSelectedPharmacy();
        }

        private void btnOpenPharmacy_Click(object sender, EventArgs e) => OpenSelectedPharmacy();
        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)
        {
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("There is nothing to export.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";
                dialog.FileName = "PharmaLink-LowRated-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string message;
                ReportService.ExportToCsv(_current, dialog.FileName, out message);
                lblStatus.Text = message;
            }
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
