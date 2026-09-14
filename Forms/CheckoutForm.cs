using System.Configuration;         // ConfigurationManager, which reads DeliveryCharge from App.config
using System.Data;                  // DataTable, DataRow and the DataView this form filters in memory
using System.Drawing;               // Font and Color for ApplyTheme and the disabled button
using System.Windows.Forms;         // Form, DataGridView, MessageBox, Cursors
using PharmaLinkApp.Helpers;        // UiTheme for styling and Money, Validator for the field rules
using PharmaLinkApp.Models;         // User, used only to pre-fill the address from the profile
using PharmaLinkApp.Services;       // the four services a checkout touches

// Every screen lives in PharmaLinkApp.Forms, so siblings open by name.
namespace PharmaLinkApp.Forms
{
    /// <summary>One checkout per pharmacy; Confirm runs one transaction.</summary>
    public partial class CheckoutForm : Form
    {
        // Four services, because a checkout touches four areas; all are stateless.
        private readonly CartService _cart = new CartService();
        private readonly OrderService _orders = new OrderService();                     // owns Checkout(), the transaction this form leads up to
        private readonly PrescriptionService _prescriptions = new PrescriptionService();   // copies the scan, once an OrderId exists
        private readonly AuthService _auth = new AuthService();                         // used once, in Load, to read the saved address

        // The basket grouped by shop, fetched ONCE in Load; the index is what advances.
        private DataTable _pharmacyGroups;
        private int _currentIndex;   // which row of that snapshot is being checked out; 0 by default

        // Held in memory until an order exists: Prescriptions.OrderId is a foreign key.
        private string _pendingRxImagePath = "";
        private string _pendingRxDoctorName = "";   // optional, so blank here is a normal value

        // The constructor does as little as possible; see the comment inside.
        public CheckoutForm()
        {
            // Designer generated. Everything that reads the database happens in Load.
            InitializeComponent();
        }

        /// <summary>The flat delivery charge, per pharmacy, read from App.config.</summary>
        private static decimal DeliveryCharge
        {
            get   // getter only: the charge is a setting the app reads, never one it writes
            {
                // A property, not a const, so App.config can change it without a rebuild.
                string configured = ConfigurationManager.AppSettings["DeliveryCharge"];
                decimal value;   // TryParse writes here, so the variable must exist first
                // TryParse, not Parse: a missing or mistyped key falls back to 60.
                return decimal.TryParse(configured, out value) ? value : 60m;
            }
        }

        // Load, not the constructor: an empty basket has to be able to close the window.
        private void CheckoutForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // colours and fonts first, so nothing is repainted after the data

            // Index 0 is a prompt, not a method: it gives the combo an unchosen state.
            cmbPayment.Items.AddRange(new object[]
            {
                "- choose how you will pay -",   // index 0, the unchosen state ValidateAll refuses
                "Cash on delivery",             // index 1, stored as 'CashOnDelivery'
                "bKash",                        // index 2, a wallet, so NeedsMobileNumber is true
                "Nagad",                        // index 3, the other wallet
                "Card"                          // index 4, needs no extra field
            });
            // The order of those five is load bearing: two methods switch on the index.
            cmbPayment.SelectedIndex = 0;

            // Pre-filled from the profile but editable: the address belongs to the ORDER.
            User me = _auth.GetUser(UserSession.UserId);
            // UserSession.UserId, never a control value, so nobody checks out as another.
            if (me != null) txtAddress.Text = me.Address;

            // ONE query for the whole basket, grouped by shop, with the totals from SQL.
            _pharmacyGroups = _cart.GetPharmacyGroups(UserSession.UserId, DeliveryCharge);

            // No groups means no lines: grouping an empty cart gives an empty table.
            if (_pharmacyGroups.Rows.Count == 0)
            {
                // An empty basket is a state, not an error, so it gets a plain message.
                MessageBox.Show("Your cart is empty.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);   // Information, not Warning
                DialogResult = DialogResult.Cancel;   // tells the cart screen nothing was ordered
                Close();                              // the return below stops the rest of Load
                // Essential: Close() during Load does not abort the rest of the handler.
                return;
            }

            ShowCurrentPharmacy();   // paint the first shop; _currentIndex is still 0
        }

        // All the styling here, so the palette stays in UiTheme, not a designer file.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Checkout");                // shared chrome: background, icon, title prefix
            StartPosition = FormStartPosition.CenterParent;     // opens over the cart screen that launched it

            panelHeader.BackColor = UiTheme.Primary;                              // the brand green band
            lblTitle.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);   // one size larger than the shared heading
            lblTitle.ForeColor = Color.White;                                     // the only pairing with enough contrast
            lblSubtitle.Font = UiTheme.FontSmall;                                 // smaller, so the shop name reads as a caption
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);                // a pale tint that recedes without vanishing

            // One loop over both group boxes, so a card style change cannot reach one only.
            foreach (GroupBox group in new[] { grpDelivery, grpReview })
            {
                group.Font = UiTheme.FontHeading;      // the caption font; children override it below
                group.ForeColor = UiTheme.Primary;     // brand colour, so the caption reads as a section head
                group.BackColor = UiTheme.CardBack;    // the off-white card surface

                // A GroupBox passes its Font down, so children are given the body font.
                foreach (Control child in group.Controls)
                {
                    child.Font = UiTheme.FontBody;         // readable body size for every child
                    child.ForeColor = UiTheme.TextDark;    // the default ink; error labels differ below
                    // Pattern matching types and names in one step; Error is the convention.
                    if (child is Label label && label.Name.EndsWith("Error"))
                    {
                        label.Font = UiTheme.FontSmall;       // smaller, so it reads as an annotation
                        label.ForeColor = UiTheme.Danger;     // red, the one colour for 'fix this'
                    }
                }
            }

            lblPayableCaption.Font = UiTheme.FontHeading;                                // the word 'Payable'
            lblPayableValue.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);   // the largest text: it is what is agreed to
            lblPayableValue.ForeColor = UiTheme.Primary;                                 // brand green separates it from the figures above

            lblRxWarning.Font = UiTheme.FontSmall;      // the amber 'you still need to attach' notice
            lblRxState.Font = UiTheme.FontSmall;        // the line that flips red to green
            lblHint.Font = UiTheme.FontMono;            // monospaced, because the text below is a numbered list
            lblHint.ForeColor = UiTheme.TextMuted;      // muted: it explains and never asks
            lblStatus.Font = UiTheme.FontSmall;         // the price-freeze line under the totals
            lblStatus.ForeColor = UiTheme.TextMuted;    // muted too: reassurance, not an instruction

            UiTheme.StyleSecondary(btnCancel);     // outlined, so leaving does not compete with confirming
            UiTheme.StyleAccent(btnUploadRx);      // accent: it is the one thing standing before Confirm
            UiTheme.StyleSuccess(btnConfirm);      // green, the colour for an action that commits
            btnConfirm.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);   // the form's single primary action
            UiTheme.StyleGrid(dgvReview);          // the shared grid look, with no user editing

            // Spelling the transaction out is the honest answer to 'what happens now'.
            lblHint.Text =   // built from string concatenation, one screen line per statement
                // Environment.NewLine, not a bare newline, so a Label breaks lines properly.
                "Confirm Order runs these five statements inside ONE transaction, all scoped to this pharmacy:" + Environment.NewLine +
                // Step one, the header row, and the moment CommissionAmount is frozen.
                "   1. INSERT INTO Orders      - the header, with CommissionAmount frozen at this pharmacy's rate today" + Environment.NewLine +
                // Step two, the lines, at the prices the grid on the right is showing.
                "   2. INSERT INTO OrderItems  - one line per cart row, at the discounted price shown on the right" + Environment.NewLine +
                // Step three, the stock reduction, which is what a sell-out rolls back.
                "   3. UPDATE Medicines        - take the units off the shelf" + Environment.NewLine +
                // Step four, and the reason a two-shop basket survives one checkout.
                "   4. DELETE FROM Cart        - clear only this pharmacy's lines; the rest becomes the next order" + Environment.NewLine +
                // Step five, the all-or-nothing guarantee the other four depend on.
                "   5. COMMIT                  - if any statement fails the whole thing is rolled back, so no order can exist without its items";
        }

        // ===== ONE PHARMACY AT A TIME =====

        // All three are derived from _currentIndex, so no second copy can go stale.
        private DataRow CurrentGroup => _pharmacyGroups.Rows[_currentIndex];
        // Convert.ToInt32, not a cast: a DataRow indexer hands back a boxed object.
        private int CurrentPharmacyId => Convert.ToInt32(CurrentGroup["PharmacyId"]);
        // ToString() for the same reason: the column arrives boxed as whatever it is.
        private string CurrentPharmacyName => CurrentGroup["PharmacyName"].ToString();

        // Repaints the form for the current shop, leaving no trace of the previous one.
        private void ShowCurrentPharmacy()
        {
            // Reset the prescription FIRST, or order 1's scan would be uploaded again.
            _pendingRxImagePath = "";
            _pendingRxDoctorName = "";   // cleared with the path, so no stale doctor's name carries over

            // 'Order 1 of 2' says plainly that a two shop basket is two orders.
            lblTitle.Text = "Order " + (_currentIndex + 1) + " of " + _pharmacyGroups.Rows.Count;
            // Names the shop and the consequence together, before the second checkout.
            lblSubtitle.Text = CurrentPharmacyName + "   -   this half of your basket is a separate order with its own invoice.";

            LoadReviewGrid();       // the lines and the totals for this shop only

            // Asked per pharmacy: an Rx item in the OTHER half must not block this order.
            bool needsRx = _cart.ContainsPrescriptionItem(UserSession.UserId, CurrentPharmacyId);

            // The three prescription controls appear only when they are relevant.
            lblRxWarning.Visible = needsRx;
            btnUploadRx.Visible = needsRx;   // hidden, not disabled: a greyed button invites a question
            lblRxState.Visible = needsRx;    // ValidateAll reads this visibility back as the rule

            // Only the wording is inside the branch, so the three lines above run either way.
            if (needsRx)
            {
                // Naming the formats and the size limit up front saves a rejected upload.
                lblRxWarning.Text = "This order contains a prescription only medicine. You cannot confirm it until you " +
                                    // The second half of the sentence, with the two rules that refuse an upload.
                                    "attach a photograph of your doctor's prescription (JPG or PNG, under 2 MB).";
                lblRxWarning.ForeColor = UiTheme.Warning;   // amber: nothing is wrong, something is still required
                lblRxState.Text = "No prescription attached yet.";   // repainted on a successful upload
                lblRxState.ForeColor = UiTheme.Danger;   // red, because this one blocks Confirm until it is green
            }

            ValidateAll();   // re-decides Confirm, since needsRx may have added a rule
        }

        // Fills the review grid and the money labels for the current shop only.
        private void LoadReviewGrid()
        {
            // Fetch the WHOLE basket, every pharmacy, in one query.
            DataTable allLines = _cart.GetLinesTable(UserSession.UserId);

            // Then narrow it IN MEMORY: the one client side filter in the application.
            DataView view = new DataView(allLines);   // a filtered window onto allLines, not a copy
            view.RowFilter = "PharmacyId = " + CurrentPharmacyId;   // an int, so no typed text reaches it
            DataTable mine = view.ToTable();      // materialise it, so the grid holds a detached table

            dgvReview.DataSource = mine;   // binding is what creates the grid's columns, so it comes first

            // Guarded, because an empty filtered table binds no columns at all.
            if (dgvReview.Columns.Count > 0)
            {
                // Hide all, then re-show five: a column added later stays hidden, not unlabelled.
                foreach (DataGridViewColumn column in dgvReview.Columns) column.Visible = false;

                // HeaderText overrides the SQL name; FillWeight is a proportion, not pixels.
                dgvReview.Columns["MedicineName"].Visible = true;
                dgvReview.Columns["MedicineName"].HeaderText = "Medicine";   // no weight, so it absorbs the leftover width
                dgvReview.Columns["Strength"].Visible = true;                // 500mg and 665mg are different products
                dgvReview.Columns["Strength"].HeaderText = "Strength";       // set explicitly, so a query rename cannot change it
                dgvReview.Columns["Strength"].FillWeight = 45;               // narrow: the content is a few characters
                dgvReview.Columns["Quantity"].Visible = true;                // the one figure the customer chose himself
                dgvReview.Columns["Quantity"].HeaderText = "Qty";            // abbreviated, because the column is the narrowest
                dgvReview.Columns["Quantity"].FillWeight = 30;               // the smallest weight; the values are one or two digits
                // Both money columns arrive discounted from SQL, so nothing is multiplied here.
                dgvReview.Columns["PriceYouPay"].Visible = true;
                dgvReview.Columns["PriceYouPay"].HeaderText = "Unit (Tk)";       // the currency sits in the header, not every cell
                dgvReview.Columns["PriceYouPay"].FillWeight = 45;                // matches Strength, so the middle columns balance
                dgvReview.Columns["LineTotal"].Visible = true;                   // quantity times unit price, multiplied by the query
                dgvReview.Columns["LineTotal"].HeaderText = "Line total (Tk)";   // spelled out: this is what adds up to the bill
                dgvReview.Columns["LineTotal"].FillWeight = 55;                  // the widest weighted column, because its header is longest
            }

            // From the GROUPED query, so this is the figure Orders.ItemsTotal will hold.
            decimal itemsTotal = Convert.ToDecimal(CurrentGroup["ItemsTotal"]);
            // The undiscounted figure, used only to quote the saving at the bottom.
            decimal beforeDiscount = Convert.ToDecimal(CurrentGroup["BeforeDiscount"]);

            // UiTheme.Money is the one money format in the application.
            lblItemsValue.Text = UiTheme.Money(itemsTotal);
            lblDeliveryValue.Text = UiTheme.Money(DeliveryCharge);   // per shop, so a two-shop basket pays twice
            // Mirrors Orders.TotalAmount, a PERSISTED column; added here only to show it.
            lblPayableValue.Text = UiTheme.Money(itemsTotal + DeliveryCharge);

            // Two wordings of one promise: these prices are what OrderItems will store.
            lblStatus.Text = beforeDiscount > itemsTotal
                // The discount branch: the saving is the difference between the two totals.
                ? "Today's offers have already taken " + UiTheme.Money(beforeDiscount - itemsTotal) +
                  // The second half of that sentence, and the promise the other branch makes too.
                  " off this order. The prices above are what will be written into OrderItems."
                // The no-offer branch: still worth saying, because a frozen price is the point.
                : "The prices above are what will be written into OrderItems, so this invoice can never change later.";
        }

        // ===== VALIDATION =====

        // A property, so the visible box and the validated box ask one question.
        private bool NeedsMobileNumber =>
            cmbPayment.SelectedIndex == 2 || cmbPayment.SelectedIndex == 3;   // bKash or Nagad

        // Wired to the combo, so it runs on every change including Load's own.
        private void cmbPayment_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Hidden, not disabled: a wallet box on a cash order only raises a question.
            lblMobile.Visible = NeedsMobileNumber;
            txtMobile.Visible = NeedsMobileNumber;   // box and label shown together, so no label sits over nothing
            // Any error there belonged to the previous method, so it is dropped.
            lblMobileError.Visible = false;

            // No else: a hidden field's caption does not matter, so the old text can stay.
            if (NeedsMobileNumber)
                // Named from the combo's own text, so bKash and Nagad cannot be mislabelled.
                lblMobile.Text = cmbPayment.SelectedItem.ToString() + " number (11 digits)";

            ValidateAll();   // the rule set may have changed, so Confirm is re-decided
        }

        // Expression bodied, because there is nothing to do but re-validate.
        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        // Returns the verdict as well as painting it, so Confirm can re-check it.
        private bool ValidateAll()
        {
            bool ok = true;   // starts true and is only ever narrowed by the rules below

            // &=, not &&=: every Check must run, so every field gets its label in one pass.
            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        // Plain language, because the message is read by a customer.
                        "We need somewhere to deliver to.");

            // > 0, not >= 0, because index 0 is the prompt, which is the unchosen state.
            ok &= Check(cmbPayment.SelectedIndex > 0, lblPaymentError, cmbPayment,
                        // Names the action, not the field, so it tells the user what to do.
                        "Choose a payment method.");

            // The same property the visibility used, so the two can never disagree.
            if (NeedsMobileNumber)
            {
                // The same 11 digit rule as sign up, from the same Validator method.
                ok &= Check(Validator.IsMobile(txtMobile.Text), lblMobileError, txtMobile,
                            // Says WHICH number: not the customer's own, but the wallet's.
                            "Enter the 11 digit number the payment will come from.");
            }
            else   // without this, a wallet's red label would survive a switch to cash
            {
                // Clear rather than skip, so a half typed number leaves nothing behind.
                UiTheme.ClearError(lblMobileError, txtMobile);
            }

            // Read off the control ShowCurrentPharmacy set, so rule and screen agree.
            bool needsRx = lblRxWarning.Visible;
            // No Check() call, so no red label: the amber notice already says it.
            if (needsRx && string.IsNullOrEmpty(_pendingRxImagePath)) ok = false;

            btnConfirm.Enabled = ok;   // the visible half of the verdict
            // Success green, because this button completes a purchase; grey when disabled.
            btnConfirm.BackColor = ok ? UiTheme.Success : Color.FromArgb(170, 190, 184);
            return ok;   // handed back, so a caller need not re-derive the verdict
        }

        // Takes the outcome as a bool, so the rule stays readable at the call site.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // The same helper as the sign up form, so every form fails the same way.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);   // shows the message and tints the field
            return rulePassed;   // passed straight back for the caller's running verdict
        }

        // A switch, not a lookup table: the compiler checks every branch returns.
        private string PaymentMethodForDatabase()
        {
            // Turns the combo position into the exact string CK_Orders_Payment accepts.
            switch (cmbPayment.SelectedIndex)
            {
                case 1: return "CashOnDelivery";   // no space: the constraint's value, not the label
                case 2: return "bKash";            // the brand's own lower-case b, matching exactly
                case 3: return "Nagad";            // screen text and stored value happen to agree
                case 4: return "Card";             // the scheme is the gateway's business, not the order's
                // Index 0 is the prompt, already blocked, so this branch is never reached.
                default: return "";
            }
        }

        // ===== PRESCRIPTION =====

        // Collects the file only: the order it belongs to does not exist yet.
        private void btnUploadRx_Click(object sender, EventArgs e)
        {
            // using(), because a form shown with ShowDialog is not disposed by closing.
            using (UploadPrescriptionForm upload = new UploadPrescriptionForm(CurrentPharmacyName))
            {
                // Testing the result means Cancel leaves an attached file untouched.
                if (upload.ShowDialog(this) == DialogResult.OK)
                {
                    // Only a path comes back: the row waits until an OrderId exists.
                    _pendingRxImagePath = upload.SelectedImagePath;
                    _pendingRxDoctorName = upload.DoctorName;   // optional, so an empty string is a valid answer

                    // The file NAME only, or a long directory pushes the rest off the label.
                    lblRxState.Text = "Attached: " + Path.GetFileName(_pendingRxImagePath) +
                                      // IsNullOrWhiteSpace, so a name of spaces prints no bracket.
                                      (string.IsNullOrWhiteSpace(_pendingRxDoctorName)
                                          // Append nothing when there is no name, the whole bracket when there is.
                                          ? "" : "   (Dr " + _pendingRxDoctorName + ")");
                    lblRxState.ForeColor = UiTheme.Success;   // red to green is the only confirmation given
                }
            }

            // Outside the using, because attaching a file raises no event of its own.
            ValidateAll();
        }

        // ===== CONFIRM =====

        // The one method that commits anything; everything above prepares for it.
        private void btnConfirm_Click(object sender, EventArgs e)
        {
            // Re-validate on entry: an enabled button is a mouse guard, not a keyboard one.
            if (!ValidateAll()) return;

            Cursor = Cursors.WaitCursor;     // a multi statement transaction is about to run
            try   // try/finally, so the cursor is restored on every path out of the method
            {
                string message;             // the service writes the refusal text here
                // One call, scoped to THIS shop, so the other half stays in the cart.
                int orderId = _orders.Checkout(UserSession.UserId, CurrentPharmacyId,
                                               // Trimmed, or a trailing space would reach the invoice.
                                               txtAddress.Text.Trim(), PaymentMethodForDatabase(),
                                               // Passed in, so the stored figure is the one just shown.
                                               DeliveryCharge, out message);

                // Tested first, because every line after this assumes a committed order.
                if (orderId == 0)
                {
                    // 0 is a safe sentinel: OrderId is an IDENTITY that starts at 1001.
                    MessageBox.Show(message, "Order not placed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;   // leaves the form open, so the basket can be fixed and retried
                }

                // ORDER MATTERS: Prescriptions.OrderId is a foreign key, so this waits.
                if (!string.IsNullOrEmpty(_pendingRxImagePath))
                {
                    string rxMessage;   // the service needs an out target, then it is ignored
                    // Not used to abort: a committed order cannot be unwound by a file copy.
                    _prescriptions.Upload(orderId, UserSession.UserId, _pendingRxImagePath,
                                          // A blank name is stored as NULL, so there is one empty case.
                                          _pendingRxDoctorName, out rxMessage);
                }

                Cursor = Cursors.Default;   // the database work is done; the rest is on screen
                // using(), because ShowDialog does not dispose the form the way Show does.
                using (InvoiceForm invoice = new InvoiceForm(orderId))
                {
                    // Built from the OrderId alone, so it prints what was really stored.
                    invoice.ShowDialog(this);
                }

                MoveToNextPharmacyOrFinish();   // only after the order is committed and seen
            }
            catch (Exception ex)   // the service rolls its transaction back and rethrows
            {
                // By the time this runs no partial order exists, so nothing is undone here.
                MessageBox.Show("The order could not be placed.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // Error: this one is a genuine fault
            }
            finally   // the only construct that runs after the early return AND the catch
            {
                // Covers the early return and the catch, which both skip the line above.
                Cursor = Cursors.Default;
            }
        }

        // Called only after a successful order: the 'one pharmacy at a time' loop.
        private void MoveToNextPharmacyOrFinish()
        {
            // The ONLY state change needed: the three Current properties derive from it.
            _currentIndex++;

            // Strictly less than: the index was just moved past the shop just ordered from.
            if (_currentIndex < _pharmacyGroups.Rows.Count)
            {
                // The snapshot still holds: the order just placed cleared only its own lines.
                MessageBox.Show(
                    // Confirms what happened before introducing what happens next.
                    "That order is placed.\r\n\r\n" +
                    // Reads the name off the snapshot at the NEW index, the shop about to show.
                    "Your basket also contains items from " + _pharmacyGroups.Rows[_currentIndex]["PharmacyName"] +
                    // Names the reason: separate pharmacy, separate order, separate invoice.
                    ", which is a separate pharmacy and therefore a separate order. " +
                    // Ends on the next action, so the OK button has an obvious meaning.
                    "Let us finish that one now.",
                    "Next pharmacy", MessageBoxButtons.OK, MessageBoxIcon.Information);   // Information: progress, not a problem

                ShowCurrentPharmacy();   // repaint everything for the new shop
                return;                  // and stop, because the form stays open
            }

            // Past the last row: every shop is ordered from and the cart is now empty.
            MessageBox.Show(
                // States both facts wanted at the end: orders exist, cart is clear.
                "All done. Every order has been placed and your cart is now empty.\r\n\r\n" +
                // Says where the invoices live, so nobody feels they must print this one now.
                "You can reopen any invoice at any time from My Orders.",
                "Thank you", MessageBoxButtons.OK, MessageBoxIcon.Information);   // the caption closes the transaction

            // OK tells the cart screen something was placed, so it can refresh itself.
            DialogResult = DialogResult.OK;
            Close();   // explicit, so closing is intent rather than a side effect
        }

        // Leaving deliberately, not finishing. Nothing here touches the database.
        private void btnCancel_Click(object sender, EventArgs e)
        {
            // Confirmation first, because one of the two orders may already exist.
            DialogResult answer = MessageBox.Show(
                // The question first, then what happens to the half already ordered.
                "Leave the checkout?\r\n\r\nAnything you have already paid for stays ordered; " +
                // And to the half that is not: nothing, which is the reassurance.
                "the rest of your basket is left untouched.",
                "Leave checkout", MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // YesNo, so there is no ambiguous Cancel

            // != Yes, so the window's own close button counts as 'do not leave' as well.
            if (answer != DialogResult.Yes) return;

            // Cancel, because the remaining lines are still in the cart, untouched.
            DialogResult = DialogResult.Cancel;
            Close();   // the cart screen sees Cancel and leaves its own grid alone
        }
    }
}
