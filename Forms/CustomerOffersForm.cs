using System.Data;                  // DataTable, the shape OfferService hands back
using System.Drawing;               // Color and Font for the theme and the row tinting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme and Validator, the shared look and the shared checks
using PharmaLinkApp.Models;         // Category, the typed item the category filter is built from
using PharmaLinkApp.Services;       // the four services; every query this screen runs lives in them

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 29. Every offer that is actually running today.
    ///
    /// The query returns only offers where today falls between StartDate and
    /// EndDate, on a medicine that is in stock, at a pharmacy that is Approved.
    /// Expired offers are excluded by the database rather than by this form, so
    /// a stale discount can never be displayed by mistake.
    /// </summary>
    public partial class CustomerOffersForm : Form
    {
        // One service per subject: the offers themselves, the two dropdown sources, and the
        // basket an offer can be added to. Because every query lives behind one of these,
        // this file contains no SQL at all - it reads two controls, calls a service and
        // binds the table that comes back.
        private readonly OfferService _offers = new OfferService();
        private readonly CategoryService _categories = new CategoryService();
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly CartService _cart = new CartService();

        // The same guard the catalogue screen uses. Both dropdowns have SelectedIndexChanged
        // wired to Filter_Changed, and assigning SelectedIndex in code raises that event just
        // as a click does, so without this flag the offers query would run twice before the
        // filters were finished being built. It starts true so nothing can query too early.
        private bool _loading = true;

        public CustomerOffersForm()
        {
            // Creates the controls the designer drew; everything that touches them waits for
            // the Load event below.
            InitializeComponent();
        }

        private void CustomerOffersForm_Load(object sender, EventArgs e)
        {
            // Appearance first, which also attaches the CellFormatting handler used below.
            ApplyTheme();

            // Item 0 is a placeholder meaning "no filter", so one ComboBox expresses both the
            // on and the off state and no separate checkbox is needed.
            cmbCategory.Items.Add("All categories");
            // Built from the database rather than typed in here, so a category an
            // administrator adds appears without a rebuild and a retired one stops appearing.
            foreach (Category category in _categories.GetActiveList())
                // "3 - Antibiotics": the identifier is carried inside the display text so the
                // selection can be turned back into a number without a second lookup list.
                cmbCategory.Items.Add(category.CategoryId + " - " + category.CategoryName);
            // Selecting the placeholder is what makes the screen open showing every offer.
            // This raises the change event, which the _loading flag is currently swallowing.
            cmbCategory.SelectedIndex = 0;

            cmbArea.Items.Add("All areas");
            // The true argument is approvedOnly, so the list is exactly the set of places an
            // offer can actually be delivered from; an area served only by a pending shop is
            // never offered as a choice that would return nothing.
            foreach (string area in _pharmacies.GetAreas(true)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;

            // The date is printed so the customer can see which day the list is valid for.
            // It is a caption only: nothing in this form compares a date with anything, and
            // the same format string is used everywhere so the day and month cannot be read
            // the wrong way round on a differently configured machine.
            lblToday.Text = "Showing offers valid on " + DateTime.Today.ToString("dd MMM yyyy");

            _loading = false;   // the filters now agree with each other, so querying is safe
            LoadGrid();         // one deliberate first read instead of the two the flag prevented
        }

        // Appearance only. The header is green rather than the usual blue because this screen
        // is about savings, and green means a discount everywhere else in the application.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Offers and Packages");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Success;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(215, 240, 225);

            lblToday.Font = UiTheme.FontSmall;
            lblToday.ForeColor = UiTheme.TextMuted;
            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StylePrimary(btnAddToCart);
            UiTheme.StyleAccent(btnViewDetails);
            UiTheme.StyleGrid(dgvOffers);
            dgvOffers.CellFormatting += dgvOffers_CellFormatting;
        }

        /// <summary>The category identifier, or 0 when "All categories" is selected.</summary>
        private int SelectedCategoryId()
        {
            // Index 0 is the placeholder and -1 means nothing is selected, so one test covers
            // both and returns the neutral value the query reads as "do not filter".
            if (cmbCategory.SelectedIndex <= 0) return 0;
            // SelectedItem is object because a ComboBox can hold anything; here it is always
            // one of the strings this form added above.
            string text = cmbCategory.SelectedItem.ToString();
            // Everything before the first space is the identifier glued on when the list was
            // built. int.Parse is safe rather than optimistic because the string was written
            // by this form moments earlier, so its shape is known.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        /// <summary>
        /// The one read path. Both filters, the refresh button and the return from the
        /// details dialog all end here, so the controls are turned into arguments in a
        /// single place and the grid is filled in a single place.
        /// </summary>
        private void LoadGrid()
        {
            // While the dropdowns are being populated their change events fire; querying then
            // would be wasted work against a half built filter.
            if (_loading) return;

            try
            {
                // Index 0 is the "All areas" placeholder, so it becomes an empty string -
                // the query's signal for "do not filter on this".
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();

                // Notice what this form does NOT do: it never checks a date. The query
                // carries "CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate",
                // so an expired offer is filtered out by the DATABASE and can never be
                // displayed by mistake. The seed data deliberately includes one expired
                // Glucometer offer precisely to prove that.
                //
                // Both arguments travel as parameters and each is paired with a condition of
                // the form (@Param = <neutral value> OR <real condition>), so a 0 or an empty
                // string makes the left side true, the OR short circuits and that filter does
                // nothing. One query therefore serves all four combinations of the two
                // filters, and no SQL text is ever built by concatenation here.
                DataTable table = _offers.GetActiveOffers(SelectedCategoryId(), area);
                // Assigning DataSource is what CREATES the columns: AutoGenerateColumns is
                // true by default, so the grid reads the DataTable's schema and adds one
                // column per DataColumn, in SELECT order, each named after the column it came
                // from. Nothing below adds a column; the SELECT list in OfferService decides
                // which columns exist, and one added there would appear here on its own.
                dgvOffers.DataSource = table;

                // The renaming must come after that assignment, because until it runs
                // dgvOffers.Columns is empty and every indexer below would throw. The Count
                // guard covers a query that returned no schema at all.
                if (dgvOffers.Columns.Count > 0)
                {
                    // Each indexer string is the DataTable column name, which is the alias the
                    // SELECT gave it, so these strings and the query have to stay in step; a
                    // rename there fails at run time rather than at compile time. HeaderText
                    // changes only the printed caption, never the name used for lookups.
                    dgvOffers.Columns["OfferId"].HeaderText = "ID";
                    // StyleGrid put the grid in Fill mode, so FillWeight is a share of the
                    // available width rather than a pixel count. An identifier needs the least.
                    dgvOffers.Columns["OfferId"].FillWeight = 26;
                    // The offer's own title is the headline of the row, so it takes by far the
                    // largest share; it is what tells a customer what the promotion is.
                    dgvOffers.Columns["OfferTitle"].HeaderText = "Offer";
                    dgvOffers.Columns["OfferTitle"].FillWeight = 130;
                    dgvOffers.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvOffers.Columns["Strength"].HeaderText = "Strength";
                    dgvOffers.Columns["Strength"].FillWeight = 42;   // "500 mg" and little else
                    // Category and Area are shown because they are the two filters above the
                    // grid; seeing the value makes it obvious why a row survived the filter.
                    dgvOffers.Columns["CategoryName"].HeaderText = "Category";
                    dgvOffers.Columns["PharmacyName"].HeaderText = "Sold by";
                    dgvOffers.Columns["Area"].HeaderText = "Area";
                    dgvOffers.Columns["Area"].FillWeight = 42;
                    // "Was" and "You pay" are set against each other deliberately: a discount
                    // is only meaningful next to the price it replaced.
                    dgvOffers.Columns["OriginalPrice"].HeaderText = "Was (Tk)";
                    dgvOffers.Columns["OriginalPrice"].FillWeight = 44;
                    dgvOffers.Columns["DiscountPercent"].HeaderText = "Off %";
                    dgvOffers.Columns["DiscountPercent"].FillWeight = 32;
                    // Calculated by the query from the same unit price and percentage the cart
                    // and the invoice use, never recalculated in this form, which is why the
                    // figure can be promised to follow the medicine into the basket.
                    dgvOffers.Columns["DiscountedPrice"].HeaderText = "You pay (Tk)";
                    dgvOffers.Columns["DiscountedPrice"].FillWeight = 50;
                    // The saving is also computed by the query rather than subtracted here, so
                    // the three money columns are guaranteed to be consistent with each other.
                    dgvOffers.Columns["YouSave"].HeaderText = "Save (Tk)";
                    dgvOffers.Columns["YouSave"].FillWeight = 44;
                    // The end date is shown as information, not as a filter: the query already
                    // excluded anything that has passed, and this tells the customer how long
                    // they have left to decide.
                    dgvOffers.Columns["EndDate"].HeaderText = "Valid until";
                    // Hidden rather than dropped from the SELECT. The identifier is what Add
                    // to cart and View details act on, so the row must still carry it even
                    // though a customer has no use for the number.
                    dgvOffers.Columns["MedicineId"].Visible = false;
                    // Stock is shown because the query only returns offers on medicines that
                    // are in stock, and the figure lets the customer judge whether the
                    // quantity they want is realistic before trying to add it.
                    dgvOffers.Columns["Stock"].HeaderText = "Stock";
                    dgvOffers.Columns["Stock"].FillWeight = 34;
                }

                // The empty result is given its own sentence, and it names the filters as the
                // likely cause; a bare empty grid would read as a screen that failed to load.
                lblStatus.Text = table.Rows.Count == 0
                    ? "No offer is running today for that combination of filters."
                    // The success wording states the promise the architecture actually makes:
                    // the discount is applied by the query, so it follows the line all the way
                    // to the invoice rather than being re-entered at checkout.
                    : table.Rows.Count + " offer(s) running today. Add one to your cart and the discounted price " +
                      "follows it all the way to the invoice.";

                // Rebinding resets the selection, so the two action buttons are brought back
                // into line with whichever row is current now.
                UpdateButtons();
            }
            catch (Exception ex)
            {
                // Without this catch an unreachable database would close the application on an
                // unhandled exception; with it the customer is told why and the window stays.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Every row on this screen is an offer, so the whole grid is tinted with the same
        /// green the other screens use for a discount. There is no condition to test.
        /// </summary>
        private void dgvOffers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The header row arrives as index -1 and has no DefaultCellStyle to set here.
            if (e.RowIndex < 0) return;
            // Done in CellFormatting rather than in a loop after binding because this fires as
            // each cell is painted, so the tint survives sorting, scrolling and every rebind.
            // No else branch is needed, unlike the other grids, because every row gets the
            // same colour and none can be left holding a previous row's tint.
            dgvOffers.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
        }

        // Moving the selection changes what the buttons would act on, so the same method the
        // load path uses is re-run rather than the states being maintained in two places.
        private void dgvOffers_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            // CurrentRow is null on an empty grid; the cell test additionally rejects a row
            // that exists but is not yet populated, which would make the conversion in
            // SelectedMedicineId throw. MedicineId is used rather than OfferId because it is
            // the medicine, not the offer, that both buttons act on.
            bool hasRow = dgvOffers.CurrentRow != null && dgvOffers.CurrentRow.Cells["MedicineId"].Value != null;
            // Disabled rather than allowed to fail with a message afterwards, so the control
            // itself says what is possible. Stock is not tested here because the query already
            // excludes anything out of stock, so every row on this screen is buyable.
            btnAddToCart.Enabled = hasRow;
            btnViewDetails.Enabled = hasRow;
        }

        /// <summary>
        /// The single place the selected medicine's identifier is read, so both handlers
        /// below agree that 0 means "nothing usable is selected".
        /// </summary>
        private int SelectedMedicineId()
        {
            // 0 is a safe sentinel: MedicineId is an IDENTITY column starting at 1, so no real
            // medicine can carry it and no caller can act on it by accident.
            if (dgvOffers.CurrentRow == null) return 0;
            // Convert rather than an (int) cast, because the cell holds a boxed value whose
            // exact numeric type is the provider's choice and unboxing the wrong one throws.
            return Convert.ToInt32(dgvOffers.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---------------------------------------------------------------------

        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            // Re-read even though the button is disabled without a selection: a keyboard
            // shortcut or a focus change can still reach a handler.
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            int quantity;
            // The shared Validator, so "a whole quantity of one or more" means exactly the
            // same thing here as on the catalogue and the details screens. It rejects text,
            // blanks, zero and negatives in one call.
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))
            {
                MessageBox.Show("Enter a whole quantity of one or more.", "Check the quantity",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // Nothing is written: the basket is left exactly as it was.
                return;
            }

            // The MEDICINE is added, not the offer. There is no offer identifier in the cart
            // at all, because the discount is recalculated from today's offers every time a
            // price is read; storing the offer on the line would freeze a discount that may
            // since have ended.
            string message;
            if (_cart.AddOrIncrease(UserSession.UserId, medicineId, quantity, out message))
            {
                // Both values are read from the current row while it is still valid, and they
                // are used only to word the confirmation below.
                string name = dgvOffers.CurrentRow.Cells["MedicineName"].Value.ToString();
                // ToString rather than a decimal conversion: the figure is only being pasted
                // into a sentence, and the query already rounded it to two decimals.
                string save = dgvOffers.CurrentRow.Cells["YouSave"].Value.ToString();

                // A status line rather than a dialog, so a customer adding several offers in a
                // row is not interrupted each time. The saving is restated per unit, which is
                // the promise this screen exists to make.
                lblStatus.Text = quantity + " x " + name + " added to your cart at the offer price. " +
                                 "You are saving Tk " + save + " per unit.";
                // Reset to 1 so the next add starts from the common case instead of repeating
                // the last quantity against a different medicine.
                txtQuantity.Text = "1";
                // Deliberately no reload here: the offers on screen have not changed, and
                // rebinding would lose the customer's place in the list mid-shop.
            }
            else
            {
                // A refusal is shown as a dialog because the customer asked for something they
                // cannot have and would otherwise assume it worked. The wording comes from the
                // service, next to the rule that produced it.
                MessageBox.Show(message, "Cannot add to cart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnViewDetails_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            // The identifier is passed and the details screen re-reads the medicine itself,
            // rather than being handed this row. That screen needs the description and the
            // reviews, which this query does not return, and it shows the same discounted
            // price because it derives it from the same offers.
            using (MedicineDetailsForm details = new MedicineDetailsForm(medicineId))
            {
                // ShowDialog blocks until it closes, so the reload below runs afterwards, and
                // "this" makes this form the owner so the dialog cannot be lost behind it.
                details.ShowDialog(this);
            }
            // Reloaded because the customer may have added to the basket from in there, which
            // can change the stock figures shown in this grid.
            LoadGrid();
        }

        private void dgvOffers_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // A double click on the header arrives as index -1 and must do nothing.
            if (e.RowIndex < 0) return;
            // Calls the button's handler rather than repeating its body, so the two ways of
            // opening a medicine cannot drift apart. EventArgs.Empty is passed because that
            // handler ignores both of its parameters.
            btnViewDetails_Click(sender, EventArgs.Empty);
        }

        // Both dropdowns share this one handler: LoadGrid re-reads every control each time
        // rather than tracking which one changed, so the two filters combine instead of
        // overwriting one another.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close rather than Dispose: the screen that opened this one used ShowDialog inside a
        // using block, so closing returns control there and the using block disposes it.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
