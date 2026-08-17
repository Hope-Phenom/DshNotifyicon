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
    /// 语言切换（Loc.Changed）时刷新全部菜单文案。
    /// </summary>
    public class TrayIcon : IDisposable
    {
        readonly TaskbarIcon _tray;
        readonly MenuItem _startItem;
        readonly MenuItem _stopItem;
        readonly MenuItem _restartItem;
        readonly MenuItem _openItem;
        readonly MenuItem _copyItem;
        readonly MenuItem _envItem;
        readonly MenuItem _winItem;
        readonly MenuItem _autoStartItem;
        readonly MenuItem _exitItem;

        DshState _lastState = DshState.Idle;
        string _lastUrl;

        public TrayIcon(TrayActions a, Settings settings)
        {
            _tray = new TaskbarIcon();

            var menu = new ContextMenu();
            _startItem = Item(Loc.T("tray.start"), a.Start);
            _stopItem = Item(Loc.T("tray.stop"), a.Stop);
            _restartItem = Item(Loc.T("tray.restart"), a.Restart);
            menu.Items.Add(_startItem);
            menu.Items.Add(_stopItem);
            menu.Items.Add(_restartItem);
            menu.Items.Add(Sep());
            _openItem = Item(Loc.T("tray.openUi"), a.OpenUi);
            _copyItem = Item(Loc.T("tray.copyUrl"), a.CopyUrl);
            menu.Items.Add(_openItem);
            menu.Items.Add(_copyItem);
            menu.Items.Add(Sep());
            _envItem = Item(Loc.T("tray.envCheck"), a.ShowEnv);
            _winItem = Item(Loc.T("tray.mainWindow"), a.ShowWindow);
            menu.Items.Add(_envItem);
            menu.Items.Add(_winItem);
            menu.Items.Add(Sep());
            _autoStartItem = Item(Loc.T("tray.autoStart"), null);
            _autoStartItem.IsCheckable = true;
            _autoStartItem.IsChecked = settings.AutoStartOnLogin;
            _autoStartItem.Click += (s, e) => a.ToggleAutoStart(_autoStartItem.IsChecked);
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(Sep());
            _exitItem = Item(Loc.T("tray.exit"), a.Exit);
            menu.Items.Add(_exitItem);
            _tray.ContextMenu = menu;

            _tray.TrayMouseDoubleClick += (s, e) =>
            {
                try
                {
                    if (settings.TrayDoubleClickAction == "web") a.OpenUi();
                    else a.ShowWindow();
                }
                catch { }
            };

            Loc.Changed += OnLangChanged;
            ApplyLanguage();
            SetState(DshState.Idle, null);
        }

        void OnLangChanged(object sender, EventArgs e)
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                try { dispatcher.BeginInvoke(new Action(ApplyLanguage)); } catch { }
                return;
            }
            ApplyLanguage();
        }

        /// <summary>刷新全部静态文案（构造时 + 语言切换时）。</summary>
        void ApplyLanguage()
        {
            _startItem.Header = Loc.T("tray.start");
            _stopItem.Header = Loc.T("tray.stop");
            _restartItem.Header = Loc.T("tray.restart");
            _openItem.Header = Loc.T("tray.openUi");
            _copyItem.Header = Loc.T("tray.copyUrl");
            _envItem.Header = Loc.T("tray.envCheck");
            _winItem.Header = Loc.T("tray.mainWindow");
            _autoStartItem.Header = Loc.T("tray.autoStart");
            _exitItem.Header = Loc.T("tray.exit");
            _tray.ToolTipText = ToolTipText();
        }

        string ToolTipText()
        {
            return _lastState == DshState.Running
                ? Loc.T("tray.running", _lastUrl)
                : Loc.T("app.name") + " — " + StateText(_lastState);
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
                _lastState = state;
                _lastUrl = url;
                bool running = state == DshState.Running;
                bool busy = state == DshState.Starting || state == DshState.Stopping;
                _startItem.IsEnabled = !running && !busy;
                _stopItem.IsEnabled = running || state == DshState.Starting;
                _restartItem.IsEnabled = running || state == DshState.Starting;
                _openItem.IsEnabled = running || url != null;
                _copyItem.IsEnabled = running || url != null;
                _tray.IconSource = running ? LoadIcon("app-running.ico") : LoadIcon("app.ico");
                _tray.ToolTipText = ToolTipText();
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
                case DshState.Starting: return Loc.T("tray.state.starting");
                case DshState.Stopping: return Loc.T("tray.state.stopping");
                case DshState.Error: return Loc.T("tray.state.error");
                default: return Loc.T("tray.state.idle");
            }
        }

        static ImageSource LoadIcon(string name)
        {
            return new BitmapImage(new Uri("pack://application:,,,/Assets/" + name));
        }

        public void Dispose()
        {
            try { Loc.Changed -= OnLangChanged; } catch { }
            try { _tray.Dispose(); } catch { }
        }
    }
}
