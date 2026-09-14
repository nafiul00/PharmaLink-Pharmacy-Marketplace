using System.Data;                  // DataTable and DataGridViewRow, the shape a read returns
using System.Drawing;               // Color and Font, used by the theming and the row colouring
using System.Windows.Forms;         // Form, Label, Control, DataGridView and MessageBox
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the rules
using PharmaLinkApp.Models;         // Medicine, used for the live price preview
using PharmaLinkApp.Services;       // OfferService and MedicineService, the only classes that reach SQL

// The same namespace every window uses, so AdminDashboard can name this form.
namespace PharmaLinkApp.Forms
{
    // Presentation layer, opened by AdminDashboard or by AdminMedicineForm.

    /// <summary>Requirement 14: time limited percentage discounts.</summary>
    public partial class DiscountOffersForm : Form   // percent and dates are checked here and again by CK_Offers_Percent/Dates
    {
        // One instance of each service; neither holds a connection between calls.
        private readonly OfferService _offers = new OfferService();
        private readonly MedicineService _medicines = new MedicineService();   // only for the ComboBox list; this screen never edits a medicine

        // Set when AdminMedicineForm opens this with a medicine in mind; 0 means none.
        private readonly int _preselectMedicineId;

        // Kept in memory too: the preview needs UnitPrice, which the ComboBox lacks.
        private List<Medicine> _medicineList = new List<Medicine>();

        // Which offer the grid sits on, or 0: it decides create mode from amend mode.
        private int _selectedOfferId;

        // True while code fills the editor, so Field_Changed skips a mid-fill pass.
        private bool _loading = true;

        // The Designer's constructor; it chains to the real one so there is one path.
        public DiscountOffersForm() : this(0) { }

        // The real constructor, and the only one that assigns anything.
        public DiscountOffersForm(int preselectMedicineId)
        {
            InitializeComponent();                      // build the controls first
            // Stored, not acted on: database work waits for Load, which has a window.
            _preselectMedicineId = preselectMedicineId;
        }

        // Fires once, after the window exists; the call order below matters.
        private void DiscountOffersForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();           // colours and fonts only, no data
            LoadMedicines();        // fills the ComboBox and the in-memory list behind it

            // A fortnight is the usual promotion, so today-to-today is not the default.
            dtpStart.Value = DateTime.Today;
            dtpEnd.Value = DateTime.Today.AddDays(14);   // AddDays, so month ends and leap years look after themselves

            // Cleared BEFORE LoadGrid, which returns immediately while this is true.
            _loading = false;
            LoadGrid();   // the first real read; it would have returned immediately one line earlier

            // After _loading is false, because the validation pass it raises is wanted.
            if (_preselectMedicineId > 0) SelectMedicine(_preselectMedicineId);

            // One deliberate pass, so the two save buttons start in the right state.
            ValidateAll();
        }

        // Colours, fonts and the grid styling. Nothing here reads or writes data.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Discount Offers");   // window background and the title bar text, from the same helper every screen uses

            panelHeader.BackColor = UiTheme.Primary;                  // the brand green band, which is what makes this read as a PharmaLink screen
            lblTitle.Font = UiTheme.FontTitle;                        // the page name, the largest text on the form
            lblTitle.ForeColor = Color.White;                         // white on green: the only pairing legible on the header band
            lblSubtitle.Font = UiTheme.FontSmall;                     // the fixed one-line explanation under the title
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);    // pale green, so the subtitle supports the title rather than competing with it

            lblGridTitle.Font = UiTheme.FontHeading;                  // section heading over the offers grid; LoadGrid rewrites its text with the counts
            lblGridTitle.ForeColor = UiTheme.TextDark;                // near-black from the theme, which is softer on white than pure black

            grpEditor.Font = UiTheme.FontHeading;                     // the GroupBox caption; children get their own font in the loop below
            grpEditor.ForeColor = UiTheme.Primary;                    // brand green caption, marking the editor as the working area of the screen
            grpEditor.BackColor = UiTheme.CardBack;                   // a tinted card, so the editor is visibly a panel rather than loose controls
            // Walking the container, so new editor fields are styled without edits here.
            foreach (Control child in grpEditor.Controls)
            {
                child.Font = UiTheme.FontBody;                        // one body font for every label, box and picker inside the card
                child.ForeColor = UiTheme.TextDark;                   // set for all of them first, then overridden just below for the error labels
                // Both must hold, and the pattern match types the variable in one step.
                if (child is Label label && label.Name.EndsWith("Error"))
                {
                    label.Font = UiTheme.FontSmall;                   // smaller than the field it sits under, so it reads as an annotation
                    label.ForeColor = UiTheme.Danger;                 // red, and the only red text in the card, so a problem is unmistakable
                }
            }

            lblPreview.Font = UiTheme.FontSmall;              // the live "was / now" line ValidateAll rewrites on every keystroke
            lblPreview.ForeColor = UiTheme.Success;           // green: this line is a result, not a warning
            lblNote.Font = UiTheme.FontSmall;                 // the fixed explanatory note that never changes at run time
            lblNote.ForeColor = UiTheme.TextMuted;            // grey, so standing guidance never looks like live feedback
            lblStatus.Font = UiTheme.FontSmall;               // the outcome line the save handlers write to
            lblStatus.ForeColor = UiTheme.TextMuted;          // grey too: a status message is information, and failures use a dialog instead

            UiTheme.StyleSecondary(btnBack);          // grey: leaves the screen and changes nothing
            UiTheme.StyleSuccess(btnCreate);          // green: the action that adds a new offer
            UiTheme.StylePrimary(btnUpdate);          // brand colour: amends the offer already selected
            UiTheme.StyleSecondary(btnClearEditor);   // grey: resets the form back to create mode, touching no data
            UiTheme.StyleDanger(btnPause);            // red: it stops customers seeing the discount, even though the row survives
            UiTheme.StyleAccent(btnResume);           // accent rather than green, so Pause and Resume are never confused at a glance
            UiTheme.StyleDanger(btnDelete);           // red: the only button here that destroys a row
            UiTheme.StyleGrid(dgvOffers);             // row height, headers and selection colour, shared with every grid
            dgvOffers.CellFormatting += dgvOffers_CellFormatting;   // subscribed here, once, because ApplyTheme runs once from Load
        }

        // Fills the ComboBox AND the backing list in one pass, so they cannot disagree.
        private void LoadMedicines()
        {
            // Filtered on PharmacyId and IsActive = 1, so only this shop's live items.
            _medicineList = _medicines.GetSimpleListForPharmacy(UserSession.PharmacyId);

            // Cleared first, or a reload would list every medicine twice.
            cmbMedicine.Items.Clear();

            // A placeholder at index 0, so no medicine starts out chosen by accident.
            cmbMedicine.Items.Add("- choose one of your medicines -");

            foreach (Medicine medicine in _medicineList)   // built from the same list the preview reads, so the two cannot disagree
            {
                // The id goes FIRST, before the space, because SelectedMedicineId reads it.
                cmbMedicine.Items.Add(medicine.MedicineId + " - " + medicine.MedicineName + " " +
                                      medicine.Strength + "  (Tk " + medicine.UnitPrice.ToString("N2") + ")");   // N2 fixes two decimals, so the list of prices lines up as a column
            }

            // Land on the placeholder, the "nothing chosen" state validation rejects.
            cmbMedicine.SelectedIndex = 0;
        }

        // Moves the ComboBox onto a medicine, used only for the handed-in preselection.
        private void SelectMedicine(int medicineId)
        {
            // Starts at 1, not 0, because index 0 is the placeholder and has no id to read.
            for (int i = 1; i < cmbMedicine.Items.Count; i++)
            {
                string text = cmbMedicine.Items[i].ToString();   // items are stored as strings, so ToString is the whole item, not a type name

                // Substring to the first space is exactly the id LoadMedicines wrote.
                if (int.Parse(text.Substring(0, text.IndexOf(' '))) == medicineId)
                {
                    cmbMedicine.SelectedIndex = i;   // raises SelectedIndexChanged, wanted here: it previews and validates
                    return;         // stop at the first match; ids are unique
                }
            }
            // Falling out is not an error: the ComboBox just stays on the placeholder.
        }

        // The inverse of SelectMedicine: reads the id back out of the chosen text.
        private int SelectedMedicineId()
        {
            // Index 0 and -1 both report 0, so callers test one empty value, not two.
            if (cmbMedicine.SelectedIndex <= 0) return 0;

            // The same id-before-the-first-space format LoadMedicines wrote.
            string text = cmbMedicine.SelectedItem.ToString();
            return int.Parse(text.Substring(0, text.IndexOf(' ')));   // safe past the index 0 guard above: every remaining entry starts with digits
        }

        // ---------------------------------------------------------------------

        // The single refresh path for the grid: every save and delete ends here.
        private void LoadGrid()
        {
            // Load is still setting defaults, and those can reach here through handlers.
            if (_loading) return;

            try   // covers the read, the bind and both counts, so a failure leaves the form intact
            {
                // The query joins Medicines and filters PharmacyId: isolation is in SQL.
                DataTable table = _offers.GetForPharmacy(UserSession.PharmacyId);

                // Binding the DataTable is what gives the grid its columns.
                dgvOffers.DataSource = table;

                // Guarded: with no columns, every line below throws on a missing name.
                if (dgvOffers.Columns.Count > 0)
                {
                    // Header text only; FillWeight is a proportion, not a pixel width.
                    dgvOffers.Columns["OfferId"].HeaderText = "ID";
                    dgvOffers.Columns["OfferId"].FillWeight = 28;             // the smallest share on the grid: an id is at most a few digits
                    dgvOffers.Columns["OfferTitle"].HeaderText = "Offer";     // the owner's own wording, which is how they recognise a promotion
                    dgvOffers.Columns["OfferTitle"].FillWeight = 120;         // the largest share, because titles are free text and run long
                    dgvOffers.Columns["MedicineName"].HeaderText = "Medicine";   // joined in by the query; the grid never shows a MedicineId
                    dgvOffers.Columns["Strength"].HeaderText = "Strength";    // kept beside the name, because the same brand exists in several strengths
                    dgvOffers.Columns["Strength"].FillWeight = 45;            // short values like "500mg", so it gives its width to the title

                    // Was and Now sit side by side, both already calculated by the query.
                    dgvOffers.Columns["OriginalPrice"].HeaderText = "Was (Tk)";
                    dgvOffers.Columns["DiscountPercent"].HeaderText = "Off %";    // the rate, sitting between the two prices it turns into each other
                    dgvOffers.Columns["DiscountPercent"].FillWeight = 38;         // never more than two digits, so it needs almost no width
                    dgvOffers.Columns["DiscountedPrice"].HeaderText = "Now (Tk)"; // computed by the query, so it is the same figure the cart will charge
                    dgvOffers.Columns["StartDate"].HeaderText = "From";           // plain words, because the pair is read as a range rather than two fields
                    dgvOffers.Columns["EndDate"].HeaderText = "Until";            // "Until" rather than "To", which reads as a destination next to a date

                    // Hidden, not dropped: UpdateGridButtons reads IsActive from the row.
                    dgvOffers.Columns["IsActive"].Visible = false;

                    dgvOffers.Columns["OfferState"].HeaderText = "State";   // Running, Scheduled, Paused or Expired, decided by the query's CASE
                    dgvOffers.Columns["OfferState"].FillWeight = 52;        // sized for "Scheduled", the longest word the column can hold
                }

                // Two counts: every offer ever created, and how many discount today.
                lblGridTitle.Text = "My offers  (" + table.Rows.Count + ")   -   " +
                                    _offers.CountRunningForPharmacy(UserSession.PharmacyId) + " running today";   // counted in SQL by the same date rule the State column uses

                // Just rebound, so the row-dependent buttons must be re-decided.
                UpdateGridButtons();
            }
            catch (Exception ex)   // Exception, not SqlException: a binding fault reports the same way
            {
                // Shown, and the form stays open; escaping would close it on a blip.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Runs once per painted cell, so it stays cheap and never queries anything.
        private void dgvOffers_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Fires for the header row (-1) and mid-rebind, when there are no columns.
            if (e.RowIndex < 0 || dgvOffers.Columns.Count == 0) return;

            DataGridViewRow row = dgvOffers.Rows[e.RowIndex];   // the row, not the cell, because the colour is applied to the whole line

            // OfferState is the query's CASE, so the customer's screen agrees with this.
            object state = row.Cells["OfferState"].Value;
            if (state == null) return;      // the new-row placeholder has no value yet

            // A switch, because the CASE in the query produces a closed set of words.
            switch (state.ToString())
            {
                // Green for live, amber for waiting, grey for paused and expired.
                case "Running": row.DefaultCellStyle.BackColor = UiTheme.DeliveredBack; break;
                case "Scheduled": row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 224); break;   // the same amber the dashboard uses for "waiting"
                default: row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); break;   // default, not two cases: it also repaints rows the grid reuses
            }
        }

        // Selecting a row copies it into the editor, so no second window opens.
        private void dgvOffers_SelectionChanged(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvOffers.CurrentRow;   // CurrentRow, not SelectedRows[0], which throws when the selection is empty

            // No row, or the blank new-row placeholder: forget it and leave the editor.
            if (row == null || row.Cells["OfferId"].Value == null)
            {
                _selectedOfferId = 0;   // back to create mode, so Update and Delete have nothing to act on
                UpdateGridButtons();    // and the buttons are re-decided from that, rather than left as they were
                return;                 // the editor's contents are deliberately untouched on the way out
            }

            // Suppressed while the four assignments below raise Field_Changed.
            _loading = true;

            // Remembering the id is what switches the editor into amend mode.
            _selectedOfferId = Convert.ToInt32(row.Cells["OfferId"].Value);

            // Copy the row in, so the owner amends what is on screen rather than retypes.
            txtOfferTitle.Text = row.Cells["OfferTitle"].Value.ToString();
            txtPercent.Text = row.Cells["DiscountPercent"].Value.ToString();     // as text, because the box is a TextBox and Validator parses it back
            dtpStart.Value = Convert.ToDateTime(row.Cells["StartDate"].Value);   // Convert, not a cast: the boxed value's exact type is the provider's choice
            dtpEnd.Value = Convert.ToDateTime(row.Cells["EndDate"].Value);       // assigned after the start date, so the range is never inverted

            _loading = false;       // the fill is over, the user is in control again

            UpdateGridButtons();    // Pause or Resume depends on the row just selected
            ValidateAll();          // one pass over the values just loaded, not four
        }

        // The three row-dependent buttons: which row is selected, not the values.
        private void UpdateGridButtons()
        {
            DataGridViewRow row = dgvOffers.CurrentRow;   // read fresh each time, so the buttons can never describe a previous selection

            // "A real row", as opposed to nothing, or the blank new-row placeholder.
            bool hasRow = row != null && row.Cells["OfferId"].Value != null;

            // Short circuiting both ways: no row skips the lookup, DBNull skips Convert.
            bool active = hasRow && row.Cells["IsActive"].Value != DBNull.Value &&
                          Convert.ToBoolean(row.Cells["IsActive"].Value);   // the BIT column arrives boxed, so Convert handles whichever type it is

            // Pause and Resume are exclusive: exactly one is available for any row.
            btnPause.Enabled = hasRow && active;
            btnResume.Enabled = hasRow && !active;   // the exact negation above, so the pair can never both be on

            // Delete applies to any real row whatever its state.
            btnDelete.Enabled = hasRow;
        }

        // ----------  VALIDATION  ----------

        // Every editor control is wired here: one "something changed" entry point.
        private void Field_Changed(object sender, EventArgs e)
        {
            // Code is filling the controls, not the user, so there is nothing to judge yet.
            if (_loading) return;
            ValidateAll();   // the return value is ignored; the labels and button states are the point
        }

        // Returns the verdict AND paints the screen: labels, preview and save buttons.
        private bool ValidateAll()
        {
            bool ok = true;   // starts true and is only ever narrowed by the &= lines below

            // Either a medicine is chosen, or a selected offer already knows its own.
            bool medicineChosen = SelectedMedicineId() > 0 || _selectedOfferId > 0;
            ok &= Check(medicineChosen, lblMedicineError, cmbMedicine,   // the ComboBox is passed so the field itself is tinted, not just the label
                        "Choose which of your medicines the discount applies to.");   // "your medicines" names the scope: the list only ever holds this shop's items

            // &= not &&: every rule runs, so every failing field is marked in one pass.
            ok &= Check(!Validator.IsBlank(txtOfferTitle.Text), lblOfferTitleError, txtOfferTitle,
                        "Give the offer a title, for example 'Fever Season Pack - 12% off'.");   // an example rather than a rule, because any non-blank title is legal

            // Doubled rule one: the same 0 to 70 bounds CK_Offers_Percent enforces.
            decimal percent;
            bool percentOk = Validator.IsDiscountPercent(txtPercent.Text, out percent);   // out hands back the parsed number, so the preview need not parse again

            // Kept in a variable, because the preview needs verdict and number both.
            ok &= Check(percentOk, lblPercentError, txtPercent,
                        "The discount must be greater than 0 and no more than 70 (CK_Offers_Percent).");   // names the constraint, so message and schema can be checked together

            // Doubled rule two, CK_Offers_Dates; .Date allows a one day promotion.
            bool datesOk = dtpEnd.Value.Date >= dtpStart.Value.Date;

            // null as the field: a DateTimePicker has no white background to tint red.
            ok &= Check(datesOk, lblDateError, null,
                        "The end date cannot be earlier than the start date (CK_Offers_Dates).");   // "cannot be earlier" rather than "must be after", because equal dates are allowed

            // Live preview of what the customer will actually pay.
            int medicineId = SelectedMedicineId();
            if (percentOk && medicineId > 0)   // both halves are needed; either one alone previews nothing
            {
                // Read from memory, so a keystroke does not cost a database round trip.
                Medicine medicine = _medicineList.Find(m => m.MedicineId == medicineId);
                if (medicine != null)   // Find returns null rather than throwing, so the preview stays blank
                {
                    // 100m forces decimal: integer division would floor every rate to 0.
                    decimal newPrice = decimal.Round(medicine.UnitPrice * (1 - percent / 100m), 2);   // rounded to 2 places, matching the DECIMAL(10,2) the query casts to

                    // Old, new and the saving, because a rate alone means little.
                    lblPreview.Text = medicine.MedicineName + " " + medicine.Strength +
                                      ":  Tk " + medicine.UnitPrice.ToString("N2") +   // the old price first, so the line reads as a change rather than a figure
                                      "  ->  Tk " + newPrice.ToString("N2") +          // an arrow rather than an en dash, which would read as a range
                                      "   (customer saves Tk " + (medicine.UnitPrice - newPrice).ToString("N2") + " per unit)";   // "per unit" matters: the saving multiplies by quantity at checkout
                }
            }
            else   // one branch covers both gaps, because either one alone makes a preview impossible
            {
                // Cleared, so a stale preview cannot outlive the value that made it.
                lblPreview.Text = "";
            }

            // Create needs a medicine picked, Update a selected offer; both need ok.
            btnCreate.Enabled = ok && SelectedMedicineId() > 0;
            btnUpdate.Enabled = ok && _selectedOfferId > 0;   // amending needs an existing row, which is the one thing Create does not need
            return ok;   // handed back, so a click handler can re-run it as its own guard
        }

        // One helper per rule, so "ok &= Check(...)" both paints and accumulates.
        private bool Check(bool rulePassed, Label errorLabel, Control field, string message)
        {
            // Clearing on success matters as much as showing: no message may linger.
            if (rulePassed) UiTheme.ClearError(errorLabel, field);
            else UiTheme.ShowError(errorLabel, field, message);   // writes the text into the label and tints the field, both from one theme helper
            return rulePassed;   // passed straight back, unchanged
        }

        // ---------------------------------------------------------------------

        // Writes a brand new offer, and the only handler that reports a refusal.
        private void btnCreate_Click(object sender, EventArgs e)
        {
            // Revalidated: a keyboard shortcut can raise an unexpected click.
            if (!ValidateAll()) return;

            int medicineId = SelectedMedicineId();   // read once, so the guard and the call cannot name different medicines
            if (medicineId == 0) return;        // nothing to attach the offer to

            // Parse, not TryParse: ValidateAll has already proved this text parses.
            decimal percent = decimal.Parse(txtPercent.Text);

            // The INSERT ... SELECT carries WHERE PharmacyId, so a foreign id fails.
            if (_offers.Create(medicineId, UserSession.PharmacyId, txtOfferTitle.Text,   // the pharmacy id comes from the session, never from a control on this form
                               percent, dtpStart.Value, dtpEnd.Value))   // the pickers' DateTime values go straight through; the service parameterises them
            {
                // The dates are repeated back: a future offer looks like nothing happened.
                lblStatus.Text = "Offer created. It appears on the customer's Offers screen from " +
                                 dtpStart.Value.ToString("dd MMM") + " to " + dtpEnd.Value.ToString("dd MMM yyyy") + ".";   // the year appears once, on the end date, because repeating it reads as clutter
                ClearEditor();      // ready for the next one, rather than a filled form inviting a duplicate
                LoadGrid();         // re-read, so the grid shows what the database now holds
            }
            else   // reached only when the service reports zero rows inserted
            {
                // False means no medicine matched: the row is not this shop's.
                MessageBox.Show("That medicine does not belong to your pharmacy.", "PharmaLink",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);   // warning, not error: the guard worked as designed, nothing broke
            }
        }

        // Amends the selected offer, with no failure message it can usefully give.
        private void btnUpdate_Click(object sender, EventArgs e)
        {
            // Both guards: a row to amend, and values that pass. Cheap test first.
            if (_selectedOfferId == 0 || !ValidateAll()) return;

            decimal percent = decimal.Parse(txtPercent.Text);   // Parse, not TryParse, for the same reason as in Create

            // The UPDATE joins Medicines and filters PharmacyId, so a foreign id misses.
            if (_offers.Update(_selectedOfferId, UserSession.PharmacyId, txtOfferTitle.Text,   // the offer id names the row, the pharmacy id proves it is this shop's
                               percent, dtpStart.Value, dtpEnd.Value))   // no medicine argument: an offer keeps the product it was created against
            {
                lblStatus.Text = "Offer saved.";   // a short line, not a dialog: amending is routine
            // Deliberately NOT cleared, so the owner can see and adjust what was saved.
                LoadGrid();
            }
        }

        // Takes a running offer off the customer's screen without destroying it.
        private void btnPause_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;   // no ValidateAll: pausing acts on the stored row, not on the editor

            // A flag flip, not a delete: the row survives and history stays intact.
            _offers.SetActive(_selectedOfferId, UserSession.PharmacyId, false);
            lblStatus.Text = "Offer paused. The row is kept, so it can be switched back on at any time.";   // says the row survives, so a red button does not read as a delete
            LoadGrid();     // re-read so the State column and the row colour both catch up
        }

        // Puts a paused offer back on sale. Never enabled at the same time as Pause.
        private void btnResume_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;   // the same guard as Pause: a shortcut can outrun the enabled state

            // The exact inverse of Pause, so the PharmacyId filter is written once.
            _offers.SetActive(_selectedOfferId, UserSession.PharmacyId, true);
            lblStatus.Text = "Offer running again.";   // "running" is the word the State column uses, so the two agree
            LoadGrid();   // the State column and the row colour are both derived from the flag just changed
        }

        // The only destructive button on this screen, and the only one that asks first.
        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (_selectedOfferId == 0) return;   // checked before the dialog, so no prompt appears with nothing selected

            // Confirmed first: this is the one button on the screen that destroys a row.
            DialogResult answer = MessageBox.Show(
                "Delete this offer permanently?\r\n\r\n" +   // "permanently" is what separates this from Pause, the other red button
                "Orders already placed keep the price they were sold at, because OrderItems stores its own UnitPrice.",   // answers the fear the question raises, about past sales
                "Delete offer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);   // Yes/No, because this is a decision rather than a notice to acknowledge

            // Anything other than Yes, including closing the box, means do nothing.
            if (answer != DialogResult.Yes) return;

            // A real DELETE is fine only because nothing holds a foreign key to Offers.
            _offers.Delete(_selectedOfferId, UserSession.PharmacyId);
            lblStatus.Text = "Offer deleted.";   // past tense and final, matching an action that cannot be undone from this screen

            // Cleared: _selectedOfferId now names a row that no longer exists.
            ClearEditor();
            LoadGrid();   // last, so the grid rebinds after the editor let go of the deleted id
        }

        // An expression body, because the whole handler is one call.
        private void btnClearEditor_Click(object sender, EventArgs e) => ClearEditor();

        // The reset used by Clear, by Create after a save and by Delete afterwards.
        private void ClearEditor()
        {
            // Suppressed for the whole reset: seven changes, one validation pass.
            _loading = true;

            // Back to create mode: Update, Pause, Resume and Delete lose their target.
            _selectedOfferId = 0;

            cmbMedicine.SelectedIndex = 0;      // the placeholder, not the first medicine
            txtOfferTitle.Clear();   // Clear() rather than assigning "", which reads as setting a value
            txtPercent.Clear();      // emptied, not zeroed: "0" is a percentage the rules reject

            // Back to the fortnight the form opened with, so both paths behave alike.
            dtpStart.Value = DateTime.Today;
            dtpEnd.Value = DateTime.Today.AddDays(14);   // the same fortnight the Load handler sets, deliberately duplicated

            // Without this, the next SelectionChanged refills the editor just cleared.
            dgvOffers.ClearSelection();

            _loading = false;   // lifted before the pass below, or that pass would return immediately too
            ValidateAll();      // the single pass the suppression above was saving up for
        }

        // Close(), not Application.Exit(): this was opened with ShowDialog.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
