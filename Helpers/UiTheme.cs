using System.Drawing;
using System.Windows.Forms;

namespace PharmaLinkApp.Helpers
{
    /// <summary>
    /// One palette and one set of control styles for the whole application.
    ///
    /// Every form calls into this class instead of picking its own colours, so
    /// the eighteen screens look like one product rather than eighteen separate
    /// student exercises. Changing the brand colour here changes it everywhere.
    /// </summary>
    public static class UiTheme
    {
        // -- palette -----------------------------------------------------------
        public static readonly Color Primary = Color.FromArgb(13, 110, 90);    // PharmaLink green
        public static readonly Color PrimaryDark = Color.FromArgb(9, 80, 66);
        public static readonly Color Accent = Color.FromArgb(23, 105, 170);    // links and info
        public static readonly Color Danger = Color.FromArgb(178, 42, 42);
        public static readonly Color Warning = Color.FromArgb(190, 120, 20);
        public static readonly Color Success = Color.FromArgb(28, 128, 72);
        public static readonly Color Sidebar = Color.FromArgb(24, 42, 56);
        public static readonly Color SidebarHover = Color.FromArgb(38, 62, 80);
        public static readonly Color PageBack = Color.FromArgb(244, 246, 248);
        public static readonly Color CardBack = Color.White;
        public static readonly Color Border = Color.FromArgb(214, 220, 226);
        public static readonly Color TextDark = Color.FromArgb(28, 36, 44);
        public static readonly Color TextMuted = Color.FromArgb(105, 118, 130);
        public static readonly Color RowAlt = Color.FromArgb(248, 250, 251);
        public static readonly Color LowStockBack = Color.FromArgb(255, 226, 226);
        public static readonly Color DeliveredBack = Color.FromArgb(226, 246, 232);

        // -- fonts -------------------------------------------------------------
        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
        public static readonly Font FontHeading = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        public static readonly Font FontBody = new Font("Segoe UI", 9.75F);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5F);
        public static readonly Font FontTileValue = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 9.5F);

        // -- form --------------------------------------------------------------
        public static void StyleForm(Form form, string title)
        {
            form.BackColor = PageBack;
            form.Font = FontBody;
            form.ForeColor = TextDark;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Text = "PharmaLink  -  " + title;
        }

        /// <summary>The coloured strip across the top of every screen.</summary>
        public static Panel BuildHeader(string title, string subtitle)
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 68;
            header.BackColor = Primary;

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = FontTitle;
            lblTitle.ForeColor = Color.White;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(18, 10);
            header.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = subtitle;
            lblSub.Font = FontSmall;
            lblSub.ForeColor = Color.FromArgb(200, 230, 220);
            lblSub.AutoSize = true;
            lblSub.Location = new Point(21, 40);
            header.Controls.Add(lblSub);

            return header;
        }

        // -- buttons -----------------------------------------------------------
        public static void StylePrimary(Button button)
        {
            StyleFlat(button, Primary, Color.White);
        }

        public static void StyleSecondary(Button button)
        {
            StyleFlat(button, Color.White, TextDark);
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
        }

        public static void StyleDanger(Button button)
        {
            StyleFlat(button, Danger, Color.White);
        }

        public static void StyleAccent(Button button)
        {
            StyleFlat(button, Accent, Color.White);
        }

        public static void StyleSuccess(Button button)
        {
            StyleFlat(button, Success, Color.White);
        }

        private static void StyleFlat(Button button, Color back, Color fore)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Font = FontBody;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        /// <summary>A left menu button, used on all three dashboards.</summary>
        public static void StyleSidebarButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = SidebarHover;
            button.BackColor = Sidebar;
            button.ForeColor = Color.White;
            button.Font = FontBody;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(16, 0, 0, 0);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        // -- grids -------------------------------------------------------------
        /// <summary>The single grid style used by every DataGridView in the application.</summary>
        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = CardBack;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersHeight = 36;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            grid.ColumnHeadersDefaultCellStyle.BackColor = Sidebar;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Sidebar;

            grid.DefaultCellStyle.Font = FontBody;
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 234, 228);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
            grid.RowTemplate.Height = 30;
        }

        // -- cards and tiles ---------------------------------------------------
        public static Panel BuildCard()
        {
            Panel card = new Panel();
            card.BackColor = CardBack;
            card.Padding = new Padding(14);
            card.BorderStyle = BorderStyle.FixedSingle;
            return card;
        }

        /// <summary>
        /// A summary tile: caption on top, big number underneath.
        /// The Label holding the number is returned in valueLabel so the form can
        /// refresh it without rebuilding the tile.
        /// </summary>
        public static Panel BuildTile(string caption, Color stripe, out Label valueLabel)
        {
            Panel tile = new Panel();
            tile.BackColor = CardBack;
            tile.BorderStyle = BorderStyle.FixedSingle;
            tile.Size = new Size(210, 84);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Left;
            bar.Width = 5;
            bar.BackColor = stripe;
            tile.Controls.Add(bar);

            Label lblCaption = new Label();
            lblCaption.Text = caption;
            lblCaption.Font = FontSmall;
            lblCaption.ForeColor = TextMuted;
            lblCaption.AutoSize = true;
            lblCaption.Location = new Point(16, 12);
            tile.Controls.Add(lblCaption);

            valueLabel = new Label();
            valueLabel.Text = "0";
            valueLabel.Font = FontTileValue;
            valueLabel.ForeColor = TextDark;
            valueLabel.AutoSize = true;
            valueLabel.Location = new Point(14, 34);
            tile.Controls.Add(valueLabel);

            return tile;
        }

        // -- validation labels -------------------------------------------------
        /// <summary>Shows a red message under a field and outlines the field in red.</summary>
        public static void ShowError(Label errorLabel, Control field, string message)
        {
            errorLabel.Text = message;
            errorLabel.ForeColor = Danger;
            errorLabel.Visible = true;
            if (field != null)
            {
                field.BackColor = Color.FromArgb(255, 240, 240);
            }
        }

        public static void ClearError(Label errorLabel, Control field)
        {
            errorLabel.Text = string.Empty;
            errorLabel.Visible = false;
            if (field != null)
            {
                field.BackColor = Color.White;
            }
        }

        public static Label BuildErrorLabel(Point location, int width)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Size = new Size(width, 18);
            label.Location = location;
            label.Font = FontSmall;
            label.ForeColor = Danger;
            label.Visible = false;
            return label;
        }

        // -- money -------------------------------------------------------------
        public static string Money(decimal amount)
        {
            return "Tk " + amount.ToString("N2");
        }
    }
}
