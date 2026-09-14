using System.Data;                  // DataTable, the shape the reviews arrive in
using System.Drawing;               // Color and Font, including the strikeout used on the old price
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the other UI types
using PharmaLinkApp.Helpers;        // UiTheme and Validator, the shared look and the shared checks
using PharmaLinkApp.Models;         // Medicine, the typed object one row of the details query becomes
using PharmaLinkApp.Services;       // the three services; all the SQL for this screen lives in them

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 23. Everything about one medicine: manufacturer, strength,
    /// expiry date, selling pharmacy, whether a prescription is needed, and the
    /// full list of visible reviews.
    ///
    /// When an offer is running today the original price is struck through and
    /// the discounted price is shown beside it. That number comes from the same
    /// query the cart and the invoice use, so the three can never disagree.
    /// </summary>
    public partial class MedicineDetailsForm : Form
    {
        // Three services because this screen shows three separate things: the medicine, the
        // reviews written about it, and the basket it can be added to. Each one owns its own
        // queries, so no SQL appears in this file and the form is left with one job - read
        // the controls, call a service, show what comes back.
        private readonly MedicineService _medicines = new MedicineService();
        private readonly ReviewService _reviews = new ReviewService();
        private readonly CartService _cart = new CartService();

        // readonly because the screen is about ONE medicine for its whole life. It is set in
        // the constructor and cannot be reassigned afterwards, so no handler can quietly
        // repoint the form at a different medicine while its labels still describe this one.
        private readonly int _medicineId;
        // The loaded row, kept as a field because several methods need it after the load:
        // ShowPrice formats it and the Add to cart button reads the stock from it.
        private Medicine _medicine;

        // The identifier is a constructor parameter rather than a property set afterwards,
        // so there is no way to construct this form without saying which medicine it is for.
        public MedicineDetailsForm(int medicineId)
        {
            // Controls first: the assignment below touches no control, but everything that
            // follows in the Load event does, and none of them exist until this has run.
            InitializeComponent();
            _medicineId = medicineId;
        }

        private void MedicineDetailsForm_Load(object sender, EventArgs e)
        {
            // Theme first so a failed load is still shown on a properly styled window, and
            // because ApplyTheme is what attaches the CellFormatting handler for the reviews.
            ApplyTheme();
            try
            {
                // Order matters: LoadMedicine may decide the medicine is gone and close the
                // form, and there is no point reading reviews for something that is not shown.
                LoadMedicine();
                LoadReviews();
            }
            catch (Exception ex)
            {
                // One catch around both reads. A details screen with half its labels filled in
                // would be worse than no screen at all, so the failure is reported...
                MessageBox.Show("This medicine could not be loaded.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // ...and the window closes rather than sitting there showing blanks the
                // customer might read as real values.
                Close();
            }
        }

        // Appearance only: fonts, colours and button styles, all routed through UiTheme so
        // this dialog matches the screen that opened it.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Medicine Details");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblMedicineName.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
            lblMedicineName.ForeColor = Color.White;
            lblGenericName.Font = UiTheme.FontSmall;
            lblGenericName.ForeColor = Color.FromArgb(200, 230, 220);

            foreach (GroupBox group in new[] { grpFacts, grpPrice })
            {
                group.Font = UiTheme.FontHeading;
                group.ForeColor = UiTheme.Primary;
                group.BackColor = UiTheme.CardBack;
            }

            foreach (Label caption in new[] { lblManufacturerCaption, lblStrengthCaption, lblCategoryCaption,
                                              lblExpiryCaption, lblPharmacyCaption, lblStockCaption })
            {
                caption.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
                caption.ForeColor = UiTheme.TextMuted;
            }

            foreach (Label value in new[] { lblManufacturer, lblStrength, lblCategory,
                                            lblExpiry, lblPharmacy, lblStock })
            {
                value.Font = new Font("Segoe UI", 10.5F);
                value.ForeColor = UiTheme.TextDark;
            }

            lblDescription.Font = UiTheme.FontBody;
            lblDescription.ForeColor = UiTheme.TextMuted;
            lblRxBadge.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);

            lblReviewsTitle.Font = UiTheme.FontHeading;
            lblReviewsTitle.ForeColor = UiTheme.TextDark;
            lblAverageRating.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            lblAverageRating.ForeColor = UiTheme.TextDark;
            lblReviewNote.Font = UiTheme.FontSmall;
            lblReviewNote.ForeColor = UiTheme.TextMuted;
            lblAddMessage.Font = UiTheme.FontSmall;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StylePrimary(btnAddToCart);
            btnAddToCart.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            UiTheme.StyleGrid(dgvReviews);
            dgvReviews.CellFormatting += dgvReviews_CellFormatting;
        }

        /// <summary>
        /// Fills the fact panel from one row. Everything here is a label assignment: the
        /// values were decided by the query, including the discount percentage, so this
        /// method formats and never calculates.
        /// </summary>
        private void LoadMedicine()
        {
            // A typed Medicine rather than a DataTable, because this screen reads named
            // fields one at a time instead of binding a grid; a compile time property name
            // is safer than a string column lookup repeated twenty times below.
            _medicine = _medicines.GetDetails(_medicineId);

            // The service returns null when no row matched, which is not an error: the
            // medicine may have been delisted between the catalogue being drawn and this
            // being opened. Treating it as a normal case is why GetDetails returns null
            // rather than throwing.
            if (_medicine == null)
            {
                MessageBox.Show("That medicine is no longer available.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                // The return is essential. Close does not stop the current method, so without
                // it every line below would run against a null reference and throw.
                return;
            }

            // Name and strength together in the title, because "Napa" alone is ambiguous when
            // the same brand is sold at three strengths.
            lblMedicineName.Text = _medicine.MedicineName + "  " + _medicine.Strength;
            // The generic name is labelled explicitly rather than shown bare, since it is the
            // name a prescription is likely to use and the customer needs to recognise it.
            lblGenericName.Text = "Generic name: " + _medicine.GenericName;

            lblManufacturer.Text = _medicine.Manufacturer;
            // A dash rather than an empty label when there is no strength. A blank space next
            // to a caption reads as a value that failed to load, whereas a dash says "none".
            lblStrength.Text = string.IsNullOrWhiteSpace(_medicine.Strength) ? "-" : _medicine.Strength;
            lblCategory.Text = _medicine.CategoryName;
            // An explicit format rather than the machine's default, so the date reads the same
            // way on every computer regardless of its regional settings, and a day and month
            // cannot be mistaken for one another.
            lblExpiry.Text = _medicine.ExpiryDate.ToString("dd MMM yyyy");
            // The area is shown beside the shop because it decides how far the delivery comes.
            lblPharmacy.Text = _medicine.PharmacyName + "   (" + _medicine.Area + ")";

            // The exact figure rather than a vague "available", so the customer can see
            // whether the quantity they want is realistic before trying to add it.
            lblStock.Text = _medicine.Stock > 0
                ? _medicine.Stock + " unit(s) on the shelf"
                : "Out of stock";
            // Colour carries the same message as the words for anyone scanning the panel.
            lblStock.ForeColor = _medicine.Stock > 0 ? UiTheme.Success : UiTheme.Danger;

            // An empty string rather than leaving whatever the designer put in the label, so
            // a medicine with no description shows nothing instead of placeholder text.
            lblDescription.Text = string.IsNullOrWhiteSpace(_medicine.Description)
                ? "" : _medicine.Description;

            // RequiresRx is a stored flag on the medicine, so the badge is a fact about the
            // product rather than a judgement made here.
            if (_medicine.RequiresRx)
            {
                // The badge says what will be asked for and when, because a customer who
                // discovers the prescription requirement at checkout has wasted their time.
                lblRxBadge.Text = "  Rx  -  prescription only. You will be asked to upload a photograph of your " +
                                  "doctor's prescription at checkout, and the pharmacy must approve it before dispatch.";
                lblRxBadge.ForeColor = UiTheme.Warning;
            }
            else
            {
                // The other branch is stated positively rather than left blank, so an absent
                // badge can never be confused with a badge that failed to render.
                lblRxBadge.Text = "  Over the counter  -  no prescription needed.";
                lblRxBadge.ForeColor = UiTheme.Success;
            }

            // Pulled out into its own method because the price has two quite different
            // presentations and mixing them into this one would bury the facts above.
            ShowPrice();

            // The same stock figure drives the button, so the panel and the button can never
            // contradict one another.
            btnAddToCart.Enabled = _medicine.Stock > 0;
            // The caption carries the reason: a disabled button still reading "Add to cart"
            // looks like a fault rather than a deliberate refusal.
            btnAddToCart.Text = _medicine.Stock > 0 ? "Add to cart" : "Out of stock";
            // A disabled WinForms button keeps its assigned colour, so the primary blue is
            // overridden with grey; otherwise it would look live and invite a pointless click.
            if (_medicine.Stock == 0) btnAddToCart.BackColor = Color.FromArgb(170, 190, 184);
        }

        /// <summary>
        /// Two presentations of one price. The branch is on whether an offer exists, so the
        /// strikeout only ever appears when there is genuinely something to compare against.
        /// </summary>
        private void ShowPrice()
        {
            // DiscountPercent came from the query's OUTER APPLY over today's offers, so it is
            // already 0 when nothing is running. This form never looks at a date itself.
            if (_medicine.DiscountPercent > 0m)
            {
                // Struck through original beside the discounted price, exactly as
                // the report describes.
                // The list price, struck through. FontStyle.Strikeout is what draws the
                // line; the label is otherwise an ordinary Label showing UnitPrice.
                lblOriginalPrice.Text = UiTheme.Money(_medicine.UnitPrice);
                lblOriginalPrice.Font = new Font("Segoe UI", 11F, FontStyle.Strikeout);
                // Muted and smaller than the price beside it, so the eye lands on what will
                // actually be charged rather than on the number that no longer applies.
                lblOriginalPrice.ForeColor = UiTheme.TextMuted;

                // PriceAfterDiscount is a COMPUTED PROPERTY on the Medicine model
                // (UnitPrice * (1 - DiscountPercent / 100), rounded to 2dp), not a
                // calculation done here. DiscountPercent itself arrived from the query's
                // OUTER APPLY over today's offers. So this screen, the cart and the
                // invoice all derive the same number from the same two sources and
                // cannot print three different prices for one medicine.
                lblFinalPrice.Text = UiTheme.Money(_medicine.PriceAfterDiscount);
                lblFinalPrice.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);
                // Green here means the same thing it means on the grid rows and the offers
                // screen: an offer is running. The colour vocabulary is kept small on purpose.
                lblFinalPrice.ForeColor = UiTheme.Success;

                // The saving is derived from the two numbers already on screen rather than
                // from the percentage, so the badge cannot disagree with the figures above it
                // by a rounding penny.
                decimal saving = _medicine.UnitPrice - _medicine.PriceAfterDiscount;
                // "N0" prints 20 rather than 20.00, because a whole percentage reads as a
                // headline and the decimals belong on the money, not on the rate.
                lblDiscountBadge.Text = _medicine.DiscountPercent.ToString("N0") + "% off today  -  " +
                                        // Per unit is stated explicitly so the figure is not
                                        // mistaken for the saving on the whole basket.
                                        "you save " + UiTheme.Money(saving) + " per unit.";
                lblDiscountBadge.ForeColor = UiTheme.Success;
                lblDiscountBadge.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            }
            else
            {
                // Blanked rather than left holding a previous value: this label is only
                // meaningful when there is a discount, and a lone struck through price would
                // suggest an offer that does not exist.
                lblOriginalPrice.Text = "";
                // One price, shown in the same large font and the same position as the
                // discounted one above, so the layout does not jump between medicines.
                lblFinalPrice.Text = UiTheme.Money(_medicine.UnitPrice);
                lblFinalPrice.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);
                // Ordinary dark text rather than green, because green is reserved for a
                // genuine saving and using it here would make every price look discounted.
                lblFinalPrice.ForeColor = UiTheme.TextDark;

                // Stated in words rather than left empty. Silence would leave the customer
                // wondering whether an offer had failed to load.
                lblDiscountBadge.Text = "No offer is running on this medicine today.";
                lblDiscountBadge.ForeColor = UiTheme.TextMuted;
                lblDiscountBadge.Font = UiTheme.FontSmall;
            }
        }

        /// <summary>
        /// The reviews other patients have written, newest first. Hidden reviews are
        /// excluded by the query rather than skipped here, so a moderated comment cannot
        /// reach this screen by way of a forgotten check.
        /// </summary>
        private void LoadReviews()
        {
            DataTable table = _reviews.GetForMedicine(_medicineId);
            // Assigning DataSource is what creates the columns: with AutoGenerateColumns left
            // at its default the grid reads the DataTable's schema and adds one column per
            // DataColumn, in SELECT order, each named after the column it came from. That is
            // why the code below only renames columns and never creates any.
            dgvReviews.DataSource = table;

            // The renaming has to come after the assignment, because until it runs there are
            // no columns and every indexer here would throw. The guard covers a query that
            // returned no schema at all, which would otherwise crash the whole screen.
            if (dgvReviews.Columns.Count > 0)
            {
                // Carried by the query as the row's key but of no interest to a reader, so it
                // is hidden rather than dropped from the SELECT.
                dgvReviews.Columns["ReviewId"].Visible = false;
                // The alias from the SELECT is what the indexer matches, so these strings and
                // the query's column names have to stay in step; a rename there breaks this
                // lookup at run time rather than at compile time.
                dgvReviews.Columns["ReviewerName"].HeaderText = "Reviewer";
                // StyleGrid put the grid in Fill mode, so FillWeight is a proportion of the
                // width rather than a pixel count. A name needs a modest share.
                dgvReviews.Columns["ReviewerName"].FillWeight = 55;
                // "Stars" rather than "Rating", because the number is on a five point scale
                // and the word tells the reader that without a legend.
                dgvReviews.Columns["Rating"].HeaderText = "Stars";
                dgvReviews.Columns["Rating"].FillWeight = 25;   // a single digit
                // The comment is the reason anyone reads this grid, so it is given by far the
                // largest share of the width.
                dgvReviews.Columns["Comment"].HeaderText = "Comment";
                dgvReviews.Columns["Comment"].FillWeight = 180;
                // "Written on" rather than "ReviewDate", since the date matters as context for
                // the comment beside it.
                dgvReviews.Columns["ReviewDate"].HeaderText = "Written on";
                dgvReviews.Columns["ReviewDate"].FillWeight = 50;
            }

            // The average is calculated by the database over every visible review, not by
            // averaging the rows on screen. They happen to be the same set today, but a grid
            // that is ever paged or filtered would quietly start reporting a different number.
            decimal average = _reviews.GetAverageForMedicine(_medicineId);

            // With no reviews the average is 0, and printing "0.00 / 5" would read as a
            // terrible product rather than as an absence of opinions, so the empty case is
            // given its own wording.
            lblAverageRating.Text = table.Rows.Count == 0
                ? "No reviews yet"
                // "N2" keeps two decimals so 4.5 and 4.50 do not alternate between medicines,
                // and the count is shown because an average of 5 from one review is not the
                // same claim as an average of 5 from forty.
                : average.ToString("N2") + " / 5   from " + table.Rows.Count + " review(s)";

            // The heading absorbs the empty state too, so an empty grid is explained by the
            // words above it instead of looking like a panel that failed to load.
            lblReviewsTitle.Text = table.Rows.Count == 0
                ? "What other patients said  -  nobody has reviewed this yet"
                : "What other patients said";
        }

        /// <summary>
        /// Tints each review row by its rating. CellFormatting is used rather than a loop
        /// after binding because it fires as each cell is painted, so the colours survive
        /// sorting and scrolling and always follow the row's own data.
        /// </summary>
        private void dgvReviews_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The header arrives as row index -1, and the event can fire mid-rebind when there
            // are no columns to look up. Both would throw on the cell read below.
            if (e.RowIndex < 0 || dgvReviews.Columns.Count == 0) return;

            // Taken from the event argument because this runs for every visible row, not just
            // the selected one.
            DataGridViewRow row = dgvReviews.Rows[e.RowIndex];
            // Read from the cell rather than the DataTable so sorting cannot apply a colour to
            // the wrong row.
            object rating = row.Cells["Rating"].Value;
            // Returning early on a missing value leaves the row at its default colour, which
            // is the honest outcome; converting DBNull would throw.
            if (rating == null || rating == DBNull.Value) return;

            // Convert rather than a cast, because the cell holds a boxed value whose exact
            // numeric type is the provider's choice.
            int stars = Convert.ToInt32(rating);
            // Two bands with a deliberate gap: 1 and 2 are complaints, 4 and 5 are
            // recommendations, and a 3 is left white because a neutral review coloured either
            // way would misrepresent it.
            if (stars <= 2) row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
            else if (stars >= 4) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            // The final else is not tidiness: DataGridView reuses row objects while scrolling,
            // so a row left unset would keep the colour of whichever row last used it.
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        // ---------------------------------------------------------------------

        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            int quantity;
            // The shared Validator, so "a whole quantity of one or more" means the same thing
            // here as on the catalogue and the offers screens. It rejects text, blanks, zero
            // and negatives in one call.
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))
            {
                // An inline message rather than a dialog on this screen, because the customer
                // is reading a long page and a modal box would take them away from it.
                // ShowError writes the text, colours it and tints the offending box so the
                // message and the field that caused it are connected.
                UiTheme.ShowError(lblAddMessage, txtQuantity, "Enter a whole quantity of one or more.");
                lblAddMessage.Visible = true;
                // No attempt to add anything: the basket is left exactly as it was.
                return;
            }

            try
            {
                // _medicineId is used rather than a grid selection, because this whole form is
                // about one medicine and there is no row here to get wrong.
                string message;
                // The service re-reads the stock as it writes. This form does not compare the
                // quantity with _medicine.Stock, because that figure was read when the screen
                // opened and another customer's checkout may have taken units since.
                if (_cart.AddOrIncrease(UserSession.UserId, _medicineId, quantity, out message))
                {
                    // Clears the red tint ShowError may have left on a previous attempt, so a
                    // successful add does not sit next to a box still marked as wrong.
                    txtQuantity.BackColor = Color.White;
                    lblAddMessage.Text = quantity + " added to your cart.";
                    lblAddMessage.ForeColor = UiTheme.Success;
                    lblAddMessage.Visible = true;
                }
                else
                {
                    // The refusal text comes from the service, written next to the rule that
                    // produced it, rather than being guessed at here from a false return.
                    lblAddMessage.Text = message;
                    lblAddMessage.ForeColor = UiTheme.Danger;
                    lblAddMessage.Visible = true;
                }
            }
            catch (Exception ex)
            {
                // A database failure during the add is reported in the same place as a refusal
                // rather than as a dialog, so the customer looks in one spot for the outcome.
                // Without this catch an unreachable database would close the application.
                lblAddMessage.Text = ex.Message;
                lblAddMessage.ForeColor = UiTheme.Danger;
                lblAddMessage.Visible = true;
            }
        }

        // Close rather than Dispose: the screen that opened this one did so with ShowDialog
        // inside a using block, so closing hands control back there and the using disposes it.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
