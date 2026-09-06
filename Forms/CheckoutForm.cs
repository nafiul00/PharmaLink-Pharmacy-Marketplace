using System.Configuration;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

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
        private readonly CartService _cart = new CartService();
        private readonly OrderService _orders = new OrderService();
        private readonly PrescriptionService _prescriptions = new PrescriptionService();
        private readonly AuthService _auth = new AuthService();

        private DataTable _pharmacyGroups;
        private int _currentIndex;

        private string _pendingRxImagePath = "";
        private string _pendingRxDoctorName = "";

        public CheckoutForm()
        {
            InitializeComponent();
        }

        private static decimal DeliveryCharge
        {
            get
            {
                string configured = ConfigurationManager.AppSettings["DeliveryCharge"];
                decimal value;
                return decimal.TryParse(configured, out value) ? value : 60m;
            }
        }

        private void CheckoutForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbPayment.Items.AddRange(new object[]
            {
                "- choose how you will pay -",
                "Cash on delivery",
                "bKash",
                "Nagad",
                "Card"
            });
            cmbPayment.SelectedIndex = 0;

            // The delivery address is pre-filled from the profile but stays editable.
            User me = _auth.GetUser(UserSession.UserId);
            if (me != null) txtAddress.Text = me.Address;

            _pharmacyGroups = _cart.GetPharmacyGroups(UserSession.UserId, DeliveryCharge);

            if (_pharmacyGroups.Rows.Count == 0)
            {
                MessageBox.Show("Your cart is empty.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            ShowCurrentPharmacy();
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

        private DataRow CurrentGroup => _pharmacyGroups.Rows[_currentIndex];
        private int CurrentPharmacyId => Convert.ToInt32(CurrentGroup["PharmacyId"]);
        private string CurrentPharmacyName => CurrentGroup["PharmacyName"].ToString();

        private void ShowCurrentPharmacy()
        {
            _pendingRxImagePath = "";
            _pendingRxDoctorName = "";

            lblTitle.Text = "Order " + (_currentIndex + 1) + " of " + _pharmacyGroups.Rows.Count;
            lblSubtitle.Text = CurrentPharmacyName + "   -   this half of your basket is a separate order with its own invoice.";

            LoadReviewGrid();

            bool needsRx = _cart.ContainsPrescriptionItem(UserSession.UserId, CurrentPharmacyId);

            lblRxWarning.Visible = needsRx;
            btnUploadRx.Visible = needsRx;
            lblRxState.Visible = needsRx;

            if (needsRx)
            {
                lblRxWarning.Text = "This order contains a prescription only medicine. You cannot confirm it until you " +
                                    "attach a photograph of your doctor's prescription (JPG or PNG, under 2 MB).";
                lblRxWarning.ForeColor = UiTheme.Warning;
                lblRxState.Text = "No prescription attached yet.";
                lblRxState.ForeColor = UiTheme.Danger;
            }

            ValidateAll();
        }

        private void LoadReviewGrid()
        {
            DataTable allLines = _cart.GetLinesTable(UserSession.UserId);

            DataView view = new DataView(allLines);
            view.RowFilter = "PharmacyId = " + CurrentPharmacyId;
            DataTable mine = view.ToTable();

            dgvReview.DataSource = mine;

            if (dgvReview.Columns.Count > 0)
            {
                foreach (DataGridViewColumn column in dgvReview.Columns) column.Visible = false;

                dgvReview.Columns["MedicineName"].Visible = true;
                dgvReview.Columns["MedicineName"].HeaderText = "Medicine";
                dgvReview.Columns["Strength"].Visible = true;
                dgvReview.Columns["Strength"].HeaderText = "Strength";
                dgvReview.Columns["Strength"].FillWeight = 45;
                dgvReview.Columns["Quantity"].Visible = true;
                dgvReview.Columns["Quantity"].HeaderText = "Qty";
                dgvReview.Columns["Quantity"].FillWeight = 30;
                dgvReview.Columns["PriceYouPay"].Visible = true;
                dgvReview.Columns["PriceYouPay"].HeaderText = "Unit (Tk)";
                dgvReview.Columns["PriceYouPay"].FillWeight = 45;
                dgvReview.Columns["LineTotal"].Visible = true;
                dgvReview.Columns["LineTotal"].HeaderText = "Line total (Tk)";
                dgvReview.Columns["LineTotal"].FillWeight = 55;
            }

            decimal itemsTotal = Convert.ToDecimal(CurrentGroup["ItemsTotal"]);
            decimal beforeDiscount = Convert.ToDecimal(CurrentGroup["BeforeDiscount"]);

            lblItemsValue.Text = UiTheme.Money(itemsTotal);
            lblDeliveryValue.Text = UiTheme.Money(DeliveryCharge);
            lblPayableValue.Text = UiTheme.Money(itemsTotal + DeliveryCharge);

            lblStatus.Text = beforeDiscount > itemsTotal
                ? "Today's offers have already taken " + UiTheme.Money(beforeDiscount - itemsTotal) +
                  " off this order. The prices above are what will be written into OrderItems."
                : "The prices above are what will be written into OrderItems, so this invoice can never change later.";
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        private bool NeedsMobileNumber =>
            cmbPayment.SelectedIndex == 2 || cmbPayment.SelectedIndex == 3;   // bKash or Nagad

        private void cmbPayment_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Choosing bKash or Nagad reveals the mobile number field.
            lblMobile.Visible = NeedsMobileNumber;
            txtMobile.Visible = NeedsMobileNumber;
            lblMobileError.Visible = false;

            if (NeedsMobileNumber)
                lblMobile.Text = cmbPayment.SelectedItem.ToString() + " number (11 digits)";

            ValidateAll();
        }

        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        private bool ValidateAll()
        {
            bool ok = true;

            ok &= Check(!Validator.IsBlank(txtAddress.Text), lblAddressError, txtAddress,
                        "We need somewhere to deliver to.");

            ok &= Check(cmbPayment.SelectedIndex > 0, lblPaymentError, cmbPayment,
                        "Choose a payment method.");

            if (NeedsMobileNumber)
            {
                ok &= Check(Validator.IsMobile(txtMobile.Text), lblMobileError, txtMobile,
                            "Enter the 11 digit number the payment will come from.");
            }
            else
            {
                UiTheme.ClearError(lblMobileError, txtMobile);
            }

            bool needsRx = lblRxWarning.Visible;
            if (needsRx && string.IsNullOrEmpty(_pendingRxImagePath)) ok = false;

            btnConfirm.Enabled = ok;
            btnConfirm.BackColor = ok ? UiTheme.Success : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        private string PaymentMethodForDatabase()
        {
            switch (cmbPayment.SelectedIndex)
            {
                case 1: return "CashOnDelivery";
                case 2: return "bKash";
                case 3: return "Nagad";
                case 4: return "Card";
                default: return "";
            }
        }

        // ---------------------------------------------------------------------
        //  PRESCRIPTION
        // ---------------------------------------------------------------------

        private void btnUploadRx_Click(object sender, EventArgs e)
        {
            using (UploadPrescriptionForm upload = new UploadPrescriptionForm(CurrentPharmacyName))
            {
                if (upload.ShowDialog(this) == DialogResult.OK)
                {
                    _pendingRxImagePath = upload.SelectedImagePath;
                    _pendingRxDoctorName = upload.DoctorName;

                    lblRxState.Text = "Attached: " + Path.GetFileName(_pendingRxImagePath) +
                                      (string.IsNullOrWhiteSpace(_pendingRxDoctorName)
                                          ? "" : "   (Dr " + _pendingRxDoctorName + ")");
                    lblRxState.ForeColor = UiTheme.Success;
                }
            }

            ValidateAll();
        }

        // ---------------------------------------------------------------------
        //  CONFIRM
        // ---------------------------------------------------------------------

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            if (!ValidateAll()) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                string message;
                int orderId = _orders.Checkout(UserSession.UserId, CurrentPharmacyId,
                                               txtAddress.Text.Trim(), PaymentMethodForDatabase(),
                                               DeliveryCharge, out message);

                if (orderId == 0)
                {
                    MessageBox.Show(message, "Order not placed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // The prescription needs the OrderId, so it is recorded straight
                // after the order is created.
                if (!string.IsNullOrEmpty(_pendingRxImagePath))
                {
                    string rxMessage;
                    _prescriptions.Upload(orderId, UserSession.UserId, _pendingRxImagePath,
                                          _pendingRxDoctorName, out rxMessage);
                }

                Cursor = Cursors.Default;

                using (InvoiceForm invoice = new InvoiceForm(orderId))
                {
                    invoice.ShowDialog(this);
                }

                MoveToNextPharmacyOrFinish();
            }
            catch (Exception ex)
            {
                MessageBox.Show("The order could not be placed.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void MoveToNextPharmacyOrFinish()
        {
            _currentIndex++;

            if (_currentIndex < _pharmacyGroups.Rows.Count)
            {
                MessageBox.Show(
                    "That order is placed.\r\n\r\n" +
                    "Your basket also contains items from " + _pharmacyGroups.Rows[_currentIndex]["PharmacyName"] +
                    ", which is a separate pharmacy and therefore a separate order. " +
                    "Let us finish that one now.",
                    "Next pharmacy", MessageBoxButtons.OK, MessageBoxIcon.Information);

                ShowCurrentPharmacy();
                return;
            }

            MessageBox.Show(
                "All done. Every order has been placed and your cart is now empty.\r\n\r\n" +
                "You can reopen any invoice at any time from My Orders.",
                "Thank you", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show(
                "Leave the checkout?\r\n\r\nAnything you have already paid for stays ordered; " +
                "the rest of your basket is left untouched.",
                "Leave checkout", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
