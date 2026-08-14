using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DshNotifyicon.Services;

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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
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

            SessionEnding += (s, se) => StopDshSync();

            DispatcherUnhandledException += (s, args) =>
            {
                try { Services.Tray.ShowBalloon("DSH 托盘助手", "发生错误: " + args.Exception.Message); } catch { }
                args.Handled = true;
            };

            if (firstRun || settings.ShowMainWindowOnStartup)
                Services.Main.ShowOrActivate();
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
                Services.Tray.ShowBalloon("DSH 已启动", "Web UI: " + url);
                if (Services.Settings.AutoOpenBrowser) Services.OpenUrl(url);
            };
            Services.Dsh.Exited += info =>
            {
                Services.Tray.SetState(DshState.Idle, null);
                Services.Tray.ShowBalloon("DSH 已退出", info);
            };
            Services.Dsh.LogLine += line => Services.Main.TraceLog(line);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (Services != null)
            {
                SettingsService.Save(Services.Settings);
                // 兜底：正常退出路径应已由 ExitWithDsh 停止 dsh（StopAsync 幂等，未运行时是 no-op）
                StopDshSync();
                if (Services.Tray != null) Services.Tray.Dispose();
            }
            base.OnExit(e);
        }

        /// <summary>
        /// 托盘"退出"：先关闭 dsh 服务再退出，避免遗留用户难以清理的 node 进程。
        /// 限时等待（启动流程进行中可能暂时拿不到互斥锁）；超时直接退出，
        /// 残留实例会在下次启动时被外部实例扫描发现并提示处理。
        /// </summary>
        void ExitWithDsh()
        {
            try
            {
                Services.Main.ForceClose();
                StopDshSync();
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
