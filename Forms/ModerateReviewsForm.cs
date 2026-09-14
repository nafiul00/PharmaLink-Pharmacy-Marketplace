using System.Data;                  // DataTable, the shape ReviewService.GetModerationQueue returns
using System.Drawing;               // Color, for the grey and red row tints
using System.Windows.Forms;         // Form, DataGridView, CheckBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the shared palette and control styles
using PharmaLinkApp.Services;       // ReviewService, which owns the queue query and the hide update

namespace PharmaLinkApp.Forms   // presentation only; there is no SQL in this file
{
    /// <summary>The moderation queue: hide a review, never delete it.</summary>
    public partial class ModerateReviewsForm : Form   // requirement 8, opened by SuperAdminDashboard
    {
        // The only write here is ReviewService.SetHidden; nothing deletes a row.
        private readonly ReviewService _reviews = new ReviewService();
        // The re-entrancy guard: SelectedIndex in code raises the same event a click does.
        private bool _loading = true;

        public ModerateReviewsForm()   // no database work before the window exists
        {
            InitializeComponent();   // designer generated controls only
        }

        private void ModerateReviewsForm_Load(object sender, EventArgs e)   // runs once, after the window exists
        {
            ApplyTheme();   // colours, fonts, grid styling and the CellFormatting hook up

            // Three bands rather than a free numeric box, because moderation is triage.
            cmbRating.Items.AddRange(new object[]
            {
                "1 and 2 stars only  (default)",   // index 0, where abusive wording sits
                "3 stars and below",               // index 1, everything that is not praise
                "All ratings"                      // index 2, used when reviewing a past decision
            });
            // Index 0 by default: opening on "All ratings" would bury the complaints.
            cmbRating.SelectedIndex = 0;

            _loading = false;   // cleared only after the combo raised its change event
            LoadGrid();         // one deliberate first load, now that everything is wired
        }

        private void ApplyTheme()   // pure presentation, called once from Load
        {
            UiTheme.StyleForm(this, "Moderate Reviews");   // window background and title bar text

            panelHeader.BackColor = UiTheme.Primary;                 // the brand green band
            lblTitle.Font = UiTheme.FontTitle;                       // the page name, the largest text here
            lblTitle.ForeColor = Color.White;                        // the only colour legible on the green band
            lblSubtitle.Font = UiTheme.FontSmall;                    // the fixed one-line explanation
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // pale green, so it supports the title

            UiTheme.StyleSecondary(btnBack);      // grey: leaves the screen and changes nothing
            UiTheme.StyleSecondary(btnRefresh);   // grey too: re-runs the same read
            UiTheme.StyleDanger(btnHide);         // red, because hiding removes a review from view
            UiTheme.StyleSuccess(btnUnhide);      // green, because restoring puts it back
            UiTheme.StyleGrid(dgvReviews);        // shared grid styling, including Fill column sizing
            dgvReviews.CellFormatting += dgvReviews_CellFormatting;   // what tints the rows by state

            txtComment.BackColor = Color.White;      // white, so the full comment reads as text
            txtComment.Font = UiTheme.FontBody;      // the body font, matching the grid
            lblNote.Font = UiTheme.FontSmall;        // the standing note about hiding versus deleting
            lblNote.ForeColor = UiTheme.TextMuted;   // grey, so guidance never reads as live feedback
            lblStatus.Font = UiTheme.FontSmall;      // the outcome line LoadGrid and the buttons write to
            lblStatus.ForeColor = UiTheme.TextMuted; // grey too: failures use a dialog instead
        }

        private int MaxRating()   // the one place that turns a caption into a number
        {
            // Keeping the mapping here lets the wording change without moving the filter.
            switch (cmbRating.SelectedIndex)
            {
                case 0: return 2;     // "1 and 2 stars only"
                case 1: return 3;     // "3 stars and below"
                // 5, not a magic large number: CK_Reviews_Rating limits Rating to 1 to 5.
                default: return 5;    // "All ratings"
            }
        }

        private void LoadGrid()   // every refresh path on this form comes through here
        {
            if (_loading) return;   // one guard covers both filter controls and Refresh

            try   // the read and the rebind are one unit of work
            {
                // Two filters: the severity band, and whether rows already dealt with show.
                DataTable table = _reviews.GetModerationQueue(MaxRating(), chkIncludeHidden.Checked);
                dgvReviews.DataSource = table;   // binding is what CREATES the columns below

                if (dgvReviews.Columns.Count > 0)   // guards a failed load, where a name lookup throws
                {
                    dgvReviews.Columns["ReviewId"].HeaderText = "ID";   // the key both buttons act on
                    dgvReviews.Columns["ReviewId"].FillWeight = 30;     // a share of the width, not pixels
                    // Reviewer is an alias for Users.FullName, joined on Reviews.CustomerId.
                    dgvReviews.Columns["Reviewer"].HeaderText = "Reviewer";
                    dgvReviews.Columns["MedicineName"].HeaderText = "Medicine";   // what was reviewed
                    // A review belongs to a shop only through its medicine, hence the extra join.
                    dgvReviews.Columns["PharmacyName"].HeaderText = "Pharmacy";
                    dgvReviews.Columns["Rating"].HeaderText = "Stars";   // the severity at a glance
                    dgvReviews.Columns["Rating"].FillWeight = 32;        // a single digit needs almost no room
                    dgvReviews.Columns["Comment"].HeaderText = "Comment";   // the text being moderated
                    dgvReviews.Columns["Comment"].FillWeight = 160;         // the most room, against a default of 100
                    dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";   // when it was posted
                    // The proof the review is genuine: it traces back to an order really received.
                    dgvReviews.Columns["OrderId"].HeaderText = "Order";
                    dgvReviews.Columns["OrderId"].FillWeight = 40;   // an order number is short
                    dgvReviews.Columns["IsHidden"].HeaderText = "Hidden";   // which rows are already dealt with
                    dgvReviews.Columns["IsHidden"].FillWeight = 40;         // a tick box needs little room
                    // Ticked when a pharmacy owner reported the review from Customer Reviews.
                    dgvReviews.Columns["IsReported"].HeaderText = "Reported";
                    dgvReviews.Columns["IsReported"].FillWeight = 48;   // slightly wider, for the caption
                }

                // Counted separately, because each reported row is an owner awaiting a decision.
                int reported = table.Select("IsReported = true").Length;
                lblStatus.Text = table.Rows.Count + " review(s) in the queue" +   // the plain row count
                                 (reported > 0 ? ", " + reported + " reported by a pharmacy." : ".");   // and the reports
                UpdateSelection();   // rebinding can leave a different row current, or none at all
            }
            catch (Exception ex)   // the read can fail for a database reason
            {
                // DbHelper has already turned any SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)   // tints rows by state
        {
            // RowIndex is -1 for the header row, and the column test covers a failed load.
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;

            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];   // the row being painted right now
            object hidden = row.Cells["IsHidden"].Value;   // object, because the grid is bound to a DataTable
            object rating = row.Cells["Rating"].Value;     // read once into a local rather than indexed twice

            // ORDER IS THE LOGIC: hidden first, so a hidden 1 star row draws grey, not red.
            if (hidden != null && hidden != DBNull.Value && Convert.ToBoolean(hidden))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(238, 238, 238);   // grey reads as "switched off"
                row.DefaultCellStyle.ForeColor = UiTheme.TextMuted;   // muted text, matching that grey
            }
            // Still visible: two stars and below, or reported at any rating, is marked red.
            else if ((rating != null && rating != DBNull.Value && Convert.ToInt32(rating) <= 2) ||
                     (row.Cells["IsReported"].Value is bool reported && reported))   // an owner flagged it
            {
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;   // the red used for anything needing attention
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;      // dark text, so the red stays readable
            }
            else   // an ordinary visible review, three stars or above and unreported
            {
                // Not redundant: rows are RECYCLED while scrolling, so unpainted keeps old paint.
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;   // back to the ordinary body colour
            }
        }

        // The comment box and both buttons depend on the same thing: which row is current.
        private void dgvReviews_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()   // recomputes the box and the two buttons together
        {
            DataGridViewRow row = dgvReviews.CurrentRow;   // null on an empty queue and mid rebind

            if (row == null || row.Cells["ReviewId"].Value == null)   // nothing selected to act on
            {
                txtComment.Clear();          // no stale comment left beside two live buttons
                btnHide.Enabled = false;     // neither action can succeed without a row
                btnUnhide.Enabled = false;   // so both are switched off together
                return;                      // nothing else to compute
            }

            // Repeated in a full width box, because a grid cell truncates the sentence.
            object comment = row.Cells["Comment"].Value;
            // Comment is nullable, so the placeholder names what is absent.
            txtComment.Text = comment == null || comment == DBNull.Value ? "(no written comment)" : comment.ToString();

            // Only DBNull is tested here: Convert.ToBoolean(null) returns false anyway.
            bool hidden = row.Cells["IsHidden"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsHidden"].Value);   // the row's own state

            // Exactly one is ever available, driven by the row rather than by memory.
            btnHide.Enabled = !hidden;   // hiding twice would be a no-op that looks like a failure
            btnUnhide.Enabled = hidden;  // and restoring is only offered on something hidden
        }

        private void btnHide_Click(object sender, EventArgs e)   // the red button: IsHidden goes to 1
        {
            // Read at the moment of the click, so the action cannot apply to a row that moved.
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;   // re-checked even though the button is disabled without a row

            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);   // what the update travels on
            string pharmacy = row.Cells["PharmacyName"].Value.ToString();  // used only in the messages

            // The prompt states the MECHANISM: hiding rather than deleting is the point.
            DialogResult answer = MessageBox.Show(
                "Hide this review from the customer screens?\r\n\r\n" +   // what the operator is choosing
                "IsHidden is set to 1. The row is not deleted, so " + pharmacy +   // names the column and the shop
                "'s rating history stays complete and the decision can be reversed.",   // and that it is reversible
                "Hide review", MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // a question, not a warning

            // Tested against Yes, so dismissing the dialog any other way also refuses.
            if (answer != DialogResult.Yes) return;

            // HIDE, never DELETE: every average carries IsHidden = 0, and the row survives.
            if (!ApplyHidden(reviewId, true)) return;
            // LoadGrid first, then the message: LoadGrid writes the row count into lblStatus.
            LoadGrid();   // re-query so the row redraws greyed out, or leaves the queue
            lblStatus.Text = "Review " + reviewId + " hidden. It no longer counts towards " + pharmacy + "'s average rating.";   // written after LoadGrid
        }

        private void btnUnhide_Click(object sender, EventArgs e)   // the green button: IsHidden back to 0
        {
            DataGridViewRow row = dgvReviews.CurrentRow;   // the same read-at-click-time rule
            if (row == null) return;   // nothing selected, so there is nothing to restore

            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);   // the key the update travels on
            // The exact inverse, and unconfirmed: restoring is the harmless direction.
            if (!ApplyHidden(reviewId, false)) return;
            LoadGrid();   // re-query, so the row loses its grey tint
            lblStatus.Text = "Review " + reviewId + " restored and is visible to customers again.";   // and counting again
        }

        // Runs the update for both buttons, so neither reports a write that failed.
        private bool ApplyHidden(int reviewId, bool hidden)
        {
            try   // the one database write this form performs
            {
                if (_reviews.SetHidden(reviewId, hidden)) return true;   // true only when one row changed

                MessageBox.Show("Review " + reviewId + " was not found. The list has been refreshed.",   // 0 rows
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // a warning, not an error
                LoadGrid();     // re-read, so the grid stops showing a row that has gone
                return false;   // the caller must not print a success message
            }
            catch (Exception ex)   // the write can still fail for a database reason
            {
                // DbHelper has already turned any SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;   // treated exactly like a refused write by both callers
            }
        }

        // The combo, the Include hidden tick box and Refresh all want the same re-read.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close, not Dispose: the dashboard opened this inside a using block.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
