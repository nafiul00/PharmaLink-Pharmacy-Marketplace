using System.Data;                  // DataTable, the shape a read returns
using System.Drawing;               // Color and Font, used by the theming
using System.Windows.Forms;         // Form, DataGridView, MessageBox, DialogResult
using PharmaLinkApp.Helpers;        // UiTheme paints the errors, Validator tests blanks
using PharmaLinkApp.Services;       // CategoryService, the only class here that hits SQL

// Every screen lives in this one namespace, so any dashboard can open any form.
namespace PharmaLinkApp.Forms
{
    /// <summary>Requirement 7. Category list with soft delete.</summary>
    public partial class ManageCategoriesForm : Form
    {
        // One service for the life of the form; DbHelper opens a connection per call.
        private readonly CategoryService _categories = new CategoryService();

        // Selected row id, 0 in add mode, and the id the duplicate check must ignore.
        private int _selectedId;

        // Kept empty on purpose - a throw here leaves no window to show the error in.
        public ManageCategoriesForm()
        {
            InitializeComponent();      // build the controls from the Designer file first
        }

        // Load fires after the handle exists, so every query is reached from here.
        private void ManageCategoriesForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();       // colours and fonts only, no data
            LoadGrid();         // the one read that populates the screen
        }

        // Colours, fonts and grid styling. Nothing here reads or writes data.
        private void ApplyTheme()
        {
            UiTheme.StyleForm(this, "Medicine Categories");   // title and background

            panelHeader.BackColor = UiTheme.Primary;          // the green band on top
            lblTitle.Font = UiTheme.FontTitle;                // the largest theme font
            lblTitle.ForeColor = Color.White;                 // white on the dark band
            lblSubtitle.Font = UiTheme.FontSmall;             // smaller, so the two pair up
            lblSubtitle.ForeColor = Color.FromArgb(200, 230, 220);   // pale tint of Primary

            grpEditor.Font = UiTheme.FontHeading;             // styles the caption only
            grpEditor.ForeColor = UiTheme.Primary;            // tints the caption of the editor
            grpEditor.BackColor = UiTheme.CardBack;           // off white card behind it
            // Looped so a control added to the editor later is themed without an edit here.
            foreach (Control child in grpEditor.Controls)
            {
                child.Font = UiTheme.FontBody;                // the ordinary reading font
                child.ForeColor = UiTheme.TextDark;           // near black, not harsh black
            }

            lblNameError.Font = UiTheme.FontSmall;            // quieter than the field above
            lblNameError.ForeColor = UiTheme.Danger;          // red is kept for blockers
            lblEditorNote.Font = UiTheme.FontSmall;           // hint text, same size as above
            lblEditorNote.ForeColor = UiTheme.TextMuted;      // grey, never competes with red
            lblStatus.Font = UiTheme.FontSmall;               // the outcome line at the foot
            lblStatus.ForeColor = UiTheme.TextMuted;          // muted, it only reports

            UiTheme.StyleSecondary(btnBack);                  // grey: changes nothing
            UiTheme.StyleSuccess(btnAdd);                     // green: creates a row
            UiTheme.StylePrimary(btnUpdate);                  // brand colour: amends a row
            UiTheme.StyleDanger(btnDeactivate);               // red, though it deletes nothing
            UiTheme.StyleAccent(btnActivate);                 // accent: the red button's twin
            UiTheme.StyleSecondary(btnNew);                   // grey: it only clears the editor
            UiTheme.StyleGrid(dgvCategories);                 // one grid style for the app
            dgvCategories.CellFormatting += dgvCategories_CellFormatting;   // wired here, not in the Designer
        }

        // The single read path; every action re-runs it instead of patching the grid.
        private void LoadGrid()
        {
            // Wrapped here because this is where the connection is actually used.
            try
            {
                // Inverted on purpose: "show inactive" ticked means activeOnly false.
                DataTable table = _categories.GetTable(!chkShowInactive.Checked);

                // Bound straight to the grid, so the columns are the SELECT list.
                dgvCategories.DataSource = table;

                // Guarded: a failed bind leaves no columns and every lookup below throws.
                if (dgvCategories.Columns.Count > 0)
                {
                    // FillWeight is a proportion, so relative widths survive a resize.
                    dgvCategories.Columns["CategoryId"].HeaderText = "ID";
                    dgvCategories.Columns["CategoryId"].FillWeight = 25;        // an id is a few digits
                    dgvCategories.Columns["CategoryName"].HeaderText = "Category";       // the raw name reads as one word
                    dgvCategories.Columns["CategoryName"].FillWeight = 70;      // a name is a few words
                    dgvCategories.Columns["Description"].HeaderText = "Description";     // renamed anyway, all headers in one place
                    dgvCategories.Columns["Description"].FillWeight = 130;      // the only free text column
                    dgvCategories.Columns["IsActive"].HeaderText = "Active";    // the flag the soft delete rests on
                    dgvCategories.Columns["IsActive"].FillWeight = 35;          // a checkbox and its heading

                    // A correlated subquery in the SELECT, not a count done in C#.
                    dgvCategories.Columns["MedicineCount"].HeaderText = "Medicines";
                    dgvCategories.Columns["MedicineCount"].FillWeight = 45;     // a count needs little room
                }

                // The singular is handled, or a finished screen prints "1 categories".
                lblStatus.Text = table.Rows.Count + " categor" + (table.Rows.Count == 1 ? "y" : "ies") + " listed.";

                // The grid was just rebound, so the row buttons must be decided again.
                UpdateButtons();
            }
            // ex carries the sentence DbHelper already made readable.
            catch (Exception ex)
            {
                // Reported, not rethrown: a transient fault must not close the screen.
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Runs per cell while painting, so it stays cheap: no query, no allocation.
        private void dgvCategories_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The header row has a negative index, and a rebind can leave no columns.
            if (e.RowIndex < 0 || dgvCategories.Columns.Count == 0) return;

            DataGridViewRow row = dgvCategories.Rows[e.RowIndex];      // reached by index, not CurrentRow
            object value = row.Cells["IsActive"].Value;                // object, because it can be DBNull

            // Two empties: null is the grid's new-row placeholder, DBNull is a database null.
            if (value == null || value == DBNull.Value) return;

            bool active = Convert.ToBoolean(value);   // Convert, not a cast: BIT boxes oddly

            // Greyed rather than hidden: the row still exists and medicines point at it.
            row.DefaultCellStyle.ForeColor = active ? UiTheme.TextDark : UiTheme.TextMuted;
            row.DefaultCellStyle.BackColor = active ? Color.White : Color.FromArgb(240, 240, 240);   // the white branch resets reused rows
        }

        // Copies the selected row in, the only place _selectedId gets a real id.
        private void dgvCategories_SelectionChanged(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvCategories.CurrentRow;   // null between a rebind and a pick

            // No row, or the blank placeholder: leave the editor and the id untouched.
            if (row == null || row.Cells["CategoryId"].Value == null) return;

            // Remembering the id is what switches the editor from add mode to edit mode.
            _selectedId = Convert.ToInt32(row.Cells["CategoryId"].Value);

            // Copy the row in so the name is amended rather than retyped.
            txtName.Text = row.Cells["CategoryName"].Value.ToString();

            // Description is nullable, and ToString on DBNull prints "System.DBNull".
            txtDescription.Text = row.Cells["Description"].Value == DBNull.Value
                ? "" : row.Cells["Description"].Value.ToString();   // the ternary is the null check

            // Assigning txtName already fired TextChanged; this settles the status buttons.
            UpdateButtons();
        }

        // The one place every button's enabled state is decided, from all three callers.
        private void UpdateButtons()
        {
            bool hasSelection = _selectedId > 0;                 // 0 is the add-mode sentinel
            bool nameOk = !Validator.IsBlank(txtName.Text);      // IsBlank also rejects spaces

            // Add needs a name; Save needs a name and a row, since it amends an existing one.
            btnAdd.Enabled = nameOk;
            btnUpdate.Enabled = hasSelection && nameOk;          // Save has nothing to write to

            // Read from the selected row, not a field, so this matches the database.
            if (hasSelection && dgvCategories.CurrentRow != null &&
                dgvCategories.CurrentRow.Cells["IsActive"].Value != DBNull.Value)   // && proves CurrentRow first
            {
                bool active = Convert.ToBoolean(dgvCategories.CurrentRow.Cells["IsActive"].Value);   // read fresh, never cached

                // Exactly one of the pair is ever available, so no action is a no-op.
                btnDeactivate.Enabled = active;
                btnActivate.Enabled = !active;    // the exact inverse of the line above
            }
            // No row, or a flag that could not be read: both buttons go off together.
            else
            {
                // Nothing selected, so neither button has a row to act on.
                btnDeactivate.Enabled = false;
                btnActivate.Enabled = false;      // set explicitly, or the last state persists
            }
        }

        // Validation: a live hint while typing, and a hard gate in front of every write.

        // Live feedback only. It refuses nothing; NameIsUsable below is the real gate.
        private void txtName_TextChanged(object sender, EventArgs e)
        {
            // An empty box clears the error: nagging a half typed field reads as broken.
            if (Validator.IsBlank(txtName.Text))
            {
                UiTheme.ClearError(lblNameError, txtName);   // blanks the label, resets the box
            }
            // _selectedId is the id to IGNORE, so editing is not blocked by its own name.
            else if (_categories.NameExists(txtName.Text, _selectedId))
            {
                // Written inline because the message has to quote what the user typed.
                UiTheme.ShowError(lblNameError, txtName,
                    "A category called '" + txtName.Text.Trim() + "' already exists (UQ_Categories_Name).");   // Trim matches what is sent
            }
            // Neither blank nor taken, the only state Add and Save will accept.
            else
            {
                // Clearing matters as much as showing, or a stale warning would sit there.
                UiTheme.ClearError(lblNameError, txtName);
            }

            // The buttons follow the name, so they are re-decided on every keystroke.
            UpdateButtons();
        }

        // Returns a verdict, and is the last thing between the user and a write.
        private bool NameIsUsable(int ignoreId)
        {
            // Both rules are re-run here: the selection may have changed since typing.
            if (Validator.IsBlank(txtName.Text))
            {
                UiTheme.ShowError(lblNameError, txtName, "The category name cannot be empty.");   // suppressed until now
                return false;   // stops the caller before a parameter is built
            }

            // ignoreId comes from the CALLER, so Add passes 0 and Save passes its row id.
            if (_categories.NameExists(txtName.Text, ignoreId))
            {
                UiTheme.ShowError(lblNameError, txtName,                                                       // same wording as the live check
                    "A category called '" + txtName.Text.Trim() + "' already exists (UQ_Categories_Name).");   // names where the rule really lives
                return false;   // refused here, so the UNIQUE violation is never caught
            }

            UiTheme.ClearError(lblNameError, txtName);   // clears what an earlier attempt left
            return true;                                 // the only path that allows a write
        }

        // ---------------------------------------------------------------------

        // Creates a category. The only handler that passes 0 as the id to ignore.
        private void btnAdd_Click(object sender, EventArgs e)
        {
            // 0, because a new category has no row of its own to excuse.
            if (!NameIsUsable(0)) return;

            // Untrimmed: the service trims once, so the rule lives in a single place.
            _categories.Add(txtName.Text, txtDescription.Text);
            lblStatus.Text = "Category '" + txtName.Text.Trim() + "' added.";   // Trim for display only

            // Cleared, so pressing Add twice cannot meet the UNIQUE constraint.
            ClearEditor();
            LoadGrid();     // re-read, so the grid shows what the database now holds
        }

        // Amends the selected category. Add's mirror, except for which id it excuses.
        private void btnUpdate_Click(object sender, EventArgs e)
        {
            // Checked even though the button is disabled: a shortcut can still click it.
            if (_selectedId == 0) return;

            // _selectedId as the id to IGNORE, or no edit could ever save unchanged.
            if (!NameIsUsable(_selectedId)) return;

            _categories.Update(_selectedId, txtName.Text, txtDescription.Text);   // id picks the row

            // Medicines store a CategoryId, so one rename reaches all of them at once.
            lblStatus.Text = "Category saved. Every medicine pointing at it now shows the new name.";

            // Not cleared, unlike Add: the row stays selected so the edit can be seen.
            LoadGrid();
        }

        // The soft delete. It asks first, and the question is built from a live query.
        private void btnDeactivate_Click(object sender, EventArgs e)
        {
            if (_selectedId == 0) return;   // the disabled button is a courtesy, this is the guarantee

            // Asked before the dialog, so the warning is true of this category.
            bool referenced = _categories.IsReferenced(_selectedId);

            // A separate string, so the question reads naturally when nothing points at it.
            string extra = referenced
                ? "\r\n\r\nMedicines already point at this category, so it can only be deactivated, never deleted."   // blank lines separate it
                : "";                                                                                                 // ends where it was

            // The wording says what does NOT happen: no row is removed, no key breaks.
            DialogResult answer = MessageBox.Show(
                "Deactivate '" + txtName.Text.Trim() + "'?\r\n\r\n" +                                           // names the category
                "It disappears from every category dropdown but existing medicines keep working." + extra,      // the extra clause may be empty
                "Deactivate category", MessageBoxButtons.YesNo, MessageBoxIcon.Question);                       // YesNo, the caption is a question

            // Anything but Yes, closing the box included, means do nothing.
            if (answer != DialogResult.Yes) return;

            // UPDATE IsActive = 0, never a DELETE: FK_Medicines_Category has no cascade.
            _categories.SetActive(_selectedId, false);

            // Names the column and says plainly that nothing was removed.
            lblStatus.Text = "Category deactivated (IsActive = 0). No row was deleted.";
            LoadGrid();     // re-read so the row greys out and the buttons swap over
        }

        // Putting a category back: one flag flip, so deliberately the shortest handler.
        private void btnActivate_Click(object sender, EventArgs e)
        {
            if (_selectedId == 0) return;   // every write path starts from a proven id

            // The exact inverse of Deactivate, one method for both directions.
            _categories.SetActive(_selectedId, true);
            lblStatus.Text = "Category reactivated.";   // short, nothing was ever destroyed
            LoadGrid();                                 // the grey lifts, the buttons swap
        }

        // Expression bodied, because New only resets the editor and touches no data.
        private void btnNew_Click(object sender, EventArgs e) => ClearEditor();

        // Returns the form to add mode, so "clean editor" has one definition.
        private void ClearEditor()
        {
            // The important line: while the id is set, Save would still point at that row.
            _selectedId = 0;

            txtName.Clear();          // Clear, not "", so TextChanged fires and buttons follow
            txtDescription.Clear();   // a stale description must not reach the next Add

            // Done here as well as in TextChanged, so the editor is clean in any event order.
            UiTheme.ClearError(lblNameError, txtName);

            // Without this the next SelectionChanged copies the old row straight back.
            dgvCategories.ClearSelection();

            UpdateButtons();    // no name and no selection, so every action button goes off
        }

        // The checkbox keeps no state: LoadGrid reads it, so flipping it re-queries.
        private void chkShowInactive_CheckedChanged(object sender, EventArgs e) => LoadGrid();

        // Close, not Application.Exit: this returns to the dashboard that opened it.
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
