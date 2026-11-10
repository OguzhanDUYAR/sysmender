using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal sealed partial class MainWindow
    {
        private Button openHistory, compareReports, exportSummary;

        private void ShowHistory()
        {
            if (Busy) return;
            Safe(() =>
            {
                string folder = Program.Admin ? MachineFiles.FindExisting() : Program.Logs;
                if (!Startup.SafeFolder(folder) || !Directory.Exists(folder))
                {
                    Inform(L.Text("İşlem günlüğü bulunmadı.", "No run log was found."));
                    return;
                }
                var logs = Directory.EnumerateFiles(folder, "*.txt", SearchOption.TopDirectoryOnly)
                    .Take(3000).Where(file => Regex.IsMatch(Path.GetFileName(file), @"\A[0-9]{8}-[0-9]{6}-[a-f0-9]{8}\.txt\z"))
                    .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal).Take(50).ToArray();
                if (logs.Length == 0) { Inform(L.Text("İşlem günlüğü bulunmadı.", "No run log was found.")); return; }
                using (var pick = new ChoiceWindow(L.Text("İşlem geçmişi", "Run history"),
                    L.Text("En fazla 50 kayıt. Seçilen günlüğün son 256 KiB bölümü salt okunur açılır.",
                        "Up to 50 entries. The final 256 KiB of the selected log opens read-only."),
                    logs.Select(Path.GetFileName).ToArray()))
                {
                    if (pick.ShowDialog(this) != DialogResult.OK) return;
                    string path = logs[pick.Selection];
                    var value = TempCleanup.ReadVerifiedFile(path, stream => LocalDiagnostics.ReadTail(stream, 256 * 1024));
                    ViewText(Path.GetFileName(path), value);
                }
            });
        }

        private void CompareReports()
        {
            if (Busy || Program.Admin) return;
            Safe(() =>
            {
                using (var dialog = new OpenFileDialog { CheckFileExists = true, DereferenceLinks = false,
                    Filter = "JSON (*.json)|*.json", Title = L.Text("Önceki raporu seçin", "Choose the baseline report") })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string first = dialog.FileName;
                    dialog.Title = L.Text("Sonraki raporu seçin", "Choose the later report");
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ViewText(L.Text("Rapor karşılaştırması", "Report comparison"),
                        SupportReport.Compare(SupportReport.Load(first), SupportReport.Load(dialog.FileName)));
                }
            });
        }

        private void ViewText(string caption, string value)
        {
            using (var window = new Form { Text = caption, StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false, ClientSize = new Size(740, 500), MinimumSize = new Size(530, 320),
                BackColor = Style.Canvas, ForeColor = Style.Ink, AutoScaleMode = AutoScaleMode.Dpi })
            {
                var panel = Style.Stack(0, 45);
                panel.Padding = new Padding(12);
                window.Controls.Add(panel);
                var display = new RichTextBox { Text = value, ReadOnly = true, DetectUrls = false, Dock = DockStyle.Fill,
                    BackColor = Style.Surface, ForeColor = Style.Ink, Font = Style.Mono, AccessibleName = caption };
                panel.Controls.Add(display, 0, 0);
                var row = Style.Row();
                var close = Style.Button(L.Text("Kapat", "Close"), () => window.Close());
                close.DialogResult = DialogResult.Cancel;
                row.Controls.Add(close);
                panel.Controls.Add(row, 0, 1);
                window.CancelButton = close;
                window.ShowDialog(this);
            }
        }
    }
}

