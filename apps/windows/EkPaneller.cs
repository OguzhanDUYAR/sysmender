using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.ServiceProcess;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal sealed partial class MainWindow
    {
        private DataGridView bulkGrid, startupGrid, serviceGrid;
        private TextBox bulkSearch;
        private Label bulkInfo, startupInfo, serviceInfo;
        private string startupError;
        private Button bulkScan, bulkRemove, bulkClear, startupScan, startupDisable, startupRestore, servicesOpen;
        private Button serviceScan, serviceDisable, serviceRestore, serviceStart, serviceStop;
        private bool updatingBulk, bulkChanged, startupLoaded, startupChanged, servicesChanged;
        private List<InstalledApp> wingetInstalled = new List<InstalledApp>();
        private List<InstalledApp> storeInstalled = new List<InstalledApp>();
        private List<ServiceEntry> serviceEntries = new List<ServiceEntry>();
        private List<StartupItem> startupEntries = new List<StartupItem>();
        private readonly HashSet<string> bulkSelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Control BulkPage()
        {
            var panel = Style.Stack(44, 0, 64, 43);
            var tools = Style.Row();
            bulkScan = Style.Button(L.Text("Kurulu uygulamaları bul", "Find installed apps"), ReviewBulkScan);
            tools.Controls.Add(bulkScan);
            var searchLabel = Style.Text(L.Translate("Ara"), Style.Small, Style.Muted);
            searchLabel.Dock = DockStyle.None;
            searchLabel.AutoSize = true;
            searchLabel.Margin = new Padding(1, 9, 7, 0);
            tools.Controls.Add(searchLabel);
            bulkSearch = new TextBox
            {
                AccessibleName = L.Text("Kurulu uygulama ara", "Search installed apps"),
                Width = 215, Font = Style.Normal, BackColor = Style.Surface, ForeColor = Style.Ink,
                BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 8, 0)
            };
            tools.Controls.Add(bulkSearch);
            bulkClear = Style.Button(L.Text("Seçimi temizle", "Clear selection"), () => { bulkSelected.Clear(); FillBulk(); });
            tools.Controls.Add(bulkClear);
            panel.Controls.Add(tools, 0, 0);

            bulkGrid = Style.Grid(L.Text("Kurulu uygulamalar", "Installed apps"));
            Style.CheckColumn(bulkGrid);
            Style.Column(bulkGrid, L.Text("Uygulama / paket kimliği", "App / package ID"), 73, true);
            Style.Column(bulkGrid, L.Text("Sürüm", "Version"), 27, true);
            Style.Column(bulkGrid, L.Text("Kaynak", "Source"), 150);
            panel.Controls.Add(bulkGrid, 0, 1);
            bulkInfo = Style.Note("");
            panel.Controls.Add(bulkInfo, 0, 2);
            var actions = Style.Row();
            bulkRemove = Style.Button(L.Text("Seçilenleri kaldır", "Uninstall selected"), ReviewBulk, true);
            actions.Controls.Add(bulkRemove);
            panel.Controls.Add(actions, 0, 3);

            bulkSearch.TextChanged += (sender, e) => FillBulk();
            bulkGrid.CellValueChanged += (sender, e) =>
            {
                if (updatingBulk || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var row = bulkGrid.Rows[e.RowIndex];
                var item = row.Tag as InstalledApp;
                if (item == null) return;
                if (Equals(row.Cells[0].Value, true)) bulkSelected.Add(item.Key);
                else bulkSelected.Remove(item.Key);
                RefreshActions();
            };
            return panel;
        }

        private IEnumerable<InstalledApp> BulkItems()
        {
            return storeInstalled.Concat(wingetInstalled).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        private void FillBulk()
        {
            updatingBulk = true;
            try
            {
                bulkGrid.Rows.Clear();
                string query = bulkSearch.Text.Trim();
                foreach (var item in BulkItems().Where(app => app.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    app.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    int index = bulkGrid.Rows.Add(bulkSelected.Contains(item.Key), item.Name, item.Version, item.Source);
                    bulkGrid.Rows[index].Tag = item;
                    bulkGrid.Rows[index].Cells[1].ToolTipText = item.Id;
                }
            }
            finally { updatingBulk = false; }
            RefreshActions();
        }

        private void ReviewBulkScan()
        {
            if (Busy) return;
            Job[] jobs =
            {
                InstalledApps.ScanStore(items => storeInstalled = items),
                InstalledApps.ScanWinGet(packages, items => wingetInstalled = items)
            };
            using (var dialog = new PlanWindow(L.Text("Kurulu uygulamalar", "Installed apps"),
                L.Text("Tarama kaldırılabilir Store uygulamalarını ve WinGet topluluk kaynağındaki eşleşmeleri arar. Devam etmek WinGet kaynak koşullarını kabul eder.",
                    "The scan looks for removable Store apps and matches in the WinGet community source. Continuing accepts the WinGet source terms."),
                jobs.Select(job => job.Title).ToArray()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }
            wingetInstalled.Clear();
            storeInstalled.Clear();
            bulkSelected.Clear();
            bulkChanged = true;
            FillBulk();
            Start(jobs, false);
        }

        private void ReviewBulk()
        {
            var selected = BulkItems().Where(item => bulkSelected.Contains(item.Key)).ToArray();
            if (selected.Length == 0) return;
            Review(L.Text("Toplu kaldırma planı", "Bulk uninstall plan"),
                L.Text("Seçilen paketler sırayla kaldırılır. Yerel uygulama verileri silinebilir. WinGet kaldırıcısı izin veya onay isteyebilir.",
                    "Selected packages are removed one at a time. Local app data may be deleted. WinGet uninstallers may request permission or confirmation."),
                selected.Select(item => InstalledApps.Remove(item, key => removed.Add(key))).ToArray(),
                selected.Select(item => item.Name + " · " + item.Id +
                    (string.IsNullOrEmpty(item.Version) ? "" : " · " + item.Version) + " · " + item.Source).ToArray());
        }

        private Control StartupPage()
        {
            var panel = Style.Stack(44, 0, 72, 45);
            var tools = Style.Row();
            startupScan = Style.Button(L.Text("Listeyi yenile", "Refresh list"), ReadStartup);
            tools.Controls.Add(startupScan);
            ToolButton(tools, L.Text("Windows başlangıç ayarları", "Windows startup settings"), "ms-settings:startupapps");
            servicesOpen = Style.Button(L.Text("Servisleri aç", "Open services"), () => Safe(() => Program.Elevate("services")));
            tools.Controls.Add(servicesOpen);
            panel.Controls.Add(tools, 0, 0);
            startupGrid = Style.Grid(L.Text("Kullanıcı başlangıç kayıtları", "User startup entries"));
            startupGrid.ReadOnly = true;
            Style.Column(startupGrid, L.Text("Kayıt", "Entry"), 50, true);
            Style.Column(startupGrid, L.Text("Konum", "Location"), 22, true);
            Style.Column(startupGrid, L.Text("Durum", "Status"), 28, true);
            panel.Controls.Add(startupGrid, 0, 1);
            startupInfo = Style.Note("");
            panel.Controls.Add(startupInfo, 0, 2);
            var actions = Style.Row();
            startupDisable = Style.Button(L.Text("Başlangıçtan çıkar", "Disable at sign-in"), () => ReviewStartup(true));
            startupRestore = Style.Button(L.Text("Önceki kaydı geri yükle", "Restore previous entry"), () => ReviewStartup(false));
            actions.Controls.Add(startupDisable);
            actions.Controls.Add(startupRestore);
            panel.Controls.Add(actions, 0, 3);
            startupGrid.SelectionChanged += (sender, e) => RefreshActions();
            return panel;
        }

        private void ReadStartup()
        {
            if (Busy) return;
            startupLoaded = true;
            Start(new[] { new Job("startup-list", L.Text("Başlangıç kayıtlarını oku", "Read startup entries"), LoadStartup) }, false);
        }

        private Result LoadStartup(Action<string> log)
        {
            startupChanged = true;
            startupError = null;
            startupEntries = new List<StartupItem>();
            try
            {
                startupEntries = Startup.Read(message =>
                {
                    log(message);
                    startupError = startupError == null ? message : startupError + " | " + message;
                });
            }
            catch (Exception ex) { startupError = ex.Message; return new Result(ResultKind.Failed, ex.Message); }
            return new Result(startupError == null ? ResultKind.Done : ResultKind.Review,
                string.Format(L.Text("{0} başlangıç kaydı bulundu.", "{0} startup entries found."), startupEntries.Count));
        }

        private void FillStartup()
        {
            startupGrid.Rows.Clear();
            foreach (var item in startupEntries)
            {
                int index = startupGrid.Rows.Add(item.Name, item.Source, item.Status);
                startupGrid.Rows[index].Tag = item;
                startupGrid.Rows[index].Cells[0].ToolTipText = item.Value;
            }
        }

        private void ReviewStartup(bool disable)
        {
            var item = Current<StartupItem>(startupGrid);
            if (item == null || Busy) return;
            Job job = Startup.Change(item, disable);
            Review(L.Text("Başlangıç uygulaması", "Startup app"),
                L.Text("İşlem bir sonraki oturum açılışını etkiler. Açık uygulamalar çalışmaya devam eder. Windows başlangıç tercihleri ayrıca uygulanabilir.",
                    "This affects the next sign-in. Running apps keep running. Windows may also apply its own startup preferences."),
                new[] { new Job(job.Id, job.Title, log =>
                {
                    try { return job.Run(log); }
                    finally { LoadStartup(log); }
                }) });
        }

        private Control ServicesPage()
        {
            var panel = Style.Stack(44, 0, 72, 46);
            var tools = Style.Row();
            serviceScan = Style.Button(L.Text("Servisleri listele", "List services"), ScanServices);
            tools.Controls.Add(serviceScan);
            panel.Controls.Add(tools, 0, 0);
            serviceGrid = Style.Grid(L.Text("İsteğe bağlı Windows servisleri", "Optional Windows services"));
            serviceGrid.ReadOnly = true;
            Style.Column(serviceGrid, L.Text("Servis", "Service"), 38, true);
            Style.Column(serviceGrid, L.Text("Durum", "Status"), 23, true);
            Style.Column(serviceGrid, L.Text("Başlangıç modu", "Startup mode"), 39, true);
            panel.Controls.Add(serviceGrid, 0, 1);
            serviceInfo = Style.Note("");
            panel.Controls.Add(serviceInfo, 0, 2);
            var actions = Style.Row();
            serviceDisable = Style.Button(L.Text("Başlangıcı kapat", "Disable startup"), () => ReviewService("disable"));
            serviceRestore = Style.Button(L.Text("Başlangıcı geri yükle", "Restore startup"), () => ReviewService("restore"));
            serviceStart = Style.Button(L.Text("Başlat", "Start"), () => ReviewService("start"));
            serviceStop = Style.Button(L.Text("Durdur", "Stop"), () => ReviewService("stop"));
            actions.Controls.Add(serviceDisable); actions.Controls.Add(serviceRestore);
            actions.Controls.Add(serviceStart); actions.Controls.Add(serviceStop);
            panel.Controls.Add(actions, 0, 3);
            serviceGrid.SelectionChanged += (sender, e) => RefreshActions();
            return panel;
        }

        private void ScanServices()
        {
            Start(new[] { new Job("service-list", L.Text("İsteğe bağlı servisleri oku", "Read optional services"), log =>
            {
                servicesChanged = true;
                int warnings = 0;
                serviceEntries = OptionalServices.Read(message => { warnings++; log(message); });
                return new Result(warnings == 0 ? ResultKind.Done : ResultKind.Review, string.Format(
                    L.Text("{0} servis listelendi. {1} uyarı.", "{0} services listed. {1} warnings."), serviceEntries.Count, warnings));
            }) }, false);
        }

        private void FillServices()
        {
            string current = Current<ServiceEntry>(serviceGrid) == null ? null : Current<ServiceEntry>(serviceGrid).Option.Name;
            serviceGrid.Rows.Clear();
            foreach (var item in serviceEntries)
            {
                int index = serviceGrid.Rows.Add(item.Option.Name, OptionalServices.StatusLabel(item.Status),
                    OptionalServices.ModeLabel(item.Mode) + (item.Backup == null ? "" : L.Text(" · yedekli", " · backed up")));
                serviceGrid.Rows[index].Tag = item;
                serviceGrid.Rows[index].Cells[0].ToolTipText = item.Option.Detail;
                if (item.Option.Name == current) serviceGrid.CurrentCell = serviceGrid.Rows[index].Cells[0];
            }
        }

        private void ReviewService(string operation)
        {
            var item = Current<ServiceEntry>(serviceGrid);
            if (item == null || Busy) return;
            Job job = OptionalServices.Create(item, operation);
            string note = item.Option.Detail + " " + (operation == "disable" ?
                L.Text("Windows bu servisi başlatamayacak. Servis şu anda çalışıyorsa çalışmaya devam eder.",
                    "Windows will be unable to start this service. If it is currently running, it will keep running.") :
                operation == "stop" ? L.Text("Servisi kullanan özellikler bu oturumda kesilebilir.", "Features using the service may be interrupted.") :
                L.Text("Windows servis durumunu ve bağımlılıklarını denetler.", "Windows checks the service state and its dependencies."));
            Review(L.Text("Servis işlemi", "Service action"), note, new[]
            {
                new Job(job.Id, job.Title, log =>
                {
                    try { return job.Run(log); }
                    finally
                    {
                        servicesChanged = true;
                        try { serviceEntries = OptionalServices.Read(log); }
                        catch (Exception ex) { serviceEntries.Clear(); log(ex.Message); }
                    }
                })
            });
        }

        private void RefreshExtraActions(bool idle)
        {
            if (Program.Admin)
            {
                var service = Current<ServiceEntry>(serviceGrid);
                serviceScan.Enabled = idle;
                serviceDisable.Enabled = idle && service != null && OptionalServices.IsRestorable(service.Mode) &&
                    (service.Backup == null || service.Backup == service.Mode);
                serviceRestore.Enabled = idle && service != null && service.Mode == "disabled" && service.Backup != null;
                serviceStart.Enabled = idle && service != null && service.Mode != "disabled" && service.Status == ServiceControllerStatus.Stopped;
                serviceStop.Enabled = idle && service != null && service.CanStop && service.Status == ServiceControllerStatus.Running;
                serviceInfo.Text = service == null ?
                    L.Text("Bu bilgisayarda bulunan isteğe bağlı servisleri görmek için listeyi açın.", "List the optional services available on this computer.") :
                    service.Option.Detail + (service.Backup == null ? "" : " " + L.Text("Önceki mod: ", "Previous mode: ") +
                        OptionalServices.ModeLabel(service.Backup));
                return;
            }

            bulkScan.Enabled = idle;
            bulkGrid.Enabled = bulkSearch.Enabled = idle;
            bulkRemove.Enabled = bulkClear.Enabled = idle && bulkSelected.Count > 0;
            bulkInfo.Text = string.Format(L.Text("{0} / {1} gösteriliyor · {2} seçili. WinGet eşleşmeleri ve kaldırılabilir Store paketleri.",
                "Showing {0} / {1} · {2} selected. WinGet matches and removable Store packages."),
                bulkGrid.Rows.Count, wingetInstalled.Count + storeInstalled.Count, bulkSelected.Count);

            var startup = Current<StartupItem>(startupGrid);
            startupScan.Enabled = servicesOpen.Enabled = idle;
            startupDisable.Enabled = idle && startup != null && startup.Present && !startup.Conflict;
            startupRestore.Enabled = idle && startup != null && startup.Saved && !startup.Conflict;
            startupInfo.Text = startup == null ?
                (startupError ?? L.Text("Kullanıcıya ait Run kayıtları ve Başlangıç klasöründeki kısayollar gösterilir.",
                    "Shows this user's Run entries and shortcuts in the Startup folder.")) :
                startup.Conflict ? L.Text("Aynı adla iki farklı kayıt var. Çakışma giderilene kadar değişiklik yapılamaz.",
                    "Two different entries share this name. Resolve the conflict before making changes.") : startup.Value;
        }

        private void RefreshExtraResults()
        {
            if (Program.Admin)
            {
                if (servicesChanged) { servicesChanged = false; FillServices(); }
                return;
            }
            if (startupChanged) { startupChanged = false; FillStartup(); }
            if (removed.Count > 0)
            {
                wingetInstalled.RemoveAll(app => removed.Contains(app.Key));
                storeInstalled.RemoveAll(app => removed.Contains(app.Key));
                bulkSelected.ExceptWith(removed);
                removed.Clear();
                bulkChanged = true;
            }
            if (bulkChanged) { bulkChanged = false; FillBulk(); }
        }
    }
}
