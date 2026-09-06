using System.Data;
using System.Drawing;
using System.Windows.Forms;
using PharmaLinkApp.Helpers;
using PharmaLinkApp.Services;

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
        private readonly CategoryService _categories = new CategoryService();
        private int _selectedId;

        public ManageCategoriesForm()
        {
            InitializeComponent();
        }

        private void ManageCategoriesForm_Load(object sender, EventArgs e)
        {
            ApplyTheme();
            LoadGrid();
        }

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
                DataTable table = _categories.GetTable(!chkShowInactive.Checked);
                dgvCategories.DataSource = table;

                if (dgvCategories.Columns.Count > 0)
                {
                    dgvCategories.Columns["CategoryId"].HeaderText = "ID";
                    dgvCategories.Columns["CategoryId"].FillWeight = 25;
                    dgvCategories.Columns["CategoryName"].HeaderText = "Category";
                    dgvCategories.Columns["CategoryName"].FillWeight = 70;
                    dgvCategories.Columns["Description"].HeaderText = "Description";
                    dgvCategories.Columns["Description"].FillWeight = 130;
                    dgvCategories.Columns["IsActive"].HeaderText = "Active";
                    dgvCategories.Columns["IsActive"].FillWeight = 35;
                    dgvCategories.Columns["MedicineCount"].HeaderText = "Medicines";
                    dgvCategories.Columns["MedicineCount"].FillWeight = 45;
                }

                lblStatus.Text = table.Rows.Count + " categor" + (table.Rows.Count == 1 ? "y" : "ies") + " listed.";
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "PharmaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvCategories_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvCategories.Columns.Count == 0) return;

            DataGridViewRow row = dgvCategories.Rows[e.RowIndex];
            object value = row.Cells["IsActive"].Value;
            if (value == null || value == DBNull.Value) return;

            bool active = Convert.ToBoolean(value);
            row.DefaultCellStyle.ForeColor = active ? UiTheme.TextDark : UiTheme.TextMuted;
            row.DefaultCellStyle.BackColor = active ? Color.White : Color.FromArgb(240, 240, 240);
        }

        private void dgvCategories_SelectionChanged(object sender, EventArgs e)
        {
            DataGridViewRow row = dgvCategories.CurrentRow;
            if (row == null || row.Cells["CategoryId"].Value == null) return;

            _selectedId = Convert.ToInt32(row.Cells["CategoryId"].Value);
            txtName.Text = row.Cells["CategoryName"].Value.ToString();
            txtDescription.Text = row.Cells["Description"].Value == DBNull.Value
                ? "" : row.Cells["Description"].Value.ToString();

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasSelection = _selectedId > 0;
            bool nameOk = !Validator.IsBlank(txtName.Text);

            btnAdd.Enabled = nameOk;
            btnUpdate.Enabled = hasSelection && nameOk;

            if (hasSelection && dgvCategories.CurrentRow != null &&
                dgvCategories.CurrentRow.Cells["IsActive"].Value != DBNull.Value)
            {
                bool active = Convert.ToBoolean(dgvCategories.CurrentRow.Cells["IsActive"].Value);
                btnDeactivate.Enabled = active;
                btnActivate.Enabled = !active;
            }
            else
            {
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
            if (Validator.IsBlank(txtName.Text))
            {
                UiTheme.ClearError(lblNameError, txtName);
            }
            else if (_categories.NameExists(txtName.Text, _selectedId))
            {
                UiTheme.ShowError(lblNameError, txtName,
                    "A category called '" + txtName.Text.Trim() + "' already exists (UQ_Categories_Name).");
            }
            else
            {
                UiTheme.ClearError(lblNameError, txtName);
            }

            UpdateButtons();
        }

        private bool NameIsUsable(int ignoreId)
        {
            if (Validator.IsBlank(txtName.Text))
            {
                UiTheme.ShowError(lblNameError, txtName, "The category name cannot be empty.");
                return false;
            }

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
            if (!NameIsUsable(0)) return;

            _categories.Add(txtName.Text, txtDescription.Text);
            lblStatus.Text = "Category '" + txtName.Text.Trim() + "' added.";
            ClearEditor();
            LoadGrid();
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            if (_selectedId == 0) return;
            if (!NameIsUsable(_selectedId)) return;

            _categories.Update(_selectedId, txtName.Text, txtDescription.Text);
            lblStatus.Text = "Category saved. Every medicine pointing at it now shows the new name.";
            LoadGrid();
        }

        private void btnDeactivate_Click(object sender, EventArgs e)
        {
            if (_selectedId == 0) return;

            bool referenced = _categories.IsReferenced(_selectedId);
            string extra = referenced
                ? "\r\n\r\nMedicines already point at this category, so it can only be deactivated, never deleted."
                : "";

            DialogResult answer = MessageBox.Show(
                "Deactivate '" + txtName.Text.Trim() + "'?\r\n\r\n" +
                "It disappears from every category dropdown but existing medicines keep working." + extra,
                "Deactivate category", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            _categories.SetActive(_selectedId, false);
            lblStatus.Text = "Category deactivated (IsActive = 0). No row was deleted.";
            LoadGrid();
        }

        private void btnActivate_Click(object sender, EventArgs e)
        {
            if (_selectedId == 0) return;
            _categories.SetActive(_selectedId, true);
            lblStatus.Text = "Category reactivated.";
            LoadGrid();
        }

        private void btnNew_Click(object sender, EventArgs e) => ClearEditor();

        private void ClearEditor()
        {
            _selectedId = 0;
            txtName.Clear();
            txtDescription.Clear();
            UiTheme.ClearError(lblNameError, txtName);
            dgvCategories.ClearSelection();
            UpdateButtons();
        }

        private void chkShowInactive_CheckedChanged(object sender, EventArgs e) => LoadGrid();
        private void btnBack_Click(object sender, EventArgs e) => Close();
    }
}
