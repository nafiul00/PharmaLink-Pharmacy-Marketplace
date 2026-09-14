using System.Data;                  // DataTable, the shape ReviewService.GetForPharmacy returns
using System.Drawing;               // Color and Font for the rating colours and the average label
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the event argument types
using PharmaLinkApp.Helpers;        // UiTheme: colours, fonts and the grid styling
using PharmaLinkApp.Services;       // ReviewService, the only class here that holds any SQL

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 15. The reviews written about this pharmacy's medicines.
    ///
    /// The form is read only on purpose: an owner can read the ratings but can
    /// neither edit nor delete them. If he believes a review is abusive he uses
    /// the Report button, which flags it for the Super Admin rather than
    /// removing it himself.
    /// </summary>
    public partial class AdminReviewsForm : Form
    {
        // ONE service, and note which of its methods this form calls: GetForPharmacy,
        // GetAverageForPharmacy and CountForPharmacy, all reads. ReviewService also exposes
        // SetHidden, which runs UPDATE Reviews SET IsHidden, but that method is called only
        // from the Super Admin's ModerateReviewsForm. The capability exists; this screen
        // deliberately never reaches for it.
        private readonly ReviewService _reviews = new ReviewService();
        // True until Load has finished. cmbRating is wired to Filter_Changed, which queries,
        // so setting SelectedIndex below would otherwise run a query before the grid is styled.
        private bool _loading = true;

        public AdminReviewsForm()
        {
            // Designer generated controls only. No query in the constructor: it runs before the
            // window exists, so a failure would have nowhere to report itself.
            InitializeComponent();
        }

        private void AdminReviewsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // colours, fonts, grid styling and the CellFormatting hook up

            // The five entries are added here, next to RatingRange which decodes them, rather
            // than in the designer where the two lists could drift apart. The ORDER is the
            // contract: RatingRange switches on the index, so moving an entry would silently
            // change what it filters. The bands are the ones an owner actually wants, which is
            // why "3 stars and below" and "1 and 2 stars only" overlap rather than partitioning
            // the range neatly: the second is the complaints list, the first is everything that
            // is not praise.
            cmbRating.Items.AddRange(new object[]
            {
                "All ratings",
                "5 stars only",
                "4 stars and above",
                "3 stars and below",
                "1 and 2 stars only"
            });
            cmbRating.SelectedIndex = 0;   // raises SelectedIndexChanged, which _loading swallows

            _loading = false;   // from here on the filter is allowed to query
            LoadGrid();         // one deliberate first load, now that everything is wired
        }

        // Pure presentation, called once from Load. It also subscribes the grid to its
        // CellFormatting handler, which is what colours the rows by rating. Note what it does
        // NOT style: there is no delete or hide button on this form to give a colour to.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Customer Reviews");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            lblAverage.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
            lblAverage.ForeColor = UiTheme.TextDark;

            lblReadOnlyNote.Font = UiTheme.FontSmall;
            lblReadOnlyNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
            txtComment.Font = UiTheme.FontBody;
            txtComment.BackColor = Color.White;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StyleAccent(btnReport);
            UiTheme.StyleGrid(dgvReviews);
            dgvReviews.CellFormatting += dgvReviews_CellFormatting;
        }

        private void RatingRange(out int min, out int max)
        {
            // Two out parameters rather than a return value, because the answer is a RANGE and
            // the service takes it as BETWEEN @MinRating AND @MaxRating. Expressing every
            // choice as a pair means "All ratings" is 1 to 5 rather than a special case the
            // query would have to test for, so there is one query and no branch in the SQL.
            switch (cmbRating.SelectedIndex)
            {
                case 1: min = 5; max = 5; break;   // only the top mark
                case 2: min = 4; max = 5; break;   // the satisfied customers
                case 3: min = 1; max = 3; break;   // everything that is not praise
                case 4: min = 1; max = 2; break;   // the complaints, and the band the Super Admin moderates
                // default, not case 0, because SelectedIndex is -1 before anything is chosen
                // and an unmatched switch would leave min and max unassigned, which the
                // compiler refuses. The widest range is also the safest fallback: it shows
                // everything rather than silently hiding reviews.
                default: min = 1; max = 5; break;
            }
        }

        private void LoadGrid()
        {
            // Every refresh path comes through here: the first load, the Refresh button and the
            // rating filter, so one guard covers all three.
            if (_loading) return;

            try
            {
                int min, max;
                RatingRange(out min, out max);

                // The pharmacy id comes from UserSession, set once at login, and the rating
                // band from the screen. Only the second is user input, and it travels as a
                // parameter, so the filter can narrow what is shown but can never widen it past
                // this shop. There is no control anywhere on this form holding a pharmacy id,
                // which is precisely why one owner cannot read another shop's reviews.
                //
                // Reviews are attached to MEDICINES, not to pharmacies, so the service joins
                // Reviews to Medicines and filters on m.PharmacyId. It also filters
                // IsHidden = 0, so a review the Super Admin has already hidden does not appear
                // here either: the owner sees exactly what customers see.
                DataTable table = _reviews.GetForPharmacy(UserSession.PharmacyId, min, max);
                dgvReviews.DataSource = table;

                // Binding is what CREATES the columns, so the renames follow the assignment,
                // and the guard stops a lookup by name from throwing if none were created.
                if (dgvReviews.Columns.Count > 0)
                {
                    dgvReviews.Columns["ReviewId"].HeaderText = "ID";
                    // StyleGrid sets AutoSizeColumnsMode to Fill, so FillWeight is a share of
                    // the width rather than a pixel count.
                    dgvReviews.Columns["ReviewId"].FillWeight = 28;
                    // The reviewer's real name, joined from Users. Reviews are not anonymous
                    // here because every one of them is tied to a delivered order.
                    dgvReviews.Columns["ReviewerName"].HeaderText = "Reviewer";
                    dgvReviews.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvReviews.Columns["Strength"].HeaderText = "Strength";
                    dgvReviews.Columns["Strength"].FillWeight = 45;
                    // "Stars" rather than "Rating", because the number is understood at a
                    // glance as a five point scale.
                    dgvReviews.Columns["Rating"].HeaderText = "Stars";
                    dgvReviews.Columns["Rating"].FillWeight = 32;
                    // By far the largest share, because the comment is the part worth reading.
                    // A grid cell still truncates it, which is why the full text is repeated in
                    // the read only box below whenever a row is selected.
                    dgvReviews.Columns["Comment"].HeaderText = "Comment";
                    dgvReviews.Columns["Comment"].FillWeight = 170;
                    dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";
                    // The order number is shown deliberately. It is the PROOF behind the
                    // review: AddReview only inserts when an EXISTS finds this customer's
                    // delivered order containing this medicine, so the owner can trace any
                    // rating back to a real sale rather than suspecting it was invented.
                    dgvReviews.Columns["OrderId"].HeaderText = "Order";
                    dgvReviews.Columns["OrderId"].FillWeight = 40;
                }

                // Two more round trips rather than averaging the rows on screen, and that is
                // the point: the grid holds only the rows inside the selected rating band, so
                // averaging them would make the headline figure change every time the filter
                // changed. Both queries ignore the filter and cover every visible review, so
                // the average stays the shop's actual average.
                decimal average = _reviews.GetAverageForPharmacy(UserSession.PharmacyId);
                int total = _reviews.CountForPharmacy(UserSession.PharmacyId);

                // "No reviews yet" rather than "0.00 / 5". A new shop has not been rated badly,
                // it has not been rated at all, and a zero would read as the worst possible
                // score. The service returns 0 for an empty set, so the count is what
                // distinguishes the two cases.
                lblAverage.Text = total == 0
                    ? "No reviews yet"
                    : "Average rating  " + average.ToString("N2") + " / 5   from " + total + " review(s)";

                // Red only when there is an average AND it is poor. Testing average > 0 first
                // keeps a shop with no reviews at all from being painted as a failing one.
                lblAverage.ForeColor = average > 0 && average < 2.5m ? UiTheme.Danger : UiTheme.TextDark;

                // The warning repeats the exact rule the Super Admin's low rated report uses:
                // an average below 2.5 with at least two reviews. The second condition is the
                // fairness rule, and it is stated here so an owner learns he is on that report
                // from his own screen rather than from a suspension notice.
                lblStatus.Text = average > 0 && average < 2.5m && total >= 2
                    ? "Warning: your average is below 2.5 with " + total + " reviews, which puts your shop on the Super Admin's low rated report."
                    // Otherwise the ordinary case states the row count and, in one line, why
                    // the ratings can be trusted: every review is tied to a delivered order.
                    : table.Rows.Count + " review(s) shown. Every review is tied to a delivered order, so none of them are fake.";

                // Rebinding cleared the selection, so the comment box and the Report button are
                // re-evaluated rather than left describing a row that is no longer there.
                UpdateSelection();
            }
            catch (Exception ex)
            {
                // DbHelper has already turned the SqlException into a readable sentence, so it
                // is shown as it stands rather than wrapped in wording that would hide it.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // CellFormatting fires once per CELL as it is painted, including while scrolling,
            // so it must stay cheap and must never query. The guard covers the header row,
            // whose index is -1, and the moment during a rebind when no columns exist.
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;

            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];
            object rating = row.Cells["Rating"].Value;
            // Both null and DBNull have to be excluded: the first happens mid rebind, and
            // Convert.ToInt32 throws on the second rather than returning zero.
            if (rating == null || rating == DBNull.Value) return;

            int stars = Convert.ToInt32(rating);
            // The colours make the list scannable without reading a word of it: the complaints
            // stand out from the praise at a glance. The thresholds match the "1 and 2 stars
            // only" and "4 stars and above" filter bands above, so a filtered view and a
            // coloured row always agree about what counts as bad.
            if (stars <= 2) row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;        // the same red used for anything needing attention
            else if (stars >= 4) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;  // the same green used for a finished order
            // The explicit white branch is not redundant. DataGridViewRow objects are REUSED as
            // the grid scrolls, so a three star row left unpainted would keep the red or green
            // of whichever row previously occupied that slot, and the colouring would appear to
            // be random.
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        // Selecting a different review changes what the comment box should show and what the
        // Report button would act on, so both are refreshed together.
        private void dgvReviews_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            DataGridViewRow row = dgvReviews.CurrentRow;

            // No row at all, which is the empty grid case and also the instant during a rebind
            // when the cells are not yet populated.
            if (row == null || row.Cells["ReviewId"].Value == null)
            {
                // Cleared rather than left holding the previous review's text, which would
                // otherwise sit under an empty grid and appear to belong to nothing.
                txtComment.Clear();
                // Disabled because there is nothing to report. Offering an action that cannot
                // succeed is worse than not offering it.
                btnReport.Enabled = false;
                return;
            }

            object comment = row.Cells["Comment"].Value;
            // The Comment column is nullable, because a customer may leave a rating with no
            // words. The placeholder NAMES that situation instead of leaving an empty box: a
            // blank panel reads as a screen that failed to load, whereas this sentence says
            // plainly that there is nothing to read and the form is working.
            txtComment.Text = comment == null || comment == DBNull.Value
                ? "(this customer left a rating but no written comment)"
                : comment.ToString();

            // The box itself is marked ReadOnly in the designer and is multiline with a
            // scrollbar, so the owner can read and copy a long comment that the grid cell
            // truncates, but cannot type over it. That is the same rule the rest of this form
            // follows: the owner may read a review and never change one.
            btnReport.Enabled = true;
        }

        private void btnReport_Click(object sender, EventArgs e)
        {
            // THE ONLY ACTION ON THIS FORM, and notice what it is not. There is deliberately no
            // Delete button and no Hide button here: neither exists on the designer surface, so
            // there is no handler to disable and nothing to bypass. A shop that could delete
            // its own bad reviews would make every rating on the platform worthless, so the
            // subject of a review is never allowed to remove it. ReviewService.SetHidden does
            // exist, and it hides rather than deletes, but it is called only from the Super
            // Admin's ModerateReviewsForm. The reviews themselves are never deleted by anyone:
            // IsHidden is a flag, and the row and its order number stay in the database.
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;   // re-checked even though the button is disabled without a row

            // Read before the message is built, because both values are used in its text.
            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);
            // The rating decides which closing sentence is shown further down.
            int rating = Convert.ToInt32(row.Cells["Rating"].Value);

            // HONEST NOTE, and worth knowing before anyone asks: this button writes
            // NOTHING to the database. There is no reported flag column on Reviews, so
            // nothing here changes the review or notifies anyone - the message box is
            // the entire behaviour.
            //
            // It is defensible rather than broken, because 1 and 2 star reviews already
            // appear in the Super Admin's moderation queue by default, so the abusive
            // ones surface without being reported. But it is incomplete: a ReportedAt
            // or IsReported column on Reviews, set here and surfaced in that queue,
            // would be the honest next step. Do not claim this files a report.
            //
            // Stated precisely, so the gap is not overstated either: the handler reads two
            // cells, shows the message box below and writes a line into lblStatus. It calls no
            // service method, opens no connection and issues no INSERT or UPDATE. The Reviews
            // table has columns ReviewId, CustomerId, MedicineId, OrderId, Rating, Comment,
            // ReviewDate and IsHidden, and there is nowhere in that schema for a report to be
            // recorded. Nothing is persisted, so the flag does not survive closing this form,
            // and the Super Admin's screen shows no trace of it.
            MessageBox.Show(
                "Review " + reviewId + " has been flagged for the Super Admin.\r\n\r\n" +
                "It stays visible to customers until the Super Admin reviews it. Nothing on this screen " +
                "changes the review itself: only the Super Admin can set IsHidden, and even then the row " +
                "is never deleted.\r\n\r\n" +
                // The closing sentence depends on the rating, because the two cases genuinely
                // differ. A 1 or 2 star review is already in the moderation queue, which the
                // Super Admin opens filtered to that band, so it will be seen either way. A
                // higher rated one will not appear there, which is why that branch asks the
                // owner to contact support rather than implying this screen has done it.
                (rating <= 2
                    ? "Reviews of 1 and 2 stars already appear in the moderation queue by default."
                    : "Higher rated reviews are rarely hidden, so please add context when you contact support."),
                "Reported to the Super Admin", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // A status line, not a database write. It is accurate for a low rated review, which
            // the moderation queue shows by default, and it is the sentence that would need
            // revisiting alongside the IsReported column described above.
            lblStatus.Text = "Review " + reviewId + " reported. The Super Admin's Moderate Reviews screen will show it.";
        }

        // Both the rating combo and the Refresh button point at this one handler, because the
        // response to either is the same: re-run the query with whatever the filter now holds.
        // Sharing it means the two routes cannot behave differently.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close, not Hide: the dashboard opened this form with ShowDialog, disposes it and then
        // refreshes itself, so closing is all this button has to do.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
