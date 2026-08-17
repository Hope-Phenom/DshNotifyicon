using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DshNotifyicon.Services;
using Newtonsoft.Json.Linq;

namespace DshNotifyicon
{
    /// <summary>
    /// 应用入口：单实例（Mutex + EventWaitHandle 激活已有实例）、托盘生命周期、
    /// DSH 状态事件接线、--smoke 无 UI 冒烟模式（用于自动化验证）。
    /// </summary>
    public partial class App : Application
    {
        public static AppServices Services;

        const string MutexName = "DshNotifyicon_SingleInstance";
        const string SignalName = "DshNotifyicon_ShowSignal";

        Mutex _mutex;
        EventWaitHandle _showSignal;
        bool _smoke;
        bool _exitStopDone;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 全局兜底：任何线程的未处理异常都落盘（配合 legacyUnhandledExceptionPolicy，
            // 线程池异常不再直接杀死进程），便于后续诊断迭代
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                WriteCrashLog(args.ExceptionObject as Exception, "AppDomain");

            _smoke = e.Args != null && Array.IndexOf(e.Args, "--smoke") >= 0;

            if (_smoke)
            {
                RunSmoke();
                return;
            }

            bool firstRun = !File.Exists(SettingsService.SettingsPath);
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                try { EventWaitHandle.OpenExisting(SignalName).Set(); } catch { }
                Shutdown();
                return;
            }
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            var watcher = new Thread(() =>
            {
                while (_showSignal.WaitOne())
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() => Services.Main.ShowOrActivate()));
                    }
                    catch { }
                }
            });
            watcher.IsBackground = true;
            watcher.Start();

            var settings = SettingsService.Load();
            // 界面语言：auto = 跟随系统；显式 zh/en 覆盖。在创建任何 UI 前应用。
            Loc.Apply(settings.Language);
            Services = new AppServices(settings);

            var actions = new TrayActions
            {
                Start = () => Services.Main.StartFromTray(),
                Stop = () => Services.Main.StopFromTray(),
                Restart = () => Services.Main.RestartFromTray(),
                OpenUi = () => Services.Main.OpenUiFromTray(),
                CopyUrl = () => Services.Main.CopyUrlFromTray(),
                ShowWindow = () => Services.Main.ShowOrActivate(),
                ShowEnv = () => Services.Main.ShowEnvTab(),
                Exit = () => ExitWithDsh(),
                ToggleAutoStart = (v) => Services.ToggleAutoStart(v)
            };
            Services.Tray = new TrayIcon(actions, settings);

            WireDshEvents();

            if (settings.AutoStartDshOnLaunch)
            {
                Dispatcher.BeginInvoke(new Action(() => Services.Main.StartFromTray()));
            }

            SessionEnding += (s, se) => StopDshSync();

            DispatcherUnhandledException += (s, args) =>
            {
                WriteCrashLog(args.Exception, "Dispatcher");
                try { Services.Tray.ShowBalloon(Loc.T("app.name"), Loc.T("app.errBalloon", args.Exception.Message)); } catch { }
                args.Handled = true;
            };

            // 上次崩溃（24h 内）留下日志时，启动后提示一次，便于配合排查
            try
            {
                var dir = SettingsService.SettingsDir;
                if (Directory.Exists(dir))
                {
                    var recent = Directory.GetFiles(dir, "crash-*.log")
                        .Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddHours(-24)).ToArray();
                    if (recent.Length > 0)
                        Dispatcher.BeginInvoke(new Action(() =>
                            Services.Tray.ShowBalloon(Loc.T("app.name"),
                                Loc.T("app.crashRecent", string.Join("; ", recent.Select(f => System.IO.Path.GetFileName(f)))))));
                }
            }
            catch { }

            if (firstRun || settings.ShowMainWindowOnStartup)
                Services.Main.ShowOrActivate();
        }

        /// <summary>崩溃/异常落盘：异常详情 + 最近日志快照 → %APPDATA%\DshNotifyicon\crash-*.log，并托盘提示路径。</summary>
        void WriteCrashLog(Exception ex, string source)
        {
            try
            {
                var dir = SettingsService.SettingsDir;
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                var sb = new StringBuilder();
                sb.AppendLine("[source] " + source);
                sb.AppendLine("[time] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("[exception] " + (ex != null ? ex.ToString() : "null"));
                sb.AppendLine("[version] " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                if (Services != null)
                {
                    sb.AppendLine("[recent-log]");
                    try { foreach (var l in Services.Dsh.SnapshotLog()) sb.AppendLine(l); } catch { }
                }
                File.WriteAllText(path, sb.ToString());
                try
                {
                    if (Services != null && Services.Tray != null)
                        Services.Tray.ShowBalloon(Loc.T("app.name"), Loc.T("app.crashInternal", path));
                }
                catch { }
            }
            catch { }
        }

        void WireDshEvents()
        {
            Services.Dsh.StateChanged += state =>
            {
                Services.Tray.SetState(state, Services.Dsh.Url);
                Services.Main.UpdateServiceState(state);
            };
            Services.Dsh.Ready += url =>
            {
                Services.Tray.SetState(DshState.Running, url);
                Services.Tray.ShowBalloon(Loc.T("app.startedTitle"), Loc.T("app.startedText", url));
                if (Services.Settings.AutoOpenBrowser) Services.OpenUrl(url);
            };
            Services.Dsh.Exited += info =>
            {
                Services.Tray.SetState(DshState.Idle, null);
                Services.Tray.ShowBalloon(Loc.T("app.exitedTitle"), info);
            };
            Services.Dsh.LogLine += line => Services.Main.TraceLog(line);
            Services.Dsh.Notification += OnDshNotification;
        }

        /// <summary>处理 dsh 通知增强插件输出。</summary>
        void OnDshNotification(string json)
        {
            try
            {
                var s = Services.Settings;
                if (!s.EnableNotifications) return;

                var obj = JObject.Parse(json);
                var sessionId = (string)obj["sessionId"] ?? "";
                var parentSessionId = (string)obj["parentSessionId"];
                var turn = obj["turn"] != null ? obj["turn"].Value<int>() : 0;
                var reason = (string)obj["reason"] ?? "unknown";
                var durationMs = obj["durationMs"] != null ? obj["durationMs"].Value<long>() : 0L;
                var sessionTitle = (string)obj["title"] ?? "";
                if (string.IsNullOrEmpty(sessionTitle)) sessionTitle = Loc.T("notify.untitled");

                var notifyTitle = parentSessionId == null
                    ? Loc.T("notify.turnEndTitle")
                    : Loc.T("notify.subTurnEndTitle");
                var text = parentSessionId == null
                    ? Loc.T("notify.turnEndText", sessionTitle, sessionId, turn, reason, FormatDuration(durationMs))
                    : Loc.T("notify.subTurnEndText", sessionTitle, sessionId, parentSessionId, turn, reason, FormatDuration(durationMs));

                if (s.EnableTrayNotification)
                    Services.Tray.ShowBalloon(notifyTitle, text);

                if (s.EnableExternalHook && !string.IsNullOrWhiteSpace(s.ExternalHookCommand))
                {
                    var args = ExpandHookTemplate(s.ExternalHookArguments, obj);
                    _ = RunExternalHookAsync(s.ExternalHookCommand, args);
                }
            }
            catch (Exception ex)
            {
                Services.Main.TraceLog(Loc.T("notify.parseFail", ex.Message));
            }
        }

        async Task RunExternalHookAsync(string command, string arguments)
        {
            try
            {
                await ProcessRunner.RunAsync(new ProcessSpec
                {
                    FileName = command,
                    Arguments = arguments,
                    TimeoutMs = 30000
                }, CancellationToken.None, null);
            }
            catch (Exception ex)
            {
                Services.Main.TraceLog(Loc.T("notify.hookFail", ex.Message));
            }
        }

        static string ExpandHookTemplate(string template, JObject data)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var turn = data["turn"];
            var durationMs = data["durationMs"];
            var s = template
                .Replace("{event}", (string)data["event"] ?? "")
                .Replace("{title}", (string)data["title"] ?? "")
                .Replace("{sessionId}", (string)data["sessionId"] ?? "")
                .Replace("{parentSessionId}", (string)data["parentSessionId"] ?? "")
                .Replace("{turn}", turn == null ? "" : turn.ToString())
                .Replace("{reason}", (string)data["reason"] ?? "")
                .Replace("{durationMs}", durationMs == null ? "" : durationMs.ToString());
            return s;
        }

        static string FormatDuration(long ms)
        {
            if (ms < 1000) return ms + " ms";
            return (ms / 1000.0).ToString("0.0") + " s";
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (Services != null)
            {
                SettingsService.Save(Services.Settings);
                // 如果 ExitWithDsh 已经处理过停止，则不再阻塞等待；
                // 其他退出路径（系统注销/异常退出）仍走兜底 StopDshSync。
                if (!_exitStopDone) StopDshSync();
                if (Services.Tray != null) Services.Tray.Dispose();
            }
            base.OnExit(e);
        }

        /// <summary>
        /// 托盘"退出"：先关闭 dsh 服务再退出，避免遗留用户难以清理的 node 进程。
        /// 异步等待避免阻塞 UI 线程导致“卡死”；超时后也继续退出，
        /// 残留实例会在下次启动时被外部实例扫描发现并提示处理。
        /// </summary>
        async void ExitWithDsh()
        {
            try
            {
                Services.Main.ForceClose();
                await Task.WhenAny(Services.Dsh.StopAsync(), Task.Delay(TimeSpan.FromSeconds(8)));
                _exitStopDone = true;
                Shutdown();
            }
            catch
            {
                try { Environment.Exit(0); } catch { }
            }
        }

        /// <summary>限时停止 dsh（退出/注销时用；子进程独立，不会随本进程消失）。</summary>
        void StopDshSync()
        {
            try
            {
                Task.Run(() => Services.Dsh.StopAsync()).Wait(TimeSpan.FromSeconds(8));
            }
            catch { }
        }

        /// <summary>
        /// --smoke 模式：不创建托盘/窗口，执行环境检查 + dsh 真实启停，
        /// 结果写入 %TEMP%\DshNotifyiconSmoke.txt，退出码 0/1。
        /// 整体在 Task.Run 中执行（无 UI 同步上下文），内部可安全同步等待。
        /// </summary>
        void RunSmoke()
        {
            var sb = new StringBuilder();
            int exit;
            try
            {
                exit = Task.Run(() =>
                {
                    var b = new StringBuilder();
                    int code = 0;
                    try
                    {
                        var settings = new Settings();
                        var envPath = NodeService.RefreshPath();
                        b.AppendLine("== env check ==");
                        var node = NodeService.DetectAsync(settings.NodePath, envPath).GetAwaiter().GetResult();
                        b.AppendLine("nodeExe: " + (node.NodeExe ?? "MISSING"));
                        b.AppendLine("node: " + (node.NodeVersion ?? "?") + " npm: " + (node.NpmVersion ?? "?"));
                        var reg = NpmService.GetRegistryAsync("", envPath).GetAwaiter().GetResult();
                        b.AppendLine("registry: " + reg.Trim());
                        var local = NpmService.GetDshLocalVersionAsync(envPath).GetAwaiter().GetResult();
                        var latest = NpmService.GetDshLatestVersionAsync("", envPath).GetAwaiter().GetResult();
                        b.AppendLine("dsh local: " + (local.Length > 0 ? local : "MISSING") + " latest: " + latest.Trim());
                        var binJs = NpmService.ResolveDshBinJsAsync(envPath).GetAwaiter().GetResult();
                        b.AppendLine("dsh binJs: " + (binJs ?? "MISSING"));

                        b.AppendLine("== dsh start/stop (random port) ==");
                        var mgr = new DshProcessManager();
                        mgr.LogLine += line => b.AppendLine("  [dsh] " + line);
                        var pre = mgr.PreflightAsync(0, true).GetAwaiter().GetResult();
                        b.AppendLine("preflight: " + pre.Kind + " (instances=" + pre.Instances.Count + ")");
                        var started = mgr.StartAsync(0, true, "", node.NodeExe, binJs, envPath).GetAwaiter().GetResult();
                        b.AppendLine("start result: " + started + " url: " + mgr.Url);
                        if (!started) code = 1;
                        if (mgr.Url != null)
                        {
                            bool ok = HttpProbe(mgr.Url + "/");
                            b.AppendLine("http GET " + mgr.Url + "/ : " + ok);
                            if (!ok) code = 1;
                        }
                        mgr.StopAsync().GetAwaiter().GetResult();
                        b.AppendLine("stop done, state=" + mgr.State);
                        if (mgr.State != DshState.Idle) code = 1;
                        b.AppendLine("== SMOKE " + (code == 0 ? "PASS" : "FAIL") + " ==");
                    }
                    catch (Exception ex)
                    {
                        code = 1;
                        b.AppendLine("== SMOKE FAILED ==");
                        b.AppendLine(ex.ToString());
                    }
                    sb.Append(b.ToString());
                    return code;
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                exit = 1;
                sb.AppendLine("== SMOKE FAILED ==");
                sb.AppendLine(ex.ToString());
            }
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "DshNotifyiconSmoke.txt"), sb.ToString());
            }
            catch { }
            Environment.Exit(exit);
        }

        static bool HttpProbe(string url)
        {
            try
            {
                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(5);
                    var resp = http.GetAsync(url).GetAwaiter().GetResult();
                    return resp.IsSuccessStatusCode;
                }
            }
            catch { return false; }
        }
    }
}
