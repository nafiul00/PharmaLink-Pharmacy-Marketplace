using System.Drawing;               // Font, Brushes and RectangleF, used when the bill is drawn on paper
using System.Drawing.Printing;      // PrintDocument and PrintPageEventArgs, the printing model
using System.Text;                  // StringBuilder for composing the bill, Encoding for saving it
using System.Windows.Forms;         // Form, RichTextBox, PrintDialog, SaveFileDialog
using PharmaLinkApp.Helpers;        // UiTheme, so this screen matches every other one
using PharmaLinkApp.Models;         // Order and OrderItem, the typed objects the bill is built from
using PharmaLinkApp.Services;       // OrderService, the only class that reads the order back

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
        // One service for the life of the window. It is used exactly once, in
        // BuildInvoice, but it is held as a field for the same reason as on every other
        // form here: the screens all read their data through a service, never directly.
        private readonly OrderService _orders = new OrderService();

        // Which order is being billed. readonly because an invoice is a record of one
        // order: if this could change after construction, the header and the lines could
        // end up describing two different orders.
        private readonly int _orderId;

        // The loaded order with its items attached. Kept as a field rather than a local
        // in BuildInvoice so a later feature can read it back without a second query.
        private Order _order;

        // The composed bill, held as ONE string. The screen, the printer and the saved
        // text file all come from this same value, which is why what is printed is
        // exactly what was on screen. Started as "" rather than null so the print and
        // save handlers cannot fault on a length check before the order has loaded.
        private string _printText = "";

        // How much of _printText has already been put on paper. It has to be a field and
        // not a local because PrintPage is raised once per page: the value must survive
        // from one page to the next so page two knows where page one stopped.
        private int _printCharsPrinted;

        public InvoiceForm(int orderId)
        {
            // Creates the controls defined in the designer file. Nothing below it may
            // touch a control before this has run, because none of them exist yet.
            InitializeComponent();

            // The order number is the only thing this window needs to be told. Taking it
            // as a constructor argument means the form cannot be opened in an
            // indeterminate state, and both callers (order history and the owner's order
            // list) hand over the id the same way.
            _orderId = orderId;
        }

        private void InvoiceForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();     // appearance first, so nothing is briefly painted unstyled
            BuildInvoice();   // then the query and the text, which may decide to close the form
        }

        // Presentation only: fonts, colours and button styling. Kept separate from the
        // data code below so a change of appearance can never alter the numbers.
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
            // One call fetches the header and its lines together and returns them as a
            // single Order object. Everything it brings back was WRITTEN AT CHECKOUT:
            // OrderItems.UnitPrice is the price the customer actually paid, not the
            // medicine's price today. That is the whole reason a bill from three months
            // ago still adds up after the pharmacy has changed its prices or ended an
            // offer. Re-reading Medicines.UnitPrice here would quietly rewrite history.
            _order = _orders.GetOrderWithItems(_orderId);

            // null means no row with that OrderId, which normally only happens if the
            // order was removed while this window was being opened. It is a warning
            // rather than an error because nothing has gone wrong in the application.
            if (_order == null)
            {
                MessageBox.Show("That order could not be found.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();

                // The return matters as much as the Close. Close only asks the form to
                // shut; the code after it would still run, and every line below would
                // then dereference the null _order.
                return;
            }

            // The order number doubles as the invoice number: Orders.OrderId is an
            // IDENTITY starting at 1001, so every bill has a unique reference without a
            // second sequence to keep in step.
            lblTitle.Text = "Invoice  #" + _order.OrderId;

            // The stored order date is formatted for display only. It is never
            // recalculated from the current time, so reopening the bill tomorrow still
            // shows when the order was actually placed.
            lblSubtitle.Text = _order.PharmacyName + "   -   " + _order.OrderDate.ToString("dd MMM yyyy, h:mm tt") +
                               "   -   status: " + _order.Status;

            // Compose once, then hold the result. Print and Save both read this field
            // rather than re-composing, so the paper copy, the text file and the screen
            // can never differ by so much as a space.
            _printText = ComposeInvoiceText(_order);
            rtbInvoice.Text = _printText;
        }

        /// <summary>Lays the bill out as fixed width text so it prints exactly as it looks.</summary>
        private static string ComposeInvoiceText(Order order)
        {
            // static and taking the order as a parameter, so it reads no fields and no
            // controls. That makes the layout depend on nothing but its argument, which
            // is what allows the same method to feed the screen, the printer and the file.

            // One width for the whole document. Every ruled line and every padded column
            // below is measured against this constant, so the bill stays square when a
            // column is widened: change the number once and the layout follows.
            const int width = 78;

            // StringBuilder rather than repeated string concatenation. A bill is dozens of
            // appends, and each += on a string allocates a whole new string; the builder
            // grows one buffer instead.
            StringBuilder bill = new StringBuilder();

            // The letterhead. Spacing out the name is a deliberate typographic choice in
            // a monospaced document, where bold and larger type are not available.
            bill.AppendLine(Centre("P H A R M A L I N K", width));
            bill.AppendLine(Centre("Pharmacy Marketplace", width));
            bill.AppendLine(new string('=', width));   // a rule drawn by repeating one character
            bill.AppendLine();                         // a blank line, for air under the heading

            // The reference block. The captions are padded to a common width by hand so
            // the values line up in a column without needing tab stops, which do not
            // survive being printed or saved as plain text.
            bill.AppendLine("INVOICE  #" + order.OrderId);
            bill.AppendLine("Date      " + order.OrderDate.ToString("dd MMM yyyy, h:mm tt"));
            bill.AppendLine("Status    " + order.Status);

            // The payment method is translated for the reader rather than printed as the
            // stored code, so the bill says "Cash on delivery" and not "CashOnDelivery".
            bill.AppendLine("Payment   " + FriendlyPayment(order.PaymentMethod));
            bill.AppendLine();
            bill.AppendLine(new string('-', width));

            // Who sold the medicine. The licence number is on the bill because a
            // pharmacy's DGDA drug licence is what makes the sale lawful, and a customer
            // who needs to raise a complaint needs it from their own copy.
            bill.AppendLine("SOLD BY");
            bill.AppendLine("  " + order.PharmacyName);
            bill.AppendLine("  " + order.PharmacyAddress);
            bill.AppendLine("  DGDA drug licence: " + order.PharmacyLicense);
            bill.AppendLine();

            // Who it went to. DeliveryAddress is the address STORED on the order, not the
            // customer's current profile address, so a bill still shows where that
            // particular parcel was sent even after the customer moves.
            bill.AppendLine("DELIVERED TO");
            bill.AppendLine("  " + order.CustomerName + "   (" + order.CustomerPhone + ")");
            bill.AppendLine("  " + order.DeliveryAddress);
            bill.AppendLine(new string('-', width));
            bill.AppendLine();

            // The column headings. PadRight on the text column and PadLeft on the three
            // numeric ones is what right-aligns the figures: 34 + 5 + 15 + 16 comes to
            // 70, leaving the remainder of the 78 character rule as a margin.
            bill.AppendLine(
                "MEDICINE".PadRight(34) +
                "QTY".PadLeft(5) +
                "UNIT PRICE".PadLeft(15) +
                "LINE TOTAL".PadLeft(16));
            bill.AppendLine(new string('-', width));

            foreach (OrderItem item in order.Items)
            {
                // Name and strength are joined here rather than being stored joined, so
                // the medicine's own two columns stay separate everywhere else.
                string name = item.MedicineName;

                // IsNullOrWhiteSpace, not IsNullOrEmpty: a strength of "   " would
                // otherwise add a trailing space and push the column out by one.
                if (!string.IsNullOrWhiteSpace(item.Strength)) name += " " + item.Strength;

                // Anything wider than the 34 character column would shunt every figure on
                // that row to the right and break the alignment of the whole table, so a
                // long name is cut. The cut is at 30 with "..." added, which lands back at
                // 33 and leaves one space before the quantity instead of running into it.
                if (name.Length > 33) name = name.Substring(0, 30) + "...";

                bill.AppendLine(
                    name.PadRight(34) +
                    item.Quantity.ToString().PadLeft(5) +

                    // The price CHARGED on the day, copied into OrderItems at checkout.
                    // "N2" forces two decimal places so 40 prints as 40.00 and the column
                    // of figures stays aligned on the decimal point.
                    item.UnitPrice.ToString("N2").PadLeft(15) +

                    // Subtotal is a PERSISTED computed column, Quantity * UnitPrice,
                    // calculated by the database rather than multiplied again here. The
                    // line total on the bill therefore cannot disagree with the stored row.
                    item.Subtotal.ToString("N2").PadLeft(16));
            }

            // The totals block. 54 + 24 is the same 78 as the rules above, so the figures
            // finish flush with the right hand end of every line on the page.
            bill.AppendLine(new string('-', width));

            // ItemsTotal is what was written at checkout, already discounted. It is not
            // re-derived by summing the lines above, and it does not need to be: the same
            // expression produced both, so the figures agree by construction.
            bill.AppendLine("Items total".PadRight(54) + ("Tk " + order.ItemsTotal.ToString("N2")).PadLeft(24));

            // Delivery is stored per order rather than per pharmacy, because a basket
            // split across two shops becomes two orders and is charged delivery twice.
            bill.AppendLine("Delivery charge".PadRight(54) + ("Tk " + order.DeliveryCharge.ToString("N2")).PadLeft(24));
            bill.AppendLine(new string('=', width));

            // TotalAmount is the PERSISTED computed column ItemsTotal + DeliveryCharge.
            // Printing the stored column rather than adding the two numbers in C# is what
            // guarantees the grand total on the paper equals the grand total in the
            // database, even if this method were ever changed carelessly.
            bill.AppendLine("GRAND TOTAL".PadRight(54) + ("Tk " + order.TotalAmount.ToString("N2")).PadLeft(24));
            bill.AppendLine(new string('=', width));
            bill.AppendLine();

            // The closing note. The commission line is stated explicitly because
            // CommissionAmount is stored on the order and a customer who heard about the
            // platform fee might otherwise assume it had been added to their bill.
            bill.AppendLine("Thank you for using PharmaLink.");
            bill.AppendLine("The platform commission is deducted from the pharmacy, not added to this bill.");
            bill.AppendLine();
            bill.AppendLine(Centre("This is a computer generated invoice.", width));

            // One finished string, returned to the caller to hold.
            return bill.ToString();
        }

        private static string Centre(string text, int width)
        {
            // Text that already fills the width is returned untouched. Without this guard
            // the subtraction below would go negative and new string(' ', negative) throws.
            if (text.Length >= width) return text;

            // Integer division on purpose: half a character cannot be printed, so an odd
            // remainder leans the text one character to the left rather than rounding up.
            int padding = (width - text.Length) / 2;

            // Only the LEFT side is padded. Trailing spaces would be invisible on screen
            // but would still be written into the saved text file, so they are not added.
            return new string(' ', padding) + text;
        }

        private static string FriendlyPayment(string stored)
        {
            // The stored values are the four the CK_Orders_Payment check constraint
            // allows. They are translated for the reader here rather than being stored in
            // their display form, so the constraint keeps working on short stable codes.
            switch (stored)
            {
                case "CashOnDelivery": return "Cash on delivery";   // the only one that needs rewording
                case "bKash": return "bKash";                       // listed so the mapping is explicit
                case "Nagad": return "Nagad";
                case "Card": return "Card";

                // Anything unrecognised is printed as it was stored. Returning "Unknown"
                // would hide a newly added payment method from the bill; showing the raw
                // value keeps the invoice truthful and makes the omission obvious.
                default: return stored;
            }
        }

        // ---------------------------------------------------------------------
        //  PRINT AND SAVE
        // ---------------------------------------------------------------------

        private void btnPrint_Click(object sender, EventArgs e)
        {
            // Printing fails for reasons outside the application: no printer installed, a
            // driver error, a cancelled spool job. Those are caught rather than allowed to
            // close the window, because the invoice on screen is still perfectly usable.
            try
            {
                // using, so the document's unmanaged printing resources are released even
                // if Print throws part way through a job.
                using (PrintDocument document = new PrintDocument())
                {
                    // The name shown in the printer queue. Including the order number means
                    // a stack of queued jobs can be told apart at the printer.
                    document.DocumentName = "PharmaLink invoice " + _orderId;

                    // The handler below is what actually draws each page. It is attached
                    // here rather than in the designer because the document is created per
                    // click and does not exist until this line has run.
                    document.PrintPage += Document_PrintPage;

                    // Reset the bookmark BEFORE printing. Without this a second print would
                    // start where the first one finished, so the customer would get the
                    // tail of the invoice or a blank sheet.
                    _printCharsPrinted = 0;

                    // A second using: the dialog owns a window handle of its own.
                    using (PrintDialog dialog = new PrintDialog())
                    {
                        // Pointing the dialog at the document is what lets the chosen
                        // printer and page settings flow back into the job below.
                        dialog.Document = document;

                        // Print only on OK. Cancel leaves both objects to be disposed by
                        // the using blocks and nothing is sent to any printer.
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                            document.Print();
                    }
                }
            }
            catch (Exception ex)
            {
                // The message names the way out as well as the problem: the same text is
                // available through Save as text, so a missing printer does not leave the
                // customer without a copy of their bill.
                MessageBox.Show("The invoice could not be printed.\r\n\r\n" + ex.Message +
                                "\r\n\r\nYou can still save it as a text file.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Document_PrintPage(object sender, PrintPageEventArgs e)
        {
            // A font created per page and disposed at the closing brace, because a Font
            // holds an unmanaged handle. 9 point is a little smaller than the 10 point on
            // screen, so the 78 character lines fit within the printable width of A4.
            using (Font font = new Font("Consolas", 9F))
            {
                // MarginBounds is the printable rectangle inside the page margins, so the
                // text is placed relative to the margins rather than the paper edge.
                RectangleF area = e.MarginBounds;

                // LineLimit stops a line of text being drawn half on and half off the
                // bottom of the page: a line that does not fit entirely is not drawn at
                // all, which is what makes the character count below an honest split point.
                StringFormat format = new StringFormat(StringFormatFlags.LineLimit);

                // Ask FIRST how much of the remaining text will physically fit on this
                // page. MeasureString reports it through charactersFitted without
                // drawing anything.
                int charactersFitted, linesFilled;
                e.Graphics.MeasureString(_printText.Substring(_printCharsPrinted), font, area.Size, format,
                                         out charactersFitted, out linesFilled);

                // Then draw from the same starting point. Anything beyond the margins is
                // clipped rather than overflowing, which is why the measurement above is
                // what decides the split rather than the drawing.
                e.Graphics.DrawString(_printText.Substring(_printCharsPrinted), font, Brushes.Black, area, format);

                // Advance the bookmark by exactly what fitted, so the next page resumes
                // where this one stopped. _printCharsPrinted is a FIELD, not a local,
                // because PrintPage is raised once per page and must remember its place.
                _printCharsPrinted += charactersFitted;

                // Setting HasMorePages true makes the framework raise PrintPage again.
                // Forgetting to set it false at the end would print forever.
                e.HasMorePages = _printCharsPrinted < _printText.Length;
            }
        }

        private void btnSaveText_Click(object sender, EventArgs e)
        {
            // using, because the dialog holds a native common-dialog resource.
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                // A single filter entry: the bill is fixed width plain text, and offering
                // a .doc or .pdf choice here would only rename the same text file.
                dialog.Filter = "Text file (*.txt)|*.txt";

                // A suggested name built from the order number, so a folder of saved bills
                // sorts and searches by invoice without the customer naming each one.
                dialog.FileName = "PharmaLink-invoice-" + _orderId + ".txt";

                // Anything other than OK means the customer backed out. Returning early
                // keeps the writing code below out of an else block.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                // Writing to a path the customer chose can fail for reasons this
                // application cannot check in advance: a read only folder, a full disk, a
                // removable drive pulled out between the dialog and the write.
                try
                {
                    // The SAME _printText that is on screen and goes to the printer, so
                    // the three copies are identical by construction. UTF8 is stated
                    // explicitly rather than left to the default so the file is readable
                    // on a machine with different regional settings.
                    File.WriteAllText(dialog.FileName, _printText, Encoding.UTF8);

                    // The confirmation quotes the full path, because a Save dialog may
                    // have started in a folder the customer did not choose.
                    MessageBox.Show("Invoice saved to " + dialog.FileName, "Saved",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    // The framework's own message already says which file and why, so it
                    // is shown as it stands rather than wrapped in a vaguer sentence.
                    MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // Expression bodied: closing is the whole behaviour. No DialogResult is set,
        // because nothing here changes any data and the caller has nothing to reload.
        private void btnClose_Click(object sender, EventArgs e) => Close();
    }
}
