using System.Data;                  // DataTable and DataGridViewRow, the shape a read returns
using System.Drawing;               // Color and Font, used by the theming and the row colouring
using System.Windows.Forms;         // Form, Control, DataGridView, MessageBox and DialogResult
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator owns the blank test
using PharmaLinkApp.Services;       // CategoryService, the only class here that reaches SQL

namespace PharmaLinkApp.Forms
{
    /// <summary>
    /// Requirement 7. The master category list, with Add, Save, Deactivate and
    /// Reactivate.
    ///
    /// A category that a medicine already references is never deleted, only
    /// deactivated, which is what keeps every foreign key from Medicines valid.
    /// </summary>
    public partial class ManageCategoriesForm : Form
    {
        // One service for the life of the form. It holds no connection of its own:
        // DbHelper opens and closes one inside every call.
        private readonly CategoryService _categories = new CategoryService();

        // Which row the grid is sitting on, or 0 when nothing is selected. This single
        // field decides three separate things: whether Save and the two status buttons
        // are available, which id they act on, and which id the duplicate name check is
        // told to IGNORE. That last use is the reason it is an id and not just a flag.
        private int _selectedId;

        public ManageCategoriesForm()
        {
            InitializeComponent();      // build the controls from the Designer file first
            // Nothing else here. Database work waits for the Load event, because a
            // constructor that throws leaves no window in which to show the error.
        }

        private void ManageCategoriesForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours and fonts only, no data
            LoadGrid();         // the first and only read needed to populate the screen
        }

        // Colours, fonts and the grid styling. Nothing here reads or writes data.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Medicine Categories");

            panelHeader.BackColor = UiTheme.Primary;
            lblTitle.Font = UiTheme.FontTitle;
            lblTitle.ForeColor = Color.White;
            lblSubtitle.Font = UiTheme.FontSmall;
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);

            grpEditor.Font = UiTheme.FontHeading;
            grpEditor.ForeColor = UiTheme.Primary;
            grpEditor.BackColor = UiTheme.CardBack;
            foreach (Control child in grpEditor.Controls)
            {
                child.Font = UiTheme.FontBody;
                child.ForeColor = UiTheme.TextDark;
            }

            lblNameError.Font = UiTheme.FontSmall;
            lblNameError.ForeColor = UiTheme.Danger;
            lblEditorNote.Font = UiTheme.FontSmall;
            lblEditorNote.ForeColor = UiTheme.TextMuted;
            lblStatus.Font = UiTheme.FontSmall;
            lblStatus.ForeColor = UiTheme.TextMuted;

            UiTheme.StyleSecondary(btnBack);
            UiTheme.StyleSuccess(btnAdd);
            UiTheme.StylePrimary(btnUpdate);
            UiTheme.StyleDanger(btnDeactivate);
            UiTheme.StyleAccent(btnActivate);
            UiTheme.StyleSecondary(btnNew);
            UiTheme.StyleGrid(dgvCategories);
            dgvCategories.CellFormatting += dgvCategories_CellFormatting;
        }

        private void LoadGrid()
        {
            try
            {
                // The checkbox is inverted on purpose: "show inactive" ticked means
                // activeOnly false, which is the parameter the query understands. Doing the
                // inversion here keeps the service reading as a plain data method rather
                // than one that has to know what a checkbox on a form means.
                DataTable table = _categories.GetTable(!chkShowInactive.Checked);

                // Bound straight to the grid, so the columns are whatever the SELECT list
                // was. That is why the headers below are renamed rather than declared.
                dgvCategories.DataSource = table;

                // Guarded because a failed bind would leave no columns and every line below
                // would throw on a missing column name.
                if (dgvCategories.Columns.Count > 0)
                {
                    // Header text only. FillWeight is a proportion rather than a pixel
                    // width, so the columns keep their relative sizes when the window
                    // is resized: Description gets the most room because it is the longest.
                    dgvCategories.Columns["CategoryId"].HeaderText = "ID";
                    dgvCategories.Columns["CategoryId"].FillWeight = 25;
                    dgvCategories.Columns["CategoryName"].HeaderText = "Category";
                    dgvCategories.Columns["CategoryName"].FillWeight = 70;
                    dgvCategories.Columns["Description"].HeaderText = "Description";
                    dgvCategories.Columns["Description"].FillWeight = 130;
                    dgvCategories.Columns["IsActive"].HeaderText = "Active";
                    dgvCategories.Columns["IsActive"].FillWeight = 35;

                    // MedicineCount is a correlated subquery in the SELECT, not a number
                    // counted in C#. It is what makes the deactivate decision informed: a
                    // category with medicines behind it is one that must never be deleted.
                    dgvCategories.Columns["MedicineCount"].HeaderText = "Medicines";
                    dgvCategories.Columns["MedicineCount"].FillWeight = 45;
                }

                // The singular is handled rather than printing "1 categories", which reads
                // as a bug in an otherwise finished screen.
                lblStatus.Text = table.Rows.Count + " categor" + (table.Rows.Count == 1 ? "y" : "ies") + " listed.";

                // The grid has just been rebound, so the row-dependent buttons have to be
                // re-decided against whatever is selected now.
                UpdateButtons();
            }
            catch (Exception ex)
            {
                // A read failure is reported and the form stays open with what it had.
                // Letting it escape would close the screen on a transient connection fault.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvCategories_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Fires for the header row, where RowIndex is negative, and can fire mid-rebind
            // when there are no columns. Both would throw on the cell lookup below.
            if (e.RowIndex < 0 || dgvCategories.Columns.Count == 0) return;

            DataGridViewRow row = dgvCategories.Rows[e.RowIndex];
            object value = row.Cells["IsActive"].Value;

            // Two different empties: null is the grid's blank new-row placeholder, DBNull is
            // a database null. Neither can be handed to Convert.ToBoolean as a real value.
            if (value == null || value == DBNull.Value) return;

            bool active = Convert.ToBoolean(value);

            // A deactivated category is greyed rather than hidden, because it is still a
            // real row that medicines point at. Colouring it says "retired, not gone",
            // which is exactly what IsActive = 0 means here.
            row.DefaultCellStyle.ForeColor = active ? UiTheme.TextDark : UiTheme.TextMuted;
            row.DefaultCellStyle.BackColor = active ? Color.White : Color.FromArgb(240, 240, 240);
        }

        private void dgvCategories_SelectionChanged(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvCategories.CurrentRow;

            // No row, or the blank new-row placeholder. Returning leaves both _selectedId
            // and the editor exactly as they were, so a stray click cannot silently wipe
            // text the user is part way through typing.
            if (row == null || row.Cells["CategoryId"].Value == null) return;

            // Remembering the id is what switches the editor from add mode to edit mode:
            // Save, Deactivate and Reactivate all act on this value, and the duplicate name
            // check uses it as the row to ignore.
            _selectedId = Convert.ToInt32(row.Cells["CategoryId"].Value);

            // Copy the row into the editor so the name is amended rather than retyped.
            txtName.Text = row.Cells["CategoryName"].Value.ToString();

            // Description is a nullable column, so the DBNull has to be turned into an
            // empty string. Calling ToString on DBNull would put the literal text
            // "System.DBNull" into the box.
            txtDescription.Text = row.Cells["Description"].Value == DBNull.Value
                ? "" : row.Cells["Description"].Value.ToString();

            // Assigning txtName above raises TextChanged, which already ran the duplicate
            // check and called UpdateButtons. This second call is what settles the two
            // status buttons against the row just selected, which TextChanged knows nothing
            // about.
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasSelection = _selectedId > 0;
            bool nameOk = !Validator.IsBlank(txtName.Text);

            // Add needs only a name, because it creates a row from nothing. Save needs a
            // name AND a selected row, because it amends one that already exists. Disabling
            // rather than checking on click is what stops an empty name being attempted at
            // all, which is the friendlier half of the same rule the database enforces.
            btnAdd.Enabled = nameOk;
            btnUpdate.Enabled = hasSelection && nameOk;

            // The status buttons read IsActive from the selected row rather than from a
            // field, so they always reflect what the database last said rather than what
            // this form remembers. The DBNull test guards Convert.ToBoolean.
            if (hasSelection && dgvCategories.CurrentRow != null &&
                dgvCategories.CurrentRow.Cells["IsActive"].Value != DBNull.Value)
            {
                bool active = Convert.ToBoolean(dgvCategories.CurrentRow.Cells["IsActive"].Value);

                // Exactly one of the two is ever available, so the Super Admin cannot
                // deactivate something already deactivated and wonder why nothing changed.
                btnDeactivate.Enabled = active;
                btnActivate.Enabled = !active;
            }
            else
            {
                // Nothing selected, so neither button has a row to act on.
                btnDeactivate.Enabled = false;
                btnActivate.Enabled = false;
            }
        }

        // ---------------------------------------------------------------------
        //  VALIDATION
        //  The name is checked for emptiness and for the UNIQUE constraint before
        //  anything is sent, so a duplicate produces a red label rather than an
        //  unhandled SQL exception.
        // ---------------------------------------------------------------------

        private void txtName_TextChanged(object sender, EventArgs e)
        {
            // An empty box clears the error rather than showing one. Nagging about a field
            // the user has only just started typing into reads as broken; the real "cannot
            // be empty" message is raised by NameIsUsable at the moment Add or Save is
            // pressed, which is when emptiness actually matters.
            if (Validator.IsBlank(txtName.Text))
            {
                UiTheme.ClearError(lblNameError, txtName);
            }
            // Checked on every keystroke, so a clash is shown while typing rather than
            // on save. _selectedId is passed as the id to IGNORE: when EDITING a
            // category, its own name must not count as a duplicate of itself. Adding a
            // new one passes 0, which matches no row, so every existing name counts.
            //
            // The message names UQ_Categories_Name deliberately - this check is a
            // courtesy and the UNIQUE constraint is what actually guarantees it.
            else if (_categories.NameExists(txtName.Text, _selectedId))
            {
                // Written out rather than routed through a helper, because the message has
                // to quote what the user typed.
                UiTheme.ShowError(lblNameError, txtName,
                    "A category called '" + txtName.Text.Trim() + "' already exists (UQ_Categories_Name).");
            }
            else
            {
                // A name that is neither blank nor taken. Clearing matters as much as
                // showing, or a warning from an earlier keystroke would sit under a box
                // that is now perfectly valid.
                UiTheme.ClearError(lblNameError, txtName);
            }

            // The buttons follow the name, so they are re-decided on every keystroke rather
            // than only when the grid selection changes.
            UpdateButtons();
        }

        private bool NameIsUsable(int ignoreId)
        {
            // Deliberately a separate method from the keystroke handler, and deliberately
            // returning a verdict. The handler exists to give live feedback; this one is the
            // gate in front of a write, and it is the one that refuses to proceed. Both
            // rules are re-run here rather than trusting the last keystroke's result,
            // because the grid selection could have changed what "duplicate" means since.
            if (Validator.IsBlank(txtName.Text))
            {
                UiTheme.ShowError(lblNameError, txtName, "The category name cannot be empty.");
                return false;
            }

            // The ignoreId is passed by the CALLER rather than read from _selectedId, which
            // is what lets Add pass 0 and Save pass the selected id from the same method.
            // The alternative, two near identical methods, would be two places for the rule
            // to drift.
            if (_categories.NameExists(txtName.Text, ignoreId))
            {
                UiTheme.ShowError(lblNameError, txtName,
                    "A category called '" + txtName.Text.Trim() + "' already exists (UQ_Categories_Name).");
                return false;
            }

            UiTheme.ClearError(lblNameError, txtName);
            return true;
        }

        // ---------------------------------------------------------------------

        private void btnAdd_Click(object sender, EventArgs e)
        {
            // 0 as the id to ignore, because a brand new category has no row of its own to
            // excuse: every existing name is a genuine clash.
            if (!NameIsUsable(0)) return;

            // The text goes across untrimmed. The service trims before it reaches the
            // parameter, so the trimming rule lives in one place rather than on every form.
            _categories.Add(txtName.Text, txtDescription.Text);
            lblStatus.Text = "Category '" + txtName.Text.Trim() + "' added.";

            // Cleared so the editor is ready for the next one rather than leaving a filled
            // form that invites pressing Add twice and meeting the UNIQUE constraint.
            ClearEditor();
            LoadGrid();     // re-read, so the grid shows what the database now holds
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            // Nothing selected means there is no row to amend. Checked even though the
            // button is disabled in that state, because a keyboard shortcut can raise a
            // click the enabled state did not anticipate.
            if (_selectedId == 0) return;

            // _selectedId as the id to IGNORE, so saving a category without renaming it is
            // not blocked by its own name. Passing 0 here would make every edit impossible.
            if (!NameIsUsable(_selectedId)) return;

            _categories.Update(_selectedId, txtName.Text, txtDescription.Text);

            // The message says what an owner would otherwise have to guess: medicines store
            // a CategoryId, not a category name, so a rename reaches every one of them at
            // once with no second update anywhere.
            lblStatus.Text = "Category saved. Every medicine pointing at it now shows the new name.";

            // Not cleared, unlike Add: the row stays selected so the result of the edit can
            // be seen in the grid next to the editor that produced it.
            LoadGrid();
        }

        private void btnDeactivate_Click(object sender, EventArgs e)
        {
            if (_selectedId == 0) return;

            // Asked BEFORE the confirmation is shown, so the message can tell the truth
            // about this particular category rather than issuing a generic warning. The
            // grid's Medicines column shows the same fact, but the dialog is where the
            // decision is actually being made.
            bool referenced = _categories.IsReferenced(_selectedId);

            // Built as a separate string so the confirmation reads naturally when nothing
            // points at the category, rather than always carrying a clause about medicines
            // that may not exist.
            string extra = referenced
                ? "\r\n\r\nMedicines already point at this category, so it can only be deactivated, never deleted."
                : "";

            // Confirmed because this changes what every category dropdown in the
            // application offers. The wording is careful to say what does NOT happen:
            // existing medicines keep working, because no row is removed and no foreign key
            // is broken.
            DialogResult answer = MessageBox.Show(
                "Deactivate '" + txtName.Text.Trim() + "'?\r\n\r\n" +
                "It disappears from every category dropdown but existing medicines keep working." + extra,
                "Deactivate category", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            // Anything other than Yes, including closing the box, means do nothing.
            if (answer != DialogResult.Yes) return;

            // A soft delete: an UPDATE setting IsActive to 0, never a DELETE. Medicines hold
            // a foreign key to Categories and FK_Medicines_Category has no cascade, so a
            // real delete would either be refused by the database or, with a cascade, would
            // quietly destroy the medicines pointing at it.
            _categories.SetActive(_selectedId, false);

            // The status line names the column and states plainly that nothing was removed,
            // because "deactivated" on its own could still be read as a delete.
            lblStatus.Text = "Category deactivated (IsActive = 0). No row was deleted.";
            LoadGrid();     // re-read so the row greys out and the buttons swap over
        }

        private void btnActivate_Click(object sender, EventArgs e)
        {
            if (_selectedId == 0) return;

            // The exact inverse of Deactivate, calling the same method with true. No
            // confirmation, because putting a category back is harmless and reversible by
            // the button next to it. One method for both directions means the statement is
            // written once rather than twice.
            _categories.SetActive(_selectedId, true);
            lblStatus.Text = "Category reactivated.";
            LoadGrid();
        }

        private void btnNew_Click(object sender, EventArgs e) => ClearEditor();

        private void ClearEditor()
        {
            // Back to add mode. Clearing the id is the important line: while it is set, the
            // duplicate check would excuse the previously selected row's name and Save would
            // still be pointed at it.
            _selectedId = 0;

            txtName.Clear();
            txtDescription.Clear();

            // Cleared explicitly rather than relying on the TextChanged handler, because
            // clearing the box fires that handler and its blank branch clears the error
            // anyway. Doing it here as well means the editor is known to be clean whatever
            // order the events arrive in.
            UiTheme.ClearError(lblNameError, txtName);

            // Without this the grid would still be sitting on a row, and the next
            // SelectionChanged would copy it straight back into the editor just cleared.
            dgvCategories.ClearSelection();

            UpdateButtons();    // with no name and no selection, every action button goes off
        }

        // The checkbox has no state of its own to keep: LoadGrid reads it directly, so
        // flipping it simply re-runs the query with the other parameter.
        private void chkShowInactive_CheckedChanged(object sender, EventArgs e) => LoadGrid();

        // Close, not Application.Exit: this form was opened from the Super Admin dashboard
        // and closing it returns there rather than ending the session.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
