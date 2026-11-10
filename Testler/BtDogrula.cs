using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal static partial class Verification
    {
        private static void ItCatalogVerification(string folder)
        {
            var catalog = ItTools.All;
            Check(catalog.Length == 66 && catalog.Count(tool => tool.Changes) == 9);
            Check(catalog.All(tool => !tool.Changes || tool.Admin));
            foreach (var tool in catalog)
            {
                var script = ItTools.ScriptFor(tool.Id);
                Check(script == null || script.Length < 9000);
            }
            var file = Path.Combine(folder, "BT-parse.ps1");
            File.WriteAllText(file, Program.Resource("BT.ps1"), new System.Text.UTF8Encoding(true));
            var output = new List<string>();
            var parsed = Commands.PowerShell(
                "$tokens=$null; $errors=$null; [void][Management.Automation.Language.Parser]::ParseFile('" +
                file.Replace("'", "''") + "',[ref]$tokens,[ref]$errors); if($errors.Count){throw ($errors -join ' / ')}",
                output.Add, probeTimeout: TimeSpan.FromSeconds(30));
            if (parsed.Code != 0) throw new InvalidDataException(string.Join(" | ", output));
            bool denied = false;
            try { ItTools.Create("winsock-reset").Run(_ => { }); }
            catch (InvalidOperationException) { denied = true; }
            Check(denied && ItTools.Classify("winsock-reset", 0, true).Reboot);
            Check(ItTools.Classify("spooler-restart", 3, false).Kind == ResultKind.Skipped);
            Check(ItTools.Classify("time-status", 3, true).Kind == ResultKind.Failed);

            string mock = @"
                $script:service=[pscustomobject]@{Name='Spooler';Status='Running';StartType='Automatic';DependentServices=@()}
                $script:service | Add-Member ScriptMethod Refresh {}
                $script:service | Add-Member ScriptMethod WaitForStatus {param($expected,$timeout); if($this.Status -ne $expected){throw 'Unexpected service state'}}
                function Get-Service {param($Name); $script:service}
                function Stop-Service { [CmdletBinding()] param($Name); if($Name -ne 'Spooler'){throw 'Wrong service'}; $script:service.Status='Stopped'; 'MOCK STOP' }
                function Start-Service { [CmdletBinding()] param($Name); if($Name -ne 'Spooler'){throw 'Wrong service'}; $script:service.Status='Running'; 'MOCK START' }
            ";
            output.Clear();
            var restart = Commands.PowerShell(mock + ItTools.ScriptFor("spooler-restart"), output.Add,
                probeTimeout: TimeSpan.FromSeconds(30));
            Check(restart.Code == 0 && output.Contains("MOCK STOP") && output.Contains("MOCK START"));
            output.Clear();
            var disabled = Commands.PowerShell(mock + "$script:service.StartType='Disabled';\n" + ItTools.ScriptFor("spooler-restart"),
                output.Add, probeTimeout: TimeSpan.FromSeconds(30));
            Check(disabled.Code == 3 && !output.Contains("MOCK STOP") && !output.Contains("MOCK START"));
        }

        private static void ItTargetVerification()
        {
            Check(ItTools.ValidateHost(" 127.0.0.1 ", 443) == "127.0.0.1");
            Check(ItTools.ValidateHost("::1", 65535) == "::1");
            Check(ItTools.ValidateHost("workstation.example.", 1) == "workstation.example.");
            foreach (var input in new[] { "", "host;whoami", "https://host", "host name", "-argument", "a..b", "a/b" })
            {
                bool denied = false;
                try { ItTools.ValidateHost(input, 80); }
                catch (ArgumentException) { denied = true; }
                Check(denied);
            }
            bool badPort = false;
            try { ItTools.ValidateHost("localhost", 65536); }
            catch (ArgumentException) { badPort = true; }
            Check(badPort);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try { listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; }
            catch (SocketException ex)
            {
                listener.Stop();
                if (ex.SocketErrorCode != SocketError.AccessDenied) throw;
                Console.WriteLine("ATLANDI  Yerel TCP dinleyicisine erişim engellendi.");
                return;
            }
            try
            {
                var lines = new List<string>();
                var open = ItTools.Create("endpoint", "127.0.0.1", port).Run(lines.Add);
                if (open.Kind != ResultKind.Done && lines.Any(line => line.StartsWith("TCP ", StringComparison.Ordinal) &&
                    line.EndsWith("AccessDenied", StringComparison.Ordinal)))
                {
                    Console.WriteLine("ATLANDI  Yerel TCP bağlantısına erişim engellendi.");
                    return;
                }
                if (open.Kind != ResultKind.Done || !open.Message.Contains("TCP OK"))
                    throw new InvalidOperationException(string.Join(" | ", lines) + " | " + open.Message);
            }
            finally { listener.Stop(); }
            Check(ItTools.Create("endpoint", "127.0.0.1", port).Run(_ => { }).Kind == ResultKind.Review);
        }

        private static void ReadinessVerification()
        {
            string fixture = @"
                function Get-CimInstance { [CmdletBinding()] param($ClassName,$OperationTimeoutSec); [pscustomobject]@{Caption='Fixture OS';BuildNumber='26000'} }
                function Get-ItemProperty { [CmdletBinding()] param($LiteralPath); [pscustomobject]@{PendingFileRenameOperations=$script:renames} }
                function Test-Path { [CmdletBinding()] param($LiteralPath);
                    if ($script:denied -and $LiteralPath.EndsWith('RebootPending')) { throw 'MOCK ACCESS' }
                    return $false
                }
            ";
            var script = ItTools.ScriptFor("readiness");
            var output = new List<string>();
            Func<string, int> run = mode =>
            {
                output.Clear();
                return Commands.PowerShell(fixture + mode + "\n" + script, output.Add,
                    probeTimeout: TimeSpan.FromSeconds(30)).Code;
            };
            Check(run("$script:renames=@('','  '); $script:denied=$false;") == 0 &&
                output.Any(line => line.Contains("Fixture OS")));
            Check(run("$script:renames=@('fixture-private-path'); $script:denied=$false;") == 2 &&
                output.Any(line => line.Contains("PendingFileRenameOperations")) &&
                !output.Any(line => line.Contains("fixture-private-path")));
            Check(run("$script:renames=@(); $script:denied=$true;") == 2 &&
                output.Any(line => line.Contains("MOCK ACCESS")));
        }

        private static void ItInterfaceVerification(string folder)
        {
            foreach (string language in new[] { "tr", "en" })
            {
                L.Initialize(language);
                try
                {
                    using (var form = new MainWindow(null, "it"))
                    {
                        var grid = Field<DataGridView>(form, "itGrid");
                        Check(grid.Rows.Count == 66);
                        Check(grid.Rows.Cast<DataGridViewRow>().Where(row => ((ItTool)row.Tag).Admin)
                            .All(row => row.Cells[0].ReadOnly));
                        Check(!Field<Button>(form, "itRun").Enabled);
                        Invoke(form, "ApplyItProfile");
                        Check(Field<HashSet<string>>(form, "itSelected").Count == 9);
                        Check(Field<Button>(form, "itRun").Enabled);
                        var search = Field<TextBox>(form, "itSearch");
                        search.Text = "winsock-reset";
                        Check(grid.Rows.Count == 1 && grid.Rows[0].Cells[0].ReadOnly);
                        Check(Field<HashSet<string>>(form, "itSelected").Count == 9);
                        search.Clear();
                        form.Size = form.MinimumSize;
                        Render(form, Path.Combine(folder, "it-" + language + ".png"));
                    }
                }
                finally { L.Initialize("tr"); }
            }
            const string dangerous = "</pre><script>alert(1)</script> Türkçe";
            var steps = new[] { new ReportStep { Tool = "<b>Test</b>", Status = "OK", Message = dangerous } };
            string html = SupportReport.Build(steps, dangerous, false);
            Check(html.Contains("Content-Security-Policy") && html.Contains("&lt;script&gt;") && !html.Contains("<script>"));
            string json = SupportReport.Build(steps, dangerous, true);
            Check(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json)["Log"].Equals(dangerous));
            string path = Path.Combine(folder, "report-fixture.html");
            SupportReport.WriteNew(path, html);
            bool refused = false;
            try { SupportReport.WriteNew(path, "replacement"); }
            catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(path) == html);
        }

        private static int ItReadOnlySweep(string folder)
        {
            L.Initialize("en");
            int failed = 0;
            var tools = ItTools.All.Where(tool => !tool.Admin && !tool.Changes && !ItTools.UsesTarget(tool.Id)).ToArray();
            Parallel.ForEach(tools, new ParallelOptions { MaxDegreeOfParallelism = 3 }, tool =>
            {
                var lines = new List<string>();
                try
                {
                    var result = ItTools.Create(tool.Id).Run(lines.Add);
                    lines.Add(result.Kind + " | " + result.Message);
                    Console.WriteLine(tool.Id + " " + result.Kind);
                    if (result.Kind == ResultKind.Failed) Interlocked.Increment(ref failed);
                }
                catch (Exception ex)
                {
                    lines.Add(ex.ToString());
                    Console.WriteLine(tool.Id + " Failed");
                    Interlocked.Increment(ref failed);
                }
                File.WriteAllLines(Path.Combine(folder, tool.Id + ".txt"), lines);
            });
            return failed == 0 ? 0 : 1;
        }
    }
}

