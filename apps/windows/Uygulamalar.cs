using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace BakimMerkezi
{
    internal sealed class Package
    {
        internal readonly string Name, Id;
        private readonly string category;
        internal string Category { get { return L.Translate(category); } }
        internal Package(string category, string name, string id) { this.category = category; Name = name; Id = id; }
    }

    internal static class Packages
    {
        private static readonly Regex Identifier = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}\z", RegexOptions.CultureInvariant);
        internal static bool ValidId(string value) { return value != null && Identifier.IsMatch(value); }
        private static readonly string[] ProtectedPrefixes =
        {
            "Microsoft.Edge", "Microsoft.AppInstaller", "Microsoft.DesktopAppInstaller", "Microsoft.WinGet",
            "Microsoft.DotNet.", "Microsoft.VCRedist.", "Microsoft.WindowsAppRuntime", "Microsoft.VCLibs", "Microsoft.UI.Xaml"
        };

        internal static bool CanUninstall(string id)
        {
            return ValidId(id) && !ProtectedPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        internal static readonly string WinGet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");

        internal static List<Package> Load()
        {
            var packages = new List<Package>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new StringReader(Program.Resource("Uygulamalar.tsv")))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 ||
                        !Identifier.IsMatch(parts[2]) || !ids.Add(parts[2]))
                        throw new InvalidDataException(L.Translate("Uygulama kataloğunda geçersiz veya yinelenen bir kayıt var."));
                    packages.Add(new Package(parts[0], parts[1], parts[2]));
                }
            }
            return packages.OrderBy(item => item.Category).ThenBy(item => item.Name).ToList();
        }

        internal static Job Create(string operation, Package package = null, string installedVersion = null)
        {
            string title;
            string args;
            switch (operation)
            {
                case "version":
                    title = L.Translate("WinGet sürümü");
                    args = "--version";
                    break;
                case "list":
                    title = L.Translate("WinGet ile eşleşen kurulu uygulamalar");
                    args = "list";
                    break;
                case "updates":
                    title = L.Translate("Uygulama güncellemelerini listele");
                    args = "upgrade";
                    break;
                case "install":
                case "upgrade":
                case "uninstall":
                    if (package == null || !ValidId(package.Id))
                        throw new ArgumentException(L.Translate("Geçersiz WinGet paket kimliği."));
                    if (operation == "uninstall" && !CanUninstall(package.Id))
                        throw new ArgumentException(L.Text("Bu paket toplu kaldırma kapsamı dışında.", "This package is outside the bulk uninstall scope."));
                    title = (operation == "install" ? L.Translate("Kur") : operation == "upgrade" ? L.Translate("Güncelle") : L.Translate("Kaldır")) +
                        " · " + package.Name + " (" + package.Id + ")";
                    args = operation + " --id " + package.Id + " --exact";
                    if (operation != "uninstall") args += " --accept-package-agreements";
                    else if (HasVersion(installedVersion))
                    {
                        if (!Regex.IsMatch(installedVersion, @"\A[A-Za-z0-9][A-Za-z0-9 ._+()\-]{0,99}\z", RegexOptions.CultureInvariant))
                            return new Job("winget-uninstall", title, log => new Result(ResultKind.Skipped,
                                L.Text("Kurulu sürüm WinGet'e güvenle aktarılamadı.", "The installed version could not be passed safely to WinGet.")));
                        args += " --version \"" + installedVersion + "\"";
                    }
                    break;
                default: throw new ArgumentException(L.Translate("Bilinmeyen WinGet işlemi."));
            }

            if (operation != "version") args += " --source winget --disable-interactivity --accept-source-agreements";
            return new Job("winget-" + operation, title, (log, token) =>
            {
                if (Program.Admin) throw new InvalidOperationException(L.Translate("Uygulama yönetimini normal kullanıcı penceresinden açın."));
                if (!File.Exists(WinGet))
                    return new Result(ResultKind.Skipped, L.Translate("WinGet bulunamadı. Microsoft Store'dan Uygulama Yükleyicisi'ni yükleyin veya güncelleyin."));
                if (operation != "version" && !VerifySource(log, token)) return UntrustedSource();
                log("winget " + args);
                TimeSpan? limit = operation == "version" ? TimeSpan.FromSeconds(20) :
                    operation == "list" || operation == "updates" ? TimeSpan.FromMinutes(4) : (TimeSpan?)null;
                var output = Commands.Run(WinGet, args, Encoding.UTF8, log, token: token, probeTimeout: limit);
                return Classify(operation, output.Code);
            });
        }

        private static bool HasVersion(string version)
        {
            return !string.IsNullOrWhiteSpace(version) && !version.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool VerifySource(Action<string> log, CancellationToken token)
        {
            var output = Commands.Run(WinGet, "source export --name winget --disable-interactivity", Encoding.UTF8, line => { },
                token: token, probeTimeout: TimeSpan.FromSeconds(20));
            if (output.Code == 0 && output.Tail.Split('\n').Any(IsCommunitySourceJson)) return true;
            log("winget source export --name winget · " + Commands.Code(output.Code));
            return false;
        }

        internal static Result UntrustedSource()
        {
            return new Result(ResultKind.Review, L.Text("Varsayılan WinGet topluluk kaynağı doğrulanamadı.",
                "The default WinGet community source could not be verified."));
        }

        internal static bool IsCommunitySourceJson(string line)
        {
            line = line.Trim().TrimStart('\uFEFF');
            if (!line.StartsWith("{", StringComparison.Ordinal)) return false;
            try
            {
                var source = new JavaScriptSerializer { MaxJsonLength = 8192, RecursionLimit = 8 }.DeserializeObject(line)
                    as Dictionary<string, object>;
                return IsCommunitySource(source, "Arg");
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static bool IsCommunitySource(Dictionary<string, object> source, string argument)
        {
            return FieldEquals(source, "Name", "winget") &&
                FieldEquals(source, "Identifier", "Microsoft.Winget.Source_8wekyb3d8bbwe") &&
                FieldEquals(source, "Type", "Microsoft.PreIndexed.Package") &&
                FieldEquals(source, argument, "https://cdn.winget.microsoft.com/cache");
        }

        private static bool FieldEquals(Dictionary<string, object> source, string key, string expected)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && string.Equals(value as string, expected, StringComparison.Ordinal);
        }

        internal static Result Classify(string operation, int code)
        {
            if (code == 0) return new Result(ResultKind.Done, L.Translate("WinGet işlemi tamamlandı."));
            uint hresult = unchecked((uint)code);
            string suffix = L.Translate(" Kod: ") + Commands.Code(code) + ".";
            if (hresult == 0x8A15002B && (operation == "upgrade" || operation == "updates"))
                return new Result(ResultKind.Done, L.Translate("Uygulanabilir güncelleme bulunmadı.") + suffix);
            if (hresult == 0x8A150014)
                return new Result(operation == "list" ? ResultKind.Done : ResultKind.Review,
                    L.Translate("WinGet eşleşen uygulama bulamadı.") + suffix);
            if (hresult == 0x8A150010)
                return new Result(ResultKind.Review, L.Translate("Bu Windows sürümü, dil veya mimari için uygun kurucu bulunamadı.") + suffix);
            if (hresult == 0x8A15010A)
                return new Result(ResultKind.Failed, L.Translate("Kurulum için önce Windows'u yeniden başlatın.") + suffix, true);
            if (hresult == 0x8A15010C)
                return new Result(ResultKind.Skipped, L.Translate("Kurulum kullanıcı tarafından iptal edildi.") + suffix);
            if (code == 3010 || code == 1641 || hresult == 0x8A150109 || hresult == 0x8A15010B)
                return new Result(ResultKind.Review, L.Translate("Kurucu yeniden başlatma gerektirdi.") + suffix, true);
            return new Result(ResultKind.Failed, L.Translate("WinGet işlemi tamamlanamadı.") + suffix + L.Translate(" Komut çıktısını inceleyin."));
        }
    }
}
