using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BakimMerkezi
{
    internal enum ResultKind { Done, Review, Failed, Skipped }

    internal sealed class Result
    {
        internal readonly ResultKind Kind;
        internal readonly string Message;
        internal readonly bool Reboot;

        internal Result(ResultKind kind, string message, bool reboot = false)
        {
            Kind = kind;
            Message = message;
            Reboot = reboot;
        }

        internal string Label
        {
            get
            {
                switch (Kind)
                {
                    case ResultKind.Done: return L.Translate("Tamamlandı");
                    case ResultKind.Review: return L.Translate("İnceleme gerekli");
                    case ResultKind.Failed: return L.Translate("Başarısız");
                    default: return L.Translate("Atlandı");
                }
            }
        }
    }

    internal sealed class Job
    {
        internal readonly string Id;
        internal readonly string Title;
        private readonly Func<Action<string>, CancellationToken, Result> run;

        internal Job(string id, string title, Func<Action<string>, Result> run) : this(id, title, (log, token) => run(log)) { }

        internal Job(string id, string title, Func<Action<string>, CancellationToken, Result> run)
        {
            Id = id;
            Title = title;
            this.run = run;
        }

        internal Result Run(Action<string> log, CancellationToken token = default(CancellationToken)) { return run(log, token); }
    }

    internal sealed class RunResult
    {
        internal readonly string Id, Key;
        internal readonly Result Value;
        internal RunResult(Job job, Result result) { Id = job.Id; Key = job.Title; Value = result; }
    }

    internal sealed class RunState
    {
        private readonly object sync = new object();
        private StreamWriter writer;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private int queued;
        private volatile bool logError;
        private int buffered;
        private const int OutputLimit = 4 * 1024 * 1024;
        private int outputChars;
        private bool outputLimited;
        private readonly ConcurrentQueue<string> output = new ConcurrentQueue<string>();
        internal readonly ConcurrentQueue<RunResult> Results = new ConcurrentQueue<RunResult>();
        internal volatile string Current = "";
        internal volatile int Completed;
        internal volatile bool Finished;
        internal volatile string Summary = "";
        internal string LogPath { get; private set; }
        internal readonly int Total;
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal bool StopRequested { get { return stop.IsCancellationRequested; } }

        internal RunState(int total) { Total = total; }

        internal void Stop() { stop.Cancel(); }

        internal bool TryRead(out string line)
        {
            if (!output.TryDequeue(out line)) return false;
            Interlocked.Decrement(ref queued);
            return true;
        }

        private void Display(string line)
        {
            if (Interlocked.Increment(ref queued) > 1500)
            {
                string ignored;
                TryRead(out ignored);
            }
            output.Enqueue(line);
        }

        private void Log(string line) { WriteLog(line, false); }
        private void ToolOutput(string line) { WriteLog(line, true); }

        private void WriteLog(string line, bool limitOutput)
        {
            line = line ?? "";
            if (line.Length > 8192) line = line.Substring(0, 8192) + "…";
            lock (sync)
            {
                if (limitOutput)
                {
                    if (outputLimited) return;
                    if (outputChars + line.Length + 2 > OutputLimit)
                    {
                        outputLimited = true;
                        line = L.Text("Araç çıktısı günlük sınırına ulaştı. İşlem sonuçları kaydedilmeye devam edecek.",
                            "Tool output reached the log limit. Action results will still be recorded.");
                    }
                    else outputChars += line.Length + 2;
                }
                if (writer != null && !logError)
                {
                    try
                    {
                        writer.WriteLine(line);
                        if (++buffered >= 64) { writer.Flush(); buffered = 0; }
                    }
                    catch (IOException ex) { LogFailed(ex); }
                    catch (UnauthorizedAccessException ex) { LogFailed(ex); }
                }
                Display(line);
            }
        }

        private void LogFailed(Exception ex)
        {
            if (logError) return;
            logError = true;
            Display(L.Translate("Günlük yazılamadı. Sıradaki işlemler başlatılmayacak. ") + ex.Message);
        }

        private void FlushLog()
        {
            lock (sync)
            {
                if (writer == null || logError) return;
                try { writer.Flush(); buffered = 0; }
                catch (IOException ex) { LogFailed(ex); }
                catch (UnauthorizedAccessException ex) { LogFailed(ex); }
            }
        }

        internal void Execute(IList<Job> jobs, string folder, Func<string> prepare = null, string baseline = null)
        {
            int good = 0, review = 0, failed = 0, skipped = 0;
            bool reboot = false;
            bool dismBlocksSfc = false;
            try
            {
                if (prepare != null) folder = prepare();
                else Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                    "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
                writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(true), 8192);
                LogPath = path;
                Log(Program.Name + " " + Program.Version + "  |  " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
                if (prepare != null)
                    Log(L.Text("Yönetici günlük klasörü: ", "Administrator log directory: ") + folder);
                Log("Plan: " + string.Join(" → ", System.Linq.Enumerable.Select(jobs, job => job.Title)));
                if (!string.IsNullOrEmpty(baseline))
                    Log(L.Text("Tanılama günlüğü: ", "Baseline log: ") + Path.GetFileName(baseline));
                foreach (Job job in jobs)
                {
                    Current = job.Title;
                    Result result;
                    if (StopRequested || logError)
                        result = new Result(ResultKind.Skipped, L.Translate("Sıra durduruldu."));
                    else if (job.Id == "sfc" && dismBlocksSfc)
                        result = new Result(ResultKind.Skipped, L.Translate("Önceki DISM onarımı tamamlanmadı veya yeniden başlatma bekliyor."));
                    else if (reboot && (job.Id == "winget-install" || job.Id == "winget-upgrade" ||
                        job.Id == "winget-uninstall" || job.Id == "store-uninstall" ||
                        job.Id.StartsWith("it-change-", StringComparison.Ordinal)))
                        result = new Result(ResultKind.Skipped, L.Text("Önceki adım yeniden başlatma gerektirdi.",
                            "A previous step requires a restart."));
                    else
                    {
                        Log("");
                        Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + job.Title);
                        FlushLog();
                        if (StopRequested)
                            result = new Result(ResultKind.Skipped, L.Translate("Sıra durduruldu."));
                        else if (logError)
                            result = new Result(ResultKind.Skipped, L.Translate("Günlük kaydedilemedi."));
                        else
                        {
                            try { result = job.Run(ToolOutput, stop.Token); }
                            catch (OperationCanceledException ex)
                            {
                                result = StopRequested ?
                                    new Result(ResultKind.Skipped, L.Text("Okuma işlemi iptal edildi.", "The read operation was cancelled.")) :
                                    new Result(ResultKind.Failed, ex.Message);
                            }
                            catch (Exception ex) { result = new Result(ResultKind.Failed, ex.Message); }
                        }
                    }
                    if (job.Id == "dism-repair")
                        dismBlocksSfc = result.Kind != ResultKind.Done || result.Reboot;

                    reboot |= result.Reboot;
                    switch (result.Kind)
                    {
                        case ResultKind.Done: good++; break;
                        case ResultKind.Review: review++; break;
                        case ResultKind.Failed: failed++; break;
                        default: skipped++; break;
                    }
                    Log(result.Label + " · " + job.Title + " — " + result.Message);
                    FlushLog();
                    Results.Enqueue(new RunResult(job, result));
                    Completed++;
                }
                Summary = string.Format(L.Translate("{0} tamamlandı · {1} inceleme · {2} başarısız · {3} atlandı"),
                    good, review, failed, skipped);
                if (reboot) Summary += L.Translate(" · Yeniden başlatma gerekiyor");
                Log("");
                Log(Summary);
            }
            catch (Exception ex)
            {
                Summary = L.Translate("İşlem günlüğü başlatılamadı. ") + ex.Message;
                Display(Summary);
            }
            finally
            {
                if (writer != null)
                {
                    lock (sync)
                    {
                        try { writer.Dispose(); }
                        catch (IOException ex) { LogFailed(ex); }
                        catch (UnauthorizedAccessException ex) { LogFailed(ex); }
                        writer = null;
                    }
                }
                if (logError) Summary += L.Translate(" · Günlük eksik");
                Current = "";
                Clock.Stop();
                Finished = true;
            }
        }
    }

    internal sealed class CommandOutput
    {
        internal readonly int Code;
        internal readonly string Tail;
        internal CommandOutput(int code, string tail) { Code = code; Tail = tail; }
    }

    internal static class Commands
    {
        internal static readonly Encoding Oem = Encoding.GetEncoding(CultureInfo.InstalledUICulture.TextInfo.OEMCodePage);
        private static readonly Regex Escape = new Regex(@"\x1B\[[0-?]*[ -/]*[@-~]|[\x00-\x08\x0B\x0C\x0E-\x1F\u202A-\u202E\u2066-\u2069]");

        internal static string SystemTool(string file)
        {
            return Path.Combine(Environment.SystemDirectory, file);
        }

        internal static CommandOutput PowerShell(string script, Action<string> log,
            CancellationToken token = default(CancellationToken), TimeSpan? probeTimeout = null)
        {
            string body = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
                "$env:PSModulePath=$PSHOME+'\\Modules'; " +
                "$English=$" + (L.English ? "true" : "false") + "; " +
                "[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); " +
                "try { " + script + "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(body));
            return Run(SystemTool(@"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded, Encoding.UTF8, log, true, token, probeTimeout);
        }

        internal static CommandOutput Run(string executable, string args, Encoding encoding, Action<string> log,
            bool powershell = false, CancellationToken token = default(CancellationToken), TimeSpan? probeTimeout = null)
        {
            var tail = new Queue<string>();
            var gate = new object();
            var start = new ProcessStartInfo(executable, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = powershell ? Encoding.UTF8 : encoding,
                CreateNoWindow = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            if (powershell)
            {
                start.EnvironmentVariables["PSModulePath"] = SystemTool(@"WindowsPowerShell\v1.0\Modules");
                start.EnvironmentVariables["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
            }
            if (Program.Admin) RestrictEnvironment(start, MachineFiles.LogDirectory);
            using (var closed = new CountdownEvent(2))
            using (var process = new Process { StartInfo = start })
            {
                bool accepting = true;
                Action<string> emit = line =>
                {
                    if (line != null)
                    {
                        if (line.Length > 8192) line = line.Substring(0, 8192) + "…";
                        line = Escape.Replace(line, "").TrimEnd();
                        if (line.Length == 0) return;
                    }
                    lock (gate)
                    {
                        if (!accepting) return;
                        if (line == null) { closed.Signal(); return; }
                        if (tail.Count == 35) tail.Dequeue();
                        tail.Enqueue(line);
                        log(line);
                    }
                };
                process.OutputDataReceived += (sender, e) => emit(e.Data);
                process.ErrorDataReceived += (sender, e) => emit(e.Data);
                try
                {
                    if (probeTimeout.HasValue) token.ThrowIfCancellationRequested();
                    process.Start();
                    process.StandardInput.Close();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    Wait(process, token, probeTimeout);
                    // A background installer can inherit the pipes and keep them open after WinGet exits.
                    if (!closed.Wait(TimeSpan.FromSeconds(8)))
                        log(L.Translate("Komut bitti. Bir alt süreç çıktı kanallarını açık tuttuğu için son satırlar eksik olabilir."));
                    lock (gate) return new CommandOutput(process.ExitCode, string.Join("\n", tail));
                }
                finally
                {
                    lock (gate) accepting = false;
                }
            }
        }

        internal static void RestrictEnvironment(ProcessStartInfo start, string temp = null)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify);
            var variables = start.EnvironmentVariables;
            foreach (string key in variables.Keys.Cast<string>().ToArray())
            {
                if (key.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("DEVPATH", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("PSExecutionPolicyPreference", StringComparison.OrdinalIgnoreCase)) variables.Remove(key);
            }
            variables["PATH"] = string.Join(";", Environment.SystemDirectory, windows,
                SystemTool("Wbem"), SystemTool(@"WindowsPowerShell\v1.0"));
            variables["COMSPEC"] = SystemTool("cmd.exe");
            variables["SystemRoot"] = windows;
            variables["windir"] = windows;
            if (temp != null) { variables["TEMP"] = temp; variables["TMP"] = temp; }
        }

        private static void Wait(Process process, CancellationToken token, TimeSpan? limit)
        {
            if (!limit.HasValue)
            {
                while (!process.WaitForExit(30000)) { }
                return;
            }
            var elapsed = Stopwatch.StartNew();
            while (!process.WaitForExit(200))
            {
                if (!token.IsCancellationRequested && elapsed.Elapsed < limit.Value) continue;
                try { if (!process.HasExited) process.Kill(); }
                catch (System.ComponentModel.Win32Exception) { if (!process.HasExited) throw; }
                catch (InvalidOperationException) { if (!process.HasExited) throw; }
                if (!process.WaitForExit(3000))
                    throw new IOException(L.Text("Okuma aracı zamanında kapanmadı.", "The read-only tool did not stop in time."));
                token.ThrowIfCancellationRequested();
                throw new TimeoutException(L.Text("Okuma aracı zaman sınırına ulaştı.", "The read-only tool reached its time limit."));
            }
        }

        internal static string Code(int code)
        {
            return string.Format("{0} (0x{0:X8})", code);
        }
    }
}
