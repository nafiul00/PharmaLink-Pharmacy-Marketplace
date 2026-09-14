using System.Data;                  // DataTable, the shape the reviews arrive in
using System.Drawing;               // Color and Font, including the strikeout on the old price
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the other UI types
using PharmaLinkApp.Helpers;        // UiTheme and Validator: the shared look and checks
using PharmaLinkApp.Models;         // Medicine, the typed object one details row becomes
using PharmaLinkApp.Services;       // the three services; all the SQL for this screen is there

// All screens share one namespace, so forms open each other by short name.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 23: one medicine in full, plus its reviews.</summary>
    public partial class MedicineDetailsForm : Form
    {
        // Three services, because this screen shows a medicine, its reviews and a basket.
        private readonly MedicineService _medicines = new MedicineService();
        private readonly ReviewService _reviews = new ReviewService();   // the review list and the average, visible rows only
        private readonly CartService _cart = new CartService();          // the add to basket write, with its own stock re-read

        // readonly: this screen is about one medicine for its whole life.
        private readonly int _medicineId;
        // The loaded row, kept because ShowPrice and the Add button both need it.
        private Medicine _medicine;

        // A constructor parameter, so the form cannot be built without naming a medicine.
        public MedicineDetailsForm(int medicineId)
        {
            InitializeComponent();      // controls first: everything in Load depends on them
            _medicineId = medicineId;   // the only chance to set it, since the field is readonly
        }

        // Load, not the constructor: these calls touch controls that must exist.
        private void MedicineDetailsForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // first, so a failed load still appears on a styled window
            try   // both reads cross the network, so a failure must become a message
            {
                LoadMedicine();  // may decide the medicine is gone and close the form
                LoadReviews();   // second, so no review can outlive its product on screen
            }
            catch (Exception ex)   // the outermost layer of this form's own startup
            {
                // One catch for both reads: a half filled details screen is worse than none.
                MessageBox.Show("This medicine could not be loaded.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // modal, the form is closing
                Close();   // close rather than sit there showing blanks that read as real values
            }
        }

        // Appearance only, routed through UiTheme so this dialog matches its caller.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Medicine Details");        // title, background and window rules
            StartPosition = FormStartPosition.CenterParent;     // opens over the catalogue that launched it

            panelHeader.BackColor = UiTheme.Primary;                                  // the dark band behind the name
            lblMedicineName.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold); // the largest text on the form
            lblMedicineName.ForeColor = Color.White;                                   // white is the only readable pairing on Primary
            lblGenericName.Font = UiTheme.FontSmall;                                   // smaller: a subtitle to the brand name
            lblGenericName.ForeColor = Color.FromArgb(200, 230, 220);                  // dimmed white, so it supports rather than competes

            foreach (GroupBox group in new[] { grpFacts, grpPrice })   // a loop, so the two panels cannot differ
            {
                group.Font = UiTheme.FontHeading;      // the caption is a heading in the page hierarchy
                group.ForeColor = UiTheme.Primary;     // brand colour ties the boxes to the header band
                group.BackColor = UiTheme.CardBack;    // a card fill, lifting the panels off the form
            }

            // The six grey words naming each fact, styled so none looks like a value.
            foreach (Label caption in new[] { lblManufacturerCaption, lblStrengthCaption, lblCategoryCaption,
                                              lblExpiryCaption, lblPharmacyCaption, lblStockCaption })   // one array, one style
            {
                caption.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);   // small and bold: a field label
                caption.ForeColor = UiTheme.TextMuted;                              // muted, so the eye goes to the value
            }

            // A second loop for the six answers, because the two sets are styled differently.
            foreach (Label value in new[] { lblManufacturer, lblStrength, lblCategory,
                                            lblExpiry, lblPharmacy, lblStock })   // same order as the captions above
            {
                value.Font = new Font("Segoe UI", 10.5F);   // larger than its caption: label then answer
                value.ForeColor = UiTheme.TextDark;         // full strength text, the content wanted here
            }

            lblDescription.Font = UiTheme.FontBody;                                 // a paragraph, so the body font
            lblDescription.ForeColor = UiTheme.TextMuted;                           // muted: background to the facts
            lblRxBadge.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);  // bold: it decides what checkout demands

            lblReviewsTitle.Font = UiTheme.FontHeading;                                  // the heading over the grid
            lblReviewsTitle.ForeColor = UiTheme.TextDark;                                // dark, marking a new section
            lblAverageRating.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);  // the headline number of the section
            lblAverageRating.ForeColor = UiTheme.TextDark;                               // not green or red: an average is a fact
            lblReviewNote.Font = UiTheme.FontSmall;                                      // the small print about the grid
            lblReviewNote.ForeColor = UiTheme.TextMuted;                                 // muted: an aside, not an instruction
            lblAddMessage.Font = UiTheme.FontSmall;                                      // no colour: the click picks green or red

            UiTheme.StyleSecondary(btnBack);                                         // outline, so Back never competes
            UiTheme.StylePrimary(btnAddToCart);                                      // filled: the one thing this screen does
            btnAddToCart.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);  // after StylePrimary, which sets its own font
            UiTheme.StyleGrid(dgvReviews);                                           // the same grid rules as every screen
            dgvReviews.CellFormatting += dgvReviews_CellFormatting;                  // wired here, beside the grid it colours
        }

        /// <summary>Fills the fact panel from one row; formats, never calculates.</summary>
        private void LoadMedicine()
        {
            // A typed Medicine, so named properties replace twenty string column lookups.
            _medicine = _medicines.GetDetails(_medicineId);

            // null is not an error: it may have been delisted since the catalogue drew.
            if (_medicine == null)
            {
                MessageBox.Show("That medicine is no longer available.", "PharmaLink",              // Information, not Error
                    MessageBoxButtons.OK, MessageBoxIcon.Information);                              // OK only, there is no choice
                Close();                                                                            // nothing to show, so the window goes
                return;                                                                             // Close does not stop this method
            }

            // Name and strength together, because "Napa" alone is ambiguous across strengths.
            lblMedicineName.Text = _medicine.MedicineName + "  " + _medicine.Strength;
            lblGenericName.Text = "Generic name: " + _medicine.GenericName;   // labelled: a prescription uses this name

            lblManufacturer.Text = _medicine.Manufacturer;   // as stored: how two look alike strips are told apart
            // A dash, not a blank: an empty label beside a caption reads as a failed load.
            lblStrength.Text = string.IsNullOrWhiteSpace(_medicine.Strength) ? "-" : _medicine.Strength;
            lblCategory.Text = _medicine.CategoryName;       // the joined name, since an id means nothing to a patient
            lblExpiry.Text = _medicine.ExpiryDate.ToString("dd MMM yyyy");   // fixed format, so day and month never swap
            // The area sits beside the shop because it decides how far the delivery comes.
            lblPharmacy.Text = _medicine.PharmacyName + "   (" + _medicine.Area + ")";

            lblStock.Text = _medicine.Stock > 0                // the exact figure, so a request for 10 can be judged
                ? _medicine.Stock + " unit(s) on the shelf"   // the real number rather than a vague "available"
                : "Out of stock";                             // worded, because "0 unit(s)" reads like a glitch
            lblStock.ForeColor = _medicine.Stock > 0 ? UiTheme.Success : UiTheme.Danger;   // colour repeats the words

            lblDescription.Text = string.IsNullOrWhiteSpace(_medicine.Description)   // blank out rather than keep designer text
                ? "" : _medicine.Description;                // WhiteSpace, not Empty: one space is just as empty

            // RequiresRx is stored on the medicine, so this badge states a fact.
            if (_medicine.RequiresRx)
            {
                // Say up front what checkout will demand, so nobody wastes the trip.
                lblRxBadge.Text = "  Rx  -  prescription only. You will be asked to upload a photograph of your " +
                                  "doctor's prescription at checkout, and the pharmacy must approve it before dispatch.";   // split to keep the line readable
                lblRxBadge.ForeColor = UiTheme.Warning;   // amber: a condition to meet, not a refusal
            }
            else   // the flag is false, so this one can be bought with no paperwork
            {
                // Stated positively, so an absent badge is never read as one that failed to draw.
                lblRxBadge.Text = "  Over the counter  -  no prescription needed.";
                lblRxBadge.ForeColor = UiTheme.Success;   // the green that means "nothing is in your way"
            }

            ShowPrice();   // its own method, because the price has two quite different forms

            btnAddToCart.Enabled = _medicine.Stock > 0;   // same figure as the panel, so they cannot disagree
            // The caption carries the reason; a disabled "Add to cart" looks like a fault.
            btnAddToCart.Text = _medicine.Stock > 0 ? "Add to cart" : "Out of stock";
            // A disabled button keeps its colour, so grey it or it still invites a click.
            if (_medicine.Stock == 0) btnAddToCart.BackColor = Color.FromArgb(170, 190, 184);
        }

        /// <summary>Two presentations of one price: with an offer, and without.</summary>
        private void ShowPrice()
        {
            // DiscountPercent came from the query's OUTER APPLY, so it is already 0 when idle.
            if (_medicine.DiscountPercent > 0m)
            {
                lblOriginalPrice.Text = UiTheme.Money(_medicine.UnitPrice);   // the list price, about to be struck through
                lblOriginalPrice.Font = new Font("Segoe UI", 11F, FontStyle.Strikeout);   // Strikeout draws the line
                lblOriginalPrice.ForeColor = UiTheme.TextMuted;   // muted, so the eye lands on what is charged

                // PriceAfterDiscount is a computed property on Medicine, not a sum done here.
                lblFinalPrice.Text = UiTheme.Money(_medicine.PriceAfterDiscount);
                lblFinalPrice.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);   // largest number: the one charged
                lblFinalPrice.ForeColor = UiTheme.Success;   // green means "an offer is running", as it does everywhere

                // Derived from the two numbers on screen, so the badge cannot be a penny out.
                decimal saving = _medicine.UnitPrice - _medicine.PriceAfterDiscount;
                lblDiscountBadge.Text = _medicine.DiscountPercent.ToString("N0") + "% off today  -  " +   // N0: 20, not 20.00
                                        "you save " + UiTheme.Money(saving) + " per unit.";   // per unit, not per basket
                lblDiscountBadge.ForeColor = UiTheme.Success;                              // same green, so it reads as one statement
                lblDiscountBadge.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold); // bold and small: a badge
            }
            else   // DiscountPercent is 0, so no offer covers today for this medicine
            {
                lblOriginalPrice.Text = "";   // blanked: a lone struck price would imply an offer
                // One price, same font and position, so the layout never jumps between medicines.
                lblFinalPrice.Text = UiTheme.Money(_medicine.UnitPrice);
                lblFinalPrice.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);   // identical to the offer branch
                lblFinalPrice.ForeColor = UiTheme.TextDark;   // not green: green is reserved for a real saving

                // Said in words, since silence looks like an offer that failed to load.
                lblDiscountBadge.Text = "No offer is running on this medicine today.";
                lblDiscountBadge.ForeColor = UiTheme.TextMuted;   // muted, so no offer is not an announcement
                lblDiscountBadge.Font = UiTheme.FontSmall;        // plain, dropping the bold of the offer branch
            }
        }

        /// <summary>The visible reviews, newest first; hidden ones never arrive.</summary>
        private void LoadReviews()
        {
            DataTable table = _reviews.GetForMedicine(_medicineId);   // kept locally too: the row count is needed twice
            dgvReviews.DataSource = table;   // this is what creates the columns, one per DataColumn

            // Renaming must follow the binding, because until then there are no columns.
            if (dgvReviews.Columns.Count > 0)
            {
                dgvReviews.Columns["ReviewId"].Visible = false;   // the key, carried but of no interest to a reader
                dgvReviews.Columns["ReviewerName"].HeaderText = "Reviewer";   // the indexer matches the SELECT alias
                dgvReviews.Columns["ReviewerName"].FillWeight = 55;   // Fill mode, so this is a share of the width
                dgvReviews.Columns["Rating"].HeaderText = "Stars";   // "Stars" says it is a five point scale
                dgvReviews.Columns["Rating"].FillWeight = 25;   // a single digit
                dgvReviews.Columns["Comment"].HeaderText = "Comment";   // the reason anyone reads this grid
                dgvReviews.Columns["Comment"].FillWeight = 180;   // by far the widest, so comments are not clipped
                dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";   // the date is context for the comment
                dgvReviews.Columns["ReviewDate"].FillWeight = 50;   // just wide enough for a formatted date
            }

            // Averaged by the database over every visible review, not over the rows on screen.
            decimal average = _reviews.GetAverageForMedicine(_medicineId);

            lblAverageRating.Text = table.Rows.Count == 0   // 0 reviews would print "0.00 / 5", which reads as awful
                ? "No reviews yet"                                 // names the absence, so it is not read as a bad score
                // N2 keeps 4.50 stable, and the count separates one 5 star from forty.
                : average.ToString("N2") + " / 5   from " + table.Rows.Count + " review(s)";

            lblReviewsTitle.Text = table.Rows.Count == 0   // the heading absorbs the empty state as well
                ? "What other patients said  -  nobody has reviewed this yet"   // explains the blank grid below
                : "What other patients said";                                   // the plain heading once there is one
        }

        /// <summary>Tints each review row by its rating as the cell is painted.</summary>
        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The header arrives as row -1, and the event can fire mid-rebind with no columns.
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;

            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];   // from the event, since every row is painted
            object rating = row.Cells["Rating"].Value;   // read the cell, so sorting cannot misplace a colour
            if (rating == null || rating == DBNull.Value) return;   // leave the default colour; DBNull would throw

            int stars = Convert.ToInt32(rating);   // Convert, because the boxed numeric type is the provider's choice
            // 1-2 is a complaint, 4-5 a recommendation; a 3 is left white on purpose.
            if (stars <= 2) row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
            else if (stars >= 4) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;   // the delivered green
            else row.DefaultCellStyle.BackColor = Color.White;   // reset, since rows are reused while scrolling
        }

        // The one write this screen performs; everything above only reads.
        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            int quantity;   // declared here so it outlives the call below and can be passed on
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))   // rejects text, blanks, zero and negatives
            {
                // Inline, not a dialog: a modal box would pull the reader off a long page.
                UiTheme.ShowError(lblAddMessage, txtQuantity, "Enter a whole quantity of one or more.");
                lblAddMessage.Visible = true;   // ShowError sets the text; this puts it on screen
                return;                         // nothing is added, the basket is untouched
            }

            try   // the add crosses the network, so an unreachable database becomes a message
            {
                string message;   // the service writes its refusal text here
                // The service re-reads the stock as it writes, since _medicine.Stock is stale.
                if (_cart.AddOrIncrease(UserSession.UserId, _medicineId, quantity, out message))
                {
                    txtQuantity.BackColor = Color.White;   // clear any red tint left by an earlier attempt
                    lblAddMessage.Text = quantity + " added to your cart.";   // echo the number actually taken
                    lblAddMessage.ForeColor = UiTheme.Success;                // green, as on the stock line
                    lblAddMessage.Visible = true;                             // the label starts hidden in the designer
                }
                else   // refused: not enough stock, or delisted since the page loaded
                {
                    lblAddMessage.Text = message;   // the service's own wording, written beside its rule
                    lblAddMessage.ForeColor = UiTheme.Danger;   // red, the same red the error labels use
                    lblAddMessage.Visible = true;               // same reveal as the success path
                }
            }
            catch (Exception ex)   // the outermost layer of this click: nothing may escape it
            {
                // A write failure is reported where a refusal is, so there is one place to look.
                lblAddMessage.Text = ex.Message;
                lblAddMessage.ForeColor = UiTheme.Danger;   // a failed write is at least as serious as a refusal
                lblAddMessage.Visible = true;               // reveal it, the label may never have been shown
            }
        }

        // Close, not Dispose: the caller used ShowDialog inside a using block.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
