using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace BakimMerkezi
{
    internal static class LocalDiagnostics
    {
        private const string UpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private static readonly string[] UpdateServices = { "wuauserv", "BITS", "cryptsvc", "UsoSvc", "WaaSMedicSvc" };
        private static readonly string[] CoreServices = { "EventLog", "RpcSs", "Dhcp", "Dnscache", "Winmgmt", "VSS", "Spooler" };
        private static readonly Regex ServicingMarkers = new Regex(
            @"\[SR\]|\b(?:error|corrupt(?:ion|ed)?|failed|missing)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool Handles(string id)
        {
            return id == "critical-services" || id == "update-services" || id == "servicing-logs" ||
                id == "dump-list" || id == "network-adapters" || id == "software-registry";
        }

        internal static Result Run(string id, Action<string> log, CancellationToken token)
        {
            switch (id)
            {
                case "critical-services": return Services(CoreServices.Concat(UpdateServices), log, token);
                case "update-services":
                    using (var root = Registry.LocalMachine.OpenSubKey(UpdatePolicy))
                    using (var automatic = Registry.LocalMachine.OpenSubKey(UpdatePolicy + @"\AU"))
                    {
                        log("WUServer = " + Field(root, "WUServer"));
                        log("UseWUServer = " + Field(automatic, "UseWUServer"));
                        log("NoAutoUpdate = " + Field(automatic, "NoAutoUpdate"));
                    }
                    return Services(UpdateServices, log, token);
                case "servicing-logs": return ServicingLogs(log, token);
                case "dump-list": return Dumps(log, token);
                case "network-adapters": return Adapters(log, token);
                case "software-registry": return Software(log, token);
                default: throw new ArgumentException("Unknown local diagnostic.");
            }
        }

        private static string Field(RegistryKey key, string name)
        {
            var value = key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value == null ? L.Text("Belirtilmedi", "Not specified") : Convert.ToString(value);
        }

        private static Result Services(IEnumerable<string> names, Action<string> log, CancellationToken token)
        {
            int read = 0, findings = 0;
            foreach (var name in names)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var service = new ServiceController(name))
                    {
                        var mode = service.StartType;
                        var state = service.Status;
                        log(name + " · " + mode + " · " + state);
                        read++;
                        if (mode == ServiceStartMode.Disabled ||
                            (mode == ServiceStartMode.Automatic && state == ServiceControllerStatus.Stopped)) findings++;
                    }
                }
                catch (InvalidOperationException ex) { findings++; log(name + " · " + ex.Message); }
                catch (System.ComponentModel.Win32Exception ex) { findings++; log(name + " · " + ex.Message); }
            }
            log(L.Text("Durdurulmuş el ile/tetiklemeli servis tek başına arıza sayılmaz.",
                "A stopped manual/trigger-start service is not by itself a fault."));
            return new Result(findings == 0 && read > 0 ? ResultKind.Done : ResultKind.Review,
                string.Format(L.Text("{0} servis okundu, {1} bulgu veya eksik okuma.", "{0} services read, {1} findings or unavailable reads."), read, findings));
        }

        internal static string ReadTail(Stream stream, int maxBytes)
        {
            if (stream == null || !stream.CanSeek || maxBytes < 4 || maxBytes > 8 * 1024 * 1024)
                throw new ArgumentException("Invalid log input.");
            long length = stream.Length;
            var bom = new byte[3];
            stream.Position = 0;
            int bytes = stream.Read(bom, 0, bom.Length);
            bool unicode = bytes >= 2 && ((bom[0] == 255 && bom[1] == 254) || (bom[0] == 254 && bom[1] == 255));
            Encoding encoding = unicode ? (bom[0] == 255 ? Encoding.Unicode : Encoding.BigEndianUnicode) : Encoding.UTF8;
            long start = Math.Max(0, length - maxBytes);
            if (unicode && (start & 1) != 0) start++;
            stream.Position = start;
            byte[] buffer = new byte[(int)(length - start)];
            int read = 0;
            while (read < buffer.Length)
            {
                int count = stream.Read(buffer, read, buffer.Length - read);
                if (count == 0) break;
                read += count;
            }
            string text = encoding.GetString(buffer, 0, read).TrimStart('\uFEFF');
            int firstLine = text.IndexOf('\n');
            return start > 0 ? (firstLine < 0 ? "" : text.Substring(firstLine + 1)) : text;
        }

        private static Result ServicingLogs(Action<string> log, CancellationToken token)
        {
            int readable = 0, matches = 0;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (string relative in new[] { @"Logs\CBS\CBS.log", @"Logs\DISM\dism.log" })
            {
                token.ThrowIfCancellationRequested();
                string path = Path.Combine(windows, relative);
                log(relative);
                try
                {
                    if (!Startup.SafeFolder(Path.GetDirectoryName(path)) ||
                        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException(L.Text("Bağlantılı dosya atlandı.", "Linked file skipped."));
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        string text = ReadTail(file, 1024 * 1024);
                        using (var reader = new StringReader(text))
                        {
                            var last = new Queue<string>(35);
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                token.ThrowIfCancellationRequested();
                                if (!ServicingMarkers.IsMatch(line)) continue;
                                if (last.Count == 35) last.Dequeue();
                                last.Enqueue(line.Length > 1400 ? line.Substring(0, 1400) : line);
                            }
                            foreach (string item in last) log(item);
                            matches += last.Count;
                        }
                    }
                    readable++;
                }
                catch (IOException ex) { log(ex.Message); }
                catch (UnauthorizedAccessException ex) { log(ex.Message); }
            }
            return new Result(readable == 2 && matches == 0 ? ResultKind.Done : ResultKind.Review,
                string.Format(L.Text("{0}/2 günlük okundu, {1} satır gösterildi. Yalnızca yakın günlük aralığı tarandı.",
                    "{0}/2 logs read, {1} lines shown. Only the recent log range was scanned."), readable, matches));
        }

        private static Result Dumps(Action<string> log, CancellationToken token)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string small = Path.Combine(windows, "Minidump");
            int count = 0, unavailable = 0;
            try
            {
                var files = new List<string> { Path.Combine(windows, "MEMORY.DMP") };
                try
                {
                    var attributes = File.GetAttributes(small);
                    if ((attributes & FileAttributes.Directory) != 0 && Startup.SafeFolder(small))
                        files.AddRange(Directory.EnumerateFiles(small, "*.dmp", SearchOption.TopDirectoryOnly).Take(100));
                    else unavailable++;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (UnauthorizedAccessException ex) { unavailable++; log(ex.Message); }
                catch (IOException ex) { unavailable++; log(ex.Message); }
                foreach (var path in files)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var file = new FileInfo(path);
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || !file.Exists) continue;
                        log(file.Name + " · " + file.LastWriteTimeUtc.ToString("u") + " · " + Maintenance.Size(file.Length));
                        count++;
                    }
                    catch (FileNotFoundException) { }
                    catch (UnauthorizedAccessException) { unavailable++; }
                    catch (IOException) { unavailable++; }
                }
            }
            catch (UnauthorizedAccessException ex) { unavailable++; log(ex.Message); }
            catch (IOException ex) { unavailable++; log(ex.Message); }
            return new Result(count > 0 || unavailable > 0 ? ResultKind.Review : ResultKind.Done,
                string.Format(L.Text("{0} döküm girdisi, {1} erişim sorunu. Bellek verisi okunmadı.",
                    "{0} dump entries, {1} access issues. Memory data was not read."), count, unavailable));
        }

        private static Result Adapters(Action<string> log, CancellationToken token)
        {
            int read = 0, failed = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Take(100))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var properties = nic.GetIPProperties();
                    log(nic.Name + " · " + nic.NetworkInterfaceType + " · " + nic.OperationalStatus +
                        " · " + (nic.Speed >= 0 ? (nic.Speed / 1000000.0).ToString("0.##") + " Mb/s" : "—") +
                        " · MAC " + nic.GetPhysicalAddress());
                    log("  IP: " + string.Join(", ", properties.UnicastAddresses.Select(ip => ip.Address.ToString())));
                    log("  Gateway: " + string.Join(", ", properties.GatewayAddresses.Select(ip => ip.Address.ToString())));
                    log("  DNS: " + string.Join(", ", properties.DnsAddresses.Select(ip => ip.ToString())));
                    read++;
                }
                catch (NetworkInformationException ex) { failed++; log(nic.Name + " · " + ex.Message); }
            }
            return new Result(read > 0 && failed == 0 ? ResultKind.Done : ResultKind.Review,
                string.Format(L.Text("{0} arabirim okundu, {1} hata.", "{0} interfaces read, {1} errors."), read, failed));
        }

        private static Result Software(Action<string> log, CancellationToken token)
        {
            const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int missing = 0;
            var views = Environment.Is64BitOperatingSystem ?
                new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Default };
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in views)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var key = root.OpenSubKey(uninstall))
                    {
                        if (key == null) continue;
                        var names = key.GetSubKeyNames();
                        if (names.Length > 2500) missing++;
                        foreach (string subkey in names.Take(2500))
                        {
                            token.ThrowIfCancellationRequested();
                            using (var entry = key.OpenSubKey(subkey))
                            {
                                if (entry == null || (entry.GetValue("DisplayName") as string ?? "").Trim().Length == 0) continue;
                                entries.Add(hive + " · " + Field(entry, "DisplayName") + " · " +
                                    Field(entry, "DisplayVersion") + " · " + Field(entry, "Publisher"));
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException ex) { missing++; log(ex.Message); }
                catch (System.Security.SecurityException ex) { missing++; log(ex.Message); }
                catch (IOException ex) { missing++; log(ex.Message); }
            }
            foreach (string entry in entries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(2000)) log(entry);
            return new Result(missing == 0 && entries.Count <= 2000 ? ResultKind.Done : ResultKind.Review,
                string.Format(L.Text("{0} kayıt bulundu, {1} gösterildi, {2} kayıt görünümü eksik.",
                    "{0} entries found, {1} shown, {2} registry views incomplete."), entries.Count, Math.Min(2000, entries.Count), missing));
        }
    }
}

