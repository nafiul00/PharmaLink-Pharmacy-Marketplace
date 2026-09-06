using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
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
        private bool _loading = true;

        public ModerateReviewsForm()
        {
            InitializeComponent();
        }

        private void ModerateReviewsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbRating.Items.AddRange(new object[]
            {
                "1 and 2 stars only  (default)",
                "3 stars and below",
                "All ratings"
            });
            cmbRating.SelectedIndex = 0;

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
            switch (cmbRating.SelectedIndex)
            {
                case 0: return 2;
                case 1: return 3;
                default: return 5;
            }
        }

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                DataTable table = _reviews.GetModerationQueue(MaxRating(), chkIncludeHidden.Checked);
                dgvReviews.DataSource = table;

                if (dgvReviews.Columns.Count > 0)
                {
                    dgvReviews.Columns["ReviewId"].HeaderText = "ID";
                    dgvReviews.Columns["ReviewId"].FillWeight = 30;
                    dgvReviews.Columns["Reviewer"].HeaderText = "Reviewer";
                    dgvReviews.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvReviews.Columns["PharmacyName"].HeaderText = "Pharmacy";
                    dgvReviews.Columns["Rating"].HeaderText = "Stars";
                    dgvReviews.Columns["Rating"].FillWeight = 32;
                    dgvReviews.Columns["Comment"].HeaderText = "Comment";
                    dgvReviews.Columns["Comment"].FillWeight = 160;
                    dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";
                    dgvReviews.Columns["OrderId"].HeaderText = "Order";
                    dgvReviews.Columns["OrderId"].FillWeight = 40;
                    dgvReviews.Columns["IsHidden"].HeaderText = "Hidden";
                    dgvReviews.Columns["IsHidden"].FillWeight = 40;
                }

                lblStatus.Text = table.Rows.Count + " review(s) in the queue.";
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
            object hidden = row.Cells["IsHidden"].Value;
            object rating = row.Cells["Rating"].Value;

            if (hidden != null && hidden != DBNull.Value && Convert.ToBoolean(hidden))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(238, 238, 238);
                row.DefaultCellStyle.ForeColor = UiTheme.TextMuted;
            }
            else if (rating != null && rating != DBNull.Value && Convert.ToInt32(rating) <= 2)
            {
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = UiTheme.TextDark;
            }
        }

        private void dgvReviews_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            DataGridViewRow row = dgvReviews.CurrentRow;

            if (row == null || row.Cells["ReviewId"].Value == null)
            {
                txtComment.Clear();
                btnHide.Enabled = false;
                btnUnhide.Enabled = false;
                return;
            }

            object comment = row.Cells["Comment"].Value;
            txtComment.Text = comment == null || comment == DBNull.Value ? "(no written comment)" : comment.ToString();

            bool hidden = row.Cells["IsHidden"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsHidden"].Value);

            btnHide.Enabled = !hidden;
            btnUnhide.Enabled = hidden;
        }

        private void btnHide_Click(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;

            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);
            string pharmacy = row.Cells["PharmacyName"].Value.ToString();

            DialogResult answer = MessageBox.Show(
                "Hide this review from the customer screens?\r\n\r\n" +
                "IsHidden is set to 1. The row is not deleted, so " + pharmacy +
                "'s rating history stays complete and the decision can be reversed.",
                "Hide review", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            _reviews.SetHidden(reviewId, true);
            lblStatus.Text = "Review " + reviewId + " hidden. It no longer counts towards " + pharmacy + "'s average rating.";
            LoadGrid();
        }

        private void btnUnhide_Click(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvReviews.CurrentRow;
            if (row == null) return;

            int reviewId = Convert.ToInt32(row.Cells["ReviewId"].Value);
            _reviews.SetHidden(reviewId, false);
            lblStatus.Text = "Review " + reviewId + " restored and is visible to customers again.";
            LoadGrid();
        }

        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
