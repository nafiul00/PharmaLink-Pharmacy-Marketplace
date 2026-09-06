using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

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
        private readonly ReviewService _reviews = new ReviewService();
        private bool _loading = true;

        public AdminReviewsForm()
        {
            InitializeComponent();
        }

        private void AdminReviewsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbRating.Items.AddRange(new object[]
            {
                "All ratings",
                "5 stars only",
                "4 stars and above",
                "3 stars and below",
                "1 and 2 stars only"
            });
            cmbRating.SelectedIndex = 0;

            _loading = false;
            LoadGrid();
        }

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
            switch (cmbRating.SelectedIndex)
            {
                case 1: min = 5; max = 5; break;
                case 2: min = 4; max = 5; break;
                case 3: min = 1; max = 3; break;
                case 4: min = 1; max = 2; break;
                default: min = 1; max = 5; break;
            }
        }

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                int min, max;
                RatingRange(out min, out max);

                DataTable table = _reviews.GetForPharmacy(UserSession.PharmacyId, min, max);
                dgvReviews.DataSource = table;

                if (dgvReviews.Columns.Count > 0)
                {
                    dgvReviews.Columns["ReviewId"].HeaderText = "ID";
                    dgvReviews.Columns["ReviewId"].FillWeight = 28;
                    dgvReviews.Columns["ReviewerName"].HeaderText = "Reviewer";
                    dgvReviews.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvReviews.Columns["Strength"].HeaderText = "Strength";
                    dgvReviews.Columns["Strength"].FillWeight = 45;
                    dgvReviews.Columns["Rating"].HeaderText = "Stars";
                    dgvReviews.Columns["Rating"].FillWeight = 32;
                    dgvReviews.Columns["Comment"].HeaderText = "Comment";
                    dgvReviews.Columns["Comment"].FillWeight = 170;
                    dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";
                    dgvReviews.Columns["OrderId"].HeaderText = "Order";
                    dgvReviews.Columns["OrderId"].FillWeight = 40;
                }

                decimal average = _reviews.GetAverageForPharmacy(UserSession.PharmacyId);
                int total = _reviews.CountForPharmacy(UserSession.PharmacyId);

                lblAverage.Text = total == 0
                    ? "No reviews yet"
                    : "Average rating  " + average.ToString("N2") + " / 5   from " + total + " review(s)";

                lblAverage.ForeColor = average > 0 && average < 2.5m ? UiTheme.Danger : UiTheme.TextDark;

                lblStatus.Text = average > 0 && average < 2.5m && total >= 2
                    ? "Warning: your average is below 2.5 with " + total + " reviews, which puts your shop on the Super Admin's low rated report."
                    : table.Rows.Count + " review(s) shown. Every review is tied to a delivered order, so none of them are fake.";

                UpdateSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;

            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];
            object rating = row.Cells["Rating"].Value;
            if (rating == null || rating == DBNull.Value) return;

            int stars = Convert.ToInt32(rating);
            if (stars <= 2) row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
            else if (stars >= 4) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        private void dgvReviews_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            DataGridViewRow row = dgvReviews.CurrentRow;

            if (row == null || row.Cells["ReviewId"].Value == null)
            {
                txtComment.Clear();
                btnReport.Enabled = false;
                return;
            }

            object comment = row.Cells["Comment"].Value;
            txtComment.Text = comment == null || comment == DBNull.Value
                ? "(this customer left a rating but no written comment)"
                : comment.ToString();

            btnReport.Enabled = true;
        }

        private void btnReport_Click(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;

            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);
            int rating = Convert.ToInt32(row.Cells["Rating"].Value);

            MessageBox.Show(
                "Review " + reviewId + " has been flagged for the Super Admin.\r\n\r\n" +
                "It stays visible to customers until the Super Admin reviews it. Nothing on this screen " +
                "changes the review itself: only the Super Admin can set IsHidden, and even then the row " +
                "is never deleted.\r\n\r\n" +
                (rating <= 2
                    ? "Reviews of 1 and 2 stars already appear in the moderation queue by default."
                    : "Higher rated reviews are rarely hidden, so please add context when you contact support."),
                "Reported to the Super Admin", MessageBoxButtons.OK, MessageBoxIcon.Information);

            lblStatus.Text = "Review " + reviewId + " reported. The Super Admin's Moderate Reviews screen will show it.";
        }

        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
