using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BakimMerkezi
{
    internal sealed class TempCleanup
    {
        private const uint Reparse = 0x400, DirectoryFlag = 0x10;
        private const uint ReadData = 1, ReadAttributes = 0x80, DeleteAccess = 0x10000;
        private const uint ReadShare = 1, WriteShare = 2;
        private const uint OpenExisting = 3, OpenReparse = 0x00200000, BackupSemantics = 0x02000000;
        private const FileAttributes ExcludedFileAttributes = FileAttributes.ReparsePoint | FileAttributes.ReadOnly |
            FileAttributes.System | FileAttributes.Offline | FileAttributes.Directory;
        private static readonly uint BasicInfoSize = (uint)Marshal.SizeOf(typeof(BasicInfo));
        private const int ScanLimit = 75000;
        private readonly string root;
        private volatile PreviewSnapshot preview;

        internal TempCleanup(string root) { this.root = Path.GetFullPath(root); }
        internal bool HasPreview
        {
            get { var snapshot = preview; return snapshot != null && snapshot.Available; }
        }

        internal Result Preview(Action<string> log, CancellationToken token = default(CancellationToken))
        {
            preview = null;
            var found = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
            int skipped = 0, visited = 0;
            long bytes = 0;
            DateTime cutoff = DateTime.UtcNow.AddDays(-7);
            log(L.Translate("Tarama konumu: ") + root);
            if (!Directory.Exists(root))
                return new Result(ResultKind.Skipped, L.Translate("Kullanıcı Temp klasörü bulunamadı veya erişilemiyor."));
            WithLocalDirectory(root, () => Scan(root, 0, cutoff, cutoff.ToFileTimeUtc(), found, token, ref bytes, ref skipped, ref visited));
            token.ThrowIfCancellationRequested();
            preview = new PreviewSnapshot(found);
            string message = string.Format(L.Translate("{0} aday dosya · {1} mantıksal boyut · {2} atlanan/erişilemeyen kayıt."),
                found.Count, Maintenance.Size(bytes), skipped);
            if (visited >= ScanLimit) message += L.Translate(" Tarama sınırına ulaşıldı. Yalnızca bulunan dosyalar temizlenebilir.");
            if (HasPreview) log(L.Translate("Önizleme bir saat geçerli. Temizlik yalnızca kimliği ve içeriğe ilişkin bilgileri aynı kalan dosyaları kapsar."));
            return new Result(skipped > 0 || visited >= ScanLimit ? ResultKind.Review : ResultKind.Done, message);
        }

        internal Result Clean(Action<string> log)
        {
            var snapshot = preview;
            if (snapshot == null || !snapshot.Available)
                return new Result(ResultKind.Skipped, L.Translate("Önce geçici dosya önizlemesini çalıştırın."));
            var approved = snapshot.Items;
            preview = null;
            int count = 0, skipped = 0;
            long bytes = 0;
            log(approved.Count + L.Translate(" önizlenen dosya yeniden doğrulanıyor."));
            WithLocalDirectory(root, () =>
            {
                var byParent = new Dictionary<string, List<KeyValuePair<string, FileStamp>>>(StringComparer.Ordinal);
                foreach (var item in approved)
                {
                    string parent = Path.GetDirectoryName(item.Key);
                    List<KeyValuePair<string, FileStamp>> list;
                    if (!byParent.TryGetValue(parent, out list))
                        byParent[parent] = list = new List<KeyValuePair<string, FileStamp>>();
                    list.Add(item);
                }
                foreach (var folder in byParent)
                {
                    try
                    {
                        WithBelowRoot(folder.Key, () =>
                        {
                            foreach (var item in folder.Value)
                            {
                                using (var handle = Open(item.Key, ReadAttributes | DeleteAccess, ReadShare))
                                {
                                    FileStamp current;
                                    byte deleteFile = 1;
                                    if (!TryStamp(handle, out current) || !item.Value.Matches(current) ||
                                        !HasExpectedPath(handle, item.Key) ||
                                        !SetFileInformationByHandle(handle, 4, ref deleteFile, 1))
                                    {
                                        skipped++;
                                        continue;
                                    }
                                    count++;
                                    bytes += current.Size;
                                }
                            }
                        });
                    }
                    catch (IOException) { skipped += folder.Value.Count; }
                    catch (UnauthorizedAccessException) { skipped += folder.Value.Count; }
                }
            });
            return new Result(skipped > 0 ? ResultKind.Review : ResultKind.Done,
                string.Format(L.Translate("{0} dosya silinmek üzere işaretlendi · {1} mantıksal boyut · {2} değişmiş, eksik veya erişilemeyen dosya atlandı."),
                    count, Maintenance.Size(bytes), skipped));
        }

        private void Scan(string path, int depth, DateTime cutoff, long cutoffFileTime, Dictionary<string, FileStamp> found,
            CancellationToken token, ref long bytes, ref int skipped, ref int visited)
        {
            try
            {
                foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if (visited >= ScanLimit) return;
                    visited++;
                    var attributes = item.Attributes;
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (depth >= 48) { skipped++; continue; }
                        try
                        {
                            using (OpenDirectory(item.FullName))
                                Scan(item.FullName, depth + 1, cutoff, cutoffFileTime, found, token, ref bytes, ref skipped, ref visited);
                        }
                        catch (IOException) { skipped++; }
                        catch (UnauthorizedAccessException) { skipped++; }
                        continue;
                    }
                    if ((attributes & ExcludedFileAttributes) != 0 || item.CreationTimeUtc >= cutoff || item.LastWriteTimeUtc >= cutoff) continue;
                    using (var handle = Open(item.FullName, ReadAttributes | ReadData, ReadShare))
                    {
                        FileStamp stamp;
                        if (!TryStamp(handle, out stamp)) { skipped++; continue; }
                        if (stamp.Created < cutoffFileTime && stamp.Written < cutoffFileTime)
                        {
                            found[item.FullName] = stamp;
                            bytes += stamp.Size;
                        }
                    }
                }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }

        internal static void WithLocalDirectory(string path, Action work)
        {
            string volume = Path.GetPathRoot(path);
            var drive = new DriveInfo(volume);
            if (drive.DriveType != DriveType.Fixed || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.Translate("Geçici dosya işlemi yerel bir NTFS sürücüsü gerektirir."));
            using (var handle = OpenDirectory(volume))
                OpenSegments(volume, path.Substring(volume.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries), 0, work);
        }

        internal static T ReadVerifiedFile<T>(string path, Func<FileStream, T> read)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 4 || path[1] != ':' || path[2] != '\\' || path.IndexOf(':', 2) >= 0)
                throw new IOException(L.Text("Yerel bir dosya seçin.", "Choose a local file."));
            string full = Path.GetFullPath(path);
            T result = default(T);
            WithLocalDirectory(Path.GetDirectoryName(full), () =>
            {
                using (var handle = Open(full, ReadAttributes | ReadData, ReadShare))
                {
                    NativeFile data;
                    const uint cloudFlags = 0x00040000 | 0x00400000;
                    if (handle.IsInvalid || !GetFileInformationByHandle(handle, out data) ||
                        (data.Attributes & (Reparse | DirectoryFlag | (uint)FileAttributes.Offline | cloudFlags)) != 0 ||
                        data.Links != 1 || !HasExpectedPath(handle, full))
                        throw new IOException(L.Text("Dosya erişilemez veya güvenli yerel dosya değil.",
                            "The file is inaccessible or is not a verified local file."));
                    using (var stream = new FileStream(handle, FileAccess.Read, 131072, false)) result = read(stream);
                }
            });
            return result;
        }

        private void WithBelowRoot(string path, Action work)
        {
            if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase)) { work(); return; }
            string prefix = root.TrimEnd('\\') + "\\";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.Translate("Önizleme yolu Temp klasörü dışında."));
            OpenSegments(root, path.Substring(prefix.Length).Split('\\'), 0, work);
        }

        private static void OpenSegments(string parent, string[] parts, int index, Action work)
        {
            if (index == parts.Length) { work(); return; }
            string next = Path.Combine(parent, parts[index]);
            // Keep each parent open without delete sharing until the file operation finishes.
            using (var handle = OpenDirectory(next))
                OpenSegments(next, parts, index + 1, work);
        }

        private static SafeFileHandle OpenDirectory(string path)
        {
            var handle = Open(path, ReadAttributes | ReadData, ReadShare | WriteShare);
            NativeFile data;
            if (!handle.IsInvalid && GetFileInformationByHandle(handle, out data) &&
                (data.Attributes & (DirectoryFlag | Reparse)) == DirectoryFlag && HasExpectedPath(handle, path)) return handle;
            handle.Dispose();
            throw new IOException(L.Translate("Klasör erişilemez veya bir bağlantı içeriyor: ") + path);
        }

        private static SafeFileHandle Open(string path, uint access, uint share)
        {
            return CreateFile(path, access, share, IntPtr.Zero, OpenExisting, OpenReparse | BackupSemantics, IntPtr.Zero);
        }

        private static bool TryStamp(SafeFileHandle handle, out FileStamp stamp)
        {
            stamp = default(FileStamp);
            NativeFile data;
            return !handle.IsInvalid && GetFileInformationByHandle(handle, out data) && TryStamp(handle, data, out stamp);
        }

        private static bool TryStamp(SafeFileHandle handle, NativeFile data, out FileStamp stamp)
        {
            stamp = default(FileStamp);
            BasicInfo basic;
            if ((data.Attributes & (uint)ExcludedFileAttributes) != 0 || data.Links != 1 ||
                !GetFileInformationByHandleEx(handle, 0, out basic, BasicInfoSize)) return false;
            stamp = new FileStamp(data, basic);
            return true;
        }

        private static bool HasExpectedPath(SafeFileHandle handle, string path)
        {
            var name = new StringBuilder(path.Length + 16);
            uint length = GetFinalPathNameByHandle(handle, name, (uint)name.Capacity, 0);
            return length > 0 && length < name.Capacity &&
                string.Equals(name.ToString().TrimEnd('\\'), (@"\\?\" + path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PreviewSnapshot
        {
            internal readonly Dictionary<string, FileStamp> Items;
            private readonly Stopwatch age = Stopwatch.StartNew();
            internal bool Available { get { return Items.Count > 0 && age.Elapsed < TimeSpan.FromHours(1); } }
            internal PreviewSnapshot(Dictionary<string, FileStamp> items) { Items = items; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFile
        {
            internal uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WrittenLow, WrittenHigh;
            internal uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicInfo
        {
            internal long Created, Accessed, Written, Changed;
            internal uint Attributes;
        }

        private struct FileStamp
        {
            internal readonly long Created, Written, Size;
            private readonly uint volume, idHigh, idLow;
            private readonly long changed;

            internal FileStamp(NativeFile data, BasicInfo basic)
            {
                Created = basic.Created;
                Written = basic.Written;
                changed = basic.Changed;
                Size = Combine(data.SizeHigh, data.SizeLow);
                volume = data.Volume; idHigh = data.IdHigh; idLow = data.IdLow;
            }

            private static long Combine(uint high, uint low) { return ((long)high << 32) | low; }

            internal bool Matches(FileStamp other)
            {
                return Created == other.Created && Written == other.Written && changed == other.changed && Size == other.Size &&
                    volume == other.volume && idHigh == other.idHigh && idLow == other.idLow;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out NativeFile data);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
            out BasicInfo info, uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder name, uint size, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
            ref byte information, uint size);
    }
}
