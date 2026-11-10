using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace BakimMerkezi
{
    internal sealed class OptionalService
    {
        internal readonly string Name, English, Turkish;
        internal OptionalService(string name, string turkish, string english) { Name = name; Turkish = turkish; English = english; }
        internal string Detail { get { return L.Text(Turkish, English); } }
    }

    internal sealed class ServiceEntry
    {
        internal readonly OptionalService Option;
        internal readonly ServiceControllerStatus Status;
        internal readonly string Mode, Backup;
        internal readonly bool CanStop;

        internal ServiceEntry(OptionalService option, ServiceControllerStatus status, string mode, string backup, bool canStop)
        {
            Option = option; Status = status; Mode = mode; Backup = backup; CanStop = canStop;
        }
    }

    internal static class OptionalServices
    {
        private const string Journal = @"SOFTWARE\WindowsBakimMerkezi\ServiceStartup";
        private static readonly OptionalService[] Allowed =
        {
            new OptionalService("Fax", "Windows faks gönderme ve alma.", "Send and receive faxes in Windows."),
            new OptionalService("RetailDemo", "Mağazalardaki Windows perakende tanıtım modu.", "Windows retail demonstration mode used in stores."),
            new OptionalService("MapsBroker", "İndirilen çevrimdışı haritalara erişim.", "Access to downloaded offline maps."),
            new OptionalService("WMPNetworkSvc", "Eski Windows Media Player ağ üzerinden kitaplık paylaşımı.", "Share a media library over the network with legacy Windows Media Player."),
            new OptionalService("AJRouter", "AllJoyn kullanan yerel akıllı cihaz iletişimi.", "Local smart-device communication using AllJoyn."),
            new OptionalService("WalletService", "Windows Cüzdan uygulaması entegrasyonu.", "Windows Wallet app integration.")
        };

        internal static bool IsAllowed(string service) { return Allowed.Any(item => item.Name == service); }
        internal static bool MatchesExactService(string requested, string resolved)
        {
            return IsAllowed(requested) && string.Equals(requested, resolved, StringComparison.OrdinalIgnoreCase);
        }

        internal static List<ServiceEntry> Read(Action<string> warning)
        {
            RequireAdmin();
            var entries = new List<ServiceEntry>();
            foreach (var option in Allowed)
            {
                try
                {
                    using (var service = new ServiceController(option.Name))
                    {
                        if (!MatchesExactService(option.Name, service.ServiceName)) continue;
                        string mode = Mode(service);
                        if (mode != null)
                            entries.Add(new ServiceEntry(option, service.Status, mode, Backup(option.Name), service.CanStop));
                    }
                }
                catch (InvalidOperationException ex)
                {
                    var error = ex.InnerException as System.ComponentModel.Win32Exception;
                    if (error == null || error.NativeErrorCode != 1060) warning(option.Name + " · " + ex.Message);
                }
                catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode != 1060) warning(option.Name + " · " + ex.Message); }
            }
            return entries;
        }

        internal static string ModeLabel(string mode)
        {
            switch (mode)
            {
                case "auto": return L.Text("Otomatik", "Automatic");
                case "delayed-auto": return L.Text("Otomatik (gecikmeli)", "Automatic (delayed)");
                case "demand": return L.Text("El ile", "Manual");
                case "disabled": return L.Text("Devre dışı", "Disabled");
                default: return "—";
            }
        }

        internal static string StatusLabel(ServiceControllerStatus status)
        {
            switch (status)
            {
                case ServiceControllerStatus.Running: return L.Text("Çalışıyor", "Running");
                case ServiceControllerStatus.Stopped: return L.Text("Durdu", "Stopped");
                case ServiceControllerStatus.Paused: return L.Text("Duraklatıldı", "Paused");
                default: return L.Text("Geçiş sürüyor", "Transition pending");
            }
        }

        private static string Mode(ServiceController service)
        {
            switch (service.StartType)
            {
                case ServiceStartMode.Automatic:
                    using (var handle = service.ServiceHandle)
                    {
                        int delayed;
                        uint needed;
                        if (!QueryServiceConfig2(handle, 3, out delayed, sizeof(int), out needed))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        return delayed != 0 ? "delayed-auto" : "auto";
                    }
                case ServiceStartMode.Manual: return "demand";
                case ServiceStartMode.Disabled: return "disabled";
                default: return null;
            }
        }

        private static string Backup(string name)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(Journal))
            {
                string value = key == null ? null : key.GetValue(name) as string;
                return IsRestorable(value) ? value : null;
            }
        }

        internal static bool IsRestorable(string value) { return value == "auto" || value == "delayed-auto" || value == "demand"; }

        internal static Job Create(ServiceEntry entry, string operation)
        {
            string name = entry.Option.Name;
            if (!IsAllowed(name)) throw new ArgumentException(L.Text("Servis izin verilen listede yok.", "This service is not on the allowlist."));
            string title;
            switch (operation)
            {
                case "disable": title = L.Text("Servis başlangıcını kapat", "Disable service startup"); break;
                case "restore": title = L.Text("Servis başlangıcını geri yükle", "Restore service startup"); break;
                case "start": title = L.Text("Servisi başlat", "Start service"); break;
                case "stop": title = L.Text("Servisi durdur", "Stop service"); break;
                default: throw new ArgumentException("Invalid service operation.");
            }
            return new Job("service-" + operation, title + " · " + name, log =>
            {
                RequireAdmin();
                using (var mutex = new Mutex(false, @"Global\WindowsBakimMerkezi.ServiceEdit"))
                {
                    bool entered;
                    try { entered = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { entered = true; }
                    if (!entered) return new Result(ResultKind.Skipped, L.Text("Başka bir servis işlemi devam ediyor.",
                        "Another service action is already running."));
                    try
                    {
                        using (var service = new ServiceController(name))
                        {
                            if (!MatchesExactService(name, service.ServiceName)) return Changed();
                            if (operation == "start" || operation == "stop") return Control(service, operation == "start", log);
                            return Configure(service, entry, operation == "disable", log);
                        }
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            });
        }

        private static Result Configure(ServiceController service, ServiceEntry entry, bool disable, Action<string> log)
        {
            string current = Mode(service);
            string saved = Backup(service.ServiceName);
            if (current != entry.Mode || saved != entry.Backup) return Changed();
            if (disable)
            {
                if (!IsRestorable(current) || (saved != null && saved != current)) return Changed();
                if (HasDependents(service, true))
                    return new Result(ResultKind.Skipped, L.Text("Başka etkin servisler bu servise bağımlı.", "Other enabled services depend on this service."));
                using (var key = Registry.LocalMachine.CreateSubKey(Journal))
                {
                    if (saved == null && key.GetValue(service.ServiceName) != null) return Changed();
                    key.SetValue(service.ServiceName, current, RegistryValueKind.String);
                    key.Flush();
                }
            }
            else if (current != "disabled" || !IsRestorable(saved)) return Changed();

            string target = disable ? "disabled" : saved;
            var outcome = Sc("config " + service.ServiceName + " start= " + target, log);
            if (outcome.Code != 0) return Maintenance.Generic(outcome, "");
            service.Refresh();
            if (Mode(service) != target)
                return new Result(ResultKind.Review, L.Text("Yeni başlangıç modu doğrulanamadı. Yedek korundu.", "The new startup mode could not be verified. The backup was kept."));
            if (!disable)
                using (var key = Registry.LocalMachine.OpenSubKey(Journal, true))
                    if (key != null && Backup(service.ServiceName) == saved) key.DeleteValue(service.ServiceName, false);
            return new Result(ResultKind.Done, L.Text("Doğrulanan başlangıç modu: ", "Verified startup mode: ") + ModeLabel(target));
        }

        private static bool HasDependents(ServiceController service, bool startup)
        {
            var dependents = service.DependentServices;
            try
            {
                return dependents.Any(item => startup ? item.StartType != ServiceStartMode.Disabled :
                    item.Status != ServiceControllerStatus.Stopped);
            }
            finally { foreach (var item in dependents) item.Dispose(); }
        }

        private static Result Control(ServiceController service, bool start, Action<string> log)
        {
            var desired = start ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped;
            if (service.Status == desired)
                return new Result(ResultKind.Done, StatusLabel(desired));
            if (start && (service.Status != ServiceControllerStatus.Stopped || Mode(service) == "disabled"))
                return Changed();
            if (!start && (service.Status != ServiceControllerStatus.Running || !service.CanStop || HasDependents(service, false)))
                return new Result(ResultKind.Skipped, L.Text("Servis durdurulabilir durumda değil veya çalışan bir servis ona bağımlı.",
                    "The service cannot be stopped now or a running service depends on it."));

            var output = Sc((start ? "start " : "stop ") + service.ServiceName, log);
            if (output.Code != 0) return Maintenance.Generic(output, "");
            try { service.WaitForStatus(desired, TimeSpan.FromSeconds(15)); }
            catch (System.ServiceProcess.TimeoutException)
            {
                return new Result(ResultKind.Review, L.Text("Windows isteği aldı. Servis geçişi sürüyor.", "Windows accepted the request. The service transition is still pending."));
            }
            return new Result(ResultKind.Done, StatusLabel(desired));
        }

        private static CommandOutput Sc(string args, Action<string> log)
        {
            log("sc.exe " + args);
            return Commands.Run(Commands.SystemTool("sc.exe"), args, Commands.Oem, log);
        }

        private static Result Changed()
        {
            return new Result(ResultKind.Review, L.Text("Servis durumu değişti veya yedek doğrulanamadı. Listeyi yenileyin.",
                "The service state changed or its backup could not be verified. Refresh the list."));
        }

        private static void RequireAdmin()
        {
            if (!Program.Admin) throw new InvalidOperationException(L.Text("Yönetici penceresini açın.", "Open the administrator window."));
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig2(SafeHandle service, uint infoLevel, out int value, uint size, out uint needed);
    }
}
