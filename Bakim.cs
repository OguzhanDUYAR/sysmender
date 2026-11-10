using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BakimMerkezi
{
    internal sealed class MaintenanceTask
    {
        internal readonly string Id;
        private readonly string group, title, detail, impact;
        internal string Group { get { return L.Translate(group); } }
        internal string Title { get { return L.Translate(title); } }
        internal string Detail { get { return L.Translate(detail); } }
        internal string Impact { get { return L.Translate(impact); } }
        internal readonly bool Admin, Changes, NeedsDrive;

        internal MaintenanceTask(string id, string group, string title, string detail, string impact,
            bool admin = false, bool changes = false, bool needsDrive = false)
        {
            Id = id;
            this.group = group;
            this.title = title;
            this.detail = detail;
            this.impact = impact;
            Admin = admin;
            Changes = changes;
            NeedsDrive = needsDrive;
        }
    }

    internal static class Maintenance
    {
        internal static readonly List<MaintenanceTask> Tasks = new List<MaintenanceTask>
        {
            new MaintenanceTask("inventory", "Tanılama", "Sistem özeti",
                "Windows sürümü, disk alanı ve bekleyen yeniden başlatma işaretlerini gösterir.", "Yerel inceleme"),
            new MaintenanceTask("events", "Tanılama", "Disk ve donanım olayları",
                "Sistem günlüğünde son 7 günün disk, NTFS, depolama ve WHEA hatalarını ve uyarılarını inceler.", "Yerel inceleme"),
            new MaintenanceTask("network", "Tanılama", "Microsoft bağlantı testi",
                "Windows Update, Store ve Xbox uç noktaları için DNS ve TCP 443 erişimini, ayrıca WinHTTP proxy ayarını gösterir.", "Ağ bağlantısı"),
            new MaintenanceTask("store-check", "Store ve Xbox", "Paket ve hizmet durumu",
                "Oturumdaki Store, Xbox ve Gaming Services paketlerini ve ilgili hizmetlerin durumunu listeler.", "Yerel inceleme"),
            new MaintenanceTask("store-repair", "Store ve Xbox", "Microsoft Store kaydını onar",
                "Oturumdaki Store ve Store Purchase App paketlerini yeniden kaydeder. Önce Store'u kapatın.", "Paket kaydını değiştirir", false, true),
            new MaintenanceTask("xbox-repair", "Store ve Xbox", "Xbox paket kaydını onar",
                "Oturumdaki Xbox ve Gaming Services paketlerini yeniden kaydeder. Önce açık oyunları ve Xbox uygulamasını kapatın.", "Paket kaydını değiştirir", false, true),
            new MaintenanceTask("temp-preview", "Depolama", "Geçici dosyaları önizle",
                "Bu hesabın Temp klasöründe 7 günden eski dosyaları sayar. Bağlantıları, kilitli dosyaları ve yakın zamanda oluşturulan dosyaları atlar.", "Önizleme"),
            new MaintenanceTask("temp-clean", "Depolama", "Önizlenen geçici dosyaları temizle",
                "Bu oturumdaki son önizlemede görülen ve o tarihten beri değişmemiş dosyaları siler. Silme kalıcıdır.", "Kalıcı dosya silme", false, true),
            new MaintenanceTask("physical", "Depolama", "Fiziksel disk sağlığı",
                "Windows'un bildirdiği disk sağlığını ve denetleyici sunuyorsa sıcaklık, aşınma ve hata sayaçlarını okur.", "Yerel inceleme", true),
            new MaintenanceTask("dism-check", "Windows", "Bileşen deposu durumunu kontrol et",
                "DISM CheckHealth ile Windows'un önceden kaydettiği bozulma durumuna bakar.", "Hızlı kontrol", true),
            new MaintenanceTask("dism-scan", "Windows", "Bileşen deposunu derin tara",
                "DISM ScanHealth ile bileşen deposunu ayrıntılı tarar. Büyük sistemlerde zaman alabilir.", "Yoğun disk kullanımı", true),
            new MaintenanceTask("wu-search", "Windows", "Windows Update taraması",
                "Windows Update aracısıyla, bilgisayarın kendi güncelleme kaynağında uygulanabilir güncellemeleri arar.", "Ağ bağlantısı", true),
            new MaintenanceTask("restore", "Windows", "Geri yükleme noktası oluştur",
                "Windows Sistem Koruması üzerinden bir nokta oluşturur ve listede doğrular. Sistem Koruması açık olmalıdır.", "Sistem koruması", true, true),
            new MaintenanceTask("dism-repair", "Windows", "Bileşen deposunu onar",
                "DISM RestoreHealth ile Windows bileşen deposunu onarır. Windows Update üzerinden dosya indirebilir.", "Sistem onarımı", true, true),
            new MaintenanceTask("sfc", "Windows", "Windows sistem dosyalarını onar",
                "SFC ile korunan sistem dosyalarını tarar ve onarır. DISM onarımı da seçildiyse onun ardından çalışır.", "Sistem onarımı", true, true),
            new MaintenanceTask("chkdsk", "Depolama", "NTFS sürücüsünü çevrimiçi tara",
                "CHKDSK /scan ile seçilen yerel, sabit NTFS sürücüsünü tarar. Windows çevrimiçi onarım yapabilir.", "Disk taraması / onarım", true, true, true),
            new MaintenanceTask("component-clean", "Depolama", "Eski Windows bileşenlerini temizle",
                "Windows'un DISM bileşen temizliğini çalıştırır. Windows güncellemelerinin geri alma tabanını sıfırlayan /ResetBase seçeneğini kullanmaz.",
                "Sistem bileşeni temizliği", true, true),
            new MaintenanceTask("flush", "Ağ", "DNS önbelleğini temizle",
                "Windows DNS istemcisinin geçici çözümleme önbelleğini boşaltır.", "Önbelleği temizler", true, true)
        };

        internal static Job Create(MaintenanceTask task, string drive, TempCleanup cleanup)
        {
            string volume = task.NeedsDrive ? VolumeKey(drive) : null;
            return new Job(task.Id, task.Title, (log, token) => Run(task, drive, volume, cleanup, log, token));
        }

        private static Result Run(MaintenanceTask task, string drive, string volume, TempCleanup cleanup, Action<string> log, CancellationToken token)
        {
            if (task.Admin != Program.Admin)
                throw new InvalidOperationException(L.Translate("Bu işlem için uygun oturum türünü açın."));

            switch (task.Id)
            {
                case "inventory": return Inventory(log);
                case "events": return Events(log, token);
                case "network": return Network(log, token);
                case "temp-preview": return cleanup.Preview(log, token);
                case "temp-clean": return cleanup.Clean(log);
                case "physical": return Script("disk", log, token, TimeSpan.FromMinutes(3));
                case "wu-search": return Script("wu", log, token, TimeSpan.FromMinutes(20));
                case "restore": return Script("restore", log);
                case "store-check": return Script("store-check", log, token, TimeSpan.FromMinutes(2));
                case "store-repair": return Script("store-repair", log);
                case "xbox-repair": return Script("xbox-repair", log);
                case "dism-check": return Dism("/CheckHealth", log);
                case "dism-scan": return Dism("/ScanHealth", log);
                case "dism-repair": return Dism("/RestoreHealth", log);
                case "component-clean": return Dism("/StartComponentCleanup", log);
                case "sfc":
                    if (RebootReasons(false).Any()) return WaitForReboot();
                    return ClassifySfc(SystemCommand("sfc.exe", "/scannow", Encoding.Unicode, log));
                case "chkdsk":
                    if (volume == null || !Drives().Contains(drive, StringComparer.OrdinalIgnoreCase) || volume != VolumeKey(drive))
                        return new Result(ResultKind.Skipped, L.Text("Seçilen NTFS birimi değişti veya doğrulanamadı. Sürücüyü yeniden seçin.",
                            "The selected NTFS volume changed or could not be verified. Select the drive again."));
                    log(drive + " → " + volume);
                    return ClassifyDisk(SystemCommand("chkdsk.exe", volume + " /scan", Commands.Oem, log).Code);
                case "flush":
                    return Generic(SystemCommand("ipconfig.exe", "/flushdns", Commands.Oem, log), L.Translate("DNS önbelleği temizlendi."));
                default:
                    throw new ArgumentException(L.Translate("Bilinmeyen bakım işlemi."));
            }
        }

        internal static string VolumeKey(string drive)
        {
            if (string.IsNullOrEmpty(drive) || !Regex.IsMatch(drive, @"\A[A-Z]:\\\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;
            var name = new StringBuilder(64);
            if (!GetVolumeNameForVolumeMountPoint(drive, name, (uint)name.Capacity)) return null;
            string path = name.ToString().TrimEnd('\\');
            const string prefix = @"\\?\Volume";
            Guid id;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(path.Substring(prefix.Length), "B", out id)
                ? prefix + id.ToString("B") : null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volume, uint capacity);

        internal static List<string> Drives()
        {
            return DriveInfo.GetDrives().Where(drive =>
            {
                try
                {
                    return drive.DriveType == DriveType.Fixed && drive.IsReady &&
                        drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase) &&
                        Regex.IsMatch(drive.Name, @"\A[A-Z]:\\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }).Select(drive => drive.Name).ToList();
        }

        private static Result Inventory(Action<string> log)
        {
            string system = Path.GetPathRoot(Environment.SystemDirectory);
            var os = Environment.OSVersion.Version;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                string display = key == null ? "" : Convert.ToString(key.GetValue("DisplayVersion", ""));
                string edition = key == null ? "" : Convert.ToString(key.GetValue("EditionID", ""));
                object revision = key == null ? null : key.GetValue("UBR");
                log(string.Format(L.Translate("Windows {0} {1} · {2} · derleme {3}{4}"),
                    os.Build >= 22000 ? "11" : "10", display, edition, os.Build, revision == null ? "" : "." + revision));
            }
            log(Environment.Is64BitOperatingSystem ? L.Translate("64 bit Windows") : L.Translate("32 bit Windows"));
            log(L.Translate("Sistem sürücüsü ") + system);
            var reasons = RebootReasons(true).ToArray();
            log(reasons.Length == 0 ? L.Translate("Yeniden başlatma işareti bulunmadı.") : L.Translate("Yeniden başlatma işaretleri"));
            foreach (string reason in reasons) log("  " + reason);
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.Fixed))
            {
                try
                {
                    if (drive.IsReady)
                        log(string.Format(L.Translate("{0}  {1}  {2} kullanılabilir / {3} toplam"), drive.Name,
                            drive.DriveFormat, Size(drive.AvailableFreeSpace), Size(drive.TotalSize)));
                }
                catch (IOException ex) { log(drive.Name + " " + ex.Message); }
                catch (UnauthorizedAccessException ex) { log(drive.Name + " " + ex.Message); }
            }
            return reasons.Length == 0 ? new Result(ResultKind.Done, L.Translate("Sistem özeti hazır.")) :
                new Result(ResultKind.Review, L.Translate("Bekleyen yeniden başlatma işaretleri var."));
        }

        private static IEnumerable<string> RebootReasons(bool includeFileRenames)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                if (key != null) yield return L.Translate("Windows bileşen servisi");
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                if (key != null) yield return "Windows Update";
            if (!includeFileRenames) yield break;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
            {
                var entries = key == null ? null : key.GetValue("PendingFileRenameOperations") as string[];
                if (entries != null && entries.Any(entry => !string.IsNullOrWhiteSpace(entry)))
                    yield return L.Translate("Bekleyen dosya işlemleri");
            }
        }

        private static Result WaitForReboot()
        {
            return new Result(ResultKind.Skipped, L.Translate("Windows yeniden başlatma bekliyor. Yeniden başlattıktan sonra tekrar çalıştırın."), true);
        }

        private static Result Events(Action<string> log, CancellationToken token)
        {
            var providers = new[] { "disk", "Ntfs", "Microsoft-Windows-Ntfs", "storahci", "stornvme",
                "storport", "Microsoft-Windows-StorPort", "Microsoft-Windows-WHEA-Logger", "volmgr" };
            string query = "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 604800000] and (" +
                string.Join(" or ", providers.Select(name => "Provider[@Name='" + name + "']")) + ")]]";
            int count = 0;
            using (var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query) { ReverseDirection = true }))
            {
                while (count < 100)
                {
                    token.ThrowIfCancellationRequested();
                    using (var item = reader.ReadEvent())
                    {
                        if (item == null) break;
                        string detail;
                        try { detail = item.FormatDescription(); }
                        catch (EventLogException) { detail = L.Translate("Olay açıklaması bu sistemde bulunamadı."); }
                        log(string.Format(L.Translate("{0:yyyy-MM-dd HH:mm:ss} · {1} · Kimlik {2} · {3}"),
                            item.TimeCreated, item.ProviderName, item.Id, detail));
                        count++;
                    }
                }
            }
            return count == 0 ? new Result(ResultKind.Done, L.Translate("Son 7 günde eşleşen olay bulunmadı.")) :
                new Result(ResultKind.Review, count + (count == 100 ? "+ " : " ") + L.Translate("olay bulundu. Tarih ve ayrıntılar günlükte."));
        }

        private static Result Network(Action<string> log, CancellationToken token)
        {
            var hosts = new[] { "download.windowsupdate.com", "www.microsoft.com", "www.xbox.com" };
            var probes = Task.WhenAll(hosts.Select(host => Probe(host, token)));
            bool all;
            try
            {
                log("netsh.exe winhttp show proxy");
                all = Commands.Run(Commands.SystemTool("netsh.exe"), "winhttp show proxy", Commands.Oem, log,
                    token: token, probeTimeout: TimeSpan.FromSeconds(10)).Code == 0;
            }
            catch (OperationCanceledException) { ObserveFailure(probes); throw; }
            catch (Exception ex) { log(ex.Message); all = false; }
            foreach (var result in probes.GetAwaiter().GetResult())
            {
                log(result.Message);
                all &= result.Kind == ResultKind.Done;
            }
            log(L.Translate("Kapsam: DNS ve TCP 443. Oturum açma, TLS ve uygulama sunucusunun yanıtı ayrı kontrollerdir."));
            return new Result(all ? ResultKind.Done : ResultKind.Review,
                all ? L.Translate("Örnek uç noktalar erişilebilir.") : L.Translate("En az bir ağ kontrolü tamamlanamadı. VPN, proxy ve güvenlik duvarı sonuçları etkileyebilir."));
        }

        private static async Task<Result> Probe(string host, CancellationToken token)
        {
            try
            {
                var lookup = Dns.GetHostAddressesAsync(host);
                if (!await WaitProbe(lookup, 4500, token).ConfigureAwait(false))
                    return new Result(ResultKind.Review, host + L.Translate(" · DNS zaman aşımı"));
                var addresses = (await lookup.ConfigureAwait(false)).Where(ip => ip.AddressFamily == AddressFamily.InterNetwork ||
                    ip.AddressFamily == AddressFamily.InterNetworkV6).Take(8).ToArray();
                bool[] attempts = await Task.WhenAll(addresses.Select(address => ProbeTcp(address, token))).ConfigureAwait(false);
                bool ok = attempts.Any(attempt => attempt);
                return new Result(ok ? ResultKind.Done : ResultKind.Review,
                    host + " · DNS " + addresses.Length + L.Translate(" adres · TCP 443 ") + (ok ? L.Translate("erişilebilir") : L.Translate("erişilemedi")));
            }
            catch (SocketException ex)
            {
                return new Result(ResultKind.Review, host + " · " + ex.Message);
            }
        }

        private static async Task<bool> ProbeTcp(IPAddress address, CancellationToken token)
        {
            try
            {
                using (var client = new TcpClient(address.AddressFamily))
                    return await WaitProbe(client.ConnectAsync(address, 443), 3500, token).ConfigureAwait(false) && client.Connected;
            }
            catch (SocketException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static async Task<bool> WaitProbe(Task work, int milliseconds, CancellationToken token)
        {
            if (await Task.WhenAny(work, Task.Delay(milliseconds, token)).ConfigureAwait(false) != work)
            {
                ObserveFailure(work);
                token.ThrowIfCancellationRequested();
                return false;
            }
            await work.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return true;
        }

        private static void ObserveFailure(Task work)
        {
            work.ContinueWith(faulted => faulted.Exception.Handle(error => true),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static Result Script(string action, Action<string> log, CancellationToken token = default(CancellationToken),
            TimeSpan? limit = null)
        {
            string source = Program.Resource("Bakim.ps1");
            var result = Commands.PowerShell("$Action = '" + action + "';\n" + source, log, token, limit);
            if (result.Code == 0) return new Result(ResultKind.Done, L.Translate("İşlem tamamlandı. Ayrıntılar günlükte."));
            if (result.Code == 2) return new Result(ResultKind.Review, L.Translate("Windows ek inceleme gerektiren bir durum bildirdi."));
            return Generic(result, "");
        }

        private static CommandOutput SystemCommand(string name, string args, Encoding encoding, Action<string> log)
        {
            log(name + " " + args);
            return Commands.Run(Commands.SystemTool(name), args, encoding, log);
        }

        private static Result Dism(string action, Action<string> log)
        {
            if ((action == "/RestoreHealth" || action == "/StartComponentCleanup") && RebootReasons(false).Any())
                return WaitForReboot();
            var result = SystemCommand("dism.exe", "/Online /Cleanup-Image " + action + " /English", Commands.Oem, log);
            log(L.Translate("Windows DISM günlüğü: ") + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Logs\DISM\dism.log"));
            if (result.Code == 3010 || result.Code == 1641)
                return new Result(ResultKind.Review, L.Translate("DISM tamamlandı ve yeniden başlatma gerekiyor."), true);
            if (result.Code != 0) return Generic(result, "");
            if (action == "/CheckHealth" || action == "/ScanHealth")
            {
                if (result.Tail.IndexOf("No component store corruption detected", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new Result(ResultKind.Done, L.Translate("DISM bileşen deposunda bozulma bildirmedi."));
                return new Result(ResultKind.Review, L.Translate("Bileşen deposu bulguları günlükte. Çıkış kodu: 0."));
            }
            return new Result(ResultKind.Done, L.Translate("DISM işlemi tamamlandı."));
        }

        internal static Result ClassifyDisk(int code)
        {
            switch (code)
            {
                case 0: return new Result(ResultKind.Done, L.Translate("CHKDSK hata bulmadı."));
                case 1: return new Result(ResultKind.Done, L.Translate("CHKDSK hata buldu ve düzeltti."));
                case 2: return new Result(ResultKind.Review, L.Translate("CHKDSK temizlik işlemi bildirdi. Çıktıyı inceleyin."));
                case 3: return new Result(ResultKind.Review, L.Translate("CHKDSK tarayamadı veya tüm hataları düzeltemedi. Çıktıyı inceleyin."));
                default: return Generic(new CommandOutput(code, ""), "");
            }
        }

        internal static Result ClassifySfc(CommandOutput output)
        {
            if (output.Code == 3010 || output.Code == 1641)
                return new Result(ResultKind.Review, L.Translate("SFC yeniden başlatma gerektirdi."), true);
            if (output.Code != 0) return Generic(output, "");
            if (Regex.IsMatch(output.Tail, "did not find any integrity violations|herhangi bir bütünlük ihlali bulmadı",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return new Result(ResultKind.Done, L.Translate("SFC bütünlük ihlali bildirmedi."));
            if (Regex.IsMatch(output.Tail, "found corrupt files and successfully repaired them|ve bunları başarıyla onardı",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return new Result(ResultKind.Done, L.Translate("SFC bozuk dosyaları onardığını bildirdi."));
            return new Result(ResultKind.Review, L.Translate("SFC tamamlandı. Sonuç için komut çıktısını veya Windows Logs\\CBS\\CBS.log dosyasını inceleyin."));
        }

        internal static Result Generic(CommandOutput output, string success)
        {
            return output.Code == 0 ? new Result(ResultKind.Done, success) :
                new Result(ResultKind.Failed, L.Translate("Windows aracı hata kodu döndürdü: ") + Commands.Code(output.Code) + ".");
        }

        internal static string Size(long bytes)
        {
            var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
            int unit = 0;
            double value = bytes;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return string.Format("{0:0.#} {1}", value, units[unit]);
        }
    }
}
