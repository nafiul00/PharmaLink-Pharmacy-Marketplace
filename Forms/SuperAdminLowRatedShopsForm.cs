using System.Data;                  // DataTable, kept in a field so Export can reuse it
using System.Drawing;               // Color, for the header strip and the row tint
using System.Windows.Forms;         // Form, DataGridView, SaveFileDialog, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette and control styles
using PharmaLinkApp.Services;       // ReportService, which owns the GROUP BY ... HAVING

// The aggregate query lives in the service, so the dashboard reuses the rule.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 6. Pharmacies whose average rating is too low.</summary>
    public partial class SuperAdminLowRatedShopsForm : Form // Ratings sit on medicines.
    {
        // One service: it owns the aggregate query and the CSV writer.
        private readonly ReportService _reports = new ReportService();

        // The table on screen: Export writes this exact set, it does not re-query.
        private DataTable _current;

        // No parameters: the spinners already hold the standard thresholds.
        public SuperAdminLowRatedShopsForm()
        {
            InitializeComponent();   // designer controls only; a query here would have no window
        }

        // Fires once the window exists, which is why the first query belongs here.
        private void SuperAdminLowRatedShopsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // colours, fonts, the SQL caption and the CellFormatting hook up
            // Run with the designer's defaults, 2.5 and 2, so the report is already on screen.
            Generate();
        }

        // Appearance only, so a colour change cannot alter which shops are returned.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Low Rated Pharmacies");   // shared window setup and caption

            panelHeader.BackColor = UiTheme.Danger;            // red: this report is a list of problems
            lblTitle.Font = UiTheme.FontTitle;                 // the shared heading font
            lblTitle.ForeColor = Color.White;                  // the only colour legible on Danger
            lblSubtitle.Font = UiTheme.FontSmall;              // smaller, it qualifies the title
            lblSubtitle.ForeColor = Color.FromArgb(250, 220, 220);   // pale tint, part of the strip

            UiTheme.StyleSecondary(btnBack);                   // leaving is not what this is for
            UiTheme.StylePrimary(btnGenerate);                 // re-running is why the operator is here
            UiTheme.StyleAccent(btnExport);                    // offered, without outranking Generate
            UiTheme.StyleSecondary(btnOpenPharmacy);           // quiet, the double click is the usual way
            UiTheme.StyleGrid(dgvLowRated);                    // read-only, full-row select, Fill columns
            dgvLowRated.CellFormatting += dgvLowRated_CellFormatting;   // in code, designer untouched

            lblSqlNote.Font = UiTheme.FontMono;                // monospaced, the caption below is SQL
            lblSqlNote.ForeColor = UiTheme.TextMuted;          // muted: it explains, it is not a result
            // A caption only. It is never executed; ReportService holds the real statement.
            lblSqlNote.Text =
                "SELECT ph.PharmacyName, COUNT(r.ReviewId), AVG(CAST(r.Rating AS DECIMAL(4,2)))" + Environment.NewLine +   // CAST first, AVG over an int truncates
                "FROM   Pharmacies ph JOIN Medicines m ON m.PharmacyId = ph.PharmacyId" + Environment.NewLine +            // the shop is two joins away
                "                     JOIN Reviews   r ON r.MedicineId = m.MedicineId" + Environment.NewLine +             // INNER, so an unrated shop never appears
                "WHERE  r.IsHidden = 0   GROUP BY ph.PharmacyId, ph.PharmacyName" + Environment.NewLine +                  // WHERE filters rows before grouping
                "HAVING AVG(CAST(r.Rating AS DECIMAL(4,2))) < 2.5 AND COUNT(r.ReviewId) >= 2;";                            // HAVING filters groups

            lblStatus.Font = UiTheme.FontSmall;                // the line Generate and Export write into
            lblStatus.ForeColor = UiTheme.TextMuted;           // muted: it reports, it does not ask
        }

        // The single read path: Load, Generate and the return from Manage Pharmacies.
        private void Generate()
        {
            try   // wraps the whole report: a half-built suspension list is worse than an error
            {
                // Spinner values travel as parameters, so nothing is concatenated into SQL.
                _current = _reports.GetLowRatedPharmacies(numThreshold.Value, (int)numMinReviews.Value);
                dgvLowRated.DataSource = _current;   // the field, so Export matches the screen

                // The columns exist only after DataSource is set, and the guard covers a failure.
                if (dgvLowRated.Columns.Count > 0)
                {
                    // Column keys are the aliases from the SELECT in GetLowRatedPharmacies.
                    dgvLowRated.Columns["PharmacyId"].HeaderText = "ID";
                    // FillWeight is a share of the width, not pixels; 30 against a default of 100.
                    dgvLowRated.Columns["PharmacyId"].FillWeight = 30;
                    dgvLowRated.Columns["PharmacyName"].HeaderText = "Pharmacy";   // what the row is about
                    dgvLowRated.Columns["Area"].HeaderText = "Area";               // a bad cluster is a finding
                    // The owner's name and phone are joined in so the shop can be rung from here.
                    dgvLowRated.Columns["OwnerName"].HeaderText = "Owner";
                    dgvLowRated.Columns["OwnerPhone"].HeaderText = "Owner phone";   // ring before suspending
                    // TotalReviews is the COUNT the HAVING tested: 1.0 over two is not 1.0 over fifty.
                    dgvLowRated.Columns["TotalReviews"].HeaderText = "Reviews";
                    dgvLowRated.Columns["TotalReviews"].FillWeight = 45;            // a count needs little room
                    dgvLowRated.Columns["AverageRating"].HeaderText = "Average rating";   // the AVG that was tested
                    dgvLowRated.Columns["AverageRating"].FillWeight = 55;                 // the header is two words
                    // Status comes from Pharmacies, so a shop already suspended is marked as such.
                    dgvLowRated.Columns["Status"].HeaderText = "Status";
                    dgvLowRated.Columns["Status"].FillWeight = 55;   // "Suspended" is the longest value
                }

                // Open Pharmacy acts on a row, so with no rows there is nothing to open.
                btnOpenPharmacy.Enabled = _current.Rows.Count > 0;

                // Empty is the GOOD outcome here, so it is spelled out rather than left as 0.
                lblStatus.Text = _current.Rows.Count == 0
                    ? "No pharmacy is rated below " + numThreshold.Value.ToString("N1") +   // N1 forces one decimal
                      " with at least " + numMinReviews.Value + " review(s). Nothing needs your attention."   // empty is good news
                    : _current.Rows.Count + " pharmac" + (_current.Rows.Count == 1 ? "y" : "ies") +   // singular from the count
                      " rated below " + numThreshold.Value.ToString("N1") +   // the threshold, repeated for meaning
                      ". Double click a row to open it in Manage Pharmacies with the record already selected.";   // states the gesture
            }
            catch (Exception ex)   // ex.Message is already a readable sentence
            {
                // DbHelper translated the SqlException, so this is safe in front of the operator.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Tints every row red: the HAVING clause already decided which shops belong here.
        private void dgvLowRated_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;   // the header row is -1, and Rows[-1] would throw
            // The ROW's style, not e.CellStyle, so one cell's event colours all nine.
            dgvLowRated.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        // Hands the selected shop to the screen that can act on it.
        private void OpenSelectedPharmacy()
        {
            // Quiet return: the button is disabled, so only a double click reaches this.
            if (dgvLowRated.CurrentRow == null) return;
            // Convert, not a cast: the cell value is boxed as the provider's own type.
            int pharmacyId = Convert.ToInt32(dgvLowRated.CurrentRow.Cells["PharmacyId"].Value);

            // Passed to the constructor, so that form cannot exist half configured.
            using (SuperAdminManageShopsForm form = new SuperAdminManageShopsForm(pharmacyId))
            {
                form.ShowDialog(this);   // modal, so this method blocks until it is closed
            }
            // Re-run: the shop may now be suspended, and Status has to say so.
            Generate();
        }

        // Double click is the gesture the status line advertises, so it shares one method.
        private void dgvLowRated_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // a double click on the headers must open nothing
            OpenSelectedPharmacy();   // the one implementation, shared with the button below
        }

        // Two entry points, one method, so the two gestures cannot drift apart.
        private void btnOpenPharmacy_Click(object sender, EventArgs e) => OpenSelectedPharmacy();
        private void btnGenerate_Click(object sender, EventArgs e) => Generate();   // re-runs with the spinners

        // Writes _current to CSV, so the file and the screen cannot differ.
        private void btnExport_Click(object sender, EventArgs e)
        {
            // Two different failures: null means Generate never ran, 0 means it found nothing.
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("There is nothing to export.", "PharmaLink",        // says why nothing happened
                    MessageBoxButtons.OK, MessageBoxIcon.Information);              // an empty report is normal
                return;   // the screen is left as it was, with no file written
            }

            // using(), because a SaveFileDialog holds unmanaged resources.
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";   // restricts the picker and adds the extension
                // yyyy-MM-dd, so the exported files sort chronologically in a folder listing.
                dialog.FileName = "PharmaLink-LowRated-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;   // cancelled, so write nothing

                string message;   // filled by the out parameter, and shown either way
                // Static, because it needs no service state: a table, a path, no database.
                ReportService.ExportToCsv(_current, dialog.FileName, out message);
                lblStatus.Text = message;   // the status line, because it names the full path
            }
        }

        // Close, not Dispose: the dashboard's using block around ShowDialog disposes this.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
