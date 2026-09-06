using System.Drawing;
using System.Drawing.Printing;
using System.Text;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// The printable bill produced after checkout, and reopened later from the
    /// customer's order history or from the pharmacy owner's order list.
    ///
    /// It carries the order number, both addresses, the pharmacy's DGDA licence
    /// number, the line items at the price actually charged and the grand total.
    /// TotalAmount comes from the computed column in the database, so the bill
    /// total can never disagree with its own parts.
    /// </summary>
    public partial class InvoiceForm : Form
    {
        private readonly OrderService _orders = new OrderService();
        private readonly int _orderId;

        private Order _order;
        private string _printText = "";
        private int _printCharsPrinted;

        public InvoiceForm(int orderId)
        {
            InitializeComponent();
            _orderId = orderId;
        }

        private void InvoiceForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            BuildInvoice();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Invoice");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            rtbInvoice.Font = new Font("Consolas", 10F);
            rtbInvoice.ForeColor = UiTheme.TextDark;

            lblFooterNote.Font = UiTheme.FontSmall;
            lblFooterNote.ForeColor = UiTheme.TextMuted;

            UiTheme.StylePrimary(btnPrint);
            UiTheme.StyleAccent(btnSaveText);
            UiTheme.StyleSecondary(btnClose);
        }

        // ---------------------------------------------------------------------

        private void BuildInvoice()
        {
            _order = _orders.GetOrderWithItems(_orderId);

            if (_order == null)
            {
                MessageBox.Show("That order could not be found.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            lblTitle.Text = "Invoice  #" + _order.OrderId;
            lblSubtitle.Text = _order.PharmacyName + "   -   " + _order.OrderDate.ToString("dd MMM yyyy, h:mm tt") +
                               "   -   status: " + _order.Status;

            _printText = ComposeInvoiceText(_order);
            rtbInvoice.Text = _printText;
        }

        /// <summary>Lays the bill out as fixed width text so it prints exactly as it looks.</summary>
        private static string ComposeInvoiceText(Order order)
        {
            const int width = 78;
            StringBuilder bill = new StringBuilder();

            bill.AppendLine(Centre("P H A R M A L I N K", width));
            bill.AppendLine(Centre("Pharmacy Marketplace", width));
            bill.AppendLine(new string('=', width));
            bill.AppendLine();

            bill.AppendLine("INVOICE  #" + order.OrderId);
            bill.AppendLine("Date      " + order.OrderDate.ToString("dd MMM yyyy, h:mm tt"));
            bill.AppendLine("Status    " + order.Status);
            bill.AppendLine("Payment   " + FriendlyPayment(order.PaymentMethod));
            bill.AppendLine();
            bill.AppendLine(new string('-', width));

            bill.AppendLine("SOLD BY");
            bill.AppendLine("  " + order.PharmacyName);
            bill.AppendLine("  " + order.PharmacyAddress);
            bill.AppendLine("  DGDA drug licence: " + order.PharmacyLicense);
            bill.AppendLine();

            bill.AppendLine("DELIVERED TO");
            bill.AppendLine("  " + order.CustomerName + "   (" + order.CustomerPhone + ")");
            bill.AppendLine("  " + order.DeliveryAddress);
            bill.AppendLine(new string('-', width));
            bill.AppendLine();

            bill.AppendLine(
                "MEDICINE".PadRight(34) +
                "QTY".PadLeft(5) +
                "UNIT PRICE".PadLeft(15) +
                "LINE TOTAL".PadLeft(16));
            bill.AppendLine(new string('-', width));

            foreach (OrderItem item in order.Items)
            {
                string name = item.MedicineName;
                if (!string.IsNullOrWhiteSpace(item.Strength)) name += " " + item.Strength;
                if (name.Length > 33) name = name.Substring(0, 30) + "...";

                bill.AppendLine(
                    name.PadRight(34) +
                    item.Quantity.ToString().PadLeft(5) +
                    item.UnitPrice.ToString("N2").PadLeft(15) +
                    item.Subtotal.ToString("N2").PadLeft(16));
            }

            bill.AppendLine(new string('-', width));
            bill.AppendLine("Items total".PadRight(54) + ("Tk " + order.ItemsTotal.ToString("N2")).PadLeft(24));
            bill.AppendLine("Delivery charge".PadRight(54) + ("Tk " + order.DeliveryCharge.ToString("N2")).PadLeft(24));
            bill.AppendLine(new string('=', width));
            bill.AppendLine("GRAND TOTAL".PadRight(54) + ("Tk " + order.TotalAmount.ToString("N2")).PadLeft(24));
            bill.AppendLine(new string('=', width));
            bill.AppendLine();

            bill.AppendLine("Thank you for using PharmaLink.");
            bill.AppendLine("The platform commission is deducted from the pharmacy, not added to this bill.");
            bill.AppendLine();
            bill.AppendLine(Centre("This is a computer generated invoice.", width));

            return bill.ToString();
        }

        private static string Centre(string text, int width)
        {
            if (text.Length >= width) return text;
            int padding = (width - text.Length) / 2;
            return new string(' ', padding) + text;
        }

        private static string FriendlyPayment(string stored)
        {
            switch (stored)
            {
                case "CashOnDelivery": return "Cash on delivery";
                case "bKash": return "bKash";
                case "Nagad": return "Nagad";
                case "Card": return "Card";
                default: return stored;
            }
        }

        // ---------------------------------------------------------------------
        //  PRINT AND SAVE
        // ---------------------------------------------------------------------

        private void btnPrint_Click(object sender, EventArgs e)
        {
            try
            {
                using (PrintDocument document = new PrintDocument())
                {
                    document.DocumentName = "PharmaLink invoice " + _orderId;
                    document.PrintPage += Document_PrintPage;
                    _printCharsPrinted = 0;

                    using (PrintDialog dialog = new PrintDialog())
                    {
                        dialog.Document = document;
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                            document.Print();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("The invoice could not be printed.\r\n\r\n" + ex.Message +
                                "\r\n\r\nYou can still save it as a text file.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Document_PrintPage(object sender, PrintPageEventArgs e)
        {
            using (Font font = new Font("Consolas", 9F))
            {
                RectangleF area = e.MarginBounds;
                StringFormat format = new StringFormat(StringFormatFlags.LineLimit);

                int charactersFitted, linesFilled;
                e.Graphics.MeasureString(_printText.Substring(_printCharsPrinted), font, area.Size, format,
                                         out charactersFitted, out linesFilled);

                e.Graphics.DrawString(_printText.Substring(_printCharsPrinted), font, Brushes.Black, area, format);

                _printCharsPrinted += charactersFitted;
                e.HasMorePages = _printCharsPrinted < _printText.Length;
            }
        }

        private void btnSaveText_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Text file (*.txt)|*.txt";
                dialog.FileName = "PharmaLink-invoice-" + _orderId + ".txt";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.WriteAllText(dialog.FileName, _printText, Encoding.UTF8);
                    MessageBox.Show("Invoice saved to " + dialog.FileName, "Saved",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnClose_Click(object sender, EventArgs e) => Close();
    }
}
