using System.Data;                  // DataTable, the shape OfferService hands back
using System.Drawing;               // Color and Font for the theme and the row tinting
using System.Windows.Forms;         // Form, DataGridView, ComboBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme and Validator, the shared look and checks
using PharmaLinkApp.Models;         // Category, the typed item the filter is built from
using PharmaLinkApp.Services;       // the four services; every query lives in them

// Presentation layer: PharmaLinkApp.Database is not imported, so no SQL is here.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 29. Every offer that is actually running today.</summary>
    public partial class CustomerOffersForm : Form // The database excludes expired ones.
    {
        // One service per subject, which is why this file contains no SQL at all.
        private readonly OfferService _offers = new OfferService();
        private readonly CategoryService _categories = new CategoryService();   // fills the category dropdown
        private readonly PharmacyService _pharmacies = new PharmacyService();   // fills the area dropdown
        private readonly CartService _cart = new CartService();                 // the only WRITE reachable here

        // Assigning SelectedIndex raises the change event, so this stops an early query.
        private bool _loading = true;

        // Parameterless: the customer comes from UserSession, the filters start neutral.
        public CustomerOffersForm()
        {
            InitializeComponent();   // designer controls only; the reads wait for Load
        }

        // Runs once after the window exists; both dropdowns are filled from the database.
        private void CustomerOffersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // appearance first, and it attaches the CellFormatting handler

            // Item 0 is the placeholder, so one ComboBox holds both the on and off state.
            cmbCategory.Items.Add("All categories");
            // Read from the database, so a new category appears without a rebuild.
            foreach (Category category in _categories.GetActiveList())
                // "3 - Antibiotics": the id rides in the text, so no second lookup list.
                cmbCategory.Items.Add(category.CategoryId + " - " + category.CategoryName);
            // Selecting the placeholder is what opens the screen showing every offer.
            cmbCategory.SelectedIndex = 0;

            cmbArea.Items.Add("All areas");   // the same placeholder-at-index-0 trick
            // true is approvedOnly, so no area is offered that could return nothing.
            foreach (string area in _pharmacies.GetAreas(true)) cmbArea.Items.Add(area);
            cmbArea.SelectedIndex = 0;   // opens on "All areas", so nothing is hidden first

            // A caption only: nothing in this form compares a date with anything.
            lblToday.Text = "Showing offers valid on " + DateTime.Today.ToString("dd MMM yyyy");

            _loading = false;   // the filters now agree, so querying is safe
            LoadGrid();         // one deliberate first read
        }

        // Appearance only. Green rather than blue: green means a discount everywhere.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Offers and Packages");        // shared window setup
            StartPosition = FormStartPosition.CenterParent;        // centred on its opener

            panelHeader.BackColor = UiTheme.Success;               // green, the discount colour
            lblTitle.Font = UiTheme.FontTitle;                     // shared font, no drift
            lblTitle.ForeColor = Color.White;                      // the only legible colour on Success
            lblSubtitle.Font = UiTheme.FontSmall;                  // smaller, it qualifies the title
            lblSubtitle.ForeColor = Color.FromArgb(215, 240, 225); // pale tint, so it belongs to the strip

            lblToday.Font = UiTheme.FontSmall;                     // the "valid on" caption
            lblToday.ForeColor = UiTheme.TextMuted;                // muted, the date is context
            lblNote.Font = UiTheme.FontSmall;                      // the note on how the discount follows
            lblNote.ForeColor = UiTheme.TextMuted;                 // same grey, so both read as one band
            lblStatus.Font = UiTheme.FontSmall;                    // the line the handlers write into
            lblStatus.ForeColor = UiTheme.TextMuted;               // muted until it carries real news

            UiTheme.StyleSecondary(btnBack);                       // leaving is not the point here
            UiTheme.StyleSecondary(btnRefresh);                    // re-reading changes nothing
            UiTheme.StylePrimary(btnAddToCart);                    // the primary action of this screen
            UiTheme.StyleAccent(btnViewDetails);                   // available, without outranking it
            UiTheme.StyleGrid(dgvOffers);                          // read-only, full-row select, Fill
            dgvOffers.CellFormatting += dgvOffers_CellFormatting;  // wired here, designer untouched
        }

        /// <summary>The category id, or 0 for "All categories".</summary>
        private int SelectedCategoryId()
        {
            // Index 0 is the placeholder and -1 is nothing selected; 0 means "do not filter".
            if (cmbCategory.SelectedIndex <= 0) return 0;
            // SelectedItem is object, but here it is always a string this form added.
            string text = cmbCategory.SelectedItem.ToString();
            // Parse is safe rather than optimistic: this form wrote the string moments ago.
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        /// <summary>The one read path: filters, Refresh and the details dialog.</summary>
        private void LoadGrid()
        {
            if (_loading) return;   // the dropdowns are still being built, so do not query

            try   // wraps the whole read, so a dropped connection leaves a message not a crash
            {
                // Index 0 is "All areas", so it becomes "", the query's do-not-filter signal.
                string area = cmbArea.SelectedIndex <= 0 ? "" : cmbArea.SelectedItem.ToString();

                // No date test here: the query's BETWEEN StartDate AND EndDate does that.
                DataTable table = _offers.GetActiveOffers(SelectedCategoryId(), area);
                // Assigning DataSource is what creates the columns, in SELECT order.
                dgvOffers.DataSource = table;

                // The renames must follow the binding, and Count guards an empty schema.
                if (dgvOffers.Columns.Count > 0)
                {
                    // Each indexer string is the SELECT alias, so a rename there breaks here.
                    dgvOffers.Columns["OfferId"].HeaderText = "ID";
                    // Fill mode, so FillWeight is a share of the width, not a pixel count.
                    dgvOffers.Columns["OfferId"].FillWeight = 26;
                    dgvOffers.Columns["OfferTitle"].HeaderText = "Offer";        // the row's headline
                    dgvOffers.Columns["OfferTitle"].FillWeight = 130;            // 130 against a default of 100
                    dgvOffers.Columns["MedicineName"].HeaderText = "Medicine";   // what the offer is on
                    dgvOffers.Columns["Strength"].HeaderText = "Strength";       // 500 mg and 250 mg differ
                    dgvOffers.Columns["Strength"].FillWeight = 42;               // "500 mg" and little else
                    // Category and Area are the two filters, so showing them explains the rows.
                    dgvOffers.Columns["CategoryName"].HeaderText = "Category";
                    dgvOffers.Columns["PharmacyName"].HeaderText = "Sold by";   // answers who he buys from
                    dgvOffers.Columns["Area"].HeaderText = "Area";              // the delivery area
                    dgvOffers.Columns["Area"].FillWeight = 42;                  // area names are short
                    // "Was" sits beside "You pay": a discount only means something next to it.
                    dgvOffers.Columns["OriginalPrice"].HeaderText = "Was (Tk)";
                    dgvOffers.Columns["OriginalPrice"].FillWeight = 44;          // the currency is in the header
                    dgvOffers.Columns["DiscountPercent"].HeaderText = "Off %";   // the percentage on the offer
                    dgvOffers.Columns["DiscountPercent"].FillWeight = 32;        // the narrowest money column
                    // Calculated by the query, never here, so cart and invoice agree with it.
                    dgvOffers.Columns["DiscountedPrice"].HeaderText = "You pay (Tk)";
                    dgvOffers.Columns["DiscountedPrice"].FillWeight = 50;   // the number he acts on
                    // Also computed by the query, so the three money columns stay consistent.
                    dgvOffers.Columns["YouSave"].HeaderText = "Save (Tk)";
                    dgvOffers.Columns["YouSave"].FillWeight = 44;   // level with "Was" for comparison
                    // Information, not a filter: it says how long is left to decide.
                    dgvOffers.Columns["EndDate"].HeaderText = "Valid until";
                    // Hidden, not dropped: both buttons act on this id though nobody reads it.
                    dgvOffers.Columns["MedicineId"].Visible = false;
                    // Shown so the customer can judge whether the quantity he wants is realistic.
                    dgvOffers.Columns["Stock"].HeaderText = "Stock";
                    dgvOffers.Columns["Stock"].FillWeight = 34;   // a count, so it needs little room
                }

                // The empty result gets its own sentence naming the filters as the cause.
                lblStatus.Text = table.Rows.Count == 0
                    ? "No offer is running today for that combination of filters."   // says what to change
                    : table.Rows.Count + " offer(s) running today. Add one to your cart and the discounted price " +   // the promise the query makes
                      "follows it all the way to the invoice.";   // split for the source only, one sentence on screen

                UpdateButtons();   // rebinding reset the selection, so re-evaluate the buttons
            }
            catch (Exception ex)   // ex holds the sentence DbHelper built from the SqlException
            {
                // Without this an unreachable database would close the whole application.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Every row is an offer, so the whole grid gets the same tint.</summary>
        private void dgvOffers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;   // the header row arrives as -1 and has no style to set
            // Here rather than in a loop, so the tint survives sorting, scrolling and rebinds.
            dgvOffers.Rows[e.RowIndex].DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
        }

        // Moving the selection changes what the buttons act on, so re-run the same method.
        private void dgvOffers_SelectionChanged(object sender, EventArgs e) => UpdateButtons();

        // The single place the two buttons are enabled, so the paths cannot disagree.
        private void UpdateButtons()
        {
            // CurrentRow is null on an empty grid; the cell test rejects an unpopulated row.
            bool hasRow = dgvOffers.CurrentRow != null && dgvOffers.CurrentRow.Cells["MedicineId"].Value != null;
            // Disabled rather than failing later: stock is not tested, the query excluded it.
            btnAddToCart.Enabled = hasRow;
            btnViewDetails.Enabled = hasRow;   // the same condition, both act on the selection
        }

        /// <summary>The selected medicine id, or 0 when nothing is selected.</summary>
        private int SelectedMedicineId()
        {
            // 0 is safe as a sentinel: MedicineId is an IDENTITY column starting at 1.
            if (dgvOffers.CurrentRow == null) return 0;
            // Convert, not a cast: the cell is boxed and the provider picks the exact type.
            return Convert.ToInt32(dgvOffers.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---------------------------------------------------------------------

        // The only handler here that writes; the decision itself belongs to CartService.
        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            // Re-read even though the button is disabled: a shortcut can still reach this.
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;   // the sentinel: nothing usable is selected

            int quantity;   // filled by the validator below through its out parameter
            // The shared Validator, so the rule is identical on every screen that uses it.
            if (!Validator.IsPositiveInt(txtQuantity.Text, out quantity))
            {
                MessageBox.Show("Enter a whole quantity of one or more.", "Check the quantity",   // states the rule
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);                                 // Warning: it can be retyped
                return;   // nothing is written, the basket is left exactly as it was
            }

            // The MEDICINE is added, not the offer: the discount is recalculated each read.
            string message;
            if (_cart.AddOrIncrease(UserSession.UserId, medicineId, quantity, out message))   // it checks stock and merges
            {
                // Read while the row is still valid, and used only to word the confirmation.
                string name = dgvOffers.CurrentRow.Cells["MedicineName"].Value.ToString();
                // ToString, not a decimal conversion: the query already rounded the figure.
                string save = dgvOffers.CurrentRow.Cells["YouSave"].Value.ToString();

                // A status line, not a dialog, so adding several offers is not interrupted.
                lblStatus.Text = quantity + " x " + name + " added to your cart at the offer price. " +
                                 "You are saving Tk " + save + " per unit.";   // per unit, as the grid shows it
                txtQuantity.Text = "1";   // reset to the common case for the next add
                // No reload here: the offers have not changed and he would lose his place.
            }
            else   // false means nothing was written and `message` holds the reason
            {
                // A dialog, because he asked for something he cannot have; the service worded it.
                MessageBox.Show(message, "Cannot add to cart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Opens the full record. Only the id is passed, so prices cannot disagree.
        private void btnViewDetails_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();   // the same helper the add handler uses
            if (medicineId == 0) return;             // nothing selected, so no record to open

            // The details screen re-reads the medicine: it needs the description and reviews.
            using (MedicineDetailsForm details = new MedicineDetailsForm(medicineId))
            {
                details.ShowDialog(this);   // blocks until closed, and "this" owns the dialog
            }
            LoadGrid();   // he may have added from in there, which changes the stock figures
        }

        // Double click is the expected gesture, so it reuses the View details behaviour.
        private void dgvOffers_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;   // a double click on the header arrives as -1
            // Calls the handler rather than copying its body, so the two cannot drift.
            btnViewDetails_Click(sender, EventArgs.Empty);
        }

        // Both dropdowns share this: LoadGrid re-reads every control, so filters combine.
        private void Filter_Changed(object sender, EventArgs e) => LoadGrid();
        // Close, not Dispose: the caller's using block around ShowDialog disposes this.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
