using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal static partial class Verification
    {
        private static int failures;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string folder = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(folder);
            if (args.Length > 1 && args[1] == "--bt-okuma") return ItReadOnlySweep(folder);
            Test("61 paket ve benzersiz kimlikler", () =>
            {
                var apps = Packages.Load();
                Check(apps.Count == 61 && apps.Any(app => app.Id == "7zip.7zip"));
                bool refused = false;
                try { Packages.Create("install", new Package("Test", "Test", "Test --all")); }
                catch (ArgumentException) { refused = true; }
                Check(refused);
                Check(Packages.Classify("updates", unchecked((int)0x8A15002B)).Kind == ResultKind.Done);
                Check(Packages.Classify("install", unchecked((int)0x8A15010A)).Reboot);
                Check(Packages.Classify("install", unchecked((int)0x8A15010C)).Kind == ResultKind.Skipped);
            });
            Test("DISM bağımlılığı, işlem kuyruğu ve günlük", () => Queue(folder));
            Test("Okuma aracında iptal ve zaman aşımı", ReadOnlyCancellation);
            Test("Yönetici günlük izinleri ve çocuk süreç ortamı", AdminIsolation);
            Test("Güvensiz eski günlük klasöründen korumalı konuma geçiş", () => AdminLogRecovery(folder));
            Test("WinGet kurulu paket listesi ve korunan kimlikler", InstalledInventory);
            Test("Store kaldırma kapsamı ve yeniden doğrulama", StoreRemovalScope);
            Test("Başlangıç kısayolu devre dışı bırakma, çakışma ve geri yükleme", () => StartupShortcut(folder));
            Test("İsteğe bağlı servis ve geri yükleme modu sınırları", ServiceScope);
            Test("SFC ve CHKDSK sonuçları", () =>
            {
                Check(Maintenance.VolumeKey(@"..\C:\") == null && Maintenance.VolumeKey(@"Z:\something\") == null);
                Check(Maintenance.ClassifyDisk(1).Kind == ResultKind.Done);
                Check(Maintenance.ClassifyDisk(3).Kind == ResultKind.Review);
                Check(Maintenance.ClassifySfc(new CommandOutput(0, "Localized unknown text")).Kind == ResultKind.Review);
                Check(Maintenance.ClassifySfc(new CommandOutput(0,
                    "Windows Resource Protection did not find any integrity violations.")).Kind == ResultKind.Done);
            });
            Test("Önizleme ile sınırlı güvenli Temp temizliği", () => Temp(folder));
            Test("Ayar yedeği, harici değişiklik ve geri alma", () => RegistryRoundTrip(folder));
            Test("PowerShell ayrıştırma ve Unicode komut çıktısı", () =>
            {
                var lines = new List<string>();
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Program.Resource("Bakim.ps1")));
                var result = Commands.PowerShell("$text=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded +
                    "')); $tokens=$null; $errors=$null; [void][Management.Automation.Language.Parser]::ParseInput($text," +
                    "[ref]$tokens,[ref]$errors); if($errors.Count){ throw ($errors -join ' / ') }; 'Türkçe çıkış: ğİşçüö'", lines.Add);
                if (result.Code != 0) throw new InvalidOperationException(string.Join(" | ", lines));
                Check(lines.Contains("Türkçe çıkış: ğİşçüö"));
            });
            Test("Windows arayüzü, profil seçimi, filtre ve yönetici görünümü", () => Interface(folder));
            Test("CPU sayaç sınırları, grafik belleği ve canlı izleme yaşam döngüsü", () => MonitorVerification(folder));
            Test("İngilizce arayüz, toplu kaldırma seçimi ve servis görünümü", () => EnglishInterface(folder));
            Test("BT kataloğu, ayrıştırma, yetki ve onarım sonuçları", () => ItCatalogVerification(folder));
            Test("WinUtil uyarlaması, yeniden başlatma ve belirsiz durum", ReadinessVerification);
            Test("BT hedef doğrulaması ve yerel TCP denemesi", ItTargetVerification);
            Test("BT arayüzü, profil, filtre ve rapor güvenliği", () => ItInterfaceVerification(folder));
            Test("Yerel dosya tarama, SHA-256 doğrulama ve iptal", () => FileWorkspace(folder));
            Test("Sorun giderme akışları, rapor karşılaştırma ve günlük sınırı", () => SupportWorkflow(folder));
            Test("Ağ sayaç sıfırlanması, açık tema ve çalışma alanı araması", () => ProfessionalUi(folder));
            Console.WriteLine(failures == 0 ? "Uygulanabilen doğrulamalar tamamlandı." : failures + " doğrulama başarısız.");
            return failures == 0 ? 0 : 1;
        }

        private static void Test(string name, Action test)
        {
            try { test(); Console.WriteLine("OK  " + name); }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("FAIL  " + name + " | " + ex);
            }
        }

        private static void Check(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Beklenen durum doğrulanamadı.");
        }

        private static void InstalledInventory()
        {
            const string community = @"""Name"":""winget"",""Identifier"":""Microsoft.Winget.Source_8wekyb3d8bbwe"",
                ""Type"":""Microsoft.PreIndexed.Package""";
            const string address = "https://cdn.winget.microsoft.com/cache";
            string json = @"{""Sources"":[{""SourceDetails"":{" + community + @",""Argument"":""" + address + @"""},""Packages"":[
                {""PackageIdentifier"":""7zip.7zip"",""Version"":""24.09""},
                {""PackageIdentifier"":""Vendor.Unknown""},{""PackageIdentifier"":""7ZIP.7ZIP""},
                {""PackageIdentifier"":""Test --all""},{""PackageIdentifier"":""Microsoft.EdgeWebView2Runtime""},
                {""PackageIdentifier"":""Microsoft.VCRedist.2015+.x64""}]},
                {""SourceDetails"":{""Name"":""other""},""Packages"":[{""PackageIdentifier"":""Vendor.Blocked""}]}]}";
            var items = InstalledApps.ParseWinGet(json, Packages.Load());
            Check(items.Count == 2 && items.Any(item => item.Id == "7zip.7zip" && item.Name == "7-Zip" && item.Version == "24.09"));
            Check(items.Any(item => item.Name == "Vendor.Unknown" && item.Source == "WinGet"));
            Check(Packages.IsCommunitySourceJson("{" + community + ",\"Arg\":\"" + address + "\"}"));
            Check(!Packages.IsCommunitySourceJson("{" + community + ",\"Arg\":\"https://invalid.example/cache\"}"));
            Check(!Packages.IsCommunitySourceJson("{not valid JSON}"));
            bool badSource = false;
            try { InstalledApps.ParseWinGet(json.Replace(address, "https://invalid.example/cache"), Packages.Load()); }
            catch (InvalidDataException) { badSource = true; }
            Check(badSource);
            Check(Packages.Create("uninstall", new Package("", "7-Zip", "7zip.7zip"), "1.0\" --all").Run(_ => { }).Kind ==
                ResultKind.Skipped);
            Check(!Packages.CanUninstall("Microsoft.Edge") && !Packages.CanUninstall("Microsoft.DotNet.DesktopRuntime.8"));
            bool refused = false;
            try { Packages.Create("uninstall", new Package("", "Test", "Microsoft.DesktopAppInstaller")); }
            catch (ArgumentException) { refused = true; }
            Check(refused);
        }

        private static void StoreRemovalScope()
        {
            string simulation = @"
                $script:apps=@(
                    [pscustomobject]@{Name='Microsoft.BingNews';PublisherId='8wekyb3d8bbwe';NonRemovable=$false;IsBundle=$true;Version='1.2.3.4';PackageFullName='allowed-bundle'},
                    [pscustomobject]@{Name='Microsoft.BingNews';PublisherId='8wekyb3d8bbwe';NonRemovable=$false;IsBundle=$false;Version='1.2.3.4';PackageFullName='allowed-main'},
                    [pscustomobject]@{Name='Microsoft.BingNews';PublisherId='wrong';NonRemovable=$false;Version='1.2.3.4';PackageFullName='wrong-publisher'},
                    [pscustomobject]@{Name='Microsoft.WindowsMaps';PublisherId='8wekyb3d8bbwe';NonRemovable=$true;Version='1.2.3.4';PackageFullName='protected'}
                );
                function Get-AppxPackage { param($Name,$PackageTypeFilter); $script:apps | Where-Object { !$Name -or $_.Name -eq $Name } }
                function Remove-AppxPackage { param($Package,$Confirm); if($Package -ne 'allowed-bundle'){ throw 'Wrong package' }; 'removed ' + $Package; $script:apps=@($script:apps | Where-Object PublisherId -ne '8wekyb3d8bbwe') }
            ";
            string script = Program.Resource("Kaldirma.ps1");
            var lines = new List<string>();
            var result = Commands.PowerShell(simulation + "$Allowed=@('Microsoft.BingNews|8wekyb3d8bbwe','Microsoft.WindowsMaps|8wekyb3d8bbwe');$Remove='';\n" +
                script, lines.Add);
            if (result.Code != 0) throw new InvalidOperationException(string.Join(" | ", lines));
            Check(lines.SequenceEqual(new[] { "BAKIM-APP|Microsoft.BingNews|1.2.3.4" }));
            lines.Clear();
            result = Commands.PowerShell(simulation + "$Allowed=@('Microsoft.WindowsMaps|8wekyb3d8bbwe');$Remove='Microsoft.WindowsMaps';$ExpectedVersion='1.2.3.4';\n" +
                script, lines.Add);
            Check(result.Code == 2 && lines.Count == 0);
            const string news = "$Allowed=@('Microsoft.BingNews|8wekyb3d8bbwe');$Remove='Microsoft.BingNews';";
            result = Commands.PowerShell(simulation + news + "$ExpectedVersion='1.2.3.3';\n" + script, lines.Add);
            Check(result.Code == 2 && lines.Count == 0);
            result = Commands.PowerShell(simulation + news + "$ExpectedVersion='1.2.3.4';\n" +
                script, lines.Add);
            if (result.Code != 0) throw new InvalidOperationException(string.Join(" | ", lines));
            Check(lines.SequenceEqual(new[] { "removed allowed-bundle" }));
        }

        private static void StartupShortcut(string folder)
        {
            string live = Directory.CreateDirectory(Path.Combine(folder, "startup-live")).FullName;
            string saved = Path.Combine(folder, "startup-saved");
            string name = "Sample.lnk";
            string original = Path.Combine(live, name), backup = Path.Combine(saved, name);
            File.WriteAllText(original, "first");
            var stale = new ShortcutStamp(new FileInfo(original));
            File.WriteAllText(original, "original shortcut fixture");
            Check(Startup.MoveShortcutBetween(live, saved, name, true, stale).Kind == ResultKind.Review);
            Check(Startup.MoveShortcutBetween(live, saved, name, true).Kind == ResultKind.Done);
            Check(!File.Exists(original) && File.ReadAllText(backup) == "original shortcut fixture");
            File.WriteAllText(original, "new entry");
            Check(Startup.MoveShortcutBetween(live, saved, name, false).Kind == ResultKind.Review);
            Check(File.ReadAllText(original) == "new entry" && File.Exists(backup));
            File.Delete(original);
            Check(Startup.MoveShortcutBetween(live, saved, name, false).Kind == ResultKind.Done);
            Check(File.ReadAllText(original) == "original shortcut fixture" && !File.Exists(backup));
            Check(Startup.MoveShortcutBetween(live, saved, @"..\Outside.lnk", true).Kind == ResultKind.Skipped);
        }

        private static void ServiceScope()
        {
            Check(OptionalServices.IsAllowed("Fax") && OptionalServices.IsAllowed("MapsBroker"));
            Check(OptionalServices.MatchesExactService("Fax", "FAX") && !OptionalServices.MatchesExactService("Fax", "OtherService"));
            Check(!OptionalServices.IsAllowed("WinDefend") && !OptionalServices.IsAllowed("wuauserv") &&
                !OptionalServices.IsAllowed("RpcSs") && !OptionalServices.IsAllowed("GamingServices"));
            Check(OptionalServices.IsRestorable("delayed-auto") && OptionalServices.IsRestorable("demand") &&
                !OptionalServices.IsRestorable("boot") && !OptionalServices.IsRestorable("disabled"));
        }

        private static void ReadOnlyCancellation()
        {
            using (var stop = new CancellationTokenSource())
            {
                stop.CancelAfter(600);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bool cancelled = false;
                try { Commands.PowerShell("Start-Sleep -Seconds 12", _ => { }, stop.Token, TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && watch.Elapsed < TimeSpan.FromSeconds(6));
            }
            bool timedOut = false;
            try { Commands.PowerShell("Start-Sleep -Seconds 12", _ => { }, probeTimeout: TimeSpan.FromMilliseconds(500)); }
            catch (TimeoutException) { timedOut = true; }
            Check(timedOut);
        }

        private static void AdminIsolation()
        {
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var acl = MachineFiles.CreateSecurity();
            Check(MachineFiles.Trusted(acl));
            acl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Write, AccessControlType.Allow));
            Check(!MachineFiles.Trusted(acl));
            var parent = new DirectorySecurity();
            parent.SetSecurityDescriptorSddlForm("O:SYD:(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)(A;OICIIO;GA;;;CO)");
            Check(MachineFiles.TrustedParent(parent));
            parent.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Allow));
            Check(!MachineFiles.TrustedParent(parent));
            var start = new System.Diagnostics.ProcessStartInfo();
            start.EnvironmentVariables["COR_PROFILER_PATH"] = "fixture";
            start.EnvironmentVariables["DOTNET_STARTUP_HOOKS"] = "fixture";
            start.EnvironmentVariables["PSExecutionPolicyPreference"] = "Bypass";
            start.EnvironmentVariables["PATH"] = "fixture";
            Commands.RestrictEnvironment(start);
            Check(!start.EnvironmentVariables.ContainsKey("COR_PROFILER_PATH") &&
                !start.EnvironmentVariables.ContainsKey("DOTNET_STARTUP_HOOKS") &&
                !start.EnvironmentVariables.ContainsKey("PSExecutionPolicyPreference") &&
                start.EnvironmentVariables["PATH"].StartsWith(Environment.SystemDirectory + ";", StringComparison.OrdinalIgnoreCase));
        }

        private static void AdminLogRecovery(string folder)
        {
            string original = Path.Combine(folder, "legacy-admin-logs");
            string alternate = Path.Combine(folder, "protected-admin-logs");
            Directory.CreateDirectory(original);
            string existingLog = Path.Combine(original, "previous.txt");
            File.WriteAllText(existingLog, "previous run");
            var legacySecurity = MachineFiles.CreateSecurity();
            using (var identity = WindowsIdentity.GetCurrent())
                legacySecurity.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            Check(!MachineFiles.Trusted(legacySecurity));
            var alternateSecurity = MachineFiles.CreateSecurity();
            Func<string, bool, bool> accept = (path, create) =>
            {
                if (path == original) return MachineFiles.Trusted(legacySecurity);
                if (!Directory.Exists(path) && create) Directory.CreateDirectory(path);
                return Directory.Exists(path) && MachineFiles.Trusted(alternateSecurity);
            };
            Check(MachineFiles.SelectDirectory(original, alternate, accept, false) == null &&
                !Directory.Exists(alternate));
            var run = new RunState(1);
            run.Execute(new[] { new Job("probe", "Fixture", log => new Result(ResultKind.Done, "read-only probe")) },
                original, () => MachineFiles.SelectDirectory(original, alternate, accept, true));
            Check(run.Finished && run.Results.Single().Value.Kind == ResultKind.Done &&
                Path.GetDirectoryName(run.LogPath) == alternate && File.Exists(run.LogPath));
            Check(File.ReadAllText(run.LogPath).Contains("read-only probe") &&
                File.ReadAllText(existingLog) == "previous run" && Directory.GetFiles(original).Length == 1);
            Check(MachineFiles.SelectDirectory(original, alternate, accept, false) == alternate);
        }

        private static void Queue(string folder)
        {
            var state = new RunState(3);
            state.Execute(new[]
            {
                new Job("dism-repair", "Onarım", log => new Result(ResultKind.Failed, "Örnek DISM hatası")),
                new Job("sfc", "SFC", log => { throw new InvalidOperationException("SFC çalışmamalı"); }),
                new Job("inventory", "Özet", log => new Result(ResultKind.Done, "Türkçe özet"))
            }, Path.Combine(folder, "logs"));
            var results = state.Results.ToArray();
            Check(state.Finished && state.Completed == 3 && results[1].Value.Kind == ResultKind.Skipped);
            Check(File.ReadAllText(state.LogPath).Contains("Türkçe özet"));
            var stop = new RunState(2);
            stop.Execute(new[]
            {
                new Job("first", "İlk", log => { stop.Stop(); return new Result(ResultKind.Done, "Bitti"); }),
                new Job("second", "İkinci", log => new Result(ResultKind.Failed, "Çalışmamalı"))
            }, Path.Combine(folder, "logs"));
            Check(stop.Results.ToArray()[1].Value.Kind == ResultKind.Skipped);
            var reboot = new RunState(3);
            reboot.Execute(new[]
            {
                new Job("winget-install", "İlk", log => new Result(ResultKind.Review, "Yeniden başlatma", true)),
                new Job("winget-install", "İkinci", log => new Result(ResultKind.Failed, "Çalışmamalı")),
                new Job("it-change-time-resync", "Üçüncü", log => new Result(ResultKind.Failed, "Çalışmamalı"))
            }, Path.Combine(folder, "logs"));
            Check(reboot.Results.ToArray().Skip(1).All(result => result.Value.Kind == ResultKind.Skipped));
            bool executed = false;
            var blocked = new RunState(1);
            blocked.Execute(new[]
            {
                new Job("example", "Örnek", _ => { executed = true; return new Result(ResultKind.Done, ""); })
            }, Path.Combine(folder, "logs"), () => { throw new IOException("Fixture"); });
            Check(!executed && blocked.Finished && blocked.LogPath == null);
            var noisy = new RunState(1);
            noisy.Execute(new[]
            {
                new Job("noisy", "Örnek", log =>
                {
                    string line = new string('x', 8192);
                    for (int i = 0; i < 1000; i++) log(line);
                    return new Result(ResultKind.Done, "Özet kaydedildi");
                })
            }, Path.Combine(folder, "logs"));
            Check(new FileInfo(noisy.LogPath).Length < 4500000 && File.ReadAllText(noisy.LogPath).Contains("Özet kaydedildi"));
        }

        private static void Temp(string folder)
        {
            string temp = Path.Combine(folder, "temp");
            string outer = Path.Combine(folder, "outside");
            Directory.CreateDirectory(Path.Combine(temp, "nested"));
            Directory.CreateDirectory(outer);
            string old = Old(temp, "nested\\old.txt");
            string edited = Old(temp, "edited.txt");
            string hardlinkTarget = Old(outer, "outside.txt");
            string hardlink = Path.Combine(temp, "hardlink.txt");
            bool linked = CreateHardLink(hardlink, hardlinkTarget, IntPtr.Zero);
            if (!linked) Console.WriteLine("ATLANDI  Sabit bağlantı kurulamıyor. Windows kodu: " + Marshal.GetLastWin32Error());
            string fresh = Path.Combine(temp, "recent.txt");
            File.WriteAllText(fresh, "new");
            File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddDays(-10));
            string readOnly = Old(temp, "readonly.txt");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            string locked = Old(temp, "locked.txt");
            var cleanup = new TempCleanup(temp);
            Action<string> log = Console.WriteLine;
            Check(cleanup.Clean(log).Kind == ResultKind.Skipped);
            using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Check(cleanup.Preview(log).Kind == ResultKind.Review && cleanup.HasPreview);
                File.WriteAllText(edited, "CHANGED");
                string later = Old(temp, "after-preview.txt");
                Check(cleanup.Clean(log).Kind == ResultKind.Review);
                Check(!File.Exists(old) && File.Exists(edited) && File.Exists(later));
                Check(File.Exists(fresh) && (!linked || File.Exists(hardlink)) && File.Exists(hardlinkTarget) && File.Exists(readOnly));
            }
            Check(File.Exists(locked) && !cleanup.HasPreview);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                bool threw = false;
                try { cleanup.Preview(log, cancelled.Token); }
                catch (OperationCanceledException) { threw = true; }
                Check(threw && !cleanup.HasPreview);
            }
        }

        private static string Old(string folder, string file)
        {
            string path = Path.Combine(folder, file);
            File.WriteAllText(path, "example");
            DateTime date = DateTime.UtcNow.AddDays(-10);
            File.SetCreationTimeUtc(path, date);
            File.SetLastWriteTimeUtc(path, date);
            return path;
        }

        private static void RegistryRoundTrip(string folder)
        {
            string key = @"Software\BakimMerkeziVerification-" + Guid.NewGuid().ToString("N");
            var setting = new Setting("test", "Test", "Test", key, "Sample", 7, 4, 0);
            int? value = null;
            var settings = new Settings(Path.Combine(folder, "registry"), item => value, (item, update) => value = update);
            Check(settings.State(setting) == "Windows varsayılanı");
            Check(settings.Apply(setting, true, Console.WriteLine).Kind == ResultKind.Done);
            Check(settings.HasBackup(setting) && settings.State(setting) == "Açık");
            Check(settings.Apply(setting, false, Console.WriteLine).Kind == ResultKind.Done);
            Check(settings.State(setting) == "Kapalı");
            Check(settings.Apply(setting, null, Console.WriteLine).Kind == ResultKind.Done);
            Check(!settings.HasBackup(setting) && settings.State(setting) == "Windows varsayılanı");
            value = 83;
            Check(settings.Apply(setting, false, Console.WriteLine).Kind == ResultKind.Done);
            value = 91;
            Check(settings.Apply(setting, null, Console.WriteLine).Kind == ResultKind.Review);
            value = 4;
            Check(settings.Apply(setting, null, Console.WriteLine).Kind == ResultKind.Done);
            Check(settings.State(setting) == "Özel değer (83)");
        }

        private static void Interface(string folder)
        {
            using (var form = new MainWindow(null))
            {
                var profile = Field<ComboBox>(form, "profiles");
                profile.SelectedIndex = 1;
                Check(Field<Button>(form, "runMaintenance").Enabled);
                Render(form, Path.Combine(folder, "bakim.png"));
                Page(form, "monitor");
                Render(form, Path.Combine(folder, "izleme.png"));
                Invoke(form, "UpdateMonitor");
                Check(Field<Label>(form, "memoryValue").Text.Contains("%"));
                Page(form, "apps");
                var search = Field<TextBox>(form, "search");
                var apps = Field<DataGridView>(form, "appGrid");
                search.Text = "7zip.7zip";
                Check(apps.Rows.Count == 1);
                apps.Rows[0].Cells[0].Value = true;
                search.Text = "firefox";
                Check(apps.Rows.Count == 2 && Field<Label>(form, "appDetail").Text.Contains("1 seçili"));
                search.Clear();
                Render(form, Path.Combine(folder, "uygulamalar.png"));
                form.Size = form.MinimumSize;
                Render(form, Path.Combine(folder, "uygulamalar-dar.png"));
                Page(form, "settings");
                Render(form, Path.Combine(folder, "ayarlar.png"));
                Page(form, "tools");
                Render(form, Path.Combine(folder, "araclar.png"));
                Page(form, "logs");
                Render(form, Path.Combine(folder, "gunluk.png"));
            }
            using (var plan = new PlanWindow("Bakım planı",
                "Bu plan değişiklik yapar. Geçici dosya silme işlemi kalıcıdır.",
                new[] { "Önizlenen geçici dosyaları temizle · Kalıcı dosya silme" }))
                Render(plan, Path.Combine(folder, "plan.png"));
            var permission = typeof(Program).GetProperty("Admin", BindingFlags.Static | BindingFlags.NonPublic);
            permission.GetSetMethod(true).Invoke(null, new object[] { true });
            try
            {
                using (var form = new MainWindow("chkdsk"))
                {
                    Check(Field<Button>(form, "runMaintenance").Enabled);
                    Check(Field<DataGridView>(form, "taskGrid").Rows.Count == Maintenance.Tasks.Count(task => task.Admin));
                    Render(form, Path.Combine(folder, "yonetici.png"));
                }
            }
            finally { permission.GetSetMethod(true).Invoke(null, new object[] { false }); }
        }

        private static void EnglishInterface(string folder)
        {
            L.Initialize("en");
            try
            {
                Check(Program.Name == "Windows Maintenance Center");
                Check(Maintenance.Tasks[0].Title == "System overview" && Settings.Options[0].Title == "Show file extensions");
                Check(Packages.Load().Any(item => item.Category == "Utilities"));
                using (var form = new MainWindow(null, "bulk"))
                {
                    Check(Field<Label>(form, "title").Text == "Bulk uninstall");
                    Field<List<InstalledApp>>(form, "wingetInstalled").Add(new InstalledApp("7zip.7zip", "7-Zip", "24.09"));
                    Field<List<InstalledApp>>(form, "storeInstalled").Add(new InstalledApp("Microsoft.BingNews", "Microsoft News", "1.2.3.4", "8wekyb3d8bbwe"));
                    Invoke(form, "FillBulk");
                    var grid = Field<DataGridView>(form, "bulkGrid");
                    Check(grid.Rows.Count == 2);
                    grid.Rows[0].Cells[0].Value = true;
                    grid.Rows[1].Cells[0].Value = true;
                    Check(Field<Button>(form, "bulkRemove").Enabled);
                    Field<TextBox>(form, "bulkSearch").Text = "news";
                    Check(grid.Rows.Count == 1 && Field<Label>(form, "bulkInfo").Text.Contains("2 selected"));
                    Field<TextBox>(form, "bulkSearch").Clear();
                    form.Size = form.MinimumSize;
                    Render(form, Path.Combine(folder, "bulk-en.png"));
                    form.GetType().GetField("startupLoaded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, true);
                    Page(form, "startup");
                    Check(Field<Label>(form, "title").Text == "Startup apps");
                    Render(form, Path.Combine(folder, "startup-en.png"));
                    Page(form, "maintenance");
                    Check(Field<Label>(form, "title").Text == "Maintenance plan");
                    Render(form, Path.Combine(folder, "maintenance-en.png"));
                }
                var permission = typeof(Program).GetProperty("Admin", BindingFlags.Static | BindingFlags.NonPublic);
                permission.GetSetMethod(true).Invoke(null, new object[] { true });
                try
                {
                    using (var form = new MainWindow(null, "services"))
                    {
                        Check(Field<Label>(form, "title").Text == "Optional services");
                        var service = new OptionalService("Fax", "Faks", "Fax");
                        Field<List<ServiceEntry>>(form, "serviceEntries").Add(new ServiceEntry(service,
                            System.ServiceProcess.ServiceControllerStatus.Stopped, "disabled", "delayed-auto", false));
                        Invoke(form, "FillServices");
                        var grid = Field<DataGridView>(form, "serviceGrid");
                        grid.CurrentCell = grid.Rows[0].Cells[0];
                        Invoke(form, "RefreshActions");
                        Check(Field<Button>(form, "serviceRestore").Enabled && !Field<Button>(form, "serviceStart").Enabled);
                        Render(form, Path.Combine(folder, "services-en.png"));
                    }
                }
                finally { permission.GetSetMethod(true).Invoke(null, new object[] { false }); }
            }
            finally { L.Initialize("tr"); }
        }

        private static void MonitorVerification(string folder)
        {
            var cpu = new CpuSampler();
            Check(!cpu.Read(100, 200, 100).HasValue);
            Check(cpu.Read(150, 300, 200) == 75.0);
            Check(!cpu.Read(150, 300, 200).HasValue);
            Check(!cpu.Read(1, 2, 1).HasValue);
            Check(!cpu.Read(1000, 3, 2).HasValue);
            cpu.Reset();
            Check(!cpu.Read(0, 0, 0).HasValue);
            Check(!cpu.Read(0, ulong.MaxValue, 1).HasValue);
            cpu.Reset();
            Check(!cpu.Read(0, 0, 0).HasValue);
            Check(cpu.Read(100, 100, 0) == 0.0);
            Check(cpu.Read(100, 200, 0) == 100.0);
            using (var graph = new HistoryChart())
            {
                for (int i = 0; i < 10000; i++) graph.Add(i * 2, i % 101);
                Check(graph.Count == 31);
                graph.Add(30000, null);
                Check(graph.Count == 1);
                graph.Add(double.NaN, 50);
                Check(graph.Count == 1);
                graph.Clear();
                Check(graph.Count == 0);
            }
            foreach (string language in new[] { "tr", "en" })
            {
                L.Initialize(language);
                try
                {
                    var form = new MainWindow(null, "monitor");
                    var timer = Field<System.Windows.Forms.Timer>(form, "monitorTimer");
                    try
                    {
                        Render(form, Path.Combine(folder, "monitor-initial-" + language + ".png"));
                        Check(timer.Enabled);
                        PumpUntil(() => Field<HistoryChart>(form, "cpuHistory").Count >= 4 &&
                            !Field<bool>(form, "diskReading"), 12000);
                        Check(Field<Label>(form, "cpuValue").Text.Contains("%"));
                        Check(Field<Label>(form, "memoryValue").Text.Contains("%"));
                        Check(Field<Label>(form, "diskValue").Text.Contains(Path.GetPathRoot(Environment.SystemDirectory)));
                        Render(form, Path.Combine(folder, "monitor-live-" + language + ".png"));
                        form.Size = form.MinimumSize;
                        Render(form, Path.Combine(folder, "monitor-small-" + language + ".png"));
                        Check(Field<HistoryChart>(form, "cpuHistory").Height >= 30);
                        Page(form, "maintenance");
                        Check(!timer.Enabled);
                        Page(form, "monitor");
                        Check(timer.Enabled && Field<Label>(form, "cpuValue").Text == L.Text("Ölçülüyor…", "Sampling…"));
                        Check(Field<HistoryChart>(form, "cpuHistory").Count == 1);
                        form.WindowState = FormWindowState.Minimized;
                        Application.DoEvents();
                        Check(!timer.Enabled);
                        form.WindowState = FormWindowState.Normal;
                        Application.DoEvents();
                        Check(timer.Enabled);
                        // Dispose while a background drive read may still be in flight.
                        Invoke(form, "UpdateMonitor");
                    }
                    finally { form.Dispose(); }
                    Application.DoEvents();
                    Check(!timer.Enabled);
                    using (var neverShown = new MainWindow(null, "monitor"))
                        Check(!Field<System.Windows.Forms.Timer>(neverShown, "monitorTimer").Enabled);
                }
                finally { L.Initialize("tr"); }
            }
        }

        private static void PumpUntil(Func<bool> condition, int timeout)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < timeout)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            Check(condition());
        }

        private static void Invoke(Form form, string name)
        {
            form.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        }

        private static T Field<T>(Form form, string name)
        {
            return (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }

        private static void Page(Form form, string name)
        {
            form.GetType().GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { name });
        }

        private static void Render(Form form, string path)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-15000, -15000);
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string link, string existing, IntPtr security);
    }
}
