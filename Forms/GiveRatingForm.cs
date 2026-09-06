using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 28. The modal opened by Rate and Review on the order history.
    ///
    /// Only the medicines on this delivered order that have not been reviewed
    /// yet appear in the dropdown, a rating must be chosen before Submit is
    /// enabled, and the comment is capped at 500 characters to match the
    /// NVARCHAR(500) column.
    /// </summary>
    public partial class GiveRatingForm : Form
    {
        private readonly ReviewService _reviews = new ReviewService();
        private readonly int _orderId;
        private int _rating;

        public GiveRatingForm(int orderId)
        {
            InitializeComponent();
            _orderId = orderId;
        }

        private void GiveRatingForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadReviewableItems();
            UpdateCharCount();
            ValidateAll();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Rate and Review");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);
            lblSubtitle.Text = "Order #" + _orderId + "  -  only medicines you actually received can be rated.";

            lblRatingCaption.Font = UiTheme.FontHeading;
            lblRatingCaption.ForeColor = UiTheme.TextDark;
            lblRatingWord.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
            lblRatingWord.ForeColor = UiTheme.TextMuted;

            foreach (Label label in new[] { lblMedicineError, lblRatingError })
            {
                label.Font = UiTheme.FontSmall;
                label.ForeColor = UiTheme.Danger;
            }

            lblCharCount.Font = UiTheme.FontSmall;
            lblCharCount.ForeColor = UiTheme.TextMuted;
            lblRuleNote.Font = UiTheme.FontSmall;
            lblRuleNote.ForeColor = UiTheme.TextMuted;

            foreach (Button star in StarButtons())
            {
                UiTheme.StyleSecondary(star);
                star.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
            }

            UiTheme.StyleSuccess(btnSubmit);
            btnSubmit.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnCancel);
        }

        private Button[] StarButtons()
        {
            return new[] { btnStar1, btnStar2, btnStar3, btnStar4, btnStar5 };
        }

        private void LoadReviewableItems()
        {
            DataTable table = _reviews.GetReviewableItems(_orderId, UserSession.UserId);

            cmbMedicine.Items.Clear();
            cmbMedicine.Items.Add("- choose a medicine from this order -");

            foreach (DataRow row in table.Rows)
            {
                cmbMedicine.Items.Add(row["MedicineId"] + " - " + row["MedicineName"] + " " + row["Strength"]);
            }

            cmbMedicine.SelectedIndex = 0;

            if (table.Rows.Count == 0)
            {
                UiTheme.ShowError(lblMedicineError, cmbMedicine,
                    "Everything on this order has already been reviewed, or the order has not been delivered yet.");
                cmbMedicine.Enabled = false;
            }
            else if (table.Rows.Count == 1)
            {
                cmbMedicine.SelectedIndex = 1;   // only one thing to review, so pick it
            }
        }

        private int SelectedMedicineId()
        {
            if (cmbMedicine.SelectedIndex <= 0) return 0;
            string text = cmbMedicine.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------
        //  STAR SELECTOR
        // ---------------------------------------------------------------------

        private void Star_Click(object sender, EventArgs e)
        {
            Button clicked = (Button)sender;
            _rating = int.Parse(clicked.Tag.ToString());
            PaintStars();
            ValidateAll();
        }

        private void PaintStars()
        {
            foreach (Button star in StarButtons())
            {
                int value = int.Parse(star.Tag.ToString());

                if (value <= _rating)
                {
                    star.BackColor = value <= 2 ? UiTheme.Danger
                                   : value == 3 ? UiTheme.Warning
                                                : UiTheme.Success;
                    star.ForeColor = Color.White;
                }
                else
                {
                    star.BackColor = Color.White;
                    star.ForeColor = UiTheme.TextMuted;
                }
            }

            switch (_rating)
            {
                case 1: lblRatingWord.Text = "1 star  -  very poor"; lblRatingWord.ForeColor = UiTheme.Danger; break;
                case 2: lblRatingWord.Text = "2 stars  -  poor"; lblRatingWord.ForeColor = UiTheme.Danger; break;
                case 3: lblRatingWord.Text = "3 stars  -  acceptable"; lblRatingWord.ForeColor = UiTheme.Warning; break;
                case 4: lblRatingWord.Text = "4 stars  -  good"; lblRatingWord.ForeColor = UiTheme.Success; break;
                case 5: lblRatingWord.Text = "5 stars  -  excellent"; lblRatingWord.ForeColor = UiTheme.Success; break;
                default: lblRatingWord.Text = "Choose a rating"; lblRatingWord.ForeColor = UiTheme.TextMuted; break;
            }
        }

        // ---------------------------------------------------------------------

        private void txtComment_TextChanged(object sender, EventArgs e) => UpdateCharCount();

        private void UpdateCharCount()
        {
            int remaining = 500 - txtComment.Text.Length;
            lblCharCount.Text = remaining + " character(s) left";
            lblCharCount.ForeColor = remaining < 40 ? UiTheme.Warning : UiTheme.TextMuted;
        }

        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        private bool ValidateAll()
        {
            bool medicineChosen = SelectedMedicineId() > 0;
            bool ratingChosen = _rating >= 1 && _rating <= 5;

            if (medicineChosen) UiTheme.ClearError(lblMedicineError, cmbMedicine);

            if (!ratingChosen && medicineChosen)
                UiTheme.ShowError(lblRatingError, null, "Choose a rating from 1 to 5 before submitting.");
            else
                UiTheme.ClearError(lblRatingError, null);

            bool ok = medicineChosen && ratingChosen;
            btnSubmit.Enabled = ok;
            btnSubmit.BackColor = ok ? UiTheme.Success : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            if (!ValidateAll()) return;

            string message;
            if (_reviews.AddReview(UserSession.UserId, SelectedMedicineId(), _orderId,
                                   _rating, txtComment.Text.Trim(), out message))
            {
                MessageBox.Show(message, "Review posted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show(message, "Review not posted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LoadReviewableItems();
                ValidateAll();
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
