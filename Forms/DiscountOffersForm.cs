using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 14. The pharmacy owner's time limited percentage discounts.
    ///
    /// The percentage must be greater than 0 and no more than 70, and the end
    /// date cannot be earlier than the start date. Both rules are enforced here
    /// and again by CK_Offers_Percent and CK_Offers_Dates on the Offers table.
    /// The discounted price itself is always computed inside the SQL query, so
    /// this screen, the customer's Offers screen, the cart and the invoice can
    /// never disagree.
    /// </summary>
    public partial class DiscountOffersForm : Form
    {
        private readonly OfferService _offers = new OfferService();
        private readonly MedicineService _medicines = new MedicineService();

        private readonly int _preselectMedicineId;
        private List<Medicine> _medicineList = new List<Medicine>();
        private int _selectedOfferId;
        private bool _loading = true;

        public DiscountOffersForm() : this(0) { }

        public DiscountOffersForm(int preselectMedicineId)
        {
            InitializeComponent();
            _preselectMedicineId = preselectMedicineId;
        }

        private void DiscountOffersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadMedicines();

            dtpStart.Value = DateTime.Today;
            dtpEnd.Value = DateTime.Today.AddDays(14);

            _loading = false;
            LoadGrid();

            if (_preselectMedicineId > 0) SelectMedicine(_preselectMedicineId);
            ValidateAll();
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Discount Offers");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            lblGridTitle.Font = UiTheme.FontHeading;
            lblGridTitle.ForeColor = UiTheme.TextDark;

            grpEditor.Font = UiTheme.FontHeading;
            grpEditor.ForeColor = UiTheme.Primary;
            grpEditor.BackColor = UiTheme.CardBack;
            foreach (Control child in grpEditor.Controls)
            {
                child.Font = UiTheme.FontBody;
                child.ForeColor = UiTheme.TextDark;
                if (child is Label label && label.Name.EndsWith("Error"))
                {
                    label.Font = UiTheme.FontSmall;
                    label.ForeColor = UiTheme.Danger;
                }
            }

            lblPreview.Font = UiTheme.FontSmall;
            lblPreview.ForeColor = UiTheme.Success;
            lblNote.Font = UiTheme.FontSmall;
            lblNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSuccess(btnCreate);
            UiTheme.StylePrimary(btnUpdate);
            UiTheme.StyleSecondary(btnClearEditor);
            UiTheme.StyleDanger(btnPause);
            UiTheme.StyleAccent(btnResume);
            UiTheme.StyleDanger(btnDelete);
            UiTheme.StyleGrid(dgvOffers);
            dgvOffers.CellFormatting += dgvOffers_CellFormatting;
        }

        private void LoadMedicines()
        {
            _medicineList = _medicines.GetSimpleListForPharmacy(UserSession.PharmacyId);

            cmbMedicine.Items.Clear();
            cmbMedicine.Items.Add("- choose one of your medicines -");
            foreach (Medicine medicine in _medicineList)
            {
                cmbMedicine.Items.Add(medicine.MedicineId + " - " + medicine.MedicineName + " " +
                                      medicine.Strength + "  (Tk " + medicine.UnitPrice.ToString("N2") + ")");
            }
            cmbMedicine.SelectedIndex = 0;
        }

        private void SelectMedicine(int medicineId)
        {
            for (int i = 1; i < cmbMedicine.Items.Count; i++)
            {
                string text = cmbMedicine.Items[i].ToString();
                if (int.Parse(text.Substring(0, text.IndexOf(' '))) == medicineId)
                {
                    cmbMedicine.SelectedIndex = i;
                    return;
                }
            }
        }

        private int SelectedMedicineId()
        {
            if (cmbMedicine.SelectedIndex <= 0) return 0;
            string text = cmbMedicine.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void LoadGrid()
        {
            if (_loading) return;

            try
            {
                DataTable table = _offers.GetForPharmacy(UserSession.PharmacyId);
                dgvOffers.DataSource = table;

                if (dgvOffers.Columns.Count > 0)
                {
                    dgvOffers.Columns["OfferId"].HeaderText = "ID";
                    dgvOffers.Columns["OfferId"].FillWeight = 28;
                    dgvOffers.Columns["OfferTitle"].HeaderText = "Offer";
                    dgvOffers.Columns["OfferTitle"].FillWeight = 120;
                    dgvOffers.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvOffers.Columns["Strength"].HeaderText = "Strength";
                    dgvOffers.Columns["Strength"].FillWeight = 45;
                    dgvOffers.Columns["OriginalPrice"].HeaderText = "Was (Tk)";
                    dgvOffers.Columns["DiscountPercent"].HeaderText = "Off %";
                    dgvOffers.Columns["DiscountPercent"].FillWeight = 38;
                    dgvOffers.Columns["DiscountedPrice"].HeaderText = "Now (Tk)";
                    dgvOffers.Columns["StartDate"].HeaderText = "From";
                    dgvOffers.Columns["EndDate"].HeaderText = "Until";
                    dgvOffers.Columns["IsActive"].Visible = false;
                    dgvOffers.Columns["OfferState"].HeaderText = "State";
                    dgvOffers.Columns["OfferState"].FillWeight = 52;
                }

                lblGridTitle.Text = "My offers  (" + table.Rows.Count + ")   -   " +
                                    _offers.CountRunningForPharmacy(UserSession.PharmacyId) + " running today";

                UpdateGridButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvOffers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvOffers.Columns.Count == 0) return;

            DataGridViewRow row = dgvOffers.Rows[e.RowIndex];
            object state = row.Cells["OfferState"].Value;
            if (state == null) return;

            switch (state.ToString())
            {
                case "Running": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;
                case "Scheduled": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;
                default: row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); break;
            }
        }

        private void dgvOffers_SelectionChanged(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvOffers.CurrentRow;
            if (row == null || row.Cells["OfferId"].Value == null)
            {
                _selectedOfferId = 0;
                UpdateGridButtons();
                return;
            }

            _loading = true;
            _selectedOfferId = Convert.ToInt32(row.Cells["OfferId"].Value);
            txtOfferTitle.Text = row.Cells["OfferTitle"].Value.ToString();
            txtPercent.Text = row.Cells["DiscountPercent"].Value.ToString();
            dtpStart.Value = Convert.ToDateTime(row.Cells["StartDate"].Value);
            dtpEnd.Value = Convert.ToDateTime(row.Cells["EndDate"].Value);
            _loading = false;

            UpdateGridButtons();
            ValidateAll();
        }

        private void UpdateGridButtons()
        {
            DataGridViewRow row = dgvOffers.CurrentRow;
            bool hasRow = row != null && row.Cells["OfferId"].Value != null;

            bool active = hasRow && row.Cells["IsActive"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsActive"].Value);

            btnPause.Enabled = hasRow && active;
            btnResume.Enabled = hasRow && !active;
            btnDelete.Enabled = hasRow;
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        private void Field_Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            ValidateAll();
        }

        private bool ValidateAll()
        {
            bool ok = true;

            bool medicineChosen = SelectedMedicineId() > 0 || _selectedOfferId > 0;
            ok &= Check(medicineChosen, lblMedicineError, cmbMedicine,
                        "Choose which of your medicines the discount applies to.");

            ok &= Check(!Validator.IsBlank(txtOfferTitle.Text), lblOfferTitleError, txtOfferTitle,
                        "Give the offer a title, for example 'Fever Season Pack - 12% off'.");

            decimal percent;
            bool percentOk = Validator.IsDiscountPercent(txtPercent.Text, out percent);
            ok &= Check(percentOk, lblPercentError, txtPercent,
                        "The discount must be greater than 0 and no more than 70 (CK_Offers_Percent).");

            bool datesOk = dtpEnd.Value.Date >= dtpStart.Value.Date;
            ok &= Check(datesOk, lblDateError, null,
                        "The end date cannot be earlier than the start date (CK_Offers_Dates).");

            // Live preview of what the customer will actually pay.
            int medicineId = SelectedMedicineId();
            if (percentOk && medicineId > 0)
            {
                Medicine medicine = _medicineList.Find(m => m.MedicineId == medicineId);
                if (medicine != null)
                {
                    decimal newPrice = decimal.Round(medicine.UnitPrice * (1 - percent / 100m), 2);
                    lblPreview.Text = medicine.MedicineName + " " + medicine.Strength +
                                      ":  Tk " + medicine.UnitPrice.ToString("N2") +
                                      "  ->  Tk " + newPrice.ToString("N2") +
                                      "   (customer saves Tk " + (medicine.UnitPrice - newPrice).ToString("N2") + " per unit)";
                }
            }
            else
            {
                lblPreview.Text = "";
            }

            btnCreate.Enabled = ok && SelectedMedicineId() > 0;
            btnUpdate.Enabled = ok && _selectedOfferId > 0;
            return ok;
        }

        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------

        private void btnCreate_Click(object sender, EventArgs e)
        {
            if (!ValidateAll()) return;

            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;

            decimal percent = decimal.Parse(txtPercent.Text);

            // The INSERT ... SELECT carries WHERE PharmacyId, so an offer can
            // never be created on another pharmacy's medicine.
            if (_offers.Create(medicineId, UserSession.PharmacyId, txtOfferTitle.Text,
                               percent, dtpStart.Value, dtpEnd.Value))
            {
                lblStatus.Text = "Offer created. It appears on the customer's Offers screen from " +
                                 dtpStart.Value.ToString("dd MMM") + " to " + dtpEnd.Value.ToString("dd MMM yyyy") + ".";
                ClearEditor();
                LoadGrid();
            }
            else
            {
                MessageBox.Show("That medicine does not belong to your pharmacy.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0 || !ValidateAll()) return;

            decimal percent = decimal.Parse(txtPercent.Text);

            if (_offers.Update(_selectedOfferId, UserSession.PharmacyId, txtOfferTitle.Text,
                               percent, dtpStart.Value, dtpEnd.Value))
            {
                lblStatus.Text = "Offer saved.";
                LoadGrid();
            }
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;
            _offers.SetActive(_selectedOfferId, UserSession.PharmacyId, false);
            lblStatus.Text = "Offer paused. The row is kept, so it can be switched back on at any time.";
            LoadGrid();
        }

        private void btnResume_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;
            _offers.SetActive(_selectedOfferId, UserSession.PharmacyId, true);
            lblStatus.Text = "Offer running again.";
            LoadGrid();
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;

            DialogResult answer = MessageBox.Show(
                "Delete this offer permanently?\r\n\r\n" +
                "Orders already placed keep the price they were sold at, because OrderItems stores its own UnitPrice.",
                "Delete offer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            _offers.Delete(_selectedOfferId, UserSession.PharmacyId);
            lblStatus.Text = "Offer deleted.";
            ClearEditor();
            LoadGrid();
        }

        private void btnClearEditor_Click(object sender, EventArgs e) => ClearEditor();

        private void ClearEditor()
        {
            _loading = true;
            _selectedOfferId = 0;
            cmbMedicine.SelectedIndex = 0;
            txtOfferTitle.Clear();
            txtPercent.Clear();
            dtpStart.Value = DateTime.Today;
            dtpEnd.Value = DateTime.Today.AddDays(14);
            dgvOffers.ClearSelection();
            _loading = false;
            ValidateAll();
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
