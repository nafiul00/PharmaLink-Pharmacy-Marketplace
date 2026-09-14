using System.Drawing;               // Color and Font, used only by the theming pass
using System.Windows.Forms;         // Form, Label, Control, MessageBox and DialogResult
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // Medicine, the typed object a row is built into
using PharmaLinkApp.Services;       // MedicineService and CategoryService, the only SQL callers

namespace PharmaLinkApp.Forms       // the presentation namespace; nothing here writes SQL
{
    // A modal dialog. medicineId 0 means add, anything else means edit that row.

    /// <summary>The Add / Edit Medicine dialog, validated as it is typed.</summary>
    public partial class MedicineEditorForm : Form
    {
        private readonly MedicineService _medicines = new MedicineService();     // DbHelper owns the connection, so nothing to dispose
        private readonly CategoryService _categories = new CategoryService();    // a second service: categories are another table

        private readonly int _medicineId;      // 0 means "add a new one"; readonly, so the mode cannot drift
        private readonly bool _focusStock;     // opened from the Restock button

        // True while Load fills the controls, so the fill does not revalidate each time.
        private bool _loading = true;

        // The only constructor, so the dialog cannot exist without stating its mode.
        public MedicineEditorForm(int medicineId, bool focusStock)
        {
            InitializeComponent();       // builds the controls from the Designer file first
            _medicineId = medicineId;    // stored and nothing else: database work waits for Load
            _focusStock = focusStock;    // the last chance to assign either field, since both are readonly
        }

        private void MedicineEditorForm_Load(object sender, EventArgs e)   // the window exists, so errors have somewhere to show
        {
            ApplyTheme();       // colours and fonts only, no data
            LoadCategories();   // must run before LoadExisting, or SelectedValue finds no list to search

            if (_medicineId > 0) LoadExisting();   // Edit pulls the stored row
            else   // the Add branch: no row to read, so these defaults stand in for one
            {
                lblTitle.Text = "Add Medicine";                 // the heading is the visible half of the mode
                dtpExpiry.Value = DateTime.Today.AddYears(2);   // a typical shelf life, safely in the future
                txtStock.Text = "0";                            // a new line starts empty and is restocked after
                txtMinStock.Text = "10";                        // matches DF_Medicines_MinStock, the column default
            }

            _loading = false;   // the fill is over, so changes from here are the user's

            ValidateAll();   // run once, so Save opens disabled on a blank Add form

            if (_focusStock)   // set only when AdminInventoryForm's Restock button opened this
            {
                txtStock.Focus();       // the one field the owner came here to change
                txtStock.SelectAll();   // so typing replaces the number instead of turning 5 into 53
            }
        }

        // Colours and fonts only, so this method can be read as pure presentation.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Medicine");                      // shared window chrome, set in one place
            StartPosition = FormStartPosition.CenterParent;           // a modal dialog belongs over its caller

            panelHeader.BackColor = UiTheme.Primary;                  // the brand green band on every screen
            lblTitle.Font = UiTheme.FontTitle;                        // the "Add" or "Edit Medicine" heading
            lblTitle.ForeColor = Color.White;                         // the only colour with contrast on that band
            lblSubtitle.Font = UiTheme.FontSmall;                     // a size down, so the two lines pair up
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);    // a pale tint: visible but subordinate

            foreach (Control control in Controls)   // walked, rather than naming all seven error labels
            {
                if (control is Label label && label.Name.EndsWith("Error"))   // the naming convention is the selector
                {
                    label.Font = UiTheme.FontSmall;      // small, so an error never pushes the layout around
                    label.ForeColor = UiTheme.Danger;    // one red from the palette, used on every screen
                }
            }

            lblConstraintNote.Font = UiTheme.FontSmall;          // the standing note naming the CHECK constraints
            lblConstraintNote.ForeColor = UiTheme.TextMuted;     // muted, because it is not itself an error

            UiTheme.StylePrimary(btnSave);                                        // green: the one action that writes
            btnSave.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);    // after StylePrimary, so Save is heavier
            UiTheme.StyleSecondary(btnCancel);                                    // grey, because cancelling writes nothing
        }

        private void LoadCategories()   // runs before LoadExisting, and the order is load-bearing
        {
            // Active rows only, so a retired category cannot be chosen for a new medicine.
            cmbCategory.DataSource = _categories.GetActiveList();

            cmbCategory.DisplayMember = "CategoryName";   // what the user reads
            cmbCategory.ValueMember = "CategoryId";       // what makes SelectedValue an id, not a name
        }

        private void LoadExisting()   // reached only when _medicineId is non-zero: the Edit half
        {
            // BOTH ids go to the query, so a guessed id cannot open another shop's record.
            Medicine medicine = _medicines.GetForEdit(_medicineId, UserSession.PharmacyId);

            if (medicine == null)   // the service's way of saying no row matched BOTH ids
            {
                // The WHERE carried PharmacyId, so the row belongs to a different pharmacy.
                MessageBox.Show("That medicine does not belong to your pharmacy.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // a warning: refusing is correct behaviour

                DialogResult = DialogResult.Cancel;   // Cancel, so the caller does not refresh on a save that never happened
                Close();    // shuts the dialog and hands control back to the form that opened it
                return;     // nothing below is safe to run with a null row
            }

            lblTitle.Text = "Edit Medicine";        // the other half of the mode, now visible

            // Straight copies; _loading is still true, so none of these triggers validation.
            txtName.Text = medicine.MedicineName;
            txtGeneric.Text = medicine.GenericName;           // the searchable name customers type
            txtManufacturer.Text = medicine.Manufacturer;     // free text, copied back exactly as stored
            txtStrength.Text = medicine.Strength;             // part of UQ_Medicines_PerShop

            txtUnitPrice.Text = medicine.UnitPrice.ToString("0.00");   // 45.00, which is what the column stores
            txtStock.Text = medicine.Stock.ToString();           // an int, so no decimals on a unit count
            txtMinStock.Text = medicine.MinStock.ToString();     // the threshold the low-stock report uses
            chkRequiresRx.Checked = medicine.RequiresRx;         // a bool straight into the tick box
            txtDescription.Text = medicine.Description;          // nullable, and the model turned NULL into ""

            // A stored date already past would fail the future rule and lock Save on open.
            dtpExpiry.Value = medicine.ExpiryDate > DateTime.Today
                ? medicine.ExpiryDate            // still in the future, so show what is stored
                : DateTime.Today.AddDays(1);     // expired, so clamp to the earliest date allowed

            // Assigned last, because it only works once LoadCategories has bound the list.
            cmbCategory.SelectedValue = medicine.CategoryId;
        }

        // ------------------------------ VALIDATION ---------------------------

        // Every input control is wired here in the Designer, so there is one entry point.
        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        private bool ValidateAll()   // returns the verdict and repaints in the same pass
        {
            if (_loading) return false;   // still filling, and false is the safe direction

            bool ok = true;   // starts optimistic and is only driven down by the rules below

            // &= not &&, so EVERY rule runs and every failing field is painted on one pass.
            ok &= Check(!Validator.IsBlank(txtName.Text), lblNameError, txtName,
                        "The brand name cannot be empty.");   // IsBlank covers whitespace too

            // The UNIQUE constraint is (PharmacyId, MedicineName, Strength).
            if (!Validator.IsBlank(txtName.Text) &&                                                              // guarded, so an empty box reports blank instead
                _medicines.NameExistsInPharmacy(UserSession.PharmacyId, txtName.Text, txtStrength.Text, _medicineId))   // _medicineId is the id to IGNORE, so a row is not its own duplicate
            {
                // Written out rather than routed through Check, which takes a fixed string.
                UiTheme.ShowError(lblNameError, txtName,
                    "You already list " + txtName.Text.Trim() + " " + txtStrength.Text.Trim() +   // Trim is cosmetic here, it does not change the test
                    " (UQ_Medicines_PerShop allows one brand and strength per pharmacy).");       // naming the constraint shows this is a rule
                ok = false;     // set directly, since this branch bypassed the &= pattern
            }

            ok &= Check(!Validator.IsBlank(txtGeneric.Text), lblGenericError, txtGeneric,                    // required, because search runs on it
                        "The generic name is what makes the medicine searchable, so it is required.");       // explains the consequence, not the rule

            ok &= Check(!Validator.IsBlank(txtManufacturer.Text), lblManufacturerError, txtManufacturer,     // no CHECK behind this one; the form is the only gate
                        "Enter the manufacturer, for example Beximco, Square or Renata.");                   // gives the shape wanted, not just "required"

            // field is null because a DateTimePicker has no white background to tint red.
            ok &= Check(Validator.IsFutureDate(dtpExpiry.Value), lblExpiryError, null,                               // the database has no CHECK for this, so the form alone enforces it
                        "The expiry date must be in the future - expired stock is never offered to customers.");     // says why, since the rule protects the customer

            // The next three are checked here AND again by a CHECK constraint on the table.
            decimal price;   // collected only because TryParse needs somewhere to put it
            ok &= Check(Validator.IsPositiveDecimal(txtUnitPrice.Text, out price), lblUnitPriceError, txtUnitPrice,   // positive, not merely numeric
                        "The unit price must be a number greater than zero (CK_Medicines_Price).");                   // named, so it matches what the database would say

            int stock;   // declared per rule, so each one owns its own out variable
            ok &= Check(Validator.IsNonNegativeInt(txtStock.Text, out stock), lblStockError, txtStock,   // zero IS allowed: a listing can be out of stock
                        "Stock must be a whole number of zero or more (CK_Medicines_Stock).");           // whole, because half a box cannot be dispensed

            int minStock;   // the third out variable, discarded like the other two
            ok &= Check(Validator.IsNonNegativeInt(txtMinStock.Text, out minStock), lblMinStockError, txtMinStock,   // the same rule as Stock, since the two are compared
                        "Minimum stock must be a whole number of zero or more (CK_Medicines_MinStock).");            // zero means "never warn me about this line"

            // No error label: the list comes from the database and is never empty in practice.
            ok &= cmbCategory.SelectedItem != null;

            btnSave.Enabled = ok;   // the real enforcement: an invalid form cannot be submitted

            // Greyed as well, so a disabled green button does not read as a broken one.
            btnSave.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;   // handed back, so btnSave_Click can re-run the pass before writing
        }

        // One helper per rule, so "ok &= Check(...)" paints and accumulates in one line.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            if (rulePassed) UiTheme.ClearError(errorLabel, field);   // clearing matters as much as showing
            else UiTheme.ShowError(errorLabel, field, message);      // tolerates a null field, as the expiry rule needs
            return rulePassed;   // returned unchanged, so this helper only paints
        }

        // ---------------------------------------------------------------------

        private void btnSave_Click(object sender, EventArgs e)   // the only handler here that writes
        {
            if (!ValidateAll()) return;   // a shortcut can fire a click the enabled state did not expect

            try   // the constraints can still refuse a row, so the dialog survives a refusal
            {
                // One object built from the controls; the form knows no table or column names.
                Medicine medicine = new Medicine
                {
                    // 0 on Add, which Insert ignores because MedicineId is an IDENTITY column.
                    MedicineId = _medicineId,

                    // SelectedValue is the CategoryId, and it arrives boxed, so Convert not a cast.
                    CategoryId = Convert.ToInt32(cmbCategory.SelectedValue),

                    // Untrimmed on purpose: the service trims, so the rule lives in one place.
                    MedicineName = txtName.Text,
                    GenericName = txtGeneric.Text,           // untrimmed for the same reason
                    Manufacturer = txtManufacturer.Text,     // likewise, so the service is the only trimmer
                    Strength = txtStrength.Text,             // part of the UNIQUE key, so its trim decides duplicates

                    // Parse, not TryParse: ValidateAll has already proved all three parse.
                    UnitPrice = decimal.Parse(txtUnitPrice.Text),
                    Stock = int.Parse(txtStock.Text),         // int, because the column counts whole units
                    MinStock = int.Parse(txtMinStock.Text),   // the third value already proved to parse

                    RequiresRx = chkRequiresRx.Checked,     // drives the prescription prompt at checkout

                    // .Date strips the picker's time, which a DATE column would drop anyway.
                    ExpiryDate = dtpExpiry.Value.Date,

                    Description = txtDescription.Text   // optional; an empty box becomes "", not NULL
                };

                // The mode decides the statement, and it is read from the constructor's id.
                if (_medicineId == 0)
                {
                    _medicines.Insert(medicine, UserSession.PharmacyId);   // PharmacyId from the session, never the form
                }
                else   // non-zero, so the constructor was handed a real row and this is an edit
                {
                    _medicines.Update(medicine, UserSession.PharmacyId);   // the same id goes into the WHERE clause
                }

                DialogResult = DialogResult.OK;   // tells the calling form its grid is out of date
                Close();   // explicit, so the reader need not know DialogResult closes a modal
            }
            catch (Exception ex)   // ex is shown, so the database's own message reaches the owner
            {
                // The dialog stays open with the typed values, so one field can be corrected.
                MessageBox.Show("The medicine could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);   // an error icon: this time something failed
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)   // the no-op exit, separate from Save
        {
            DialogResult = DialogResult.Cancel;   // writes nothing and says so, so the caller leaves its grid alone
            Close();   // the same explicit close as the success path, so both exits read alike
        }
    }
}
