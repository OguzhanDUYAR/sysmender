using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace BakimMerkezi
{
    internal sealed class ReportStep
    {
        public string Id { get; set; }
        public string Outcome { get; set; }
        public string Tool { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
    }

    internal sealed class ReportDocument
    {
        public ReportDocument() { }
        public int Schema { get; set; }
        public string Application { get; set; }
        public string Version { get; set; }
        public string DevelopedBy { get; set; }
        public string Created { get; set; }
        public string Computer { get; set; }
        public string Session { get; set; }
        public bool SummaryOnly { get; set; }
        public ReportStep[] Steps { get; set; }
        public string Log { get; set; }
    }

    internal static class SupportReport
    {
        private const int ReportLimit = 16 * 1024 * 1024;

        internal static string Build(ReportStep[] steps, string log, bool json, bool summaryOnly = false)
        {
            if(log==null || log.Length>8*1024*1024) throw new InvalidDataException("Report exceeds size limit.");
            var data=new ReportDocument { Schema=1, Application=Program.Name, Version=Program.Version, DevelopedBy="Oguzhan DUYAR",
                Created=DateTimeOffset.Now.ToString("o"), Computer=summaryOnly ? "" : Environment.MachineName,
                Session=summaryOnly ? "" : Program.Admin ? "Administrator" : "Standard user", SummaryOnly=summaryOnly,
                Steps=summaryOnly ? steps.Select(step => new ReportStep { Id=step.Id, Tool=step.Id,
                    Status=step.Status, Outcome=step.Outcome, Message="" }).ToArray() : steps, Log=summaryOnly ? "" : log };
            if(json) return new JavaScriptSerializer { MaxJsonLength=ReportLimit }.Serialize(data);
            var html=new StringBuilder();
            html.Append("<!doctype html><html lang='").Append(L.Code).Append("'><meta charset='utf-8'>");
            html.Append("<meta name='viewport' content='width=device-width,initial-scale=1'>");
            html.Append("<meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">");
            html.Append("<title>IT support report</title><style>body{background:#070907;color:#edf4eb;font:15px system-ui;margin:32px;max-width:1200px}h1,h2{color:#b8ff3c}table{width:100%;border-collapse:collapse}td,th{border:1px solid #394835;text-align:left;padding:10px;vertical-align:top}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#111611;padding:18px}small{color:#b1c2ad}</style>");
            html.Append("<h1>").Append(E(Program.Name)).Append(" ").Append(E(Program.Version)).Append("</h1><p>");
            html.Append(E(data.Computer)).Append(" · ").Append(E(data.Created)).Append(" · ").Append(E(data.Session)).Append("</p>");
            html.Append("<small>Developed by Oguzhan DUYAR</small><h2>").Append(E(L.Text("İşlem sonuçları","Action results"))).Append("</h2>");
            html.Append("<table><thead><tr><th>").Append(E(L.Text("Araç","Tool"))).Append("</th><th>").Append(E(L.Text("Durum","Status"))).Append("</th><th>").Append(E(L.Text("Sonuç","Result"))).Append("</th></tr></thead><tbody>");
            foreach(var step in data.Steps)
                html.Append("<tr><td>").Append(E(step.Tool)).Append("</td><td>").Append(E(step.Status)).Append("</td><td>").Append(E(step.Message)).Append("</td></tr>");
            html.Append("</tbody></table><h2>").Append(E(L.Text("İşlem günlüğü","Execution log"))).Append("</h2><pre>").Append(E(data.Log)).Append("</pre></html>");
            return html.ToString();
        }

        internal static ReportDocument Load(string path)
        {
            return TempCleanup.ReadVerifiedFile(path, stream =>
            {
                if (stream.Length > ReportLimit) throw new InvalidDataException("Report exceeds size limit.");
                using (var reader = new StreamReader(stream))
                    return Parse(reader.ReadToEnd());
            });
        }

        internal static ReportDocument Parse(string json)
        {
            var data = new JavaScriptSerializer { MaxJsonLength=ReportLimit, RecursionLimit=24 }.Deserialize<ReportDocument>(json);
            if (data == null || (data.Schema != 0 && data.Schema != 1) ||
                (data.Application != "Windows Bakım Merkezi" && data.Application != "Windows Maintenance Center") ||
                (data.Created ?? "").Length > 80 || (data.Computer ?? "").Length > 256 ||
                (data.Version ?? "").Length > 40 ||
                data.Steps == null || data.Steps.Length > 5000 || data.Steps.Any(step => step == null ||
                    string.IsNullOrWhiteSpace(step.Tool) || step.Tool.Length > 1024 || (step.Id ?? "").Length > 160 ||
                    (step.Outcome ?? "").Length > 32 || (step.Status ?? "").Length > 160 || (step.Message ?? "").Length > 8192))
                throw new InvalidDataException(L.Text("Destek raporu biçimi geçersiz.", "Invalid support report format."));
            return data;
        }

        internal static string Compare(ReportDocument first, ReportDocument second)
        {
            var text = new StringBuilder();
            text.AppendLine(L.Text("Önce: ", "Before: ") + first.Created);
            text.AppendLine(L.Text("Sonra: ", "After: ") + second.Created);
            if (!string.IsNullOrEmpty(first.Computer) && !string.IsNullOrEmpty(second.Computer) &&
                !string.Equals(first.Computer, second.Computer, StringComparison.OrdinalIgnoreCase))
                text.AppendLine(L.Text("Raporlar farklı bilgisayarlardan.", "Reports are from different computers."));
            if (first.SummaryOnly || second.SummaryOnly)
                text.AppendLine(L.Text("Özet rapor karşılaştırması yalnızca durumları inceler.", "Summary comparison uses status only."));
            bool legacy = first.Steps.All(step => string.IsNullOrEmpty(step.Id)) ||
                second.Steps.All(step => string.IsNullOrEmpty(step.Id));
            var repeated = new HashSet<string>(first.Steps.GroupBy(step => step.Id).Concat(second.Steps.GroupBy(step => step.Id))
                .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.Ordinal);
            var before = Index(first.Steps, legacy, repeated);
            var after = Index(second.Steps, legacy, repeated);
            int changed = 0, added = 0, removed = 0, same = 0;
            var details = new StringBuilder();
            foreach (string key in before.Keys.Union(after.Keys, StringComparer.Ordinal))
            {
                ReportStep oldStep, newStep;
                before.TryGetValue(key, out oldStep);
                after.TryGetValue(key, out newStep);
                if (oldStep == null)
                {
                    added++;
                    if (added + removed + changed <= 250) details.AppendLine("+ " + newStep.Tool + " · " + newStep.Status);
                }
                else if (newStep == null)
                {
                    removed++;
                    if (added + removed + changed <= 250) details.AppendLine("- " + oldStep.Tool + " · " + oldStep.Status);
                }
                else
                {
                    bool canonical = !string.IsNullOrEmpty(oldStep.Outcome) && !string.IsNullOrEmpty(newStep.Outcome);
                    bool statusChanged = canonical ? oldStep.Outcome != newStep.Outcome : oldStep.Status != newStep.Status;
                    bool detailChanged = !first.SummaryOnly && !second.SummaryOnly && oldStep.Message != newStep.Message;
                    if (!statusChanged && !detailChanged) { same++; continue; }
                    changed++;
                    if (added + removed + changed <= 250)
                        details.AppendLine("~ " + newStep.Tool + " · " + oldStep.Status + " → " + newStep.Status +
                            (detailChanged ? "\n  " + Limit(oldStep.Message) + "\n  → " + Limit(newStep.Message) : ""));
                }
            }
            text.AppendLine(string.Format(L.Text("{0} değişen, {1} eklenen, {2} çıkarılan, {3} aynı.",
                "{0} changed, {1} added, {2} removed, {3} unchanged."), changed, added, removed, same));
            if (changed + added + removed > 250)
                text.AppendLine(L.Text("İlk 250 fark gösterildi.", "Showing the first 250 differences."));
            text.AppendLine();
            text.Append(details);
            return text.ToString();
        }

        private static Dictionary<string, ReportStep> Index(ReportStep[] steps, bool legacy, HashSet<string> repeated)
        {
            var result = new Dictionary<string, ReportStep>(StringComparer.Ordinal);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var step in steps)
            {
                string key = legacy || string.IsNullOrEmpty(step.Id) ? step.Tool :
                    repeated.Contains(step.Id) ? step.Id + "\0" + step.Tool : step.Id;
                int occurrence;
                occurrences.TryGetValue(key, out occurrence);
                occurrences[key] = occurrence + 1;
                result.Add(key + "\0" + occurrence, step);
            }
            return result;
        }

        private static string Limit(string text) { return (text ?? "").Length > 350 ? text.Substring(0, 350) + "…" : text ?? ""; }
        private static string E(string text) { return WebUtility.HtmlEncode(text??""); }
        internal static void WriteNew(string path,string data)
        {
            if(!Startup.SafeFolder(Path.GetDirectoryName(Path.GetFullPath(path))))
                throw new IOException(L.Text("Bağlantı içermeyen yerel bir klasör seçin.", "Choose a local folder without links."));
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(true))) writer.Write(data);
        }
    }
}

