using System;
using System.Drawing;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal static class Style
    {
        private static readonly bool Contrast = SystemInformation.HighContrast;
        internal static bool Light { get; private set; }
        internal static void InitializeTheme(string value)
        {
            Light = value == "light" || (value != "dark" && UserPreference.Read("Tema.txt", 16).Trim() == "light");
        }
        internal static void SwitchTheme()
        {
            UserPreference.Write("Tema.txt", Light ? "dark" : "light");
            Light = !Light;
        }
        internal static Color Canvas { get { return Contrast ? SystemColors.Window : Light ? Color.FromArgb(242, 246, 239) : Color.FromArgb(7, 9, 7); } }
        internal static Color Rail { get { return Contrast ? SystemColors.Control : Light ? Color.FromArgb(229, 239, 224) : Color.FromArgb(13, 17, 13); } }
        internal static Color Surface { get { return Contrast ? SystemColors.Window : Light ? Color.White : Color.FromArgb(17, 22, 17); } }
        internal static Color Raised { get { return Contrast ? SystemColors.Control : Light ? Color.FromArgb(218, 234, 212) : Color.FromArgb(28, 36, 27); } }
        internal static Color Border { get { return Contrast ? SystemColors.WindowText : Light ? Color.FromArgb(153, 176, 147) : Color.FromArgb(57, 72, 53); } }
        internal static Color Ink { get { return Contrast ? SystemColors.WindowText : Light ? Color.FromArgb(25, 40, 24) : Color.FromArgb(237, 244, 251); } }
        internal static Color Muted { get { return Contrast ? SystemColors.WindowText : Light ? Color.FromArgb(64, 83, 62) : Color.FromArgb(177, 194, 173); } }
        internal static Color Accent { get { return Contrast ? SystemColors.Highlight : Light ? Color.FromArgb(40, 91, 23) : Color.FromArgb(184, 255, 60); } }
        internal static Color AccentInk { get { return Contrast ? SystemColors.HighlightText : Light ? Color.White : Color.FromArgb(12, 23, 7); } }
        internal static Color Selected { get { return Contrast ? SystemColors.Highlight : Light ? Color.FromArgb(204, 230, 193) : Color.FromArgb(43, 63, 34); } }
        internal static Color SelectedInk { get { return Contrast ? SystemColors.HighlightText : Ink; } }
        internal static Color Amber { get { return Contrast ? Ink : Light ? Color.FromArgb(130, 73, 0) : Color.FromArgb(251, 201, 127); } }
        internal static Color Red { get { return Contrast ? Ink : Light ? Color.FromArgb(165, 25, 40) : Color.FromArgb(255, 155, 162); } }
        internal static readonly Font Normal = new Font("Segoe UI", 10);
        internal static readonly Font Small = new Font("Segoe UI", 9);
        internal static readonly Font Heading = new Font("Segoe UI", 23, FontStyle.Bold);
        internal static readonly Font Section = new Font("Segoe UI", 11, FontStyle.Bold);
        internal static readonly Font Mono = new Font("Consolas", 10);

        internal static Label Text(string value, Font font = null, Color? color = null)
        {
            return new Label
            {
                Text = value, Font = font ?? Normal, ForeColor = color ?? Ink,
                UseMnemonic = false, AutoEllipsis = true, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
            };
        }

        internal static Button Button(string label, Action click, bool primary = false)
        {
            var button = new ReadableButton
            {
                Text = label, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(68, 34), Padding = new Padding(9, 1, 9, 1),
                BackColor = primary ? Accent : Raised, ForeColor = primary ? AccentInk : Ink,
                UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Font = Small,
                Margin = new Padding(0, 1, 8, 2), Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Accent : Selected;
            button.Click += (sender, e) => click();
            return button;
        }

        private sealed class ReadableButton : Button
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Enabled) return;
                using (var background = new SolidBrush(Raised)) e.Graphics.FillRectangle(background, ClientRectangle);
                using (var outline = new Pen(Border)) e.Graphics.DrawRectangle(outline, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        internal static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
                Margin = Padding.Empty, Padding = Padding.Empty, FlowDirection = FlowDirection.LeftToRight
            };
        }

        internal static TableLayoutPanel Stack(params int[] rows)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty,
                ColumnCount = 1, RowCount = rows.Length
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int row in rows)
                panel.RowStyles.Add(row == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, row));
            return panel;
        }

        internal static ComboBox Combo(string name, int width)
        {
            return new ComboBox
            {
                AccessibleName = name, Width = width, DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink, Font = Normal,
                Margin = new Padding(0, 4, 8, 3)
            };
        }

        internal static DataGridView Grid(string name)
        {
            var grid = new DataGridView
            {
                AccessibleName = name, Dock = DockStyle.Fill, Margin = Padding.Empty, BackgroundColor = Surface,
                BorderStyle = BorderStyle.FixedSingle, GridColor = Border, RowHeadersVisible = false,
                AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AllowUserToResizeColumns = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, EditMode = DataGridViewEditMode.EditOnEnter,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeight = 34, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EnableHeadersVisualStyles = false, Font = Normal
            };
            grid.RowTemplate.Height = 35;
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface, ForeColor = Ink, SelectionBackColor = Selected, SelectionForeColor = SelectedInk,
                Padding = new Padding(6, 0, 6, 0)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Rail;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Raised, ForeColor = Ink, SelectionBackColor = Raised, SelectionForeColor = Ink,
                Font = Section, Padding = new Padding(6, 0, 6, 0)
            };
            return grid;
        }

        internal static void Column(DataGridView grid, string header, int size, bool fill = false)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
            };
            if (fill) column.FillWeight = size;
            else column.Width = size;
            grid.Columns.Add(column);
        }

        internal static void CheckColumn(DataGridView grid)
        {
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = L.Translate("Seç"), Width = 74, SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.CurrentCellDirtyStateChanged += (sender, e) =>
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
        }

        internal static Label Note(string value)
        {
            var label = Text(value, Small, Muted);
            label.BackColor = Rail;
            label.Padding = new Padding(11, 3, 11, 3);
            label.Margin = new Padding(0, 5, 0, 5);
            label.AutoEllipsis = false;
            return label;
        }
    }

    internal sealed class PlanWindow : Form
    {
        internal PlanWindow(string caption, string note, string[] items)
        {
            Text = caption;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(655, 390);
            MinimumSize = new Size(570, 370);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Style.Canvas;
            ForeColor = Style.Ink;
            Font = Style.Normal;
            var layout = Style.Stack(40, 91, 0, 50);
            layout.Padding = new Padding(18, 12, 18, 8);
            Controls.Add(layout);
            layout.Controls.Add(Style.Text(caption, Style.Heading), 0, 0);
            layout.Controls.Add(Style.Note(note), 0, 1);
            var list = new RichTextBox
            {
                AccessibleName = L.Translate("İşlem sırası"), Dock = DockStyle.Fill,
                ReadOnly = true, DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = Style.Surface, ForeColor = Style.Ink, Font = Style.Normal, Margin = Padding.Empty,
                Text = string.Join("\n\n", System.Linq.Enumerable.Select(items, (item, i) => (i + 1) + ".  " + item))
            };
            layout.Controls.Add(list, 0, 2);
            var buttons = Style.Row();
            buttons.Padding = new Padding(0, 8, 0, 0);
            buttons.FlowDirection = FlowDirection.RightToLeft;
            var run = Style.Button(L.Translate("Başlat"), () => { }, true);
            var cancel = Style.Button(L.Translate("Vazgeç"), () => { });
            run.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(run);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 3);
            CancelButton = cancel;
            ActiveControl = cancel;
        }
    }
}
