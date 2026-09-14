using System.Data;                  // DataTable and DataGridViewRow, the shape a read returns
using System.Drawing;               // Color and Font, used by the theming and the row colouring
using System.Windows.Forms;         // Form, Label, Control, DataGridView and MessageBox
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // Medicine, used for the live price preview
using PharmaLinkApp.Services;       // OfferService and MedicineService, the only classes that reach SQL

namespace PharmaLinkApp.Forms
{
    // -------------------------------------------------------------------------
    //  Layer: presentation.  Opened by AdminDashboard, and by AdminMedicineForm
    //  which passes a medicine id to preselect. Uses OfferService and
    //  MedicineService.
    //
    //  Load order:
    //      DiscountOffersForm_Load -> ApplyTheme -> LoadMedicines
    //                              -> set the default 14 day range -> LoadGrid
    //                              -> SelectMedicine -> ValidateAll
    //
    //  LoadGrid assigns OfferService.GetForPharmacy to dgvOffers.DataSource. The
    //  OfferState column is produced by a CASE expression in that query, not in
    //  C#, and CellFormatting colours each row from it.
    //
    //  Selecting a grid row copies its values into the editor fields. _loading is
    //  set to true while that happens so Field_Changed does not revalidate in the
    //  middle of the fill. Create and Update call OfferService, whose statements
    //  join Medicines and filter on PharmacyId.
    // -------------------------------------------------------------------------

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
        // One instance of each service for the life of the form. Neither holds a
        // connection: DbHelper opens and closes one inside every call.
        private readonly OfferService _offers = new OfferService();
        private readonly MedicineService _medicines = new MedicineService();

        // Set once by the constructor when AdminMedicineForm opens this screen with a
        // medicine already in mind, so the owner does not have to find it in the list
        // again. Zero means the screen was opened on its own with nothing preselected.
        private readonly int _preselectMedicineId;

        // The medicine list is kept in memory as well as in the ComboBox, because the
        // live price preview needs UnitPrice and the ComboBox only holds display text.
        // Reading it from this list costs nothing; re-querying on every keystroke would
        // put a round trip behind each character typed into the percent box.
        private List<Medicine> _medicineList = new List<Medicine>();

        // Which existing offer the grid is sitting on, or 0 when none is selected.
        // This is what decides whether the editor is creating a new offer or amending
        // the selected one, and it is what Update, Pause, Resume and Delete act on.
        private int _selectedOfferId;

        // True while code is filling the editor controls. Every field raises
        // Field_Changed on assignment, so without this guard copying a grid row into the
        // editor would run a validation pass per control and could paint errors over
        // half filled boxes. It is also true during Load for the same reason.
        private bool _loading = true;

        // The parameterless constructor is what the Designer and the dashboard use. It
        // chains to the real one with 0, so there is one initialisation path rather than
        // two that could drift apart.
        public DiscountOffersForm() : this(0) { }

        public DiscountOffersForm(int preselectMedicineId)
        {
            InitializeComponent();                      // build the controls first
            // Stored, not acted on. Database work waits for the Load event, because a
            // constructor that throws leaves no window in which to show the error.
            _preselectMedicineId = preselectMedicineId;
        }

        private void DiscountOffersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();           // colours and fonts only, no data
            LoadMedicines();        // fills the ComboBox and the in-memory list behind it

            // A fortnight is the usual length of a promotion, so the dates are prefilled
            // rather than left at today-to-today, which would be a valid but useless offer.
            // Setting them while _loading is still true keeps them from triggering a pass.
            dtpStart.Value = DateTime.Today;
            dtpEnd.Value = DateTime.Today.AddDays(14);

            // Cleared BEFORE LoadGrid, because LoadGrid returns immediately while it is
            // true. Everything above this line was form setup; everything below is real.
            _loading = false;
            LoadGrid();

            // Only when a medicine was named by the caller. SelectMedicine raises the
            // ComboBox's changed event, which is why it runs after _loading is false:
            // the resulting validation pass is wanted here.
            if (_preselectMedicineId > 0) SelectMedicine(_preselectMedicineId);

            // One deliberate pass so the Create and Update buttons start in the right
            // state rather than waiting for the first keystroke to settle them.
            ValidateAll();
        }

        // Colours, fonts and the grid styling. Nothing here reads or writes data.
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
            // The query behind this filters on PharmacyId and on IsActive = 1, so the list
            // can only ever contain this shop's live medicines. A discount on a delisted
            // item, or on another owner's item, cannot be started because it cannot be chosen.
            _medicineList = _medicines.GetSimpleListForPharmacy(UserSession.PharmacyId);

            // Cleared first, because this method also runs on a reload and adding to a
            // populated list would leave every medicine listed twice.
            cmbMedicine.Items.Clear();

            // Index 0 is a deliberate placeholder rather than a real medicine. Without it
            // the ComboBox would open with the first medicine already chosen and an owner
            // could create a discount on the wrong product by never touching the list.
            cmbMedicine.Items.Add("- choose one of your medicines -");

            foreach (Medicine medicine in _medicineList)
            {
                // The id is put at the FRONT of the text, before the first space, because
                // SelectedMedicineId reads it back from there. Two shops can list the same
                // brand and strength, so the visible name alone is not a reliable key.
                // The price is shown as well so the owner can judge the discount in context.
                cmbMedicine.Items.Add(medicine.MedicineId + " - " + medicine.MedicineName + " " +
                                      medicine.Strength + "  (Tk " + medicine.UnitPrice.ToString("N2") + ")");
            }

            // Land on the placeholder, which is the "nothing chosen" state the validation
            // treats as incomplete.
            cmbMedicine.SelectedIndex = 0;
        }

        private void SelectMedicine(int medicineId)
        {
            // Starts at 1, not 0, because index 0 is the placeholder and has no id to read.
            for (int i = 1; i < cmbMedicine.Items.Count; i++)
            {
                string text = cmbMedicine.Items[i].ToString();

                // Substring up to the first space is exactly the id LoadMedicines wrote.
                // Parse rather than TryParse is safe here because every entry past index 0
                // was built by that method and always begins with a number.
                if (int.Parse(text.Substring(0, text.IndexOf(' '))) == medicineId)
                {
                    cmbMedicine.SelectedIndex = i;
                    return;         // stop at the first match; ids are unique
                }
            }
            // Falling out of the loop is not an error: it means the preselected medicine is
            // not in this shop's live list, and the ComboBox simply stays on the placeholder.
        }

        private int SelectedMedicineId()
        {
            // Index 0 is the placeholder and -1 means nothing is selected at all. Both are
            // reported as 0, which the callers read as "no medicine chosen", so there is one
            // empty value to test for rather than two.
            if (cmbMedicine.SelectedIndex <= 0) return 0;

            // The same id-before-the-first-space format LoadMedicines wrote.
            string text = cmbMedicine.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));
        }

        // ---------------------------------------------------------------------

        private void LoadGrid()
        {
            // The Load handler is still setting default dates, and those assignments can
            // reach here through the change handlers. Reloading the grid mid-setup would be
            // wasted work and would fire SelectionChanged before the form is ready.
            if (_loading) return;

            try
            {
                // The query joins Medicines and filters on PharmacyId, so the grid can only
                // ever show this owner's offers. The isolation is in the SQL, not in a
                // filter applied to a wider result after it arrives.
                DataTable table = _offers.GetForPharmacy(UserSession.PharmacyId);

                // Binding the DataTable straight to the grid means the columns come from the
                // query. That is why the headers below are renamed rather than declared: the
                // grid's shape follows the SELECT list.
                dgvOffers.DataSource = table;

                // Guarded because an empty result still binds, but a failed bind would leave
                // no columns and every line below would throw on a missing column name.
                if (dgvOffers.Columns.Count > 0)
                {
                    // Header text only, so the grid reads as English rather than as column
                    // names. FillWeight is a proportion, not a pixel width, so the columns
                    // keep their relative sizes when the window is resized.
                    dgvOffers.Columns["OfferId"].HeaderText = "ID";
                    dgvOffers.Columns["OfferId"].FillWeight = 28;
                    dgvOffers.Columns["OfferTitle"].HeaderText = "Offer";
                    dgvOffers.Columns["OfferTitle"].FillWeight = 120;
                    dgvOffers.Columns["MedicineName"].HeaderText = "Medicine";
                    dgvOffers.Columns["Strength"].HeaderText = "Strength";
                    dgvOffers.Columns["Strength"].FillWeight = 45;

                    // Was and Now sit side by side so the discount can be read off the row
                    // without arithmetic. Both come from the query already calculated.
                    dgvOffers.Columns["OriginalPrice"].HeaderText = "Was (Tk)";
                    dgvOffers.Columns["DiscountPercent"].HeaderText = "Off %";
                    dgvOffers.Columns["DiscountPercent"].FillWeight = 38;
                    dgvOffers.Columns["DiscountedPrice"].HeaderText = "Now (Tk)";
                    dgvOffers.Columns["StartDate"].HeaderText = "From";
                    dgvOffers.Columns["EndDate"].HeaderText = "Until";

                    // Hidden rather than dropped from the SELECT. UpdateGridButtons reads
                    // IsActive from the row to decide between Pause and Resume, so the value
                    // has to be present even though the State column says it better.
                    dgvOffers.Columns["IsActive"].Visible = false;

                    dgvOffers.Columns["OfferState"].HeaderText = "State";
                    dgvOffers.Columns["OfferState"].FillWeight = 52;
                }

                // Two different counts on purpose. Rows.Count is every offer ever created
                // here, including paused, scheduled and expired ones; the second number is
                // how many are actually discounting a price today, which is the one that
                // affects customers. Showing only the first would be misleading.
                lblGridTitle.Text = "My offers  (" + table.Rows.Count + ")   -   " +
                                    _offers.CountRunningForPharmacy(UserSession.PharmacyId) + " running today";

                // The grid has just been rebound, so the previous selection is gone and the
                // row-dependent buttons have to be re-decided.
                UpdateGridButtons();
            }
            catch (Exception ex)
            {
                // A read failure is shown and the form stays open with whatever it had. The
                // alternative, letting it escape, would close the screen on a transient
                // connection problem.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvOffers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Fires for the header row as well, where RowIndex is negative, and can fire
            // while the grid is being rebound and has no columns yet. Both would throw on
            // the cell lookup below.
            if (e.RowIndex < 0 || dgvOffers.Columns.Count == 0) return;

            DataGridViewRow row = dgvOffers.Rows[e.RowIndex];

            // OfferState is computed by the CASE expression in the query, not here. Keeping
            // the decision in SQL means the customer's Offers screen and this one agree on
            // what "Running" means, because they read the same expression rather than two
            // separate copies of the date comparison.
            object state = row.Cells["OfferState"].Value;
            if (state == null) return;      // the new-row placeholder has no value yet

            switch (state.ToString())
            {
                // Green for what is discounting today, amber for what is waiting to start,
                // grey for everything else, which is paused and expired. Colour carries the
                // same information as the State column, so the eye finds the live offers
                // without reading every row.
                case "Running": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;
                case "Scheduled": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;
                default: row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); break;
            }
        }

        private void dgvOffers_SelectionChanged(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvOffers.CurrentRow;

            // No current row, or the grid's blank new-row placeholder. Forget the selection
            // and leave the editor exactly as the user left it, so a stray click does not
            // wipe half typed text.
            if (row == null || row.Cells["OfferId"].Value == null)
            {
                _selectedOfferId = 0;
                UpdateGridButtons();
                return;
            }

            // Suppressed while the four assignments below run. Each one raises
            // Field_Changed, so without this the editor would validate four times mid-fill
            // and could flash an error for a value that is about to be overwritten.
            _loading = true;

            // Remembering the id is what switches the editor from create mode to amend
            // mode: btnUpdate, Pause, Resume and Delete all act on this one value.
            _selectedOfferId = Convert.ToInt32(row.Cells["OfferId"].Value);

            // Copy the row into the editor so the owner amends what is on screen rather
            // than retyping it. Convert.ToDateTime because the grid hands back boxed
            // objects from the DataTable, not typed values.
            txtOfferTitle.Text = row.Cells["OfferTitle"].Value.ToString();
            txtPercent.Text = row.Cells["DiscountPercent"].Value.ToString();
            dtpStart.Value = Convert.ToDateTime(row.Cells["StartDate"].Value);
            dtpEnd.Value = Convert.ToDateTime(row.Cells["EndDate"].Value);

            _loading = false;       // the fill is over, the user is in control again

            UpdateGridButtons();    // Pause or Resume depends on the row just selected
            ValidateAll();          // one pass over the values just loaded, not four
        }

        private void UpdateGridButtons()
        {
            DataGridViewRow row = dgvOffers.CurrentRow;

            // "A real row is selected", as opposed to nothing selected or the grid's blank
            // new-row placeholder, which has a null id.
            bool hasRow = row != null && row.Cells["OfferId"].Value != null;

            // Short circuiting matters in both directions here: hasRow stops the cell
            // lookup when there is no row, and the DBNull test stops Convert.ToBoolean
            // being handed a database null.
            bool active = hasRow && row.Cells["IsActive"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsActive"].Value);

            // Pause and Resume are deliberately exclusive: exactly one of them is available
            // for any selected row, so the owner cannot pause something already paused and
            // wonder why nothing changed.
            btnPause.Enabled = hasRow && active;
            btnResume.Enabled = hasRow && !active;

            // Delete applies to any real row whatever its state.
            btnDelete.Enabled = hasRow;
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        // ---------------------------------------------------------------------

        // Every editor control is wired to this one handler in the Designer, so there is a
        // single "something changed" entry point rather than several that could drift.
        private void Field_Changed(object sender, EventArgs e)
        {
            // Code is filling the controls, not the user, so there is nothing to judge yet.
            if (_loading) return;
            ValidateAll();
        }

        private bool ValidateAll()
        {
            bool ok = true;

            // Either a medicine is chosen from the list, or an existing offer is selected
            // and already knows which medicine it belongs to. The second case is what lets
            // the owner change the percentage on a selected offer without reselecting the
            // medicine it was created against.
            bool medicineChosen = SelectedMedicineId() > 0 || _selectedOfferId > 0;
            ok &= Check(medicineChosen, lblMedicineError, cmbMedicine,
                        "Choose which of your medicines the discount applies to.");

            // &= rather than &&: the right hand side is evaluated every time, so every rule
            // runs and every failing field is marked on the same pass. With && the first
            // failure would short circuit the rest and the owner would meet the errors one
            // at a time instead of all at once.
            ok &= Check(!Validator.IsBlank(txtOfferTitle.Text), lblOfferTitleError, txtOfferTitle,
                        "Give the offer a title, for example 'Fever Season Pack - 12% off'.");

            // THE FIRST OF THE TWO DOUBLED RULES. IsDiscountPercent enforces "greater than
            // 0 and no more than 70", which is exactly what CK_Offers_Percent enforces on
            // the Offers table. Doing it here turns a mistake into a red label under the
            // box; doing it there means a row that never came through this form still
            // cannot be written. The bounds are copied from the constraint so the two can
            // never disagree about what is legal.
            decimal percent;
            bool percentOk = Validator.IsDiscountPercent(txtPercent.Text, out percent);

            // percentOk is kept in a variable rather than inlined because the preview below
            // needs both the verdict and the parsed number.
            ok &= Check(percentOk, lblPercentError, txtPercent,
                        "The discount must be greater than 0 and no more than 70 (CK_Offers_Percent).");

            // THE SECOND DOUBLED RULE, matching CK_Offers_Dates. .Date on both sides strips
            // the time the pickers carry, so an offer that starts and ends on the same day
            // compares as equal and is allowed, which is what a one day promotion is.
            bool datesOk = dtpEnd.Value.Date >= dtpStart.Value.Date;

            // null as the field, because a DateTimePicker has no white background to tint
            // red. The message still appears beneath the pair, which is the part that matters.
            ok &= Check(datesOk, lblDateError, null,
                        "The end date cannot be earlier than the start date (CK_Offers_Dates).");

            // Live preview of what the customer will actually pay.
            int medicineId = SelectedMedicineId();
            if (percentOk && medicineId > 0)
            {
                // Read from the list held in memory rather than re-queried, so typing in the
                // percent box does not put a database round trip behind every keystroke.
                Medicine medicine = _medicineList.Find(m => m.MedicineId == medicineId);
                if (medicine != null)
                {
                    // 100m, not 100, forces decimal arithmetic. Integer division would floor
                    // every percentage to 0 and the preview would quietly show full price.
                    // Rounded to 2 places to match the DECIMAL(10,2) the real query CASTs to,
                    // so the preview shows the same figure the cart will charge.
                    decimal newPrice = decimal.Round(medicine.UnitPrice * (1 - percent / 100m), 2);

                    // Old price, new price and the saving, because "12% off" means less to a
                    // shopkeeper judging a promotion than "Tk 45.00 becomes Tk 39.60".
                    lblPreview.Text = medicine.MedicineName + " " + medicine.Strength +
                                      ":  Tk " + medicine.UnitPrice.ToString("N2") +
                                      "  ->  Tk " + newPrice.ToString("N2") +
                                      "   (customer saves Tk " + (medicine.UnitPrice - newPrice).ToString("N2") + " per unit)";
                }
            }
            else
            {
                // Cleared rather than left behind, so a stale preview cannot sit under a
                // percentage that no longer parses or a medicine that is no longer chosen.
                lblPreview.Text = "";
            }

            // The two buttons need different things. Create needs a medicine picked from
            // the list, because there is nothing else to attach a new offer to. Update needs
            // a selected offer, because it amends a row that already exists. Both need the
            // rules to pass, which is what ok carries.
            btnCreate.Enabled = ok && SelectedMedicineId() > 0;
            btnUpdate.Enabled = ok && _selectedOfferId > 0;
            return ok;
        }

        // One helper for every rule, so showing and clearing an error is written once. It
        // returns the verdict it was given, which is what lets the caller write
        // "ok &= Check(...)" and get the painting and the accumulation in a single line.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Clearing on success matters as much as showing on failure, or a message from
            // an earlier keystroke would sit under a field that is now correct.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);
            return rulePassed;
        }

        // ---------------------------------------------------------------------

        private void btnCreate_Click(object sender, EventArgs e)
        {
            // Revalidated even though the button is only enabled when valid: a keyboard
            // shortcut can raise a click the enabled state did not anticipate.
            if (!ValidateAll()) return;

            int medicineId = SelectedMedicineId();
            if (medicineId == 0) return;        // nothing to attach the offer to

            // Parse, not TryParse, and deliberately so: ValidateAll has already proved this
            // text parses, so a throw here would mean validation and save had drifted apart.
            decimal percent = decimal.Parse(txtPercent.Text);

            // The INSERT ... SELECT carries WHERE PharmacyId, so an offer can
            // never be created on another pharmacy's medicine.
            if (_offers.Create(medicineId, UserSession.PharmacyId, txtOfferTitle.Text,
                               percent, dtpStart.Value, dtpEnd.Value))
            {
                // The confirmation repeats the dates back, because an offer that starts in
                // the future looks like nothing happened until the owner is told when it
                // will begin.
                lblStatus.Text = "Offer created. It appears on the customer's Offers screen from " +
                                 dtpStart.Value.ToString("dd MMM") + " to " + dtpEnd.Value.ToString("dd MMM yyyy") + ".";
                ClearEditor();      // ready for the next one, rather than leaving a filled form that invites a duplicate
                LoadGrid();         // re-read, so the grid shows what the database now holds
            }
            else
            {
                // False means the INSERT ... SELECT matched no medicine, and since the id
                // came from this owner's own list the only remaining explanation is that the
                // row is not theirs. Nothing was written.
                MessageBox.Show("That medicine does not belong to your pharmacy.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            // Both guards are needed: there must be a row to amend, and the values must
            // pass. The order matters only in that the cheap test comes first.
            if (_selectedOfferId == 0 || !ValidateAll()) return;

            decimal percent = decimal.Parse(txtPercent.Text);

            // The UPDATE joins Medicines and filters on PharmacyId, so an offer id
            // belonging to another shop matches no row and changes nothing. Note the
            // medicine is not passed: an existing offer keeps the product it was created
            // against, and only the title, percentage and dates can be amended.
            if (_offers.Update(_selectedOfferId, UserSession.PharmacyId, txtOfferTitle.Text,
                               percent, dtpStart.Value, dtpEnd.Value))
            {
                lblStatus.Text = "Offer saved.";
                // The editor is deliberately NOT cleared here, so the owner can see what
                // they just saved and adjust it again if the preview reads wrong.
                LoadGrid();
            }
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;

            // A flag flip, not a delete. The row survives, so the same promotion can be
            // switched back on later without being retyped, and any order placed while it
            // ran keeps its history intact.
            _offers.SetActive(_selectedOfferId, UserSession.PharmacyId, false);
            lblStatus.Text = "Offer paused. The row is kept, so it can be switched back on at any time.";
            LoadGrid();     // re-read so the State column and the row colour both catch up
        }

        private void btnResume_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;

            // The exact inverse of Pause, calling the same method with true. One method for
            // both directions means the PharmacyId filter is written once rather than twice.
            _offers.SetActive(_selectedOfferId, UserSession.PharmacyId, true);
            lblStatus.Text = "Offer running again.";
            LoadGrid();
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;

            // Confirmed first, because this is the one button on the screen that destroys a
            // row. The message explains why that is safe: OrderItems stores its own
            // UnitPrice at the moment of sale, so deleting the offer cannot reach back and
            // change what a past order was charged.
            DialogResult answer = MessageBox.Show(
                "Delete this offer permanently?\r\n\r\n" +
                "Orders already placed keep the price they were sold at, because OrderItems stores its own UnitPrice.",
                "Delete offer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            // Anything other than Yes, including closing the box, means do nothing.
            if (answer != DialogResult.Yes) return;

            // A real DELETE is acceptable here only because nothing holds a foreign key to
            // Offers. A medicine or a category in the same position is deactivated instead,
            // because rows elsewhere point at them.
            _offers.Delete(_selectedOfferId, UserSession.PharmacyId);
            lblStatus.Text = "Offer deleted.";

            // Cleared because _selectedOfferId now names a row that no longer exists, and
            // leaving it set would let Update and Pause act on a missing id.
            ClearEditor();
            LoadGrid();
        }

        private void btnClearEditor_Click(object sender, EventArgs e) => ClearEditor();

        private void ClearEditor()
        {
            // Suppressed for the whole reset, so seven control changes produce one
            // validation pass at the end instead of seven partial ones.
            _loading = true;

            // Back to create mode: with no offer selected, Update, Pause, Resume and Delete
            // all have nothing to act on and are disabled by the two methods below.
            _selectedOfferId = 0;

            cmbMedicine.SelectedIndex = 0;      // the placeholder, not the first medicine
            txtOfferTitle.Clear();
            txtPercent.Clear();

            // Back to the same fortnight the form opened with, so a cleared editor and a
            // freshly opened one behave identically.
            dtpStart.Value = DateTime.Today;
            dtpEnd.Value = DateTime.Today.AddDays(14);

            // Without this the grid would still be sitting on a row, and the next
            // SelectionChanged would copy it straight back into the editor just cleared.
            dgvOffers.ClearSelection();

            _loading = false;
            ValidateAll();      // the single pass the suppression above was saving up for
        }

        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
