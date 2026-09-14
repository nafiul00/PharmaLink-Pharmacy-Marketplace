using System.Data;                  // DataTable, the shape MedicineService hands back
using System.Drawing;               // Color and Font for the theme and the row tinting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme and Validator, the shared look and the shared checks
using PharmaLinkApp.Models;         // Category and Pharmacy, the typed items the filters are built from
using PharmaLinkApp.Services;       // the four services; every line of SQL in this screen lives in them

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// The customer's home screen (requirements 20, 21 and 22).
    ///
    /// One search box over three columns - brand name, generic name and
    /// manufacturer - so a patient holding a doctor's chit that says
    /// "paracetamol" finds Napa and Ace Plus even though neither brand contains
    /// that word. Five ComboBox filters narrow the result: category, price
    /// range, area, pharmacy and availability. They are combined in a single
    /// query, and only medicines belonging to an Approved pharmacy are ever
    /// returned.
    /// </summary>
    public partial class CustomerHomeForm : Form
    {
        // Four services, one per subject the screen deals with, each created once with the
        // form. The split is deliberate: the catalogue query lives in MedicineService, the
        // two dropdown sources in CategoryService and PharmacyService, and the basket in
        // CartService. No SQL of any kind appears in this file. The form's whole job is to
        // read its controls, call a service and bind whatever comes back, which is why the
        // filtering below can be described without reading a single query.
        private readonly MedicineService _medicines = new MedicineService();
        private readonly CategoryService _categories = new CategoryService();
        private readonly PharmacyService _pharmacies = new PharmacyService();
        private readonly CartService _cart = new CartService();

        // Guard against the dropdowns firing while they are still being filled. Every
        // ComboBox on this screen has SelectedIndexChanged wired to Filter_Changed, and
        // setting SelectedIndex in code raises that event exactly as a click does. Without
        // this flag, opening the screen would run the catalogue query five times against
        // half-populated filters. It starts true so nothing can query before Load says so.
        private bool _loading = true;

        public CustomerHomeForm()
        {
            // Creates the controls the designer drew. Everything else waits for Load,
            // because none of the fields below exist until this has run.
            InitializeComponent();
        }

        private void CustomerHomeForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();     // appearance only, and it attaches the CellFormatting handler
            LoadFilters();    // fills the five dropdowns; the _loading flag suppresses its events
            _loading = false; // the screen is now consistent, so filter changes may query
            LoadGrid();       // one deliberate first query instead of the five the flag prevented
        }

        // Appearance only. Colours, fonts and button styles are pushed through UiTheme so
        // every screen in the application looks like the same product.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Browse Medicines");

            panelSide.BackColor = UiTheme.Sidebar;
            lblBrand.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
            lblBrand.ForeColor = Color.White;
            lblRole.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            lblRole.ForeColor = Color.FromArgb(140, 205, 185);
            lblUserName.Font = UiTheme.FontSmall;
            lblUserName.ForeColor = Color.FromArgb(190, 205, 216);
            lblUserName.Text = UserSession.FullName;

            foreach (Button button in new[] { btnBrowse, btnCart, btnOffers, btnOrders, btnMyAccount })
                UiTheme.StyleSidebarButton(button);

            btnBrowse.BackColor = UiTheme.SidebarHover;   // the screen we are on

            UiTheme.StyleSidebarButton(btnLogout);
            btnLogout.BackColor = UiTheme.Danger;
            btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 60, 60);

            panelHeader.BackColor = UiTheme.Primary;
            lblHeaderTitle.Font = UiTheme.FontTitle;
            lblHeaderTitle.ForeColor = Color.White;
            lblHeaderSub.Font = UiTheme.FontSmall;
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);
            lblCartSummary.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            lblCartSummary.ForeColor = Color.White;

            UiTheme.StyleSecondary(btnSearch);
            UiTheme.StyleSecondary(btnClearFilters);
            UiTheme.StyleAccent(btnDetails);
            UiTheme.StylePrimary(btnAddToCart);
            UiTheme.StyleSecondary(btnOpenCart);
            btnAddToCart.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);

            UiTheme.StyleGrid(dgvMedicines);
            dgvMedicines.CellFormatting += dgvMedicines_CellFormatting;

            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;
        }

        /// <summary>
        /// Fills the five dropdowns once, at startup. Two of them are built from the
        /// database so they can never offer a choice that returns nothing, and three are
        /// fixed lists because they describe bands and states rather than stored rows.
        /// </summary>
        private void LoadFilters()
        {
            // Item 0 is a placeholder that means "no filter". Putting it in the list rather
            // than adding a separate "use this filter" checkbox is what keeps the control
            // count down: one ComboBox expresses both the on and the off state.
            cmbCategory.Items.Add("All categories");
            // Only active categories, and the list comes from the database rather than being
            // typed in here, so a category added by an administrator appears without a
            // rebuild and a retired one stops being offered.
            foreach (Category category in _categories.GetActiveList())
                // "3 - Antibiotics": the identifier is carried in the display text so the
                // selection can be turned back into a number without a parallel lookup list.
                // The alternative, binding with ValueMember, would mean every SelectedItem
                // read below returning a Category object instead of a simple string.
                cmbCategory.Items.Add(category.CategoryId + " - " + category.CategoryName);
            // Selecting the placeholder is what makes the screen open showing everything.
            // This assignment raises SelectedIndexChanged, which _loading is swallowing.
            cmbCategory.SelectedIndex = 0;

            // Price is a fixed list of bands, not a pair of typed boxes. A band cannot be
            // entered backwards or non-numerically, so there is no input to validate and no
            // way to ask for a minimum above the maximum.
            cmbPriceRange.Items.AddRange(new object[]
            {
                "Any price",        // index 0, the placeholder again
                "Under Tk 10",      // index 1  ... the order here is what SelectedPriceRange
                "Tk 10 - 50",       // index 2       switches on, so these two lists must stay
                "Tk 50 - 200",      // index 3       in step; the text itself is never parsed
                "Tk 200 - 1000",    // index 4
                "Over Tk 1000"      // index 5
            });
            cmbPriceRange.SelectedIndex = 0;

            cmbArea.Items.Add("All areas");
            // Areas are read as DISTINCT values from the approved pharmacies, so the list is
            // exactly the set of places something can actually be delivered from. The true
            // argument is approvedOnly: an area served only by a pending shop is not offered.
            foreach (string area in _pharmacies.GetAreas(true)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;

            cmbPharmacy.Items.Add("All pharmacies");
            // Approved shops only, for the same reason: a filter that can return an empty
            // grid no matter what else is chosen would look like a fault in the search.
            foreach (Pharmacy pharmacy in _pharmacies.GetApprovedList())
                // Same "id - name" convention as the category list, read back the same way.
                cmbPharmacy.Items.Add(pharmacy.PharmacyId + " - " + pharmacy.PharmacyName);
            cmbPharmacy.SelectedIndex = 0;

            // Two states rather than a checkbox, so all five filters look and behave alike.
            cmbAvailability.Items.AddRange(new object[] { "Any", "In stock" });
            cmbAvailability.SelectedIndex = 1;   // in stock only is the sensible default
        }

        // ---------------------------------------------------------------------
        //  READING THE FILTERS
        // ---------------------------------------------------------------------
        //
        //  Each method below turns one control into a plain value that means "no filter"
        //  when the customer has chosen nothing: 0 for the two identifiers, an empty
        //  string for the area, a maximum of 0 for the price. The query is written so
        //  that each of those neutral values switches its own condition off, which is why
        //  five controls can be combined into a single SELECT instead of the application
        //  assembling different SQL for each of the thirty-two possible combinations.

        /// <summary>The category identifier, or 0 when "All categories" is selected.</summary>
        private int SelectedCategoryId()
        {
            // Index 0 is the placeholder and -1 means nothing is selected at all, so one
            // test covers both and returns the neutral value.
            if (cmbCategory.SelectedIndex <= 0) return 0;
            // SelectedItem is typed object because a ComboBox can hold anything; here it is
            // always one of the strings added above.
            string text = cmbCategory.SelectedItem.ToString();
            // Everything before the first space is the identifier that was glued on when the
            // list was built. int.Parse is safe rather than optimistic here because this form
            // wrote the string itself a moment ago in LoadFilters, so its shape is known.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        /// <summary>The pharmacy identifier, or 0 when "All pharmacies" is selected.</summary>
        private int SelectedPharmacyId()
        {
            // Deliberately identical in shape to SelectedCategoryId. The two lists were built
            // to the same convention, so they are read back by the same rule.
            if (cmbPharmacy.SelectedIndex <= 0) return 0;
            string text = cmbPharmacy.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        /// <summary>
        /// Turns the chosen band into two numbers. Two out parameters rather than a return
        /// value because a range is one decision that produces two figures, and splitting it
        /// into two methods would allow a minimum from one band to be paired with a maximum
        /// from another.
        /// </summary>
        private void SelectedPriceRange(out decimal minPrice, out decimal maxPrice)
        {
            // Switching on the INDEX, not on the text. Reparsing "Tk 200 - 1000" would make
            // the label wording load bearing, so relabelling a band for clarity would silently
            // change what it searches for.
            switch (cmbPriceRange.SelectedIndex)
            {
                case 1: minPrice = 0m; maxPrice = 10m; break;        // "Under Tk 10" starts at zero
                case 2: minPrice = 10m; maxPrice = 50m; break;       // bands share their edges, so a
                case 3: minPrice = 50m; maxPrice = 200m; break;      // medicine priced exactly at 50
                case 4: minPrice = 200m; maxPrice = 1000m; break;    // appears in both neighbours
                // "Over Tk 1000" still needs an upper bound because the query uses BETWEEN;
                // 999999 is far above any realistic unit price and keeps the comparison simple.
                case 5: minPrice = 1000m; maxPrice = 999999m; break;
                default: minPrice = 0m; maxPrice = 0m; break;   // 0 max means "do not filter"
            }
            // Both parameters are assigned on every path, which the compiler insists on for
            // an out parameter; that is what guarantees the caller never reads a stale value.
        }

        /// <summary>
        /// Counts how many filters are actually narrowing the result, purely so the status
        /// line can say so. A customer who sees an unexpectedly short list can tell at a
        /// glance whether a forgotten filter is the reason.
        /// </summary>
        private int ActiveFilterCount()
        {
            int count = 0;
            // IsNullOrWhiteSpace rather than a length test, because a box holding only spaces
            // is not a search and would otherwise be counted as an active filter.
            if (!string.IsNullOrWhiteSpace(txtSearch.Text)) count++;
            // "> 0" for the four that use index 0 as their placeholder.
            if (cmbCategory.SelectedIndex > 0) count++;
            if (cmbPriceRange.SelectedIndex > 0) count++;
            if (cmbArea.SelectedIndex > 0) count++;
            if (cmbPharmacy.SelectedIndex > 0) count++;
            // Availability is the exception: its placeholder is "Any" at index 0 and the
            // narrowing choice is "In stock" at index 1, so this one tests for equality.
            if (cmbAvailability.SelectedIndex == 1) count++;
            return count;
        }

        // ---------------------------------------------------------------------

        /// <summary>
        /// The one read path. Every filter, the search button and the sidebar all end here,
        /// so there is a single place where the controls are turned into arguments and a
        /// single place where the grid is filled.
        /// </summary>
        private void LoadGrid()
        {
            // The guard described at the field: while the dropdowns are being populated their
            // change events fire, and running the catalogue query against a half built filter
            // would be wasted work at best and a wrong result at worst.
            if (_loading) return;

            try
            {
                // Turn the chosen band ("Tk 10 - 50") into two numbers. Index 0 is
                // "Any price" and yields max = 0, which the query reads as "do not
                // filter on price at all".
                decimal minPrice, maxPrice;
                SelectedPriceRange(out minPrice, out maxPrice);

                // Index 0 is the "All areas" placeholder, so it becomes an empty string
                // rather than the literal word "All areas" being sent to SQL as a value
                // to match. Empty is the query's "no filter" signal.
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();

                // ONE call, seven arguments, five controls. Everything the user chose is
                // converted to a neutral value here and combined inside a single query -
                // no filtering happens in this form and nothing is filtered in the grid.
                //
                // The query pairs each argument with a condition of the form
                //     (@Param = <neutral value> OR <real condition>)
                // so a neutral argument makes the left side true, the OR short circuits and
                // that condition does nothing. Two consequences worth stating plainly: the
                // application never builds SQL text by concatenation, so nothing the customer
                // types can change the shape of the statement; and the three safety rules the
                // query always applies - active medicine, approved pharmacy, unexpired stock -
                // sit outside this optional set and cannot be switched off from any control.
                DataTable table = _medicines.SearchForCustomer(
                    txtSearch.Text.Trim(),                    // matched against 3 columns
                    SelectedCategoryId(),                     // 0 = all categories
                    minPrice, maxPrice,                       // maxPrice 0 = any price
                    area,                                     // "" = all areas
                    SelectedPharmacyId(),                     // 0 = all pharmacies
                    cmbAvailability.SelectedIndex == 1);      // true = in stock only

                dgvMedicines.DataSource = table;   // binding creates the columns...
                LabelColumns();                    // ...so renaming them must come after

                // The basket count is re-read on every load so the header badge cannot drift
                // after an add, a removal made on another screen, or a checkout.
                int cartLines = _cart.CountLines(UserSession.UserId);
                // "Cart is empty" rather than "Cart: 0 item(s)", because a zero in a badge
                // reads as a number that failed to load.
                lblCartSummary.Text = cartLines == 0 ? "Cart is empty" : "Cart:  " + cartLines + " item(s)";

                // The status line answers the two questions a short result list provokes:
                // how many rows came back, and how many filters produced them.
                lblStatus.Text = table.Rows.Count + " medicine(s) found across every approved pharmacy   |   " +
                                 ActiveFilterCount() + " filter(s) active" + Environment.NewLine +
                                 // The legend explains both colours the grid uses, so the tinting
                                 // below is self-describing rather than decorative.
                                 "Rx marks a prescription only medicine - you will be asked to upload a doctor's prescription at checkout. " +
                                 "A discount percentage means an offer is running today and the price shown is already the discounted one.";

                // Rebinding resets the selection, so the buttons have to be brought back into
                // line with whichever row is current now.
                UpdateButtons();
            }
            catch (Exception ex)
            {
                // The catalogue is the first thing this screen does, so a database that is
                // unreachable surfaces here. Naming the screen in the message distinguishes
                // this failure from the one raised when adding to the basket.
                MessageBox.Show("Could not load the catalogue.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Gives the automatically generated columns readable headers and sensible widths.
        /// It is a separate method only because it is long; it must always run immediately
        /// after the DataSource assignment that created those columns.
        /// </summary>
        private void LabelColumns()
        {
            // Nothing to label when the grid has no columns, which happens if the query
            // returned no schema. Without this guard every indexer below would throw.
            if (dgvMedicines.Columns.Count == 0) return;

            // Each indexer string is the DataTable column name, which is the name or alias
            // from the SELECT list. HeaderText changes only what is printed at the top of the
            // column; the underlying name stays as the query gave it, which is what lets the
            // handlers below keep looking cells up by "MedicineId" and "Stock".
            dgvMedicines.Columns["MedicineId"].HeaderText = "ID";
            // StyleGrid put the grid in Fill mode, so FillWeight is a share of the width
            // rather than a pixel count. An identifier needs the smallest share on the row.
            dgvMedicines.Columns["MedicineId"].FillWeight = 26;
            // Brand and generic are the two names a customer might arrive with, so both are
            // shown; they are left on the default weight because names are the longest values.
            dgvMedicines.Columns["MedicineName"].HeaderText = "Brand";
            dgvMedicines.Columns["GenericName"].HeaderText = "Generic";
            dgvMedicines.Columns["Strength"].HeaderText = "Strength";
            dgvMedicines.Columns["Strength"].FillWeight = 42;   // "500 mg" needs very little room
            // The third column the keyword searches, shown so a manufacturer match does not
            // look like an unexplained result.
            dgvMedicines.Columns["Manufacturer"].HeaderText = "Made by";
            dgvMedicines.Columns["CategoryName"].HeaderText = "Category";
            // "Sold by" and "Area" are what make this a marketplace listing rather than a
            // single shop's stock list, and they match the two filters above the grid.
            dgvMedicines.Columns["PharmacyName"].HeaderText = "Sold by";
            dgvMedicines.Columns["Area"].HeaderText = "Area";
            dgvMedicines.Columns["Area"].FillWeight = 42;
            // The shelf price, labelled "List" so it is clearly not the price that will be
            // charged when the next column shows a discount.
            dgvMedicines.Columns["UnitPrice"].HeaderText = "List (Tk)";
            dgvMedicines.Columns["UnitPrice"].FillWeight = 40;
            dgvMedicines.Columns["DiscountPercent"].HeaderText = "Off %";
            dgvMedicines.Columns["DiscountPercent"].FillWeight = 32;
            // Already discounted by the query, never recalculated in this form, which is why
            // this figure can be promised to match the cart and the invoice.
            dgvMedicines.Columns["PriceYouPay"].HeaderText = "You pay (Tk)";
            dgvMedicines.Columns["PriceYouPay"].FillWeight = 50;
            // Stock is on screen because UpdateButtons reads it to decide whether Add to cart
            // is offered; hiding it would leave a disabled button with no visible reason.
            dgvMedicines.Columns["Stock"].HeaderText = "Stock";
            dgvMedicines.Columns["Stock"].FillWeight = 32;
            dgvMedicines.Columns["RequiresRx"].HeaderText = "Rx";
            dgvMedicines.Columns["RequiresRx"].FillWeight = 24;   // a checkbox column, so very narrow
            // Expiry is shown rather than filtered on by this form: the query already excludes
            // anything expired, and the date is here so the customer can judge shelf life.
            dgvMedicines.Columns["ExpiryDate"].HeaderText = "Expires";
        }

        /// <summary>Discounted rows are tinted green and prescription only rows amber.</summary>
        private void dgvMedicines_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // CellFormatting fires for each cell as it is about to be painted, which is why
            // the colouring is done here rather than in a loop after binding: it runs again
            // after every sort, scroll and rebind, so the tint follows the row's data instead
            // of its position. The guard rejects the header, which arrives as index -1, and
            // the moment during rebinding when the grid has no columns to look up.
            if (e.RowIndex < 0 || dgvMedicines.Columns.Count == 0) return;

            // The row comes from the event argument because this runs for every visible row,
            // not for the selected one.
            DataGridViewRow row = dgvMedicines.Rows[e.RowIndex];
            // Read from the cells rather than the DataTable so sorting cannot put the colour
            // on the wrong row. Both are object, since a cell holds whatever the column holds.
            object discount = row.Cells["DiscountPercent"].Value;
            object rx = row.Cells["RequiresRx"].Value;

            // Three tests each, in order: a cell can be null before the row is fully bound,
            // DBNull when the database had no value, and only then is it safe to convert.
            // Convert.ToDecimal on DBNull throws, so the checks are not decoration.
            bool discounted = discount != null && discount != DBNull.Value && Convert.ToDecimal(discount) > 0m;
            bool needsRx = rx != null && rx != DBNull.Value && Convert.ToBoolean(rx);

            // A discount is the more useful thing to see, so it wins when a medicine is both
            // discounted and prescription only. The status line explains both colours.
            if (discounted) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            else if (needsRx) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 232);
            // The final else is required, not tidiness: DataGridView reuses row objects while
            // scrolling, so a row left unset would inherit the colour of the row that last
            // used it and the tint would appear to move around the grid.
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        // Moving the selection changes what the buttons would act on, so the same method the
        // load path uses is re-run rather than the enabled states being set in two places.
        private void dgvMedicines_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        /// <summary>
        /// Keeps the two action buttons honest about what can be done with the current row.
        /// </summary>
        private void UpdateButtons()
        {
            // The grid is single select with full row selection, so CurrentRow is always the
            // row the customer means; it is null only when the grid is empty.
            DataGridViewRow row = dgvMedicines.CurrentRow;
            // The cell test catches a row that exists but is not yet populated, which would
            // otherwise let SelectedMedicineId convert a null and throw.
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            // Short circuit order matters: hasRow has to be true before the cell is read, or
            // the conversion runs against a null row on an empty grid.
            bool inStock = hasRow && Convert.ToInt32(row.Cells["Stock"].Value) > 0;

            // Details works for anything, including something out of stock, because the
            // reviews and the expiry date are still worth reading.
            btnDetails.Enabled = hasRow;
            // Adding is refused up front. The service would refuse it again anyway, but the
            // disabled button explains the situation before the click rather than after it.
            btnAddToCart.Enabled = hasRow && inStock;
            // The caption carries the reason, since a disabled button with its usual label
            // looks like a fault rather than a deliberate state.
            btnAddToCart.Text = hasRow && !inStock ? "Out of stock" : "Add to cart";
        }

        /// <summary>
        /// The single place the selected medicine's identifier is read, so every handler
        /// below agrees that 0 means "nothing usable is selected".
        /// </summary>
        private int SelectedMedicineId()
        {
            // 0 is a safe sentinel: MedicineId is an IDENTITY column beginning at 1, so no
            // real medicine can carry it and no caller can act on it by mistake.
            if (dgvMedicines.CurrentRow == null) return 0;
            // Convert rather than an (int) cast, because the cell holds a boxed value whose
            // exact numeric type is the provider's choice and unboxing the wrong one throws.
            return Convert.ToInt32(dgvMedicines.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---------------------------------------------------------------------
        //  ACTIONS
        // ---------------------------------------------------------------------

        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            // Re-read the selection even though the button is disabled without one: a
            // keyboard shortcut or a focus change can still reach a handler.
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            int quantity;
            // The shared Validator rather than a local int.TryParse, so "a whole number of
            // one or more" means exactly the same thing on every screen that adds to a
            // basket. It returns false for text, for empty input, for zero and for negatives.
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))
            {
                MessageBox.Show("Enter a whole quantity of one or more.", "Check the quantity",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // Focus then SelectAll puts the caret in the offending box with the bad value
                // highlighted, so correcting it is one keystroke rather than a delete and a
                // retype. Returning here leaves the basket untouched.
                txtQuantity.Focus();
                txtQuantity.SelectAll();
                return;
            }

            // The service decides. This form does not check the stock itself, because the
            // number in the grid may be seconds old and another customer's checkout may
            // already have taken those units; AddOrIncrease re-reads the shelf as it writes.
            string message;
            if (_cart.AddOrIncrease(UserSession.UserId, medicineId, quantity, out message))
            {
                // Read before the reload below rebinds the grid and invalidates this row.
                string name = dgvMedicines.CurrentRow.Cells["MedicineName"].Value.ToString();
                // A status line rather than a dialog for success: nothing needs acknowledging,
                // and a modal box on every add would make repeated shopping tedious.
                lblStatus.Text = quantity + " x " + name + " added to your cart. " +
                                 "Adding the same medicine again increases the quantity instead of creating a second line, " +
                                 "which is what the UNIQUE constraint on (CustomerId, MedicineId) guarantees.";
                // Reset to 1 so the next add starts from the common case instead of repeating
                // the last quantity against a different medicine.
                txtQuantity.Text = "1";
                // Reloaded to refresh the cart badge in the header, which is read inside
                // LoadGrid; the stock figures come back fresh in the same round trip.
                LoadGrid();
            }
            else
            {
                // A refusal is shown as a dialog because the customer asked for something
                // they cannot have and would otherwise assume it worked. The wording comes
                // from the service, next to the rule that produced it, rather than being
                // guessed at here from a false return value.
                MessageBox.Show(message, "Cannot add to cart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnDetails_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            // The identifier is passed to the constructor and the details screen re-reads the
            // medicine for itself. Handing over the grid row instead would give that screen a
            // snapshot that is already ageing, and it needs the description and the reviews
            // which this query does not return anyway.
            using (MedicineDetailsForm details = new MedicineDetailsForm(medicineId))
            {
                // ShowDialog blocks until it closes, so the reload below runs afterwards; the
                // "this" argument makes this form the owner so the dialog stays in front.
                details.ShowDialog(this);
            }
            // The customer may have added to the basket from in there, so the badge and the
            // stock column are refreshed rather than assumed unchanged.
            LoadGrid();
        }

        private void dgvMedicines_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // Double clicking a header would arrive with index -1 and must do nothing.
            if (e.RowIndex < 0) return;
            // Deliberately calls the button's handler rather than repeating its body, so the
            // two ways of opening a medicine can never drift apart. EventArgs.Empty is passed
            // because that handler ignores both of its parameters.
            btnDetails_Click(sender, EventArgs.Empty);
        }

        // ---------------------------------------------------------------------
        //  NAVIGATION
        // ---------------------------------------------------------------------

        /// <summary>
        /// One helper for every sidebar destination: show the child modally, dispose it, then
        /// refresh this screen. Written once because all four children can change something
        /// this screen displays, and repeating the pattern four times invites one of them to
        /// be left without the reload.
        /// </summary>
        private void OpenChild(Form child)
        {
            // using on the parameter disposes the form the caller constructed, so each handler
            // below is a single expression and no window handle is leaked when it closes.
            using (child)
            {
                child.ShowDialog(this);
            }
            // The cart badge and the stock figures may both have moved while the child was
            // open, so the screen is re-read rather than left as it was.
            LoadGrid();
        }

        // Browse is the screen we are already on, so it simply re-runs the query, which
        // doubles as a refresh button.
        private void btnBrowse_Click(object sender, EventArgs e) => LoadGrid();
        // Each of these constructs its child and hands it to the helper above; the
        // constructed form is disposed there, which is why none of them uses its own using.
        private void btnCart_Click(object sender, EventArgs e) => OpenChild(new CartForm());
        private void btnOffers_Click(object sender, EventArgs e) => OpenChild(new CustomerOffersForm());
        private void btnOrders_Click(object sender, EventArgs e) => OpenChild(new OrderHistoryForm());
        private void btnMyAccount_Click(object sender, EventArgs e) => OpenChild(new MyProfileForm());
        // The Search button exists for people who expect one; the text box already re-queries
        // on TextChanged, so both paths end in the same single method.
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();
        // All five dropdowns share this one handler. They do not need separate ones because
        // LoadGrid reads every control each time rather than tracking which one changed, so
        // the filters combine instead of overwriting one another.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();

        private void btnClearFilters_Click(object sender, EventArgs e)
        {
            // The same guard the startup path uses, for the same reason. Without it the six
            // resets below would each raise their change event and run six catalogue queries,
            // five of them against a partly cleared set of filters.
            _loading = true;
            txtSearch.Clear();
            cmbCategory.SelectedIndex = 0;        // back to every placeholder, meaning no filter
            cmbPriceRange.SelectedIndex = 0;
            cmbArea.SelectedIndex = 0;
            cmbPharmacy.SelectedIndex = 0;
            // Availability is reset to "Any", not to the "In stock" default it opened with:
            // clearing the filters has to mean showing everything, including what is out of
            // stock, or the button would not have done what it says.
            cmbAvailability.SelectedIndex = 0;
            _loading = false;
            // One query for the six changes, run deliberately now that the controls agree.
            LoadGrid();
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            // Logging out by accident costs the customer their place in the catalogue, so it
            // is confirmed first.
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Tested for Yes specifically, so dismissing the dialog any other way means no.
            if (answer == DialogResult.Yes)
            {
                // The session is cleared BEFORE the window closes. UserSession is static and
                // lives as long as the process, so leaving the identity in place would let the
                // next screen opened in the same run act as the customer who just left.
                UserSession.Clear();
                // Closing this form returns to the login window that opened it.
                Close();
            }
        }
    }
}
