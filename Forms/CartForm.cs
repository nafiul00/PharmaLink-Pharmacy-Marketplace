using System.Configuration;         // ConfigurationManager, the reader for App.config at run time
using System.Data;                  // DataTable, the shape every service read hands back
using System.Drawing;               // Color and Font, needed by the theme and by the row tinting
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the rest of the UI types
using PharmaLinkApp.Helpers;        // UiTheme, the one place the look of the application is defined
using PharmaLinkApp.Services;       // CartService, which is where every basket query actually lives

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 24. The customer's basket.
    ///
    /// Every price shown here is calculated by the query with today's offer
    /// already applied, so the cart and the checkout can never disagree.
    /// A basket that spans two pharmacies becomes two orders at checkout, each
    /// with its own delivery charge and its own invoice, and the summary panel
    /// says so before the customer commits.
    /// </summary>
    public partial class CartForm : Form
    {
        // One service object, created with the form and never replaced. This field is the
        // only route out of this file: the form owns no SqlConnection, no command and no
        // SQL text, it just knows that CartService can answer questions about a basket.
        // That separation is why nothing below would change if the database moved - the
        // query would be rewritten inside CartService and every line in this form would
        // still compile and still be correct. readonly says the reference is fixed for
        // the life of the form, which rules out a handler accidentally reassigning it.
        private readonly CartService _cart = new CartService();

        public CartForm()
        {
            // InitializeComponent is generated into CartForm.Designer.cs and is what actually
            // creates dgvCart, the labels and the buttons. Nothing may touch those fields
            // before it has run, which is precisely why all the work below sits in the Load
            // event rather than being written here in the constructor.
            InitializeComponent();
        }

        private void CartForm_Load(object sender, EventArgs e)
        {
            // Load fires once, after the window handle exists but before the first paint, so
            // the user never sees an unstyled grid flash into a styled one. The order matters:
            // ApplyTheme also attaches the CellFormatting handler, so it must run before any
            // row is drawn by LoadCart.
            ApplyTheme();   // fonts, colours, button styling; touches no data at all
            LoadCart();     // everything that needs the database
        }

        // The delivery charge is read from App.config rather than hard coded as a const, so a
        // change of rate is a change to a settings file and not a rebuild. It is a property
        // rather than a field because it re-reads the setting each time it is used, which
        // keeps one edited config value from needing an application restart.
        private static decimal DeliveryCharge
        {
            get
            {
                // AppSettings returns null when the key is missing. That is treated as an
                // ordinary case, not an error, because the fallback below covers it.
                string configured = ConfigurationManager.AppSettings["DeliveryCharge"];
                decimal value;
                // TryParse rather than decimal.Parse. Parse throws on a typo in the config
                // file, and an unhandled exception in a property read here would take the
                // whole cart screen down over a presentation setting. TryParse turns a bad
                // or absent value into the documented 60 default, so the screen still opens
                // and the customer still sees a number they can question.
                return decimal.TryParse(configured, out value) ? value : 60m;
            }
        }

        // Appearance only: fonts, colours and button styles pushed through UiTheme so this
        // screen matches the others. Nothing in here reads or writes data.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Cart");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            grpSummary.Font = UiTheme.FontHeading;
            grpSummary.ForeColor = UiTheme.Primary;
            grpSummary.BackColor = UiTheme.CardBack;

            foreach (Label caption in new[] { lblItemsCaption, lblDiscountCaption, lblDeliveryCaption })
            {
                caption.Font = UiTheme.FontBody;
                caption.ForeColor = UiTheme.TextMuted;
            }

            foreach (Label value in new[] { lblItemsValue, lblDiscountValue, lblDeliveryValue })
            {
                value.Font = UiTheme.FontBody;
                value.ForeColor = UiTheme.TextDark;
            }

            lblPayableCaption.Font = UiTheme.FontHeading;
            lblPayableCaption.ForeColor = UiTheme.TextDark;
            lblPayableValue.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
            lblPayableValue.ForeColor = UiTheme.Primary;

            lblSummaryNote.Font = UiTheme.FontSmall;
            lblSummaryNote.ForeColor = UiTheme.TextMuted;
            lblSplitNote.Font = UiTheme.FontSmall;
            lblSplitNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StylePrimary(btnUpdateQuantity);
            UiTheme.StyleDanger(btnRemoveLine);
            UiTheme.StyleSecondary(btnClearCart);
            UiTheme.StyleSuccess(btnCheckout);
            btnCheckout.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnContinueShopping);

            UiTheme.StyleGrid(dgvCart);
            dgvCart.CellFormatting += dgvCart_CellFormatting;
        }

        // ---------------------------------------------------------------------

        /// <summary>
        /// The single read path for this screen. Every button below finishes by calling
        /// this rather than patching the grid or adjusting a total by hand, so what is on
        /// screen is always a fresh answer from the database and never an assumption about
        /// what the last write did. Re-reading also picks up changes another user made.
        /// </summary>
        private void LoadCart()
        {
            try
            {
                // The form asks a question and is handed a table. UserSession.UserId is taken
                // from the static session set at login and never from a control on screen,
                // which is what stops one customer reading another customer's basket - there
                // is nothing on this form a user could edit to widen the scope of the query.
                // The prices in this DataTable are already discounted by the SQL, so the cart
                // never recalculates a price and cannot drift from the invoice.
                DataTable lines = _cart.GetLinesTable(UserSession.UserId);

                // Assigning DataSource is what CREATES the columns. AutoGenerateColumns is
                // true by default, so the grid walks the DataTable's schema and adds one
                // DataGridViewColumn per DataColumn, in the order the SELECT listed them,
                // each named after the column it came from. That is why nothing below has to
                // add a column: the SELECT list inside CartService decides what exists here,
                // and a column added to that SELECT appears on this grid with no code change.
                dgvCart.DataSource = lines;

                // The headers are set AFTER the assignment for the same reason: until the line
                // above runs, dgvCart.Columns is empty and every indexer below would throw a
                // null reference. The Count guard covers the case where the query returned no
                // schema at all, which would otherwise crash the whole screen on an empty cart.
                if (dgvCart.Columns.Count > 0)
                {
                    // Hidden rather than dropped from the SELECT: the key still travels with
                    // the row so it can be used in code, it simply means nothing to a customer.
                    dgvCart.Columns["CartId"].Visible = false;
                    // The string indexer is the DataTable column name, which is the alias the
                    // SELECT gave it. Renaming an alias in CartService would break this lookup
                    // at run time rather than at compile time, so the two are kept in step.
                    dgvCart.Columns["MedicineId"].HeaderText = "ID";
                    // StyleGrid sets AutoSizeColumnsMode to Fill, so every column shares the
                    // available width in proportion to its FillWeight. 28 is a small share
                    // because an identifier is only ever three or four digits wide.
                    dgvCart.Columns["MedicineId"].FillWeight = 28;
                    // "Medicine" reads better than the raw column name and is the widest thing
                    // on the row, so it is left on the default weight and takes the slack.
                    dgvCart.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvCart.Columns["Strength"].HeaderText = "Strength";
                    dgvCart.Columns["Strength"].FillWeight = 45;   // "500 mg" and little else
                    dgvCart.Columns["PharmacyId"].Visible = false; // needed by the split note, not by the customer
                    // "Sold by" rather than "PharmacyName": on a marketplace the customer needs
                    // to see which shop each line comes from, because that decides the split.
                    dgvCart.Columns["PharmacyName"].HeaderText = "Sold by";
                    dgvCart.Columns["Quantity"].HeaderText = "Qty";
                    dgvCart.Columns["Quantity"].FillWeight = 30;   // a one or two digit number
                    // Both prices are labelled with their unit so the figures are unambiguous
                    // on a screen that shows a list price and a paid price side by side.
                    dgvCart.Columns["ListPrice"].HeaderText = "List (Tk)";
                    dgvCart.Columns["ListPrice"].FillWeight = 42;
                    dgvCart.Columns["DiscountPercent"].HeaderText = "Off %";
                    dgvCart.Columns["DiscountPercent"].FillWeight = 32;
                    // The one number the customer actually cares about, so it gets a plain
                    // English header and more width than the list price beside it.
                    dgvCart.Columns["PriceYouPay"].HeaderText = "You pay (Tk)";
                    dgvCart.Columns["PriceYouPay"].FillWeight = 48;
                    // Quantity times the discounted unit price, computed by the query rather
                    // than multiplied here, so the rounding matches the invoice exactly.
                    dgvCart.Columns["LineTotal"].HeaderText = "Line total (Tk)";
                    dgvCart.Columns["LineTotal"].FillWeight = 52;
                    // Stock is shown because the CellFormatting handler below compares it with
                    // the quantity; without the column on screen the red row would be a mystery.
                    dgvCart.Columns["Stock"].HeaderText = "In stock";
                    dgvCart.Columns["Stock"].FillWeight = 38;
                    // "Rx" is the pharmacy abbreviation the rest of the application uses, and a
                    // two letter header keeps this boolean column as narrow as it deserves.
                    dgvCart.Columns["RequiresRx"].HeaderText = "Rx";
                    dgvCart.Columns["RequiresRx"].FillWeight = 24;
                }

                // Two more questions for the service rather than a loop over the grid. Summing
                // the visible rows in C# would give a subtly different answer, because each
                // line total is already rounded to two decimals and adding rounded numbers is
                // not the same as rounding the sum. The database does both sums in one place.
                decimal itemsTotal = _cart.GetItemsTotal(UserSession.UserId);
                decimal discount = _cart.GetDiscountTotal(UserSession.UserId);

                // GetPharmacyGroups collapses the basket to one row per pharmacy with a
                // GROUP BY. That row count is not cosmetic: it is exactly how many
                // ORDERS checkout will create, because an order belongs to one pharmacy
                // (Orders.PharmacyId is a single foreign key, not a list).
                DataTable groups = _cart.GetPharmacyGroups(UserSession.UserId, DeliveryCharge);
                int pharmacyCount = groups.Rows.Count;

                // Hence delivery is charged PER ORDER, not per basket. Two pharmacies
                // means two deliveries from two different shops, so two charges - and
                // the summary panel says so before the customer commits to anything.
                decimal delivery = DeliveryCharge * pharmacyCount;

                // UiTheme.Money is used for every figure on the screen so the currency prefix
                // and the two decimal places are identical everywhere, rather than each label
                // choosing its own ToString format and drifting apart over time.
                lblItemsValue.Text = UiTheme.Money(itemsTotal);
                // A saving is written with a leading minus so the panel reads as a sum the eye
                // can follow. When there is no discount it shows a plain zero instead of "- Tk
                // 0.00", which would look like an error rather than an absence.
                lblDiscountValue.Text = discount > 0m ? "- " + UiTheme.Money(discount) : UiTheme.Money(0m);
                // Green only when there is something to celebrate; a zero saving stays in the
                // ordinary text colour so green always means a real discount was applied.
                lblDiscountValue.ForeColor = discount > 0m ? UiTheme.Success : UiTheme.TextDark;
                lblDeliveryValue.Text = UiTheme.Money(delivery);
                // The payable figure is the two numbers above added together in front of the
                // customer. Discount is not subtracted again here: itemsTotal is already the
                // discounted sum, so taking the discount off a second time would undercharge.
                lblPayableValue.Text = UiTheme.Money(itemsTotal + delivery);

                // The note explains the delivery arithmetic in words before the customer
                // reaches checkout and is surprised by a second charge. Two phrasings rather
                // than one generic sentence, because "one delivery" and "two separate orders"
                // are genuinely different outcomes and the wording has to say which applies.
                lblSummaryNote.Text = pharmacyCount <= 1
                    ? "One delivery charge of " + UiTheme.Money(DeliveryCharge) + " applies.\r\n\r\n" +
                      "PharmaLink takes its commission from the pharmacy, not from you. The delivery charge is not commissionable."
                    : "Your basket spans " + pharmacyCount + " pharmacies, so " + pharmacyCount +
                      " separate orders will be created at checkout - one per pharmacy, each with its own " +
                      "delivery charge of " + UiTheme.Money(DeliveryCharge) + " and its own invoice.\r\n\r\n" +
                      "PharmaLink takes its commission from the pharmacy, not from you.";

                // The grouped table is passed straight on rather than being queried again, so
                // the itemised note and the delivery arithmetic above are guaranteed to be
                // describing the same set of rows read in the same round trip.
                BuildSplitNote(groups);

                // One test drives every enabled state below, so the buttons cannot disagree
                // with each other about whether there is anything in the basket.
                bool hasLines = lines.Rows.Count > 0;
                // Checking out an empty basket would create an order with no lines, so the
                // button is disabled rather than allowed to fail with a message afterwards.
                btnCheckout.Enabled = hasLines;
                btnClearCart.Enabled = hasLines;   // nothing to clear when the cart is empty
                // Disabled WinForms buttons keep their colour, so a green Checkout button that
                // does nothing would read as broken. The grey makes the state visible.
                btnCheckout.BackColor = hasLines ? UiTheme.Success : Color.FromArgb(170, 190, 184);

                // The line count goes in the title so it is visible without counting rows.
                lblTitle.Text = hasLines ? "My Cart  (" + lines.Rows.Count + " line(s))" : "My Cart";

                // An empty cart gets a sentence that says what to do next rather than a blank
                // panel, which would leave the customer wondering whether the screen failed.
                lblStatus.Text = hasLines
                    ? "Increasing a quantity above the available stock is refused, and reducing it to zero removes the line."
                    : "Your cart is empty. Browse the catalogue and add something to it.";

                // Rebinding the grid resets the selection, so the per line buttons and the
                // quantity box have to be re-synchronised against whatever row is now current.
                UpdateLineButtons();
            }
            catch (Exception ex)
            {
                // One catch around the whole read. A database that is unreachable throws on
                // the first call, and without this the application would close on an unhandled
                // exception; with it the customer gets the reason and the window stays open.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Writes out the split, one line per pharmacy, so the customer can check the
        /// arithmetic themselves instead of being told a single total to trust.
        /// </summary>
        private void BuildSplitNote(DataTable groups)
        {
            // An empty basket has no groups. The label is blanked rather than left holding
            // the text from the previous load, which would describe orders that no longer
            // exist, and the early return keeps the rest of the method free of null checks.
            if (groups.Rows.Count == 0)
            {
                lblSplitNote.Text = "";
                return;
            }

            // Environment.NewLine rather than "\n": a WinForms Label needs the Windows pair
            // to break a line, and a bare newline would render as one run-on paragraph.
            string text = "Checkout will create " + groups.Rows.Count + " order(s):" + Environment.NewLine;

            // A separate counter because DataRow carries no index of its own, and the customer
            // is being shown a numbered list starting at 1 rather than at 0.
            int index = 1;
            foreach (DataRow row in groups.Rows)
            {
                // Converted once into a decimal so it can be both formatted and added below.
                // Taking it straight from the row twice would mean two conversions of the same
                // value and two chances for them to be done differently.
                decimal itemsTotal = DbHelperDecimal(row, "ItemsTotal");
                // The line shows the working, not just the answer: items plus delivery equals
                // the shop's total. Lines and Units are interpolated directly because
                // string concatenation calls ToString on them and they need no formatting.
                text += "   " + index + ".  " + row["PharmacyName"] + "  -  " +
                        row["Lines"] + " line(s), " + row["Units"] + " unit(s), items " +
                        UiTheme.Money(itemsTotal) + " + delivery " + UiTheme.Money(DeliveryCharge) +
                        "  =  " + UiTheme.Money(itemsTotal + DeliveryCharge) + Environment.NewLine;
                index++;
            }

            // Assigned once at the end rather than appended to the label inside the loop,
            // because every assignment to Text repaints the control.
            lblSplitNote.Text = text;
        }

        /// <summary>
        /// A DataRow column is typed object and may hold DBNull, which Convert.ToDecimal
        /// would reject. This turns "no value" into zero so the arithmetic above never
        /// has to guard each read itself.
        /// </summary>
        private static decimal DbHelperDecimal(DataRow row, string column)
        {
            // DBNull.Value is a singleton, so reference comparison is the correct test; the
            // boxed decimal that arrives otherwise is unwrapped by Convert rather than by a
            // cast, because the provider may hand back any numeric type for a SQL DECIMAL.
            return row[column] == DBNull.Value ? 0m : Convert.ToDecimal(row[column]);
        }

        /// <summary>
        /// CellFormatting fires as each cell is about to be painted, which is what makes it
        /// the right place to colour a row: it runs again after sorting, scrolling and every
        /// rebind, so the colour follows the data rather than the row position. Setting the
        /// colours in a loop after binding would be undone by the next sort.
        /// </summary>
        private void dgvCart_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The event also fires for the header, which arrives as row index -1, and it can
            // fire while the grid is being rebound and has no columns yet. Either case would
            // throw on the cell lookups below, so both are refused before anything is read.
            if (e.RowIndex < 0 || dgvCart.Columns.Count == 0) return;

            // The row is read by index from the event argument rather than from CurrentRow,
            // because this runs for every visible row, not only the selected one.
            DataGridViewRow row = dgvCart.Rows[e.RowIndex];
            // Values come from the cells rather than from the DataTable so the colouring stays
            // correct no matter how the grid has been sorted. They are object, because a grid
            // cell holds whatever the bound column holds, including DBNull.
            object quantity = row.Cells["Quantity"].Value;
            object stock = row.Cells["Stock"].Value;
            object discount = row.Cells["DiscountPercent"].Value;

            // Tested first because it is the more serious condition: a basket line that can
            // no longer be fulfilled has to be visible even when the item is also discounted.
            if (quantity != DBNull.Value && stock != DBNull.Value &&
                Convert.ToInt32(quantity) > Convert.ToInt32(stock))
            {
                // The stock ran out after the line was added.
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
            }
            else if (discount != DBNull.Value && Convert.ToDecimal(discount) > 0m)
            {
                // A running offer, tinted the same green the offers screen uses so the two
                // screens teach the customer one colour rather than two.
                row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            }
            else
            {
                // The else is not optional. DataGridView reuses row objects as it scrolls, so
                // a row left unset would keep the colour of whichever row last used it and the
                // tint would appear to wander up and down the grid.
                row.DefaultCellStyle.BackColor = Color.White;
            }
        }

        // Clicking a different line has to refresh the buttons and reload the quantity box,
        // so the selection event is routed into the same method the initial load uses.
        private void dgvCart_SelectionChanged(object sender, EventArgs e) => UpdateLineButtons();

        /// <summary>
        /// Keeps the per line controls in step with the selection, so a button is never
        /// offered for a row that is not there.
        /// </summary>
        private void UpdateLineButtons()
        {
            // CurrentRow is null when the grid is empty, and the grid is single select with
            // full row selection, so this is always the one row the customer means.
            DataGridViewRow row = dgvCart.CurrentRow;
            // The second test catches the new row placeholder and any partially bound row,
            // whose cells are present but empty; without it a valid looking row could be
            // selected and the conversion in SelectedMedicineId would then throw.
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            // Disabling is preferred to letting the click happen and showing an error
            // afterwards: the control itself tells the customer what is available.
            btnUpdateQuantity.Enabled = hasRow;
            btnRemoveLine.Enabled = hasRow;
            txtQuantity.Enabled = hasRow;

            // The box is pre-filled with the quantity already on the line, so editing means
            // adjusting a real number rather than guessing what is currently there.
            if (hasRow) txtQuantity.Text = row.Cells["Quantity"].Value.ToString();
            // Cleared when nothing is selected, so a stale quantity from a removed line can
            // never be submitted against whichever row happens to be selected next.
            else txtQuantity.Clear();
        }

        /// <summary>
        /// The single place the selected line's identifier is read, so every handler below
        /// uses the same rule and 0 consistently means "nothing usable is selected".
        /// </summary>
        private int SelectedMedicineId()
        {
            // 0 is a safe sentinel because MedicineId is an IDENTITY column starting at 1, so
            // no real medicine can ever have it and no caller can act on it by accident.
            if (dgvCart.CurrentRow == null) return 0;
            // Convert rather than a cast: the cell holds a boxed value whose exact numeric
            // type is decided by the provider, and an (int) cast of a boxed long would throw.
            return Convert.ToInt32(dgvCart.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---------------------------------------------------------------------

        private void btnUpdateQuantity_Click(object sender, EventArgs e)
        {
            // Read the selection first. The button is disabled without a row, but the check is
            // repeated here because a keyboard shortcut or a focus change could still reach it.
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            int quantity;
            // TryParse rather than int.Parse: the box accepts free text and "two" or an empty
            // string must produce a message, not an unhandled FormatException. Note that zero
            // and negatives are deliberately allowed through, because the service treats them
            // as the remove gesture rather than as an error.
            if (!int.TryParse(txtQuantity.Text, out quantity))
            {
                // The message says what zero does, so the customer learns the shortcut rather
                // than hunting for a separate remove button.
                MessageBox.Show("Enter a whole number. Zero removes the line.", "Check the quantity",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The service returns a sentence as well as a result, so the wording of a refusal
            // is written once next to the rule that produced it rather than duplicated here.
            string message;
            // The stock check itself is not repeated in this form. SetQuantity re-reads the
            // shelf inside the same call, which is the only way to be right about a number
            // that another customer's checkout may have changed since this grid was drawn.
            if (_cart.SetQuantity(UserSession.UserId, medicineId, quantity, out message))
                lblStatus.Text = message;            // success is quiet: a status line, no dialog
            else
                // A refusal is loud, because the customer asked for something they cannot have
                // and would otherwise assume the change went through.
                MessageBox.Show(message, "Cannot update the quantity", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // Reloaded either way. On success the totals and the split note have to catch up,
            // and on failure the grid is put back to what the database actually holds so the
            // screen never shows a quantity that was refused.
            LoadCart();
        }

        private void btnRemoveLine_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            // The name is captured BEFORE the delete, because LoadCart rebinds the grid and
            // the row this text came from will not exist by the time the message is shown.
            string name = dgvCart.CurrentRow.Cells["MedicineName"].Value.ToString();
            // No confirmation for a single line: removing one item is cheap to undo by adding
            // it again, unlike emptying the whole basket below.
            _cart.Remove(UserSession.UserId, medicineId);
            // Naming the medicine confirms which line went, which matters when several lines
            // look alike apart from their strength.
            lblStatus.Text = name + " removed from your cart.";
            LoadCart();
        }

        private void btnClearCart_Click(object sender, EventArgs e)
        {
            // This one does ask. Emptying the basket destroys work the customer cannot get
            // back with a single click, so the confirmation is the safeguard rather than an
            // undo feature that would need its own storage.
            DialogResult answer = MessageBox.Show("Remove everything from your cart?", "Empty the cart",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Tested against Yes explicitly rather than assuming anything that is not No means
            // consent: closing the dialog with the window control also has to mean no.
            if (answer != DialogResult.Yes) return;

            // One statement in the service deletes every line for this customer, rather than
            // this form looping over the grid and issuing one delete per row.
            _cart.ClearAll(UserSession.UserId);
            LoadCart();
        }

        private void btnCheckout_Click(object sender, EventArgs e)
        {
            // using guarantees the dialog's window handle and its controls are released even
            // if the checkout throws, which matters because this screen can be opened and
            // closed repeatedly in one session.
            using (CheckoutForm checkout = new CheckoutForm())
            {
                // ShowDialog, not Show: it blocks until checkout closes, so the line after it
                // is only reached once the customer has finished, and passing "this" makes the
                // cart the owner so the dialog cannot be lost behind it.
                checkout.ShowDialog(this);
            }

            // Checkout may have emptied the basket completely or only the part belonging to
            // one pharmacy, and this form has no way of knowing which. Re-reading rather than
            // guessing is what keeps the totals correct after a partial checkout.
            LoadCart();

            // When the whole basket has been paid for there is nothing left to
            // show, so the cart closes and returns to the catalogue.
            if (_cart.CountLines(UserSession.UserId) == 0) Close();
        }

        // Close rather than Dispose or Hide: the parent screen opened this one with
        // ShowDialog inside a using block, so closing returns control there and the using
        // block disposes it.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
