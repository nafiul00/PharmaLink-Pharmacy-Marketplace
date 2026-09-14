using System.Drawing;              // Color, Font, Point, Size
using System.Windows.Forms;        // the controls this class styles

// Helpers holds what belongs to no one screen: theme, validation, session.
namespace PharmaLinkApp.Helpers
{
    /// <summary>One palette and one set of control styles for the app.</summary>
    public static class UiTheme            // static: one theme, nothing to construct
    {
        // -- palette: each colour named by its role, so a form asks for Danger not a red
        public static readonly Color Primary = Color.FromArgb(13, 110, 90);    // headers and primary buttons
        public static readonly Color PrimaryDark = Color.FromArgb(9, 80, 66);  // pressed and hovered states
        public static readonly Color Accent = Color.FromArgb(23, 105, 170);    // links and info
        public static readonly Color Danger = Color.FromArgb(178, 42, 42);     // delete, suspend, validation failures
        public static readonly Color Warning = Color.FromArgb(190, 120, 20);   // commission and anything needing attention
        public static readonly Color Success = Color.FromArgb(28, 128, 72);    // confirmations and net earnings
        public static readonly Color Sidebar = Color.FromArgb(24, 42, 56);     // the dark left menu, and grid headers
        public static readonly Color SidebarHover = Color.FromArgb(38, 62, 80); // only the item under the pointer
        public static readonly Color PageBack = Color.FromArgb(244, 246, 248); // near white, so white cards read as raised
        public static readonly Color CardBack = Color.White;                   // the fill of every card, tile and grid
        public static readonly Color Border = Color.FromArgb(214, 220, 226);   // hairline round a card and between cells
        public static readonly Color TextDark = Color.FromArgb(28, 36, 44);    // body text: near black, not pure black
        public static readonly Color TextMuted = Color.FromArgb(105, 118, 130); // captions and secondary lines
        public static readonly Color RowAlt = Color.FromArgb(248, 250, 251);   // the banding on alternate grid rows
        public static readonly Color LowStockBack = Color.FromArgb(255, 226, 226);  // pale red: the needs-attention row
        public static readonly Color DeliveredBack = Color.FromArgb(226, 246, 232); // pale green: the settled row

        // -- fonts: built once and shared, rather than a new Font object per control
        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);    // the page title
        public static readonly Font FontHeading = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);   // section headings inside a card
        public static readonly Font FontBody = new Font("Segoe UI", 9.75F);                            // the default, set on the FORM so children inherit it
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5F);                            // subtitles, tile captions and error labels
        public static readonly Font FontTileValue = new Font("Segoe UI Semibold", 18F, FontStyle.Bold); // the big number on a summary tile
        public static readonly Font FontMono = new Font("Consolas", 9.5F);                             // fixed width, so quoted SQL reads as code

        // -- form: the five lines every screen would otherwise repeat for itself
        public static void StyleForm(Form form, string title)
        {
            form.BackColor = PageBack;             // the near white page, so cards and grids read as raised
            form.Font = FontBody;                  // set on the FORM, so every child control inherits it
            form.ForeColor = TextDark;             // inherited the same way, so no label sets its own colour
            form.StartPosition = FormStartPosition.CenterScreen;   // always centred, not wherever Windows last was
            form.Text = "PharmaLink  -  " + title; // one title bar format: product first, screen second
        }

        /// <summary>The coloured strip across the top of every screen.</summary>
        public static Panel BuildHeader(string title, string subtitle)
        {
            Panel header = new Panel();            // a BuildX method: builds the tree and returns the root
            header.Dock = DockStyle.Top;           // spans the full width however the window is resized
            header.Height = 68;                    // tall enough for the title and the subtitle under it
            header.BackColor = Primary;            // the brand green is what identifies this as the header

            Label lblTitle = new Label();          // the screen name, the first thing read on the page
            lblTitle.Text = title;                 // supplied by the caller, so this method knows no screens
            lblTitle.Font = FontTitle;             // the largest size in the palette, used only here
            lblTitle.ForeColor = Color.White;      // white on the brand green
            lblTitle.AutoSize = true;              // let the label size itself to the words
            lblTitle.Location = new Point(18, 10); // measured from the header's top left, not the form's
            header.Controls.Add(lblTitle);         // added to the header, so the two move together

            Label lblSub = new Label();            // the second line: what the screen is for, in one phrase
            lblSub.Text = subtitle;                // also supplied by the caller, for the same reason
            lblSub.Font = FontSmall;               // smaller than the title, so the two never compete
            lblSub.ForeColor = Color.FromArgb(200, 230, 220);   // a pale tint, so it sits behind the title
            lblSub.AutoSize = true;                // same as the title: the words decide the width
            lblSub.Location = new Point(21, 40);   // just under the title, so the pair reads as one block
            header.Controls.Add(lblSub);           // into the header too, so one Add carries both labels

            return header;                         // the caller receives one Panel and adds only that
        }

        // -- buttons: a form picks a button by MEANING, never by colour
        public static void StylePrimary(Button button)
        {
            StyleFlat(button, Primary, Color.White);   // the one confirming action: Save, Place Order, Sign In
        }

        // The quiet partner beside a primary button, for Cancel and Close.
        public static void StyleSecondary(Button button)
        {
            StyleFlat(button, Color.White, TextDark);     // white, so it cannot compete with the primary
            button.FlatAppearance.BorderColor = Border;   // the same hairline as a card edge
            button.FlatAppearance.BorderSize = 1;         // put back after StyleFlat removed it: it is the shape
        }

        // Only for what a click cannot take back: delete, suspend, cancel an order.
        public static void StyleDanger(Button button)
        {
            StyleFlat(button, Danger, Color.White);    // red fill, white text: the destructive pairing
        }

        // The neutral third action: View Details, Track Order, Open Report.
        public static void StyleAccent(Button button)
        {
            StyleFlat(button, Accent, Color.White);    // blue reads as "go and look", never as "commit"
        }

        // For a completing action, where green is the outcome rather than the brand.
        public static void StyleSuccess(Button button)
        {
            StyleFlat(button, Success, Color.White);   // marking an order delivered, approving a prescription
        }

        // private: the five methods above are the vocabulary, this is their one body.
        private static void StyleFlat(Button button, Color back, Color fore)
        {
            button.FlatStyle = FlatStyle.Flat;     // flat, not Windows chrome, so the set looks designed
            button.FlatAppearance.BorderSize = 0;  // no outline: the fill alone is the button
            button.BackColor = back;               // the only thing the five public methods differ on
            button.ForeColor = fore;               // passed in with the fill so the pair stays legible
            button.Font = FontBody;                // the same size as the surrounding text
            button.Cursor = Cursors.Hand;          // the pointer says "this is clickable"
            button.UseVisualStyleBackColor = false;   // without this Windows ignores BackColor entirely
        }

        /// <summary>A left menu button, used on all three dashboards.</summary>
        public static void StyleSidebarButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;     // no Windows chrome inside the dark menu
            button.FlatAppearance.BorderSize = 0;  // borderless, so the column reads as one panel
            button.FlatAppearance.MouseOverBackColor = SidebarHover;   // hover handled by WinForms, no event wiring
            button.BackColor = Sidebar;            // identical to the panel behind it until hovered
            button.ForeColor = Color.White;        // the only readable choice on a colour this dark
            button.Font = FontBody;                // menu items are navigation, not headings
            button.TextAlign = ContentAlignment.MiddleLeft;   // a vertical list of labels, not centred captions
            button.Padding = new Padding(16, 0, 0, 0);   // the indent that goes with MiddleLeft
            button.Cursor = Cursors.Hand;                // the same hand as every other button
            button.UseVisualStyleBackColor = false;      // same trap: Windows would repaint over Sidebar
        }

        /// <summary>The one grid style used by every DataGridView in the app.</summary>
        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = CardBack;            // white, so a half empty grid shows no grey slab
            grid.BorderStyle = BorderStyle.None;        // the card already draws an edge; two would double up
            grid.GridColor = Border;                    // the same hairline as that card edge
            grid.EnableHeadersVisualStyles = false;     // required before the header colours below take effect

            // The next five lines make the grid read only: data is edited through forms.
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;         // Delete must not look like it removed a record
            grid.AllowUserToResizeRows = false;         // row height is set once, on the template below
            grid.ReadOnly = true;                       // states the intent: this grid displays, never edits
            grid.MultiSelect = false;                   // one row, because every button acts on one record
            grid.RowHeadersVisible = false;             // the grey stub column carries nothing useful
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;   // the record is the unit of selection
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;   // fills the panel, no horizontal scrollbar
            grid.ColumnHeadersHeight = 36;              // taller than a data row, so it reads as a heading
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;  // locks that 36 in

            grid.ColumnHeadersDefaultCellStyle.BackColor = Sidebar;       // headers in the menu colour, which ties the two
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;   // white on Sidebar, as in the left menu
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);  // heavier, but the same size
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;   // captions line up with the cells
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Sidebar;   // so clicking a heading flashes nothing

            grid.DefaultCellStyle.Font = FontBody;      // cells take the same body font as the rest of the screen
            grid.DefaultCellStyle.ForeColor = TextDark; // and the same near black, so row tints carry the meaning
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 234, 228);   // pale green: the row stays readable
            grid.DefaultCellStyle.SelectionForeColor = TextDark;      // text deliberately does NOT change on selection
            grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);  // breathing room inside each cell
            grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;  // banding, so the eye follows a long row
            grid.RowTemplate.Height = 30;               // on the TEMPLATE, because no rows exist yet
        }

        // -- cards and tiles: a white panel on the near white page, to group controls
        public static Panel BuildCard()
        {
            Panel card = new Panel();                     // a plain container; the form fills it
            card.BackColor = CardBack;                    // white on PageBack is what makes it look raised
            card.Padding = new Padding(14);               // keeps the contents off the border
            card.BorderStyle = BorderStyle.FixedSingle;   // a single hairline, so the group has a definite end
            return card;                                  // unpositioned: where it goes is the form's business
        }

        /// <summary>A summary tile; the out label lets the form refresh it.</summary>
        public static Panel BuildTile(string caption, Color stripe, out Label valueLabel)
        {
            Panel tile = new Panel();              // every KPI tile in the application is built here
            tile.BackColor = CardBack;             // the same white as a card, so the two read as a family
            tile.BorderStyle = BorderStyle.FixedSingle;   // and the same single edge, for the same reason
            tile.Size = new Size(210, 84);         // fixed, so four tiles sit in a neat row

            Panel bar = new Panel();               // the coloured spine down the left edge
            bar.Dock = DockStyle.Left;             // docked, so it follows the tile if the size changes
            bar.Width = 5;                         // narrow on purpose: a signal, not a block of colour
            bar.BackColor = stripe;                // the caller's choice, and the only part that differs
            tile.Controls.Add(bar);                // added first, so the text below sits clear of it

            Label lblCaption = new Label();        // the caption: small and muted, so the number wins the eye
            lblCaption.Text = caption;             // what the figure means: "Today's Orders", "Low Stock"
            lblCaption.Font = FontSmall;           // the smallest size in the palette
            lblCaption.ForeColor = TextMuted;      // muted as well as small: the label, not the answer
            lblCaption.AutoSize = true;            // captions vary in length, so nothing gets clipped
            lblCaption.Location = new Point(16, 12);   // clear of the 5 pixel stripe, near the top
            tile.Controls.Add(lblCaption);         // into the tile, so moving the tile moves the caption

            valueLabel = new Label();              // assigning the out parameter hands it to the caller
            valueLabel.Text = "0";                 // a real starting figure, never a blank box
            valueLabel.Font = FontTileValue;       // the largest weight: the line the tile exists to show
            valueLabel.ForeColor = TextDark;       // full strength, against the muted caption above it
            valueLabel.AutoSize = true;            // "7" grows to "1,240" between refreshes
            valueLabel.Location = new Point(14, 34);   // under the caption, so the pair reads as one unit
            tile.Controls.Add(valueLabel);         // the tile owns it; the form only keeps a reference

            return tile;                           // PlaceTile below is what positions it
        }

        /// <summary>Adds a tile at the designer's coordinates, then rescales it.</summary>
        public static void PlaceTile(Form form, Panel tile, int x, int y, int width, int height)
        {
            form.Controls.Add(tile);               // added BEFORE the bounds: a position needs a parent
            tile.Bounds = new Rectangle(x, y, width, height);   // designer coordinates, corrected below

            SizeF current = form.CurrentAutoScaleDimensions;    // what the screen and font turned out to be
            tile.Scale(new SizeF(current.Width / 7F, current.Height / 15F));   // ratio against the designer's 7x15
            tile.BringToFront();                   // otherwise a docked panel can paint over it
        }

        /// <summary>Shows a red message under a field and tints the field red.</summary>
        public static void ShowError(Label errorLabel, Control field, string message)
        {
            errorLabel.Text = message;             // the other half of Validator: what the user actually sees
            errorLabel.ForeColor = Danger;         // the same red as a delete button, so red always means stop
            errorLabel.Visible = true;             // BuildErrorLabel creates it hidden, so this reveals it
            // Typed as Control so a combo box or date picker works; null means no field.
            if (field != null)
            {
                field.BackColor = Color.FromArgb(255, 240, 240);   // far paler than Danger, so typing stays readable
            }
        }

        // The exact inverse, called before re-validating so a fixed message disappears.
        public static void ClearError(Label errorLabel, Control field)
        {
            errorLabel.Text = string.Empty;        // cleared as well as hidden, so no stale message returns
            errorLabel.Visible = false;            // hidden rather than blank, so it occupies no space
            // Same null guard as ShowError: a form-level rule belongs to no one field.
            if (field != null)
            {
                field.BackColor = Color.White;     // back to normal, which says the field is accepted now
            }
        }

        // Called from a form's constructor for each field it validates.
        public static Label BuildErrorLabel(Point location, int width)
        {
            Label label = new Label();             // one message holder, positioned by the caller
            label.AutoSize = false;                // fixed size, so the controls below never shift
            label.Size = new Size(width, 18);      // width matches the field; 18 is one line of FontSmall
            label.Location = location;             // only the form knows which field this sits under
            label.Font = FontSmall;                // small, so a long message fits without wrapping
            label.ForeColor = Danger;              // already red before any text arrives
            label.Visible = false;                 // created hidden; ShowError reveals it
            return label;                          // the form holds it for ShowError and ClearError
        }

        // -- money: one method, so the currency and decimal places are decided once
        public static string Money(decimal amount)
        {
            return "Tk " + amount.ToString("N2");  // "N2" gives thousands separators and two decimals
        }
    }
}
