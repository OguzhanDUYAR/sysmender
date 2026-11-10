using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BakimMerkezi
{
    internal sealed partial class MainWindow
    {
        private readonly HashSet<string> itSelected = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> itFavorites = new HashSet<string>(StringComparer.Ordinal);
        private DataGridView itGrid;
        private TableLayoutPanel itLayout;
        private Control itTarget;
        private TextBox itSearch, itHost;
        private NumericUpDown itPort;
        private ComboBox itCategory, itProfile;
        private Label itDetail, itSummary;
        private Button itRun, itClear, itApply, itAdmin, itFavorite, exportHtml, exportJson;
        private bool fillingIt;

        private Control ItPage()
        {
            var layout = itLayout = Style.Stack(39, 40, 0, 65, 38, 40, 27);
            var filter = Style.Row();
            var searchLabel = Style.Text(L.Text("Ara", "Search"), Style.Small);
            searchLabel.Dock = DockStyle.None;
            searchLabel.Size = new Size(57, 31);
            itSearch = new TextBox { Width = 270, BackColor = Style.Surface, ForeColor = Style.Ink,
                BorderStyle = BorderStyle.FixedSingle, Font = Style.Normal, Margin = new Padding(0,4,8,0),
                AccessibleName = L.Text("BT aracı ara", "Search IT tools") };
            itCategory = Style.Combo(L.Text("Araç kategorisi", "Tool category"), 220);
            itCategory.Items.Add(L.Text("Tüm kategoriler", "All categories"));
            itCategory.Items.Add(L.Text("★ Sık kullanılanlar", "★ Favorites"));
            itCategory.Items.AddRange(ItTools.All.Select(tool => tool.Group).Distinct().OrderBy(name => name).Cast<object>().ToArray());
            var savedFavorites = new HashSet<string>(UserPreference.Read("SikAraclar.txt", 4096).Split(
                new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            foreach (var tool in ItTools.All)
                if (savedFavorites.Contains(tool.Id)) itFavorites.Add(tool.Id);
            itCategory.SelectedIndex = 0;
            filter.Controls.Add(searchLabel);
            filter.Controls.Add(itSearch);
            filter.Controls.Add(itCategory);
            layout.Controls.Add(filter,0,0);

            var profilesRow = Style.Row();
            itProfile = Style.Combo(L.Text("Tanılama profili", "Diagnostic profile"), 205);
            itProfile.Items.AddRange(new object[] { L.Text("Genel BT incelemesi", "General IT triage"),
                L.Text("Ağ incelemesi", "Network review"), L.Text("Windows / kararlılık", "Windows / stability"),
                L.Text("Yönetici incelemesi", "Administrator review") });
            itProfile.SelectedIndex = 0;
            itApply = Style.Button(L.Text("Profili seç", "Select profile"), ApplyItProfile);
            itClear = Style.Button(L.Text("Seçimi temizle", "Clear selection"), () => { itSelected.Clear(); FillIt(); });
            profilesRow.Controls.Add(itProfile); profilesRow.Controls.Add(itApply); profilesRow.Controls.Add(itClear);
            itFavorite = Style.Button(L.Text("Favori", "Favorite"), ToggleFavorite);
            profilesRow.Controls.Add(itFavorite);
            layout.Controls.Add(profilesRow,0,1);

            itGrid = Style.Grid(L.Text("BT araçları", "IT tools"));
            Style.CheckColumn(itGrid);
            Style.Column(itGrid,L.Text("Kategori", "Category"),115);
            Style.Column(itGrid,L.Text("Araç", "Tool"),100,true);
            Style.Column(itGrid,L.Text("Etki / izin", "Risk / access"),160);
            itGrid.CellValueChanged += (sender,e) =>
            {
                if (fillingIt || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var row=itGrid.Rows[e.RowIndex];
                var tool=row.Tag as ItTool;
                if(tool == null || (tool.Admin && !Program.Admin)) return;
                if (Equals(row.Cells[0].Value,true)) itSelected.Add(tool.Id); else itSelected.Remove(tool.Id);
                RefreshItActions(!Busy);
            };
            itGrid.SelectionChanged += (sender,e) => { if (!fillingIt) RefreshItActions(!Busy); };
            layout.Controls.Add(itGrid,0,2);
            itDetail=Style.Note("");
            layout.Controls.Add(itDetail,0,3);

            var target = Style.Row();
            itTarget = target;
            var hostLabel=Style.Text(L.Text("Ağ hedefi", "Network target"),Style.Small);
            hostLabel.Dock=DockStyle.None; hostLabel.Size=new Size(93,30);
            itHost = new TextBox { Width=215,Text="localhost",Font=Style.Normal,BackColor=Style.Surface,
                ForeColor=Style.Ink,BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,4,10,0),
                AccessibleName=L.Text("DNS adı veya IP adresi", "DNS name or IP address") };
            var portLabel=Style.Text("TCP",Style.Small); portLabel.Dock=DockStyle.None; portLabel.Size=new Size(39,30);
            itPort=new NumericUpDown { Minimum=1,Maximum=65535,Value=443,Width=90,Font=Style.Normal,
                BackColor=Style.Surface,ForeColor=Style.Ink,Margin=new Padding(0,4,0,0),AccessibleName="TCP port" };
            target.Controls.Add(hostLabel); target.Controls.Add(itHost); target.Controls.Add(portLabel); target.Controls.Add(itPort);
            layout.Controls.Add(target,0,4);
            var actions=Style.Row();
            itRun=Style.Button(L.Text("Planı incele", "Review plan"),ReviewIt,true);
            actions.Controls.Add(itRun);
            openGuide = Style.Button(L.Text("Sorun gider", "Troubleshoot"), ShowGuide);
            actions.Controls.Add(openGuide);
            if(!Program.Admin)
            {
                openFileTools = Style.Button(L.Text("Dosya araçları", "File tools"), ShowFileTools);
                actions.Controls.Add(openFileTools);
                itAdmin=Style.Button(L.Text("Yönetici BT araçları", "Administrator IT tools"),()=>Safe(()=>Program.Elevate("it")));
                actions.Controls.Add(itAdmin);
            }
            layout.Controls.Add(actions,0,5);
            itSummary=Style.Text("",Style.Small,Style.Muted);
            layout.Controls.Add(itSummary,0,6);
            itSearch.TextChanged+=(sender,e)=>FillIt();
            itCategory.SelectedIndexChanged+=(sender,e)=>FillIt();
            FillIt();
            return layout;
        }

        private void FillIt()
        {
            if(itGrid==null) return;
            fillingIt=true;
            try
            {
                itGrid.Rows.Clear();
                string query=itSearch.Text.Trim();
                foreach(var tool in ItTools.All)
                {
                    if(itCategory.SelectedIndex == 1 && !itFavorites.Contains(tool.Id)) continue;
                    if(itCategory.SelectedIndex>1 && tool.Group != (string)itCategory.SelectedItem) continue;
                    if(query.Length>0 && (tool.Title+" "+tool.Detail+" "+tool.Id).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)<0) continue;
                    int index=itGrid.Rows.Add(itSelected.Contains(tool.Id),tool.Group,
                        (itFavorites.Contains(tool.Id) ? "★ " : "") + tool.Title,tool.Access);
                    var row=itGrid.Rows[index]; row.Tag=tool;
                    row.Cells[2].ToolTipText=tool.Detail;
                    if(tool.Admin && !Program.Admin)
                    {
                        row.Cells[0]=new DataGridViewTextBoxCell { Value="—" };
                        row.Cells[0].ReadOnly=true;
                        row.DefaultCellStyle.ForeColor=Style.Muted;
                    }
                }
            }
            finally { fillingIt=false; }
            RefreshItActions(!Busy);
        }

        private void ApplyItProfile()
        {
            string[][] profiles = {
                new[] {"readiness","hardware","device-errors","volumes","processes","events-system","defender","update-history","printers"},
                new[] {"network-adapters","network-config","routes","tcp-udp","proxy","wlan","time-status"},
                new[] {"events-system","events-app","crashes","reliability","update-history","hotfixes","critical-services","servicing-logs"},
                new[] {"features","component-analysis","volume-analysis","bitlocker","tpm-boot","task-errors","policy","winre","boot-config","firewall-rules"}
            };
            itSelected.Clear();
            foreach(string id in profiles[itProfile.SelectedIndex])
            {
                var tool=ItTools.All.Single(item=>item.Id==id);
                if(!tool.Changes && (!tool.Admin || Program.Admin)) itSelected.Add(id);
            }
            FillIt();
            if(itSelected.Count==0)
                Inform(L.Text("Bu profil yönetici penceresi gerektiriyor.", "This profile requires the administrator window."));
        }

        private void RefreshItActions(bool idle)
        {
            if(itGrid==null || itRun==null) return;
            var tool=Current<ItTool>(itGrid);
            itDetail.Text=tool==null ? L.Text("Ayrıntı için bir araç seçin. Hedef alanı bağlantı testi ve rota izleme için kullanılır.",
                "Select a tool for details. The target field is used by the connection test and trace route.") :
                tool.Detail+(tool.Admin&&!Program.Admin ? L.Text(" Yönetici BT araçlarını açın.", " Open administrator IT tools.") : "");
            itRun.Enabled=idle&&itSelected.Count>0;
            itRun.Text=L.Text("Planı incele", "Review plan")+" ("+itSelected.Count+")";
            itClear.Enabled=idle&&itSelected.Count>0;
            itFavorite.Enabled = idle && tool != null;
            itFavorite.Text = tool != null && itFavorites.Contains(tool.Id) ?
                L.Text("Favoriden çıkar", "Remove favorite") : L.Text("Favoriye ekle", "Add favorite");
            openGuide.Enabled = idle;
            if (openFileTools != null) openFileTools.Enabled = idle;
            itApply.Enabled=itProfile.Enabled=itGrid.Enabled=itSearch.Enabled=itCategory.Enabled=idle;
            bool targetSelected = itSelected.Any(ItTools.UsesTarget);
            itTarget.Visible = targetSelected;
            itLayout.RowStyles[4].Height = targetSelected ? 38 : 0;
            itHost.Enabled = idle && targetSelected;
            itPort.Enabled = idle && itSelected.Contains("endpoint");
            if(itAdmin!=null) itAdmin.Enabled=idle;
            itSummary.Text=string.Format(L.Text("{0}/{1} araç · {2} seçili · Seçimler filtre değişince korunur.",
                "{0}/{1} tools · {2} selected · Selections survive filtering."),itGrid.Rows.Count,ItTools.All.Length,itSelected.Count);
        }

        private void ToggleFavorite()
        {
            var tool = Current<ItTool>(itGrid);
            if (tool == null || Busy) return;
            Safe(() =>
            {
                var next = new HashSet<string>(itFavorites, StringComparer.Ordinal);
                if (!next.Add(tool.Id)) next.Remove(tool.Id);
                UserPreference.Write("SikAraclar.txt", string.Join("\n", next.OrderBy(id => id, StringComparer.Ordinal)));
                itFavorites.Clear();
                itFavorites.UnionWith(next);
                FillIt();
                var row = itGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => item.Tag == tool);
                if (row != null) itGrid.CurrentCell = row.Cells[2];
            });
        }

        private void ReviewIt()
        {
            Safe(() =>
            {
                var tools=ItTools.All.Where(tool=>itSelected.Contains(tool.Id)).OrderBy(tool=>tool.Changes).ToArray();
                if(tools.Any(tool=>tool.Admin&&!Program.Admin)) throw new InvalidOperationException("Administrator required.");
                var jobs=tools.Select(tool=>ItTools.Create(tool.Id,itHost.Text,(int)itPort.Value)).ToArray();
                string note=tools.Any(tool=>tool.Changes) ?
                    L.Text("Seçilen değişikliklerin etkilerini aşağıda inceleyin. Sırayı durdurmak çalışan onarımı zorla kesmez.",
                        "Review the effects of selected changes below. Stopping the queue does not force-stop an active repair.") :
                    L.Text("Bu plan tanılama yapar. Rapor cihaz, kullanıcı ve ağ bilgileri içerebilir.",
                        "This plan runs diagnostics. Reports may contain device, user and network information.");
                var items=tools.Select(tool=>tool.Title+" · "+tool.Access+" · "+tool.Detail+
                    (ItTools.UsesTarget(tool.Id) ? " [" + itHost.Text + (tool.Id == "endpoint" ? ":" + itPort.Value : "") + "]" : "")).ToArray();
                Review(L.Text("BT işlem planı", "IT action plan"),note,jobs,items);
            });
        }

        private void ExportSupport(bool json, bool summaryOnly = false)
        {
            if(Busy || string.IsNullOrEmpty(lastLog)) return;
            Safe(() =>
            {
                if(!summaryOnly && new FileInfo(lastLog).Length>8*1024*1024) throw new IOException(L.Text("Rapor için günlük çok büyük.", "The log is too large to export."));
                var rows=resultsGrid.Rows.Cast<DataGridViewRow>().Select(row=>new ReportStep {
                    Id = row.Tag is RunResult ? ((RunResult)row.Tag).Id : null,
                    Outcome = row.Tag is RunResult ? ((RunResult)row.Tag).Value.Kind.ToString() : null,
                    Tool=Convert.ToString(row.Cells[0].Value), Status=Convert.ToString(row.Cells[1].Value),
                    Message=Convert.ToString(row.Cells[2].Value) }).ToArray();
                string log = summaryOnly ? "" : TempCleanup.ReadVerifiedFile(lastLog, stream =>
                {
                    if (stream.Length > 8 * 1024 * 1024) throw new IOException("The log is too large to export.");
                    using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
                });
                string data=SupportReport.Build(rows,log,json,summaryOnly);
                string name="BT-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+(json?".json":".html");
                string path;
                if(Program.Admin)
                {
                    path=Path.Combine(MachineFiles.Prepare(),name);
                }
                else
                {
                    using(var dialog=new SaveFileDialog { FileName=name,DefaultExt=json?"json":"html",
                        Filter=json?"JSON (*.json)|*.json":"HTML (*.html)|*.html",AddExtension=true,
                        Title=summaryOnly ? L.Text("Paylaşım özetini kaydet", "Save shareable summary") :
                            L.Text("Destek raporunu kaydet · cihaz ve ağ bilgileri içerir", "Save support report · contains device and network information") })
                    {
                        if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                        path=dialog.FileName;
                    }
                }
                if(!string.Equals(Path.GetExtension(path),json?".json":".html",StringComparison.OrdinalIgnoreCase))
                    throw new IOException(L.Text("Seçilen rapor uzantısını kullanın.", "Use the selected report extension."));
                SupportReport.WriteNew(path,data);
                Inform(L.Text("Rapor kaydedildi: ", "Report saved: ")+path);
            });
        }
    }
}

