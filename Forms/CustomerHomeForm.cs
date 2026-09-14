using System.Data;                  // DataTable, the shape MedicineService hands back
using System.Drawing;               // Color and Font for the theme and the row tinting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme and Validator, the shared look and shared checks
using PharmaLinkApp.Models;         // Category and Pharmacy, the items the filters are built from
using PharmaLinkApp.Services;       // the four services; every line of SQL for this screen is in them

// A form reads its controls, calls a service and binds the answer: no SQL.
namespace PharmaLinkApp.Forms
{
    /// <summary>The customer's home screen (requirements 20, 21 and 22).</summary>
    public partial class CustomerHomeForm : Form   // one search box, five filters, one query
    {
        private readonly MedicineService _medicines = new MedicineService();    // the catalogue query
        private readonly CategoryService _categories = new CategoryService();   // read once, for the category list
        private readonly PharmacyService _pharmacies = new PharmacyService();   // read twice, for areas and shops
        private readonly CartService _cart = new CartService();                 // the only one this screen writes through

        // Guard: filling a ComboBox raises its change event and would query early.
        private bool _loading = true;

        // Nothing but the designer call: a failure here would have no window to report on.
        public CustomerHomeForm()
        {
            InitializeComponent();   // creates the controls the designer drew; the rest waits for Load
        }

        // The order below IS the startup design: style, fill, unguard, query once.
        private void CustomerHomeForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();     // appearance only, and it attaches the CellFormatting handler
            LoadFilters();    // fills the five dropdowns; _loading swallows their events
            _loading = false; // the screen is consistent now, so filter changes may query
            LoadGrid();       // one deliberate first query instead of the five the flag stopped
        }

        // Appearance only, pushed through UiTheme so every screen looks like one product.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Browse Medicines");   // page colour, body font and the title bar format

            panelSide.BackColor = UiTheme.Sidebar;         // the dark column all three dashboards share
            lblBrand.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);   // the product name atop the menu
            lblBrand.ForeColor = Color.White;              // white on the dark sidebar, the only readable choice
            lblRole.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);     // tiny and bold, so it reads as a badge
            lblRole.ForeColor = Color.FromArgb(140, 205, 185);   // a desaturated brand green, tied to the product
            lblUserName.Font = UiTheme.FontSmall;          // the smallest size: a name identifies, it does not inform
            lblUserName.ForeColor = Color.FromArgb(190, 205, 216);   // the dark theme's equivalent of TextMuted
            lblUserName.Text = UserSession.FullName;       // read from the static session, never passed in

            // One loop, so a sixth destination cannot be left looking like a system button.
            foreach (Button button in new[] { btnBrowse, btnCart, btnOffers, btnOrders, btnMyAccount })
                UiTheme.StyleSidebarButton(button);        // flat, dark, left aligned, hover colour attached

            btnBrowse.BackColor = UiTheme.SidebarHover;    // the screen we are on

            UiTheme.StyleSidebarButton(btnLogout);         // styled as a menu item first, then overridden
            btnLogout.BackColor = UiTheme.Danger;          // red: the one item here that ends the session
            btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 60, 60);   // a lighter red, as SidebarHover lifts the rest

            panelHeader.BackColor = UiTheme.Primary;       // the green strip every screen opens with
            lblHeaderTitle.Font = UiTheme.FontTitle;       // the screen name, at the size reserved for titles
            lblHeaderTitle.ForeColor = Color.White;        // white on the green header
            lblHeaderSub.Font = UiTheme.FontSmall;         // the line under it, smaller so it cannot compete
            lblHeaderSub.ForeColor = Color.FromArgb(200, 230, 220);   // the pale tint used for every subtitle
            lblCartSummary.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);   // heavier: the one live figure here
            lblCartSummary.ForeColor = Color.White;        // white, so the badge belongs to the header

            // Each button gets the style that matches what it does, never a chosen colour.
            UiTheme.StyleSecondary(btnSearch);             // quiet: the text box already re-queries
            UiTheme.StyleSecondary(btnClearFilters);       // quiet too: resetting can always be redone
            UiTheme.StyleAccent(btnDetails);               // blue, because it only opens another screen
            UiTheme.StylePrimary(btnAddToCart);            // green: the one action that changes stored data
            UiTheme.StyleSecondary(btnOpenCart);           // navigation, so it must not compete with Add
            // The same override SignUpForm uses: a heavier face on the one main control.
            btnAddToCart.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);

            UiTheme.StyleGrid(dgvMedicines);               // read only, full row select, banded rows
            // Wired here, not in the designer, so the handler travels with the two tints.
            dgvMedicines.CellFormatting += dgvMedicines_CellFormatting;

            lblStatus.Font = UiTheme.FontSmall;            // the status and legend line, sized as a caption
            lblStatus.ForeColor = UiTheme.TextMuted;       // muted, so it never looks like an error
        }

        /// <summary>Fills the five dropdowns once, at startup.</summary>
        private void LoadFilters()
        {
            cmbCategory.Items.Add("All categories");   // item 0 is the placeholder, meaning no filter
            // From the database, so a new category appears and a retired one is dropped.
            foreach (Category category in _categories.GetActiveList())
                // "3 - Antibiotics": the id rides in the text, so no parallel list is needed.
                cmbCategory.Items.Add(category.CategoryId + " - " + category.CategoryName);
            cmbCategory.SelectedIndex = 0;   // opens showing everything; _loading swallows the event

            // A fixed list of bands: a band cannot be typed backwards or non-numerically.
            cmbPriceRange.Items.AddRange(new object[]
            {
                "Any price",        // index 0, the placeholder again
                "Under Tk 10",      // index 1  ... the order here is what SelectedPriceRange
                "Tk 10 - 50",       // index 2       switches on, so these two lists must stay
                "Tk 50 - 200",      // index 3       in step; the text itself is never parsed
                "Tk 200 - 1000",    // index 4
                "Over Tk 1000"      // index 5
            });
            cmbPriceRange.SelectedIndex = 0;   // "Any price", so the screen opens unfiltered on price

            cmbArea.Items.Add("All areas");    // the placeholder, so index 0 means no filter here too
            // DISTINCT areas from approved shops only, so every choice can deliver something.
            foreach (string area in _pharmacies.GetAreas(true)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;         // Area is a plain string column, so no id is glued on

            cmbPharmacy.Items.Add("All pharmacies");   // the placeholder, matching the other four
            // Approved shops only: a filter that can only return nothing looks like a fault.
            foreach (Pharmacy pharmacy in _pharmacies.GetApprovedList())
                // The same "id - name" convention as the category list, read back the same way.
                cmbPharmacy.Items.Add(pharmacy.PharmacyId + " - " + pharmacy.PharmacyName);
            cmbPharmacy.SelectedIndex = 0;     // opens on every approved shop, as a marketplace should

            // Two states rather than a checkbox, so all five filters look and behave alike.
            cmbAvailability.Items.AddRange(new object[] { "Any", "In stock" });
            cmbAvailability.SelectedIndex = 1;   // in stock only is the sensible default
        }

        // -- READING THE FILTERS: each control becomes a neutral value the query ignores

        /// <summary>The category id, or 0 when "All categories" is selected.</summary>
        private int SelectedCategoryId()
        {
            // Index 0 is the placeholder and -1 is nothing selected, so one test covers both.
            if (cmbCategory.SelectedIndex <= 0) return 0;
            string text = cmbCategory.SelectedItem.ToString();   // always one of the strings added above
            // Everything before the first space is the id glued on when the list was built.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        /// <summary>The pharmacy id, or 0 when "All pharmacies" is selected.</summary>
        private int SelectedPharmacyId()
        {
            // The same shape as SelectedCategoryId, because both lists share one convention.
            if (cmbPharmacy.SelectedIndex <= 0) return 0;
            string text = cmbPharmacy.SelectedItem.ToString();   // "7 - Mitford Pharmacy", written in LoadFilters
            return int.Parse(text.Substring(0, text.IndexOf(' ')));   // Parse, since this form wrote the string
        }

        /// <summary>Turns the chosen band into two numbers, as one decision.</summary>
        private void SelectedPriceRange(out decimal minPrice, out decimal maxPrice)
        {
            // Switching on the INDEX, so relabelling a band cannot change what it searches.
            switch (cmbPriceRange.SelectedIndex)
            {
                case 1: minPrice = 0m; maxPrice = 10m; break;        // "Under Tk 10" starts at zero
                case 2: minPrice = 10m; maxPrice = 50m; break;       // bands share their edges, so a
                case 3: minPrice = 50m; maxPrice = 200m; break;      // medicine priced exactly at 50
                case 4: minPrice = 200m; maxPrice = 1000m; break;    // appears in both neighbours
                // BETWEEN needs an upper bound, and 999999 is above any real unit price.
                case 5: minPrice = 1000m; maxPrice = 999999m; break;
                default: minPrice = 0m; maxPrice = 0m; break;   // a 0 maximum means "do not filter"
            }
        }

        /// <summary>Counts how many filters are narrowing the result.</summary>
        private int ActiveFilterCount()
        {
            int count = 0;   // counts controls, not conditions: it drives a sentence, not the query
            // IsNullOrWhiteSpace, because a box holding only spaces is not a search.
            if (!string.IsNullOrWhiteSpace(txtSearch.Text)) count++;
            if (cmbCategory.SelectedIndex > 0) count++;      // "> 0" for the four with a placeholder at 0
            if (cmbPriceRange.SelectedIndex > 0) count++;    // any band other than "Any price" narrows
            if (cmbArea.SelectedIndex > 0) count++;          // a chosen area, not the "All areas" item
            if (cmbPharmacy.SelectedIndex > 0) count++;      // one shop chosen out of all approved ones
            // Availability is the exception: its narrowing choice is "In stock" at index 1.
            if (cmbAvailability.SelectedIndex == 1) count++;
            return count;   // read only by the status line, so it can mislead but never change results
        }

        /// <summary>The one read path: every filter and every button ends here.</summary>
        private void LoadGrid()
        {
            if (_loading) return;   // the dropdowns are still being filled, so do not query yet

            // Wrapped, because the one service call inside is the only database reach here.
            try
            {
                // Index 0 is "Any price" and yields max = 0, which the query reads as no filter.
                decimal minPrice, maxPrice;
                SelectedPriceRange(out minPrice, out maxPrice);   // one call fills both, so bands cannot mix

                // The placeholder becomes "", which is the query's own "no filter" signal.
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();

                // One call, seven arguments, five controls; no filtering happens in this form.
                DataTable table = _medicines.SearchForCustomer(
                    txtSearch.Text.Trim(),                    // matched against 3 columns
                    SelectedCategoryId(),                     // 0 = all categories
                    minPrice, maxPrice,                       // maxPrice 0 = any price
                    area,                                     // "" = all areas
                    SelectedPharmacyId(),                     // 0 = all pharmacies
                    cmbAvailability.SelectedIndex == 1);      // true = in stock only

                dgvMedicines.DataSource = table;   // binding creates the columns...
                LabelColumns();                    // ...so renaming them must come after

                // Re-read every load, so the badge cannot drift after an add or a checkout.
                int cartLines = _cart.CountLines(UserSession.UserId);
                lblCartSummary.Text = cartLines == 0 ? "Cart is empty" : "Cart:  " + cartLines + " item(s)";   // a 0 in a badge reads as a failed load

                // Answers the two questions a short result list provokes: how many, and why.
                lblStatus.Text = table.Rows.Count + " medicine(s) found across every approved pharmacy   |   " +
                                 ActiveFilterCount() + " filter(s) active" + Environment.NewLine +   // NewLine, not "\n", for Windows
                                 // The legend explains both row colours, so the tinting explains itself.
                                 "Rx marks a prescription only medicine - you will be asked to upload a doctor's prescription at checkout. " +
                                 "A discount percentage means an offer is running today and the price shown is already the discounted one.";   // "already" is the promise the cart keeps

                UpdateButtons();   // rebinding reset the selection, so the buttons must catch up
            }
            // Exception itself: every failure here is reported and the grid is left as it was.
            catch (Exception ex)
            {
                // Naming the screen separates this from the failure raised when adding to a cart.
                MessageBox.Show("Could not load the catalogue.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // Error: the customer cannot act on it
            }
        }

        /// <summary>Gives the generated columns readable headers and widths.</summary>
        private void LabelColumns()
        {
            // No columns means no schema came back, and every indexer below would throw.
            if (dgvMedicines.Columns.Count == 0) return;

            // The indexer strings are the query's own column names, left unchanged.
            dgvMedicines.Columns["MedicineId"].HeaderText = "ID";
            dgvMedicines.Columns["MedicineId"].FillWeight = 26;   // Fill mode: a share of the width, not pixels
            dgvMedicines.Columns["MedicineName"].HeaderText = "Brand";     // one of the two names a customer arrives with
            dgvMedicines.Columns["GenericName"].HeaderText = "Generic";    // the name on the chit, not the one on the box
            dgvMedicines.Columns["Strength"].HeaderText = "Strength";      // the dose, which separates two identical rows
            dgvMedicines.Columns["Strength"].FillWeight = 42;   // "500 mg" needs very little room
            dgvMedicines.Columns["Manufacturer"].HeaderText = "Made by";   // the third column the keyword searches
            dgvMedicines.Columns["CategoryName"].HeaderText = "Category";  // the NAME: the id only ever filters
            dgvMedicines.Columns["PharmacyName"].HeaderText = "Sold by";   // what makes this a marketplace listing
            dgvMedicines.Columns["Area"].HeaderText = "Area";   // where it ships from, so a near shop can be preferred
            dgvMedicines.Columns["Area"].FillWeight = 42;       // a narrow share: area names are one short word
            dgvMedicines.Columns["UnitPrice"].HeaderText = "List (Tk)";    // the shelf price, not what will be charged
            dgvMedicines.Columns["UnitPrice"].FillWeight = 40;   // a money column is only a handful of digits
            dgvMedicines.Columns["DiscountPercent"].HeaderText = "Off %";   // beside the list price it applies to
            dgvMedicines.Columns["DiscountPercent"].FillWeight = 32;        // narrower still: three digits and a sign
            // Discounted by the query and never recalculated here, so cart and invoice agree.
            dgvMedicines.Columns["PriceYouPay"].HeaderText = "You pay (Tk)";
            dgvMedicines.Columns["PriceYouPay"].FillWeight = 50;   // the widest money column: the one that matters
            dgvMedicines.Columns["Stock"].HeaderText = "Stock";   // on screen because UpdateButtons reads it
            dgvMedicines.Columns["Stock"].FillWeight = 32;        // a small count, so a small share
            dgvMedicines.Columns["RequiresRx"].HeaderText = "Rx";   // two letters: the status line carries the legend
            dgvMedicines.Columns["RequiresRx"].FillWeight = 24;   // a checkbox column, so very narrow
            dgvMedicines.Columns["ExpiryDate"].HeaderText = "Expires";   // shown, not filtered: the query excludes expired
        }

        /// <summary>Discounted rows tint green, prescription only rows amber.</summary>
        private void dgvMedicines_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Fires per cell as it paints, so the tint follows the data through any sort.
            if (e.RowIndex < 0 || dgvMedicines.Columns.Count == 0) return;

            // From the event argument, because this runs for every visible row.
            DataGridViewRow row = dgvMedicines.Rows[e.RowIndex];
            object discount = row.Cells["DiscountPercent"].Value;   // read from the cell, so sorting cannot mismatch it
            object rx = row.Cells["RequiresRx"].Value;   // by the query's column name, which LabelColumns left alone

            // Null before binding finishes, DBNull when unset, and only then safe to convert.
            bool discounted = discount != null && discount != DBNull.Value && Convert.ToDecimal(discount) > 0m;
            bool needsRx = rx != null && rx != DBNull.Value && Convert.ToBoolean(rx);   // the same three tests

            // A discount is the more useful thing to see, so it wins when a row is both.
            if (discounted) row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            else if (needsRx) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 232);   // the amber tint
            // Required, not tidiness: scrolling reuses rows, which would keep old tints.
            else row.DefaultCellStyle.BackColor = Color.White;
        }

        // Moving the selection changes what the buttons act on, so this re-runs.
        private void dgvMedicines_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        /// <summary>Keeps the two action buttons honest about the current row.</summary>
        private void UpdateButtons()
        {
            DataGridViewRow row = dgvMedicines.CurrentRow;   // full row select, so this is the row meant
            // The cell test catches a row that exists but is not populated yet.
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            // Short circuit order matters: hasRow must be true before the cell is read.
            bool inStock = hasRow && Convert.ToInt32(row.Cells["Stock"].Value) > 0;

            btnDetails.Enabled = hasRow;   // details are worth reading even for something out of stock
            btnAddToCart.Enabled = hasRow && inStock;   // refused up front; the service would refuse it too
            btnAddToCart.Text = hasRow && !inStock ? "Out of stock" : "Add to cart";   // the caption gives the reason
        }

        /// <summary>Reads the selected medicine's id; 0 means nothing usable.</summary>
        private int SelectedMedicineId()
        {
            // 0 is a safe sentinel: MedicineId is an IDENTITY column that starts at 1.
            if (dgvMedicines.CurrentRow == null) return 0;
            // Convert, not a cast: the cell holds a boxed value of the provider's own type.
            return Convert.ToInt32(dgvMedicines.CurrentRow.Cells["MedicineId"].Value);
        }

        // -- ACTIONS

        // Adds the selected medicine to the basket; the service decides whether it may.
        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            // Re-read the selection: a keyboard shortcut can still reach this handler.
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;   // nothing usable is selected, so nothing to add

            int quantity;   // filled by the Validator call below, and only read if it returns true
            // The shared Validator, so "one or more" means the same on every basket screen.
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))
            {
                // Said plainly, because the box accepts text and the rule is not obvious.
                MessageBox.Show("Enter a whole quantity of one or more.", "Check the quantity",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);   // Warning: the customer can fix this
                txtQuantity.Focus();       // the caret goes back into the offending box
                txtQuantity.SelectAll();   // with the bad value highlighted, so one keystroke fixes it
                return;                    // the basket is left untouched
            }

            // The service decides: the grid's stock figure may already be seconds old.
            string message;
            if (_cart.AddOrIncrease(UserSession.UserId, medicineId, quantity, out message))   // out message carries any refusal
            {
                // Read before the reload below rebinds the grid and invalidates this row.
                string name = dgvMedicines.CurrentRow.Cells["MedicineName"].Value.ToString();
                // A status line, not a dialog: a modal box on every add would be tedious.
                lblStatus.Text = quantity + " x " + name + " added to your cart. " +
                                 "Adding the same medicine again increases the quantity instead of creating a second line, " +   // one line per medicine
                                 "which is what the UNIQUE constraint on (CustomerId, MedicineId) guarantees.";   // the database enforces that, not this form
                txtQuantity.Text = "1";   // back to the common case for the next medicine
                // Reloaded to refresh the cart badge, and the stock figures come back with it.
                LoadGrid();
            }
            // The refusal path: the customer asked for something they cannot have.
            else
            {
                // The wording comes from the service, next to the rule that produced it.
                MessageBox.Show(message, "Cannot add to cart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Opens the details screen for the selected medicine, then refreshes this one.
        private void btnDetails_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();   // 0 when the grid is empty or unbound
            if (medicineId == 0) return;             // nothing selected, so nothing to open

            // Only the id is passed: the details screen re-reads the medicine for itself.
            using (MedicineDetailsForm details = new MedicineDetailsForm(medicineId))
            {
                details.ShowDialog(this);   // modal, and "this" keeps the dialog in front
            }
            // The customer may have added to the basket in there, so nothing is assumed.
            LoadGrid();
        }

        // Double clicking a row is a second way into the details screen.
        private void dgvMedicines_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // a double click on the header arrives as -1
            // Calls the button's handler rather than repeating it, so the two cannot drift.
            btnDetails_Click(sender, EventArgs.Empty);
        }

        // -- NAVIGATION

        /// <summary>Shows a child modally, disposes it, then refreshes here.</summary>
        private void OpenChild(Form child)
        {
            // using on the parameter disposes the form the caller constructed.
            using (child)
            {
                child.ShowDialog(this);   // modal, so the reload below runs after it closes
            }
            // The badge and the stock figures may both have moved while the child was open.
            LoadGrid();
        }

        // Browse is the screen we are already on, so this doubles as a refresh button.
        private void btnBrowse_Click(object sender, EventArgs e) => LoadGrid();
        private void btnCart_Click(object sender, EventArgs e) => OpenChild(new CartForm());   // disposed inside OpenChild
        private void btnOffers_Click(object sender, EventArgs e) => OpenChild(new CustomerOffersForm());   // today's running offers
        private void btnOrders_Click(object sender, EventArgs e) => OpenChild(new OrderHistoryForm());   // past orders and tracking
        private void btnMyAccount_Click(object sender, EventArgs e) => OpenChild(new MyProfileForm());   // profile and password
        // The text box already re-queries; this button is for people who expect one.
        private void btnSearch_Click(object sender, EventArgs e) => LoadGrid();
        // All five dropdowns share this: LoadGrid re-reads every control each time.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();

        // Resets every filter to its placeholder and re-queries exactly once.
        private void btnClearFilters_Click(object sender, EventArgs e)
        {
            _loading = true;   // the same guard as startup: six resets would fire six queries
            txtSearch.Clear();                    // the keyword box, which is not a dropdown
            cmbCategory.SelectedIndex = 0;        // back to every placeholder, meaning no filter
            cmbPriceRange.SelectedIndex = 0;      // "Any price"
            cmbArea.SelectedIndex = 0;            // "All areas"
            cmbPharmacy.SelectedIndex = 0;        // "All pharmacies"
            // "Any", not the "In stock" default: clearing has to mean showing everything.
            cmbAvailability.SelectedIndex = 0;
            _loading = false;   // the controls agree again, so a query is safe
            LoadGrid();         // one query for all six changes, run deliberately
        }

        // Logging out by accident costs the customer their place, so confirm first.
        private void btnLogout_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show("Log out of PharmaLink?", "Log out",   // body, then caption
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // Question: nothing has gone wrong

            // Tested for Yes, so dismissing the dialog any other way means no.
            if (answer == DialogResult.Yes)
            {
                // Cleared BEFORE the window closes: UserSession is static and outlives the form.
                UserSession.Clear();
                Close();   // closing this form returns to the login window that opened it
            }
        }
    }
}
