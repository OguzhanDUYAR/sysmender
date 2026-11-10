using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BakimMerkezi
{
    // Counter resets and zero-length intervals must never look like 0% usage.
    internal sealed class CpuSampler
    {
        private ulong idle, kernel, user;
        private bool sampled;
        internal void Reset() { sampled = false; }
        internal double? Read(ulong nextIdle, ulong nextKernel, ulong nextUser)
        {
            double? result = null;
            if (sampled && nextIdle >= idle && nextKernel >= kernel && nextUser >= user)
            {
                ulong k = nextKernel - kernel, u = nextUser - user, i = nextIdle - idle;
                if (k <= ulong.MaxValue - u)
                {
                    ulong total = k + u;
                    if (total > 0 && i <= total) result = 100.0 * (total - i) / total;
                }
            }
            idle = nextIdle; kernel = nextKernel; user = nextUser; sampled = true;
            return result;
        }
    }

    internal sealed class HistoryChart : Control
    {
        private struct Sample { internal double Time, Value; }
        private readonly List<Sample> samples = new List<Sample>();
        private double now;
        internal int Count { get { return samples.Count; } }
        internal HistoryChart()
        {
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            BackColor = Style.Surface;
            ForeColor = Style.Accent;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        internal void Clear() { samples.Clear(); now = 0; Invalidate(); }
        internal void Add(double time, double? value)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0) return;
            if (time < now) samples.Clear();
            now = time;
            samples.RemoveAll(sample => time - sample.Time > 60);
            double number = value.HasValue ? value.Value : double.NaN;
            if (double.IsInfinity(number) || number < 0 || number > 100) number = double.NaN;
            samples.Add(new Sample { Time = time, Value = number });
            if (samples.Count > 31) samples.RemoveAt(0);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float left = 1, top = 4, width = ClientSize.Width - 47, height = ClientSize.Height - 24;
            if (width < 20 || height < 10) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var grid = new Pen(Style.Border))
            using (var line = new Pen(ForeColor, 2))
            using (var dot = new SolidBrush(ForeColor))
            {
                for (int i = 0; i <= 2; i++)
                    e.Graphics.DrawLine(grid, left, top + height * i / 2, left + width, top + height * i / 2);
                for (int i = 0; i <= 4; i++)
                    e.Graphics.DrawLine(grid, left + width * i / 4, top, left + width * i / 4, top + height);
                PointF? previous = null;
                double previousTime = 0;
                foreach (var sample in samples)
                {
                    if (double.IsNaN(sample.Value)) { previous = null; continue; }
                    var point = new PointF(left + width * (float)(1 - (now - sample.Time) / 60),
                        top + height * (float)(1 - sample.Value / 100));
                    if (previous.HasValue && sample.Time - previousTime <= 5)
                        e.Graphics.DrawLine(line, previous.Value, point);
                    e.Graphics.FillEllipse(dot, point.X - 2, point.Y - 2, 4, 4);
                    previous = point; previousTime = sample.Time;
                }
            }
            TextRenderer.DrawText(e.Graphics, "100%", Style.Small, new Point((int)(left + width + 5), 0), Style.Muted);
            TextRenderer.DrawText(e.Graphics, "0%", Style.Small, new Point((int)(left + width + 5), (int)(top + height - 13)), Style.Muted);
            TextRenderer.DrawText(e.Graphics, L.Text("60 sn önce", "60s ago"), Style.Small,
                new Point(0, (int)(top + height + 1)), Style.Muted);
            string current = L.Text("şimdi", "now");
            var size = TextRenderer.MeasureText(current, Style.Small);
            TextRenderer.DrawText(e.Graphics, current, Style.Small,
                new Point((int)(left + width) - size.Width, (int)(top + height + 1)), Style.Muted);
        }
    }

    internal sealed class NetworkRateSampler
    {
        internal struct Traffic
        {
            internal readonly long Received, Sent;
            internal Traffic(long received, long sent) { Received = received; Sent = sent; }
        }

        private Dictionary<string, Traffic> previous;
        private double previousTime;

        internal void Reset() { previous = null; }

        internal Tuple<double, double> Read(Dictionary<string, Traffic> current, double time)
        {
            var old = previous;
            double duration = time - previousTime;
            previous = current;
            previousTime = time;
            if (old == null || current == null || current.Count == 0 || old.Count != current.Count ||
                duration <= 0 || duration > 10) return null;
            double received = 0, sent = 0;
            foreach (var item in current)
            {
                Traffic before;
                if (!old.TryGetValue(item.Key, out before) || item.Value.Received < before.Received ||
                    item.Value.Sent < before.Sent || before.Received < 0 || before.Sent < 0) return null;
                received += item.Value.Received - before.Received;
                sent += item.Value.Sent - before.Sent;
            }
            return Tuple.Create(received / duration, sent / duration);
        }

        internal static Dictionary<string, Traffic> Capture()
        {
            var result = new Dictionary<string, Traffic>(StringComparer.Ordinal);
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                var type = adapter.NetworkInterfaceType;
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    (type != NetworkInterfaceType.Ethernet && type != NetworkInterfaceType.GigabitEthernet &&
                     type != NetworkInterfaceType.FastEthernetT && type != NetworkInterfaceType.FastEthernetFx &&
                     type != NetworkInterfaceType.Wireless80211)) continue;
                var counters = adapter.GetIPStatistics();
                result[adapter.Id] = new Traffic(counters.BytesReceived, counters.BytesSent);
            }
            return result.Count == 0 ? null : result;
        }
    }

    internal sealed partial class MainWindow
    {
        private readonly System.Windows.Forms.Timer monitorTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly CpuSampler cpuSampler = new CpuSampler();
        private readonly NetworkRateSampler networkSampler = new NetworkRateSampler();
        private static Task<Dictionary<string, NetworkRateSampler.Traffic>> pendingTraffic;
        private static long trafficRequested;
        private long trafficSession;
        private readonly Stopwatch monitorClock = Stopwatch.StartNew();
        private Label cpuValue, memoryValue, diskValue, networkValue, monitorStatus;
        private HistoryChart cpuHistory, memoryHistory;
        private bool diskReading;
        private int monitorGeneration;

        private Control MonitorPage()
        {
            var panel = Style.Stack(40, 0, 0, 72, 32);
            panel.Controls.Add(Style.Note(L.Text(
                "2 saniyelik ölçüm · CPU/RAM için 60 saniyelik geçmiş · Sayfa etkin değilken duraklar.",
                "2-second samples · 60-second CPU/RAM history · Pauses when inactive.")), 0, 0);
            cpuHistory = new HistoryChart { AccessibleName = L.Text("İşlemci geçmişi", "CPU history") };
            memoryHistory = new HistoryChart { AccessibleName = L.Text("Bellek geçmişi", "Memory history") };
            string cpuTitle = GetActiveProcessorGroupCount() > 1 ?
                L.Text("İŞLEMCİ · BİRİNCİL İŞLEMCİ GRUBU", "CPU · PRIMARY PROCESSOR GROUP") : L.Text("İŞLEMCİ", "CPU");
            panel.Controls.Add(MonitorCard(cpuTitle, out cpuValue, cpuHistory), 0, 1);
            panel.Controls.Add(MonitorCard(L.Text("FİZİKSEL BELLEK", "PHYSICAL MEMORY"), out memoryValue, memoryHistory), 0, 2);
            var devices = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            devices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
            devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
            var disk = MonitorCard(L.Text("SİSTEM SÜRÜCÜSÜ · BOŞ ALAN", "SYSTEM DRIVE · FREE SPACE"), out diskValue, null);
            disk.Margin = new Padding(0, 4, 4, 4);
            devices.Controls.Add(disk, 0, 0);
            devices.Controls.Add(MonitorCard(L.Text("AĞ · ETHERNET / WI-FI", "NETWORK · ETHERNET / WI-FI"), out networkValue, null), 1, 0);
            panel.Controls.Add(devices, 0, 3);
            monitorStatus = Style.Text("", Style.Small, Style.Muted);
            panel.Controls.Add(monitorStatus, 0, 4);
            monitorTimer.Tick += (sender, e) => UpdateMonitor();
            Shown += (sender, e) => RefreshMonitorState();
            Resize += (sender, e) => { if (built) RefreshMonitorState(); };
            return panel;
        }

        private static Control MonitorCard(string label, out Label value, HistoryChart history)
        {
            var card = history == null ? Style.Stack(23, 0) : Style.Stack(28, 32, 0);
            card.BackColor = Style.Surface;
            card.Padding = new Padding(14, 5, 14, 5);
            card.Margin = new Padding(0, 4, 0, 4);
            card.Controls.Add(Style.Text(label, Style.Small, Style.Muted), 0, 0);
            value = Style.Text("—", Style.Section, Style.Accent);
            value.AccessibleName = label;
            card.Controls.Add(value, 0, 1);
            if (history != null) card.Controls.Add(history, 0, 2);
            return card;
        }

        private void RefreshMonitorState()
        {
            bool enabled = CurrentPage == "monitor" && WindowState != FormWindowState.Minimized && Visible;
            if (!enabled)
            {
                if (monitorTimer.Enabled) { monitorTimer.Stop(); monitorGeneration++; }
                cpuSampler.Reset();
                return;
            }
            if (monitorTimer.Enabled) return;
            cpuSampler.Reset();
            networkSampler.Reset();
            trafficSession = Stopwatch.GetTimestamp();
            cpuHistory.Clear(); memoryHistory.Clear();
            diskValue.Text = "—";
            networkValue.Text = "—";
            monitorClock.Restart();
            UpdateMonitor();
            monitorTimer.Start();
        }

        private void UpdateMonitor()
        {
            if (IsDisposed || Disposing || !IsHandleCreated || CurrentPage != "monitor" ||
                WindowState == FormWindowState.Minimized) return;
            ulong idle, kernel, user;
            double? cpu = null;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                cpu = cpuSampler.Read(idle, kernel, user);
                cpuValue.Text = cpu.HasValue ? string.Format("{0:0.0} %", cpu.Value) : L.Text("Ölçülüyor…", "Sampling…");
            }
            else { cpuSampler.Reset(); cpuValue.Text = L.Text("Kullanılamıyor", "Unavailable"); }

            var memory = new MemoryStatus();
            memory.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            double? memoryPercent = null;
            if (GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 && memory.AvailablePhysical <= memory.TotalPhysical)
            {
                memoryPercent = 100.0 * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical;
                memoryValue.Text = string.Format("{0:0.0}%  ·  {1} / {2}", memoryPercent.Value,
                    Maintenance.Size((long)(memory.TotalPhysical - memory.AvailablePhysical)), Maintenance.Size((long)memory.TotalPhysical));
            }
            else memoryValue.Text = L.Text("Kullanılamıyor", "Unavailable");
            double time = monitorClock.Elapsed.TotalSeconds;
            cpuHistory.Add(time, cpu); memoryHistory.Add(time, memoryPercent);
            var uptime = TimeSpan.FromMilliseconds(GetTickCount64());
            monitorStatus.Text = L.Text("Son ölçüm  ", "Last sample  ") + DateTime.Now.ToString("HH:mm:ss") +
                L.Text("  ·  Çalışma süresi ", "  ·  Uptime ") + uptime.Days + L.Text(" gün ", " days ") +
                uptime.Hours.ToString("00") + ":" + uptime.Minutes.ToString("00");
            ReadMonitorDisk();
            PollNetwork();
        }

        private void PollNetwork()
        {
            if (pendingTraffic != null)
            {
                if (!pendingTraffic.IsCompleted)
                {
                    if ((Stopwatch.GetTimestamp() - trafficRequested) / (double)Stopwatch.Frequency > 4)
                    {
                        networkValue.Text = L.Text("Ağ sayacı yanıt vermiyor", "Network counter is not responding");
                        networkSampler.Reset();
                    }
                    return;
                }
                var values = pendingTraffic.GetAwaiter().GetResult();
                pendingTraffic = null;
                if (trafficRequested >= trafficSession)
                {
                    var rate = networkSampler.Read(values, trafficRequested / (double)Stopwatch.Frequency);
                    networkValue.Text = values == null ? L.Text("Kullanılamıyor", "Unavailable") :
                        rate == null ? L.Text("Ölçülüyor…", "Sampling…") :
                        "↓ " + Maintenance.Size((long)rate.Item1) + "/s   ↑ " + Maintenance.Size((long)rate.Item2) + "/s";
                }
            }
            trafficRequested = Stopwatch.GetTimestamp();
            pendingTraffic = Task.Run(() =>
            {
                try { return NetworkRateSampler.Capture(); }
                catch (NetworkInformationException) { return null; }
                catch (NotSupportedException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
                catch (System.Security.SecurityException) { return null; }
                catch (System.ComponentModel.Win32Exception) { return null; }
            });
        }

        private async void ReadMonitorDisk()
        {
            if (diskReading) return;
            diskReading = true;
            int generation = monitorGeneration;
            try
            {
                string disk = await Task.Run(() =>
                {
                    var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                    return !drive.IsReady ? L.Text("Sürücü hazır değil", "Drive is not ready") :
                        string.Format("{0}  ·  {1} / {2}", drive.Name,
                            Maintenance.Size(drive.AvailableFreeSpace), Maintenance.Size(drive.TotalSize));
                });
                if (!IsDisposed && !Disposing && generation == monitorGeneration) diskValue.Text = disk;
            }
            catch (Exception)
            {
                if (!IsDisposed && !Disposing && generation == monitorGeneration)
                    diskValue.Text = L.Text("Sürücü bilgisi okunamadı", "Drive information unavailable");
            }
            finally { diskReading = false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            internal ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        private static extern ushort GetActiveProcessorGroupCount();

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}
