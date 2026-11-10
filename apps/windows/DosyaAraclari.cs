using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace BakimMerkezi
{
    internal static class FileTools
    {
        private const int EntryLimit = 20000;
        private const long SingleHashLimit = 2L * 1024 * 1024 * 1024;
        private const long DuplicateFileLimit = 256L * 1024 * 1024;
        private const long DuplicateBudget = 512L * 1024 * 1024;
        private const int SkipMask = (int)(FileAttributes.ReparsePoint | FileAttributes.Offline) | 0x00040000 | 0x00400000;

        internal static Job CreateHash(string path, string expected)
        {
            string file = LocalPath(path);
            expected = (expected ?? "").Trim();
            if (expected.Length > 0 && !Regex.IsMatch(expected, @"\A[0-9A-Fa-f]{64}\z"))
                throw new ArgumentException(L.Text("Beklenen SHA-256 tam 64 onaltılık karakter olmalı.",
                    "The expected SHA-256 must contain exactly 64 hexadecimal characters."));
            return new Job("file-hash", L.Text("Dosya SHA-256", "File SHA-256"), (log, token) =>
            {
                log(file);
                string digest = TempCleanup.ReadVerifiedFile(file, stream =>
                {
                    if (stream.Length > SingleHashLimit) throw new IOException(L.Text("Dosya 2 GiB sınırını aşıyor.",
                        "The file exceeds the 2 GiB limit."));
                    return Hash(stream, token);
                });
                log("SHA-256 " + digest);
                if (expected.Length == 0) return new Result(ResultKind.Done, "SHA-256 " + digest);
                bool matches = string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase);
                return new Result(matches ? ResultKind.Done : ResultKind.Review, matches ?
                    L.Text("SHA-256 beklenen değerle eşleşiyor.", "SHA-256 matches the expected value.") :
                    L.Text("SHA-256 beklenen değerden farklı.", "SHA-256 differs from the expected value."));
            });
        }

        internal static Job CreateScan(string folder, bool duplicates)
        {
            string root = LocalPath(folder);
            return new Job(duplicates ? "files-duplicates" : "files-largest",
                duplicates ? L.Text("Aynı içerikli dosyalar", "Files with matching content") : L.Text("En büyük dosyalar", "Largest files"),
                (log, token) => Scan(root, duplicates, log, token));
        }

        private static string LocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"\A[A-Za-z]:\\"))
                throw new ArgumentException(L.Text("Yerel NTFS dosyası veya klasörü seçin.", "Choose a local NTFS file or folder."));
            if (path.IndexOf(':', 2) >= 0) throw new ArgumentException("Alternate data streams are not supported.");
            return Path.GetFullPath(path);
        }

        internal static string Hash(Stream stream, CancellationToken token)
        {
            using (var sha = SHA256.Create())
            {
                byte[] buffer = new byte[131072];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }

        private sealed class Entry
        {
            internal readonly string Path;
            internal readonly long Size;
            internal Entry(string path, long size) { Path = path; Size = size; }
        }

        private static Result Scan(string root, bool duplicates, Action<string> log, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            var stack = new Stack<string>();
            var files = new List<Entry>();
            int visited = 0, skipped = 0;
            bool limited = false;
            TempCleanup.WithLocalDirectory(root, () => { });
            stack.Push(root);
            log(root);
            while (stack.Count > 0 && !limited)
            {
                token.ThrowIfCancellationRequested();
                string directory = stack.Pop();
                try
                {
                    TempCleanup.WithLocalDirectory(directory, () =>
                    {
                        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                        {
                            token.ThrowIfCancellationRequested();
                            if (visited >= EntryLimit || watch.Elapsed.TotalSeconds > 35) { limited = true; break; }
                            visited++;
                            try
                            {
                                var attr = File.GetAttributes(path);
                                if (((int)attr & SkipMask) != 0) { skipped++; continue; }
                                if ((attr & FileAttributes.Directory) != 0) stack.Push(path);
                                else files.Add(new Entry(path, new FileInfo(path).Length));
                            }
                            catch (IOException) { skipped++; }
                            catch (UnauthorizedAccessException) { skipped++; }
                        }
                    });
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }

            int groups = 0;
            if (!duplicates)
            {
                foreach (var entry in files.OrderByDescending(file => file.Size).Take(30))
                    log(Maintenance.Size(entry.Size) + " · " + entry.Path);
            }
            else
            {
                long hashed = 0;
                foreach (var sizeGroup in files.Where(file => file.Size > 0).GroupBy(file => file.Size).Where(group => group.Count() > 1))
                {
                    var hashes = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
                    foreach (var entry in sizeGroup)
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.Size > DuplicateFileLimit) { skipped++; continue; }
                        if (watch.Elapsed.TotalSeconds > 60 || entry.Size > DuplicateBudget - hashed) { limited = true; break; }
                        try
                        {
                            string digest = TempCleanup.ReadVerifiedFile(entry.Path, stream =>
                            {
                                if (stream.Length != entry.Size) throw new IOException("File length changed.");
                                hashed += entry.Size;
                                return Hash(stream, token);
                            });
                            List<Entry> same;
                            if (!hashes.TryGetValue(digest, out same)) hashes.Add(digest, same = new List<Entry>());
                            same.Add(entry);
                        }
                        catch (IOException) { skipped++; }
                        catch (UnauthorizedAccessException) { skipped++; }
                    }
                    foreach (var match in hashes.Where(pair => pair.Value.Count > 1))
                    {
                        groups++;
                        if (groups > 30) { limited = true; break; }
                        log("SHA-256 " + match.Key + " · " + Maintenance.Size(sizeGroup.Key) + " · " + match.Value.Count);
                        foreach (var entry in match.Value.Take(20)) log("  " + entry.Path);
                        if (match.Value.Count > 20) limited = true;
                    }
                    if (limited) break;
                }
            }
            if (limited) log(L.Text("Tarama veya çıktı sınırına ulaşıldı.", "The scan or output limit was reached."));
            string summary = string.Format(L.Text("{0} girdi, {1} dosya, {2} atlama.", "{0} entries, {1} files, {2} skips."),
                visited, files.Count, skipped);
            if (duplicates) summary += " " + groups + L.Text(" eşleşen grup.", " matching groups.");
            return new Result(limited || skipped > 0 ? ResultKind.Review : ResultKind.Done, summary);
        }
    }
}

