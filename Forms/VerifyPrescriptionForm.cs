using System.Data;                  // DataTable and DataGridViewRow work, the queue is bound to a table
using System.Drawing;               // Image, Bitmap and Color, for the preview and the row colours
using System.Windows.Forms;         // Form, DataGridView, PictureBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, so this screen matches the rest of the owner's area
using PharmaLinkApp.Services;       // PrescriptionService and OrderService, the two data sources
// File and FileStream come from System.IO, which is in scope through the project's
// ImplicitUsings setting rather than a using line of its own.

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 16. The pharmacy's prescription verification queue.
    ///
    /// When an order contains a medicine whose RequiresRx flag is set, it
    /// appears here with the uploaded image and the doctor's name. Approve or
    /// Reject sets Prescriptions.VerifyStatus, and an order whose prescription
    /// is still Pending can never be moved to Confirmed.
    /// </summary>
    public partial class VerifyPrescriptionForm : Form
    {
        // The prescription queue and the order lines come from two different services,
        // each owning its own queries. The form joins them on screen rather than in SQL,
        // because the lines are only needed for the one row the owner is looking at.
        private readonly PrescriptionService _prescriptions = new PrescriptionService();
        private readonly OrderService _orders = new OrderService();

        // Starts true so the filter handler does nothing while the Load handler populates
        // the dropdown. Assigning SelectedIndex raises SelectedIndexChanged, which is
        // wired to Filter_Changed, so without this the queue would be queried before the
        // filter had settled on its intended value.
        private bool _loading = true;

        public VerifyPrescriptionForm()
        {
            // Controls only. The queries live in Load, where a failure can be shown to the
            // owner instead of breaking construction of the window.
            InitializeComponent();
        }

        private void VerifyPrescriptionForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            // The first three are exactly the values CK_Prescriptions_Status allows, and
            // "All" is a sentinel translated to an empty string in LoadQueue. Note the
            // order: unlike the other filters in this project the sentinel is LAST,
            // because the useful default here is the work still to be done.
            cmbVerifyStatus.Items.AddRange(new object[] { "Pending", "Approved", "Rejected", "All" });

            // Index 0 is "Pending" on purpose. This screen is a work queue, so it opens
            // showing what needs a decision rather than the whole history.
            cmbVerifyStatus.SelectedIndex = 0;

            // Lowered only now that the filter holds its intended value, so the call below
            // is the first query this screen runs.
            _loading = false;
            LoadQueue();
        }

        // Presentation only: fonts, colours, grid styling and the one event subscription
        // that has to be made in code. Kept apart from the data methods so a change of
        // appearance cannot alter what is approved or rejected.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Verify Prescriptions");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            grpImage.Font = UiTheme.FontHeading;
            grpImage.ForeColor = UiTheme.Primary;
            grpImage.BackColor = UiTheme.CardBack;

            lblOrderItems.Font = UiTheme.FontHeading;
            lblOrderItems.ForeColor = UiTheme.TextDark;
            lblDoctor.Font = UiTheme.FontBody;
            lblDoctor.ForeColor = UiTheme.TextDark;
            lblImagePath.Font = UiTheme.FontSmall;
            lblImagePath.ForeColor = UiTheme.TextMuted;
            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSecondary(btnRefresh);
            UiTheme.StyleSuccess(btnApprove);
            UiTheme.StyleDanger(btnReject);
            UiTheme.StyleGrid(dgvQueue);
            UiTheme.StyleGrid(dgvOrderItems);
            dgvQueue.CellFormatting += dgvQueue_CellFormatting;
        }

        private void LoadQueue()
        {
            // The guard that makes the filter safe to wire up before it has been populated.
            if (_loading) return;

            // A failed query leaves the window open showing what it already had, rather
            // than closing a screen the owner may be part way through working through.
            try
            {
                string status = cmbVerifyStatus.SelectedItem.ToString();

                // "All" becomes an empty string because the query uses the optional-filter
                // pattern (@VerifyStatus = '' OR p.VerifyStatus = @VerifyStatus). Sending
                // the word "All" would match no rows, since it is not one of the three
                // values the column is allowed to hold.
                if (status == "All") status = "";

                // UserSession.PharmacyId is what scopes this queue to the signed-in owner's
                // own shop, and it is read from the session rather than from any control on
                // screen. There is therefore nothing here a user could edit to see another
                // pharmacy's prescriptions, which are a customer's medical records.
                DataTable table = _prescriptions.GetQueueForPharmacy(UserSession.PharmacyId, status);
                dgvQueue.DataSource = table;

                // Columns exist only after a DataSource has been set, so every line below
                // has to follow the binding. The count check covers the unbound case.
                if (dgvQueue.Columns.Count > 0)
                {
                    // Headers are renamed here rather than aliased in the query, so the SQL
                    // keeps returning the names the C# code indexes cells by.
                    dgvQueue.Columns["PrescriptionId"].HeaderText = "Rx";

                    // FillWeight is a proportion, not a pixel count: the grid is in Fill
                    // mode, so shrinking the two id columns gives the width to the customer
                    // and doctor names, which are the columns worth reading.
                    dgvQueue.Columns["PrescriptionId"].FillWeight = 28;
                    dgvQueue.Columns["OrderId"].HeaderText = "Order";
                    dgvQueue.Columns["OrderId"].FillWeight = 40;
                    dgvQueue.Columns["Customer"].HeaderText = "Customer";
                    dgvQueue.Columns["DoctorName"].HeaderText = "Prescribing doctor";

                    // Hidden, not removed from the query. The stored path is carried so
                    // ShowImage can load the photograph for the selected row, but a column
                    // of folder paths tells the owner nothing and would crowd out the
                    // columns that do.
                    dgvQueue.Columns["ImagePath"].Visible = false;
                    dgvQueue.Columns["UploadedAt"].HeaderText = "Uploaded";

                    // "Verification" and "Order status" are deliberately distinct words,
                    // because the two travel separately: a prescription can be Approved
                    // while its order is still Placed.
                    dgvQueue.Columns["VerifyStatus"].HeaderText = "Verification";
                    dgvQueue.Columns["OrderStatus"].HeaderText = "Order status";
                    dgvQueue.Columns["TotalAmount"].HeaderText = "Order total (Tk)";
                }

                // A separate query rather than counting the rows above, and that is the
                // point: it counts every Pending prescription for this pharmacy whatever
                // the filter is showing. Counting the bound rows instead would report zero
                // outstanding work the moment the owner filtered to Approved.
                int pending = _prescriptions.CountPending(UserSession.PharmacyId);

                // Two messages rather than "0 prescriptions are pending", because an empty
                // queue is good news and should read as such. The second names the
                // consequence, since a pending prescription is what blocks dispatch.
                lblStatus.Text = pending == 0
                    ? "Nothing is waiting for verification right now."
                    : pending + " prescription(s) are still Pending. Those orders cannot be dispatched until you decide.";

                // Rebinding moves the current row without reliably raising
                // SelectionChanged, so the image, the lines and both buttons are brought
                // back into step explicitly. Without this the panel would still describe
                // the prescription that was selected before the reload.
                UpdateSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvQueue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // RowIndex below zero is the header, which has no data behind it, and the
            // column check covers the moment before binding. CellFormatting is raised very
            // often, including during binding itself, so both guards earn their place.
            if (e.RowIndex < 0 || dgvQueue.Columns.Count == 0) return;

            DataGridViewRow row = dgvQueue.Rows[e.RowIndex];

            // Read from the cell rather than the DataTable, so the colour always matches
            // the word the owner can actually see on that row.
            object status = row.Cells["VerifyStatus"].Value;
            if (status == null) return;

            // Applied to the ROW's DefaultCellStyle, so one pass colours the whole row.
            // Setting e.CellStyle instead would tint only the cell being formatted.
            switch (status.ToString())
            {
                // Amber for work outstanding, the same amber the order screens use for an
                // order that has been placed but not yet confirmed.
                case "Pending": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;

                // The shared green and red from UiTheme, so approved and rejected read the
                // same way here as delivered and low stock do elsewhere.
                case "Approved": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;
                case "Rejected": row.DefaultCellStyle.BackColor = UiTheme.LowStockBack; break;

                // No default: VerifyStatus is constrained to those three values, so there
                // is no fourth case to paint. The grid's own alternating style handles
                // anything this switch does not touch.
            }
        }

        // Every change of row has to reload the image, the order lines and both buttons,
        // so the handler forwards to the single method that does all three.
        private void dgvQueue_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            // CurrentRow rather than SelectedRows[0]: the grid is single select, and
            // CurrentRow is null rather than throwing when the queue is empty.
            DataGridViewRow row = dgvQueue.CurrentRow;

            // Disposed FIRST, before anything else can replace it. Every path below either
            // loads a new photograph or leaves the frame empty, so releasing the old bitmap
            // here means there is no branch on which one is quietly abandoned.
            ClearImage();

            // Nothing selected, or a row with no prescription behind it. Both buttons are
            // disabled together, because approving or rejecting needs a row to act on.
            if (row == null || row.Cells["PrescriptionId"].Value == null)
            {
                dgvOrderItems.DataSource = null;   // clear the lines rather than leave the last row's
                btnApprove.Enabled = false;
                btnReject.Enabled = false;
                return;
            }

            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);

            // The order's lines are what make the decision possible: the owner has to see
            // WHICH medicine needed a prescription before judging whether this photograph
            // covers it. They are fetched per selection rather than loaded with the queue,
            // since only one row is being looked at at a time.
            dgvOrderItems.DataSource = _orders.GetOrderItems(orderId);

            if (dgvOrderItems.Columns.Count > 0)
            {
                dgvOrderItems.Columns["MedicineName"].HeaderText = "Medicine";
                dgvOrderItems.Columns["Strength"].HeaderText = "Strength";
                dgvOrderItems.Columns["Quantity"].HeaderText = "Qty";

                // These are the stored order figures, written at checkout, not today's
                // shelf prices, so an old order still shows what the customer was charged.
                dgvOrderItems.Columns["UnitPrice"].HeaderText = "Unit price (Tk)";
                dgvOrderItems.Columns["Subtotal"].HeaderText = "Line total (Tk)";
            }

            object doctor = row.Cells["DoctorName"].Value;

            // DoctorName is nullable in the table, so both null and DBNull are covered: the
            // first is an unbound cell, the second is a stored NULL. "(not given)" is shown
            // rather than an empty label, so a missing name reads as a fact about the
            // upload instead of as a screen that failed to load.
            lblDoctor.Text = "Doctor: " + (doctor == null || doctor == DBNull.Value ? "(not given)" : doctor.ToString());

            // The path is read defensively for the same reason, and left as "" when absent
            // so ShowImage has one kind of empty to test for.
            string storedPath = row.Cells["ImagePath"].Value == null
                ? "" : row.Cells["ImagePath"].Value.ToString();

            ShowImage(storedPath);

            string status = row.Cells["VerifyStatus"].Value.ToString();

            // Each button is disabled only for the status it would set, rather than both
            // being disabled once a decision has been made. That is deliberate: a
            // prescription rejected by mistake can still be approved afterwards, and an
            // approval can be withdrawn if the photograph turns out to be for someone else.
            // What it does prevent is the pointless write of setting a status to itself.
            btnApprove.Enabled = status != "Approved";
            btnReject.Enabled = status != "Rejected";
        }

        private void ClearImage()
        {
            // Disposed rather than simply replaced. A Bitmap holds unmanaged memory, and an
            // owner clicking down a queue of twenty prescriptions loads twenty of them.
            if (picPrescription.Image != null)
            {
                picPrescription.Image.Dispose();

                // Nulled after disposing, so nothing can repaint an image that has already
                // been released, which would throw on the next paint.
                picPrescription.Image = null;
            }
        }

        private void ShowImage(string storedPath)
        {
            // The database stores a RELATIVE path, so it is resolved against the
            // application's own folder here. Storing the absolute path the customer's
            // machine used would break the moment the application ran from anywhere else;
            // resolving at display time means each installation finds its own copy.
            string fullPath = PrescriptionService.ResolveImagePath(storedPath);

            // The stored value is shown as it is, not the resolved one. It is the value
            // recorded against the order, so it is the one worth quoting when a file has
            // to be traced.
            lblImagePath.Text = storedPath;

            // Two different absences handled by one test: no path recorded at all, and a
            // path that no longer points at a file. Checked BEFORE opening, so a missing
            // file is an explanation rather than a caught exception.
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                // The wording separates the row from the file deliberately. The
                // Prescriptions row is intact and still proves an upload happened; it is
                // only the copied image that is not in this installation's Uploads folder,
                // which is what a database restored without that folder looks like.
                lblImagePath.Text = storedPath + Environment.NewLine +
                                    "(the image file is not on this machine - the row still records where it was uploaded)";
                return;
            }

            try
            {
                // Loaded through a stream and copied, so the file is not locked
                // and can still be replaced by a fresh upload.
                // Image.FromFile would keep a LOCK on the file for as long as the Image
                // object lives, so the customer could not replace a blurry prescription
                // while the owner had it open. Reading through a stream and copying into
                // a new Bitmap releases the file immediately: both using blocks dispose
                // at the closing brace, and the Bitmap that survives holds pixels in
                // memory rather than a handle to disk.
                using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                using (Image original = Image.FromStream(stream))
                {
                    picPrescription.Image = new Bitmap(original);   // independent copy
                }
            }
            catch (Exception ex)
            {
                // Reported in the path label rather than in a message box. A file that
                // cannot be decoded is information about that one row, and a modal dialog
                // would interrupt an owner clicking through a queue.
                lblImagePath.Text = "The image could not be opened: " + ex.Message;
            }
        }

        // ---------------------------------------------------------------------

        private void SetStatus(string newStatus)
        {
            // Approve and Reject differ by one word, so they share this method and pass the
            // status they want. Two near-identical handlers would be two places for the
            // confirmation wording and the refresh to drift apart.
            DataGridViewRow row = dgvQueue.CurrentRow;
            if (row == null) return;   // nothing selected, so there is nothing to decide

            // Both ids are read from the row before the dialog opens. The prescription id
            // is what gets updated; the order number is what the owner recognises, so it is
            // the one quoted in the question below.
            int prescriptionId = Convert.ToInt32(row.Cells["PrescriptionId"].Value);
            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);

            // The two messages state the CONSEQUENCE rather than repeating the button's
            // own word, because that consequence is the part the owner is deciding on:
            // approving unblocks the Confirm button on that order, rejecting leaves the
            // order sitting at 'Placed' where it cannot be dispatched.
            string question = newStatus == "Approved"
                ? "Approve the prescription on order " + orderId + "?\r\n\r\n" +
                  "Once every prescription on the order is approved, the Confirm button on that order becomes usable."
                : "Reject the prescription on order " + orderId + "?\r\n\r\n" +
                  "The order stays at 'Placed' and cannot be dispatched. The customer can upload a clearer photograph.";

            // Confirmed before writing, because this decision is visible to the customer
            // and affects whether their medicine is sent. Yes and No rather than OK and
            // Cancel, since the prompt is a question.
            DialogResult answer = MessageBox.Show(question, newStatus + " prescription",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;   // anything else leaves the row untouched

            // The pharmacy id is passed with the prescription id, and the UPDATE joins to
            // Orders and filters on it, so an owner can only ever change a prescription
            // that belongs to their own shop. The method returns true only when exactly one
            // row was affected, which is why the message and the reload sit inside the if:
            // a false means nothing changed, so claiming success would be a lie.
            if (_prescriptions.SetVerifyStatus(prescriptionId, UserSession.PharmacyId, newStatus))
            {
                // Named in full, because the owner has just been asked about an order
                // number and is now being told about a prescription number.
                lblStatus.Text = "Prescription " + prescriptionId + " on order " + orderId + " is now " + newStatus + ".";

                // Re-queried rather than patched in the grid, so the row colour, the
                // pending count and the two buttons all come from the database rather than
                // from an assumption about what the update did. Note this overwrites the
                // status line just set when the current filter is "Pending", which is
                // correct: the row has left that filter and the message is replaced by the
                // new outstanding count.
                LoadQueue();
            }
        }

        // Three one-line handlers. The two decisions pass the exact strings
        // CK_Prescriptions_Status allows, so a typo here would be rejected by the database
        // rather than stored as an unrecognised status.
        private void btnApprove_Click(object sender, EventArgs e) => SetStatus("Approved");
        private void btnReject_Click(object sender, EventArgs e) => SetStatus("Rejected");

        // The filter and the Refresh button are wired to this same handler, so both simply
        // re-run the query with whatever the dropdown currently holds.
        private void Filter_Changed(object sender, EventArgs e) => LoadQueue();

        private void btnBack_Click(object sender, EventArgs e)
        {
            // The bitmap is released before the window goes. It was created from a stream
            // rather than taken from the designer, so nothing else owns it, and closing a
            // form does not dispose an image assigned to a PictureBox at run time.
            ClearImage();
            Close();
        }
    }
}
