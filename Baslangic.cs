using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace BakimMerkezi
{
    internal sealed class StartupItem
    {
        internal readonly string Name, Value;
        internal readonly bool Shortcut, Present, Saved, Conflict;
        internal readonly RegistryValueKind Kind;
        internal readonly ShortcutStamp Stamp;

        internal StartupItem(string name, string value, bool shortcut, bool present, bool saved,
            bool conflict, RegistryValueKind kind = RegistryValueKind.Unknown, ShortcutStamp stamp = null)
        {
            Name = name; Value = value; Shortcut = shortcut; Present = present;
            Saved = saved; Conflict = conflict; Kind = kind; Stamp = stamp;
        }

        internal string Source { get { return Shortcut ? L.Text("Başlangıç klasörü", "Startup folder") : "HKCU Run"; } }
        internal string Status
        {
            get
            {
                if (Conflict) return L.Text("Çakışma", "Conflict");
                if (!Present) return L.Text("Devre dışı", "Disabled");
                return Saved ? L.Text("Kayıt var · yedekli", "Entry present · backed up") :
                    L.Text("Kayıt var", "Entry present");
            }
        }
    }

    internal sealed class ShortcutStamp
    {
        private readonly long length;
        private readonly DateTime created, written;

        internal ShortcutStamp(FileInfo info)
        {
            length = info.Length; created = info.CreationTimeUtc; written = info.LastWriteTimeUtc;
        }

        internal bool Matches(ShortcutStamp other)
        {
            return other != null && length == other.length && created == other.created && written == other.written;
        }
    }

    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SaveKey = @"Software\WindowsBakimMerkezi\StartupRun";
        private static readonly string Folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup,
            Environment.SpecialFolderOption.DoNotVerify);
        private static readonly string Saved = Path.Combine(Program.Data, "StartupShortcuts");

        internal static List<StartupItem> Read(Action<string> warning)
        {
            RequireUser();
            var items = new List<StartupItem>();
            ReadSource(() => AddRegistryItems(items), "HKCU Run", warning);
            ReadSource(() => AddShortcutItems(items), L.Text("Başlangıç klasörü", "Startup folder"), warning);
            return items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void ReadSource(Action action, string source, Action<string> warning)
        {
            try { action(); }
            catch (IOException ex) { warning(source + " · " + ex.Message); }
            catch (UnauthorizedAccessException ex) { warning(source + " · " + ex.Message); }
            catch (System.Security.SecurityException ex) { warning(source + " · " + ex.Message); }
        }

        private static void AddRegistryItems(List<StartupItem> items)
        {
            var active = RegistryEntries(RunKey);
            var backups = RegistryEntries(SaveKey);
            foreach (string name in active.Keys.Union(backups.Keys, StringComparer.OrdinalIgnoreCase))
            {
                StartupItem live, backup;
                active.TryGetValue(name, out live);
                backups.TryGetValue(name, out backup);
                var item = live ?? backup;
                items.Add(new StartupItem(item.Name, item.Value, false, live != null, backup != null,
                    live != null && backup != null && !Same(live, backup), item.Kind));
            }
        }

        private static void AddShortcutItems(List<StartupItem> items)
        {
            var links = Shortcuts(Folder);
            var archived = Shortcuts(Saved);
            foreach (string name in links.Union(archived, StringComparer.OrdinalIgnoreCase))
            {
                bool live = links.Contains(name), backup = archived.Contains(name);
                string path = Path.Combine(live ? Folder : Saved, name);
                var stamp = ReadShortcut(path);
                if (stamp != null)
                    items.Add(new StartupItem(name, path, true, live, backup, live && backup, stamp: stamp));
            }
        }

        private static Dictionary<string, StartupItem> RegistryEntries(string path)
        {
            var entries = new Dictionary<string, StartupItem>(StringComparer.OrdinalIgnoreCase);
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                if (key == null) return entries;
                foreach (string name in key.GetValueNames())
                {
                    StartupItem item = ReadEntry(key, name);
                    if (item != null) entries[name] = item;
                }
            }
            return entries;
        }

        private static StartupItem ReadEntry(RegistryKey key, string name)
        {
            if (key == null || string.IsNullOrWhiteSpace(name)) return null;
            object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return null;
            RegistryValueKind kind = key.GetValueKind(name);
            if (!(value is string) || (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString)) return null;
            return new StartupItem(name, (string)value, false, true, false, false, kind);
        }

        private static bool Same(StartupItem left, StartupItem right)
        {
            return left != null && right != null && left.Value == right.Value && left.Kind == right.Kind;
        }

        private static HashSet<string> Shortcuts(string folder)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!SafeFolder(folder) || !Directory.Exists(folder)) return files;
            foreach (string file in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
                if (IsShortcut(file)) files.Add(Path.GetFileName(file));
            return files;
        }

        internal static bool IsShortcut(string file)
        {
            return File.Exists(file) && string.Equals(Path.GetExtension(file), ".lnk", StringComparison.OrdinalIgnoreCase) &&
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0;
        }

        private static ShortcutStamp ReadShortcut(string file)
        {
            var info = new FileInfo(file);
            if (!info.Exists || !string.Equals(info.Extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            return new ShortcutStamp(info);
        }

        internal static bool SafeFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return false;
            string root = Path.GetPathRoot(path);
            if (root.Length != 3 || root[1] != ':' || new DriveInfo(root).DriveType != DriveType.Fixed) return false;
            var directory = new DirectoryInfo(path);
            while (directory != null)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                directory = directory.Parent;
            }
            return true;
        }

        internal static Job Change(StartupItem item, bool disable)
        {
            return new Job("startup-change", (disable ? L.Text("Başlangıçtan çıkar", "Disable at sign-in") :
                L.Text("Başlangıcı geri yükle", "Restore at sign-in")) + " · " + item.Name, log =>
            {
                RequireUser();
                if (item.Conflict) return Conflict();
                log(item.Source + " · " + item.Name);
                return item.Shortcut ? MoveShortcut(item, disable) : ChangeEntry(item, disable);
            });
        }

        private static Result ChangeEntry(StartupItem item, bool disable)
        {
            using (var live = Registry.CurrentUser.CreateSubKey(RunKey))
            using (var saved = Registry.CurrentUser.CreateSubKey(SaveKey))
            {
                StartupItem original = ReadEntry(live, item.Name);
                StartupItem backup = ReadEntry(saved, item.Name);
                if (disable)
                {
                    if (!Same(original, item) || (backup != null && !Same(original, backup))) return Conflict();
                    if (backup == null)
                    {
                        if (Exists(saved, item.Name)) return Conflict();
                        saved.SetValue(item.Name, original.Value, original.Kind);
                        saved.Flush();
                    }
                    if (!Same(ReadEntry(live, item.Name), original) || !Same(ReadEntry(saved, item.Name), original)) return Conflict();
                    live.DeleteValue(item.Name);
                    if (Exists(live, item.Name)) return Conflict();
                }
                else
                {
                    if (!Same(backup, item) || (Exists(live, item.Name) && !Same(original, backup))) return Conflict();
                    if (original == null)
                    {
                        live.SetValue(item.Name, backup.Value, backup.Kind);
                        live.Flush();
                    }
                    if (!Same(ReadEntry(live, item.Name), backup) || !Same(ReadEntry(saved, item.Name), backup)) return Conflict();
                    saved.DeleteValue(item.Name);
                }
            }
            return Done(disable);
        }

        private static bool Exists(RegistryKey key, string name)
        {
            return key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        private static Result MoveShortcut(StartupItem item, bool disable)
        {
            return MoveShortcutBetween(Folder, Saved, item.Name, disable, item.Stamp);
        }

        internal static Result MoveShortcutBetween(string live, string backup, string name, bool disable, ShortcutStamp expected = null)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || !SafeFolder(live) || !SafeFolder(backup) ||
                !string.Equals(Path.GetPathRoot(live), Path.GetPathRoot(backup), StringComparison.OrdinalIgnoreCase))
                return new Result(ResultKind.Skipped, L.Text("Kısayol için aynı yerel sürücüde güvenli klasörler gerekir.",
                    "The shortcut requires safe folders on the same local drive."));
            string from = Path.Combine(disable ? live : backup, name);
            string to = Path.Combine(disable ? backup : live, name);
            var current = ReadShortcut(from);
            if (current == null || (expected != null && !expected.Matches(current)) || File.Exists(to) || Directory.Exists(to))
                return Conflict();
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (!SafeFolder(Path.GetDirectoryName(to)) || !current.Matches(ReadShortcut(from))) return Conflict();
            File.Move(from, to);
            return Done(disable);
        }

        private static Result Conflict()
        {
            return new Result(ResultKind.Review, L.Text("Başlangıç kaydı değişti veya hedef dolu. Listeyi yenileyin.", 
                "The startup entry changed or its destination is occupied. Refresh the list."));
        }

        private static Result Done(bool disable)
        {
            return new Result(ResultKind.Done, disable ? L.Text("Başlangıç kaydı yedeklendi ve devre dışı bırakıldı.",
                "The startup entry was backed up and disabled.") :
                L.Text("Önceki başlangıç kaydı geri yüklendi.", "The previous startup entry was restored."));
        }

        private static void RequireUser()
        {
            if (Program.Admin) throw new InvalidOperationException(L.Text("Normal kullanıcı penceresini açın.", "Open the standard user window."));
        }
    }
}
