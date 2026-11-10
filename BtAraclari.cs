using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BakimMerkezi
{
    internal sealed class ItTool
    {
        internal readonly string Id;
        private readonly string[] text;
        internal readonly bool Admin, Changes;
        internal ItTool(string[] cells)
        {
            Id = cells[0]; text = cells; Admin = cells[7] == "1"; Changes = cells[8] == "1";
        }
        internal string Group { get { return L.Text(text[1], text[2]); } }
        internal string Title { get { return L.Text(text[3], text[4]); } }
        internal string Detail { get { return L.Text(text[5], text[6]); } }
        internal string Access
        {
            get
            {
                if (!Changes) return Admin ? L.Text("Okuma · yönetici", "Read · admin") : L.Text("Okuma", "Read");
                bool moderate = Id == "driver-backup" || Id == "defender-update";
                return moderate ? L.Text("Orta · yönetici", "Medium · admin") : L.Text("Yüksek · yönetici", "High · admin");
            }
        }
    }

    internal static class ItTools
    {
        internal static readonly ItTool[] All = ReadCatalog();
        private static readonly Dictionary<string, string[]> Native = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "network-config", new[] { "ipconfig.exe", "/all" } },
            { "routes", new[] { "route.exe", "print" } },
            { "dns-cache", new[] { "ipconfig.exe", "/displaydns" } },
            { "wlan", new[] { "netsh.exe", "wlan show interfaces" } },
            { "time-status", new[] { "w32tm.exe", "/query /status" } },
            { "policy", new[] { "gpresult.exe", "/r /scope computer" } },
            { "sfc-verify", new[] { "sfc.exe", "/verifyonly" } },
            { "component-analysis", new[] { "dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore /English" } },
            { "winre", new[] { "reagentc.exe", "/info" } },
            { "boot-config", new[] { "bcdedit.exe", "/enum active" } },
            { "tcp-udp", new[] { "netstat.exe", "-ano" } },
            { "usb-devices", new[] { "pnputil.exe", "/enum-devices /connected /class USB" } },
            { "winsock-reset", new[] { "netsh.exe", "winsock reset" } },
            { "time-resync", new[] { "w32tm.exe", "/resync" } },
            { "pnp-scan", new[] { "pnputil.exe", "/scan-devices" } },
            { "policy-refresh", new[] { "gpupdate.exe", "/target:computer /force /wait:600" } }
        };

        private static ItTool[] ReadCatalog()
        {
            var result = new List<ItTool>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = new StringReader(Program.Resource("BT.tsv")))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = line.Split('\t');
                    if (cells.Length != 9 || !Regex.IsMatch(cells[0], @"\A[a-z]+(?:-[a-z]+)*\z") ||
                        !ids.Add(cells[0]) || (cells[7] != "0" && cells[7] != "1") ||
                        (cells[8] != "0" && cells[8] != "1") || (cells[8] == "1" && cells[7] != "1"))
                        throw new InvalidDataException("Invalid IT tool catalog.");
                    result.Add(new ItTool(cells));
                }
            }
            return result.ToArray();
        }

        internal static string ScriptFor(string id)
        {
            if (!All.Any(tool => tool.Id == id)) throw new ArgumentException("Unknown tool.");
            if (Native.ContainsKey(id) || LocalDiagnostics.Handles(id) || UsesTarget(id) || id == "driver-backup") return null;
            string source = Program.Resource("BT.ps1").Replace("\r\n", "\n");
            string marker = "# TOOL " + id + "\n";
            int start = source.IndexOf(marker, StringComparison.Ordinal);
            int common = source.IndexOf("# TOOL ", StringComparison.Ordinal);
            int end = start < 0 ? -1 : source.IndexOf("# END TOOL", start, StringComparison.Ordinal);
            if (common < 0 || start < 0 || end < 0) throw new InvalidDataException("Missing tool implementation: " + id);
            return source.Substring(0, common) + source.Substring(start + marker.Length, end - start - marker.Length) +
                "\nif ($script:review) { exit 2 }";
        }

        internal static Job Create(string id, string host = null, int port = 443)
        {
            var tool = All.SingleOrDefault(item => item.Id == id);
            if (tool == null) throw new ArgumentException("Unknown tool.");
            if (UsesTarget(id)) host = ValidateHost(host, port);
            string target = host;
            return new Job((tool.Changes ? "it-change-" : "it-") + id, tool.Title, (log, token) =>
            {
                if (tool.Admin && !Program.Admin) throw new InvalidOperationException(L.Text("Yönetici penceresi gerekiyor.", "Open the administrator window."));
                log(tool.Detail);
                token.ThrowIfCancellationRequested();
                if (id == "endpoint") return Probe(target, port, log, token).GetAwaiter().GetResult();
                if (id == "trace-route") return TraceRoute(target, log, token);
                if (id == "driver-backup") return BackupDrivers(log);
                if (LocalDiagnostics.Handles(id)) return LocalDiagnostics.Run(id, log, token);
                string[] command;
                bool native = Native.TryGetValue(id, out command);
                CommandOutput output;
                TimeSpan? limit = tool.Changes ? (TimeSpan?)null :
                    TimeSpan.FromMinutes(id == "sfc-verify" ? 20 : id == "component-analysis" || id == "volume-analysis" ? 10 : 3);
                if (native)
                {
                    log(command[0] + " " + command[1]);
                    output = Commands.Run(Commands.SystemTool(command[0]), command[1],
                        id == "sfc-verify" ? Encoding.Unicode : Commands.Oem, log, token: token, probeTimeout: limit);
                }
                else output = Commands.PowerShell(ScriptFor(id), log, token, limit);
                if (id == "sfc-verify") return Maintenance.ClassifySfc(output);
                return Classify(id, output.Code, native);
            });
        }

        internal static bool UsesTarget(string id) { return id == "endpoint" || id == "trace-route"; }

        private static Result TraceRoute(string target, Action<string> log, CancellationToken token)
        {
            log(target + " · tracert /d /h 15 /w 750");
            var output = Commands.Run(Commands.SystemTool("tracert.exe"), "/d /h 15 /w 750 " + target,
                Commands.Oem, log, token: token, probeTimeout: TimeSpan.FromSeconds(90));
            return output.Code == 0 ?
                new Result(ResultKind.Review, L.Text("Rota çıktısını inceleyin. Çıkış kodu hedefe ulaşıldığını kanıtlamaz.",
                    "Review the route output. The exit code does not prove the target was reached.")) : Maintenance.Generic(output, "");
        }

        private static Result BackupDrivers(Action<string> log)
        {
            string folder = Path.Combine(MachineFiles.Prepare(), "Drivers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            if (!Startup.SafeFolder(folder)) throw new IOException("Driver backup destination is not a safe local folder.");
            log(L.Text("Yedek hedefi: ", "Backup destination: ") + folder);
            var outcome = Commands.Run(Commands.SystemTool("pnputil.exe"), "/export-driver * \"" + folder + "\"",
                Commands.Oem, log);
            if (outcome.Code != 0) return Maintenance.Generic(outcome, "");
            if (!Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).Any())
                return new Result(ResultKind.Review, L.Text("Yedek klasöründe INF bulunamadı.", "No INF files were found in the backup folder."));
            return new Result(ResultKind.Done, L.Text("Sürücü yedeği oluşturuldu: ", "Driver backup created: ") + folder);
        }

        internal static Result Classify(string id, int code, bool native)
        {
            if (code == 3010 || code == 1641 || (id == "winsock-reset" && code == 0))
                return new Result(ResultKind.Review, L.Text("Komut tamamlandı. Yeniden başlatıp sonucu kontrol edin.",
                    "Command completed. Reboot and verify the outcome."), true);
            if (!native && code == 3) return new Result(ResultKind.Skipped, L.Text("Bu sistemde uygulanamadı. Nedeni günlükte.",
                "Not applicable on this system. See the log for the reason."));
            if ((!native && code == 2) || (id == "policy-refresh" && code == 0))
                return new Result(ResultKind.Review, L.Text("Bulguları ve Windows çıktısını inceleyin.", "Review the findings and Windows output."));
            if (code != 0) return Maintenance.Generic(new CommandOutput(code, ""), "");
            return new Result(ResultKind.Done, L.Text("Araç tamamlandı. Ayrıntılar günlükte.", "Tool completed. Details are in the log."));
        }

        internal static string ValidateHost(string host, int port)
        {
            host = (host ?? "").Trim();
            IPAddress ip;
            bool literal = IPAddress.TryParse(host, out ip);
            bool dns = host.Length <= 253 && Regex.IsMatch(host,
                @"\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*\.?\z");
            if ((!literal && !dns) || host.Length > 253 || port < 1 || port > 65535)
                throw new ArgumentException(L.Text("Geçerli bir DNS adı veya IP adresi ve 1–65535 arasında port girin.",
                    "Enter a valid DNS name or IP address and a port between 1 and 65535."));
            return literal ? ip.ToString() : host;
        }

        private static async Task<bool> Wait(Task task, int timeout, CancellationToken token)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout, token)).ConfigureAwait(false) != task)
            {
                ObserveFault(task);
                token.ThrowIfCancellationRequested();
                return false;
            }
            await task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return true;
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(failed => failed.Exception.Handle(error => true),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        internal static async Task<Result> Probe(string host, int port, Action<string> log, CancellationToken token)
        {
            ValidateHost(host, port);
            token.ThrowIfCancellationRequested();
            try
            {
                log(host + " : " + port);
                var lookup = Dns.GetHostAddressesAsync(host);
                if (!await Wait(lookup, 5000, token).ConfigureAwait(false))
                    return new Result(ResultKind.Review, L.Text("DNS zaman aşımı.", "DNS timeout."));
                var addresses = lookup.Result.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork ||
                    ip.AddressFamily == AddressFamily.InterNetworkV6).Distinct().Take(4).ToArray();
                if (addresses.Length == 0) return new Result(ResultKind.Review, L.Text("IP adresi bulunamadı.", "No IP address found."));
                foreach (var address in addresses) log("DNS → " + address);
                int replies = 0;
                using (var ping = new Ping())
                {
                    for (int i = 0; i < 4; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var reply = await ping.SendPingAsync(addresses[0], 1200).ConfigureAwait(false);
                            if (reply.Status == IPStatus.Success) replies++;
                            log("ICMP " + (i + 1) + "/4 · " + reply.Status + " · " + reply.RoundtripTime + " ms");
                        }
                        catch (PingException ex) { log("ICMP · " + ex.Message); break; }
                    }
                }
                bool connected = false;
                foreach (var address in addresses)
                {
                    token.ThrowIfCancellationRequested();
                    using (var client = new TcpClient(address.AddressFamily))
                    {
                        try
                        {
                            bool ok = await Wait(client.ConnectAsync(address, port), 2500, token).ConfigureAwait(false) && client.Connected;
                            connected |= ok;
                            log("TCP " + address + " : " + port + " · " + (ok ? "OK" : L.Text("Zaman aşımı", "Timeout")));
                        }
                        catch (SocketException ex) { log("TCP " + address + " · " + ex.SocketErrorCode); }
                    }
                }
                log(L.Text("Kapsam DNS, ICMP ve tek TCP portudur; TLS veya uygulama yanıtı doğrulanmaz.",
                    "Scope is DNS, ICMP and one TCP port; TLS and application responses are not validated."));
                return new Result(connected ? ResultKind.Done : ResultKind.Review,
                    "ICMP " + replies + "/4 · TCP " + (connected ? "OK" : L.Text("Erişilemedi", "Unreachable")));
            }
            catch (SocketException ex) { return new Result(ResultKind.Review, ex.Message); }
        }
    }
}

