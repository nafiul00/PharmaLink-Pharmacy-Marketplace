using System.Drawing;               // Color and Font, used only by the theming pass
using System.Windows.Forms;         // Form, Label, Control, MessageBox and DialogResult
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // Medicine, the typed object a row is built into
using PharmaLinkApp.Services;       // MedicineService and CategoryService, the only classes that reach SQL

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation, modal dialog. Opened by AdminMedicineForm (Add and
    //  Edit) and by AdminInventoryForm (Restock). Uses MedicineService and
    //  CategoryService.
    //
    //  The constructor takes (medicineId, focusStock). A medicineId of 0 means
    //  add, any other value means edit, and focusStock is true when the Restock
    //  button opened the dialog so the cursor lands in the stock box.
    //
    //  Load order:
    //      MedicineEditorForm_Load -> ApplyTheme -> LoadCategories
    //                              -> LoadExisting (only when editing)
    //
    //  LoadCategories binds the ComboBox with DataSource plus DisplayMember
    //  "CategoryName" and ValueMember "CategoryId", which is why SelectedValue
    //  hands back the id directly when saving.
    //
    //  LoadExisting calls GetForEdit(medicineId, PharmacyId). A null result means
    //  the row belongs to a different pharmacy: the id on its own is not enough
    //  to load a record.
    //  Save builds a Medicine object and calls Insert or Update, passing
    //  PharmacyId from the session rather than from any control on the form.
    // -------------------------------------------------------------------------

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
        // One service instance per dialog. Neither holds a connection of its own:
        // DbHelper opens and closes one inside every call, so keeping these for the life
        // of the form costs nothing and there is nothing to dispose when it closes.
        private readonly MedicineService _medicines = new MedicineService();
        private readonly CategoryService _categories = new CategoryService();

        // THE MODE SWITCH. One dialog serves both Add and Edit, and this single field is
        // how it knows which it is. Zero means add, any other value means edit that row.
        // It is readonly and set in the constructor, so the mode is fixed the moment the
        // dialog is created and cannot drift half way through: there is no way for a
        // form that opened as Add to save as an Update, or the reverse.
        private readonly int _medicineId;      // 0 means "add a new one"
        private readonly bool _focusStock;     // opened from the Restock button

        // True while the Load handler is filling the controls. Every field on this form
        // raises Field_Changed when its value is assigned, and without this guard the
        // fill would run ValidateAll a dozen times and paint red labels over boxes the
        // user has not even seen yet. It is cleared once, after the fill, and then a
        // single deliberate ValidateAll produces the real starting state.
        private bool _loading = true;

        public MedicineEditorForm(int medicineId, bool focusStock)
        {
            InitializeComponent();       // builds the controls from the Designer file first
            // The two arguments are stored and nothing else happens here. Database work is
            // deliberately left to the Load event: a constructor that throws leaves a
            // half built form with no window to show the error in.
            _medicineId = medicineId;
            _focusStock = focusStock;
        }

        private void MedicineEditorForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours and fonts only, no data
            // Categories must be bound BEFORE LoadExisting runs, because setting
            // cmbCategory.SelectedValue only works once the list it has to search is there.
            LoadCategories();

            // The mode decides what the dialog opens with. Edit pulls the stored row;
            // Add fills in sensible starting values instead, so the owner is not forced to
            // type numbers that are almost always the same.
            if (_medicineId > 0) LoadExisting();
            else
            {
                lblTitle.Text = "Add Medicine";                 // the heading is the visible half of the mode
                dtpExpiry.Value = DateTime.Today.AddYears(2);   // a typical shelf life, and safely in the future
                txtStock.Text = "0";                            // a new line starts empty and is restocked after
                txtMinStock.Text = "10";                        // matches DF_Medicines_MinStock, the column default
            }

            // The fill is over, so changes from here on are the user's and must revalidate.
            _loading = false;

            // Run the rules once now. This is what leaves Save disabled on a blank Add
            // form, rather than enabled until the first keystroke proves otherwise.
            ValidateAll();

            if (_focusStock)
            {
                // Opened from Restock, so the one field the owner came to change gets the
                // cursor. SelectAll as well, so typing replaces the current number instead
                // of appending to it and turning 5 into 53.
                txtStock.Focus();
                txtStock.SelectAll();
            }
        }

        // Colours and fonts only. Nothing here reads or writes data, so it can be
        // re-read as pure presentation.
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
            // GetActiveList returns only rows with IsActive = 1, so a category the Super
            // Admin has retired cannot be chosen for a new medicine. Medicines already
            // pointing at it keep working, because deactivating never deletes the row.
            cmbCategory.DataSource = _categories.GetActiveList();

            // DisplayMember is what the user reads, ValueMember is what the code stores.
            // Binding both is what lets Save read cmbCategory.SelectedValue and get the
            // CategoryId straight out, with no lookup by name and no string parsing.
            cmbCategory.DisplayMember = "CategoryName";
            cmbCategory.ValueMember = "CategoryId";
        }

        private void LoadExisting()
        {
            // BOTH ids go to the query. The medicine id says which row, and PharmacyId
            // says it must belong to this shop, so an id typed or guessed from elsewhere
            // simply matches nothing rather than opening another owner's record.
            Medicine medicine = _medicines.GetForEdit(_medicineId, UserSession.PharmacyId);

            if (medicine == null)
            {
                // The WHERE clause carried PharmacyId, so this only happens when
                // the row belongs to a different pharmacy.
                MessageBox.Show("That medicine does not belong to your pharmacy.",
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // Close as Cancel, not OK, so the caller does not refresh its grid and
                // report a save that never happened.
                DialogResult = DialogResult.Cancel;
                Close();
                return;     // nothing below this point is safe to run with a null row
            }

            lblTitle.Text = "Edit Medicine";        // the other half of the mode, now visible

            // Straight copies from the row into the boxes. _loading is still true at this
            // point, so none of these assignments triggers a validation pass.
            txtName.Text = medicine.MedicineName;
            txtGeneric.Text = medicine.GenericName;
            txtManufacturer.Text = medicine.Manufacturer;
            txtStrength.Text = medicine.Strength;

            // Formatted to two decimals so the box shows 45.00 rather than 45, which is
            // what the column stores and what the invoice will print.
            txtUnitPrice.Text = medicine.UnitPrice.ToString("0.00");
            txtStock.Text = medicine.Stock.ToString();
            txtMinStock.Text = medicine.MinStock.ToString();
            chkRequiresRx.Checked = medicine.RequiresRx;
            txtDescription.Text = medicine.Description;

            // A stored date that has already passed would fail the future date rule the
            // moment the dialog opened, leaving Save disabled on a row the owner may only
            // have come to restock. Clamping to tomorrow gives a usable starting point and
            // still forces a deliberate choice, and nothing is written until Save is pressed.
            dtpExpiry.Value = medicine.ExpiryDate > DateTime.Today
                ? medicine.ExpiryDate
                : DateTime.Today.AddDays(1);

            // Assigned last, and it works because LoadCategories has already bound the list
            // and named CategoryId as the ValueMember. Setting it before the binding would
            // silently do nothing and the dialog would open on the wrong category.
            cmbCategory.SelectedValue = medicine.CategoryId;
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        // Every input control on the form is wired to this one handler in the Designer,
        // so there is a single entry point for "something changed" rather than a dozen
        // near identical handlers that could drift apart.
        private void Field_Changed(object sender, EventArgs e) => ValidateAll();

        private bool ValidateAll()
        {
            // The Load handler is still filling controls, so there is nothing to judge yet.
            // Returning false rather than true is the safe direction: a caller that ignored
            // the guard would be told "not valid" instead of being waved through.
            if (_loading) return false;

            bool ok = true;

            // Note &= and not &&. The compound assignment evaluates the right hand side
            // every time, so EVERY rule runs and every failing field gets its red label on
            // the same pass. With && the first failure would short circuit the rest and the
            // user would fix one field only to discover the next, one at a time.
            ok &= Check(!Validator.IsBlank(txtName.Text), lblNameError, txtName,
                        "The brand name cannot be empty.");

            // The UNIQUE constraint is (PharmacyId, MedicineName, Strength).
            // Guarded by the blank test so an empty box reports "cannot be empty" rather
            // than running a pointless query for the empty string.
            if (!Validator.IsBlank(txtName.Text) &&
                // _medicineId is passed as the id to IGNORE. When editing, the row's own
                // name and strength must not count as a duplicate of itself, or Save would
                // be blocked on a record that changed nothing. Adding passes 0, which
                // matches no row, so every existing name counts.
                _medicines.NameExistsInPharmacy(UserSession.PharmacyId, txtName.Text, txtStrength.Text, _medicineId))
            {
                // Written out rather than routed through Check, because the message has to
                // quote what the user typed and Check takes a fixed string.
                UiTheme.ShowError(lblNameError, txtName,
                    "You already list " + txtName.Text.Trim() + " " + txtStrength.Text.Trim() +
                    " (UQ_Medicines_PerShop allows one brand and strength per pharmacy).");
                ok = false;     // set directly, since this branch bypassed the &= pattern
            }

            ok &= Check(!Validator.IsBlank(txtGeneric.Text), lblGenericError, txtGeneric,
                        "The generic name is what makes the medicine searchable, so it is required.");

            ok &= Check(!Validator.IsBlank(txtManufacturer.Text), lblManufacturerError, txtManufacturer,
                        "Enter the manufacturer, for example Beximco, Square or Renata.");

            // The field argument is null because a DateTimePicker has no white background
            // to tint red. The message still appears under it, which is the part that
            // matters. The database has no CHECK for this one, so unlike the three rules
            // below it is enforced here alone.
            ok &= Check(Validator.IsFutureDate(dtpExpiry.Value), lblExpiryError, null,
                        "The expiry date must be in the future - expired stock is never offered to customers.");

            // The next three are the deliberately doubled rules. Each one is checked here,
            // in front of the user, AND again by a CHECK constraint on the Medicines table.
            // The form check exists so a mistake becomes a red label under the box instead
            // of an exception; the constraint exists because the form can be bypassed and
            // the table is the last line that cannot be. The numbers are copied from the
            // constraints on purpose, so the two can never disagree about what is legal.
            //
            // The out values are collected only because TryParse requires somewhere to put
            // them. They are discarded here and Save parses the same text again, which is
            // safe precisely because this pass has already proved that it parses.
            decimal price;
            ok &= Check(Validator.IsPositiveDecimal(txtUnitPrice.Text, out price), lblUnitPriceError, txtUnitPrice,
                        "The unit price must be a number greater than zero (CK_Medicines_Price).");

            int stock;
            ok &= Check(Validator.IsNonNegativeInt(txtStock.Text, out stock), lblStockError, txtStock,
                        "Stock must be a whole number of zero or more (CK_Medicines_Stock).");

            int minStock;
            ok &= Check(Validator.IsNonNegativeInt(txtMinStock.Text, out minStock), lblMinStockError, txtMinStock,
                        "Minimum stock must be a whole number of zero or more (CK_Medicines_MinStock).");

            // No error label for the category: the list is bound from the database and is
            // never empty in practice, so the only job here is to keep Save disabled in the
            // impossible case. CategoryId is a foreign key, so a missing choice would be
            // refused by FK_Medicines_Category anyway.
            ok &= cmbCategory.SelectedItem != null;

            // Disabling the button is the real enforcement: an invalid form cannot be
            // submitted at all, rather than being submitted and then rejected.
            btnSave.Enabled = ok;

            // Greying the colour as well, because a disabled button that still looks
            // primary green reads as a broken button rather than a blocked one.
            btnSave.BackColor = ok ? UiTheme.Primary : Color.FromArgb(170, 190, 184);
            return ok;
        }

        // One helper for every rule, so showing and clearing an error is written once.
        // It returns the verdict it was given, which is what lets the caller write
        // "ok &= Check(...)" and get the painting and the accumulation in one line.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Clearing on success matters as much as showing on failure: without it a
            // message from an earlier keystroke would sit under a field that is now correct.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------

        private void btnSave_Click(object sender, EventArgs e)
        {
            // Revalidated even though the button is only enabled when valid. A keyboard
            // shortcut or a default button can fire a click the enabled state did not
            // anticipate, and this line costs nothing next to a bad row.
            if (!ValidateAll()) return;

            try
            {
                // One object built from the controls, then handed to the service. The form
                // never writes SQL itself: it does not know the table name or the column
                // list, which is what keeps the two layers separable.
                Medicine medicine = new Medicine
                {
                    // On Add this is 0 and Insert ignores it, because MedicineId is an
                    // IDENTITY column the database assigns. On Edit it is the real id and
                    // becomes the WHERE clause of the UPDATE.
                    MedicineId = _medicineId,

                    // SelectedValue is the CategoryId because ValueMember was bound to it.
                    // Convert.ToInt32 rather than a cast, since the bound value arrives
                    // boxed as an object.
                    CategoryId = Convert.ToInt32(cmbCategory.SelectedValue),

                    // The text goes across untrimmed: the service trims each value before
                    // it reaches the parameter, so the trimming rule lives in one place
                    // rather than being repeated on every form that writes a medicine.
                    MedicineName = txtName.Text,
                    GenericName = txtGeneric.Text,
                    Manufacturer = txtManufacturer.Text,
                    Strength = txtStrength.Text,

                    // Parse, not TryParse, and deliberately so: ValidateAll has already
                    // proved all three of these parse, so a throw here would mean the
                    // validation and the save had drifted apart, which is worth knowing
                    // about rather than swallowing.
                    UnitPrice = decimal.Parse(txtUnitPrice.Text),
                    Stock = int.Parse(txtStock.Text),
                    MinStock = int.Parse(txtMinStock.Text),

                    RequiresRx = chkRequiresRx.Checked,     // drives the prescription prompt at checkout

                    // .Date strips the time part the picker carries, because ExpiryDate is
                    // a DATE column and a time of day would be thrown away by SQL Server
                    // anyway. Dropping it here keeps the value the same on both sides.
                    ExpiryDate = dtpExpiry.Value.Date,

                    Description = txtDescription.Text
                };

                // THE MODE DECIDES THE STATEMENT. This is the whole difference between the
                // two jobs the dialog does: everything above was identical, and only these
                // two branches differ. The test is on the id the constructor was given, not
                // on anything the user could have altered while the dialog was open.
                if (_medicineId == 0)
                {
                    // PharmacyId comes from the session, never from the form, so
                    // an owner cannot create a medicine under someone else's shop.
                    _medicines.Insert(medicine, UserSession.PharmacyId);
                }
                else
                {
                    // Update carries the same PharmacyId into its WHERE clause, so editing
                    // works only on rows this shop owns. An id belonging to another
                    // pharmacy matches nothing and changes nothing.
                    _medicines.Update(medicine, UserSession.PharmacyId);
                }

                // OK is what tells the calling form the grid is now out of date and should
                // be reloaded. Setting it also closes a modal dialog, but Close is called
                // explicitly so the intent is readable rather than a side effect.
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                // The catch is here because the CHECK and UNIQUE constraints are the real
                // guarantee and can still refuse a row this form thought was fine. Showing
                // the message keeps the dialog open with the typed values intact, so the
                // owner can correct one field rather than start again.
                MessageBox.Show("The medicine could not be saved.\r\n\r\n" + ex.Message,
                    "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            // Cancel writes nothing and says so, so the caller leaves its grid alone.
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
