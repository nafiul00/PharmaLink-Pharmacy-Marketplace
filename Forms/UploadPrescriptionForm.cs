using System.Drawing;               // Image, Bitmap and Color, for the preview and the button states
using System.Windows.Forms;         // Form, OpenFileDialog, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, including the shared ShowError and ClearError pair
// FileInfo and FileStream come from System.IO, which is in scope through the project's
// ImplicitUsings setting rather than a using line of its own.

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 26. The modal that opens at the Confirm Order step when the
    /// basket contains a medicine whose RequiresRx flag is set.
    ///
    /// It accepts only JPG and PNG files under 2 MB, and it cannot be dismissed
    /// with Attach until a valid image has been chosen. The form only collects
    /// the file; the row is written into Prescriptions by the checkout, because
    /// the OrderId does not exist until the order does.
    /// </summary>
    public partial class UploadPrescriptionForm : Form
    {
        // The size ceiling, written as a calculation rather than as 2097152 so the intent
        // is readable at a glance. const because it is fixed at compile time and there is
        // no case in which one copy of this dialog should allow a different limit.
        // long, not int, because FileInfo.Length is a long and comparing the two directly
        // avoids a conversion that could overflow on a very large file.
        private const long MaxBytes = 2 * 1024 * 1024;

        // The shop this prescription is for, used only in the subtitle. readonly, because
        // the checkout steps through one pharmacy at a time and opens a fresh dialog for
        // each: the name a customer is shown must stay the one they were asked about.
        private readonly string _pharmacyName;

        /// <summary>The chosen file, read by CheckoutForm after the dialog closes with OK.</summary>
        // The private setter is the point: the outside world can read the path but only
        // this form can decide what it is, and it only ever sets it after every check
        // below has passed. Initialised to "" rather than null so the callers can test it
        // with IsNullOrEmpty without a null check of their own.
        public string SelectedImagePath { get; private set; } = "";

        // The prescribing doctor, optional. It is only copied out of the text box when
        // Attach is pressed, so a name typed and then cancelled is never handed back.
        public string DoctorName { get; private set; } = "";

        public UploadPrescriptionForm(string pharmacyName)
        {
            // Creates the designer's controls. The assignment below must follow it,
            // because the Load handler puts this value into a label that does not yet exist.
            InitializeComponent();
            _pharmacyName = pharmacyName;
        }

        private void UploadPrescriptionForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            // Naming the pharmacy makes the requirement concrete. "Your order" would be
            // vague in a checkout that is stepping through two shops in turn.
            lblSubtitle.Text = _pharmacyName + " must verify this before your order can be dispatched.";

            // Called before anything has been chosen, so Attach starts disabled and greyed
            // rather than looking available and then refusing.
            UpdateAttachButton();
        }

        // Presentation only: fonts, colours and button styling, kept apart from the file
        // handling below so a change of appearance cannot weaken a check.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Upload Prescription");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Warning;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(255, 240, 220);

            lblFileError.Font = UiTheme.FontSmall;
            lblFileError.ForeColor = UiTheme.Danger;
            lblFileInfo.Font = UiTheme.FontSmall;
            lblFileInfo.ForeColor = UiTheme.TextMuted;
            lblRules.Font = UiTheme.FontSmall;
            lblRules.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleAccent(btnChooseFile);
            UiTheme.StyleSuccess(btnAttach);
            btnAttach.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnCancel);
        }

        // ---------------------------------------------------------------------

        private void btnChooseFile_Click(object sender, EventArgs e)
        {
            // using, because the dialog holds a native common-dialog resource that is not
            // released simply by the dialog closing.
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                // The title says what is wanted. A default "Open" gives no hint that a
                // photograph of the prescription is what should be chosen.
                dialog.Title = "Choose a photograph of your prescription";

                // The filter narrows what is OFFERED, which is a convenience, not a
                // control: a customer can still type any name into the box, and the
                // browser's filter does nothing at all for a file passed in another way.
                // That is why TryAcceptFile re-checks the extension itself.
                dialog.Filter = "Prescription image (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";

                // Cancel leaves everything as it was, including any file already accepted,
                // so backing out of the browser does not undo a good earlier choice.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                // The chosen path is validated in one method, so a future drag and drop or
                // paste could reuse exactly the same checks rather than copying them.
                TryAcceptFile(dialog.FileName);
            }
        }

        private void TryAcceptFile(string path)
        {
            // Both are cleared FIRST. Every failing branch below returns without setting
            // them again, so a rejected second choice cannot leave the previous file's
            // preview on screen beside a fresh error message, or worse, leave a good path
            // in the property while the customer is being told the file was refused.
            ClearPreview();
            SelectedImagePath = "";

            // FileInfo is used rather than separate File.Exists and File.ReadAllBytes
            // calls, because it answers existence, extension, size and display name from
            // one object and one look at the file system.
            FileInfo file = new FileInfo(path);

            // Checked even though the file was just picked from a browser: a network share
            // can drop and a removable drive can be pulled out between the two moments.
            if (!file.Exists)
            {
                UiTheme.ShowError(lblFileError, null, "That file no longer exists.");
                UpdateAttachButton();   // keeps Attach disabled, since nothing valid was set
                return;
            }

            // ToLowerInvariant, not ToLower: the invariant form does not depend on the
            // machine's culture, so ".JPG" is matched the same way on every installation.
            string extension = file.Extension.ToLowerInvariant();

            // The type check is repeated here rather than left to the dialog's filter,
            // because the filter only changes what the browser lists. Only image types are
            // accepted because the pharmacy has to LOOK at the prescription: a PDF or a
            // document would not load into the picture box on the verification screen.
            // Both .jpg and .jpeg are listed since cameras and phones use each of them.
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                // The rejected extension is quoted back, so a customer who chose the wrong
                // file can see which one was wrong instead of guessing.
                UiTheme.ShowError(lblFileError, null,
                    "Only JPG and PNG images are accepted. '" + extension + "' is not one of them.");
                UpdateAttachButton();
                return;
            }

            // Size checked BEFORE the image is decoded, because loading a very large
            // photograph into memory just to reject it would be wasteful - and a
            // malformed huge file could fail in a less controlled way.
            // MaxBytes is 2 * 1024 * 1024, written as a calculation rather than 2097152
            // so the intent stays readable.
            if (file.Length > MaxBytes)
            {
                // 1024 / 1024.0 - the second divisor is a double on purpose, so the
                // result keeps its fraction and reports "2.4 MB" rather than "2 MB".
                UiTheme.ShowError(lblFileError, null,
                    "The image is " + (file.Length / 1024 / 1024.0).ToString("N1") +
                    " MB. Please use a photograph under 2 MB.");
                UpdateAttachButton();   // keeps Attach disabled, since no valid file was set
                return;
            }

            // The last check is whether the bytes really are an image. An extension is only
            // a name, so this opens the file and lets the decoder decide.
            try
            {
                // Read through a STREAM rather than with Image.FromFile, because FromFile
                // keeps a lock on the file for as long as the Image object lives. That lock
                // would still be held when the checkout copies the file a moment later.
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image original = Image.FromStream(stream))
                {
                    // Copied into a new Bitmap, which holds pixels in memory rather than a
                    // handle to disk. Both using blocks dispose at the closing brace, so
                    // the file is released the moment the preview exists.
                    picPreview.Image = new Bitmap(original);
                }
            }
            catch
            {
                // No exception variable, because the decoder's own wording ("parameter is
                // not valid") explains nothing to a customer. What matters is the outcome:
                // whatever this file is, it cannot be shown to the pharmacy as an image.
                UiTheme.ShowError(lblFileError, null, "That file could not be read as an image.");
                UpdateAttachButton();
                return;
            }

            // Set only now, after existence, type, size and decoding have all passed. This
            // single assignment is what makes the property mean "a file that has been
            // checked", which is exactly what the Attach button tests.
            SelectedImagePath = path;
            UiTheme.ClearError(lblFileError, null);   // remove any complaint from an earlier attempt

            // Name and size shown back, so the customer can confirm they chose the right
            // photograph. KB here rather than MB because every accepted file is under 2 MB
            // and "1,450 KB" is easier to judge against the limit than "1.4 MB".
            lblFileInfo.Text = file.Name + "   -   " + (file.Length / 1024.0).ToString("N0") + " KB";
            UpdateAttachButton();   // the one call that can now actually enable Attach
        }

        private void ClearPreview()
        {
            // Disposed rather than just replaced. A Bitmap holds unmanaged memory, and this
            // dialog can load several photographs in a row while a customer finds the right
            // one; dropping the reference alone would leave each of them to the collector.
            if (picPreview.Image != null)
            {
                picPreview.Image.Dispose();

                // Nulled after disposing, so nothing can paint a bitmap that has already
                // been released, which would throw during the next repaint.
                picPreview.Image = null;
            }

            // The caption goes with the picture. Leaving the old file's name under an empty
            // frame would suggest a file is still attached when none is.
            lblFileInfo.Text = "";
        }

        private void UpdateAttachButton()
        {
            // The button's state is derived from the property, not tracked in a flag of its
            // own. One source of truth means the button cannot say "ready" while the path
            // it would hand back is empty.
            bool ready = !string.IsNullOrEmpty(SelectedImagePath);
            btnAttach.Enabled = ready;

            // Colour set alongside Enabled, because a themed button keeps its custom
            // BackColor when disabled and would otherwise still look pressable.
            btnAttach.BackColor = ready ? UiTheme.Success : Color.FromArgb(170, 190, 184);
        }

        // ---------------------------------------------------------------------

        private void btnAttach_Click(object sender, EventArgs e)
        {
            // Re-checked rather than trusted, because the Enter key can reach a click
            // handler through a form's AcceptButton without the button being pressed.
            if (string.IsNullOrEmpty(SelectedImagePath)) return;

            // Read only on success. Trim, so a box containing spaces is handed back as an
            // empty string, which the service then stores as NULL rather than as
            // whitespace in DoctorName. The box's MaxLength matches the NVARCHAR(100)
            // column, so the value cannot be too long to store.
            DoctorName = txtDoctor.Text.Trim();

            // The preview is released here, not in the form's Dispose, because the bitmap
            // has done its job and the checkout window behind this one may be about to
            // load another photograph for the next pharmacy in the basket.
            ClearPreview();

            // OK is the signal the checkout waits for. Note what this dialog does NOT do:
            // it writes no row and copies no file. The Prescriptions row needs an OrderId,
            // and the order does not exist until Confirm Order has run its transaction, so
            // the path is handed back and the checkout does the work afterwards.
            //
            // At that point PrescriptionService copies the file INTO the application's own
            // Uploads folder rather than storing the path the customer chose. The original
            // may sit on a USB stick, in a Downloads folder that gets emptied, or on a
            // phone that is unplugged a minute later; a reference to it would be a picture
            // the pharmacy could not open when it came to verify the order. The copy is
            // given a name built from the order number and a timestamp, so two customers
            // who both send "photo.jpg" cannot overwrite one another, and only a RELATIVE
            // path is written to the database, because an absolute path from one computer
            // means nothing on any other machine.
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            // Cancel is confirmed rather than taken at face value, because abandoning this
            // dialog also abandons the order: without a prescription the Confirm button
            // stays disabled, so a misclick here would leave the customer stuck at a step
            // they could not complete. The message names the alternative as well.
            DialogResult answer = MessageBox.Show(
                "Without a prescription this order cannot be confirmed.\r\n\r\n" +
                "You can go back to your cart and remove the prescription only medicine instead.",
                "No prescription attached", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            // Anything but OK means they changed their mind, so the dialog stays open with
            // the chosen file and the preview exactly as they were.
            if (answer != DialogResult.OK) return;

            // Cleared deliberately. A file may already have been accepted before Cancel was
            // pressed, and leaving the path in place would let the checkout read a
            // prescription from a dialog the customer explicitly abandoned.
            SelectedImagePath = "";
            ClearPreview();
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
