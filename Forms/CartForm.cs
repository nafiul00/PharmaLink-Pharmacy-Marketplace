using System.Configuration;         // ConfigurationManager, the reader for App.config
using System.Data;                  // DataTable, the shape every service read hands back
using System.Drawing;               // Color and Font, for the theme and the row tinting
using System.Windows.Forms;         // Form, DataGridView, MessageBox and the other UI types
using PharmaLinkApp.Helpers;        // UiTheme, the one place the look is defined
using PharmaLinkApp.Services;       // CartService, where every basket query actually lives

// Forms hold presentation and know no SQL; Services holds the statements.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 24: the customer's basket.</summary>
    public partial class CartForm : Form
    {
        // The only route out of this file: no connection, no command and no SQL text here.
        private readonly CartService _cart = new CartService();

        // Builds the controls and nothing else; the reason is spelled out inside.
        public CartForm()
        {
            // Nothing may touch dgvCart before this has run, so the work waits for Load.
            InitializeComponent();
        }

        // The real entry point: everything needing a control or a row happens from here.
        private void CartForm_Load(object sender, EventArgs e)
        {
            // Order matters: ApplyTheme attaches CellFormatting before any row is drawn.
            ApplyTheme();   // fonts, colours, button styling; touches no data at all
            LoadCart();     // everything that needs the database
        }

        // Read from App.config, so a change of rate is a settings edit and not a rebuild.
        private static decimal DeliveryCharge
        {
            // Getter only: the config file is the single place that decides the rate.
            get
            {
                // AppSettings returns null for a missing key, which the fallback covers.
                string configured = ConfigurationManager.AppSettings["DeliveryCharge"];
                decimal value;   // the out target for TryParse, which assigns it either way
                // TryParse, not Parse: a typo in the config must not close the cart screen.
                return decimal.TryParse(configured, out value) ? value : 60m;
            }
        }

        // Appearance only, pushed through UiTheme so this screen matches the others.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "My Cart");                     // size, icon, background and caption in one call
            StartPosition = FormStartPosition.CenterParent;         // opened with ShowDialog, so it should land over its owner

            panelHeader.BackColor = UiTheme.Primary;                // the green band every screen in the app wears
            lblTitle.Font = UiTheme.FontTitle;                      // the largest type here, and the line that names the screen
            lblTitle.ForeColor = Color.White;                       // the only text colour with contrast on the primary green
            lblSubtitle.Font = UiTheme.FontSmall;                   // smaller, so title and subtitle read as one unit
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);  // a pale tint of the band: present, but secondary

            grpSummary.Font = UiTheme.FontHeading;                  // the totals panel's caption
            grpSummary.ForeColor = UiTheme.Primary;                 // green caption, tying the panel to the header
            grpSummary.BackColor = UiTheme.CardBack;                // an off-white card, so the panel lifts off the form

            // Styled as a set, so the three captions cannot end up looking different.
            foreach (Label caption in new[] { lblItemsCaption, lblDiscountCaption, lblDeliveryCaption })
            {
                caption.Font = UiTheme.FontBody;         // body weight: a caption is a label, not a heading
                caption.ForeColor = UiTheme.TextMuted;   // muted, so the eye lands on the figure beside it
            }

            // And their three values as a matching set, one shade darker than the captions.
            foreach (Label value in new[] { lblItemsValue, lblDiscountValue, lblDeliveryValue })
            {
                value.Font = UiTheme.FontBody;          // the same size as the caption, so the rows line up
                value.ForeColor = UiTheme.TextDark;     // darker, because the number is the thing being read
            }

            lblPayableCaption.Font = UiTheme.FontHeading;                              // the payable row is a conclusion, so heading weight
            lblPayableCaption.ForeColor = UiTheme.TextDark;                            // dark, not muted: this is no background detail
            lblPayableValue.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);  // 18pt, because it is the number that matters
            lblPayableValue.ForeColor = UiTheme.Primary;                               // brand green, so the eye finds the total first

            lblSummaryNote.Font = UiTheme.FontSmall;         // the sentence explaining the delivery arithmetic
            lblSummaryNote.ForeColor = UiTheme.TextMuted;    // muted: an explanation must not fight the totals
            lblSplitNote.Font = UiTheme.FontSmall;           // the per-pharmacy breakdown from BuildSplitNote
            lblSplitNote.ForeColor = UiTheme.TextMuted;      // same treatment, so the two notes read as one block
            lblStatus.Font = UiTheme.FontSmall;              // the quiet line recording what the last button did
            lblStatus.ForeColor = UiTheme.TextMuted;         // muted, so a success message never looks like an alarm

            UiTheme.StyleSecondary(btnBack);                                          // grey: leaving is not the encouraged action
            UiTheme.StylePrimary(btnUpdateQuantity);                                  // green: the ordinary editing action on a line
            UiTheme.StyleDanger(btnRemoveLine);                                       // red, because it destroys a line rather than editing one
            UiTheme.StyleSecondary(btnClearCart);                                     // grey: its confirmation dialog is the real guard
            UiTheme.StyleSuccess(btnCheckout);                                        // the one button the whole screen points at
            btnCheckout.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);    // set after StyleSuccess, or it would be overwritten
            UiTheme.StyleSecondary(btnContinueShopping);                              // an alternative, so it must not compete with checkout

            UiTheme.StyleGrid(dgvCart);                          // row heights, headers and the Fill mode the weights below need
            dgvCart.CellFormatting += dgvCart_CellFormatting;    // attached before LoadCart binds, so the first paint is coloured
        }

        // ---- READ ----

        /// <summary>The single read path: every button ends by calling this.</summary>
        private void LoadCart()
        {
            // One try around the whole read, because an unreachable DB shows up on call one.
            try
            {
                // UserId comes from the session, not a control, so only this basket loads.
                DataTable lines = _cart.GetLinesTable(UserSession.UserId);

                // Assigning DataSource is what CREATES the columns, from the SELECT list.
                dgvCart.DataSource = lines;

                // Headers come after the bind, because until then dgvCart.Columns is empty.
                if (dgvCart.Columns.Count > 0)
                {
                    // Hidden, not dropped: the key travels with the row but means nothing here.
                    dgvCart.Columns["CartId"].Visible = false;
                    // The indexer is the SELECT's alias, so renaming one would break this.
                    dgvCart.Columns["MedicineId"].HeaderText = "ID";
                    // Fill mode shares the width by FillWeight; 28 suits a four-digit id.
                    dgvCart.Columns["MedicineId"].FillWeight = 28;
                    // Left on the default weight, so the widest column takes the slack.
                    dgvCart.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvCart.Columns["Strength"].HeaderText = "Strength";   // already plain English, so the name is kept
                    dgvCart.Columns["Strength"].FillWeight = 45;   // "500 mg" and little else
                    dgvCart.Columns["PharmacyId"].Visible = false; // needed by the split note, not by the customer
                    // "Sold by", because on a marketplace the shop decides the order split.
                    dgvCart.Columns["PharmacyName"].HeaderText = "Sold by";
                    dgvCart.Columns["Quantity"].HeaderText = "Qty";   // abbreviated: one or two digits at most
                    dgvCart.Columns["Quantity"].FillWeight = 30;   // a one or two digit number
                    // Both prices carry their unit, since list and paid sit side by side.
                    dgvCart.Columns["ListPrice"].HeaderText = "List (Tk)";
                    dgvCart.Columns["ListPrice"].FillWeight = 42;             // narrow: it is the reference price, not the charge
                    dgvCart.Columns["DiscountPercent"].HeaderText = "Off %";  // short: the percent sign says what it is
                    dgvCart.Columns["DiscountPercent"].FillWeight = 32;       // two digits at most, since it cannot pass 100
                    // The one number the customer cares about, so it gets plain English.
                    dgvCart.Columns["PriceYouPay"].HeaderText = "You pay (Tk)";
                    dgvCart.Columns["PriceYouPay"].FillWeight = 48;   // wider than List, so the real price draws the eye
                    // Computed by the query, not multiplied here, so rounding matches the invoice.
                    dgvCart.Columns["LineTotal"].HeaderText = "Line total (Tk)";
                    dgvCart.Columns["LineTotal"].FillWeight = 52;   // the widest money column: it carries the most digits
                    // Shown because CellFormatting compares it with the quantity below.
                    dgvCart.Columns["Stock"].HeaderText = "In stock";
                    dgvCart.Columns["Stock"].FillWeight = 38;   // a plain count, so no wider than the quantity
                    // "Rx" is the abbreviation the rest of the application already uses.
                    dgvCart.Columns["RequiresRx"].HeaderText = "Rx";
                    dgvCart.Columns["RequiresRx"].FillWeight = 24;   // the narrowest column: a tick needs no more
                }

                // Summed by the database, because adding already-rounded line totals drifts.
                decimal itemsTotal = _cart.GetItemsTotal(UserSession.UserId);
                decimal discount = _cart.GetDiscountTotal(UserSession.UserId);   // the mirror sum: what the offers took off

                // The GROUP BY gives one row per pharmacy, and an order belongs to one shop.
                DataTable groups = _cart.GetPharmacyGroups(UserSession.UserId, DeliveryCharge);
                int pharmacyCount = groups.Rows.Count;   // one row per shop, so this IS the order count

                // Delivery is charged PER ORDER: two shops means two deliveries, so two charges.
                decimal delivery = DeliveryCharge * pharmacyCount;

                // UiTheme.Money everywhere, so the prefix and the decimals never drift apart.
                lblItemsValue.Text = UiTheme.Money(itemsTotal);
                // A leading minus reads as a sum, but a plain zero when there is no saving.
                lblDiscountValue.Text = discount > 0m ? "- " + UiTheme.Money(discount) : UiTheme.Money(0m);
                // Green only for a real saving, so the colour always means the same thing.
                lblDiscountValue.ForeColor = discount > 0m ? UiTheme.Success : UiTheme.TextDark;
                lblDeliveryValue.Text = UiTheme.Money(delivery);   // the multiplied figure, so two shops show two charges
                // itemsTotal is already discounted, so the discount is not taken off twice.
                lblPayableValue.Text = UiTheme.Money(itemsTotal + delivery);

                // Two phrasings, because one delivery and two separate orders differ in kind.
                lblSummaryNote.Text = pharmacyCount <= 1
                    ? "One delivery charge of " + UiTheme.Money(DeliveryCharge) + " applies.\r\n\r\n" +   // <= 1, so an empty basket takes this branch
                      "PharmaLink takes its commission from the pharmacy, not from you. The delivery charge is not commissionable."   // said here, or the customer would wonder
                    : "Your basket spans " + pharmacyCount + " pharmacies, so " + pharmacyCount +         // the count is repeated, so the sentence stands alone
                      " separate orders will be created at checkout - one per pharmacy, each with its own " +   // "will be", not "may be": this is certain
                      "delivery charge of " + UiTheme.Money(DeliveryCharge) + " and its own invoice.\r\n\r\n" + // each order gets its own invoice too
                      "PharmaLink takes its commission from the pharmacy, not from you.";                       // the same reassurance in both branches

                // The grouped table is passed on, so the note describes the same round trip.
                BuildSplitNote(groups);

                // One test drives every enabled state, so the buttons cannot disagree.
                bool hasLines = lines.Rows.Count > 0;
                // An empty checkout would create an order with no lines, so the button is off.
                btnCheckout.Enabled = hasLines;
                btnClearCart.Enabled = hasLines;   // nothing to clear when the cart is empty
                // Disabled buttons keep their colour, so grey is what makes the state visible.
                btnCheckout.BackColor = hasLines ? UiTheme.Success : Color.FromArgb(170, 190, 184);

                // The line count goes in the title, so it is visible without counting rows.
                lblTitle.Text = hasLines ? "My Cart  (" + lines.Rows.Count + " line(s))" : "My Cart";

                // An empty cart gets a sentence saying what to do next, not a blank panel.
                lblStatus.Text = hasLines
                    ? "Increasing a quantity above the available stock is refused, and reducing it to zero removes the line."   // teaches both shortcuts early
                    : "Your cart is empty. Browse the catalogue and add something to it.";                                      // an instruction, not a statement

                // Rebinding resets the selection, so the per line controls are re-synced.
                UpdateLineButtons();
            }
            // One catch for the whole read: any call failing leaves the same unusable screen.
            catch (Exception ex)
            {
                // Without this an unreachable database would close the whole application.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Writes the split, one line per pharmacy.</summary>
        private void BuildSplitNote(DataTable groups)
        {
            // An empty basket has no groups, and stale text would describe vanished orders.
            if (groups.Rows.Count == 0)
            {
                lblSplitNote.Text = "";   // blanked, not left holding the previous load's text
                return;                   // so the loop below never has to test for an empty table
            }

            // Environment.NewLine, not "\n": a Label needs the Windows pair to break a line.
            string text = "Checkout will create " + groups.Rows.Count + " order(s):" + Environment.NewLine;

            // A separate counter, because DataRow carries no index and the list starts at 1.
            int index = 1;
            // One pass over already-grouped rows: the GROUP BY has done the work already.
            foreach (DataRow row in groups.Rows)
            {
                // Converted once, so it can be both formatted and added without repeating.
                decimal itemsTotal = DbHelperDecimal(row, "ItemsTotal");
                // The line shows the working, not just the answer: items plus delivery.
                text += "   " + index + ".  " + row["PharmacyName"] + "  -  " +
                        row["Lines"] + " line(s), " + row["Units"] + " unit(s), items " +          // two boxes of one medicine is one line
                        UiTheme.Money(itemsTotal) + " + delivery " + UiTheme.Money(DeliveryCharge) +   // the same Money formatter as the panel
                        "  =  " + UiTheme.Money(itemsTotal + DeliveryCharge) + Environment.NewLine;    // the sum shown beside its parts
                index++;   // the human-facing counter, stepped apart from the loop itself
            }

            // Assigned once at the end, because every assignment to Text repaints the label.
            lblSplitNote.Text = text;
        }

        /// <summary>Turns a DBNull column into zero for the arithmetic above.</summary>
        private static decimal DbHelperDecimal(DataRow row, string column)
        {
            // Convert, not a cast: the provider may box a SQL DECIMAL as any numeric type.
            return row[column] == DBNull.Value ? 0m : Convert.ToDecimal(row[column]);
        }

        /// <summary>Runs on every repaint, so the colour follows the data.</summary>
        private void dgvCart_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The header arrives as row -1, and a rebinding grid has no columns yet.
            if (e.RowIndex < 0 || dgvCart.Columns.Count == 0) return;

            // Read by index from the event, because this runs for every visible row.
            DataGridViewRow row = dgvCart.Rows[e.RowIndex];
            // Values come from the cells, so the colouring survives any sort of the grid.
            object quantity = row.Cells["Quantity"].Value;
            object stock = row.Cells["Stock"].Value;                    // the shelf count as of the last read
            object discount = row.Cells["DiscountPercent"].Value;       // 0 when no offer runs, so test > 0, not null

            // Tested first: an unfulfillable line must show even when it is also discounted.
            if (quantity != DBNull.Value && stock != DBNull.Value &&
                Convert.ToInt32(quantity) > Convert.ToInt32(stock))   // Convert, since the provider picks the boxed type
            {
                // The stock ran out after the line was added.
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
            }
            // Only reached when the line IS fulfillable, so a discount never hides a shortage.
            else if (discount != DBNull.Value && Convert.ToDecimal(discount) > 0m)
            {
                // A running offer, in the same green the offers screen uses.
                row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            }
            // The plain case, and not optional; see the line inside.
            else
            {
                // DataGridView reuses rows while scrolling, so an unset row keeps a stale tint.
                row.DefaultCellStyle.BackColor = Color.White;
            }
        }

        // Clicking a line refreshes the buttons, so selection routes into the same method.
        private void dgvCart_SelectionChanged(object sender, EventArgs e) => UpdateLineButtons();

        /// <summary>Keeps the per line controls in step with the selection.</summary>
        private void UpdateLineButtons()
        {
            // CurrentRow is null on an empty grid; selection is single and full row.
            DataGridViewRow row = dgvCart.CurrentRow;
            // The second test catches the placeholder row, whose cells exist but are empty.
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            // Disabling beats letting the click happen and apologising afterwards.
            btnUpdateQuantity.Enabled = hasRow;
            btnRemoveLine.Enabled = hasRow;   // the same flag for all three, so they cannot disagree
            txtQuantity.Enabled = hasRow;     // the box goes with its button, or typing would go nowhere

            // Pre-filled with the current quantity, so editing adjusts a real number.
            if (hasRow) txtQuantity.Text = row.Cells["Quantity"].Value.ToString();
            // Cleared otherwise, so a stale quantity cannot be sent against the next row.
            else txtQuantity.Clear();
        }

        /// <summary>The one reader for the selection; 0 means nothing usable.</summary>
        private int SelectedMedicineId()
        {
            // 0 is safe: MedicineId is an IDENTITY starting at 1, so no row can hold it.
            if (dgvCart.CurrentRow == null) return 0;
            // Convert, not a cast: an (int) cast of a boxed long would throw at run time.
            return Convert.ToInt32(dgvCart.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---- LINE ACTIONS ----

        // Replaces the quantity on the selected line; the stock rule lives in the service.
        private void btnUpdateQuantity_Click(object sender, EventArgs e)
        {
            // Repeated, because a shortcut can reach this past a disabled button.
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;   // the sentinel: nothing selected, so nothing is attempted

            int quantity;   // the out target for TryParse, which assigns it on both branches
            // TryParse, so "two" is a message; zero passes, as the service removes on it.
            if (!int.TryParse(txtQuantity.Text, out quantity))
            {
                // The message names the zero shortcut, so no separate button is hunted for.
                MessageBox.Show("Enter a whole number. Zero removes the line.", "Check the quantity",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);   // Warning: the typing was wrong, nothing failed
                return;   // nothing is sent, and the typed text stays to be corrected
            }

            // The service returns the wording too, so a refusal is phrased next to its rule.
            string message;
            // SetQuantity re-reads the shelf inside the call, which is the only correct test.
            if (_cart.SetQuantity(UserSession.UserId, medicineId, quantity, out message))
                lblStatus.Text = message;            // success is quiet: a status line, no dialog
            // The refusal branch, which is loud on purpose.
            else
                // Loud, because the customer would otherwise assume the change went through.
                MessageBox.Show(message, "Cannot update the quantity", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // Reloaded either way: on failure the grid goes back to what the database holds.
            LoadCart();
        }

        // Deletes the selected line outright. No confirmation, for the reason given below.
        private void btnRemoveLine_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();   // the same single reader every handler uses
            if (medicineId == 0) return;             // and the same sentinel, so nothing goes without a row

            // Captured BEFORE the delete: LoadCart rebinds and this row will be gone.
            string name = dgvCart.CurrentRow.Cells["MedicineName"].Value.ToString();
            // No confirmation for one line: adding it again is cheap, unlike a full clear.
            _cart.Remove(UserSession.UserId, medicineId);
            // Naming the medicine confirms which line went when several rows look alike.
            lblStatus.Text = name + " removed from your cart.";
            LoadCart();   // re-read rather than deleting the row by hand, so totals follow
        }

        // Empties the whole basket. The one action here that asks first.
        private void btnClearCart_Click(object sender, EventArgs e)
        {
            // Asked, because this destroys work no single click can bring back.
            DialogResult answer = MessageBox.Show("Remove everything from your cart?", "Empty the cart",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // YesNo and a Question icon: this is a choice

            // Tested against Yes, so closing the dialog with the window control means no.
            if (answer != DialogResult.Yes) return;

            // One statement in the service, rather than a delete per row from this form.
            _cart.ClearAll(UserSession.UserId);
            LoadCart();   // the reload is what empties the grid and zeroes the panel
        }

        // Hands over to checkout, then asks the database whether anything is left.
        private void btnCheckout_Click(object sender, EventArgs e)
        {
            // using releases the dialog's handle even if the checkout throws.
            using (CheckoutForm checkout = new CheckoutForm())
            {
                // ShowDialog blocks until it closes, and "this" makes the cart its owner.
                checkout.ShowDialog(this);
            }

            // Checkout may have emptied all or only part, so re-read rather than guess.
            LoadCart();

            // Nothing left to show, so the cart closes and returns to the catalogue.
            if (_cart.CountLines(UserSession.UserId) == 0) Close();
        }

        // Close, not Dispose: the parent's using block around ShowDialog disposes this.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
