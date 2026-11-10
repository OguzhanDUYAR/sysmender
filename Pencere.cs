using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal sealed partial class MainWindow : Form
    {
        private readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Dictionary<string, string[]> copy = new Dictionary<string, string[]>();
        private readonly HashSet<string> selectedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Package> packages = Packages.Load();
        private readonly TempCleanup temp = new TempCleanup(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
        private readonly Settings settings = new Settings(Path.Combine(Program.Data, "AyarYedekleri"));
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 160 };
        private Panel stage;
        private Label title, subtitle, status, elapsed, taskDetail, appDetail, settingDetail;
        private ProgressBar progress;
        private Button stop, runMaintenance, elevate, clearTasks, clearApps, install, upgrade, uninstall;
        private Button listApps, listUpdates, version, enable, disable, undo, refreshSettings, openLog;
        private readonly List<Button> toolShortcuts = new List<Button>();
        private DataGridView taskGrid, appGrid, settingGrid, resultsGrid;
        private TextBox search;
        private ComboBox profiles, drives, categories;
        private RichTextBox console;
        private RunState active;
        private string lastLog;
        private bool built, loadingApps, updatingProfiles, settingsRefreshPending, closeWhenIdle;

        internal bool ReopenRequested { get; private set; }
        internal string CurrentPage { get; private set; }
        private Button language;

        internal MainWindow(string initialTask, string page = null)
        {
            Text = Program.Name + (Program.Admin ? L.Translate(" · Yönetici araçları") : "");
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(920, 620);
            ClientSize = new Size(1120, 720);
            BackColor = Style.Canvas;
            ForeColor = Style.Ink;
            Font = Style.Normal;
            SuspendLayout();

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);

            var main = Style.Stack(84, 0, 59);
            main.Padding = new Padding(22, 12, 22, 6);
            shell.Controls.Add(main, 1, 0);
            var heading = Style.Stack(45, 32);
            title = Style.Text("", Style.Heading);
            subtitle = Style.Text("", Style.Normal, Style.Muted);
            heading.Controls.Add(title, 0, 0);
            heading.Controls.Add(subtitle, 0, 1);
            main.Controls.Add(heading, 0, 0);
            stage = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            main.Controls.Add(stage, 0, 1);
            main.Controls.Add(Footer(), 0, 2);

            AddPage("monitor", L.Text("Canlı izleme", "Live monitor"),
                L.Text("İşlemci, bellek, disk boş alanı ve ağ trafiğini izleyin.", "Monitor CPU, memory, disk free space and network traffic."), MonitorPage());
            AddPage("it", L.Text("BT araçları", "IT tools"),
                L.Text("Yerel bilgisayar için tanılama, bakım ve destek raporları.",
                    "Diagnostics, maintenance and support reports for the local PC."), ItPage());
            AddPage("maintenance", Program.Admin ? L.Translate("Yönetici bakımı") : L.Translate("Bakım planı"),
                Program.Admin ? L.Translate("Windows ve disk işlemlerini seçin. Plan, incelemenizden sonra başlar.") :
                L.Translate("Tanılama, Store ve Xbox, geçici dosyalar ve Windows onarımı."), MaintenancePage(initialTask));
            if (!Program.Admin)
            {
                AddPage("apps", L.Translate("Uygulamalar"), L.Translate("WinGet kataloğunda arayın ve işlemleri paket bazında seçin."), AppsPage());
                AddPage("bulk", L.Text("Toplu kaldırma", "Bulk uninstall"),
                    L.Text("Bu bilgisayardaki uygulamalardan kaldırılacakları tek tek seçin.", "Choose installed apps to uninstall in one batch."), BulkPage());
                AddPage("startup", L.Text("Başlangıç uygulamaları", "Startup apps"),
                    L.Text("Bu hesabın oturum açma kayıtlarını yedekleyin ve geri yükleyin.", "Back up and restore sign-in entries for this account."), StartupPage());
                AddPage("settings", L.Translate("Kullanıcı ayarları"), L.Translate("Dosya Gezgini, tema ve görev çubuğu tercihleri."), SettingsPage());
            }
            else AddPage("services", L.Text("İsteğe bağlı servisler", "Optional services"),
                L.Text("Seçili Windows servislerini yönetin ve önceki başlangıç modunu geri yükleyin.",
                    "Manage selected Windows services and restore their previous startup modes."), ServicesPage());
            AddPage("tools", L.Translate("Windows araçları"), L.Translate("Yerleşik Windows ekranlarına ve bakım kısayollarına ulaşın."), ToolsPage());
            AddPage("logs", L.Translate("Sonuçlar ve günlük"), L.Translate("Her adımın durumunu ve Windows araçlarının çıktısını burada izleyin."), LogsPage());
            shell.Controls.Add(Sidebar(), 0, 0);

            timer.Tick += (sender, e) => Tick();
            FormClosing += OnClosing;
            built = true;
            if (!Program.Admin) FillSettings();
            SelectPage(page != null && pages.ContainsKey(page) ? page : "maintenance");
            RefreshActions();
            ResumeLayout(true);
        }

        private bool Busy { get { return active != null; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                monitorTimer.Dispose();
                monitorGeneration++;
            }
            base.Dispose(disposing);
        }

        private void AddPage(string id, string caption, string description, Control page)
        {
            copy.Add(id, new[] { caption, description });
            pages.Add(id, page);
            page.Visible = false;
            page.Dock = DockStyle.Fill;
            stage.Controls.Add(page);
        }

        private void SelectPage(string id)
        {
            CurrentPage = id;
            RefreshMonitorState();
            foreach (var item in pages) item.Value.Visible = item.Key == id;
            pages[id].BringToFront();
            title.Text = copy[id][0];
            subtitle.Text = copy[id][1];
            foreach (var item in navigation)
            {
                item.Value.BackColor = item.Key == id ? Style.Selected : Style.Rail;
                item.Value.ForeColor = item.Key == id ? Style.SelectedInk : Style.Ink;
                item.Value.FlatAppearance.BorderColor = item.Key == id ? Style.Accent : Style.Rail;
            }
            if (id == "startup" && !startupLoaded && !Busy) ReadStartup();
        }

        private Control Sidebar()
        {
            string[] ids = Program.Admin ? new[] { "monitor", "it", "maintenance", "services", "tools", "logs" } :
                new[] { "monitor", "it", "maintenance", "apps", "bulk", "startup", "settings", "tools", "logs" };
            int[] heights = new[] { 100, 34 }.Concat(ids.Select(id => 34)).Concat(new[] { 0, 38, 72 }).ToArray();
            var layout = Style.Stack(heights);
            layout.BackColor = Style.Rail;
            layout.Padding = new Padding(13, 16, 13, 9);
            var brand = Style.Stack(45, 24, 22);
            var logo = new PictureBox
            {
                Image = Icon.ToBitmap(), Dock = DockStyle.Left, Width = 46,
                SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(3, 0, 0, 4), TabStop = false
            };
            logo.Disposed += (sender, e) => logo.Image.Dispose();
            brand.Controls.Add(logo, 0, 0);
            brand.Controls.Add(Style.Text(L.Translate("WINDOWS BAKIM"), Style.Section), 0, 1);
            brand.Controls.Add(Style.Text(L.Translate("M E R K E Z İ"), Style.Small, Style.Muted), 0, 2);
            layout.Controls.Add(brand, 0, 0);
            workspaceSearch = Style.Button(L.Text("Ara · Ctrl+K", "Search · Ctrl+K"), ShowWorkspaceSearch);
            workspaceSearch.Dock = DockStyle.Fill;
            workspaceSearch.MinimumSize = new Size(68, 28);
            workspaceSearch.Margin = new Padding(0, 0, 0, 4);
            layout.Controls.Add(workspaceSearch, 0, 1);
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var button = Style.Button(copy[id][0], () => SelectPage(id));
                button.Dock = DockStyle.Fill;
                button.AutoSize = false;
                button.Margin = new Padding(0, 2, 0, 5);
                button.Padding = new Padding(8, 0, 0, 0);
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Font = Style.Normal;
                navigation.Add(id, button);
                layout.Controls.Add(button, 0, i + 2);
            }
            var mode = Style.Stack(24, 22, 22);
            mode.Controls.Add(Style.Text(Program.Admin ? L.Translate("YÖNETİCİ OTURUMU") : L.Translate("KULLANICI OTURUMU"), Style.Small,
                Program.Admin ? Style.Amber : Style.Accent), 0, 0);
            mode.Controls.Add(Style.Text("v" + Program.Version + L.Text("  ·  Geliştiren", "  ·  Developed by"), Style.Small, Style.Muted), 0, 1);
            mode.Controls.Add(Style.Text("Oguzhan DUYAR", Style.Small, Style.Muted), 0, 2);
            language = Style.Button(L.Text("Language · English", "Dil · Türkçe"), ChangeLanguage);
            language.Dock = DockStyle.Fill;
            language.Margin = new Padding(0, 0, 0, 5);
            layout.Controls.Add(language, 0, heights.Length - 2);
            layout.Controls.Add(mode, 0, heights.Length - 1);
            return layout;
        }

        private Control Footer()
        {
            var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 133));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status = Style.Text(L.Translate("Hazır"), Style.Small);
            status.AccessibleName = L.Translate("İşlem durumu");
            status.Padding = new Padding(0, 0, 7, 0);
            progress = new ProgressBar
            {
                AccessibleName = L.Translate("Plan ilerlemesi"), Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 12, Margin = new Padding(5, 0, 5, 0), Maximum = 1
            };
            elapsed = Style.Text("", Style.Small, Style.Muted);
            elapsed.TextAlign = ContentAlignment.MiddleCenter;
            stop = Style.Button(L.Translate("Sırayı durdur"), () => { if (Busy) active.Stop(); });
            stop.Dock = DockStyle.Fill;
            stop.AutoSize = false;
            stop.Margin = new Padding(1, 6, 0, 7);
            table.Controls.Add(status, 0, 0);
            table.Controls.Add(progress, 1, 0);
            table.Controls.Add(elapsed, 2, 0);
            table.Controls.Add(stop, 3, 0);
            outer.Controls.Add(table);
            return outer;
        }

        private Control MaintenancePage(string initialTask)
        {
            var layout = Style.Stack(43, 0, 60, 42);
            var tools = Style.Row();
            profiles = Style.Combo(L.Translate("Bakım profili"), 213);
            profiles.Items.AddRange(Program.Admin ?
                new object[] { L.Translate("Özel seçim"), L.Translate("Windows ve disk tanılama"), L.Translate("Bileşen onarımı") } :
                new object[] { L.Translate("Özel seçim"), L.Translate("Hızlı tanılama"), L.Translate("Store ve Xbox tanılaması") });
            profiles.SelectedIndex = 0;
            tools.Controls.Add(profiles);
            clearTasks = Style.Button(L.Translate("Seçimi temizle"), () => ApplyProfile(new string[0]));
            tools.Controls.Add(clearTasks);
            if (Program.Admin)
            {
                drives = Style.Combo(L.Translate("NTFS sürücüsü"), 85);
                drives.Items.AddRange(Maintenance.Drives().Cast<object>().ToArray());
                int system = drives.Items.IndexOf(Path.GetPathRoot(Environment.SystemDirectory));
                if (drives.Items.Count > 0) drives.SelectedIndex = Math.Max(0, system);
                drives.SelectedIndexChanged += (sender, e) => RefreshActions();
                tools.Controls.Add(drives);
            }
            else
            {
                elevate = Style.Button(L.Translate("Yönetici araçlarını aç"), () =>
                {
                    var task = Current<MaintenanceTask>(taskGrid);
                    Safe(() => Program.Elevate(task != null && task.Admin ? task.Id : null));
                });
                tools.Controls.Add(elevate);
            }
            layout.Controls.Add(tools, 0, 0);

            taskGrid = Style.Grid(L.Translate("Bakım adımları"));
            Style.CheckColumn(taskGrid);
            Style.Column(taskGrid, L.Translate("Alan"), 126);
            Style.Column(taskGrid, L.Translate("İşlem"), 66, true);
            Style.Column(taskGrid, L.Translate("Etki / izin"), 34, true);
            foreach (var task in Maintenance.Tasks.Where(item => !Program.Admin || item.Admin))
            {
                int index = taskGrid.Rows.Add(task.Id == initialTask, task.Group, task.Title,
                    !Program.Admin && task.Admin ? L.Translate("Yönetici gerekiyor") : task.Impact);
                var row = taskGrid.Rows[index];
                row.Tag = task;
                row.Cells[2].ToolTipText = task.Detail;
                if (!Program.Admin && task.Admin)
                {
                    row.Cells[0] = new DataGridViewTextBoxCell();
                    row.Cells[0].Value = "—";
                    row.Cells[0].ReadOnly = true;
                    row.Cells[0].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    row.DefaultCellStyle.ForeColor = Style.Muted;
                }
            }
            layout.Controls.Add(taskGrid, 0, 1);
            taskDetail = Style.Note(L.Translate("Bir adıma tıklayarak açıklamasını görün."));
            layout.Controls.Add(taskDetail, 0, 2);

            var actions = Style.Row();
            runMaintenance = Style.Button(L.Translate("Planı incele"), ReviewMaintenance, true);
            actions.Controls.Add(runMaintenance);
            if (Program.Admin) ToolButton(actions, L.Translate("Sistem Koruması"), Commands.SystemTool("SystemPropertiesProtection.exe"));
            else ToolButton(actions, "Windows Update", "ms-settings:windowsupdate");
            layout.Controls.Add(actions, 0, 3);

            taskGrid.SelectionChanged += (sender, e) => RefreshActions();
            taskGrid.CellValueChanged += (sender, e) =>
            {
                if (!built || e.ColumnIndex != 0) return;
                if (!updatingProfiles) profiles.SelectedIndex = 0;
                RefreshActions();
            };
            profiles.SelectedIndexChanged += (sender, e) =>
            {
                if (profiles.SelectedIndex == 1)
                    ApplyProfile(Program.Admin ? new[] { "physical", "dism-check" } :
                        new[] { "inventory", "events", "temp-preview" }, false);
                if (profiles.SelectedIndex == 2)
                    ApplyProfile(Program.Admin ? new[] { "dism-repair", "sfc" } :
                        new[] { "network", "store-check" }, false);
            };
            if (initialTask != null)
            {
                var match = taskGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => ((MaintenanceTask)row.Tag).Id == initialTask);
                if (match != null) taskGrid.CurrentCell = match.Cells[2];
            }
            return layout;
        }

        private void ApplyProfile(string[] ids, bool reset = true)
        {
            updatingProfiles = true;
            try
            {
                if (reset) profiles.SelectedIndex = 0;
                foreach (DataGridViewRow row in taskGrid.Rows)
                    if (!row.Cells[0].ReadOnly) row.Cells[0].Value = ids.Contains(((MaintenanceTask)row.Tag).Id);
            }
            finally { updatingProfiles = false; }
            RefreshActions();
        }

        private MaintenanceTask[] SelectedTasks()
        {
            return taskGrid.Rows.Cast<DataGridViewRow>().Where(row => !row.Cells[0].ReadOnly && Equals(row.Cells[0].Value, true))
                .Select(row => (MaintenanceTask)row.Tag).ToArray();
        }

        private void ReviewMaintenance()
        {
            var tasks = SelectedTasks();
            if (tasks.Length == 0) return;
            if (tasks.Any(task => task.Id == "temp-clean") &&
                (!temp.HasPreview || tasks.Any(task => task.Id == "temp-preview")))
            {
                Inform(L.Translate("Önce geçici dosya önizlemesini ayrı bir plan olarak çalıştırın. Ardından yalnızca temizleme adımını seçebilirsiniz."));
                return;
            }
            string drive = drives == null ? null : drives.SelectedItem as string;
            if (tasks.Any(task => task.NeedsDrive) && drive == null)
            {
                Inform(L.Translate("Disk taraması için yerel bir NTFS sürücüsü seçin."));
                return;
            }
            string note = tasks.Any(task => task.Changes) ?
                L.Translate("Bu plan değişiklik yapar. Etkiyi her satırda inceleyin. Çalışan onarım güvenli bir bitiş noktasına kadar devam eder.") :
                L.Translate("Tanılama adımları sırayla çalışır. Ağ erişimi gereken adımlar aşağıda belirtilir.");
            if (tasks.Any(task => task.Id == "temp-clean"))
                note += L.Translate(" Geçici dosya silme işlemi kalıcıdır.");
            Review(L.Translate("Bakım planı"), note, tasks.Select(task => Maintenance.Create(task, drive, temp)).ToArray(),
                tasks.Select(task => task.Title + "  ·  " + task.Impact + (task.NeedsDrive ? "  ·  " + drive : "")).ToArray());
        }

        private Control AppsPage()
        {
            var layout = Style.Stack(43, 0, 50, 43, 43);
            var filter = Style.Row();
            search = new TextBox
            {
                Width = 260, Font = Style.Normal, AccessibleName = L.Translate("Uygulama adı veya paket kimliği"),
                BackColor = Style.Surface, ForeColor = Style.Ink, BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 8, 0)
            };
            categories = Style.Combo(L.Translate("Uygulama kategorisi"), 163);
            categories.Items.Add(L.Translate("Tüm kategoriler"));
            categories.Items.AddRange(packages.Select(package => package.Category).Distinct().Cast<object>().ToArray());
            categories.SelectedIndex = 0;
            var searchLabel = Style.Text(L.Translate("Ara"), Style.Small, Style.Muted);
            searchLabel.Dock = DockStyle.None;
            searchLabel.AutoSize = true;
            searchLabel.Margin = new Padding(0, 9, 7, 0);
            filter.Controls.Add(searchLabel);
            filter.Controls.Add(search);
            filter.Controls.Add(categories);
            clearApps = Style.Button(L.Translate("Seçimi temizle"), () => { selectedApps.Clear(); FillApps(); });
            filter.Controls.Add(clearApps);
            layout.Controls.Add(filter, 0, 0);
            appGrid = Style.Grid(L.Translate("WinGet uygulama kataloğu"));
            Style.CheckColumn(appGrid);
            Style.Column(appGrid, L.Translate("Uygulama"), 51, true);
            Style.Column(appGrid, L.Translate("Kategori"), 135);
            Style.Column(appGrid, L.Translate("WinGet kimliği"), 49, true);
            layout.Controls.Add(appGrid, 0, 1);
            appDetail = Style.Note("");
            layout.Controls.Add(appDetail, 0, 2);

            var batch = Style.Row();
            install = Style.Button(L.Translate("Seçileni kur"), () => ReviewPackages("install"), true);
            upgrade = Style.Button(L.Translate("Seçileni güncelle"), () => ReviewPackages("upgrade"));
            uninstall = Style.Button(L.Text("Toplu kaldırmayı aç", "Open bulk uninstall"), () => SelectPage("bulk"));
            batch.Controls.Add(install); batch.Controls.Add(upgrade); batch.Controls.Add(uninstall);
            layout.Controls.Add(batch, 0, 3);

            var inspect = Style.Row();
            listApps = Style.Button(L.Translate("Kurulu uygulamaları listele"), () => ReviewPackages("list"));
            listUpdates = Style.Button(L.Translate("Güncellemeleri listele"), () => ReviewPackages("updates"));
            version = Style.Button(L.Translate("WinGet'i denetle"), () =>
            {
                RefreshActions();
                if (File.Exists(Packages.WinGet)) Start(new[] { Packages.Create("version") });
                else Inform(L.Translate("Bu oturumda WinGet bulunamadı. Windows araçları bölümünden Uygulama Yükleyicisi'ni açın."));
            });
            inspect.Controls.Add(listApps); inspect.Controls.Add(listUpdates); inspect.Controls.Add(version);
            layout.Controls.Add(inspect, 0, 4);

            search.TextChanged += (sender, e) => FillApps();
            categories.SelectedIndexChanged += (sender, e) => FillApps();
            appGrid.CellValueChanged += (sender, e) =>
            {
                if (loadingApps || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var row = appGrid.Rows[e.RowIndex];
                var package = row.Tag as Package;
                if (package == null) return;
                if (Equals(row.Cells[0].Value, true)) selectedApps.Add(package.Id);
                else selectedApps.Remove(package.Id);
                RefreshActions();
            };
            FillApps();
            return layout;
        }

        private void FillApps()
        {
            loadingApps = true;
            try
            {
                appGrid.Rows.Clear();
                string query = search.Text.Trim();
                string category = categories.SelectedIndex > 0 ? categories.SelectedItem as string : null;
                foreach (var package in packages)
                {
                    if (category != null && category != package.Category) continue;
                    if (package.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        package.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int i = appGrid.Rows.Add(selectedApps.Contains(package.Id), package.Name, package.Category, package.Id);
                    appGrid.Rows[i].Tag = package;
                }
            }
            finally { loadingApps = false; }
            RefreshActions();
        }

        private void ReviewPackages(string operation)
        {
            if (operation == "updates" || operation == "list")
            {
                Review(L.Translate("WinGet sorgusu"), L.Translate("WinGet topluluk kaynağının koşullarını kabul ederek sorgulamayı başlatın."),
                    new[] { Packages.Create(operation) });
                return;
            }
            string note = L.Translate("Devam ederek WinGet kaynağının ve seçili paketlerin koşullarını kabul edersiniz. Kurucular pencere veya Windows izin isteği açabilir.");
            Review(L.Translate("Uygulama planı"), note, packages.Where(package => selectedApps.Contains(package.Id))
                .Select(package => Packages.Create(operation, package)).ToArray());
        }

        private Control SettingsPage()
        {
            var layout = Style.Stack(47, 0, 62, 43);
            layout.Controls.Add(Style.Note(L.Translate("İlk değişiklikten önceki değer saklanır ve geri alma sırasında kullanılır.")), 0, 0);
            settingGrid = Style.Grid(L.Translate("Geri alınabilir kullanıcı ayarları"));
            settingGrid.ReadOnly = true;
            Style.Column(settingGrid, L.Translate("Ayar"), 59, true);
            Style.Column(settingGrid, L.Translate("Geçerli durum"), 25, true);
            Style.Column(settingGrid, L.Translate("Yedek"), 16, true);
            foreach (var setting in Settings.Options)
            {
                int row = settingGrid.Rows.Add(setting.Title, "", "");
                settingGrid.Rows[row].Tag = setting;
            }
            layout.Controls.Add(settingGrid, 0, 1);
            settingDetail = Style.Note("");
            layout.Controls.Add(settingDetail, 0, 2);
            var actions = Style.Row();
            enable = Style.Button(L.Translate("Aç"), () => ReviewSetting(true), true);
            disable = Style.Button(L.Translate("Kapat"), () => ReviewSetting(false));
            undo = Style.Button(L.Translate("Önceki değere dön"), () => ReviewSetting(null));
            refreshSettings = Style.Button(L.Translate("Durumu yenile"), () => { FillSettings(); RefreshActions(); });
            actions.Controls.Add(enable); actions.Controls.Add(disable); actions.Controls.Add(undo); actions.Controls.Add(refreshSettings);
            layout.Controls.Add(actions, 0, 3);
            settingGrid.SelectionChanged += (sender, e) => RefreshActions();
            return layout;
        }

        private void FillSettings()
        {
            foreach (DataGridViewRow row in settingGrid.Rows)
            {
                var setting = (Setting)row.Tag;
                try { row.Cells[1].Value = settings.State(setting); }
                catch (Exception ex) { row.Cells[1].Value = L.Translate("Okunamadı"); row.Cells[1].ToolTipText = ex.Message; }
                row.Cells[2].Value = settings.HasBackup(setting) ? L.Translate("Var") : "—";
            }
        }

        private void ReviewSetting(bool? enabled)
        {
            var setting = Current<Setting>(settingGrid);
            if (setting == null) return;
            Review(L.Translate("Kullanıcı tercihi"), setting.Detail + L.Translate(" Değişiklik bu Windows hesabına uygulanır."),
                new[] { settings.Create(setting, enabled) });
        }

        private Control ToolsPage()
        {
            var layout = Style.Stack(44, 102, 42, 90, 55, 0);
            layout.Controls.Add(Style.Text(L.Translate("Sistem ayarları"), Style.Section), 0, 0);
            var windows = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            if (Program.Admin)
                layout.Controls.Add(Style.Note(L.Text("Windows Ayarları kısayolları normal kullanıcı penceresinde bulunur.",
                    "Windows Settings shortcuts are available in the standard user window.")), 0, 1);
            else
            {
                ToolButton(windows, "Windows Update", "ms-settings:windowsupdate");
                ToolButton(windows, L.Translate("Depolama"), "ms-settings:storagesense");
                ToolButton(windows, L.Translate("Ağ ve internet"), "ms-settings:network-status");
                ToolButton(windows, L.Translate("Kurulu uygulamalar"), "ms-settings:appsfeatures");
                ToolButton(windows, L.Translate("Kurtarma"), "ms-settings:recovery");
                ToolButton(windows, L.Translate("Windows Güvenliği"), "ms-settings:windowsdefender");
                layout.Controls.Add(windows, 0, 1);
            }
            layout.Controls.Add(Style.Text(L.Translate("Bakım ve destek"), Style.Section), 0, 2);
            var support = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            if (Program.Admin)
                ToolButton(support, L.Translate("Sistem Koruması"), Commands.SystemTool("SystemPropertiesProtection.exe"));
            else
            {
                ToolButton(support, "Microsoft Store", "ms-windows-store:");
                ToolButton(support, L.Translate("Uygulama Yükleyicisi"), "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1");
                ToolButton(support, L.Translate("Çalıştırma diğer adları"), "ms-settings:appsfeatures-appexecutionalias");
            }
            ToolButton(support, L.Translate("Sürücüleri İyileştir"), Commands.SystemTool("dfrgui.exe"));
            support.Controls.Add(Style.Button(L.Translate("Hakkında"), ShowAbout));
            var themeButton = Style.Button(Style.Light ? L.Text("Koyu tema", "Dark theme") :
                L.Text("Açık tema", "Light theme"), ChangeTheme);
            toolShortcuts.Add(themeButton);
            support.Controls.Add(themeButton);
            support.Controls.Add(Style.Button(L.Text("Açık kaynak lisansı", "Open-source license"),
                () => Safe(() => Inform(Program.Resource("WinUtil-MIT.txt")))));
            layout.Controls.Add(support, 0, 3);
            layout.Controls.Add(Style.Note(L.Translate("Windows araçları ayrı pencerelerde açılır. Bakım Merkezi'nin günlüğü, burada açılan araçlardaki işlemleri izlemez.")), 0, 4);
            return layout;
        }

        private void ToolButton(Control parent, string caption, string target)
        {
            var button = Style.Button(caption, () => Safe(() => Program.Open(target)));
            toolShortcuts.Add(button);
            parent.Controls.Add(button);
        }

        private void ShowAbout()
        {
            Inform(Program.Name + " " + Program.Version + "\n\n" + L.Text("Geliştiren Oguzhan DUYAR", "Developed by Oguzhan DUYAR") +
                "\nWindows Bakım 3.2 · Hugo ZED / Oğuzhan D.\nWinUtil · Chris Titus Tech / CT Tech Group LLC\n\n" +
                L.Text("WinUtil'den uyarlanan katalog, tercihler ve yeniden başlatma denetimi MIT lisanslıdır.",
                    "The adapted WinUtil catalog, preferences and restart check are MIT-licensed."));
        }

        private Control LogsPage()
        {
            var layout = Style.Stack(42, 42, 42, 133, 27, 0);
            var actions = Style.Row();
            openLog = Style.Button(Program.Admin ? L.Text("Günlük yolunu kopyala", "Copy log path") : L.Translate("Son günlüğü aç"),
                () => Safe(() =>
                {
                    if (string.IsNullOrEmpty(lastLog)) return;
                    if (Program.Admin) Clipboard.SetText(lastLog);
                    else Program.Open(lastLog);
                }));
            actions.Controls.Add(openLog);
            actions.Controls.Add(Style.Button(Program.Admin ? L.Text("Klasör yolunu kopyala", "Copy folder path") : L.Translate("Günlük klasörü"), () => Safe(() =>
            {
                if (Program.Admin)
                {
                    string folder = string.IsNullOrEmpty(lastLog) ? MachineFiles.FindExisting() : Path.GetDirectoryName(lastLog);
                    if (!string.IsNullOrEmpty(folder)) Clipboard.SetText(folder);
                    else Inform(L.Text("Henüz korumalı bir işlem günlüğü yok.", "There is no protected run log yet."));
                }
                else
                {
                    Directory.CreateDirectory(Program.Logs);
                    Program.Open(Program.Logs);
                }
            })));
            actions.Controls.Add(Style.Button(L.Translate("Görünümü kopyala"), () => Safe(() =>
            {
                string value = console.SelectedText.Length > 0 ? console.SelectedText : console.Text;
                if (value.Length > 0) Clipboard.SetText(value);
            })));
            layout.Controls.Add(actions, 0, 0);
            var reports = Style.Row();
            exportHtml = Style.Button(L.Text("HTML raporu kaydet", "Save HTML report"), () => ExportSupport(false));
            exportJson = Style.Button(L.Text("JSON raporu kaydet", "Save JSON report"), () => ExportSupport(true));
            reports.Controls.Add(exportHtml);
            reports.Controls.Add(exportJson);
            exportSummary = Style.Button(L.Text("Paylaşım özeti", "Shareable summary"), () => ExportSupport(true, true));
            reports.Controls.Add(exportSummary);
            layout.Controls.Add(reports, 0, 1);
            var followUp = Style.Row();
            guideNext = Style.Button(L.Text("Sonraki adım", "Next step"), ShowGuideNext);
            openHistory = Style.Button(L.Text("İşlem geçmişi", "Run history"), ShowHistory);
            followUp.Controls.Add(guideNext);
            followUp.Controls.Add(openHistory);
            if (!Program.Admin)
            {
                compareReports = Style.Button(L.Text("Rapor karşılaştır", "Compare reports"), CompareReports);
                followUp.Controls.Add(compareReports);
            }
            layout.Controls.Add(followUp, 0, 2);
            resultsGrid = Style.Grid(L.Translate("Adım sonuçları"));
            resultsGrid.ReadOnly = true;
            Style.Column(resultsGrid, L.Translate("Adım"), 36, true);
            Style.Column(resultsGrid, L.Translate("Durum"), 140);
            Style.Column(resultsGrid, L.Translate("Sonuç"), 64, true);
            layout.Controls.Add(resultsGrid, 0, 3);
            layout.Controls.Add(Style.Text(L.Text("CANLI ÇIKTI  /  Adım sonuçları dosyaya kaydedilir",
                "LIVE OUTPUT  /  Step results are saved to disk"), Style.Small, Style.Muted), 0, 4);
            console = new RichTextBox
            {
                Dock = DockStyle.Fill, Margin = Padding.Empty, BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true, AccessibleName = L.Translate("Canlı işlem çıktısı"), DetectUrls = false, WordWrap = false,
                BackColor = Style.Surface, ForeColor = Style.Ink, Font = Style.Mono,
                ScrollBars = RichTextBoxScrollBars.Both
            };
            console.Text = L.Translate("Bir plan başlatıldığında adımlar ve araç çıktısı burada görünür.");
            layout.Controls.Add(console, 0, 5);
            return layout;
        }

        private static T Current<T>(DataGridView grid) where T : class
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as T;
        }

        private void RefreshActions()
        {
            if (!built) return;
            bool idle = !Busy;
            var task = Current<MaintenanceTask>(taskGrid);
            var selected = SelectedTasks();
            taskDetail.Text = task == null ? L.Translate("Bir adıma tıklayarak açıklamasını görün.") :
                task.Detail + (!Program.Admin && task.Admin ? L.Translate(" Bu işlem için yönetici penceresini açın.") : "");
            if (task != null && task.Id == "temp-clean")
                taskDetail.Text += temp.HasPreview ? L.Translate(" Önizleme hazır.") : L.Translate(" Önce önizleme çalıştırın.");
            runMaintenance.Text = selected.Length == 0 ? L.Translate("Planı incele") : L.Translate("Planı incele  (") + selected.Length + ")";
            runMaintenance.Enabled = idle && selected.Length != 0;
            clearTasks.Enabled = idle && selected.Length != 0;
            taskGrid.Enabled = profiles.Enabled = idle;
            if (drives != null) drives.Enabled = idle;
            if (elevate != null) elevate.Enabled = idle;
            foreach (var shortcut in toolShortcuts) shortcut.Enabled = idle;
            language.Enabled = idle;
            workspaceSearch.Enabled = idle;
            RefreshExtraActions(idle);
            RefreshItActions(idle);
            stop.Enabled = Busy && !active.StopRequested;
            openLog.Enabled = !string.IsNullOrEmpty(lastLog);
            exportHtml.Enabled = exportJson.Enabled = exportSummary.Enabled = idle && !string.IsNullOrEmpty(lastLog);
            guideNext.Enabled = idle && guidedScenario != null && !guidedFollowUp && !string.IsNullOrEmpty(lastLog);
            openHistory.Enabled = idle;
            if (compareReports != null) compareReports.Enabled = idle;
            if (Program.Admin) return;

            bool alias = File.Exists(Packages.WinGet);
            bool canUseWinGet = idle && alias;
            appGrid.Enabled = idle;
            install.Enabled = upgrade.Enabled = canUseWinGet && selectedApps.Count != 0;
            uninstall.Enabled = idle;
            listApps.Enabled = listUpdates.Enabled = canUseWinGet;
            version.Enabled = idle;
            clearApps.Enabled = idle && selectedApps.Count != 0;
            appDetail.Text = string.Format(L.Translate("{0} / {1} paket gösteriliyor · {2} seçili. {3}"), appGrid.Rows.Count, packages.Count,
                selectedApps.Count, alias ? L.Translate("Seçimler filtre değiştiğinde korunur.") :
                L.Translate("WinGet yok. Windows araçları → Uygulama Yükleyicisi."));

            var setting = Current<Setting>(settingGrid);
            settingDetail.Text = setting == null ? L.Translate("Bir ayar seçin.") : setting.Detail +
                L.Translate(" Uygulamaların yeni değeri görmesi için pencereyi veya oturumu yeniden açmanız gerekebilir.");
            enable.Enabled = disable.Enabled = idle && setting != null && Settings.Supported(setting);
            undo.Enabled = enable.Enabled && settings.HasBackup(setting);
            refreshSettings.Enabled = idle;
        }

        private void Review(string caption, string note, Job[] jobs, string[] items = null,
            SupportScenario scenario = null, bool followUp = false, string baseline = null)
        {
            if (Busy || jobs.Length == 0) return;
            using (var dialog = new PlanWindow(caption, note, items ?? jobs.Select(job => job.Title).ToArray()))
                if (dialog.ShowDialog(this) == DialogResult.OK) Start(jobs, true, scenario, followUp, baseline);
        }

        private void Start(Job[] jobs, bool showLogs = true, SupportScenario scenario = null, bool followUp = false, string baseline = null)
        {
            if (Busy || jobs.Length == 0) return;
            var run = new RunState(jobs.Length);
            active = run;
            guidedScenario = scenario;
            guidedFollowUp = followUp;
            lastLog = null;
            resultsGrid.Rows.Clear();
            console.Clear();
            progress.Maximum = jobs.Length;
            progress.Value = 0;
            status.Text = L.Translate("Plan hazırlanıyor…");
            elapsed.Text = "00:00";
            if (showLogs) SelectPage("logs");
            RefreshActions();
            settingsRefreshPending = jobs.Any(job => job.Id.StartsWith("setting-", StringComparison.Ordinal));
            timer.Start();
            Task.Run(() => run.Execute(jobs, Program.Logs, Program.Admin ? (Func<string>)MachineFiles.Prepare : null, baseline));
        }

        private void Tick()
        {
            if (!Busy) return;
            var run = active;
            bool finished = run.Finished;
            string line;
            var lines = new StringBuilder();
            int limit = finished ? 1600 : 160;
            for (int i = 0; i < limit && run.TryRead(out line); i++)
            {
                if (lines.Length + line.Length > 145000) lines.Remove(0, Math.Min(lines.Length, 42000));
                lines.AppendLine(line);
            }
            if (lines.Length > 0)
            {
                if (console.TextLength > 145000)
                {
                    console.Select(0, 42000);
                    console.SelectedText = "";
                }
                if (lines.Length > 145000) lines.Remove(0, lines.Length - 145000);
                console.AppendText(lines.ToString());
                console.ScrollToCaret();
            }
            RunResult pair;
            while (run.Results.TryDequeue(out pair))
            {
                int row = resultsGrid.Rows.Add(pair.Key, pair.Value.Label, pair.Value.Message);
                resultsGrid.Rows[row].Tag = pair;
                resultsGrid.Rows[row].Cells[1].Style.ForeColor = pair.Value.Kind == ResultKind.Failed ? Style.Red :
                    pair.Value.Kind == ResultKind.Done ? Style.Accent : Style.Amber;
                resultsGrid.Rows[row].Cells[2].ToolTipText = pair.Value.Message;
            }
            progress.Value = Math.Min(run.Completed, run.Total);
            elapsed.Text = run.Clock.Elapsed.ToString(run.Clock.Elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
            if (finished)
            {
                timer.Stop();
                lastLog = run.LogPath;
                status.Text = run.Summary;
                active = null;
                RefreshExtraResults();
                if (settingsRefreshPending) { settingsRefreshPending = false; FillSettings(); }
                RefreshActions();
                if (closeWhenIdle) Close();
            }
            else
            {
                status.Text = (run.StopRequested ? L.Translate("Sıra duracak  •  ") : "") +
                    run.Completed + "/" + run.Total + "  " + (run.Current.Length == 0 ? L.Translate("Hazırlanıyor…") : run.Current);
                stop.Enabled = !run.StopRequested;
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!Busy || active.Finished || e.CloseReason == CloseReason.WindowsShutDown) return;
            e.Cancel = true;
            if (MessageBox.Show(this, L.Text("Sıra durdurulup pencere kapatılsın mı? Çalışan onarım veya kaldırma tamamlanana kadar beklenir.",
                "Stop the queue and close the window? An active repair or uninstall is allowed to finish."),
                Program.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                closeWhenIdle = true;
                if (active != null) active.Stop();
                else BeginInvoke((Action)Close);
            }
        }

        private void Safe(Action action)
        {
            try { action(); }
            catch (Exception ex) { Inform(ex.Message); }
        }

        private void ChangeLanguage()
        {
            if (Busy) return;
            string question = L.Text("Arayüz İngilizce açılacak. Bu penceredeki geçici seçimler ve temizlik önizlemesi sıfırlanır.",
                "The interface will reopen in Turkish. Temporary selections and the cleanup preview in this window will be cleared.");
            if (MessageBox.Show(this, question, Program.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            Safe(() =>
            {
                L.Set(L.English ? "tr" : "en");
                ReopenRequested = true;
                Close();
            });
        }

        private void ChangeTheme()
        {
            if (Busy) return;
            if (MessageBox.Show(this, L.Text("Tema değişince bu penceredeki geçici seçimler sıfırlanır. Devam edilsin mi?",
                "Changing the theme clears temporary selections in this window. Continue?"), Program.Name,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            Safe(() =>
            {
                Style.SwitchTheme();
                ReopenRequested = true;
                Close();
            });
        }

        private void Inform(string text)
        {
            MessageBox.Show(this, text, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
