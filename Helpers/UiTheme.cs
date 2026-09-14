using System.Drawing;              // Color, Font, Point, Size
using System.Windows.Forms;        // the controls this class styles

namespace PharmaLinkApp.Helpers
{
    /// <summary>
    /// One palette and one set of control styles for the whole application.
    ///
    /// Every form calls into this class instead of picking its own colours, so
    /// the eighteen screens look like one product rather than eighteen separate
    /// student exercises. Changing the brand colour here changes it everywhere.
    /// </summary>
    // Static, like the other two helpers: there is one theme and nothing to construct.
    // The methods come in two shapes, and the names say which is which. StyleX takes a
    // control that already exists and changes it; BuildX creates a control and hands it
    // back for the form to position.
    public static class UiTheme
    {
        // -- palette -----------------------------------------------------------
        // readonly rather than const, because a Color is built by a method call and only
        // a compile time literal can be const. readonly gives the same guarantee that
        // matters here: assigned once, never reassigned by a form.
        //
        // FromArgb takes red, green and blue. Naming each colour by its ROLE rather than
        // by its hue is the point of the whole block: a form asks for Danger, not for a
        // particular red, so every warning in the application is the same red and one
        // edit here restyles all of them.
        public static readonly Color Primary = Color.FromArgb(13, 110, 90);    // PharmaLink green
        public static readonly Color PrimaryDark = Color.FromArgb(9, 80, 66);  // the darker shade for pressed and hovered states
        public static readonly Color Accent = Color.FromArgb(23, 105, 170);    // links and info
        public static readonly Color Danger = Color.FromArgb(178, 42, 42);     // delete, suspend, and validation failures
        public static readonly Color Warning = Color.FromArgb(190, 120, 20);   // commission and anything needing attention
        public static readonly Color Success = Color.FromArgb(28, 128, 72);    // confirmations and net earnings
        public static readonly Color Sidebar = Color.FromArgb(24, 42, 56);     // the dark left menu, and grid headers
        public static readonly Color SidebarHover = Color.FromArgb(38, 62, 80);// a touch lighter, so a menu item lifts under the pointer
        public static readonly Color PageBack = Color.FromArgb(244, 246, 248); // near white, so white cards still read as raised
        public static readonly Color CardBack = Color.White;
        public static readonly Color Border = Color.FromArgb(214, 220, 226);
        public static readonly Color TextDark = Color.FromArgb(28, 36, 44);    // body text: near black, not pure black, which glares
        public static readonly Color TextMuted = Color.FromArgb(105, 118, 130);// captions and secondary lines
        public static readonly Color RowAlt = Color.FromArgb(248, 250, 251);   // the banding on alternate grid rows
        // The two row highlights. They are defined once here and used by name across the
        // screens, which is why a low stock medicine, a suspended user and a one star
        // review are all tinted the same, and a delivered order, an approved prescription
        // and a running offer are all tinted the other.
        public static readonly Color LowStockBack = Color.FromArgb(255, 226, 226);
        public static readonly Color DeliveredBack = Color.FromArgb(226, 246, 232);

        // -- fonts -------------------------------------------------------------
        // Created once and shared by every control that asks for them, rather than a new
        // Font object per control. Segoe UI is the Windows system typeface, so the forms
        // match the operating system instead of announcing a font choice.
        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
        public static readonly Font FontHeading = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        public static readonly Font FontBody = new Font("Segoe UI", 9.75F);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5F);
        public static readonly Font FontTileValue = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
        // Consolas is fixed width. It is used for the labels that quote SQL or explain
        // the isolation rule, so that text reads as code rather than as prose.
        public static readonly Font FontMono = new Font("Consolas", 9.5F);

        // -- form --------------------------------------------------------------
        // Called first in every form's ApplyTheme. Five lines that every screen would
        // otherwise repeat, which is exactly the kind of thing that drifts if copied.
        public static void StyleForm(Form form, string title)
        {
            form.BackColor = PageBack;
            // Setting the font on the FORM rather than on each control: child controls
            // inherit it, so one assignment reaches everything the form contains.
            form.Font = FontBody;
            form.ForeColor = TextDark;
            form.StartPosition = FormStartPosition.CenterScreen;
            // One title bar format for the whole application, so every window announces
            // the product first and the screen second.
            form.Text = "PharmaLink  -  " + title;
        }

        /// <summary>The coloured strip across the top of every screen.</summary>
        public static Panel BuildHeader(string title, string subtitle)
        {
            // A BuildX method: it constructs the control tree and returns the root, so
            // the form only has to add one thing to itself.
            Panel header = new Panel();
            // Docked to the top, so it spans the full width whatever the window is
            // resized to; the height is fixed because the text inside it is.
            header.Dock = DockStyle.Top;
            header.Height = 68;
            header.BackColor = Primary;

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = FontTitle;
            lblTitle.ForeColor = Color.White;      // white on the brand green
            lblTitle.AutoSize = true;              // let the label size itself to the words
            lblTitle.Location = new Point(18, 10);
            // Added to the header, not to the form, so the two move together.
            header.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = subtitle;
            lblSub.Font = FontSmall;
            // A pale tint of the header colour rather than plain white, so the subtitle
            // sits behind the title in the reading order without becoming hard to read.
            lblSub.ForeColor = Color.FromArgb(200, 230, 220);
            lblSub.AutoSize = true;
            lblSub.Location = new Point(21, 40);
            header.Controls.Add(lblSub);

            return header;
        }

        // -- buttons -----------------------------------------------------------
        // Five named button styles, each one a single call into StyleFlat below. A form
        // therefore chooses a button by MEANING - primary, danger, accent - and never
        // picks a colour, which is what keeps the same action looking the same
        // everywhere. Save is green, delete is red, on every screen, without agreement.
        public static void StylePrimary(Button button)
        {
            StyleFlat(button, Primary, Color.White);
        }

        public static void StyleSecondary(Button button)
        {
            // The quiet one, for Cancel and Close. White with a thin border rather than a
            // filled block, so it cannot compete with the primary action beside it.
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

        // private, because no form should call it directly: the five public methods above
        // are the vocabulary, and this is the one implementation they share.
        private static void StyleFlat(Button button, Color back, Color fore)
        {
            // Flat, with no border, instead of the default Windows chrome. That is what
            // makes the buttons look like one designed set rather than system buttons.
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Font = FontBody;
            button.Cursor = Cursors.Hand;          // the pointer says "this is clickable"
            // Without this, Windows ignores BackColor and paints its own theme; it is the
            // one line that makes every colour above actually appear on a button.
            button.UseVisualStyleBackColor = false;
        }

        /// <summary>A left menu button, used on all three dashboards.</summary>
        public static void StyleSidebarButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            // The hover colour is handed to WinForms rather than being painted by hand in
            // a MouseEnter event, so the effect costs one line and no event wiring.
            button.FlatAppearance.MouseOverBackColor = SidebarHover;
            button.BackColor = Sidebar;
            button.ForeColor = Color.White;
            button.Font = FontBody;
            // Left aligned with a left pad, so the menu reads as a vertical list of
            // labels rather than a column of centred captions.
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
            // Required before the header colours below have any effect: with visual
            // styles on, Windows paints the headers itself and ignores them.
            grid.EnableHeadersVisualStyles = false;

            // The next five lines turn the grid into a read only display. Every grid in
            // the application shows data that is edited through a form and saved by a
            // service, so an editable cell could only ever produce a change that is never
            // written, and the blank new row at the bottom would just be confusing.
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            // One row at a time, because every action button acts on one selected record.
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;        // the grey stub column carries nothing useful
            // Selecting any cell selects the whole row, so the record is the unit of
            // selection rather than the cell, which is what the buttons expect.
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            // Columns share the width instead of being sized to their content, so a grid
            // fills its panel with no horizontal scrollbar.
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersHeight = 36;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            // Headers in the sidebar colour, which ties the grid to the menu beside it.
            grid.ColumnHeadersDefaultCellStyle.BackColor = Sidebar;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            // The same colour for the selected state, so clicking a column heading does
            // not make it flash a different colour.
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Sidebar;

            grid.DefaultCellStyle.Font = FontBody;
            grid.DefaultCellStyle.ForeColor = TextDark;
            // A pale green selection with dark text, rather than the default deep blue
            // with white text. The row stays readable while selected, which matters
            // because the forms read values out of the row the user is looking at.
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 234, 228);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            // Banding, so the eye can follow a long row across the screen.
            grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
            grid.RowTemplate.Height = 30;
        }

        // -- cards and tiles ---------------------------------------------------
        // A white panel on the near white page, used to group related controls. Padding
        // keeps the contents off the edge; the single line border defines where it ends.
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
            // The four KPI tiles on the dashboards, the inventory screen and both
            // earnings screens are all built here, which is why they line up and read
            // alike across the application. The stripe colour is the only thing that
            // varies, and it carries the meaning: Warning for commission, Danger for low
            // stock, Success for net earnings.
            //
            // An OUT PARAMETER is used because a method returns one value and this one
            // has two things to give back: the panel to position, and the label to
            // rewrite when the figures are reloaded. Without it the form would have to
            // dig the label back out of the panel's control collection by index.
            Panel tile = new Panel();
            tile.BackColor = CardBack;
            tile.BorderStyle = BorderStyle.FixedSingle;
            tile.Size = new Size(210, 84);         // fixed, so four tiles sit in a neat row

            // The coloured spine down the left edge. Docked, so it stretches the full
            // height of the tile whatever else is placed inside.
            Panel bar = new Panel();
            bar.Dock = DockStyle.Left;
            bar.Width = 5;
            bar.BackColor = stripe;
            tile.Controls.Add(bar);

            // The caption: small and muted, because the number is what the eye should
            // land on first.
            Label lblCaption = new Label();
            lblCaption.Text = caption;
            lblCaption.Font = FontSmall;
            lblCaption.ForeColor = TextMuted;
            lblCaption.AutoSize = true;
            lblCaption.Location = new Point(16, 12);
            tile.Controls.Add(lblCaption);

            // Assigning the out parameter is what hands the label back to the caller. It
            // starts at "0" so a tile is never blank while its figures are loading.
            valueLabel = new Label();
            valueLabel.Text = "0";
            valueLabel.Font = FontTileValue;
            valueLabel.ForeColor = TextDark;
            valueLabel.AutoSize = true;
            valueLabel.Location = new Point(14, 34);
            tile.Controls.Add(valueLabel);

            return tile;
        }

        /// <summary>
        /// Adds a tile using the same 7x15 design coordinates as the designer file.
        /// Tiles are built in Load, after WinForms has already scaled the designer
        /// controls for the screen DPI and the form font, so raw pixel positions
        /// would land on top of the sidebar and header. Scaling the tile by the
        /// same factor keeps it lined up with everything else.
        /// </summary>
        public static void PlaceTile(Form form, Panel tile, int x, int y, int width, int height)
        {
            form.Controls.Add(tile);
            tile.Bounds = new Rectangle(x, y, width, height);

            SizeF current = form.CurrentAutoScaleDimensions;
            tile.Scale(new SizeF(current.Width / 7F, current.Height / 15F));
            tile.BringToFront();
        }

        // -- validation labels -------------------------------------------------
        /// <summary>Shows a red message under a field and outlines the field in red.</summary>
        public static void ShowError(Label errorLabel, Control field, string message)
        {
            // The other half of the Validator rules: a rule returns false and this is what
            // the user actually sees. Two signals at once, the message and the tinted box,
            // so a form with several errors shows which fields they belong to.
            errorLabel.Text = message;
            errorLabel.ForeColor = Danger;
            errorLabel.Visible = true;
            // Typed as Control, not TextBox, so the same method works for a combo box or a
            // date picker; null is allowed for the rules that belong to no single field.
            if (field != null)
            {
                field.BackColor = Color.FromArgb(255, 240, 240);
            }
        }

        // The exact inverse, called before re-validating so a message that has been fixed
        // disappears. Hidden rather than emptied as well, so the label takes no space.
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
            // AutoSize off and a fixed size, unlike the labels in BuildHeader: an error
            // label must occupy the same space whatever text lands in it, or the controls
            // below it would shift as messages appear and vanish.
            label.AutoSize = false;
            label.Size = new Size(width, 18);
            label.Location = location;
            label.Font = FontSmall;
            label.ForeColor = Danger;
            label.Visible = false;                 // created hidden; ShowError reveals it
            return label;
        }

        // -- money -------------------------------------------------------------
        public static string Money(decimal amount)
        {
            // Every amount on every screen goes through this one method, so the currency
            // and the number of decimal places are decided in a single place. "N2" is the
            // numeric format: thousands separators and exactly two decimals, so 1234.5
            // prints as Tk 1,234.50 rather than Tk 1234.5.
            return "Tk " + amount.ToString("N2");
        }
    }
}
