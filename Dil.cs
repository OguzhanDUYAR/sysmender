using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BakimMerkezi
{
    internal static class L
    {
        private static readonly Lazy<Dictionary<string, string>> EnglishText =
            new Lazy<Dictionary<string, string>>(ReadTranslations);
        internal static bool English { get; private set; }
        internal static string Code { get { return English ? "en" : "tr"; } }
        internal static string Text(string turkish, string english) { return English ? english : turkish; }
        internal static string Translate(string turkish)
        {
            string translated;
            return English && EnglishText.Value.TryGetValue(turkish, out translated) ? translated : turkish;
        }

        private static Dictionary<string, string> ReadTranslations()
        {
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var reader = new StringReader(Program.Resource("English.tsv")))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length != 2) throw new InvalidDataException("Invalid English translation entry.");
                    text.Add(parts[0].Replace(@"\\", @"\"), parts[1].Replace(@"\\", @"\"));
                }
            }
            return text;
        }
        private static string Preference { get { return Path.Combine(Program.Data, "Dil.txt"); } }

        internal static void Initialize(string requested)
        {
            if (requested == "en" || requested == "tr") { English = requested == "en"; return; }
            try
            {
                var file = new FileInfo(Preference);
                English = file.Exists && file.Length < 16 && File.ReadAllText(Preference, Encoding.ASCII).Trim() == "en";
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void Set(string code)
        {
            if (code != "en" && code != "tr") throw new ArgumentException("Invalid language.");
            if (Program.Admin) { English = code == "en"; return; }
            Directory.CreateDirectory(Program.Data);
            string temp = Preference + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, code, Encoding.ASCII);
                if (File.Exists(Preference)) File.Replace(temp, Preference, null);
                else File.Move(temp, Preference);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            English = code == "en";
        }
    }
}
