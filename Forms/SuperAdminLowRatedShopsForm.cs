using System.Data;                  // DataTable, kept in a field here so Export can reuse it without re-querying
using System.Drawing;               // Color, for the header strip and the row tint
using System.Windows.Forms;         // Form, DataGridView, SaveFileDialog and MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette and control styles
using PharmaLinkApp.Services;       // ReportService, which owns the GROUP BY ... HAVING query

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by SuperAdminDashboard. Uses ReportService
    //  for the query and for the CSV export; it opens no connection itself.
    //
    //  Flow:
    //      Load -> ApplyTheme -> Generate
    //      btnGenerate_Click -> Generate (with whatever the two spinners hold)
    //      double click a row -> OpenSelectedPharmacy -> SuperAdminManageShopsForm
    //
    //  The result is kept in _current so Export writes exactly the table that is
    //  on screen rather than running the query a second time, which could
    //  produce a different set of rows if a review were hidden in between.
    // -------------------------------------------------------------------------

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

        // The table currently on screen. Held in a field for two reasons: Export writes
        // this exact set of rows rather than re-running the query, and the status line
        // needs the row count after the grid has been bound. It is null until the first
        // Generate, which is why btnExport_Click tests for null before using it.
        private DataTable _current;

        public SuperAdminLowRatedShopsForm()
        {
            InitializeComponent();
        }

        private void SuperAdminLowRatedShopsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            // Generate immediately with the designer's defaults, 2.5 and 2, so the report
            // is already on screen. An empty grid with a Generate button would make the
            // operator work to discover that nothing needs their attention.
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
                // Both thresholds come from spinners, so the Super Admin can ask "below
                // 2.0 with at least 5 reviews" without anyone editing SQL. The dashboard
                // calls the same method with the fixed defaults 2.5 and 2.
                //
                // numMinReviews.Value is decimal (NumericUpDown always is), so it needs
                // the explicit (int) cast; numThreshold stays decimal because the rating
                // average genuinely is fractional.
                //
                // WHY THE QUERY THIS CALLS USES HAVING AND NOT WHERE.
                // WHERE is evaluated on individual rows BEFORE any grouping happens, at a
                // point where AVG(r.Rating) does not yet exist - there is no average of a
                // single review row to compare against 2.5, so SQL Server rejects an
                // aggregate in a WHERE clause outright rather than guessing. HAVING runs
                // AFTER GROUP BY has collapsed each pharmacy's reviews into one row, which
                // is the first moment the average and the count are defined. The rule
                // "average below 2.5 over at least 2 reviews" is a statement about a GROUP,
                // so it can only be expressed in the clause that filters groups.
                //
                // The same query does use a WHERE, for r.IsHidden = 0, and that placement
                // is equally deliberate: hiding is a property of one review row, so it is
                // tested before grouping and a hidden review is therefore excluded from
                // both the average and the count. Moving that test into the HAVING would
                // let a hidden review still pull a shop's average down.
                //
                // Neither threshold is concatenated into the SQL. They travel as
                // SqlParameters, so a value typed into a spinner can never become query
                // text, and the same execution plan is reused whatever numbers are chosen.
                _current = _reports.GetLowRatedPharmacies(numThreshold.Value, (int)numMinReviews.Value);
                dgvLowRated.DataSource = _current;

                // The columns only exist after DataSource has been assigned, so the header
                // work happens here rather than in ApplyTheme. The guard covers the case
                // where the query returned no columns at all because it failed.
                if (dgvLowRated.Columns.Count > 0)
                {
                    // Column keys are the aliases from the SELECT list in
                    // ReportService.GetLowRatedPharmacies. Binding by name is what makes the
                    // grid self-describing, at the cost of these strings having to track
                    // the query if an alias is ever renamed.
                    dgvLowRated.Columns["PharmacyId"].HeaderText = "ID";
                    // FillWeight is a share of the available width, not a pixel count,
                    // because every grid in the application is set to AutoSize Fill. An ID
                    // gets 30 against a default of 100, so the names get the room instead.
                    dgvLowRated.Columns["PharmacyId"].FillWeight = 30;
                    dgvLowRated.Columns["PharmacyName"].HeaderText = "Pharmacy";
                    dgvLowRated.Columns["Area"].HeaderText = "Area";
                    // The owner's name and telephone number are joined in by the query so
                    // the operator can call the shop about its rating from this one screen,
                    // rather than noting an id and going to look the contact up elsewhere.
                    dgvLowRated.Columns["OwnerName"].HeaderText = "Owner";
                    dgvLowRated.Columns["OwnerPhone"].HeaderText = "Owner phone";
                    // TotalReviews is the COUNT the HAVING clause tested, shown because it
                    // is the evidence for the average beside it. A 1.0 average over two
                    // reviews and one over fifty are very different cases, and the operator
                    // cannot judge the second column without the first.
                    dgvLowRated.Columns["TotalReviews"].HeaderText = "Reviews";
                    dgvLowRated.Columns["TotalReviews"].FillWeight = 45;
                    dgvLowRated.Columns["AverageRating"].HeaderText = "Average rating";
                    dgvLowRated.Columns["AverageRating"].FillWeight = 55;
                    // Status comes from Pharmacies, so a shop already suspended for a poor
                    // rating still appears here and is visibly marked as dealt with.
                    dgvLowRated.Columns["Status"].HeaderText = "Status";
                    dgvLowRated.Columns["Status"].FillWeight = 55;
                }

                // Open Pharmacy acts on the current row, so with no rows there is nothing
                // to open. Disabling is clearer than allowing the click and then explaining.
                btnOpenPharmacy.Enabled = _current.Rows.Count > 0;

                // An empty result is the GOOD outcome for this particular report, so it is
                // spelled out as such. A bare "0 rows" would read as a report that failed.
                // Both branches quote the thresholds back, because the reader needs to know
                // which question produced this answer.
                lblStatus.Text = _current.Rows.Count == 0
                    // ToString("N1") forces one decimal place, so the message says 2.5 and
                    // not 2.50 or 2, matching the spinner's own DecimalPlaces setting.
                    ? "No pharmacy is rated below " + numThreshold.Value.ToString("N1") +
                      " with at least " + numMinReviews.Value + " review(s). Nothing needs your attention."
                    // The singular and plural are chosen from the count rather than printing
                    // "pharmacy(ies)", because this line is read aloud in review meetings.
                    : _current.Rows.Count + " pharmac" + (_current.Rows.Count == 1 ? "y" : "ies") +
                      " rated below " + numThreshold.Value.ToString("N1") +
                      ". Double click a row to open it in Manage Pharmacies with the record already selected.";
            }
            catch (Exception ex)
            {
                // DbHelper has already translated any SqlException into a sentence, so
                // ex.Message is safe to put in front of the operator. The catch is around
                // the whole method because a partly built report is worse than none.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvLowRated_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // RowIndex is -1 for the header row, which this event also fires for, and
            // Rows[-1] would throw. The guard is part of using the event, not extra caution.
            if (e.RowIndex < 0) return;
            // No condition is needed on the row itself: the HAVING clause has already
            // guaranteed that every row present is a shop below the threshold, so the whole
            // grid is tinted. Setting the ROW's DefaultCellStyle rather than e.CellStyle
            // colours all nine cells from the first cell's event.
            dgvLowRated.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.LowStockBack;
        }

        private void OpenSelectedPharmacy()
        {
            // CurrentRow is null on an empty grid. Returning quietly is right here because
            // the button that calls this is already disabled in that case, so reaching this
            // line at all means the double click handler fired on nothing.
            if (dgvLowRated.CurrentRow == null) return;
            // Convert rather than a cast: the value arrives boxed from the DataTable as
            // whatever CLR type the provider chose for a SQL int.
            int pharmacyId = Convert.ToInt32(dgvLowRated.CurrentRow.Cells["PharmacyId"].Value);

            // The id goes in through the constructor, which is what makes the other form
            // open with this shop already selected and its Suspend button live. Passing it
            // as a constructor argument rather than setting a property afterwards means the
            // form cannot exist in a half configured state.
            using (SuperAdminManageShopsForm form = new SuperAdminManageShopsForm(pharmacyId))
            {
                // Modal, so this method blocks here until the operator closes that form.
                form.ShowDialog(this);
            }
            // Re-run the report afterwards. The operator may have suspended the shop, and
            // its Status column must now say so; re-querying is also the only way to pick
            // up a review hidden on another screen in the meantime.
            Generate();
        }

        private void dgvLowRated_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // Double clicking the column headers must not open a pharmacy.
            if (e.RowIndex < 0) return;
            OpenSelectedPharmacy();
        }

        // Two entry points, one method: the button and the double click do exactly the same
        // thing, so the behaviour is written once and cannot drift between them.
        private void btnOpenPharmacy_Click(object sender, EventArgs e) => OpenSelectedPharmacy();
        private void btnGenerate_Click(object sender, EventArgs e) => Generate();

        private void btnExport_Click(object sender, EventArgs e)
        {
            // Two conditions, because they are different failures: _current is null when
            // Generate has never succeeded, and Rows.Count is 0 when it ran and found
            // nothing. Either way there is no file worth writing, and the || short circuits
            // so the second test never dereferences a null.
            if (_current == null || _current.Rows.Count == 0)
            {
                MessageBox.Show("There is nothing to export.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // using() on the dialog, because a SaveFileDialog holds unmanaged resources and
            // this handler can run many times in a session.
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                // The filter restricts the picker to .csv and supplies the extension, so
                // the operator cannot accidentally save a spreadsheet as a text file.
                dialog.Filter = "CSV file (*.csv)|*.csv";
                // A dated default name, in yyyy-MM-dd so the files sort chronologically in
                // a folder listing. That is the reason for the ISO order rather than the
                // local dd/MM/yyyy used elsewhere on screen.
                dialog.FileName = "PharmaLink-LowRated-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                // Anything other than OK means the operator cancelled, so nothing is written.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string message;
                // ExportToCsv is static because it needs no service state: it takes a table
                // and a path and touches no database at all. It reports through an out
                // parameter so a failure to write the file arrives as a sentence rather
                // than an exception that would have to be caught here.
                ReportService.ExportToCsv(_current, dialog.FileName, out message);
                // The message goes to the status line rather than a dialog. It names the
                // full path, so the operator can find the file they just wrote.
                lblStatus.Text = message;
            }
        }

        // Close, not Dispose or Hide. The dashboard opened this form with ShowDialog inside
        // a using block, so closing returns control there and the dashboard refreshes.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
