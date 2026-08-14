using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DshNotifyicon.Services;

namespace DshNotifyicon
{
    /// <summary>
    /// 主窗口：环境 / 服务 / 设置 三页。关闭即隐藏到托盘；所有操作经服务层异步执行。
    /// </summary>
    public partial class MainWindow : Window
    {
        bool _allowClose;
        const int MaxLogLines = 2000;

        // 长操作（安装/更新）状态
        CancellationTokenSource _opCts;
        bool _opActive;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ── 窗口生命周期 ──

        public void ShowOrActivate()
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(ShowOrActivate)); } catch { return; }
                return;
            }
            try
            {
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
            }
            catch { }
        }

        public void ShowEnvTab()
        {
            ShowOrActivate();
            tabMain.SelectedIndex = 0;
            _ = RunEnvCheckAsync();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsIntoUi();
            FillLogFromSnapshot();
            txtAboutVersion.Text = "版本 " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) +
                                   " · " + (Environment.Is64BitProcess ? "x64" : "x86") + " · .NET Framework 4.6.2";
            _ = RunEnvCheckAsync();
        }

        void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开链接失败: " + ex.Message, "DSH 托盘助手", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            e.Handled = true;
        }

        // ── 环境页 ──

        async void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            await RunEnvCheckAsync();
        }

        async Task RunEnvCheckAsync()
        {
            try
            {
                btnCheck.IsEnabled = false;
                txtCheckStatus.Text = "正在检查…";
                var s = App.Services.Settings;
                var items = await EnvironmentCheckService.CheckAllAsync(s, App.Services.EnvPath, line => SetCheckStatus(line));
                ApplyEnvItems(items);
                txtCheckStatus.Text = "检查完成";
            }
            catch (Exception ex)
            {
                txtCheckStatus.Text = "检查失败: " + ex.Message;
            }
            finally
            {
                btnCheck.IsEnabled = true;
            }
        }

        void SetCheckStatus(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(() => SetCheckStatus(line))); } catch { return; }
                return;
            }
            try { txtCheckStatus.Text = line; } catch { }
        }

        void ApplyEnvItems(System.Collections.Generic.List<EnvItem> items)
        {
            foreach (var item in items)
            {
                var detail = item.Detail;
                switch (item.Name)
                {
                    case "Node.js":
                        txtNode.Text = StatusPrefix(item.Status) + detail;
                        btnInstallNode.Visibility = item.Status == EnvStatus.Missing ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    case "npm 镜像源":
                        txtRegistry.Text = StatusPrefix(item.Status) + detail;
                        break;
                    case "dsh":
                        txtDsh.Text = StatusPrefix(item.Status) + detail;
                        btnInstallDsh.Content = item.Status == EnvStatus.Missing ? "安装 dsh" : "更新 dsh";
                        break;
                }
            }
        }

        static string StatusPrefix(EnvStatus s)
        {
            switch (s)
            {
                case EnvStatus.Ok: return "✓ ";
                case EnvStatus.Missing: return "✗ ";
                case EnvStatus.Outdated: return "↑ ";
                default: return "! ";
            }
        }

        async void BtnInstallNode_Click(object sender, RoutedEventArgs e)
        {
            if (_opActive) { CancelOp(); return; }
            await RunLongOpAsync(
                "安装 Node.js",
                btnInstallNode, null,
                async (log, ct) =>
                {
                    var ok = await NodeService.InstallNodeAsync(log, ct);
                    if (!ok) throw new Exception("安装未成功，详见日志（可手动从 nodejs.org 下载安装）");
                    App.Services.RefreshEnvPath();
                    log("安装完成，正在验证…");
                    // 轮询检测（最多 ~15s），确保新安装的 node 立即可见（走刷新后的 PATH）
                    var s = App.Services.Settings;
                    var node = await NodeService.DetectAsync(s.NodePath, App.Services.EnvPath);
                    for (int i = 0; i < 10 && node.NodeExe == null; i++)
                    {
                        await Task.Delay(1500);
                        node = await NodeService.DetectAsync(s.NodePath, App.Services.EnvPath);
                    }
                    log(node.NodeExe != null
                        ? "检测到 Node.js: " + node.NodeVersion
                        : "未检测到可执行文件（可尝试重新体检或检查安装路径）");
                },
                RunEnvCheckAsync);
        }

        void CmbMirror_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            txtCustomMirror.IsEnabled = cmbMirror.SelectedIndex == 2;
        }

        string SelectedMirrorUrl()
        {
            switch (cmbMirror.SelectedIndex)
            {
                case 1: return "https://registry.npmmirror.com";
                case 2: return (txtCustomMirror.Text ?? "").Trim();
                default: return "";
            }
        }

        async void BtnApplyMirror_Click(object sender, RoutedEventArgs e)
        {
            var url = SelectedMirrorUrl();
            if (cmbMirror.SelectedIndex == 2 && url.Length == 0)
            {
                MessageBox.Show("请输入自定义 registry URL", "DSH 托盘助手", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            App.Services.Settings.MirrorUrl = url;
            SettingsService.Save(App.Services.Settings);
            txtCheckStatus.Text = "已应用镜像: " + (url.Length == 0 ? "跟随 npm 全局配置" : url);
            await RunEnvCheckAsync();
        }

        async void BtnGlobalRegistry_Click(object sender, RoutedEventArgs e)
        {
            var url = SelectedMirrorUrl();
            if (url.Length == 0)
            {
                MessageBox.Show("请先选择或输入一个镜像源", "DSH 托盘助手", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var r = MessageBox.Show(
                "将 registry " + url + " 写入全局 npmrc（影响你所有 npm 命令）。\n\n确定继续？",
                "写入全局 npmrc", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                await NpmService.SetGlobalRegistryAsync(url, App.Services.EnvPath, line => SetCheckStatus(line));
                txtCheckStatus.Text = "已写入全局 npmrc";
            }
            catch (Exception ex)
            {
                txtCheckStatus.Text = "写入失败: " + ex.Message;
            }
            await RunEnvCheckAsync();
        }

        async void BtnInstallDsh_Click(object sender, RoutedEventArgs e)
        {
            if (_opActive) { CancelOp(); return; }
            await RunLongOpAsync(
                "安装 / 更新 dsh",
                btnInstallDsh, btnCheckUpdate,
                async (log, ct) =>
                {
                    await NpmService.InstallOrUpdateDshAsync(App.Services.Settings.MirrorUrl, App.Services.EnvPath, log, ct);
                    var v = await NpmService.GetDshLocalVersionAsync(App.Services.EnvPath);
                    log("已安装版本: " + v);
                },
                RunEnvCheckAsync);
        }

        // ── 长操作统一交互（安装/更新）──

        void CancelOp()
        {
            try { if (_opCts != null) _opCts.Cancel(); } catch { }
        }

        /// <summary>
        /// 长操作统一交互：自动切到日志面板（logTab，默认服务页）透传原始输出（滚动+限行）、
        /// 环境页显示不确定进度条与截断状态行、操作按钮变为"取消"（点击即杀进程树）、
        /// 完成后若用户未手动切走标签页则回到 returnTab（默认环境页），再执行 after（通常为重新体检）。
        /// </summary>
        async Task RunLongOpAsync(string title, Button busyButton, Button disableButton,
            Func<Action<string>, CancellationToken, Task> op, Func<Task> after = null,
            int logTab = 1, int returnTab = 0)
        {
            if (_opActive) return;
            _opActive = true;
            _opCts = new CancellationTokenSource();
            var ct = _opCts.Token;
            var origContent = busyButton != null ? busyButton.Content : null;

            Action<string> progressSink = line =>
            {
                TraceLog(line);
                var s = line.Length > 80 ? line.Substring(0, 80) + "…" : line;
                if (txtCheckStatus.Text != s) txtCheckStatus.Text = s;
            };

            txtCheckStatus.Text = title + "…";
            if (busyButton != null) busyButton.Content = "取消";
            if (disableButton != null) disableButton.IsEnabled = false;
            prgOp.Visibility = Visibility.Visible;
            TraceLog("════ " + title + " ════");
            tabMain.SelectedIndex = logTab; // 日志面板透传进度

            bool succeeded = false;
            try
            {
                await op(progressSink, ct);
                succeeded = true;
                TraceLog("✔ " + title + "完成");
                txtCheckStatus.Text = title + "完成";
                if (tabMain.SelectedIndex == logTab) tabMain.SelectedIndex = returnTab; // 用户未手动切走则回目标页
            }
            catch (OperationCanceledException)
            {
                TraceLog("✖ " + title + "已取消");
                txtCheckStatus.Text = title + "已取消";
            }
            catch (Exception ex)
            {
                TraceLog("✖ " + title + "失败: " + ex.Message);
                txtCheckStatus.Text = title + "失败";
            }
            finally
            {
                _opActive = false;
                _opCts = null;
                if (busyButton != null) busyButton.Content = origContent;
                if (disableButton != null) disableButton.IsEnabled = true;
                prgOp.Visibility = Visibility.Collapsed;
            }
            if (succeeded && after != null) await after();
        }

        async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await RunEnvCheckAsync();
        }

        // ── 服务页 ──

        async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            await StartCoreAsync(true);
        }

        async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            await App.Services.Dsh.StopAsync();
        }

        async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            await App.Services.Dsh.StopAsync();
            await StartCoreAsync(true);
        }

        async void BtnOpenUi_Click(object sender, RoutedEventArgs e)
        {
            OpenUiFromTray();
        }

        /// <summary>启动核心流程：校验 → 前置检查（对话框）→ StartAsync。供按钮与托盘共用。</summary>
        async Task<bool> StartCoreAsync(bool withPreflight)
        {
            try
            {
                var s = App.Services.Settings;

                // 窗口不可见（托盘启动）时直接用持久化设置，避免控件未初始化导致的误报弹窗；
                // 窗口可见时以输入框为准并即时持久化，托盘后续启动保持一致。
                int port = s.Port;
                bool random = s.RandomPort;
                if (IsLoaded)
                {
                    random = chkRandomPort.IsChecked == true;
                    if (!random)
                    {
                        if (!int.TryParse(txtPort.Text.Trim(), out port) || port < 1 || port > 65535)
                        {
                            Ask("端口必须是 1-65535 之间的整数", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                    }
                    s.Port = port;
                    s.RandomPort = random;
                    SettingsService.Save(s);
                }
                if (port < 1 || port > 65535)
                {
                    Ask("设置中的端口无效（应为 1-65535 的整数），请在设置页修改", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var node = await NodeService.DetectAsync(s.NodePath);
                if (node.NodeExe == null)
                {
                    Ask("未检测到 Node.js，请先到环境页一键安装", MessageBoxButton.OK, MessageBoxImage.Information);
                    tabMain.SelectedIndex = 0;
                    return false;
                }
                var binJs = await App.Services.DshBinJsAsync();
                if (binJs == null)
                {
                    Ask("未检测到 dsh，请先到环境页安装", MessageBoxButton.OK, MessageBoxImage.Information);
                    tabMain.SelectedIndex = 0;
                    return false;
                }

                if (withPreflight)
                {
                    var pre = await App.Services.Dsh.PreflightAsync(port, random);
                    if (pre.Kind == PreflightKind.PortBusy)
                    {
                        var r = Ask(pre.Message + "。\n\n是 = 直接打开浏览器访问该端口\n否 = 取消",
                            MessageBoxButton.YesNo, MessageBoxImage.Question, "端口被占用");
                        if (r == MessageBoxResult.Yes) App.Services.OpenUrl("http://127.0.0.1:" + port);
                        return false;
                    }
                    if (pre.Kind == PreflightKind.ExternalInstances)
                    {
                        var detail = "";
                        foreach (var inst in pre.Instances) detail += "  PID " + inst.Pid + "\n";
                        var r = Ask(
                            pre.Message + "：\n" + detail +
                            "\n是 = 停止这些实例并启动新实例\n否 = 仅打开浏览器\n取消 = 放弃",
                            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, "检测到其他 dsh 实例");
                        if (r == MessageBoxResult.No)
                        {
                            App.Services.OpenUrl("http://127.0.0.1:" + (random ? 3080 : port));
                            return false;
                        }
                        if (r == MessageBoxResult.Cancel) return false;
                        foreach (var inst in pre.Instances)
                        {
                            await KillExternalAsync(inst.Pid);
                        }
                    }
                }

                bool ok = await App.Services.Dsh.StartAsync(port, random, s.TrustedHosts, node.NodeExe, binJs, App.Services.EnvPath);
                return ok;
            }
            catch (Exception ex)
            {
                Ask("启动失败: " + ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 弹窗助手：窗口尚未显示（托盘启动）时先显示并激活主窗口作为 owner，
        /// 避免无主弹窗在 Windows 11 上一闪而过或跑到其他窗口后面。
        /// </summary>
        MessageBoxResult Ask(string text, MessageBoxButton buttons, MessageBoxImage icon, string title = "DSH 托盘助手")
        {
            if (!IsLoaded) ShowOrActivate();
            return MessageBox.Show(this, text, title, buttons, icon);
        }

        static async Task KillExternalAsync(int pid)
        {
            try
            {
                await ProcessRunner.RunAsync(new ProcessSpec
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + pid + " /T /F",
                    TimeoutMs = 15000
                }, CancellationToken.None, null);
            }
            catch { }
        }

        /// <summary>服务状态刷新（任意线程可调）。</summary>
        public void UpdateServiceState(DshState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(() => UpdateServiceState(state))); } catch { return; }
                return;
            }
            try
            {
                bool running = state == DshState.Running;
                bool busy = state == DshState.Starting || state == DshState.Stopping;
                btnStart.IsEnabled = !running && !busy;
                btnStop.IsEnabled = running || state == DshState.Starting;
                btnRestart.IsEnabled = running;
                btnOpenUi.IsEnabled = running || App.Services.Dsh.Url != null;

                switch (state)
                {
                    case DshState.Running:
                        txtServiceStatus.Text = "● 运行中: " + App.Services.Dsh.Url;
                        break;
                    case DshState.Starting:
                        txtServiceStatus.Text = "… 正在启动（首次启动需初始化 profile，可能较慢，请观察日志）";
                        break;
                    case DshState.Stopping:
                        txtServiceStatus.Text = "… 正在停止";
                        break;
                    case DshState.Error:
                        txtServiceStatus.Text = "✗ 启动失败（查看下方日志定位原因）";
                        App.Services.Tray.ShowBalloon("DSH 启动失败", "请查看主窗口日志面板");
                        break;
                    default:
                        txtServiceStatus.Text = "○ 未运行";
                        break;
                }
            }
            catch { }
        }

        // ── 日志 ──

        volatile bool _logQueued;

        /// <summary>
        /// 日志追加（任意线程可调）。突发输出合并去重：跨线程时若已有待处理的追加则丢弃本次，
        /// 防止安装/下载输出洪峰把 Dispatcher 队列塞爆导致界面卡死。
        /// </summary>
        public void TraceLog(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                if (_logQueued) return;
                _logQueued = true;
                try { Dispatcher.BeginInvoke(new Action(() => { _logQueued = false; TraceLog(line); })); }
                catch { _logQueued = false; }
                return;
            }
            if (!IsLoaded) return;
            try
            {
                txtLog.AppendText(line + Environment.NewLine);
                TrimLog();
                txtLog.ScrollToEnd();
            }
            catch { }
        }

        void FillLogFromSnapshot()
        {
            var snap = App.Services.Dsh.SnapshotLog();
            if (snap.Length == 0) return;
            txtLog.AppendText(string.Join(Environment.NewLine, snap) + Environment.NewLine);
            TrimLog();
            txtLog.ScrollToEnd();
        }

        void TrimLog()
        {
            var t = txtLog.Text;
            int count = 0, cut = -1;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '\n')
                {
                    count++;
                    if (count > MaxLogLines) { cut = i + 1; }
                }
            }
            if (cut > 0) txtLog.Text = t.Substring(cut);
        }

        // ── 托盘入口 ──

        public void StartFromTray() { _ = StartCoreAsync(true); }
        public void StopFromTray() { _ = App.Services.Dsh.StopAsync(); }
        public void RestartFromTray() { RestartFromTrayAsync(); }
        async void RestartFromTrayAsync()
        {
            await App.Services.Dsh.StopAsync();
            await StartCoreAsync(true);
        }

        public void OpenUiFromTray()
        {
            var url = App.Services.Dsh.Url;
            if (string.IsNullOrEmpty(url))
            {
                var s = App.Services.Settings;
                if (!s.RandomPort && s.Port > 0 && s.Port <= 65535) url = "http://127.0.0.1:" + s.Port;
            }
            if (string.IsNullOrEmpty(url))
                App.Services.Tray.ShowBalloon("DSH 未运行", "请先启动 DSH 服务");
            else
                App.Services.OpenUrl(url);
        }

        public void CopyUrlFromTray()
        {
            var url = App.Services.Dsh.Url;
            if (string.IsNullOrEmpty(url))
            {
                var s = App.Services.Settings;
                if (!s.RandomPort && s.Port > 0 && s.Port <= 65535) url = "http://127.0.0.1:" + s.Port;
            }
            if (string.IsNullOrEmpty(url))
                App.Services.Tray.ShowBalloon("DSH 未运行", "请先启动 DSH 服务");
            else
                App.Services.CopyUrl(url);
        }

        // ── 设置页 ──

        void LoadSettingsIntoUi()
        {
            var s = App.Services.Settings;
            txtPort.Text = s.Port.ToString();
            txtPort.IsEnabled = !s.RandomPort;
            chkRandomPort.IsChecked = s.RandomPort;
            chkAutoOpen.IsChecked = s.AutoOpenBrowser;
            chkAutoStart.IsChecked = s.AutoStartOnLogin;
            chkShowMain.IsChecked = s.ShowMainWindowOnStartup;
            txtTrustedHosts.Text = s.TrustedHosts;
            txtNodePath.Text = s.NodePath;
            if (s.MirrorUrl == "https://registry.npmmirror.com") cmbMirror.SelectedIndex = 1;
            else if (s.MirrorUrl.Length > 0) { cmbMirror.SelectedIndex = 2; txtCustomMirror.Text = s.MirrorUrl; }
            else cmbMirror.SelectedIndex = 0;
        }

        void ChkRandomPort_Changed(object sender, RoutedEventArgs e)
        {
            txtPort.IsEnabled = chkRandomPort.IsChecked != true;
        }

        async void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Services.Settings;
            int port;
            if (!int.TryParse(txtPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                MessageBox.Show("端口必须是 1-65535 之间的整数", "DSH 托盘助手", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            s.Port = port;
            s.RandomPort = chkRandomPort.IsChecked == true;
            s.AutoOpenBrowser = chkAutoOpen.IsChecked == true;
            s.ShowMainWindowOnStartup = chkShowMain.IsChecked == true;
            s.TrustedHosts = (txtTrustedHosts.Text ?? "").Trim();
            s.NodePath = (txtNodePath.Text ?? "").Trim();
            bool autoStartChanged = s.AutoStartOnLogin != (chkAutoStart.IsChecked == true);
            s.AutoStartOnLogin = chkAutoStart.IsChecked == true;
            if (autoStartChanged) App.Services.ToggleAutoStart(s.AutoStartOnLogin);
            SettingsService.Save(s);
            txtCheckStatus.Text = "设置已保存";
            MessageBox.Show("设置已保存", "DSH 托盘助手", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void BtnOpenSettingsDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(SettingsService.SettingsDir);
                Process.Start("explorer.exe", SettingsService.SettingsDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败: " + ex.Message, "DSH 托盘助手", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 清理 dsh 环境：停止 dsh → npm 卸载全局包 → 数据目录改名备份（含凭据）→ 移除开机自启。
        /// 全程确认 + 日志透传；不卸载 Node.js。
        /// </summary>
        async void BtnCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (_opActive) { CancelOp(); return; }
            var r = Ask(
                "将执行以下清理：\n\n" +
                "1. 停止正在运行的 dsh 服务\n" +
                "2. 卸载全局 npm 包 @deepseek-ai/dsh\n" +
                "3. 将数据目录（含 API 凭据与会话记录）重命名备份为 .dsh.bak-日期（不直接删除，可恢复）\n" +
                "4. 移除本工具的开机自启项\n\n" +
                "不会卸载 Node.js。确定继续？",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, "清理 dsh 环境");
            if (r != MessageBoxResult.Yes) return;

            await RunLongOpAsync("清理 dsh 环境", btnCleanup, null, async (log, ct) =>
            {
                // 1. 停止 dsh
                if (App.Services.Dsh.State != DshState.Idle)
                {
                    log("停止 dsh 服务…");
                    await App.Services.Dsh.StopAsync();
                }
                else
                {
                    log("dsh 未在运行，跳过停止");
                }

                // 2. 卸载全局 npm 包
                log("卸载全局包 @deepseek-ai/dsh…");
                await NpmService.UninstallDshAsync(App.Services.EnvPath, log, ct);
                log("npm 全局包已卸载");

                // 3. 数据目录改名备份（尊重 DSH_HOME 覆盖）
                var home = Environment.GetEnvironmentVariable("DSH_HOME");
                if (string.IsNullOrEmpty(home))
                    home = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
                if (System.IO.Directory.Exists(home))
                {
                    var bak = home + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    log("数据目录备份为: " + bak + "（含凭据，确认不再需要后可手动删除）");
                    System.IO.Directory.Move(home, bak);
                }
                else
                {
                    log("未发现数据目录 " + home + "，跳过备份");
                }

                // 4. 移除开机自启
                if (App.Services.Settings.AutoStartOnLogin) App.Services.ToggleAutoStart(false);
                log("清理完成。本工具删除自身文件夹即卸载；设置目录可一并删除: " + SettingsService.SettingsDir);
            }, RunEnvCheckAsync, logTab: 1, returnTab: 2);
        }
    }
}
