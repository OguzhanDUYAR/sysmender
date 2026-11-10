using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal sealed class ChoiceWindow : Form
    {
        private readonly ComboBox choices;
        private readonly TextBox input;
        internal int Selection { get { return choices.SelectedIndex; } }
        internal string Input { get { return input == null ? "" : input.Text; } }

        internal ChoiceWindow(string title, string note, string[] items, string optionalInput = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(550, optionalInput == null ? 217 : 277);
            MinimumSize = Size;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            BackColor = Style.Canvas;
            ForeColor = Style.Ink;
            var layout = Style.Stack(81, 46, optionalInput == null ? 1 : 52, 45);
            layout.Padding = new Padding(15, 8, 15, 8);
            Controls.Add(layout);
            layout.Controls.Add(Style.Note(note), 0, 0);
            choices = Style.Combo(title, 500);
            choices.Dock = DockStyle.Fill;
            choices.Items.AddRange(items.Cast<object>().ToArray());
            choices.SelectedIndex = 0;
            layout.Controls.Add(choices, 0, 1);
            if (optionalInput != null)
            {
                var extra = Style.Stack(23, 0);
                extra.Controls.Add(Style.Text(optionalInput, Style.Small), 0, 0);
                input = new TextBox { Dock = DockStyle.Fill, MaxLength = 64, AccessibleName = optionalInput,
                    ForeColor = Style.Ink, BackColor = Style.Surface, Font = Style.Mono };
                extra.Controls.Add(input, 0, 1);
                layout.Controls.Add(extra, 0, 2);
                choices.SelectedIndexChanged += (sender, e) => extra.Visible = choices.SelectedIndex == 0;
            }
            var buttons = Style.Row();
            var proceed = Style.Button(L.Text("Devam", "Continue"), () => { }, true);
            var cancel = Style.Button(L.Translate("Vazgeç"), () => { });
            proceed.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(proceed);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 3);
            AcceptButton = proceed;
            CancelButton = cancel;
        }
    }

    internal sealed class SupportScenario
    {
        private readonly string turkish, english, trAdvice, enAdvice;
        internal readonly string[] Diagnostics, Repair, Verify;
        internal readonly bool RepairAdmin;
        internal string Title { get { return L.Text(turkish, english); } }
        internal string Advice { get { return L.Text(trAdvice, enAdvice); } }

        private SupportScenario(string turkish, string english, string trAdvice, string enAdvice,
            string[] diagnostics, string[] repair, string[] verify, bool repairAdmin = false)
        {
            this.turkish = turkish; this.english = english; this.trAdvice = trAdvice; this.enAdvice = enAdvice;
            Diagnostics = diagnostics; Repair = repair; Verify = verify; RepairAdmin = repairAdmin;
        }

        internal static readonly SupportScenario[] All =
        {
            new SupportScenario("Windows Update hata veriyor", "Windows Update fails",
                "Günlüklerde bileşen deposu veya sistem dosyası bulgusu varsa DISM ve SFC planını inceleyin. Güncelleme ilkeleri korunur.",
                "If findings point to the component store or system files, review the DISM and SFC plan. Update policies are preserved.",
                new[] { "readiness", "update-services", "update-history", "servicing-logs" },
                new[] { "maintenance:dism-repair", "maintenance:sfc" }, new[] { "sfc-verify", "servicing-logs" }, true),
            new SupportScenario("Ağ veya DNS sorunu", "Network or DNS issue",
                "DNS önbelleği sorunu düşünüyorsanız temizleme planını inceleyin. Ardından sorun yaşanan adresi hedef testiyle yeniden sınayın.",
                "If DNS cache is suspected, review the flush plan. Then retest the affected address using the target tool.",
                new[] { "network-adapters", "network-config", "proxy", "tcp-udp" },
                new[] { "maintenance:flush" }, new[] { "network-config" }, true),
            new SupportScenario("Yazdırma çalışmıyor", "Printing fails",
                "Kuyruk veya Spooler bulgusu varsa hizmeti yeniden başlatmayı inceleyin. Yazdırma kısa süre kesilebilir.",
                "If the queue or Spooler findings support it, review restarting the service. Printing can be briefly interrupted.",
                new[] { "printers", "critical-services" },
                new[] { "spooler-restart" }, new[] { "printers", "critical-services" }, true),
            new SupportScenario("Disk alanı azalıyor", "Disk space is running low",
                "Yalnızca Temp önizlemesinde doğrulanan dosyalar temizlenebilir. Son ölçümü ilk birim raporuyla karşılaştırın.",
                "Only files confirmed in the Temp preview can be removed. Compare the final volume report with the baseline.",
                new[] { "volumes", "maintenance:temp-preview" }, new[] { "maintenance:temp-clean" }, new[] { "volumes" }),
            new SupportScenario("Microsoft Store açılmıyor", "Microsoft Store fails",
                "Store paket kaydında sorun varsa yeniden kayıt planını inceleyin. Önce Store uygulamasını kapatın.",
                "If Store package registration shows a problem, review re-registration. Close the Store app first.",
                new[] { "maintenance:store-check", "readiness" }, new[] { "maintenance:store-repair" },
                new[] { "maintenance:store-check" }),
            new SupportScenario("Bilgisayar yavaş", "The PC is slow",
                "Bellek, güç planı ve servis bulgularını inceleyin. Başlangıç uygulamalarını ayrı ekrandan seçerek yönetebilirsiniz.",
                "Review memory, power and service findings. Manage individual startup entries from Startup apps.",
                new[] { "processes", "power", "critical-services", "volumes" }, new string[0], new string[0]),
            new SupportScenario("Mavi ekran veya beklenmeyen kapanma", "Bug checks or unexpected shutdowns",
                "Olay saatlerini ve döküm envanterini karşılaştırın. Kayıtlar tek başına donanım arızası kanıtı değildir.",
                "Correlate event timestamps and dump inventory. These records alone do not prove a hardware failure.",
                new[] { "crashes", "dump-list", "device-errors", "winre", "boot-config" }, new string[0], new string[0])
        };
    }

    internal sealed partial class MainWindow
    {
        private SupportScenario guidedScenario;
        private bool guidedFollowUp;
        private Button guideNext, openGuide, openFileTools;

        private void ShowGuide()
        {
            if (Busy) return;
            Safe(() =>
            {
                var available = SupportScenario.All.Where(scenario => !Program.Admin ||
                    scenario.RepairAdmin || scenario.Repair.Length == 0).ToArray();
                using (var pick = new ChoiceWindow(L.Text("Sorun giderme asistanı", "Troubleshooting assistant"),
                    L.Text("Bir belirti seçin. Önce yerel tanılama çalışır. Onarım ve doğrulama ayrı plan olarak incelenir.",
                        "Choose a symptom. Local diagnostics run first. Repair and verification are reviewed in a separate plan."),
                    available.Select(scenario => scenario.Title).ToArray()))
                {
                    if (pick.ShowDialog(this) != DialogResult.OK) return;
                    var scenario = available[pick.Selection];
                    string[] tools = scenario.Diagnostics.Where(GuideAvailable).ToArray();
                    Review(scenario.Title, L.Text("Aşağıdaki okuma adımlarını inceleyin. Sonraki adım sonuç ekranında açılır.",
                        "Review these read-only steps. The next step opens from the results page."),
                        tools.Select(CreateGuideJob).ToArray(), tools.Select(GuideDescription).ToArray(), scenario);
                }
            });
        }

        private void ShowGuideNext()
        {
            if (Busy || guidedScenario == null || guidedFollowUp || string.IsNullOrEmpty(lastLog)) return;
            var scenario = guidedScenario;
            Safe(() =>
            {
                if (scenario.Repair.Length == 0)
                {
                    Inform(scenario.Advice);
                    return;
                }
                if (!resultsGrid.Rows.Cast<DataGridViewRow>().Any(row => row.Tag is RunResult &&
                    (((RunResult)row.Tag).Value.Kind == ResultKind.Done || ((RunResult)row.Tag).Value.Kind == ResultKind.Review)))
                {
                    Inform(L.Text("Tanılama tamamlanmış bir okuma üretmedi. Önce günlükte erişim ve diğer hataları inceleyin.",
                        "Diagnostics did not produce a completed read. Review access and other errors in the log first."));
                    return;
                }
                if (scenario.RepairAdmin != Program.Admin)
                {
                    if (scenario.RepairAdmin)
                    {
                        if (MessageBox.Show(this, scenario.Advice + "\n\n" +
                            L.Text("Yönetici penceresini açıp aynı senaryoyu orada yeniden çalıştırın.",
                                "Open the administrator window and rerun the same scenario there."), scenario.Title,
                            MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
                            Program.Elevate("it");
                    }
                    else Inform(L.Text("Bu işlem standart kullanıcı penceresine aittir.", "This action belongs in the standard user window."));
                    return;
                }
                if (scenario.Repair.Contains("maintenance:temp-clean") && !temp.HasPreview)
                {
                    Inform(L.Text("Geçerli bir Temp önizlemesi yok veya temizlenebilecek dosya bulunmadı.",
                        "There is no current Temp preview or there are no eligible files."));
                    return;
                }
                string previous = lastLog;
                var jobs = scenario.Repair.Concat(scenario.Verify).Select(CreateGuideJob).ToArray();
                var items = scenario.Repair.Select(GuideDescription).Concat(scenario.Verify.Select(id =>
                    L.Text("Doğrulama · ", "Verification · ") + GuideDescription(id))).ToArray();
                Review(scenario.Title, scenario.Advice, jobs, items, scenario, true, previous);
            });
        }

        internal static bool GuideAvailable(string id)
        {
            if (id.StartsWith("maintenance:", StringComparison.Ordinal))
                return Maintenance.Tasks.Single(task => task.Id == id.Substring(12)).Admin == Program.Admin;
            var tool = ItTools.All.Single(item => item.Id == id);
            return !tool.Admin || Program.Admin;
        }

        private Job CreateGuideJob(string id)
        {
            if (id.StartsWith("maintenance:", StringComparison.Ordinal))
                return Maintenance.Create(Maintenance.Tasks.Single(task => task.Id == id.Substring(12)), null, temp);
            return ItTools.Create(id);
        }

        private static string GuideDescription(string id)
        {
            if (id.StartsWith("maintenance:", StringComparison.Ordinal))
            {
                var task = Maintenance.Tasks.Single(item => item.Id == id.Substring(12));
                return task.Title + " · " + task.Detail;
            }
            var tool = ItTools.All.Single(item => item.Id == id);
            return tool.Title + " · " + tool.Detail;
        }

        private void ShowFileTools()
        {
            if (Busy || Program.Admin) return;
            Safe(() =>
            {
                using (var pick = new ChoiceWindow(L.Text("Dosya araçları", "File tools"),
                    L.Text("Yerel NTFS dosyası veya klasörü seçin. Büyük dosya ve kopya taramaları yalnızca rapor üretir.",
                        "Choose a local NTFS file or folder. Large-file and duplicate scans produce reports only."),
                    new[] { "SHA-256", L.Text("En büyük dosyalar", "Largest files"), L.Text("Aynı içerikli dosyalar", "Matching files") },
                    L.Text("Beklenen SHA-256 (isteğe bağlı)", "Expected SHA-256 (optional)")))
                {
                    if (pick.ShowDialog(this) != DialogResult.OK) return;
                    Job job;
                    string target;
                    if (pick.Selection == 0)
                    {
                        using (var dialog = new OpenFileDialog { CheckFileExists = true, DereferenceLinks = false,
                            Title = L.Text("SHA-256 için dosya seçin", "Choose a file for SHA-256") })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            target = dialog.FileName;
                        }
                        job = FileTools.CreateHash(target, pick.Input);
                    }
                    else
                    {
                        using (var dialog = new FolderBrowserDialog { ShowNewFolderButton = false,
                            Description = L.Text("Taranacak yerel klasörü seçin.", "Choose the local folder to scan.") })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            target = dialog.SelectedPath;
                        }
                        job = FileTools.CreateScan(target, pick.Selection == 2);
                    }
                    Review(job.Title, L.Text("Salt okunur inceleme. Sonuçlar günlükte listelenir.",
                        "Read-only inspection. Results are listed in the log."), new[] { job }, new[] { job.Title + " · " + target });
                }
            });
        }
    }
}

