using System.Data;                  // DataTable and DataGridViewRow: the queue is a bound table
using System.Drawing;               // Image, Bitmap and Color, for the preview and row colours
using System.Windows.Forms;         // Form, DataGridView, PictureBox, MessageBox
using PharmaLinkApp.Helpers;        // UiTheme, so this matches the rest of the owner's area
using PharmaLinkApp.Services;       // PrescriptionService and OrderService, the two sources
// File and FileStream come from System.IO, in scope through ImplicitUsings.

// All screens share one namespace, so forms open each other by short name.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 16: the prescription verification queue.</summary>
    public partial class VerifyPrescriptionForm : Form
    {
        // Two services, joined on screen rather than in SQL: the lines suit one row only.
        private readonly PrescriptionService _prescriptions = new PrescriptionService();
        private readonly OrderService _orders = new OrderService();   // only GetOrderItems, and only for the selected row

        // True at first, so setting SelectedIndex in Load cannot fire a query too early.
        private bool _loading = true;

        // The constructor runs before the window exists, so every query is left to Load.
        public VerifyPrescriptionForm()
        {
            InitializeComponent();   // controls only; a failed query belongs in Load
        }

        // Load fires once every control exists, so the grid can be filled here.
        private void VerifyPrescriptionForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();   // styling first, so even a failed query lands on a finished window

            // The first three are the values the CHECK allows; "All" is a sentinel.
            cmbVerifyStatus.Items.AddRange(new object[] { "Pending", "Approved", "Rejected", "All" });

            cmbVerifyStatus.SelectedIndex = 0;   // "Pending": a work queue opens on what needs doing

            _loading = false;   // lowered now the filter holds its intended value
            LoadQueue();        // the one deliberate first query of this screen
        }

        // Presentation only, kept apart from anything that approves or rejects.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Verify Prescriptions");   // title, background and window rules

            panelHeader.BackColor = UiTheme.Primary;                    // the dark band above the work area
            lblTitle.Font = UiTheme.FontTitle;                          // the shared title font
            lblTitle.ForeColor = Color.White;                           // white is the readable pairing on Primary
            lblSubtitle.Font = UiTheme.FontSmall;                       // smaller: a subtitle, not a second heading
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);      // dimmed white, so it supports the title

            grpImage.Font = UiTheme.FontHeading;      // the caption over the photograph
            grpImage.ForeColor = UiTheme.Primary;     // brand colour ties the panel to the header band
            grpImage.BackColor = UiTheme.CardBack;    // a card fill, lifting the preview off the form

            lblOrderItems.Font = UiTheme.FontHeading;      // heading over the order lines grid
            lblOrderItems.ForeColor = UiTheme.TextDark;    // dark, marking a new section
            lblDoctor.Font = UiTheme.FontBody;             // a sentence, so the body font
            lblDoctor.ForeColor = UiTheme.TextDark;        // full strength: part of the decision
            lblImagePath.Font = UiTheme.FontSmall;         // small: the stored path is reference detail
            lblImagePath.ForeColor = UiTheme.TextMuted;    // muted, useful only when tracing a file
            lblNote.Font = UiTheme.FontSmall;              // the standing explanation of this queue
            lblNote.ForeColor = UiTheme.TextMuted;         // muted: an aside, not an instruction
            lblStatus.Font = UiTheme.FontSmall;            // the live outstanding count
            lblStatus.ForeColor = UiTheme.TextMuted;       // neutral, since the wording carries the tone

            UiTheme.StyleSecondary(btnBack);        // outline, so leaving never competes with deciding
            UiTheme.StyleSecondary(btnRefresh);     // also secondary: re-reading is a convenience
            UiTheme.StyleSuccess(btnApprove);       // green, matching an approved row
            UiTheme.StyleDanger(btnReject);         // red, matching a rejected row
            UiTheme.StyleGrid(dgvQueue);            // the same grid rules as every other screen
            UiTheme.StyleGrid(dgvOrderItems);       // the same for the lines grid, so they read as a pair
            dgvQueue.CellFormatting += dgvQueue_CellFormatting;   // wired here, beside the grid it colours
        }

        // Re-reads the queue: on load, on a filter change and after a decision.
        private void LoadQueue()
        {
            if (_loading) return;   // the guard that makes wiring the filter up early safe

            try   // a failed query must leave the window open on what it already had
            {
                string status = cmbVerifyStatus.SelectedItem.ToString();   // read once, so it cannot change mid-method

                // "All" becomes "" because the query uses the optional-filter pattern.
                if (status == "All") status = "";

                // PharmacyId comes from the session, so no control on screen can widen this.
                DataTable table = _prescriptions.GetQueueForPharmacy(UserSession.PharmacyId, status);
                dgvQueue.DataSource = table;   // binding creates the columns, so renames must follow

                // Columns exist only after a DataSource is set; the count covers the unbound case.
                if (dgvQueue.Columns.Count > 0)
                {
                    dgvQueue.Columns["PrescriptionId"].HeaderText = "Rx";   // renamed here, so the SQL keeps its names

                    // FillWeight is a proportion in Fill mode, so the ids give width to the names.
                    dgvQueue.Columns["PrescriptionId"].FillWeight = 28;
                    dgvQueue.Columns["OrderId"].HeaderText = "Order";        // the number owner and customer both quote
                    dgvQueue.Columns["OrderId"].FillWeight = 40;             // wider, since order numbers get read aloud
                    dgvQueue.Columns["Customer"].HeaderText = "Customer";    // renamed, so the heading never depends on the SQL
                    dgvQueue.Columns["DoctorName"].HeaderText = "Prescribing doctor";   // spelled out, not a database field name

                    // Hidden, not dropped: ShowImage needs the path, the grid does not.
                    dgvQueue.Columns["ImagePath"].Visible = false;
                    dgvQueue.Columns["UploadedAt"].HeaderText = "Uploaded";   // makes an old Pending row stand out

                    // Two distinct words: an Approved prescription can sit on a Placed order.
                    dgvQueue.Columns["VerifyStatus"].HeaderText = "Verification";
                    dgvQueue.Columns["OrderStatus"].HeaderText = "Order status";       // shown beside it, so the pair compares
                    dgvQueue.Columns["TotalAmount"].HeaderText = "Order total (Tk)";   // currency named once, in the header
                }

                // A separate count, so filtering to Approved cannot report zero work outstanding.
                int pending = _prescriptions.CountPending(UserSession.PharmacyId);

                lblStatus.Text = pending == 0   // two messages, because an empty queue is good news
                    ? "Nothing is waiting for verification right now."   // an all clear, not a count of zero
                    // Names the consequence: a Pending prescription is what blocks dispatch.
                    : pending + " prescription(s) are still Pending. Those orders cannot be dispatched until you decide.";

                UpdateSelection();   // rebinding moves the row without reliably raising SelectionChanged
            }
            catch (Exception ex)   // a failed read must not close a queue being worked through
            {
                // DbHelper has already turned the SqlException into a readable sentence.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Raised as each cell is painted, so it colours from the row's own data.
        private void dgvQueue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Row -1 is the header, and the event also fires mid-bind with no columns yet.
            if (e.RowIndex < 0 || dgvQueue.Columns.Count == 0) return;

            DataGridViewRow row = dgvQueue.Rows[e.RowIndex];   // the row being painted, not the selected one

            // Read from the cell, so the colour always matches the word on that row.
            object status = row.Cells["VerifyStatus"].Value;
            if (status == null) return;   // unbound mid-rebind; the default colour is the honest answer

            switch (status.ToString())   // set on the ROW style, so one pass colours the whole row
            {
                // Amber for work outstanding, the same amber as an order not yet confirmed.
                case "Pending": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;

                // The shared green and red, so these read as delivered and low stock do.
                case "Approved": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;
                case "Rejected": row.DefaultCellStyle.BackColor = UiTheme.LowStockBack; break;   // red means blocked

                // No default: the CHECK constraint allows only those three values.
            }
        }

        // Every change of row reloads the image, the lines and both buttons together.
        private void dgvQueue_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        // The one place the right hand panel is built, so its three parts always agree.
        private void UpdateSelection()
        {
            DataGridViewRow row = dgvQueue.CurrentRow;   // CurrentRow is null on an empty grid, not a throw

            ClearImage();   // disposed first, so no branch below can quietly abandon a bitmap

            if (row == null || row.Cells["PrescriptionId"].Value == null)   // nothing selected, or no row behind it
            {
                dgvOrderItems.DataSource = null;   // clear the lines rather than keep the last row's
                btnApprove.Enabled = false;        // no row means nothing to approve
                btnReject.Enabled = false;         // disabled together, so the pair is never half live
                return;                            // nothing else can be filled in without a row
            }

            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);   // Convert, since the cell value is boxed

            // The lines make the decision possible: which medicine needed a prescription.
            dgvOrderItems.DataSource = _orders.GetOrderItems(orderId);

            if (dgvOrderItems.Columns.Count > 0)   // same rule: columns exist only once bound
            {
                dgvOrderItems.Columns["MedicineName"].HeaderText = "Medicine";   // the name on the shelf
                dgvOrderItems.Columns["Strength"].HeaderText = "Strength";       // a prescription names one strength
                dgvOrderItems.Columns["Quantity"].HeaderText = "Qty";            // short, so the width goes to names

                // These are the figures stored at checkout, not today's shelf prices.
                dgvOrderItems.Columns["UnitPrice"].HeaderText = "Unit price (Tk)";
                dgvOrderItems.Columns["Subtotal"].HeaderText = "Line total (Tk)";   // "line", so it is not read as the order total
            }

            object doctor = row.Cells["DoctorName"].Value;   // object, because the column is nullable

            // DoctorName is nullable, so null and DBNull are both covered by one test.
            lblDoctor.Text = "Doctor: " + (doctor == null || doctor == DBNull.Value ? "(not given)" : doctor.ToString());

            string storedPath = row.Cells["ImagePath"].Value == null   // read defensively for the same reason
                ? "" : row.Cells["ImagePath"].Value.ToString();   // "" rather than null, so ShowImage tests once

            ShowImage(storedPath);   // resolves the path and loads it, or explains why it cannot

            string status = row.Cells["VerifyStatus"].Value.ToString();   // safe: the column is NOT NULL

            // Each button is off only for the status it would set, so a mistake can be undone.
            btnApprove.Enabled = status != "Approved";
            btnReject.Enabled = status != "Rejected";   // the mirror, so the two rules stay symmetrical
        }

        // Releases the displayed bitmap, before every load and again on the way out.
        private void ClearImage()
        {
            if (picPrescription.Image != null)   // a Bitmap holds unmanaged memory, so dispose it
            {
                picPrescription.Image.Dispose();   // frees the buffer now, not at some later collection

                // Nulled after disposing, so nothing can repaint an image already released.
                picPrescription.Image = null;
            }
        }

        // Turns the stored path into a picture, or a sentence saying why not.
        private void ShowImage(string storedPath)
        {
            // Stored relative and resolved here, so each installation finds its own.
            string fullPath = PrescriptionService.ResolveImagePath(storedPath);

            lblImagePath.Text = storedPath;   // the stored value, since that is what gets traced

            // One test for two absences: no path recorded, and a path pointing at no file.
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                // The row is intact; only the copied image is missing from this Uploads folder.
                lblImagePath.Text = storedPath + Environment.NewLine +
                                    // the stored path stays in the message: a search starts there
                                    "(the image file is not on this machine - the row still records where it was uploaded)";
                return;   // leaves the frame empty, which ClearImage already guaranteed
            }

            try   // decoding can fail on a truncated or non image file, and must not close the queue
            {
                // FromFile would LOCK the file; a stream plus a copy releases it at once.
                using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                using (Image original = Image.FromStream(stream))   // FromStream is the half that avoids the lock
                {
                    picPrescription.Image = new Bitmap(original);   // an independent copy, held as pixels
                }
            }
            catch (Exception ex)   // a corrupt file, a locked file and an unsupported format alike
            {
                // Reported in the path label: a modal box would interrupt a queue being worked.
                lblImagePath.Text = "The image could not be opened: " + ex.Message;
            }
        }

        // Shared by both decision buttons; the status word is the only difference.
        private void SetStatus(string newStatus)
        {
            DataGridViewRow row = dgvQueue.CurrentRow;   // the row the owner is looking at
            if (row == null) return;   // nothing selected, so there is nothing to decide

            // Both ids are read before the dialog opens, so a rebind cannot change them.
            int prescriptionId = Convert.ToInt32(row.Cells["PrescriptionId"].Value);
            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);   // the number the owner recognises

            string question = newStatus == "Approved"   // each message states the consequence, not the button word
                ? "Approve the prescription on order " + orderId + "?\r\n\r\n" +   // the order number, not the Rx id
                  // "every" matters: one order can carry more than one prescription.
                  "Once every prescription on the order is approved, the Confirm button on that order becomes usable."
                : "Reject the prescription on order " + orderId + "?\r\n\r\n" +   // the same number on both paths
                  // Says the rejection is recoverable, so it does not read as cancelling the order.
                  "The order stays at 'Placed' and cannot be dispatched. The customer can upload a clearer photograph.";

            // Confirmed first, because the customer sees it. Yes/No: this is a question.
            DialogResult answer = MessageBox.Show(question, newStatus + " prescription",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);   // Question: routine, not destructive

            if (answer != DialogResult.Yes) return;   // anything else leaves the row untouched

            // The UPDATE filters on PharmacyId too, and returns true only if one row changed.
            if (_prescriptions.SetVerifyStatus(prescriptionId, UserSession.PharmacyId, newStatus))
            {
                // Both numbers named, since the question quoted an order and this reports an Rx.
                lblStatus.Text = "Prescription " + prescriptionId + " on order " + orderId + " is now " + newStatus + ".";

                LoadQueue();   // re-queried, not patched, so colours and counts come from the database
            }
        }

        // The two decisions pass the exact strings CK_Prescriptions_Status allows.
        private void btnApprove_Click(object sender, EventArgs e) => SetStatus("Approved");
        private void btnReject_Click(object sender, EventArgs e) => SetStatus("Rejected");   // the same one line shape

        // The filter and the Refresh button share this handler, so both re-run the query.
        private void Filter_Changed(object sender, EventArgs e) => LoadQueue();

        // Back does more than close, which is why it is a block and not an arrow.
        private void btnBack_Click(object sender, EventArgs e)
        {
            ClearImage();   // closing a form does not dispose an image assigned at run time
            Close();        // hands control back to the dashboard that opened this with ShowDialog
        }
    }
}
