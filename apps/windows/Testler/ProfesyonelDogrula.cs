using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal static partial class Verification
    {
        private static void FileWorkspace(string folder)
        {
            const string abc = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
            string root = Path.Combine(folder, "file-tools");
            Directory.CreateDirectory(root);
            string first = Path.Combine(root, "sample-a.txt"), second = Path.Combine(root, "sample-b.txt");
            string other = Path.Combine(root, "sample-c.txt");
            File.WriteAllText(first, "abc", new UTF8Encoding(false));
            File.WriteAllText(second, "abc", new UTF8Encoding(false));
            File.WriteAllText(other, "xyz", new UTF8Encoding(false));
            var output = new List<string>();
            Check(FileTools.CreateHash(first, abc).Run(output.Add).Kind == ResultKind.Done);
            Check(output.Any(line => line.Contains(abc)));
            Check(FileTools.CreateHash(first, new string('0', 64)).Run(_ => { }).Kind == ResultKind.Review);
            bool blocked = false;
            try { FileTools.CreateHash(first + ":sample", ""); }
            catch (ArgumentException) { blocked = true; }
            Check(blocked);
            output.Clear();
            Check(FileTools.CreateScan(root, true).Run(output.Add).Kind == ResultKind.Done);
            Check(output.Any(line => line.Contains(abc)) && output.Any(line => line.EndsWith("sample-a.txt")) &&
                output.Any(line => line.EndsWith("sample-b.txt")) && !output.Any(line => line.EndsWith("sample-c.txt")));
            output.Clear();
            Check(FileTools.CreateScan(root, false).Run(output.Add).Kind == ResultKind.Done);
            Check(output.Count(line => line.EndsWith(".txt")) == 3 && File.ReadAllText(other) == "xyz");
            using (var cancel = new CancellationTokenSource())
            using (var stream = new MemoryStream(new byte[1]))
            {
                cancel.Cancel();
                bool cancelled = false;
                try { FileTools.Hash(stream, cancel.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled);
            }
        }

        private static void SupportWorkflow(string folder)
        {
            L.Initialize("en");
            try
            {
                foreach (var scenario in SupportScenario.All)
                {
                    Check(scenario.Diagnostics.Length > 0);
                    foreach (var id in scenario.Diagnostics)
                    {
                        if (id.StartsWith("maintenance:", StringComparison.Ordinal))
                            Check(!Maintenance.Tasks.Single(item => item.Id == id.Substring(12)).Changes);
                        else Check(!ItTools.All.Single(item => item.Id == id).Changes);
                    }
                }
                var initial = new[] { new ReportStep { Id="it-volumes", Tool="Volumes", Outcome="Review", Status="Review", Message="fixture-secret" },
                    new ReportStep { Id="it-power", Tool="Power", Outcome="Done", Status="Done", Message="same" } };
                var final = new[] { new ReportStep { Id="it-volumes", Tool="Volumes", Outcome="Done", Status="Done", Message="free" },
                    new ReportStep { Id="it-power", Tool="Güç", Outcome="Done", Status="Tamamlandı", Message="same" } };
                string report = SupportReport.Build(initial, "fixture-log", true);
                string summary = SupportReport.Build(initial, "fixture-log", true, true);
                Check(!summary.Contains("fixture-secret") && !summary.Contains("fixture-log") &&
                    SupportReport.Parse(summary).Computer == "");
                string path = Path.Combine(folder, "baseline.json");
                SupportReport.WriteNew(path, report);
                var comparison = SupportReport.Compare(SupportReport.Load(path), SupportReport.Parse(SupportReport.Build(final, "", true)));
                Check(comparison.Contains("1 changed, 0 added, 0 removed, 1 unchanged") && comparison.Contains("Review → Done"));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("old\nkeep\nlast")))
                    Check(LocalDiagnostics.ReadTail(stream, 8) == "last");
                using (var stream = new MemoryStream(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("old\nlast")).ToArray()))
                    Check(LocalDiagnostics.ReadTail(stream, 32) == "old\nlast");
                using (var picker = new ChoiceWindow("Troubleshooting assistant", "Choose a scenario.",
                    SupportScenario.All.Select(scenario => scenario.Title).ToArray()))
                {
                    Check(picker.Selection == 0);
                    Render(picker, Path.Combine(folder, "assistant-en.png"));
                }
            }
            finally { L.Initialize("tr"); }
        }

        private static void ProfessionalUi(string folder)
        {
            var sampler = new NetworkRateSampler();
            var first = new Dictionary<string, NetworkRateSampler.Traffic> { { "nic", new NetworkRateSampler.Traffic(100, 90) } };
            var second = new Dictionary<string, NetworkRateSampler.Traffic> { { "nic", new NetworkRateSampler.Traffic(500, 290) } };
            Check(sampler.Read(first, 1) == null);
            var rate = sampler.Read(second, 3);
            Check(rate.Item1 == 200 && rate.Item2 == 100 && sampler.Read(first, 5) == null);
            var other = new Dictionary<string, NetworkRateSampler.Traffic> { { "other", new NetworkRateSampler.Traffic(1000, 900) } };
            Check(sampler.Read(other, 7) == null);
            Check(sampler.Read(other, 19) == null);
            Style.InitializeTheme("light");
            L.Initialize("en");
            try
            {
                using (var form = new MainWindow(null, "it"))
                {
                    Check(Style.Light && form.BackColor == Style.Canvas);
                    form.Size = form.MinimumSize;
                    Render(form, Path.Combine(folder, "it-light-en.png"));
                }
                var entries = new[] { new SearchEntry("System", "boot", () => { }),
                    new SearchEntry("Tools", "network", () => { }) };
                using (var search = new SearchWindow(entries))
                {
                    var panel = (TableLayoutPanel)search.Controls[0];
                    var query = (TextBox)panel.GetControlFromPosition(0, 1);
                    query.Text = "network";
                    Check(search.Selected == entries[1]);
                    query.Text = "unknown";
                    Check(search.Selected == null);
                    query.Clear();
                    Render(search, Path.Combine(folder, "search-en.png"));
                }
            }
            finally { Style.InitializeTheme("dark"); L.Initialize("tr"); }
        }
    }
}

