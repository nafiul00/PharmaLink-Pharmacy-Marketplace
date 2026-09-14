using System.Data;                  // DataTable and DataRow, the shape ReviewService hands back
using System.Drawing;               // Color and Font, used to fill and empty the star buttons
using System.Windows.Forms;         // Form, Button, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, the one place colours and error styling are defined
using PharmaLinkApp.Services;       // ReviewService, which owns every rating query

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
        // One service for the life of the dialog. It opens no connection of its own, so a
        // single field simply saves building a new instance in each of the three places
        // below that read from or write to the database.
        private readonly ReviewService _reviews = new ReviewService();

        // The order being rated, captured once in the constructor. readonly because a
        // review must stay pinned to the order it was opened for: if this could be
        // reassigned later, a rating could be written against a different purchase.
        private readonly int _orderId;

        // The chosen star count, 0 until a star is pressed. Held as a field rather than
        // read back off the buttons, so every other method has one number to test and
        // 0 is an unambiguous "nothing chosen yet".
        private int _rating;

        public GiveRatingForm(int orderId)
        {
            // Builds every control from the designer file. It has to run first, because
            // the assignment below and everything in the Load handler touch controls that
            // do not exist until this call returns.
            InitializeComponent();

            // The order number arrives as a constructor argument rather than being picked
            // inside this dialog, so there is exactly one way to open the form and it can
            // never be shown without knowing which order it is rating.
            _orderId = orderId;
        }

        private void GiveRatingForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // fonts and colours only, so it is safe to run before any data arrives

            // The database work is wrapped because it is the first thing that happens when
            // the dialog opens. An unhandled exception on a Load handler takes the whole
            // application down; caught here it becomes a message and a closed dialog, and
            // the order history behind it carries on.
            try
            {
                LoadReviewableItems();   // fill the dropdown with what may still be rated
                UpdateCharCount();       // show the remaining characters before anything is typed
                ValidateAll();           // leaves Submit disabled, because no rating exists yet
            }
            catch (Exception ex)
            {
                // ex.Message is appended rather than swallowed, so the actual cause is on
                // screen instead of a bare "something went wrong" that leaves nothing to act on.
                MessageBox.Show("The medicines on this order could not be loaded.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Closing is deliberate. With no list of medicines the dialog cannot do its
                // job, and leaving it open would offer a Submit button that could only fail.
                Close();
            }
        }

        // Presentation only: fonts, colours and button styling, kept in its own method so
        // a change of appearance can never alter what the form does.
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
            // The five stars gathered in one place, in order, so ApplyTheme and PaintStars
            // can both loop instead of naming the buttons five times each. Listing them
            // once means adding or removing a star is a single edit rather than three.
            return new[] { btnStar1, btnStar2, btnStar3, btnStar4, btnStar5 };
        }

        private void LoadReviewableItems()
        {
            // The query behind this call is what decides what may be rated: the medicines
            // on THIS order, for THIS customer, only when the order status is 'Delivered',
            // and only those with no matching row in Reviews yet. The form does none of
            // that filtering itself, which is why the dropdown cannot offer an item the
            // database would go on to refuse.
            DataTable table = _reviews.GetReviewableItems(_orderId, UserSession.UserId);

            // Cleared before filling. Without this, the rebuild after a refused submit
            // would append a second copy of every medicine to the ones already listed.
            cmbMedicine.Items.Clear();

            // A prompt at index 0 that is not a medicine. It makes "nothing chosen" a
            // visible state instead of silently pre-selecting the first row, which is how
            // a customer ends up rating an item they never meant to.
            cmbMedicine.Items.Add("- choose a medicine from this order -");

            foreach (DataRow row in table.Rows)
            {
                // The id is put at the FRONT of the display text so SelectedMedicineId can
                // read it back off whatever the customer picks. A ComboBox filled with
                // plain strings carries no hidden value column, so the id has to travel
                // inside the text itself, and leading it keeps the parsing trivial.
                cmbMedicine.Items.Add(row["MedicineId"] + " - " + row["MedicineName"] + " " + row["Strength"]);
            }

            cmbMedicine.SelectedIndex = 0;   // land on the prompt, so nothing is chosen by accident

            if (table.Rows.Count == 0)
            {
                // Empty means one of exactly two things, and the message names both rather
                // than guessing: either every medicine here has already been rated by this
                // customer, or the order has not reached 'Delivered' and nothing on it can
                // be rated yet. Both are the same empty result set, so both are stated.
                UiTheme.ShowError(lblMedicineError, cmbMedicine,
                    "Everything on this order has already been reviewed, or the order has not been delivered yet.");

                // Disabled rather than left empty and clickable, so the control itself says
                // there is nothing to choose instead of inviting a click that does nothing.
                cmbMedicine.Enabled = false;
            }
            else if (table.Rows.Count == 1)
            {
                cmbMedicine.SelectedIndex = 1;   // only one thing to review, so pick it
            }
        }

        private int SelectedMedicineId()
        {
            // Index 0 is the prompt and -1 is no selection at all, so anything at or below
            // 0 means no medicine. Returning 0 rather than -1 keeps every call site to a
            // simple "> 0" test and matches how missing ids are signalled elsewhere here.
            if (cmbMedicine.SelectedIndex <= 0) return 0;

            string text = cmbMedicine.SelectedItem.ToString();

            // The item was built above as "17 - Napa 500mg", so everything before the first
            // space is the id. Substring with IndexOf is used rather than Split(' ') because
            // the medicine name and strength contain spaces of their own; taking only the
            // first field by position is unaffected by how many follow it.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------
        //  STAR SELECTOR
        // ---------------------------------------------------------------------

        private void Star_Click(object sender, EventArgs e)
        {
            // All five star buttons share THIS ONE handler. Casting sender tells us
            // which was pressed, and each button's Tag property carries its own value
            // (1 to 5), set in the designer. Without Tag this would need five almost
            // identical handlers, or a name-parsing hack.
            Button clicked = (Button)sender;
            _rating = int.Parse(clicked.Tag.ToString());

            PaintStars();    // repaint all five so 1..n appear filled and the rest empty
            ValidateAll();   // Submit stays disabled until a rating AND a medicine exist
        }

        private void PaintStars()
        {
            foreach (Button star in StarButtons())
            {
                // The value comes from the button's own Tag, so the loop never has to
                // assume the array is in order or that a button name ends in the right
                // digit. Tag is the single source for a star's meaning in both handlers.
                int value = int.Parse(star.Tag.ToString());

                if (value <= _rating)
                {
                    // Every star up to and including the chosen one is filled, and the
                    // colour follows the SCORE rather than the individual star: all five
                    // go red on a 1 and all five go green on a 5. That reads as one verdict
                    // rather than a gradient, which would suggest the stars mean different
                    // things to each other.
                    star.BackColor = value <= 2 ? UiTheme.Danger
                                   : value == 3 ? UiTheme.Warning
                                                : UiTheme.Success;
                    star.ForeColor = Color.White;   // white keeps the glyph readable on all three fills
                }
                else
                {
                    // Anything above the chosen rating is emptied again. Repainting the
                    // whole row on every click is what lets a customer drop from 5 to 2 and
                    // watch the top three clear; only ever filling stars would leave the
                    // display stuck at the highest value ever pressed.
                    star.BackColor = Color.White;
                    star.ForeColor = UiTheme.TextMuted;
                }
            }

            // The word beside the stars. A bare number leaves the customer guessing whether
            // 3 is poor or fair, so each score is named, and the colour of the word is
            // taken from the same three-way split as the stars so the two cannot disagree.
            switch (_rating)
            {
                case 1: lblRatingWord.Text = "1 star  -  very poor"; lblRatingWord.ForeColor = UiTheme.Danger; break;
                case 2: lblRatingWord.Text = "2 stars  -  poor"; lblRatingWord.ForeColor = UiTheme.Danger; break;
                case 3: lblRatingWord.Text = "3 stars  -  acceptable"; lblRatingWord.ForeColor = UiTheme.Warning; break;
                case 4: lblRatingWord.Text = "4 stars  -  good"; lblRatingWord.ForeColor = UiTheme.Success; break;
                case 5: lblRatingWord.Text = "5 stars  -  excellent"; lblRatingWord.ForeColor = UiTheme.Success; break;
                // 0 is the only value left, so default is the nothing-chosen state rather
                // than an unexpected one. It is reached when PaintStars runs before any
                // star has been pressed, and it restores the muted prompt.
                default: lblRatingWord.Text = "Choose a rating"; lblRatingWord.ForeColor = UiTheme.TextMuted; break;
            }
        }

        // ---------------------------------------------------------------------

        // Expression bodied because the handler does exactly one thing. It is wired to
        // TextChanged rather than to Leave, so the counter moves while the customer is
        // still typing instead of only once they tab away from the box.
        private void txtComment_TextChanged(object sender, EventArgs e) => UpdateCharCount();

        private void UpdateCharCount()
        {
            // 500 is the width of Reviews.Comment, which is NVARCHAR(500). The designer
            // also sets MaxLength to 500 on the box, so the limit is actually enforced by
            // the control; this counter exists so the ceiling is visible while the comment
            // is being written rather than discovered as silently refused keystrokes.
            int remaining = 500 - txtComment.Text.Length;
            lblCharCount.Text = remaining + " character(s) left";

            // Amber for the last 40 characters: a quiet warning while there is still room
            // to shorten a sentence, rather than a red error once the text has been cut off.
            lblCharCount.ForeColor = remaining < 40 ? UiTheme.Warning : UiTheme.TextMuted;
        }

        // Shared by every control whose change can affect whether Submit should be usable.
        // Re-validating on each change is what keeps the button honest without a timer or
        // a manual refresh, and one handler for all of them means the rule lives in one place.
        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        private bool ValidateAll()
        {
            bool medicineChosen = SelectedMedicineId() > 0;     // 0 means the prompt is still selected

            // Tested as a RANGE, not merely as "not zero". 1 to 5 is the same window as the
            // CK_Reviews_Rating check constraint on the table, so the form refuses exactly
            // what the database would refuse instead of sending a value that cannot be stored.
            bool ratingChosen = _rating >= 1 && _rating <= 5;

            // Cleared only when a medicine IS chosen, rather than unconditionally. That
            // leaves the "everything already reviewed" message from LoadReviewableItems on
            // screen, because it describes a state the customer cannot fix by choosing.
            if (medicineChosen) UiTheme.ClearError(lblMedicineError, cmbMedicine);

            // The rating complaint is held back until a medicine has been picked, so the
            // dialog raises one problem at a time in the order they are worked through
            // rather than showing two red lines the instant it opens.
            if (!ratingChosen && medicineChosen)
                // null is passed as the field to tint, because a rating is five buttons and
                // not one input box: there is nothing sensible to shade red, so only the
                // message label is used.
                UiTheme.ShowError(lblRatingError, null, "Choose a rating from 1 to 5 before submitting.");
            else
                UiTheme.ClearError(lblRatingError, null);

            bool ok = medicineChosen && ratingChosen;   // both halves, since a review needs both
            btnSubmit.Enabled = ok;

            // The colour is set alongside Enabled because a button with a custom BackColor
            // keeps that colour when disabled and would otherwise still look pressable.
            // The grey is the same one used for every other blocked action in the project.
            btnSubmit.BackColor = ok ? UiTheme.Success : Color.FromArgb(170, 190, 184);

            // Returned as well as applied, so the click handler can call this same method
            // as its final gate instead of trusting that the button was enabled correctly.
            return ok;
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            // Re-checked even though the button is disabled when the form is invalid. A
            // form with an AcceptButton can reach this handler from the Enter key, and a
            // fast second click can arrive after the state has changed, so the guard is
            // repeated rather than assumed from the button's appearance.
            if (!ValidateAll()) return;

            try
            {
                // Declared before the call because AddReview reports through an out
                // parameter: it returns true or false, and the words to show travel in
                // message. The form therefore never composes its own explanation of a
                // refusal, so the wording cannot drift from the rule that produced it.
                string message;

                // The customer id comes from the session, not from anything on screen, so
                // there is no control a customer could edit to post a review as somebody else.
                //
                // The service does not trust this form either. Its INSERT only writes when
                // an EXISTS proves the order belonged to this customer, contained this
                // medicine and reached 'Delivered'; and the UNIQUE constraint
                // UQ_Reviews_OneEach (CustomerId, MedicineId, OrderId) is what actually
                // prevents a second review of the same purchase. The dropdown here and the
                // Rate button on the order history only reflect that rule for the customer's
                // benefit. The constraint is what enforces it, which is why two copies of
                // this dialog opened on the same order cannot both succeed.
                if (_reviews.AddReview(UserSession.UserId, SelectedMedicineId(), _orderId,
                                       _rating, txtComment.Text.Trim(), out message))
                {
                    // Trim is applied to the comment above so a box containing only spaces
                    // is stored as an empty string rather than as whitespace.
                    MessageBox.Show(message, "Review posted", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // OK is what tells the order history that something changed, so it can
                    // reload and drop the Rate button to "Already reviewed" without the
                    // customer having to refresh the screen by hand.
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    // A refusal, not a failure: AddReview returned false with a reason, and
                    // nothing was written. Warning rather than Error, because the
                    // application is working exactly as intended.
                    MessageBox.Show(message, "Review not posted", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    // The dialog deliberately stays open and the list is rebuilt from the
                    // database. If the refusal was a duplicate, that medicine has now
                    // disappeared from the dropdown, which shows the customer why rather
                    // than letting them press Submit again on the same item.
                    LoadReviewableItems();
                    ValidateAll();   // re-run the gate against the rebuilt list
                }
            }
            catch (Exception ex)
            {
                // Only genuine faults reach here, such as the database being unreachable.
                // A refused review is handled above as a false return, not as an exception,
                // so this block never fires for an ordinary rule violation.
                MessageBox.Show("The review could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            // Cancel is set explicitly so the order history can tell a deliberate
            // abandonment from a posted review and skip the reload it would otherwise do.
            DialogResult = DialogResult.Cancel;

            // Assigning DialogResult on a form shown with ShowDialog already closes it, so
            // this is belt and braces. It is kept so both button handlers read the same
            // way, and so the form still closes if it is ever shown non-modally.
            Close();
        }
    }
}
