using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace BakimMerkezi
{
    internal sealed class Setting
    {
        internal readonly string Id, Key, Name;
        private readonly string title, detail;
        internal string Title { get { return L.Translate(title); } }
        internal string Detail { get { return L.Translate(detail); } }
        internal readonly int On, Off, Build;

        internal Setting(string id, string title, string detail, string key, string name, int on, int off, int build = 19045)
        {
            Id = id;
            this.title = title;
            this.detail = detail;
            Key = key;
            Name = name;
            On = on;
            Off = off;
            Build = build;
        }
    }

    internal sealed class Settings
    {
        private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        internal static readonly Setting[] Options =
        {
            new Setting("extensions", "Dosya uzantılarını göster", "Dosya Gezgini'nde .pdf, .jpg ve .exe gibi uzantıları görünür yapar.",
                Advanced, "HideFileExt", 0, 1),
            new Setting("hidden", "Gizli dosyaları göster", "Dosya Gezgini'ndeki gizli dosya görünümünü değiştirir.",
                Advanced, "Hidden", 1, 2),
            new Setting("apps-dark", "Uygulamalarda koyu görünüm", "Windows temasını izleyen uygulamaların görünümünü değiştirir.",
                Personalize, "AppsUseLightTheme", 0, 1),
            new Setting("windows-dark", "Windows'ta koyu görünüm", "Başlat ve görev çubuğu gibi Windows yüzeylerinin temasını değiştirir.",
                Personalize, "SystemUsesLightTheme", 0, 1),
            new Setting("taskbar-end", "Görev çubuğunda Görevi Sonlandır", "Uygulamanın sağ tıklama menüsüne işlemi sonlandırma seçeneği ekler.",
                Advanced + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1, 0, 22631)
        };

        private readonly string folder;
        private readonly Func<Setting, int?> read;
        private readonly Action<Setting, int?> write;

        internal Settings(string folder) : this(folder, ReadRegistry, WriteRegistry) { }

        internal Settings(string folder, Func<Setting, int?> read, Action<Setting, int?> write)
        {
            this.folder = folder;
            this.read = read;
            this.write = write;
        }

        internal static bool Supported(Setting setting)
        {
            return Environment.OSVersion.Version.Build >= setting.Build;
        }

        internal string State(Setting setting)
        {
            if (!Supported(setting)) return L.Translate("Bu Windows sürümünde yok");
            int? current = read(setting);
            if (!current.HasValue) return L.Translate("Windows varsayılanı");
            if (current == setting.On) return L.Translate("Açık");
            if (current == setting.Off) return L.Translate("Kapalı");
            return L.Translate("Özel değer (") + current.Value + ")";
        }

        internal bool HasBackup(Setting setting) { return File.Exists(PathFor(setting)); }

        internal Job Create(Setting setting, bool? enable)
        {
            string verb = enable == null ? L.Translate("Geri al") : enable.Value ? L.Translate("Aç") : L.Translate("Kapat");
            return new Job("setting-" + setting.Id, verb + " · " + setting.Title,
                log => Apply(setting, enable, log));
        }

        internal Result Apply(Setting setting, bool? enable, Action<string> log)
        {
            if (Program.Admin || !Supported(setting))
                return new Result(ResultKind.Skipped, L.Translate("Bu ayar normal kullanıcı oturumunda ve uyumlu bir Windows sürümünde kullanılabilir."));
            string file = PathFor(setting);
            Entry backup = File.Exists(file) ? Entry.Read(file) : null;
            int? current = read(setting);
            if (backup != null && current != backup.Target && current != backup.Previous)
                return new Result(ResultKind.Review, L.Translate("Ayar uygulama dışında değiştirildi. Kayıtlı önceki değer korunuyor."));
            if (!enable.HasValue && backup == null)
                return new Result(ResultKind.Skipped, L.Translate("Bu ayar için kayıtlı önceki değer yok."));

            int? target = enable.HasValue ? (enable.Value ? setting.On : setting.Off) : backup.Baseline;
            log(@"HKCU\" + setting.Key + "\\" + setting.Name + " · " + Format(current) + " → " + Format(target));
            if (current != target)
            {
                if (enable.HasValue)
                    Save(file, new Entry(backup == null ? current : backup.Baseline, target, current));
                write(setting, target);
                if (read(setting) != target)
                    return new Result(ResultKind.Review, L.Translate("Yazılan değer doğrulanamadı. Yedek saklandı."));
            }
            if (!enable.HasValue) File.Delete(file);
            return new Result(ResultKind.Done, current == target ? L.Translate("Değer zaten istenen durumdaydı.") :
                L.Translate("Değer doğrulandı. Etkiyi görmek için ilgili pencereyi yeniden açın veya oturumu kapatıp açın."));
        }

        private string PathFor(Setting setting) { return Path.Combine(folder, setting.Id + ".txt"); }

        private static int? ReadRegistry(Setting setting)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(setting.Key))
            {
                object value = key == null ? null : key.GetValue(setting.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null) return null;
                if (key.GetValueKind(setting.Name) != RegistryValueKind.DWord || !(value is int))
                    throw new InvalidDataException(L.Translate("Mevcut değer türü beklenenden farklı. Ayar değiştirilmedi."));
                return (int)value;
            }
        }

        private static void WriteRegistry(Setting setting, int? value)
        {
            if (value.HasValue)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(setting.Key))
                    key.SetValue(setting.Name, value.Value, RegistryValueKind.DWord);
            }
            else
            {
                using (var key = Registry.CurrentUser.OpenSubKey(setting.Key, true))
                    if (key != null) key.DeleteValue(setting.Name, false);
            }
        }

        private void Save(string file, Entry entry)
        {
            Directory.CreateDirectory(folder);
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] data = Encoding.UTF8.GetBytes("1|" + Format(entry.Baseline) + "|" +
                    Format(entry.Target) + "|" + Format(entry.Previous));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static string Format(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "-";
        }

        private sealed class Entry
        {
            internal readonly int? Baseline, Target, Previous;
            internal Entry(int? baseline, int? target, int? previous)
            {
                Baseline = baseline; Target = target; Previous = previous;
            }

            internal static Entry Read(string file)
            {
                if (new FileInfo(file).Length > 80)
                    throw new InvalidDataException(L.Translate("Ayar yedeği okunamadı."));
                string[] parts = File.ReadAllText(file, Encoding.UTF8).Split('|');
                if (parts.Length != 4 || parts[0] != "1")
                    throw new InvalidDataException(L.Translate("Ayar yedeğinin biçimi geçersiz."));
                return new Entry(Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
            }

            private static int? Parse(string value)
            {
                if (value == "-") return null;
                int number;
                if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
                    throw new InvalidDataException(L.Translate("Ayar yedeği okunamadı."));
                return number;
            }
        }
    }
}
