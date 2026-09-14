using System.Data;                  // DataTable and DataRow, the shape ReviewService returns
using System.Drawing;               // Color and Font, used to fill and empty the star buttons
using System.Windows.Forms;         // Form, Button, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the one place colours and errors are defined
using PharmaLinkApp.Services;       // ReviewService, which owns every rating query

// This dialog decides what to SHOW; the service decides what may be WRITTEN.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 28: the Rate and Review modal for one order.</summary>
    public partial class GiveRatingForm : Form
    {
        // One service for the life of the dialog; it holds no connection of its own.
        private readonly ReviewService _reviews = new ReviewService();

        // readonly, so a review stays pinned to the order the dialog was opened for.
        private readonly int _orderId;

        // The chosen star count; 0 is an unambiguous 'nothing chosen yet'.
        private int _rating;

        // The order id is required, so an unanchored dialog cannot be created.
        public GiveRatingForm(int orderId)
        {
            // Runs first, because everything below touches controls it creates.
            InitializeComponent();

            // Passed in rather than picked here, so the dialog always knows its order.
            _orderId = orderId;
        }

        // Runs once the window exists, so a failure can be shown and the dialog closed.
        private void GiveRatingForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // fonts and colours only, so it is safe before any data arrives

            // Wrapped because an unhandled exception on Load takes the application down.
            try
            {
                LoadReviewableItems();   // fill the dropdown with what may still be rated
                UpdateCharCount();       // show the remaining characters before anything is typed
                ValidateAll();           // leaves Submit disabled, because no rating exists yet
            }
            // Caught here, this becomes a message and a closed dialog, not a crash.
            catch (Exception ex)
            {
                // The cause is appended, so there is something to act on.
                MessageBox.Show("The medicines on this order could not be loaded.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // Error: the read itself failed

                // With no list the dialog cannot work, so it closes rather than offering Submit.
                Close();
            }
        }

        // Presentation only, so a change of appearance cannot alter what the form does.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Rate and Review");       // shared window chrome and caption
            StartPosition = FormStartPosition.CenterParent;   // centred on the order history

            panelHeader.BackColor = UiTheme.Primary;          // the brand green band every screen wears
            lblTitle.Font = UiTheme.FontTitle;                // one shared heading font across the project
            lblTitle.ForeColor = Color.White;                 // the only ink legible on that green
            lblSubtitle.Font = UiTheme.FontSmall;             // smaller: it qualifies the title
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // a pale tint, so it recedes
            // Names the order AND the rule, so a missing medicine is explained.
            lblSubtitle.Text = "Order #" + _orderId + "  -  only medicines you actually received can be rated.";

            lblRatingCaption.Font = UiTheme.FontHeading;      // the stars are the main question
            lblRatingCaption.ForeColor = UiTheme.TextDark;    // full strength ink, not an aside
            lblRatingWord.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);   // large: it confirms the answer
            lblRatingWord.ForeColor = UiTheme.TextMuted;      // muted until PaintStars recolours it

            // One loop, so a new rule cannot be styled as ordinary text by accident.
            foreach (Label label in new[] { lblMedicineError, lblRatingError })
            {
                label.Font = UiTheme.FontSmall;               // small, because it sits beneath its field
                label.ForeColor = UiTheme.Danger;             // red is what makes it read as a problem
            }

            lblCharCount.Font = UiTheme.FontSmall;            // the live counter under the comment box
            lblCharCount.ForeColor = UiTheme.TextMuted;       // muted until UpdateCharCount turns it amber
            lblRuleNote.Font = UiTheme.FontSmall;             // the fixed line about the delivered rule
            lblRuleNote.ForeColor = UiTheme.TextMuted;        // quiet, because it never changes

            // Every star is styled alike; only PaintStars distinguishes them, by fill.
            foreach (Button star in StarButtons())
            {
                UiTheme.StyleSecondary(star);                 // outlined to begin with: the empty state
                star.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);   // the largest type on the dialog
            }

            UiTheme.StyleSuccess(btnSubmit);                  // green: posting a review is encouraged
            btnSubmit.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);   // heavier, so it carries weight
            UiTheme.StyleSecondary(btnCancel);                // outlined, so leaving never competes
        }

        // The five stars in one place, so theming and repainting can both loop.
        private Button[] StarButtons()
        {
            // Listed once, so adding or removing a star is a single edit.
            return new[] { btnStar1, btnStar2, btnStar3, btnStar4, btnStar5 };
        }

        // Rebuilt from the database, so the service's conditions decide what appears.
        private void LoadReviewableItems()
        {
            // This order, this customer, status 'Delivered', and not already reviewed.
            DataTable table = _reviews.GetReviewableItems(_orderId, UserSession.UserId);

            // Cleared first, or a rebuild after a refusal would list everything twice.
            cmbMedicine.Items.Clear();

            // A prompt at index 0, so 'nothing chosen' is a visible state.
            cmbMedicine.Items.Add("- choose a medicine from this order -");

            // One entry per line the service allowed, in its own alphabetical order.
            foreach (DataRow row in table.Rows)
            {
                // The id leads the text, because a string ComboBox carries no value column.
                cmbMedicine.Items.Add(row["MedicineId"] + " - " + row["MedicineName"] + " " + row["Strength"]);
            }

            cmbMedicine.SelectedIndex = 0;   // land on the prompt, so nothing is chosen by accident

            // An empty result is a state of the screen, not an exception.
            if (table.Rows.Count == 0)
            {
                // Both causes are named, because one empty set is produced by either.
                UiTheme.ShowError(lblMedicineError, cmbMedicine,
                    "Everything on this order has already been reviewed, or the order has not been delivered yet.");   // named, not guessed

                // Disabled, so the control says there is nothing to choose.
                cmbMedicine.Enabled = false;
            }
            // Exactly one candidate, so choosing it removes a step without guessing.
            else if (table.Rows.Count == 1)
            {
                cmbMedicine.SelectedIndex = 1;   // only one thing to review, so pick it
            }
        }

        // Reads the medicine id back out of the selected text, or 0 for 'none'.
        private int SelectedMedicineId()
        {
            // Index 0 is the prompt and -1 is no selection, so 0 means no medicine.
            if (cmbMedicine.SelectedIndex <= 0) return 0;

            // SelectedItem, not Text: the control is not editable, so this is our string.
            string text = cmbMedicine.SelectedItem.ToString();

            // IndexOf, not Split: the name and strength contain spaces of their own.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // STAR SELECTOR

        // Wired to all five stars, so it starts by working out which was pressed.
        private void Star_Click(object sender, EventArgs e)
        {
            // One handler for five buttons; sender says which one.
            Button clicked = (Button)sender;
            // Tag carries the value 1 to 5, set in the designer, not read off the name.
            _rating = int.Parse(clicked.Tag.ToString());

            PaintStars();    // repaint all five so 1..n appear filled and the rest empty
            ValidateAll();   // Submit stays disabled until a rating AND a medicine exist
        }

        // Repaints the whole row, which is what lets a rating go down as well as up.
        private void PaintStars()
        {
            // The same array the theming pass used, so no star is missed by one loop.
            foreach (Button star in StarButtons())
            {
                // Tag again, so the loop never assumes the array order.
                int value = int.Parse(star.Tag.ToString());

                // At or below the score is filled; above it is cleared by the else.
                if (value <= _rating)
                {
                    // The colour follows the SCORE, so all five stars agree on one verdict.
                    star.BackColor = value <= 2 ? UiTheme.Danger
                                   : value == 3 ? UiTheme.Warning   // the middle is not a complaint
                                                : UiTheme.Success;   // 4 and 5, the only scores read as praise
                    star.ForeColor = Color.White;   // white keeps the glyph readable on all three fills
                }
                // Stars above the score have to be actively cleared, not left alone.
                else
                {
                    star.BackColor = Color.White;      // back to the empty state
                    star.ForeColor = UiTheme.TextMuted;   // grey, so an empty star still reads as a star
                }
            }

            // The word beside the stars, because a bare 3 could be poor or fair.
            switch (_rating)
            {
                case 1: lblRatingWord.Text = "1 star  -  very poor"; lblRatingWord.ForeColor = UiTheme.Danger; break;    // singular, one star is one star
                case 2: lblRatingWord.Text = "2 stars  -  poor"; lblRatingWord.ForeColor = UiTheme.Danger; break;        // still red, matching the stars
                case 3: lblRatingWord.Text = "3 stars  -  acceptable"; lblRatingWord.ForeColor = UiTheme.Warning; break; // amber: neither complaint nor praise
                case 4: lblRatingWord.Text = "4 stars  -  good"; lblRatingWord.ForeColor = UiTheme.Success; break;       // green begins where the stars do
                case 5: lblRatingWord.Text = "5 stars  -  excellent"; lblRatingWord.ForeColor = UiTheme.Success; break;  // the top of CK_Reviews_Rating
                // 0 is the only value left, so this is the nothing-chosen state.
                default: lblRatingWord.Text = "Choose a rating"; lblRatingWord.ForeColor = UiTheme.TextMuted; break;
            }
        }

        // TextChanged, not Leave, so the counter moves while the customer types.
        private void txtComment_TextChanged(object sender, EventArgs e) => UpdateCharCount();

        // Reports the limit; the box's own MaxLength is what enforces it.
        private void UpdateCharCount()
        {
            // 500 is the width of Reviews.Comment, which is NVARCHAR(500).
            int remaining = 500 - txtComment.Text.Length;
            // "character(s)", because a grammar branch would add no information.
            lblCharCount.Text = remaining + " character(s) left";

            // Amber for the last 40, while there is still room to shorten a sentence.
            lblCharCount.ForeColor = remaining < 40 ? UiTheme.Warning : UiTheme.TextMuted;
        }

        // One handler for every control whose change can affect the Submit button.
        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        // Paints the errors, sets the button and returns the verdict for reuse.
        private bool ValidateAll()
        {
            bool medicineChosen = SelectedMedicineId() > 0;     // 0 means the prompt is still selected

            // A RANGE, not just 'not zero': the same window as CK_Reviews_Rating.
            bool ratingChosen = _rating >= 1 && _rating <= 5;

            // Cleared only on a choice, so the 'already reviewed' message survives.
            if (medicineChosen) UiTheme.ClearError(lblMedicineError, cmbMedicine);

            // The rating complaint waits for a medicine, so one problem is raised at a time.
            if (!ratingChosen && medicineChosen)
                // null as the field: a rating is five buttons, not one box to tint red.
                UiTheme.ShowError(lblRatingError, null, "Choose a rating from 1 to 5 before submitting.");
            // Covers both the satisfied case and the not-yet-asked one.
            else
                UiTheme.ClearError(lblRatingError, null);   // the same null field, so only the label moves

            bool ok = medicineChosen && ratingChosen;   // both halves, since a review needs both
            btnSubmit.Enabled = ok;   // the real block: an incomplete review cannot be sent

            // Set alongside Enabled, or a custom BackColor still looks pressable.
            btnSubmit.BackColor = ok ? UiTheme.Success : Color.FromArgb(170, 190, 184);

            // Returned as well as applied, so the click handler can run the same gate.
            return ok;
        }

        // The one write here, and the service re-checks everything it is sent.
        private void btnSubmit_Click(object sender, EventArgs e)
        {
            // Re-checked, because the Enter key can reach this handler regardless.
            if (!ValidateAll()) return;

            // Wrapped, so a genuine fault does not close the dialog and lose the comment.
            try
            {
                // AddReview reports through out, so the form never words a refusal itself.
                string message;

                // UQ_Reviews_OneEach, not this dropdown, is what stops a second review.
                if (_reviews.AddReview(UserSession.UserId, SelectedMedicineId(), _orderId,   // the id comes from the session
                                       _rating, txtComment.Text.Trim(), out message))   // true only when a row was inserted
                {
                    // Trimmed above, so a box of spaces is stored as an empty string.
                    MessageBox.Show(message, "Review posted", MessageBoxButtons.OK, MessageBoxIcon.Information);   // the service's own words

                    // OK tells the order history to reload and retire its Rate button.
                    DialogResult = DialogResult.OK;
                    Close();   // the review is stored, so there is nothing left to do here
                }
                // A false return is the rule working, not anything failing.
                else
                {
                    // Warning, not Error: the application is behaving exactly as intended.
                    MessageBox.Show(message, "Review not posted", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    // Rebuilt, so a duplicate simply disappears from the dropdown.
                    LoadReviewableItems();
                    ValidateAll();   // re-run the gate against the rebuilt list
                }
            }
            // Only a real fault lands here; a refused review arrives as false above.
            catch (Exception ex)
            {
                // The dialog stays open, so the typed comment survives the failure.
                MessageBox.Show("The review could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // a genuine fault, so Error
            }
        }

        // Leaves without writing, but sets a result the caller can tell apart.
        private void btnCancel_Click(object sender, EventArgs e)
        {
            // Explicit, so the order history knows to skip the reload.
            DialogResult = DialogResult.Cancel;

            // Belt and braces: assigning DialogResult already closes a modal form.
            Close();
        }
    }
}
