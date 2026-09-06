using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

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
        private readonly PrescriptionService _prescriptions = new PrescriptionService();
        private readonly OrderService _orders = new OrderService();
        private bool _loading = true;

        public VerifyPrescriptionForm()
        {
            InitializeComponent();
        }

        private void VerifyPrescriptionForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();

            cmbVerifyStatus.Items.AddRange(new object[] { "Pending", "Approved", "Rejected", "All" });
            cmbVerifyStatus.SelectedIndex = 0;

            _loading = false;
            LoadQueue();
        }

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
            if (_loading) return;

            try
            {
                string status = cmbVerifyStatus.SelectedItem.ToString();
                if (status == "All") status = "";

                DataTable table = _prescriptions.GetQueueForPharmacy(UserSession.PharmacyId, status);
                dgvQueue.DataSource = table;

                if (dgvQueue.Columns.Count > 0)
                {
                    dgvQueue.Columns["PrescriptionId"].HeaderText = "Rx";
                    dgvQueue.Columns["PrescriptionId"].FillWeight = 28;
                    dgvQueue.Columns["OrderId"].HeaderText = "Order";
                    dgvQueue.Columns["OrderId"].FillWeight = 40;
                    dgvQueue.Columns["Customer"].HeaderText = "Customer";
                    dgvQueue.Columns["DoctorName"].HeaderText = "Prescribing doctor";
                    dgvQueue.Columns["ImagePath"].Visible = false;
                    dgvQueue.Columns["UploadedAt"].HeaderText = "Uploaded";
                    dgvQueue.Columns["VerifyStatus"].HeaderText = "Verification";
                    dgvQueue.Columns["OrderStatus"].HeaderText = "Order status";
                    dgvQueue.Columns["TotalAmount"].HeaderText = "Order total (Tk)";
                }

                int pending = _prescriptions.CountPending(UserSession.PharmacyId);
                lblStatus.Text = pending == 0
                    ? "Nothing is waiting for verification right now."
                    : pending + " prescription(s) are still Pending. Those orders cannot be dispatched until you decide.";

                UpdateSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvQueue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvQueue.Columns.Count == 0) return;

            DataGridViewRow row = dgvQueue.Rows[e.RowIndex];
            object status = row.Cells["VerifyStatus"].Value;
            if (status == null) return;

            switch (status.ToString())
            {
                case "Pending": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;
                case "Approved": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;
                case "Rejected": row.DefaultCellStyle.BackColor = UiTheme.LowStockBack; break;
            }
        }

        private void dgvQueue_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            DataGridViewRow row = dgvQueue.CurrentRow;

            ClearImage();

            if (row == null || row.Cells["PrescriptionId"].Value == null)
            {
                dgvOrderItems.DataSource = null;
                btnApprove.Enabled = false;
                btnReject.Enabled = false;
                return;
            }

            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);
            dgvOrderItems.DataSource = _orders.GetOrderItems(orderId);

            if (dgvOrderItems.Columns.Count > 0)
            {
                dgvOrderItems.Columns["MedicineName"].HeaderText = "Medicine";
                dgvOrderItems.Columns["Strength"].HeaderText = "Strength";
                dgvOrderItems.Columns["Quantity"].HeaderText = "Qty";
                dgvOrderItems.Columns["UnitPrice"].HeaderText = "Unit price (Tk)";
                dgvOrderItems.Columns["Subtotal"].HeaderText = "Line total (Tk)";
            }

            object doctor = row.Cells["DoctorName"].Value;
            lblDoctor.Text = "Doctor: " + (doctor == null || doctor == DBNull.Value ? "(not given)" : doctor.ToString());

            string storedPath = row.Cells["ImagePath"].Value == null
                ? "" : row.Cells["ImagePath"].Value.ToString();

            ShowImage(storedPath);

            string status = row.Cells["VerifyStatus"].Value.ToString();
            btnApprove.Enabled = status != "Approved";
            btnReject.Enabled = status != "Rejected";
        }

        private void ClearImage()
        {
            if (picPrescription.Image != null)
            {
                picPrescription.Image.Dispose();
                picPrescription.Image = null;
            }
        }

        private void ShowImage(string storedPath)
        {
            string fullPath = PrescriptionService.ResolveImagePath(storedPath);
            lblImagePath.Text = storedPath;

            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                lblImagePath.Text = storedPath + Environment.NewLine +
                                    "(the image file is not on this machine - the row still records where it was uploaded)";
                return;
            }

            try
            {
                // Loaded through a stream and copied, so the file is not locked
                // and can still be replaced by a fresh upload.
                using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                using (Image original = Image.FromStream(stream))
                {
                    picPrescription.Image = new Bitmap(original);
                }
            }
            catch (Exception ex)
            {
                lblImagePath.Text = "The image could not be opened: " + ex.Message;
            }
        }

        // ---------------------------------------------------------------------

        private void SetStatus(string newStatus)
        {
            DataGridViewRow row = dgvQueue.CurrentRow;
            if (row == null) return;

            int prescriptionId = Convert.ToInt32(row.Cells["PrescriptionId"].Value);
            int orderId = Convert.ToInt32(row.Cells["OrderId"].Value);

            string question = newStatus == "Approved"
                ? "Approve the prescription on order " + orderId + "?\r\n\r\n" +
                  "Once every prescription on the order is approved, the Confirm button on that order becomes usable."
                : "Reject the prescription on order " + orderId + "?\r\n\r\n" +
                  "The order stays at 'Placed' and cannot be dispatched. The customer can upload a clearer photograph.";

            DialogResult answer = MessageBox.Show(question, newStatus + " prescription",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            if (_prescriptions.SetVerifyStatus(prescriptionId, UserSession.PharmacyId, newStatus))
            {
                lblStatus.Text = "Prescription " + prescriptionId + " on order " + orderId + " is now " + newStatus + ".";
                LoadQueue();
            }
        }

        private void btnApprove_Click(object sender, EventArgs e) => SetStatus("Approved");
        private void btnReject_Click(object sender, EventArgs e) => SetStatus("Rejected");
        private void Filter_Changed(object sender, EventArgs e) => LoadQueue();

        private void btnBack_Click(object sender, EventArgs e)
        {
            ClearImage();
            Close();
        }
    }
}
