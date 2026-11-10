using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Windows Bakım Merkezi")]
[assembly: AssemblyDescription("Windows tanılama, bakım ve uygulama yönetimi")]
[assembly: AssemblyCompany("Oguzhan DUYAR")]
[assembly: AssemblyCopyright("Developed by Oguzhan DUYAR")]
[assembly: AssemblyVersion("0.0.1.0")]
[assembly: AssemblyFileVersion("0.0.1.1")]
[assembly: AssemblyInformationalVersion("0.001")]
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace BakimMerkezi
{
    internal static class Program
    {
        internal static string Name { get { return L.Text("Windows Bakım Merkezi", "Windows Maintenance Center"); } }
        internal static readonly string Data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsBakimMerkezi");
        internal const string Version = "0.001";
        internal static string Logs { get { return Admin ? MachineFiles.LogDirectory : Path.Combine(Data, "Gunlukler"); } }
        internal static bool Admin { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (!SetDefaultDllDirectories(0x00000800)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (var identity = WindowsIdentity.GetCurrent())
                    Admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                string language = args.FirstOrDefault(item => item.StartsWith("--lang=", StringComparison.Ordinal));
                string code = language == null ? null : language.Substring(7);
                L.Initialize(Admin && code != "en" && code != "tr" ? "tr" : code);
                string theme = args.FirstOrDefault(item => item.StartsWith("--theme=", StringComparison.Ordinal));
                Style.InitializeTheme(theme == null ? null : theme.Substring(8));
                if (Admin) RequireLocalExecutable(Application.ExecutablePath);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var os = Environment.OSVersion.Version;
                if (os.Major < 10 || (os.Major == 10 && os.Build < 19045))
                {
                    MessageBox.Show(L.Translate("Bu sürüm için Windows 10 22H2 veya Windows 11 gerekiyor."), Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (args.Length > 0 && args[0] == "--yonetici" && !Admin)
                    throw new InvalidOperationException(L.Translate("Windows yönetici izni vermedi."));
                string initialTask = Admin && args.Length >= 2 && args[0] == "--yonetici" &&
                    !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
                string mutexName = @"Local\WindowsBakimMerkezi.4." + (Admin ? "Makine" : "Kullanici");
                using (var mutex = new Mutex(false, mutexName))
                {
                    bool entered;
                    try { entered = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { entered = true; }
                    if (!entered)
                    {
                        MessageBox.Show(L.Translate("Bu oturumda aynı türde bir Bakım Merkezi penceresi zaten açık."), Name);
                        return;
                    }
                    try
                    {
                        string page = initialTask == "services" || initialTask == "it" ? initialTask : null;
                        do
                        {
                            using (var window = new MainWindow(initialTask, page))
                            {
                                Application.Run(window);
                                page = window.ReopenRequested ? window.CurrentPage : null;
                                initialTask = null;
                            }
                        } while (page != null);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                MessageBox.Show(ex.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static void Open(string target)
        {
            if (Admin)
            {
                if (target != Commands.SystemTool("SystemPropertiesProtection.exe") && target != Commands.SystemTool("dfrgui.exe"))
                    throw new InvalidOperationException(L.Text("Bu kısayolu normal kullanıcı penceresinden açın.",
                        "Open this shortcut from the standard user window."));
                var start = new ProcessStartInfo(target)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Environment.SystemDirectory
                };
                Commands.RestrictEnvironment(start);
                using (Process.Start(start)) { }
                return;
            }
            using (Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })) { }
        }

        internal static void Elevate(string taskId)
        {
            if (taskId != null && taskId != "services" && taskId != "it" &&
                !Maintenance.Tasks.Exists(task => task.Admin && task.Id == taskId))
                throw new ArgumentException(L.Translate("Yönetici işlemi tanınmadı."));
            string exe = Application.ExecutablePath;
            RequireLocalExecutable(exe);

            var start = new ProcessStartInfo(exe, "--yonetici" +
                (taskId == null ? "" : " " + taskId) + " --lang=" + L.Code +
                " --theme=" + (Style.Light ? "light" : "dark"))
            {
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            try { using (Process.Start(start)) { } }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return;
                throw;
            }
        }

        internal static string Resource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Bakim." + name))
            {
                if (stream == null) throw new InvalidDataException(L.Translate("Uygulama verisi bulunamadı: ") + name);
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static void RequireLocalExecutable(string exe)
        {
            if (!Startup.SafeFolder(Path.GetDirectoryName(exe)) || (File.GetAttributes(exe) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Text("Yönetici penceresini açmak için EXE'yi bağlantı içermeyen yerel bir klasöre çıkarın.",
                    "Extract the EXE to a local folder without links before opening the administrator window."));
            if (File.Exists(exe + ".config") || File.Exists(exe + ".local") || Directory.Exists(exe + ".local"))
                throw new IOException(L.Text("Yönetici penceresi, EXE yanında harici yapılandırma veya DLL yönlendirmesi algıladı.",
                    "The administrator window detected external configuration or DLL redirection next to the EXE."));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDefaultDllDirectories(uint flags);
    }
}
