using System;
using System.IO;
using System.Text;

namespace BakimMerkezi
{
    internal static class UserPreference
    {
        internal static string Read(string name, int limit)
        {
            if (Program.Admin) return "";
            try
            {
                return TempCleanup.ReadVerifiedFile(Path.Combine(Program.Data, name), stream =>
                {
                    if (stream.Length > limit) return "";
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
                });
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
            catch (System.Security.SecurityException) { return ""; }
        }

        internal static void Write(string name, string value)
        {
            if (Program.Admin) return;
            if (!Startup.SafeFolder(Program.Data)) throw new IOException("The settings folder is not a safe local folder.");
            Directory.CreateDirectory(Program.Data);
            string path = Path.Combine(Program.Data, name);
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(value);
                if (File.Exists(path)) File.Replace(staging, path, null);
                else File.Move(staging, path);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
    }
}

