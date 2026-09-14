using System.Drawing;               // Font, Brushes and RectangleF, used when the bill prints
using System.Drawing.Printing;      // PrintDocument and PrintPageEventArgs, the print model
using System.Text;                  // StringBuilder for the bill, Encoding for saving it
using System.Windows.Forms;         // Form, RichTextBox, PrintDialog, SaveFileDialog
using PharmaLinkApp.Helpers;        // UiTheme, so this screen matches every other one
using PharmaLinkApp.Models;         // Order and OrderItem, the objects the bill is built from
using PharmaLinkApp.Services;       // OrderService, the only class that reads the order back

// This form formats an order that already exists; it never computes a price.
namespace PharmaLinkApp.Forms
{
    /// <summary>The printable bill, reopened from either order list.</summary>
    public partial class InvoiceForm : Form
    {
        // Used once, in BuildInvoice, but held like every other screen's service.
        private readonly OrderService _orders = new OrderService();

        // readonly, because an invoice is the record of exactly one order.
        private readonly int _orderId;

        // The loaded order with its items, kept so a later feature need not re-query.
        private Order _order;

        // ONE string feeds the screen, the printer and the file, so all three agree.
        private string _printText = "";

        // A field, not a local: PrintPage is raised per page and must remember its place.
        private int _printCharsPrinted;

        // Requiring the order id means the window cannot exist without knowing its order.
        public InvoiceForm(int orderId)
        {
            // Must run first; nothing below may touch a control before it exists.
            InitializeComponent();

            // Both callers hand the id over the same way, so there is no other state.
            _orderId = orderId;
        }

        // Runs once the window exists, so BuildInvoice can report and close.
        private void InvoiceForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();     // appearance first, so nothing is briefly painted unstyled
            BuildInvoice();   // then the query and the text, which may close the form
        }

        // Presentation only, so a change of appearance can never alter the numbers.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Invoice");               // the shared window chrome
            StartPosition = FormStartPosition.CenterParent;   // centred on the list that opened it

            panelHeader.BackColor = UiTheme.Primary;          // the brand green band, project wide
            lblTitle.Font = UiTheme.FontTitle;                // one heading font, so none is invented
            lblTitle.ForeColor = Color.White;                 // the only ink legible on that green
            lblSubtitle.Font = UiTheme.FontSmall;             // smaller: it carries shop, date, status
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // a pale tint, so it recedes

            rtbInvoice.Font = new Font("Consolas", 10F);      // MONOSPACED: the bill is aligned by padding
            rtbInvoice.ForeColor = UiTheme.TextDark;          // full strength, since this is the document

            lblFooterNote.Font = UiTheme.FontSmall;           // the line explaining the two buttons
            lblFooterNote.ForeColor = UiTheme.TextMuted;      // muted, because it never changes

            UiTheme.StylePrimary(btnPrint);                   // filled green: printing is the point
            UiTheme.StyleAccent(btnSaveText);                 // a different colour: saving is the fallback
            UiTheme.StyleSecondary(btnClose);                 // outlined, so leaving never competes
        }

        // The single read, plus the composition that turns it into text.
        private void BuildInvoice()
        {
            // Everything here was WRITTEN AT CHECKOUT, so an old bill still adds up.
            _order = _orders.GetOrderWithItems(_orderId);

            // null means the order went away while this window was opening.
            if (_order == null)
            {
                // Warning, not Error: a missing order is a state of the data.
                MessageBox.Show("That order could not be found.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);   // one button, nothing to offer
                Close();   // an invoice with no order behind it has nothing to show

                // Close only asks the form to shut, so this stops the null use below.
                return;
            }

            // OrderId is an IDENTITY from 1001, so it doubles as the invoice number.
            lblTitle.Text = "Invoice  #" + _order.OrderId;

            // The STORED date, formatted for display and never recalculated from now.
            lblSubtitle.Text = _order.PharmacyName + "   -   " + _order.OrderDate.ToString("dd MMM yyyy, h:mm tt") +
                               "   -   status: " + _order.Status;   // status travels in the header too

            // Composed once and held, so paper, file and screen cannot differ by a space.
            _printText = ComposeInvoiceText(_order);
            rtbInvoice.Text = _printText;   // the screen is simply this same string, shown

            // Nothing below this point ever consults the database again.
        }

        /// <summary>Lays out the bill as fixed width text.</summary>
        private static string ComposeInvoiceText(Order order)
        {
            // One width for the whole document, so widening a column keeps it square.
            const int width = 78;

            // A builder, because a bill is dozens of appends and each += reallocates.
            StringBuilder bill = new StringBuilder();

            // Spaced out on purpose: a monospaced document has no bold to use instead.
            bill.AppendLine(Centre("P H A R M A L I N K", width));
            // Centred against the same width, so the two lines stack on one axis.
            bill.AppendLine(Centre("Pharmacy Marketplace", width));
            bill.AppendLine(new string('=', width));   // a rule drawn by repeating one character
            bill.AppendLine();                         // a blank line, for air under the heading

            // Captions padded by hand, because tab stops do not survive plain text.
            bill.AppendLine("INVOICE  #" + order.OrderId);
            // The STORED order date, so a reprint next year still shows the real day.
            bill.AppendLine("Date      " + order.OrderDate.ToString("dd MMM yyyy, h:mm tt"));
            // Printed because paper carries no colour to mark a cancelled order.
            bill.AppendLine("Status    " + order.Status);

            // Translated for the reader, so it says 'Cash on delivery', not the code.
            bill.AppendLine("Payment   " + FriendlyPayment(order.PaymentMethod));
            bill.AppendLine();                         // a blank line closing the reference block
            bill.AppendLine(new string('-', width));   // a lighter rule, since this divides sections

            // Who sold it. The DGDA licence is what makes the sale lawful.
            bill.AppendLine("SOLD BY");
            // Two leading spaces group the detail under its heading.
            bill.AppendLine("  " + order.PharmacyName);
            // The shop's own address, so the customer can reach the seller directly.
            bill.AppendLine("  " + order.PharmacyAddress);
            // Spelled out, because a complaint is made to the DGDA using this reference.
            bill.AppendLine("  DGDA drug licence: " + order.PharmacyLicense);
            bill.AppendLine();   // closes the seller block before the buyer block opens

            // Who it went to, from the order's own stored address.
            bill.AppendLine("DELIVERED TO");
            // Name and phone on one line, because they identify one person.
            bill.AppendLine("  " + order.CustomerName + "   (" + order.CustomerPhone + ")");
            // The checkout snapshot, so a later move does not rewrite an old parcel.
            bill.AppendLine("  " + order.DeliveryAddress);
            bill.AppendLine(new string('-', width));   // closes the addresses, opens the table
            bill.AppendLine();                         // so the headings do not sit on the rule

            // 34 + 5 + 15 + 16 is 70, leaving the rest of the 78 rule as a margin.
            bill.AppendLine(
                "MEDICINE".PadRight(34) +      // the only left-aligned column: names read left
                "QTY".PadLeft(5) +             // matches the 5 the quantity values use
                "UNIT PRICE".PadLeft(15) +     // matches the 15 the prices use, sharing an edge
                "LINE TOTAL".PadLeft(16));     // matches the 16 the line totals use
            bill.AppendLine(new string('-', width));   // the rule under the headings

            // One row per STORED line, printed in the order checkout wrote them.
            foreach (OrderItem item in order.Items)
            {
                // Joined here, so the medicine's two columns stay separate elsewhere.
                string name = item.MedicineName;

                // IsNullOrWhiteSpace, or a strength of "   " would push the column out.
                if (!string.IsNullOrWhiteSpace(item.Strength)) name += " " + item.Strength;

                // Cut at 30 plus "...", which lands at 33 and leaves one space before Qty.
                if (name.Length > 33) name = name.Substring(0, 30) + "...";

                // The row, built from the same four widths as the headings above.
                bill.AppendLine(
                    name.PadRight(34) +                       // padded only; the cut above made it fit
                    item.Quantity.ToString().PadLeft(5) +     // right-aligned, so 2 and 12 line up

                    // The price CHARGED that day; "N2" keeps the column on its decimal point.
                    item.UnitPrice.ToString("N2").PadLeft(15) +

                    // Subtotal is a PERSISTED computed column, so it cannot disagree.
                    item.Subtotal.ToString("N2").PadLeft(16));
            }

            // 54 + 24 is the same 78, so the figures finish flush with every rule.
            bill.AppendLine(new string('-', width));

            // Written at checkout and already discounted, so it is not re-summed here.
            bill.AppendLine("Items total".PadRight(54) + ("Tk " + order.ItemsTotal.ToString("N2")).PadLeft(24));

            // Stored per order, because a split basket becomes two orders and two charges.
            bill.AppendLine("Delivery charge".PadRight(54) + ("Tk " + order.DeliveryCharge.ToString("N2")).PadLeft(24));
            bill.AppendLine(new string('=', width));   // the heavy rule, because the total follows

            // The PERSISTED computed column, so paper and database agree by construction.
            bill.AppendLine("GRAND TOTAL".PadRight(54) + ("Tk " + order.TotalAmount.ToString("N2")).PadLeft(24));
            bill.AppendLine(new string('=', width));   // closes the totals block
            bill.AppendLine();                         // air before the closing note

            // The closing note.
            bill.AppendLine("Thank you for using PharmaLink.");
            // Says who pays the platform, since commission never lands on this bill.
            bill.AppendLine("The platform commission is deducted from the pharmacy, not added to this bill.");
            bill.AppendLine();   // one blank line before the centred footer
            // Centred like the letterhead, so the document opens and closes alike.
            bill.AppendLine(Centre("This is a computer generated invoice.", width));

            // One finished string, returned to the caller to hold.
            return bill.ToString();
        }

        // Centring helper, static and pure, so it can be read on its own.
        private static string Centre(string text, int width)
        {
            // Guard first: a negative count would make new string(' ', n) throw.
            if (text.Length >= width) return text;

            // Integer division, so an odd remainder leans the text one to the left.
            int padding = (width - text.Length) / 2;

            // Left only: trailing spaces are invisible but would still be saved.
            return new string(' ', padding) + text;
        }

        // Translates the stored code at print time, so the column keeps short codes.
        private static string FriendlyPayment(string stored)
        {
            // The four values CK_Orders_Payment allows, mapped for the reader.
            switch (stored)
            {
                case "CashOnDelivery": return "Cash on delivery";   // the only one needing rewording
                case "bKash": return "bKash";                       // listed so the map is explicit
                case "Nagad": return "Nagad";                       // already a brand name
                case "Card": return "Card";                         // all four stated, not just exceptions

                // Printed as stored, so a new method shows up rather than hiding.
                default: return stored;
            }
        }

        // PRINT AND SAVE

        // Sends _printText to a printer; it composes nothing, it only paginates.
        private void btnPrint_Click(object sender, EventArgs e)
        {
            // Printing fails for reasons outside the app, and the screen copy survives.
            try
            {
                // using, so the printing resources are released even if Print throws.
                using (PrintDocument document = new PrintDocument())
                {
                    // The queue name, so stacked jobs can be told apart at the printer.
                    document.DocumentName = "PharmaLink invoice " + _orderId;

                    // Attached here, because the document is created per click.
                    document.PrintPage += Document_PrintPage;

                    // Reset BEFORE printing, or a second print would start mid-bill.
                    _printCharsPrinted = 0;

                    // A second using: the dialog owns a window handle of its own.
                    using (PrintDialog dialog = new PrintDialog())
                    {
                        // Pointing it at the document is what carries the settings back.
                        dialog.Document = document;

                        // Print only on OK; Cancel sends nothing to any printer.
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                            document.Print();   // raises PrintPage until HasMorePages is false
                    }
                }
            }
            // Wraps the whole attempt, since a driver can throw at any point.
            catch (Exception ex)
            {
                // The message names the way out as well as the problem.
                MessageBox.Show("The invoice could not be printed.\r\n\r\n" + ex.Message +
                                "\r\n\r\nYou can still save it as a text file.",   // so a missing printer is not a dead end
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // Warning: the bill is intact
            }
        }

        // Raised once per page, which is why the split point lives in a field.
        private void Document_PrintPage(object sender, PrintPageEventArgs e)
        {
            // A Font holds an unmanaged handle; 9 point keeps 78 columns inside A4.
            using (Font font = new Font("Consolas", 9F))
            {
                // MarginBounds is the printable rectangle inside the page margins.
                RectangleF area = e.MarginBounds;

                // LineLimit refuses to draw a half line, which makes the split honest.
                StringFormat format = new StringFormat(StringFormatFlags.LineLimit);

                // Ask FIRST how much will fit; MeasureString draws nothing.
                int charactersFitted, linesFilled;
                // The substring starts at the bookmark, so only unprinted text is measured.
                e.Graphics.MeasureString(_printText.Substring(_printCharsPrinted), font, area.Size, format,
                                         out charactersFitted, out linesFilled);   // linesFilled is unused but required

                // Then draw from the same point; the overflow is clipped, not wrapped.
                e.Graphics.DrawString(_printText.Substring(_printCharsPrinted), font, Brushes.Black, area, format);

                // Advance by exactly what fitted, so the next page resumes here.
                _printCharsPrinted += charactersFitted;

                // Forgetting to let this go false would print forever.
                e.HasMorePages = _printCharsPrinted < _printText.Length;
            }
        }

        // Writes the same _printText to a file, so the fallback copy is identical.
        private void btnSaveText_Click(object sender, EventArgs e)
        {
            // using, because the dialog holds a native common-dialog resource.
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                // One filter: the bill is fixed width plain text and nothing else.
                dialog.Filter = "Text file (*.txt)|*.txt";

                // The order number in the name, so a folder of bills is searchable.
                dialog.FileName = "PharmaLink-invoice-" + _orderId + ".txt";

                // Returning early on anything but OK keeps the write out of an else.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                // A chosen path can fail for reasons this application cannot check.
                try
                {
                    // The SAME string as the screen and the printer; UTF8 stated, not assumed.
                    File.WriteAllText(dialog.FileName, _printText, Encoding.UTF8);

                    // The full path is quoted, because the dialog may have moved folder.
                    MessageBox.Show("Invoice saved to " + dialog.FileName, "Saved",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);   // this reports something that worked
                }
                // Wraps only the write; a cancelled save returned earlier.
                catch (Exception ex)
                {
                    // The framework already says which file and why, so it is shown as is.
                    MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // Closing is the whole behaviour; no DialogResult, because nothing changed.
        private void btnClose_Click(object sender, EventArgs e) => Close();
    }
}
