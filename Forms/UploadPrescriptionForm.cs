using System.Drawing;               // Image, Bitmap and Color, for the preview and buttons
using System.Windows.Forms;         // Form, OpenFileDialog, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, including ShowError and ClearError
// FileInfo and FileStream come from System.IO, in scope through ImplicitUsings.

// This dialog writes nothing: it collects a file, so no service is imported here.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 26. The modal shown when a script is required.</summary>
    public partial class UploadPrescriptionForm : Form // JPG or PNG, under 2 MB only.
    {
        // Written as a calculation rather than 2097152, and long to match FileInfo.Length.
        private const long MaxBytes = 2 * 1024 * 1024;

        // The shop being asked about, used only in the subtitle; one dialog per pharmacy.
        private readonly string _pharmacyName;

        /// <summary>The chosen file, read by CheckoutForm after this closes.</summary>
        public string SelectedImagePath { get; private set; } = ""; // private set: only checks set it

        // The prescribing doctor, optional, and copied out only when Attach is pressed.
        public string DoctorName { get; private set; } = "";

        // Required by the constructor, so the shop asking is always named.
        public UploadPrescriptionForm(string pharmacyName)
        {
            InitializeComponent();          // must come first: Load writes into these controls
            _pharmacyName = pharmacyName;   // stored now, displayed in Load once the label exists
        }

        // Fires once the window exists; anything that reads a control belongs here.
        private void UploadPrescriptionForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // fonts, colours and the button styles, before anything is shown

            // Naming the pharmacy makes it concrete when the checkout steps through two shops.
            lblSubtitle.Text = _pharmacyName + " must verify this before your order can be dispatched.";

            UpdateAttachButton();   // nothing chosen yet, so Attach starts disabled and grey
        }

        // Presentation only, kept apart so a change of appearance cannot weaken a check.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Upload Prescription");   // shared window setup and caption
            StartPosition = FormStartPosition.CenterParent;   // centred on the checkout step

            panelHeader.BackColor = UiTheme.Warning;          // amber: this stands between him and the order
            lblTitle.Font = UiTheme.FontTitle;                // the shared heading font
            lblTitle.ForeColor = Color.White;                 // the only colour that holds up on Warning
            lblSubtitle.Font = UiTheme.FontSmall;             // smaller, it names the pharmacy
            lblSubtitle.ForeColor = Color.FromArgb(255, 240, 220);   // pale tint, part of the strip

            lblFileError.Font = UiTheme.FontSmall;            // every rejection writes into this label
            lblFileError.ForeColor = UiTheme.Danger;          // red, it only ever carries a refusal
            lblFileInfo.Font = UiTheme.FontSmall;             // the name and size of an accepted file
            lblFileInfo.ForeColor = UiTheme.TextMuted;        // muted, so it cannot read as an error
            lblRules.Font = UiTheme.FontSmall;                // the caption listing JPG, PNG and 2 MB
            lblRules.ForeColor = UiTheme.TextMuted;           // stated before a mistake, so it must not shout

            UiTheme.StyleAccent(btnChooseFile);               // choosing a file is the first thing to do
            UiTheme.StyleSuccess(btnAttach);                  // green; UpdateAttachButton greys it out
            // Bolder than its neighbours, because Attach is what lets the order proceed.
            btnAttach.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnCancel);                // quiet: abandoning this abandons the order
        }

        // ---------------------------------------------------------------------

        // Opens the browser; it only picks a path, every check lives in TryAcceptFile.
        private void btnChooseFile_Click(object sender, EventArgs e)
        {
            // using, because the dialog holds a native resource beyond its closing.
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                // The title says what is wanted; a default "Open" gives no hint.
                dialog.Title = "Choose a photograph of your prescription";

                // The filter narrows what is OFFERED, so TryAcceptFile re-checks the type.
                dialog.Filter = "Prescription image (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";

                // Cancel leaves everything as it was, including a good earlier choice.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                // One method for the checks, so a future drag and drop could reuse them.
                TryAcceptFile(dialog.FileName);
            }
        }

        // Four checks, cheapest first: exists, extension, size, then decoding the bytes.
        private void TryAcceptFile(string path)
        {
            // Cleared FIRST, so a refused second choice cannot leave the old file behind.
            ClearPreview();
            SelectedImagePath = "";   // emptied too, so a refusal leaves no readable path

            // FileInfo answers existence, extension, size and name from one look at disk.
            FileInfo file = new FileInfo(path);

            // Checked again: a share can drop or a drive be pulled since it was picked.
            if (!file.Exists)
            {
                UiTheme.ShowError(lblFileError, null, "That file no longer exists.");   // null: no box to outline
                UpdateAttachButton();   // keeps Attach disabled, nothing valid was set
                return;   // every failing branch returns, which is what protects the accept below
            }

            // ToLowerInvariant, not ToLower, so ".JPG" matches on every machine's culture.
            string extension = file.Extension.ToLowerInvariant();

            // Re-checked here, and only images: the pharmacy has to LOOK at the script.
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                // The rejected extension is quoted back, so the wrong file is identifiable.
                UiTheme.ShowError(lblFileError, null,
                    "Only JPG and PNG images are accepted. '" + extension + "' is not one of them.");   // '' shows an empty one
                UpdateAttachButton();   // stays disabled, SelectedImagePath is still ""
                return;                 // stops before the size and decode checks
            }

            // Size BEFORE decoding, so a huge photograph is never loaded just to reject it.
            if (file.Length > MaxBytes)
            {
                // 1024.0 is a double on purpose, so the result keeps its fraction.
                UiTheme.ShowError(lblFileError, null,
                    "The image is " + (file.Length / 1024 / 1024.0).ToString("N1") +   // N1: one decimal
                    " MB. Please use a photograph under 2 MB.");                       // restates the limit
                UpdateAttachButton();   // keeps Attach disabled, no valid file was set
                return;                 // the bytes are never opened, which is the point
            }

            // An extension is only a name, so the last check lets the decoder decide.
            try
            {
                // A STREAM, not Image.FromFile, which would keep the file locked.
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image original = Image.FromStream(stream))   // throws for anything it cannot read
                {
                    // Copied into a Bitmap, which holds pixels rather than a disk handle.
                    picPreview.Image = new Bitmap(original);
                }
            }
            catch   // any decoder failure: the outcome is the same whichever type it is
            {
                // No exception variable: "parameter is not valid" explains nothing to him.
                UiTheme.ShowError(lblFileError, null, "That file could not be read as an image.");
                UpdateAttachButton();   // disabled, because the assignment below was not reached
                return;                 // returns from inside the catch, so nothing falls through
            }

            // Set only now, so the property means "a file that has been checked".
            SelectedImagePath = path;
            UiTheme.ClearError(lblFileError, null);   // removes any complaint from an earlier try

            // Name and size shown back; KB, because every accepted file is under 2 MB.
            lblFileInfo.Text = file.Name + "   -   " + (file.Length / 1024.0).ToString("N0") + " KB";
            UpdateAttachButton();   // the one call that can now actually enable Attach
        }

        // Releases the preview and its caption, so no photograph outlives its choice.
        private void ClearPreview()
        {
            // Disposed, not just replaced: a Bitmap holds unmanaged memory each time.
            if (picPreview.Image != null)
            {
                picPreview.Image.Dispose();   // frees the native handle immediately

                // Nulled after disposing, so no repaint can touch a released bitmap.
                picPreview.Image = null;
            }

            // The caption goes with the picture, or it would imply a file is still attached.
            lblFileInfo.Text = "";
        }

        // The only place btnAttach's state is decided, from the property alone.
        private void UpdateAttachButton()
        {
            // Derived from the property, not from a flag: one source of truth.
            bool ready = !string.IsNullOrEmpty(SelectedImagePath);
            btnAttach.Enabled = ready;   // Enabled blocks the click, the colour is only the look

            // Set alongside Enabled: a themed button keeps its BackColor when disabled.
            btnAttach.BackColor = ready ? UiTheme.Success : Color.FromArgb(170, 190, 184);
        }

        // ---------------------------------------------------------------------

        // Closes with OK, the checkout's signal to read the two properties.
        private void btnAttach_Click(object sender, EventArgs e)
        {
            // Re-checked, because Enter can reach a handler through the form's AcceptButton.
            if (string.IsNullOrEmpty(SelectedImagePath)) return;

            // Trim, so a box of spaces comes back as "" and is stored as NULL, not blanks.
            DoctorName = txtDoctor.Text.Trim();

            // Released here: the checkout may load another photograph for the next shop.
            ClearPreview();

            // No row is written here: Prescriptions needs an OrderId, and there is none.
            DialogResult = DialogResult.OK;
            Close();   // returns to the checkout's ShowDialog, which then reads the properties
        }

        // Abandons the dialog, but only after confirming: leaving also stalls the order.
        private void btnCancel_Click(object sender, EventArgs e)
        {
            // Confirmed, because without a prescription the Confirm button stays disabled.
            DialogResult answer = MessageBox.Show(
                "Without a prescription this order cannot be confirmed.\r\n\r\n" +                        // a blank line between paragraphs
                "You can go back to your cart and remove the prescription only medicine instead.",        // names the way out
                "No prescription attached", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);          // Cancel, meaning stay, is on the right

            // Anything but OK means he changed his mind, so the file and preview stay.
            if (answer != DialogResult.OK) return;

            // Cleared deliberately: a file may have been accepted before Cancel was pressed.
            SelectedImagePath = "";
            ClearPreview();                          // the bitmap goes with it
            DialogResult = DialogResult.Cancel;      // on Cancel the checkout reads no properties
            Close();                                 // hands control back to the ShowDialog call
        }
    }
}
