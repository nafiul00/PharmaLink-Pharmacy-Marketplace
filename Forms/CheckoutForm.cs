using System.Configuration;         // ConfigurationManager, which reads DeliveryCharge out of App.config
using System.Data;                  // DataTable, DataRow and DataView - the DataView is what this form filters in memory
using System.Drawing;               // Font and Color for ApplyTheme and the disabled button
using System.Windows.Forms;         // Form, DataGridView, MessageBox, Cursors
using PharmaLinkApp.Helpers;        // UiTheme (styling, Money, the error helpers) and Validator (field rules)
using PharmaLinkApp.Models;         // User, used only to pre-fill the delivery address from the profile
using PharmaLinkApp.Services;       // Cart, Order, Prescription and Auth services - this form calls four of them

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirements 25 and 26.
    ///
    /// A basket that spans two pharmacies becomes two orders, so this form runs
    /// once per pharmacy and the heading says "Order 1 of 2". Confirm runs a
    /// single transaction that writes the order header, writes one OrderItems
    /// row per cart line at the price the customer was shown, reduces the stock,
    /// freezes the commission and clears only that pharmacy's cart lines.
    /// </summary>
    public partial class CheckoutForm : Form
    {
        // Four services, because a checkout touches four areas: the basket is read, the order
        // is written, a prescription may be attached and the customer's profile supplies the
        // default address. Each one is stateless and holds no connection, so they can all be
        // created once here rather than per click.
        private readonly CartService _cart = new CartService();
        private readonly OrderService _orders = new OrderService();
        private readonly PrescriptionService _prescriptions = new PrescriptionService();
        private readonly AuthService _auth = new AuthService();

        // The basket grouped by shop, fetched ONCE in Load, and the position within it. This
        // pair is the whole "one pharmacy at a time" mechanism: the rows never change while
        // the form is open, and advancing _currentIndex is the only thing that moves the form
        // on to the next order.
        private DataTable _pharmacyGroups;
        private int _currentIndex;

        // The prescription the user picked, held in memory until an order exists to attach it
        // to. Prescriptions.OrderId is a foreign key, so nothing can be written at the moment
        // the file is chosen - see the comment in btnConfirm_Click. Empty string rather than
        // null so every test below is a plain IsNullOrEmpty with no null guard of its own.
        private string _pendingRxImagePath = "";
        private string _pendingRxDoctorName = "";

        public CheckoutForm()
        {
            // Designer generated. Everything that reads the database happens in Load, where an
            // empty cart can close the form cleanly instead of throwing inside a constructor.
            InitializeComponent();
        }

        /// <summary>
        /// The flat delivery charge, per pharmacy, read from App.config.
        /// </summary>
        private static decimal DeliveryCharge
        {
            get
            {
                // A property rather than a const, so the charge can be changed in App.config
                // without rebuilding the application; static because it depends on nothing in
                // any particular form. ConfigurationManager caches the file after the first
                // read, so calling this repeatedly costs nothing measurable.
                string configured = ConfigurationManager.AppSettings["DeliveryCharge"];
                decimal value;
                // TryParse, not Parse: a missing key returns null and a mistyped value returns
                // something like "sixty", and either would throw from Parse - inside a property
                // that every total on this screen depends on. Falling back to 60 means a broken
                // configuration file produces the documented default rather than a crash at
                // checkout. The m suffix keeps it decimal; a double would introduce a binary
                // rounding error into a money figure that ends up in Orders.DeliveryCharge.
                return decimal.TryParse(configured, out value) ? value : 60m;
            }
        }

        private void CheckoutForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            // Index 0 is a prompt, not a payment method. Having it there means the combo starts
            // in a state that means "nothing chosen yet", which ValidateAll tests as
            // SelectedIndex > 0, and PaymentMethodForDatabase maps to the empty string. The
            // alternative - starting on Cash on delivery - would let a customer place an order
            // by a method they never actually picked.
            cmbPayment.Items.AddRange(new object[]
            {
                "- choose how you will pay -",
                "Cash on delivery",
                "bKash",
                "Nagad",
                "Card"
            });
            // The order of these five items is load bearing: NeedsMobileNumber and
            // PaymentMethodForDatabase both switch on the index, so moving a line here would
            // silently change what gets written into Orders.PaymentMethod.
            cmbPayment.SelectedIndex = 0;

            // The delivery address is pre-filled from the profile but stays editable.
            // Editable because the delivery address belongs to the ORDER, not to the account:
            // Orders.DeliveryAddress is written from this box, so sending one parcel to work
            // and the next one home must not require editing the profile.
            User me = _auth.GetUser(UserSession.UserId);
            // UserSession.UserId, never a value taken from a control - the same rule the whole
            // application follows, and the reason no customer can check out as another one.
            if (me != null) txtAddress.Text = me.Address;
            // Null guard because GetUser returns null when no row matches. A missing profile
            // simply leaves the box empty, and ValidateAll then refuses to enable Confirm.

            // ONE query for the whole basket, grouped by shop. Each row is one order that is
            // about to be created, and it carries the totals already calculated in SQL, so the
            // figures on this screen and the figures written into Orders come from the same
            // expression. Fetching it once, here, is what makes the step through the shops
            // stable even as each confirmed order deletes its own cart lines.
            _pharmacyGroups = _cart.GetPharmacyGroups(UserSession.UserId, DeliveryCharge);

            if (_pharmacyGroups.Rows.Count == 0)
            {
                // An empty basket is not an error, so it gets a plain message and a closed
                // window rather than a red label on a form with nothing to check out.
                MessageBox.Show("Your cart is empty.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.Cancel;
                Close();
                // The return is essential and not tidiness. Close() during Load does not abort
                // the handler - the rest of the method still runs - so without it
                // ShowCurrentPharmacy would execute against a table with no rows and
                // _pharmacyGroups.Rows[0] would throw before the window had even appeared.
                return;
            }

            ShowCurrentPharmacy();   // paint the first shop; _currentIndex is still 0
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Checkout");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            foreach (GroupBox group in new[] { grpDelivery, grpReview })
            {
                group.Font = UiTheme.FontHeading;
                group.ForeColor = UiTheme.Primary;
                group.BackColor = UiTheme.CardBack;

                foreach (Control child in group.Controls)
                {
                    child.Font = UiTheme.FontBody;
                    child.ForeColor = UiTheme.TextDark;
                    if (child is Label label && label.Name.EndsWith("Error"))
                    {
                        label.Font = UiTheme.FontSmall;
                        label.ForeColor = UiTheme.Danger;
                    }
                }
            }

            lblPayableCaption.Font = UiTheme.FontHeading;
            lblPayableValue.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
            lblPayableValue.ForeColor = UiTheme.Primary;

            lblRxWarning.Font = UiTheme.FontSmall;
            lblRxState.Font = UiTheme.FontSmall;
            lblHint.Font = UiTheme.FontMono;
            lblHint.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnCancel);
            UiTheme.StyleAccent(btnUploadRx);
            UiTheme.StyleSuccess(btnConfirm);
            btnConfirm.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            UiTheme.StyleGrid(dgvReview);

            lblHint.Text =
                "Confirm Order runs these five statements inside ONE transaction, all scoped to this pharmacy:" + Environment.NewLine +
                "   1. INSERT INTO Orders      - the header, with CommissionAmount frozen at this pharmacy's rate today" + Environment.NewLine +
                "   2. INSERT INTO OrderItems  - one line per cart row, at the discounted price shown on the right" + Environment.NewLine +
                "   3. UPDATE Medicines        - take the units off the shelf" + Environment.NewLine +
                "   4. DELETE FROM Cart        - clear only this pharmacy's lines; the rest becomes the next order" + Environment.NewLine +
                "   5. COMMIT                  - if any statement fails the whole thing is rolled back, so no order can exist without its items";
        }

        // ---------------------------------------------------------------------
        //  ONE PHARMACY AT A TIME
        // ---------------------------------------------------------------------

        // Three expression bodied properties, all derived from _currentIndex. Because they are
        // computed on every read rather than copied into fields, incrementing the index in
        // MoveToNextPharmacyOrFinish is the single act that moves the form to the next order -
        // there is no second copy of "which shop are we on" that could fall out of step.
        private DataRow CurrentGroup => _pharmacyGroups.Rows[_currentIndex];
        // Convert.ToInt32 rather than a cast: a DataRow indexer returns object, and the value
        // arrives boxed as whatever type the column is. Convert handles that, and would also
        // handle the column coming back as a different numeric type.
        private int CurrentPharmacyId => Convert.ToInt32(CurrentGroup["PharmacyId"]);
        private string CurrentPharmacyName => CurrentGroup["PharmacyName"].ToString();

        private void ShowCurrentPharmacy()
        {
            // Reset the prescription FIRST. A prescription belongs to one order, and this
            // method runs again for the second shop after the first order has been placed. If
            // these two were not cleared, the photograph attached to order 1 would still be
            // sitting in the field and would be uploaded a second time against order 2.
            _pendingRxImagePath = "";
            _pendingRxDoctorName = "";

            // "Order 1 of 2" states plainly that a two shop basket is two orders, so the
            // second invoice is not a surprise. +1 because _currentIndex counts from zero.
            lblTitle.Text = "Order " + (_currentIndex + 1) + " of " + _pharmacyGroups.Rows.Count;
            lblSubtitle.Text = CurrentPharmacyName + "   -   this half of your basket is a separate order with its own invoice.";

            LoadReviewGrid();       // the lines and the totals for this shop only

            // Asked per pharmacy, not per basket: the second argument scopes the question to
            // the shop being checked out now. A prescription item in the OTHER half of the
            // basket must not block this order, and must still block that one.
            bool needsRx = _cart.ContainsPrescriptionItem(UserSession.UserId, CurrentPharmacyId);

            // The three prescription controls appear only when they are relevant. A customer
            // buying paracetamol never sees an upload button, and the form does not have to
            // explain why it is disabled.
            lblRxWarning.Visible = needsRx;
            btnUploadRx.Visible = needsRx;
            lblRxState.Visible = needsRx;

            if (needsRx)
            {
                // The text is set here rather than in the designer because it is only ever
                // shown on this branch, and stating the accepted formats and the size limit up
                // front saves a rejected upload - PrescriptionService enforces exactly these
                // two rules when the file is finally copied.
                lblRxWarning.Text = "This order contains a prescription only medicine. You cannot confirm it until you " +
                                    "attach a photograph of your doctor's prescription (JPG or PNG, under 2 MB).";
                lblRxWarning.ForeColor = UiTheme.Warning;
                // Amber, not red: nothing is wrong yet, something is simply still required.
                lblRxState.Text = "No prescription attached yet.";
                lblRxState.ForeColor = UiTheme.Danger;
                // The state line IS red, because this one is a blocking condition - ValidateAll
                // refuses to enable Confirm while it says this.
            }

            ValidateAll();
            // Re-run at the end, because needsRx may just have made a new rule apply and the
            // Confirm button must reflect that before the user touches anything.
        }

        private void LoadReviewGrid()
        {
            // Fetch the WHOLE basket, every pharmacy, in one query.
            DataTable allLines = _cart.GetLinesTable(UserSession.UserId);

            // Then narrow it IN MEMORY to the pharmacy being checked out right now.
            // This is the only client side filter in the entire application - every
            // other screen filters in SQL with a WHERE clause. It is justified here
            // because the form is stepping through the same basket pharmacy by
            // pharmacy, so the rows are already loaded and re-querying per step would
            // be a round trip for data we are holding.
            //
            // RowFilter takes a DataColumn expression, not SQL. Concatenating the id
            // is safe because CurrentPharmacyId is an int read from a DataRow the
            // database gave us, never text a user typed.
            //
            // The property is typed int, so the compiler itself guarantees that only digits
            // can reach the concatenation - the same guarantee a parameter would give, obtained
            // here by the type rather than by SqlParameter. The rule this obeys is: text a user
            // typed is never concatenated into anything a query engine reads, and no value on
            // this line came from a control.
            DataView view = new DataView(allLines);
            // A DataView is a filtered window ONTO allLines, not a copy of it. Building one is
            // cheap because nothing is duplicated until the next line.
            view.RowFilter = "PharmacyId = " + CurrentPharmacyId;
            DataTable mine = view.ToTable();      // materialise the filtered view
            // ToTable makes an independent DataTable of just the matching rows. Binding the
            // grid to the view directly would work, but the grid would then be holding a live
            // window over the full basket; a detached table is what this screen actually means
            // and it cannot be disturbed by anything that later touches allLines.

            dgvReview.DataSource = mine;
            // Assigning DataSource is what creates the grid's columns, which is why every line
            // below has to come after it and not before.

            if (dgvReview.Columns.Count > 0)
            {
                // The guard matters because the column collection is empty until a DataSource
                // with columns has been bound. Without it, every Columns["..."] lookup below
                // would throw a null reference on a basket the query returned nothing for.

                // Hide everything, then re-show the five columns a customer needs. Written this
                // way round because the query returns more than it displays - CartId,
                // MedicineId, PharmacyId, ListPrice, DiscountPercent, Stock and RequiresRx are
                // all used by other screens - and hiding by exception means a column added to
                // the query later stays hidden here rather than appearing unlabelled.
                foreach (DataGridViewColumn column in dgvReview.Columns) column.Visible = false;

                // HeaderText overrides the column name from SQL, so the grid says "Qty" and
                // "Unit (Tk)" instead of the database's own vocabulary. FillWeight is a
                // proportion, not a pixel count, so the widths stay sensible when the form is
                // resized; Medicine has no weight set and keeps the default, which makes it the
                // widest column and the one that absorbs the remaining space.
                dgvReview.Columns["MedicineName"].Visible = true;
                dgvReview.Columns["MedicineName"].HeaderText = "Medicine";
                dgvReview.Columns["Strength"].Visible = true;
                dgvReview.Columns["Strength"].HeaderText = "Strength";
                dgvReview.Columns["Strength"].FillWeight = 45;
                dgvReview.Columns["Quantity"].Visible = true;
                dgvReview.Columns["Quantity"].HeaderText = "Qty";
                dgvReview.Columns["Quantity"].FillWeight = 30;
                // PriceYouPay and LineTotal are both calculated by the cart query with today's
                // offer already applied. Showing the database's own figures, rather than
                // multiplying anything in C#, is what guarantees the grid, the totals below and
                // the invoice can never disagree by a rounding step.
                dgvReview.Columns["PriceYouPay"].Visible = true;
                dgvReview.Columns["PriceYouPay"].HeaderText = "Unit (Tk)";
                dgvReview.Columns["PriceYouPay"].FillWeight = 45;
                dgvReview.Columns["LineTotal"].Visible = true;
                dgvReview.Columns["LineTotal"].HeaderText = "Line total (Tk)";
                dgvReview.Columns["LineTotal"].FillWeight = 55;
            }

            // The two totals come from the GROUPED query, not from adding up the grid. SQL
            // summed them with the same expression the checkout will use to write
            // Orders.ItemsTotal, so what the customer is shown is arithmetically the same
            // number that is about to be stored.
            decimal itemsTotal = Convert.ToDecimal(CurrentGroup["ItemsTotal"]);
            decimal beforeDiscount = Convert.ToDecimal(CurrentGroup["BeforeDiscount"]);

            // UiTheme.Money formats every amount in the application as "Tk 1,234.00", so no
            // screen invents its own money format.
            lblItemsValue.Text = UiTheme.Money(itemsTotal);
            lblDeliveryValue.Text = UiTheme.Money(DeliveryCharge);
            // The payable figure mirrors Orders.TotalAmount, which is a PERSISTED computed
            // column defined as ItemsTotal + DeliveryCharge. The addition is written here only
            // so the customer can see it before the row exists; the stored total is still
            // computed by the database and cannot drift from its two parts.
            lblPayableValue.Text = UiTheme.Money(itemsTotal + DeliveryCharge);

            // Two versions of the same reassurance. When an offer is running, name the saving -
            // the difference between the undiscounted total and what is being charged - because
            // that is the number a customer wants to see. When none is, still state plainly
            // that these prices are the ones being written into OrderItems, which is what makes
            // a re-opened invoice show the same figures months later even if prices have moved.
            lblStatus.Text = beforeDiscount > itemsTotal
                ? "Today's offers have already taken " + UiTheme.Money(beforeDiscount - itemsTotal) +
                  " off this order. The prices above are what will be written into OrderItems."
                : "The prices above are what will be written into OrderItems, so this invoice can never change later.";
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        // Indexes 2 and 3 are bKash and Nagad, counted from the AddRange in Load. Both are
        // mobile wallets, so the payment has to come from a number; cash and card do not need
        // one. Expressed as a property so the visibility code and the validation code ask the
        // same question rather than each testing the index themselves.
        private bool NeedsMobileNumber =>
            cmbPayment.SelectedIndex == 2 || cmbPayment.SelectedIndex == 3;   // bKash or Nagad

        private void cmbPayment_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Choosing bKash or Nagad reveals the mobile number field.
            // Progressive disclosure: the field is hidden rather than disabled, because a
            // permanently visible wallet number box on a cash on delivery order is a question
            // the customer has to work out the answer to.
            lblMobile.Visible = NeedsMobileNumber;
            txtMobile.Visible = NeedsMobileNumber;
            // Any error under that box belonged to the previous payment method, so it is
            // dropped whenever the method changes rather than left pointing at a hidden field.
            lblMobileError.Visible = false;

            if (NeedsMobileNumber)
                // The label names the wallet the user actually chose, taken from the combo's
                // own text, so one line serves both bKash and Nagad and the two can never be
                // mislabelled by a copied string.
                lblMobile.Text = cmbPayment.SelectedItem.ToString() + " number (11 digits)";

            ValidateAll();
            // Changing the payment method changes the rule set - it can add or remove the
            // mobile number requirement - so the Confirm button has to be re-decided here.
        }

        // Expression bodied because there is genuinely nothing to do but re-validate. Wired to
        // the address and mobile boxes, so the verdict tracks every keystroke.
        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        private bool ValidateAll()
        {
            bool ok = true;

            // &= again, not &&=: every Check must run so that every field gets its label in one
            // pass. Short circuiting would report the address and stay silent about the
            // payment method until the address was fixed.

            // Orders.DeliveryAddress is NOT NULL, and an order with nowhere to go is useless
            // even to a database that would accept an empty string, so the rule is here.
            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "We need somewhere to deliver to.");

            // > 0, not >= 0, because index 0 is the "- choose how you will pay -" prompt. This
            // is why the prompt was added: it gives the combo a genuine unchosen state that can
            // be refused, which a list of four real methods could not have.
            ok &= Check(cmbPayment.SelectedIndex > 0, lblPaymentError, cmbPayment,
                        "Choose a payment method.");

            if (NeedsMobileNumber)
            {
                // The same 11 digit rule the sign up form applies, from the same Validator
                // method, so a wallet number is checked exactly as a contact number is.
                ok &= Check(Validator.IsMobile(txtMobile.Text), lblMobileError, txtMobile,
                            "Enter the 11 digit number the payment will come from.");
            }
            else
            {
                // Clear rather than skip. If the user typed a half finished number and then
                // switched to cash, the red label would otherwise stay on screen under a hidden
                // box, blocking nothing and explaining nothing.
                UiTheme.ClearError(lblMobileError, txtMobile);
            }

            // The prescription requirement is read back off the control that ShowCurrentPharmacy
            // set from the database check. Using the visible state keeps the rule and what the
            // user can actually see in exact agreement, and avoids asking the database the same
            // question again on every keystroke.
            bool needsRx = lblRxWarning.Visible;
            // No Check() call here, so no red label: the requirement is already spelled out in
            // the amber warning and the red state line beside the upload button. A third
            // message would repeat what is on screen twice already.
            if (needsRx && string.IsNullOrEmpty(_pendingRxImagePath)) ok = false;

            btnConfirm.Enabled = ok;
            // Success green rather than the theme's primary, because this button completes a
            // purchase; the grey is the same disabled colour every other form uses.
            btnConfirm.BackColor = ok ? UiTheme.Success : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // The same helper as the sign up form: paint the outcome, hand the verdict back, so
            // a rule and its message stay on one line at the call site.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        private string PaymentMethodForDatabase()
        {
            // Translates the combo's position into the exact string CK_Orders_Payment accepts.
            // The user reads "Cash on delivery"; the column stores 'CashOnDelivery'. Keeping
            // the two vocabularies apart means the wording on screen can be changed without
            // touching a CHECK constraint, and the constraint can be trusted because only these
            // four strings are ever produced.
            switch (cmbPayment.SelectedIndex)
            {
                case 1: return "CashOnDelivery";
                case 2: return "bKash";
                case 3: return "Nagad";
                case 4: return "Card";
                // Index 0 is the prompt. It returns the empty string, which CK_Orders_Payment
                // would refuse - but ValidateAll has already blocked that path, so this branch
                // exists to make the method total rather than to be reached.
                default: return "";
            }
        }

        // ---------------------------------------------------------------------
        //  PRESCRIPTION
        // ---------------------------------------------------------------------

        private void btnUploadRx_Click(object sender, EventArgs e)
        {
            // using() so the dialog is disposed on every path: a form shown with ShowDialog is
            // not disposed by closing, unlike one shown with Show. The pharmacy name is passed
            // in so the dialog can tell the customer which of their two orders the photograph
            // is going to be attached to.
            using (UploadPrescriptionForm upload = new UploadPrescriptionForm(CurrentPharmacyName))
            {
                // ShowDialog(this) sets the owner, which centres the dialog over this form and
                // keeps it above it. Testing the result means Cancel leaves the previously
                // attached file, if any, untouched.
                if (upload.ShowDialog(this) == DialogResult.OK)
                {
                    // Only a path and a name come back. The dialog deliberately does not copy
                    // the file or write a row, because Prescriptions.OrderId is a foreign key
                    // and no order exists yet - see btnConfirm_Click, where both happen.
                    _pendingRxImagePath = upload.SelectedImagePath;
                    _pendingRxDoctorName = upload.DoctorName;

                    // Show the file NAME only, not the whole path: the customer recognises the
                    // file they chose, and a long directory would push the doctor's name off
                    // the end of the label.
                    lblRxState.Text = "Attached: " + Path.GetFileName(_pendingRxImagePath) +
                                      (string.IsNullOrWhiteSpace(_pendingRxDoctorName)
                                          ? "" : "   (Dr " + _pendingRxDoctorName + ")");
                    // The doctor's name is optional, so the conditional appends nothing rather
                    // than leaving a stray "(Dr )" on screen.
                    lblRxState.ForeColor = UiTheme.Success;
                    // Red to green is the only feedback that this blocking requirement has been
                    // satisfied, which is why the colour is set here and not in the designer.
                }
            }

            ValidateAll();
            // Outside the using and outside the if, so it runs on the cancel path too. This is
            // the call that turns the Confirm button green once a prescription is attached -
            // nothing else re-evaluates that rule, because attaching a file raises no
            // TextChanged event.
        }

        // ---------------------------------------------------------------------
        //  CONFIRM
        // ---------------------------------------------------------------------

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            // Re-validate on entry, for the same reason as every other form here: an enabled
            // button is a mouse guard, not a keyboard guard, and the state may have moved on
            // since it was last decided.
            if (!ValidateAll()) return;

            Cursor = Cursors.WaitCursor;     // a multi statement transaction is about to run
            try
            {
                string message;              // the service writes the refusal text here
                // One call runs the whole five statement transaction for THIS pharmacy
                // only. PaymentMethodForDatabase() converts the friendly combo text
                // ('Cash on delivery') into the stored value ('CashOnDelivery') that
                // CK_Orders_Payment accepts - the UI wording and the database
                // vocabulary are deliberately separate.
                //
                // The address is trimmed because it is typed by hand and a trailing space would
                // be stored in Orders.DeliveryAddress and printed on the invoice. CurrentPharmacyId
                // is what scopes every one of the five statements to this shop, which is what
                // leaves the other shop's lines in the cart to become the next order.
                int orderId = _orders.Checkout(UserSession.UserId, CurrentPharmacyId,
                                               txtAddress.Text.Trim(), PaymentMethodForDatabase(),
                                               DeliveryCharge, out message);

                if (orderId == 0)
                {
                    // 0 means the transaction rolled back - almost always because a
                    // medicine sold out between adding it and confirming. message
                    // names the medicine, so show it rather than a generic failure.
                    //
                    // 0 is a safe sentinel because Orders.OrderId is an IDENTITY starting at
                    // 1001, so a real order can never have that number. Returning it instead of
                    // throwing says that selling out is an ordinary outcome, not a fault - and
                    // because the transaction rolled back, there is nothing to undo here.
                    MessageBox.Show(message, "Order not placed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // ORDER OF OPERATIONS MATTERS HERE. Prescriptions.OrderId is a foreign
                // key to Orders, so the prescription row CANNOT be written until the
                // order exists and has been given its identity. That is also why
                // UploadPrescriptionForm only collects the file and hands back a path:
                // at the moment the user chose the image there was no OrderId to attach
                // it to. The upload copies the file and inserts the row here instead.
                if (!string.IsNullOrEmpty(_pendingRxImagePath))
                {
                    string rxMessage;
                    // The result is deliberately not used to abort anything: the order is
                    // already committed and cannot be unwound by a failed file copy. A failure
                    // here leaves an order with no attached image, which the pharmacy can chase
                    // up, rather than a paid-for order that silently vanished.
                    _prescriptions.Upload(orderId, UserSession.UserId, _pendingRxImagePath,
                                          _pendingRxDoctorName, out rxMessage);
                }

                Cursor = Cursors.Default;
                // Restored BEFORE the invoice dialog rather than in finally alone, because
                // ShowDialog below blocks for as long as the customer reads the invoice and a
                // wait cursor hanging over it would suggest the application had frozen. The
                // finally block still runs afterwards, which makes this line safe to repeat.

                using (InvoiceForm invoice = new InvoiceForm(orderId))
                {
                    // Modal, so the customer sees the invoice for the order just placed before
                    // being moved on to the next pharmacy. It is constructed from the OrderId
                    // alone and re-reads everything from the database, so what is printed is
                    // what was actually stored rather than what this form was holding.
                    invoice.ShowDialog(this);
                }

                MoveToNextPharmacyOrFinish();
            }
            catch (Exception ex)
            {
                // The transaction inside OrderService rolls itself back and rethrows, so by the
                // time this runs no partial order exists. Reporting it in a message box rather
                // than a label because the user's attention is on the button they just pressed.
                MessageBox.Show("The order could not be placed.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Covers the early return on a rolled back order and the exception path, both
                // of which skip the Cursors.Default set in the middle of the try.
                Cursor = Cursors.Default;
            }
        }

        private void MoveToNextPharmacyOrFinish()
        {
            // Advancing the index is the ONLY state change needed to move on: CurrentGroup,
            // CurrentPharmacyId and CurrentPharmacyName are all computed from it, so the next
            // call to ShowCurrentPharmacy paints the next shop with no other bookkeeping.
            _currentIndex++;

            if (_currentIndex < _pharmacyGroups.Rows.Count)
            {
                // _pharmacyGroups is the snapshot taken in Load, and the order just placed
                // deleted only its own cart lines, so the row waiting at the new index still
                // describes exactly what is left in the basket. Re-querying here would work too
                // but would be a round trip for data that cannot have changed.
                MessageBox.Show(
                    "That order is placed.\r\n\r\n" +
                    "Your basket also contains items from " + _pharmacyGroups.Rows[_currentIndex]["PharmacyName"] +
                    ", which is a separate pharmacy and therefore a separate order. " +
                    "Let us finish that one now.",
                    "Next pharmacy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // The message explains WHY there is a second checkout before the form changes
                // under the customer's hands, so a second payment screen does not read as an
                // error or a double charge.

                ShowCurrentPharmacy();   // repaint everything for the new shop
                return;                  // and stop, because the form stays open
            }

            // Past the last row, so every pharmacy in the basket has been ordered from and the
            // cart is now empty - each transaction deleted its own lines as it committed.
            MessageBox.Show(
                "All done. Every order has been placed and your cart is now empty.\r\n\r\n" +
                "You can reopen any invoice at any time from My Orders.",
                "Thank you", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // OK tells the calling cart screen that something was placed, so it can refresh
            // itself and show an empty basket rather than the lines it was displaying before.
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            // Confirmation first, because leaving halfway through a two pharmacy checkout is
            // ambiguous: one order may already exist. The message says exactly what happens to
            // each half, so the choice is made with the consequence stated.
            DialogResult answer = MessageBox.Show(
                "Leave the checkout?\r\n\r\nAnything you have already paid for stays ordered; " +
                "the rest of your basket is left untouched.",
                "Leave checkout", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // != Yes rather than == No, so the window's own close button - which returns
            // Cancel, not No - is treated as "do not leave" as well.
            if (answer != DialogResult.Yes) return;

            // Cancel, because the remaining lines are still in the cart. Orders already
            // committed stay committed: nothing here touches the database, which is what makes
            // abandoning a multi pharmacy checkout safe rather than destructive.
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
