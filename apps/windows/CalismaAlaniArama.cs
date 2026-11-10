using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal sealed class SearchEntry
    {
        internal readonly string Title, Keywords;
        internal readonly Action Open;
        internal SearchEntry(string title, string keywords, Action open) { Title = title; Keywords = keywords; Open = open; }
        public override string ToString() { return Title; }
    }

    internal sealed class SearchWindow : Form
    {
        private readonly ListBox matches;
        internal SearchEntry Selected { get { return matches.SelectedItem as SearchEntry; } }

        internal SearchWindow(SearchEntry[] entries)
        {
            Text = L.Text("Çalışma alanında ara", "Search the workspace");
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(610, 367);
            MinimumSize = new Size(520, 300);
            ShowInTaskbar = false;
            BackColor = Style.Canvas;
            ForeColor = Style.Ink;
            var panel = Style.Stack(33, 38, 0, 45);
            panel.Padding = new Padding(12);
            Controls.Add(panel);
            panel.Controls.Add(Style.Text(L.Text("Modüller, BT araçları, bakım adımları ve uygulamalar",
                "Modules, IT tools, maintenance steps and applications"), Style.Small), 0, 0);
            var queryBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 180, AccessibleName = Text,
                Font = Style.Normal, BackColor = Style.Surface, ForeColor = Style.Ink };
            panel.Controls.Add(queryBox, 0, 1);
            matches = new ListBox { Dock = DockStyle.Fill, Font = Style.Normal, IntegralHeight = false,
                HorizontalScrollbar = true, BackColor = Style.Surface, ForeColor = Style.Ink, AccessibleName = Text };
            panel.Controls.Add(matches, 0, 2);
            var actions = Style.Row();
            var open = Style.Button(L.Text("Aç", "Open"), () => { if (Selected != null) DialogResult = DialogResult.OK; }, true);
            var cancel = Style.Button(L.Translate("Vazgeç"), () => { });
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(open);
            actions.Controls.Add(cancel);
            panel.Controls.Add(actions, 0, 3);
            Action filter = () =>
            {
                string query = queryBox.Text.Trim();
                matches.BeginUpdate();
                try
                {
                    matches.Items.Clear();
                    matches.Items.AddRange(entries.Where(entry => query.Length == 0 ||
                        (entry.Title + " " + entry.Keywords).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        .Take(180).Cast<object>().ToArray());
                    if (matches.Items.Count > 0) matches.SelectedIndex = 0;
                    open.Enabled = matches.Items.Count > 0;
                }
                finally { matches.EndUpdate(); }
            };
            queryBox.TextChanged += (sender, e) => filter();
            queryBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode != Keys.Down || matches.Items.Count == 0) return;
                matches.Focus();
                e.SuppressKeyPress = true;
            };
            matches.DoubleClick += (sender, e) => { if (Selected != null) DialogResult = DialogResult.OK; };
            AcceptButton = open;
            CancelButton = cancel;
            ActiveControl = queryBox;
            filter();
        }
    }

    internal sealed partial class MainWindow
    {
        private Button workspaceSearch;

        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if (keys == (Keys.Control | Keys.K))
            {
                ShowWorkspaceSearch();
                return true;
            }
            return base.ProcessCmdKey(ref message, keys);
        }

        private void ShowWorkspaceSearch()
        {
            if (!built || Busy) return;
            Safe(() =>
            {
                var entries = new List<SearchEntry>();
                foreach (string page in navigation.Keys)
                    entries.Add(new SearchEntry(copy[page][0], copy[page][1], () => SelectPage(page)));
                foreach (var tool in ItTools.All)
                {
                    entries.Add(new SearchEntry((itFavorites.Contains(tool.Id) ? "★ " : "") + tool.Title + " · " + tool.Group,
                        tool.Id + " " + tool.Detail, () =>
                    {
                        SelectPage("it");
                        itCategory.SelectedIndex = 0;
                        itSearch.Text = tool.Id;
                        itGrid.Focus();
                    }));
                }
                foreach (var task in Maintenance.Tasks.Where(item => !Program.Admin || item.Admin))
                {
                    entries.Add(new SearchEntry(task.Title + " · " + task.Group, task.Id + " " + task.Detail, () =>
                    {
                        SelectPage("maintenance");
                        var row = taskGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => item.Tag == task);
                        if (row != null) { taskGrid.CurrentCell = row.Cells[2]; taskGrid.Focus(); }
                    }));
                }
                if (!Program.Admin)
                {
                    foreach (var package in packages)
                    {
                        entries.Add(new SearchEntry(package.Name + " · WinGet", package.Id + " " + package.Category, () =>
                        {
                            SelectPage("apps");
                            categories.SelectedIndex = 0;
                            search.Text = package.Id;
                        }));
                    }
                }
                using (var dialog = new SearchWindow(entries.ToArray()))
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Selected != null) dialog.Selected.Open();
            });
        }
    }
}

