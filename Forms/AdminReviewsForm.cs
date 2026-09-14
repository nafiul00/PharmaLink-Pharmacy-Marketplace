using System.Data;                  // DataTable, the shape ReviewService.GetForPharmacy returns
using System.Drawing;               // Color and Font for the rating colours and the average label
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the event argument types
using PharmaLinkApp.Helpers;        // UiTheme: colours, fonts and the grid styling
using PharmaLinkApp.Services;       // ReviewService, the only class here that holds any SQL

namespace PharmaLinkApp.Forms   // presentation only; not one line of SQL is written in a form
{
    /// <summary>The reviews written about this pharmacy's medicines.</summary>
    public partial class AdminReviewsForm : Form   // requirement 15
    {
        // Three reads and one write: Report flags a review, it never edits or deletes one.
        private readonly ReviewService _reviews = new ReviewService();
        // True until Load finishes, so setting SelectedIndex below cannot query early.
        private bool _loading = true;

        public AdminReviewsForm()   // runs before the window exists, so no query belongs here
        {
            InitializeComponent();   // designer generated controls only
        }

        private void AdminReviewsForm_Load(object sender, EventArgs e)   // runs once, after the window exists
        {
            ApplyTheme();   // colours, fonts, grid styling and the CellFormatting hook up

            // Added here, next to RatingRange which decodes them; the ORDER is the contract.
            cmbRating.Items.AddRange(new object[]
            {
                "All ratings",          // index 0, the default and the widest band
                "5 stars only",         // index 1, just the top mark
                "4 stars and above",    // index 2, the satisfied customers
                "3 stars and below",    // index 3, everything that is not praise
                "1 and 2 stars only"    // index 4, the complaints the Super Admin moderates
            });
            cmbRating.SelectedIndex = 0;   // raises SelectedIndexChanged, which _loading swallows

            _loading = false;   // from here on the filter is allowed to query
            LoadGrid();         // one deliberate first load, now that everything is wired
        }

        // Pure presentation, and note what it does NOT style: no delete or hide button.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Customer Reviews");   // window background and title bar text

            panelHeader.BackColor = UiTheme.Primary;                 // the brand green band
            lblTitle.Font = UiTheme.FontTitle;                       // the page name, the largest text here
            lblTitle.ForeColor = Color.White;                        // the only colour legible on the green band
            lblSubtitle.Font = UiTheme.FontSmall;                    // the fixed one-line explanation
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // pale green, so it supports the title

            lblAverage.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);   // the headline figure
            lblAverage.ForeColor = UiTheme.TextDark;   // recoloured to Danger in LoadGrid when the score is poor

            lblReadOnlyNote.Font = UiTheme.FontSmall;        // the standing note about what an owner may do
            lblReadOnlyNote.ForeColor = UiTheme.TextMuted;   // grey, so guidance never reads as live feedback
            lblStatus.Font = UiTheme.FontSmall;              // the outcome line LoadGrid and Report write to
            lblStatus.ForeColor = UiTheme.TextMuted;         // grey too: failures use a dialog instead
            txtComment.Font = UiTheme.FontBody;              // the full comment box under the grid
            txtComment.BackColor = Color.White;              // white even though it is ReadOnly, so it reads as text

            UiTheme.StyleSecondary(btnBack);      // grey: leaves the screen and changes nothing
            UiTheme.StyleSecondary(btnRefresh);   // grey too: re-runs the same read
            UiTheme.StyleAccent(btnReport);       // the one button here that writes to the database
            UiTheme.StyleGrid(dgvReviews);        // shared grid styling, including Fill column sizing
            dgvReviews.CellFormatting += dgvReviews_CellFormatting;   // what colours the rows by rating
        }

        private void RatingRange(out int min, out int max)   // combo index into a rating band
        {
            // Two out parameters, because the service takes BETWEEN @MinRating AND @MaxRating.
            switch (cmbRating.SelectedIndex)
            {
                case 1: min = 5; max = 5; break;   // only the top mark
                case 2: min = 4; max = 5; break;   // the satisfied customers
                case 3: min = 1; max = 3; break;   // everything that is not praise
                case 4: min = 1; max = 2; break;   // the complaints the Super Admin moderates
                // default, not case 0: SelectedIndex is -1 until something is chosen.
                default: min = 1; max = 5; break;
            }
        }

        private void LoadGrid()   // every refresh path on this form comes through here
        {
            if (_loading) return;   // one guard covers the first load, Refresh and the filter

            try   // the three reads below are one unit of work
            {
                int min, max;              // filled by RatingRange from the combo box
                RatingRange(out min, out max);   // the only user input that reaches the query

                // PharmacyId comes from UserSession, never a control, so this cannot widen.
                DataTable table = _reviews.GetForPharmacy(UserSession.PharmacyId, min, max);
                dgvReviews.DataSource = table;   // binding is what CREATES the columns below

                if (dgvReviews.Columns.Count > 0)   // guards a failed bind, where a name lookup throws
                {
                    dgvReviews.Columns["ReviewId"].HeaderText = "ID";      // the key the Report button acts on
                    dgvReviews.Columns["ReviewId"].FillWeight = 28;        // FillWeight is a share of width, not pixels
                    // The reviewer's real name, joined from Users; reviews are not anonymous here.
                    dgvReviews.Columns["ReviewerName"].HeaderText = "Reviewer";
                    dgvReviews.Columns["MedicineName"].HeaderText = "Medicine";   // what was reviewed
                    dgvReviews.Columns["Strength"].HeaderText = "Strength";       // 250mg against 500mg
                    dgvReviews.Columns["Strength"].FillWeight = 45;               // a short column, so a small share
                    dgvReviews.Columns["Rating"].HeaderText = "Stars";   // read at a glance as a five point scale
                    dgvReviews.Columns["Rating"].FillWeight = 32;        // a single digit needs almost no room
                    dgvReviews.Columns["Comment"].HeaderText = "Comment";   // the part actually worth reading
                    dgvReviews.Columns["Comment"].FillWeight = 170;         // by far the largest share of the width
                    dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";   // when it was posted
                    // The PROOF: AddReview only inserts against a delivered order.
                    dgvReviews.Columns["OrderId"].HeaderText = "Order";
                    dgvReviews.Columns["OrderId"].FillWeight = 40;   // an order number is short
                }

                // Two round trips, not an average of the grid: it holds only the band.
                decimal average = _reviews.GetAverageForPharmacy(UserSession.PharmacyId);
                int total = _reviews.CountForPharmacy(UserSession.PharmacyId);   // how many it is built from

                // "No reviews yet" rather than "0.00 / 5": an unrated shop is not a bad one.
                lblAverage.Text = total == 0
                    ? "No reviews yet"   // the service returns 0 for an empty set, so count decides
                    : "Average rating  " + average.ToString("N2") + " / 5   from " + total + " review(s)";   // N2 pins it at two places

                // Red only when there IS an average and it is poor, so a new shop stays dark.
                lblAverage.ForeColor = average > 0 && average < 2.5m ? UiTheme.Danger : UiTheme.TextDark;

                // The rule the low rated report uses: below 2.5 with at least two reviews.
                lblStatus.Text = average > 0 && average < 2.5m && total >= 2
                    ? "Warning: your average is below 2.5 with " + total + " reviews, which puts your shop on the Super Admin's low rated report."   // the warning branch
                    : table.Rows.Count + " review(s) shown. Every review is tied to a delivered order, so none of them are fake.";   // the ordinary branch

                UpdateSelection();   // rebinding cleared the selection, so the box and button are re-evaluated
            }
            catch (Exception ex)   // one catch around all three reads
            {
                // DbHelper already turned the SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)   // paints rows by rating
        {
            // Fires once per CELL as it is painted, so it must stay cheap and never query.
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;   // -1 is the header row

            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];   // the row being painted right now
            object rating = row.Cells["Rating"].Value;           // object, because the grid is bound to a DataTable
            // null happens mid rebind; Convert.ToInt32 throws on DBNull rather than 0.
            if (rating == null || rating == DBNull.Value) return;

            int stars = Convert.ToInt32(rating);   // safe now that both empty cases are excluded
            // The thresholds match the filter bands, so colour and filter agree on 'bad'.
            if (stars <= 2) row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;        // the red used for attention
            else if (stars >= 4) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;  // the green used for done
            // Not redundant: rows are REUSED while scrolling, so unpainted keeps old paint.
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        // The comment box and the Report button both depend on which row is current.
        private void dgvReviews_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()   // the comment box and the Report button, together
        {
            DataGridViewRow row = dgvReviews.CurrentRow;   // null on an empty grid and mid rebind

            if (row == null || row.Cells["ReviewId"].Value == null)   // nothing selected to act on
            {
                txtComment.Clear();        // cleared, so old text cannot sit under an empty grid
                btnReport.Enabled = false; // offering an action that cannot succeed is worse than none
                return;                    // nothing else to compute without a row
            }

            object comment = row.Cells["Comment"].Value;   // nullable: a rating may carry no words
            // The placeholder NAMES the situation, because a blank box reads as a failed load.
            txtComment.Text = comment == null || comment == DBNull.Value
                ? "(this customer left a rating but no written comment)"   // said plainly
                : comment.ToString();   // the full text the grid cell truncates

            // ReadOnly in the designer: an owner may read a review but never type over it.
            btnReport.Enabled = true;
        }

        private void btnReport_Click(object sender, EventArgs e)   // flags one review for the admin
        {
            // The only write here. No Delete or Hide button exists on the designer surface.
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;   // re-checked, even though the button is disabled without a row

            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);   // what the update travels on

            // Asked first, because a report puts the review in front of the Super Admin.
            DialogResult answer = MessageBox.Show(
                "Report review " + reviewId + " to the Super Admin?\r\n\r\n" +   // names the id being sent
                "It stays visible to customers until the Super Admin decides. Only the Super Admin " +   // what happens next
                "can hide a review, and even then the row is never deleted.",   // states the mechanism
                "Report review", MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // a question, not a warning
            if (answer != DialogResult.Yes) return;   // testing against Yes, so any dismissal refuses

            try   // the one database write this form performs
            {
                // Report WRITES: it sets Reviews.IsReported = 1, scoped to this shop.
                if (_reviews.Report(reviewId, UserSession.PharmacyId))
                {
                    // The queue always includes reported reviews whatever their rating, first.
                    lblStatus.Text = "Review " + reviewId + " reported. It is now at the top of the Super Admin's Moderate Reviews queue.";
                }
                else   // 0 rows: already hidden in the meantime, or not about this shop
                {
                    MessageBox.Show("Review " + reviewId + " could not be reported. It may already have been hidden by the Super Admin.",   // the honest answer
                        "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // says so rather than claiming success
                    LoadGrid();   // re-read, so the grid stops showing a row that has moved on
                }
            }
            catch (Exception ex)   // the write can still fail for a database reason
            {
                // DbHelper has already turned any SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // The rating combo and Refresh share one handler, so they cannot differ.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close, not Hide: the dashboard opened this with ShowDialog and disposes it.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
