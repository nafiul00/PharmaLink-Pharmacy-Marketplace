using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Models;
using PharmaLinkApp.Services;

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// The Add / Edit Medicine modal dialog (requirement 12).
    ///
    /// This is the form the navigation diagram draws with a dashed border. Every
    /// field is validated as it is typed: a failing rule paints the field red,
    /// names the CHECK constraint that would reject the value at the database as
    /// well, and keeps the Save button disabled.
    /// </summary>
    public partial class MedicineEditorForm : Form
    {
        private readonly MedicineService _medicines = new MedicineService();
        private readonly CategoryService _categories = new CategoryService();

        private readonly int _medicineId;      // 0 means "add a new one"
        private readonly bool _focusStock;     // opened from the Restock button
        private bool _loading = true;

        public MedicineEditorForm(int medicineId, bool focusStock)
        {
            InitializeComponent();
            _medicineId = medicineId;
            _focusStock = focusStock;
        }

        private void MedicineEditorForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadCategories();

            if (_medicineId > 0) LoadExisting();
            else
            {
                lblTitle.Text = "Add Medicine";
                dtpExpiry.Value = DateTime.Today.AddYears(2);
                txtStock.Text = "0";
                txtMinStock.Text = "10";
            }

            _loading = false;
            ValidateAll();

            if (_focusStock)
            {
                txtStock.Focus();
                txtStock.SelectAll();
            }
        }

        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Medicine");
            StartPosition = FormStartPosition.CenterParent;

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            foreach (Control control in Controls)
            {
                if (control is Label label && label.Name.EndsWith("Error"))
                {
                    label.Font = UiTheme.FontSmall;
                    label.ForeColor = UiTheme.Danger;
                }
            }

            lblConstraintNote.Font = UiTheme.FontSmall;
            lblConstraintNote.ForeColor = UiTheme.TextMuted;

            UiTheme.StylePrimary(btnSave);
            btnSave.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            UiTheme.StyleSecondary(btnCancel);
        }

        private void LoadCategories()
        {
            cmbCategory.DataSource = _categories.GetActiveList();
            cmbCategory.DisplayMember = "CategoryName";
            cmbCategory.ValueMember = "CategoryId";
        }

        private void LoadExisting()
        {
            Medicine medicine = _medicines.GetForEdit(_medicineId, UserSession.PharmacyId);

            if (medicine == null)
            {
                // The WHERE clause carried PharmacyId, so this only happens when
                // the row belongs to a different pharmacy.
                MessageBox.Show("That medicine does not belong to your pharmacy.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            lblTitle.Text = "Edit Medicine";
            txtName.Text = medicine.MedicineName;
            txtGeneric.Text = medicine.GenericName;
            txtManufacturer.Text = medicine.Manufacturer;
            txtStrength.Text = medicine.Strength;
            txtUnitPrice.Text = medicine.UnitPrice.ToString("0.00");
            txtStock.Text = medicine.Stock.ToString();
            txtMinStock.Text = medicine.MinStock.ToString();
            chkRequiresRx.Checked = medicine.RequiresRx;
            txtDescription.Text = medicine.Description;

            dtpExpiry.Value = medicine.ExpiryDate > DateTime.Today
                ? medicine.ExpiryDate
                : DateTime.Today.AddDays(1);

            cmbCategory.SelectedValue = medicine.CategoryId;
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        private bool ValidateAll()
        {
            if (_loading) return false;

            bool ok = true;

            ok &= Check(!Validator.IsBlank(txtName.Text), lblNameError, txtName,
                        "The brand name cannot be empty.");

            // The UNIQUE constraint is (PharmacyId, MedicineName, Strength).
            if (!Validator.IsBlank(txtName.Text) &&
                _medicines.NameExistsInPharmacy(UserSession.PharmacyId, txtName.Text, txtStrength.Text, _medicineId))
            {
                UiTheme.ShowError(lblNameError, txtName,
                    "You already list " + txtName.Text.Trim() + " " + txtStrength.Text.Trim() +
                    " (UQ_Medicines_PerShop allows one brand and strength per pharmacy).");
                ok = false;
            }

            ok &= Check(!Validator.IsBlank(txtGeneric.Text), lblGenericError, txtGeneric,
                        "The generic name is what makes the medicine searchable, so it is required.");

            ok &= Check(!Validator.IsBlank(txtManufacturer.Text), lblManufacturerError, txtManufacturer,
                        "Enter the manufacturer, for example Beximco, Square or Renata.");

            ok &= Check(Validator.IsFutureDate(dtpExpiry.Value), lblExpiryError, null,
                        "The expiry date must be in the future - expired stock is never offered to customers.");

            decimal price;
            ok &= Check(Validator.IsPositiveDecimal(txtUnitPrice.Text, out price), lblUnitPriceError, txtUnitPrice,
                        "The unit price must be a number greater than zero (CK_Medicines_Price).");

            int stock;
            ok &= Check(Validator.IsNonNegativeInt(txtStock.Text, out stock), lblStockError, txtStock,
                        "Stock must be a whole number of zero or more (CK_Medicines_Stock).");

            int minStock;
            ok &= Check(Validator.IsNonNegativeInt(txtMinStock.Text, out minStock), lblMinStockError, txtMinStock,
                        "Minimum stock must be a whole number of zero or more (CK_Medicines_MinStock).");

            ok &= cmbCategory.SelectedItem != null;

            btnSave.Enabled = ok;
            btnSave.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateAll()) return;

            try
            {
                Medicine medicine = new Medicine
                {
                    MedicineId = _medicineId,
                    CategoryId = Convert.ToInt32(cmbCategory.SelectedValue),
                    MedicineName = txtName.Text,
                    GenericName = txtGeneric.Text,
                    Manufacturer = txtManufacturer.Text,
                    Strength = txtStrength.Text,
                    UnitPrice = decimal.Parse(txtUnitPrice.Text),
                    Stock = int.Parse(txtStock.Text),
                    MinStock = int.Parse(txtMinStock.Text),
                    RequiresRx = chkRequiresRx.Checked,
                    ExpiryDate = dtpExpiry.Value.Date,
                    Description = txtDescription.Text
                };

                if (_medicineId == 0)
                {
                    // PharmacyId comes from the session, never from the form, so
                    // an owner cannot create a medicine under someone else's shop.
                    _medicines.Insert(medicine, UserSession.PharmacyId);
                }
                else
                {
                    _medicines.Update(medicine, UserSession.PharmacyId);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("The medicine could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
