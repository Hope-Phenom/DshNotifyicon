using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DshNotifyicon.Services;
using Microsoft.Win32;

namespace DshNotifyicon
{
    /// <summary>
    /// 应用级服务容器：设置、DSH 进程管理、主窗口、托盘，以及跨层辅助方法。
    /// </summary>
    public class AppServices
    {
        public readonly Settings Settings;
        public readonly DshProcessManager Dsh = new DshProcessManager();
        public MainWindow Main;
        public TrayIcon Tray;

        string _envPath;
        string _binJs;

        public AppServices(Settings settings)
        {
            Settings = settings;
            Main = new MainWindow();
        }

        /// <summary>刷新后的 PATH（合并注册表，安装 Node 后重新获取）。</summary>
        public string EnvPath
        {
            get { return _envPath ?? (_envPath = NodeService.RefreshPath()); }
        }

        public void RefreshEnvPath() { _envPath = NodeService.RefreshPath(); }

        /// <summary>解析 dsh bin.js 路径（缓存）。未安装返回 null。</summary>
        public async Task<string> DshBinJsAsync()
        {
            if (!string.IsNullOrEmpty(_binJs) && File.Exists(_binJs)) return _binJs;
            _binJs = await NpmService.ResolveDshBinJsAsync(EnvPath);
            return _binJs;
        }

        public void ToggleAutoStart(bool enable)
        {
            Settings.AutoStartOnLogin = enable;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable)
                        key.SetValue("DshNotifyicon", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                    else
                        key.DeleteValue("DshNotifyicon", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("autostart.fail", ex.Message), Loc.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void OpenUrl(string url)
        {
            try
            {
                var psi = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("browser.fail", ex.Message), Loc.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void CopyUrl(string url)
        {
            try
            {
                Clipboard.SetText(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("copy.fail", ex.Message), Loc.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
