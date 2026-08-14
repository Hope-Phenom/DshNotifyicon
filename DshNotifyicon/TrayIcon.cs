using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DshNotifyicon.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace DshNotifyicon
{
    /// <summary>托盘菜单动作集合（由 App 装配到具体实现）。</summary>
    public class TrayActions
    {
        public Action Start;
        public Action Stop;
        public Action Restart;
        public Action OpenUi;
        public Action CopyUrl;
        public Action ShowWindow;
        public Action ShowEnv;
        public Action Exit;
        public Action<bool> ToggleAutoStart;
    }

    /// <summary>
    /// 托盘图标与上下文菜单（Hardcodet.NotifyIcon.Wpf 代码构建）。
    /// 状态切换会更新图标（运行态带绿点）、Tooltip 与菜单可用性。
    /// </summary>
    public class TrayIcon : IDisposable
    {
        readonly TaskbarIcon _tray;
        readonly MenuItem _startItem;
        readonly MenuItem _stopItem;
        readonly MenuItem _restartItem;
        readonly MenuItem _openItem;
        readonly MenuItem _copyItem;
        readonly MenuItem _autoStartItem;

        public TrayIcon(TrayActions a, Settings settings)
        {
            _tray = new TaskbarIcon();
            _tray.ToolTipText = "DSH 托盘助手";

            var menu = new ContextMenu();
            _startItem = Item("启动 DSH", a.Start);
            _stopItem = Item("停止 DSH", a.Stop);
            _restartItem = Item("重启 DSH", a.Restart);
            menu.Items.Add(_startItem);
            menu.Items.Add(_stopItem);
            menu.Items.Add(_restartItem);
            menu.Items.Add(Sep());
            _openItem = Item("打开 Web UI", a.OpenUi);
            _copyItem = Item("复制 URL", a.CopyUrl);
            menu.Items.Add(_openItem);
            menu.Items.Add(_copyItem);
            menu.Items.Add(Sep());
            menu.Items.Add(Item("环境体检", a.ShowEnv));
            menu.Items.Add(Item("打开主窗口", a.ShowWindow));
            menu.Items.Add(Sep());
            _autoStartItem = Item("开机自启", null);
            _autoStartItem.IsCheckable = true;
            _autoStartItem.IsChecked = settings.AutoStartOnLogin;
            _autoStartItem.Click += (s, e) => a.ToggleAutoStart(_autoStartItem.IsChecked);
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(Sep());
            menu.Items.Add(Item("退出", a.Exit));
            _tray.ContextMenu = menu;

            SetState(DshState.Idle, null);
        }

        static MenuItem Item(string header, Action click)
        {
            var m = new MenuItem { Header = header };
            if (click != null) m.Click += (s, e) => click();
            return m;
        }

        static Separator Sep() { return new Separator(); }

        /// <summary>更新托盘状态。可在任意线程调用（内部调度到 UI 线程）。</summary>
        public void SetState(DshState state, string url)
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                try { dispatcher.BeginInvoke(new Action(() => SetState(state, url))); } catch { return; }
                return;
            }
            try
            {
                bool running = state == DshState.Running;
                bool busy = state == DshState.Starting || state == DshState.Stopping;
                _startItem.IsEnabled = !running && !busy;
                _stopItem.IsEnabled = running || state == DshState.Starting;
                _restartItem.IsEnabled = running || state == DshState.Starting;
                _openItem.IsEnabled = running || url != null;
                _copyItem.IsEnabled = running || url != null;
                _tray.IconSource = running ? LoadIcon("app-running.ico") : LoadIcon("app.ico");
                _tray.ToolTipText = running
                    ? "DSH 运行中: " + url
                    : "DSH 托盘助手 — " + StateText(state);
            }
            catch { }
        }

        public void ShowBalloon(string title, string text)
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                try { dispatcher.BeginInvoke(new Action(() => ShowBalloon(title, text))); } catch { return; }
                return;
            }
            try { _tray.ShowBalloonTip(title, text, BalloonIcon.Info); } catch { }
        }

        static string StateText(DshState s)
        {
            switch (s)
            {
                case DshState.Starting: return "正在启动…";
                case DshState.Stopping: return "正在停止…";
                case DshState.Error: return "启动失败";
                default: return "未运行";
            }
        }

        static ImageSource LoadIcon(string name)
        {
            return new BitmapImage(new Uri("pack://application:,,,/Assets/" + name));
        }

        public void Dispose()
        {
            try { _tray.Dispose(); } catch { }
        }
    }
}
