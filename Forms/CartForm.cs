using System.Configuration;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

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
        private readonly CartService _cart = new CartService();

        public CartForm()
        {
            InitializeComponent();
        }

        private void CartForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadCart();
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

        private void LoadCart()
        {
            try
            {
                DataTable lines = _cart.GetLinesTable(UserSession.UserId);
                dgvCart.DataSource = lines;

                if (dgvCart.Columns.Count > 0)
                {
                    dgvCart.Columns["CartId"].Visible = false;
                    dgvCart.Columns["MedicineId"].HeaderText = "ID";
                    dgvCart.Columns["MedicineId"].FillWeight = 28;
                    dgvCart.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvCart.Columns["Strength"].HeaderText = "Strength";
                    dgvCart.Columns["Strength"].FillWeight = 45;
                    dgvCart.Columns["PharmacyId"].Visible = false;
                    dgvCart.Columns["PharmacyName"].HeaderText = "Sold by";
                    dgvCart.Columns["Quantity"].HeaderText = "Qty";
                    dgvCart.Columns["Quantity"].FillWeight = 30;
                    dgvCart.Columns["ListPrice"].HeaderText = "List (Tk)";
                    dgvCart.Columns["ListPrice"].FillWeight = 42;
                    dgvCart.Columns["DiscountPercent"].HeaderText = "Off %";
                    dgvCart.Columns["DiscountPercent"].FillWeight = 32;
                    dgvCart.Columns["PriceYouPay"].HeaderText = "You pay (Tk)";
                    dgvCart.Columns["PriceYouPay"].FillWeight = 48;
                    dgvCart.Columns["LineTotal"].HeaderText = "Line total (Tk)";
                    dgvCart.Columns["LineTotal"].FillWeight = 52;
                    dgvCart.Columns["Stock"].HeaderText = "In stock";
                    dgvCart.Columns["Stock"].FillWeight = 38;
                    dgvCart.Columns["RequiresRx"].HeaderText = "Rx";
                    dgvCart.Columns["RequiresRx"].FillWeight = 24;
                }

                decimal itemsTotal = _cart.GetItemsTotal(UserSession.UserId);
                decimal discount = _cart.GetDiscountTotal(UserSession.UserId);

                DataTable groups = _cart.GetPharmacyGroups(UserSession.UserId, DeliveryCharge);
                int pharmacyCount = groups.Rows.Count;
                decimal delivery = DeliveryCharge * pharmacyCount;

                lblItemsValue.Text = UiTheme.Money(itemsTotal);
                lblDiscountValue.Text = discount > 0m ? "- " + UiTheme.Money(discount) : UiTheme.Money(0m);
                lblDiscountValue.ForeColor = discount > 0m ? UiTheme.Success : UiTheme.TextDark;
                lblDeliveryValue.Text = UiTheme.Money(delivery);
                lblPayableValue.Text = UiTheme.Money(itemsTotal + delivery);

                lblSummaryNote.Text = pharmacyCount <= 1
                    ? "One delivery charge of " + UiTheme.Money(DeliveryCharge) + " applies.\r\n\r\n" +
                      "PharmaLink takes its commission from the pharmacy, not from you. The delivery charge is not commissionable."
                    : "Your basket spans " + pharmacyCount + " pharmacies, so " + pharmacyCount +
                      " separate orders will be created at checkout - one per pharmacy, each with its own " +
                      "delivery charge of " + UiTheme.Money(DeliveryCharge) + " and its own invoice.\r\n\r\n" +
                      "PharmaLink takes its commission from the pharmacy, not from you.";

                BuildSplitNote(groups);

                bool hasLines = lines.Rows.Count > 0;
                btnCheckout.Enabled = hasLines;
                btnClearCart.Enabled = hasLines;
                btnCheckout.BackColor = hasLines ? UiTheme.Success : Color.FromArgb(170, 190, 184);

                lblTitle.Text = hasLines ? "My Cart  (" + lines.Rows.Count + " line(s))" : "My Cart";

                lblStatus.Text = hasLines
                    ? "Increasing a quantity above the available stock is refused, and reducing it to zero removes the line."
                    : "Your cart is empty. Browse the catalogue and add something to it.";

                UpdateLineButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildSplitNote(DataTable groups)
        {
            if (groups.Rows.Count == 0)
            {
                lblSplitNote.Text = "";
                return;
            }

            string text = "Checkout will create " + groups.Rows.Count + " order(s):" + Environment.NewLine;

            int index = 1;
            foreach (DataRow row in groups.Rows)
            {
                decimal itemsTotal = DbHelperDecimal(row, "ItemsTotal");
                text += "   " + index + ".  " + row["PharmacyName"] + "  -  " +
                        row["Lines"] + " line(s), " + row["Units"] + " unit(s), items " +
                        UiTheme.Money(itemsTotal) + " + delivery " + UiTheme.Money(DeliveryCharge) +
                        "  =  " + UiTheme.Money(itemsTotal + DeliveryCharge) + Environment.NewLine;
                index++;
            }

            lblSplitNote.Text = text;
        }

        private static decimal DbHelperDecimal(DataRow row, string column)
        {
            return row[column] == DBNull.Value ? 0m : Convert.ToDecimal(row[column]);
        }

        private void dgvCart_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvCart.Columns.Count == 0) return;

            DataGridViewRow row = dgvCart.Rows[e.RowIndex];
            object quantity = row.Cells["Quantity"].Value;
            object stock = row.Cells["Stock"].Value;
            object discount = row.Cells["DiscountPercent"].Value;

            if (quantity != DBNull.Value && stock != DBNull.Value &&
                Convert.ToInt32(quantity) > Convert.ToInt32(stock))
            {
                // The stock ran out after the line was added.
                row.DefaultCellStyle.BackColor = UiTheme.LowStockBack;
            }
            else if (discount != DBNull.Value && Convert.ToDecimal(discount) > 0m)
            {
                row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack;
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.White;
            }
        }

        private void dgvCart_SelectionChanged(object sender, EventArgs e) => UpdateLineButtons();

        private void UpdateLineButtons()
        {
            DataGridViewRow row = dgvCart.CurrentRow;
            bool hasRow = row != null && row.Cells["MedicineId"].Value != null;

            btnUpdateQuantity.Enabled = hasRow;
            btnRemoveLine.Enabled = hasRow;
            txtQuantity.Enabled = hasRow;

            if (hasRow) txtQuantity.Text = row.Cells["Quantity"].Value.ToString();
            else txtQuantity.Clear();
        }

        private int SelectedMedicineId()
        {
            if (dgvCart.CurrentRow == null) return 0;
            return Convert.ToInt32(dgvCart.CurrentRow.Cells["MedicineId"].Value);
        }

        // ---------------------------------------------------------------------

        private void btnUpdateQuantity_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            int quantity;
            if (!int.TryParse(txtQuantity.Text, out quantity))
            {
                MessageBox.Show("Enter a whole number. Zero removes the line.", "Check the quantity",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string message;
            if (_cart.SetQuantity(UserSession.UserId, medicineId, quantity, out message))
                lblStatus.Text = message;
            else
                MessageBox.Show(message, "Cannot update the quantity", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            LoadCart();
        }

        private void btnRemoveLine_Click(object sender, EventArgs e)
        {
            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            string name = dgvCart.CurrentRow.Cells["MedicineName"].Value.ToString();
            _cart.Remove(UserSession.UserId, medicineId);
            lblStatus.Text = name + " removed from your cart.";
            LoadCart();
        }

        private void btnClearCart_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show("Remove everything from your cart?", "Empty the cart",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            _cart.ClearAll(UserSession.UserId);
            LoadCart();
        }

        private void btnCheckout_Click(object sender, EventArgs e)
        {
            using (CheckoutForm checkout = new CheckoutForm())
            {
                checkout.ShowDialog(this);
            }

            LoadCart();

            // When the whole basket has been paid for there is nothing left to
            // show, so the cart closes and returns to the catalogue.
            if (_cart.CountLines(UserSession.UserId) == 0) Close();
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
