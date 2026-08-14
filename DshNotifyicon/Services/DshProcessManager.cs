using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DshNotifyicon.Services
{
    public enum DshState { Idle, Starting, Running, Stopping, Error }

    public class InstanceInfo
    {
        public int Pid;
        public string CommandLine;
    }

    public enum PreflightKind { Ok, PortBusy, ExternalInstances }

    public class PreflightResult
    {
        public PreflightKind Kind;
        public int Port;
        public List<InstanceInfo> Instances = new List<InstanceInfo>();
        public string Message = "";
    }

    /// <summary>
    /// dsh web 服务进程生命周期：命令行拼接、隐藏启动、URL 解析、健康探测、树杀停止。
    /// 状态机 Idle/Starting/Running/Stopping/Error；所有操作经信号量互斥。
    /// 日志文案走 Loc（中英双语，随界面语言切换）。
    /// </summary>
    public class DshProcessManager
    {
        public DshState State { get { return _state; } }
        public string Url { get; private set; }
        public int? ProcessId { get; private set; }

        /// <summary>环形日志（最多 1000 行），主窗口打开时回填。</summary>
        public List<string> RecentLog { get; } = new List<string>();

        public event Action<DshState> StateChanged;
        /// <summary>启动成功且服务就绪，携带实际 URL（--port 0 时为解析结果）。</summary>
        public event Action<string> Ready;
        /// <summary>非主动停止的进程退出，携带说明。</summary>
        public event Action<string> Exited;
        public event Action<string> LogLine;

        readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        readonly object _logLock = new object();
        Process _proc;
        DshState _state = DshState.Idle;
        volatile bool _stoppingByUs;

        static readonly Regex UrlRx = new Regex(@"dsh web: http://127\.0\.0\.1:(\d+)", RegexOptions.Compiled);

        void SetState(DshState s)
        {
            _state = s;
            try { StateChanged?.Invoke(s); } catch { } // 订阅者异常隔离，不影响进程
        }

        void Log(string line)
        {
            lock (_logLock)
            {
                RecentLog.Add(line);
                if (RecentLog.Count > 1000) RecentLog.RemoveRange(0, RecentLog.Count - 1000);
            }
            try { LogLine?.Invoke(line); } catch { } // 订阅者异常隔离
        }

        /// <summary>线程安全地取日志快照（主窗口打开时回填）。</summary>
        public string[] SnapshotLog()
        {
            lock (_logLock) return RecentLog.ToArray();
        }

        /// <summary>固定端口是否已被占用（TcpListener 探测）。</summary>
        public static bool IsPortBusy(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return false;
            }
            catch { return true; }
        }

        /// <summary>
        /// 扫描运行中的其他 dsh 实例（node.exe 且命令行含 dsh lib/bin.js）。
        /// 用于防止双实例并发写同一 DSH_HOME。PowerShell CIM 查询，约 1-2s。
        /// </summary>
        public static async Task<List<InstanceInfo>> ScanExternalInstancesAsync()
        {
            var list = new List<InstanceInfo>();
            try
            {
                // -EncodedCommand（UTF-16LE Base64）彻底规避 CreateProcess 引号转义问题
                var script = "Get-CimInstance Win32_Process -Filter \"Name='node.exe'\" | Where-Object { $_.CommandLine -like '*@deepseek-ai\\dsh*lib\\bin.js*' } | Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress";
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var r = await ProcessRunner.RunAsync(new ProcessSpec
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -EncodedCommand " + encoded,
                    TimeoutMs = 20000
                }, CancellationToken.None, null);
                var t = (r.Output ?? "").Trim();
                if (t.Length == 0) return list;
                var arr = t.StartsWith("[") ? JArray.Parse(t) : new JArray(JObject.Parse(t));
                foreach (var item in arr)
                {
                    list.Add(new InstanceInfo
                    {
                        Pid = (int)item["ProcessId"],
                        CommandLine = (string)item["CommandLine"] ?? ""
                    });
                }
            }
            catch { }
            return list;
        }

        /// <summary>启动前检查：固定端口占用、外部 dsh 实例。返回需要 UI 决策的结果。</summary>
        public async Task<PreflightResult> PreflightAsync(int port, bool randomPort)
        {
            var r = new PreflightResult { Kind = PreflightKind.Ok, Port = port };
            if (!randomPort && port != 0 && IsPortBusy(port))
            {
                r.Kind = PreflightKind.PortBusy;
                r.Message = Loc.T("dsh.portBusy", port);
                return r;
            }
            var inst = await ScanExternalInstancesAsync();
            if (inst.Count > 0)
            {
                r.Kind = PreflightKind.ExternalInstances;
                r.Instances = inst;
                r.Message = Loc.T("dsh.externalFound", inst.Count);
            }
            return r;
        }

        /// <summary>启动 dsh web。返回是否成功进入 Running；失败时状态为 Error。</summary>
        public async Task<bool> StartAsync(int port, bool randomPort, string trustedHosts, string nodeExe, string binJs, string envPath)
        {
            await _opLock.WaitAsync();
            try
            {
                if (_state == DshState.Running || _state == DshState.Starting)
                {
                    Log(Loc.T("dsh.alreadyRunning"));
                    return true;
                }
                SetState(DshState.Starting);
                Url = null;
                ProcessId = null;
                _stoppingByUs = false;

                int actualPort = randomPort ? 0 : port;
                var args = ProcessRunner.Quote(binJs) + " web --port " + actualPort;
                if (!string.IsNullOrEmpty(trustedHosts))
                {
                    foreach (var h in trustedHosts.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        args += " --trusted-host " + ProcessRunner.Quote(h.Trim());
                }
                Log(Loc.T("dsh.starting", nodeExe, args));

                var urlHolder = new string[1];
                var exitedTcs = new TaskCompletionSource<bool>();
                Process proc;
                try
                {
                    proc = ProcessRunner.Start(new ProcessSpec
                    {
                        FileName = nodeExe,
                        Arguments = args,
                        Environment = new Dictionary<string, string> { { "Path", envPath } }
                    },
                    line => OnProcLine(line, urlHolder),
                    line => { Log("[stderr] " + line); });
                }
                catch (Exception ex)
                {
                    Log(Loc.T("dsh.startFail", ex.Message));
                    SetState(DshState.Error);
                    return false;
                }
                _proc = proc;
                proc.EnableRaisingEvents = true;
                proc.Exited += (s, e) =>
                {
                    exitedTcs.TrySetResult(true);
                    // Running 状态下的非主动退出（如用户从任务管理器结束进程）→ 通知 UI 复位
                    if (!_stoppingByUs && _state == DshState.Running)
                    {
                        var code = SafeExitCode(proc);
                        _proc = null;
                        Url = null;
                        ProcessId = null;
                        SetState(DshState.Idle);
                        Log(Loc.T("dsh.exited", code));
                        try { Exited?.Invoke(Loc.T("dsh.exitedMsg", code)); } catch { }
                    }
                };
                ProcessId = proc.Id;

                // 1) 等 URL 输出行（loader settle 后才打印；冷启动可能较久，上限 120s）
                var deadline = DateTime.UtcNow.AddSeconds(120);
                while (DateTime.UtcNow < deadline && urlHolder[0] == null && !proc.HasExited)
                    await Task.Delay(250);

                string url = urlHolder[0];
                int probePort = actualPort;
                if (url != null)
                {
                    // urlHolder 存的是裸 URL（无 "dsh web: " 前缀），直接取尾部端口
                    var pm = Regex.Match(url, @":(\d+)$");
                    if (pm.Success) probePort = int.Parse(pm.Groups[1].Value);
                }

                // 2) 健康探测（URL 已解析则探测该端口；固定端口未解析则探测固定端口）
                bool healthy = false;
                if (probePort != 0)
                {
                    if (url != null) Log(Loc.T("dsh.urlParsed", url));
                    healthy = await ProbeHealthyAsync(probePort, deadline);
                }
                else
                {
                    Log(Loc.T("dsh.urlUnresolved"));
                }

                if (proc.HasExited && !healthy)
                {
                    Log(Loc.T("dsh.earlyExit", SafeExitCode(proc)));
                    _proc = null;
                    ProcessId = null;
                    SetState(DshState.Error);
                    return false;
                }
                if (!healthy)
                {
                    Log(Loc.T("dsh.notReady"));
                    StopLocked();
                    SetState(DshState.Error);
                    return false;
                }

                Url = url ?? ("http://127.0.0.1:" + probePort);
                SetState(DshState.Running);
                Log(Loc.T("dsh.ready", Url));
                try { Ready?.Invoke(Url); } catch { } // 订阅者异常隔离
                return true;
            }
            finally
            {
                _opLock.Release();
            }
        }

        /// <summary>停止 dsh（杀进程树）。</summary>
        public async Task StopAsync()
        {
            await _opLock.WaitAsync();
            try { StopLocked(); }
            finally { _opLock.Release(); }
        }

        void StopLocked()
        {
            var p = _proc;
            if (p == null || p.HasExited)
            {
                _proc = null;
                Url = null;
                ProcessId = null;
                if (_state != DshState.Idle) SetState(DshState.Idle);
                return;
            }
            _stoppingByUs = true;
            SetState(DshState.Stopping);
            Log(Loc.T("dsh.stop", p.Id));
            ProcessRunner.KillTree(p);
            try { p.WaitForExit(5000); } catch { }
            _proc = null;
            Url = null;
            ProcessId = null;
            SetState(DshState.Idle);
            Log(Loc.T("dsh.stopped"));
        }

        void OnProcLine(string line, string[] urlHolder)
        {
            Log(line);
            if (urlHolder[0] == null)
            {
                var m = UrlRx.Match(line);
                if (m.Success) urlHolder[0] = "http://127.0.0.1:" + m.Groups[1].Value;
            }
        }

        static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }

        static async Task<bool> ProbeHealthyAsync(int port, DateTime deadline)
        {
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var resp = await http.GetAsync("http://127.0.0.1:" + port + "/");
                        if (resp.IsSuccessStatusCode) return true;
                    }
                    catch { }
                    await Task.Delay(1000);
                }
                return false;
            }
        }
    }
}
