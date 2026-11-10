using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace BakimMerkezi
{
    internal sealed class InstalledApp
    {
        internal readonly string Id, Name, Version, Publisher;
        internal bool Store { get { return Publisher != null; } }
        internal string Source { get { return Store ? "Microsoft Store" : "WinGet"; } }
        internal string Key { get { return Source + "|" + Id; } }

        internal InstalledApp(string id, string name, string version, string publisher = null)
        {
            Id = id; Name = name; Version = version; Publisher = publisher;
        }
    }

    internal static class InstalledApps
    {
        private static readonly InstalledApp[] OptionalStore =
        {
            new InstalledApp("Microsoft.BingNews", "Microsoft News", "", "8wekyb3d8bbwe"),
            new InstalledApp("Microsoft.BingWeather", "Microsoft Weather", "", "8wekyb3d8bbwe"),
            new InstalledApp("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "", "8wekyb3d8bbwe"),
            new InstalledApp("Clipchamp.Clipchamp", "Microsoft Clipchamp", "", "yxz26nhyzhsrt"),
            new InstalledApp("Microsoft.WindowsFeedbackHub", "Feedback Hub", "", "8wekyb3d8bbwe"),
            new InstalledApp("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Hub)", "", "8wekyb3d8bbwe"),
            new InstalledApp("Microsoft.WindowsMaps", "Windows Maps", "", "8wekyb3d8bbwe"),
            new InstalledApp("Microsoft.Getstarted", "Tips", "", "8wekyb3d8bbwe")
        };

        internal static Job ScanWinGet(IEnumerable<Package> known, Action<List<InstalledApp>> receive)
        {
            return new Job("bulk-scan-winget", L.Text("WinGet ile eşleşen uygulamaları bul", "Find apps matched by WinGet"), (log, token) =>
            {
                RequireUser();
                if (!File.Exists(Packages.WinGet))
                    return new Result(ResultKind.Skipped, L.Text("WinGet bulunamadı.", "WinGet is unavailable."));
                if (!Packages.VerifySource(log, token)) return Packages.UntrustedSource();

                string path = Path.Combine(Program.Data, "WinGet-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var result = Commands.Run(Packages.WinGet, "export --output \"" + path +
                        "\" --source winget --include-versions --disable-interactivity --accept-source-agreements",
                        Encoding.UTF8, log, token: token, probeTimeout: TimeSpan.FromMinutes(4));
                    if (result.Code != 0 || !File.Exists(path))
                        return new Result(ResultKind.Failed, L.Text("WinGet listesi alınamadı. Kod: ", "WinGet inventory failed. Code: ") +
                            Commands.Code(result.Code));
                    List<InstalledApp> items;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = new StreamReader(stream))
                    {
                        if (stream.Length > 4194304) throw LargeInventory();
                        items = ParseWinGet(reader.ReadToEnd(), known);
                    }
                    receive(items);
                    return Count(items.Count);
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            });
        }

        internal static List<InstalledApp> ParseWinGet(string data, IEnumerable<Package> known)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4194304, RecursionLimit = 12 };
            var root = serializer.DeserializeObject(data) as Dictionary<string, object>;
            var sources = ArrayField(root, "Sources");
            if (sources == null) throw new InvalidDataException(L.Text("WinGet liste biçimi tanınmadı.", "Unrecognized WinGet inventory format."));
            var names = known.ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
            var items = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourceObject in sources)
            {
                var source = sourceObject as Dictionary<string, object>;
                var detail = Field(source, "SourceDetails") as Dictionary<string, object>;
                if (!string.Equals(Field(detail, "Name") as string, "winget", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Packages.IsCommunitySource(detail, "Argument"))
                    throw new InvalidDataException(Packages.UntrustedSource().Message);
                foreach (var packageObject in ArrayField(source, "Packages") ?? new object[0])
                {
                    var package = packageObject as Dictionary<string, object>;
                    string id = Field(package, "PackageIdentifier") as string;
                    if (!Packages.CanUninstall(id) || items.ContainsKey(id)) continue;
                    string name;
                    string version = Field(package, "Version") as string ?? "";
                    if (version.Length > 100 || version.Any(character => char.IsControl(character) ||
                        char.GetUnicodeCategory(character) == UnicodeCategory.Format)) continue;
                    if (items.Count >= 2000) throw LargeInventory();
                    if (!names.TryGetValue(id, out name)) name = id;
                    items.Add(id, new InstalledApp(id, name, version));
                }
            }
            return items.Values.OrderBy(item => item.Name).ToList();
        }

        private static InvalidDataException LargeInventory()
        {
            return new InvalidDataException(L.Text("WinGet listesi beklenenden büyük.", "WinGet inventory is unexpectedly large."));
        }

        private static object Field(Dictionary<string, object> node, string key)
        {
            object value;
            return node != null && node.TryGetValue(key, out value) ? value : null;
        }

        private static object[] ArrayField(Dictionary<string, object> node, string key)
        {
            return Field(node, key) as object[];
        }

        internal static Job ScanStore(Action<List<InstalledApp>> receive)
        {
            return new Job("bulk-scan-store", L.Text("Kaldırılabilir Store uygulamalarını bul", "Find removable Store apps"), (log, token) =>
            {
                RequireUser();
                var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string filter = string.Join(",", OptionalStore.Select(app => "'" + app.Id + "|" + app.Publisher + "'"));
                string script = "$Allowed=@(" + filter + "); $Remove='';\n" + Program.Resource("Kaldirma.ps1");
                var result = Commands.PowerShell(script, line =>
                {
                    if (!line.StartsWith("BAKIM-APP|", StringComparison.Ordinal)) { log(line); return; }
                    string[] parts = line.Split('|');
                    Version version;
                    if (parts.Length == 3 && Version.TryParse(parts[2], out version)) found[parts[1]] = parts[2];
                }, token, TimeSpan.FromMinutes(2));
                if (result.Code != 0)
                    return new Result(ResultKind.Failed, L.Text("Store listesi alınamadı. Kod: ", "Store inventory failed. Code: ") +
                        Commands.Code(result.Code));
                var items = OptionalStore.Where(app => found.ContainsKey(app.Id))
                    .Select(app => new InstalledApp(app.Id, StoreName(app), found[app.Id], app.Publisher)).ToList();
                receive(items);
                return Count(items.Count);
            });
        }

        private static string StoreName(InstalledApp app)
        {
            switch (app.Id)
            {
                case "Microsoft.BingNews": return L.Text("Microsoft Haberler", "Microsoft News");
                case "Microsoft.BingWeather": return L.Text("Microsoft Hava Durumu", "Microsoft Weather");
                case "Microsoft.WindowsFeedbackHub": return L.Text("Geri Bildirim Merkezi", "Feedback Hub");
                case "Microsoft.WindowsMaps": return L.Text("Windows Haritalar", "Windows Maps");
                case "Microsoft.Getstarted": return L.Text("İpuçları", "Tips");
                default: return app.Name;
            }
        }

        internal static Job Remove(InstalledApp app, Action<string> removed)
        {
            if (!app.Store)
            {
                Job job = Packages.Create("uninstall", new Package("", app.Name, app.Id), app.Version);
                return new Job(job.Id, job.Title, (log, token) => Track(job.Run(log, token), app, removed));
            }
            var allowed = OptionalStore.FirstOrDefault(item => item.Id == app.Id && item.Publisher == app.Publisher);
            if (allowed == null) throw new ArgumentException(L.Text("Store paketi izin verilen listede yok.", "This Store package is not on the allowlist."));
            Version expected;
            if (!Version.TryParse(app.Version, out expected))
                throw new ArgumentException(L.Text("Store paket sürümü geçersiz.", "Invalid Store package version."));
            return new Job("store-uninstall", L.Text("Kaldır · ", "Uninstall · ") + app.Name + " (" + app.Id + ")", log =>
            {
                RequireUser();
                string script = "$Allowed=@('" + allowed.Id + "|" + allowed.Publisher + "'); $Remove='" + allowed.Id +
                    "'; $ExpectedVersion='" + expected + "';\n" + Program.Resource("Kaldirma.ps1");
                var result = Commands.PowerShell(script, log);
                if (result.Code == 2)
                    return new Result(ResultKind.Review, L.Text("Paket kaldırma için yeniden doğrulanamadı.", "The package could not be revalidated for removal."));
                return Track(Maintenance.Generic(result, L.Text("Paket bu Windows hesabından kaldırıldı.", "Package removed from this Windows account.")), app, removed);
            });
        }

        private static Result Track(Result result, InstalledApp app, Action<string> removed)
        {
            if (result.Kind == ResultKind.Done) removed(app.Key);
            return result;
        }

        private static Result Count(int count)
        {
            return new Result(ResultKind.Done, string.Format(L.Text("{0} uygulama bulundu.", "{0} apps found."), count));
        }

        private static void RequireUser()
        {
            if (Program.Admin) throw new InvalidOperationException(L.Text("Normal kullanıcı penceresini açın.", "Open the standard user window."));
        }
    }
}
