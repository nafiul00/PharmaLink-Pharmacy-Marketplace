using System.Data;                  // DataTable, the shape ReviewService.GetModerationQueue returns
using System.Drawing;               // Color, for the grey and red row tints
using System.Windows.Forms;         // Form, DataGridView, CheckBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette and control styles
using PharmaLinkApp.Services;       // ReviewService, which owns the queue query and the hide update

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by SuperAdminDashboard. Uses ReviewService
    //  for the queue and for the single UPDATE this screen performs.
    //
    //  Flow:
    //      Load -> ApplyTheme -> fill the rating filter -> _loading = false
    //           -> LoadGrid
    //      Hide / Unhide -> ReviewService.SetHidden -> LoadGrid
    //
    //  The only write on this form is UPDATE Reviews SET IsHidden. Nothing here
    //  deletes a row, and that is the whole design: a hidden review stops
    //  counting everywhere while remaining on record.
    //
    //  There is no SQL in this file.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirement 8. The moderation queue, filtered by default to one and two
    /// star reviews, which is where abuse usually sits.
    ///
    /// Hide Review sets IsHidden to 1 rather than deleting the row, so the
    /// review disappears from the customer screens and from every average rating
    /// calculation while remaining available if the pharmacy disputes it.
    /// </summary>
    public partial class ModerateReviewsForm : Form
    {
        private readonly ReviewService _reviews = new ReviewService();

        // The re-entrancy guard. Setting cmbRating.SelectedIndex in code raises
        // SelectedIndexChanged exactly as a click does, and that handler calls LoadGrid.
        // Starting true means the assignment during Load is ignored and the grid is
        // queried once, deliberately, at the end of Load.
        private bool _loading = true;

        public ModerateReviewsForm()
        {
            InitializeComponent();
        }

        private void ModerateReviewsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            // Three bands rather than a free numeric box, because moderation is about
            // triage: the operator wants the worst reviews first, not an arbitrary cut off.
            // The captions are what the operator reads; MaxRating() below turns the chosen
            // index into the number the query actually filters on.
            cmbRating.Items.AddRange(new object[]
            {
                "1 and 2 stars only  (default)",
                "3 stars and below",
                "All ratings"
            });
            // Index 0 is the default because one and two star reviews are where abusive
            // wording sits. Opening on "All ratings" would bury those in hundreds of
            // ordinary five star entries that need no attention at all.
            cmbRating.SelectedIndex = 0;

            // Cleared only after the combo box is populated, so the change event it raised
            // while filling has already been swallowed by the guard in LoadGrid.
            _loading = false;
            LoadGrid();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Moderate Reviews");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StyleDanger(btnHide);
            UiTheme.StyleSuccess(btnUnhide);
            UiTheme.StyleGrid(dgvReviews);
            dgvReviews.CellFormatting += dgvReviews_CellFormatting;

            txtComment.BackColor = Color.White;
            txtComment.Font = UiTheme.FontBody;
            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        private int MaxRating()
        {
            // The combo box holds captions, the query needs a number, and this is the one
            // place that translates between them. Keeping the mapping in a method rather
            // than parsing the caption text means the wording on screen can be changed
            // without touching the filter, and a caption typo cannot alter what is queried.
            switch (cmbRating.SelectedIndex)
            {
                case 0: return 2;     // "1 and 2 stars only"
                case 1: return 3;     // "3 stars and below"
                // 5 rather than a magic large number, because CK_Reviews_Rating limits
                // Rating to 1 to 5, so "<= 5" genuinely means every review there can be.
                default: return 5;    // "All ratings"
            }
        }

        private void LoadGrid()
        {
            // Both filter controls route here, so one guard covers them both.
            if (_loading) return;

            try
            {
                // Two arguments, two independent filters in the query:
                //   Rating <= @MaxRating           picks the severity band, and
                //   (@IncludeHidden = 1 OR IsHidden = 0)  decides whether reviews that
                // have already been dealt with are shown. The default is unticked, so the
                // queue holds only work still to do; ticking it is how a past decision is
                // found again in order to reverse it.
                DataTable table = _reviews.GetModerationQueue(MaxRating(), chkIncludeHidden.Checked);
                dgvReviews.DataSource = table;

                // Columns exist only after DataSource is assigned, so the headers follow the
                // bind. The guard covers a failed load, where indexing by name would throw.
                if (dgvReviews.Columns.Count > 0)
                {
                    dgvReviews.Columns["ReviewId"].HeaderText = "ID";
                    // FillWeight is a proportion of the width, not pixels, because the grid
                    // auto sizes to fill. The comment column is given far more than the rest.
                    dgvReviews.Columns["ReviewId"].FillWeight = 30;
                    // Reviewer is an alias for Users.FullName, joined in on Reviews.
                    // CustomerId, so the operator can see who wrote a comment before acting.
                    dgvReviews.Columns["Reviewer"].HeaderText = "Reviewer";
                    dgvReviews.Columns["MedicineName"].HeaderText = "Medicine";
                    // The pharmacy is reached by joining Medicines to Pharmacies, because a
                    // review is written against a MEDICINE and only belongs to a shop
                    // through it. That indirection is the same reason the low rated report
                    // has to group back up to the pharmacy to get an average.
                    dgvReviews.Columns["PharmacyName"].HeaderText = "Pharmacy";
                    dgvReviews.Columns["Rating"].HeaderText = "Stars";
                    dgvReviews.Columns["Rating"].FillWeight = 32;
                    dgvReviews.Columns["Comment"].HeaderText = "Comment";
                    // 160 against a default of 100, so the text gets the most room in the
                    // grid. The full comment is still shown in the box below, because a long
                    // one is truncated in a cell and moderation depends on reading all of it.
                    dgvReviews.Columns["Comment"].FillWeight = 160;
                    dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";
                    // OrderId is shown because it is the proof the review is genuine: a
                    // review can only be written against an order the customer actually
                    // received, so this number is the audit trail back to that purchase.
                    dgvReviews.Columns["OrderId"].HeaderText = "Order";
                    dgvReviews.Columns["OrderId"].FillWeight = 40;
                    dgvReviews.Columns["IsHidden"].HeaderText = "Hidden";
                    dgvReviews.Columns["IsHidden"].FillWeight = 40;
                    // Ticked when a pharmacy owner reported the review from Customer Reviews.
                    dgvReviews.Columns["IsReported"].HeaderText = "Reported";
                    dgvReviews.Columns["IsReported"].FillWeight = 48;
                }

                // Reported reviews are counted separately, because each one is a pharmacy
                // owner waiting on a decision.
                int reported = table.Select("IsReported = true").Length;
                lblStatus.Text = table.Rows.Count + " review(s) in the queue" +
                                 (reported > 0 ? ", " + reported + " reported by a pharmacy." : ".");
                // Rebinding can leave a different row current, or none, so the comment box
                // and the two buttons are recomputed rather than left as they were.
                UpdateSelection();
            }
            catch (Exception ex)
            {
                // DbHelper has already turned any SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // RowIndex is -1 for the header row, which fires this event too, and the column
            // test covers the gap between a failed load and the next successful bind.
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;

            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];
            // Both values are read once into locals rather than through the cell indexer
            // three times, and both arrive as object because the grid is bound to a
            // DataTable that knows nothing about bool or int at this level.
            object hidden = row.Cells["IsHidden"].Value;
            object rating = row.Cells["Rating"].Value;

            // THE ORDER OF THESE THREE BRANCHES IS THE LOGIC.
            // Hidden is tested first, so an already hidden one star review draws grey
            // rather than red. Testing the rating first would paint it as outstanding work
            // when it has in fact already been dealt with, which is precisely the mistake
            // this screen exists to avoid.
            //
            // Both null and DBNull are tested before converting: Convert.ToBoolean(null) is
            // defined to be false, but Convert.ToBoolean(DBNull.Value) throws, so the second
            // test is the one that actually matters.
            if (hidden != null && hidden != DBNull.Value && Convert.ToBoolean(hidden))
            {
                // Grey on muted text reads as "switched off" rather than "urgent", which is
                // exactly the status of a review that has already been hidden.
                row.DefaultCellStyle.BackColor = Color.FromArgb(238, 238, 238);
                row.DefaultCellStyle.ForeColor = UiTheme.TextMuted;
            }
            // Only reached when the review is still visible. Two stars and below is the same
            // threshold the default filter uses, so the rows that matter most stay marked
            // even when the operator has widened the filter to all ratings.
            // A review a pharmacy reported is marked the same way, whatever its star rating.
            else if ((rating != null && rating != DBNull.Value && Convert.ToInt32(rating) <= 2) ||
                     (row.Cells["IsReported"].Value is bool reported && reported))
            {
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
            else
            {
                // The explicit reset is not redundant. Grid rows are recycled as the list
                // scrolls, so a row that is left unpainted keeps the previous row's colours
                // and grey or red would smear down the grid.
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
        }

        // One handler for the selection, because the comment box and the two buttons all
        // depend on the same thing: which row is current.
        private void dgvReviews_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            DataGridViewRow row = dgvReviews.CurrentRow;

            // The empty case handled first and in full: an empty queue, or a grid mid
            // rebind, must leave no stale comment on screen beside two live buttons.
            if (row == null || row.Cells["ReviewId"].Value == null)
            {
                txtComment.Clear();
                btnHide.Enabled = false;
                btnUnhide.Enabled = false;
                return;
            }

            // The comment is repeated in a full width box because a grid cell truncates it,
            // and a moderation decision depends on reading the whole sentence.
            object comment = row.Cells["Comment"].Value;
            // Comment is nullable in the schema, since a customer may rate without writing
            // anything. The placeholder names what is absent rather than leaving an empty
            // box, which would be indistinguishable from a control that failed to fill.
            txtComment.Text = comment == null || comment == DBNull.Value ? "(no written comment)" : comment.ToString();

            // Only DBNull is tested here, unlike CellFormatting above, and that is
            // sufficient: Convert.ToBoolean(null) is defined to return false, so a null
            // would fall through to the same answer, whereas DBNull would throw.
            bool hidden = row.Cells["IsHidden"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsHidden"].Value);

            // Exactly one of the two is ever available, driven by the row's own state rather
            // than by remembering what was last clicked. Hiding twice would be a no-op, and
            // offering it would suggest the first attempt had not worked.
            btnHide.Enabled = !hidden;
            btnUnhide.Enabled = hidden;
        }

        private void btnHide_Click(object sender, EventArgs e)
        {
            // Read the current row at the moment of the click rather than caching it when
            // the selection changed, so the action cannot apply to a row that has moved on.
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;

            // The id is what the update travels on. The pharmacy name is only used in the
            // messages, so a stale caption cannot cause the wrong review to be hidden.
            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);
            string pharmacy = row.Cells["PharmacyName"].Value.ToString();

            // The prompt states the mechanism, not just the intent, because the difference
            // between hiding and deleting is the entire point of this screen and the
            // operator should know the decision is reversible before they take it.
            DialogResult answer = MessageBox.Show(
                "Hide this review from the customer screens?\r\n\r\n" +
                "IsHidden is set to 1. The row is not deleted, so " + pharmacy +
                "'s rating history stays complete and the decision can be reversed.",
                "Hide review", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Testing against Yes rather than for No, so dismissing the dialog any other way
            // also counts as a refusal.
            if (answer != DialogResult.Yes) return;

            // HIDE, never DELETE. SetHidden runs UPDATE Reviews SET IsHidden = 1, so the
            // row survives. Every customer-facing query and every average-rating
            // calculation carries "WHERE r.IsHidden = 0", which is why hiding one review
            // silently changes the pharmacy's average on the low-rated report as well.
            //
            // Keeping the row matters twice over: the pharmacy can dispute the decision,
            // and Reviews.OrderId is a foreign key to a real order, so deleting reviews
            // would erase part of the audit trail that proves ratings are genuine.
            //
            // The soft delete is also what makes the effect reversible and measurable. A
            // DELETE would take the rating out of the average identically, but it would
            // take the evidence with it: nobody could afterwards show which review was
            // removed, who wrote it, when, or against which order, and a shop whose average
            // had risen overnight would have no way to be told why. IsHidden costs one bit
            // per row and buys a complete record of every moderation decision ever taken.
            //
            // It is the same pattern as Medicines.IsActive and the suspension of a
            // pharmacy: throughout this application a record with history behind it is
            // deactivated rather than destroyed.
            if (!ApplyHidden(reviewId, true)) return;
            // LoadGrid first, then the message: LoadGrid writes the row count into lblStatus,
            // so a message set before it would be overwritten before anyone could read it.
            LoadGrid();   // re-query so the row redraws greyed out, or leaves the queue
            lblStatus.Text = "Review " + reviewId + " hidden. It no longer counts towards " + pharmacy + "'s average rating.";
        }

        private void btnUnhide_Click(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;

            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);
            // The exact inverse, through the same method with false: IsHidden goes back to
            // 0 and the review counts again everywhere it did before. No confirmation is
            // asked for, because restoring a customer's own words is the harmless direction
            // and it is undone by the button beside it. This only works at all because the
            // hide never destroyed anything.
            if (!ApplyHidden(reviewId, false)) return;
            LoadGrid();
            lblStatus.Text = "Review " + reviewId + " restored and is visible to customers again.";
        }

        // Runs the update for both buttons and reports failure, so neither button can show a
        // success message for a write that did not happen.
        private bool ApplyHidden(int reviewId, bool hidden)
        {
            try
            {
                // SetHidden returns true only when exactly one row changed.
                if (_reviews.SetHidden(reviewId, hidden)) return true;

                MessageBox.Show("Review " + reviewId + " was not found. The list has been refreshed.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LoadGrid();
                return false;
            }
            catch (Exception ex)
            {
                // DbHelper has already turned any SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // Both the rating combo box and the Include hidden tick box are wired here, and the
        // Refresh button as well, because all three want the same thing: re-run the query
        // with whatever the filters now hold.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close, not Dispose: the dashboard opened this form inside a using block, so
        // closing returns control there and the dashboard refreshes itself.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
