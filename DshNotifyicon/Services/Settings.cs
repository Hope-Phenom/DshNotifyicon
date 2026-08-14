using System;
using System.IO;
using Newtonsoft.Json;

namespace DshNotifyicon.Services
{
    /// <summary>
    /// 工具设置模型。全部公开字段，由 Newtonsoft.Json 直接序列化。
    /// </summary>
    public class Settings
    {
        /// <summary>DSH Web 服务端口（1-65535）。</summary>
        public int Port = 3080;

        /// <summary>随机端口（--port 0，由 OS 分配）。</summary>
        public bool RandomPort;

        /// <summary>启动成功后自动打开浏览器。</summary>
        public bool AutoOpenBrowser = true;

        /// <summary>开机自动启动本工具。</summary>
        public bool AutoStartOnLogin;

        /// <summary>启动时显示主窗口（默认隐藏到托盘）。</summary>
        public bool ShowMainWindowOnStartup;

        /// <summary>
        /// 本工具发起的 npm 命令使用的镜像源；空 = 跟随 npm 全局配置。
        /// 通过每次命令追加 --registry 实现，不修改用户全局配置。
        /// </summary>
        public string MirrorUrl = "";

        /// <summary>trusted-host 列表（逗号/分号分隔，可重复传给 dsh）。</summary>
        public string TrustedHosts = "";

        /// <summary>用户手动指定的 node.exe 路径（留空自动检测）。</summary>
        public string NodePath = "";

        /// <summary>界面语言：auto = 跟随系统；zh / en = 手动指定。</summary>
        public string Language = "auto";
    }

    /// <summary>
    /// 设置持久化：%APPDATA%\DshNotifyicon\settings.json。
    /// 原子写（临时文件 + File.Replace）；损坏时备份 .bak 并回退默认值。
    /// </summary>
    public static class SettingsService
    {
        public static string SettingsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DshNotifyicon"); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDir, "settings.json"); }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var s = JsonConvert.DeserializeObject<Settings>(json);
                    if (s != null) return s;
                }
            }
            catch (Exception ex)
            {
                try { File.Copy(SettingsPath, SettingsPath + ".bak", true); } catch { }
                System.Diagnostics.Debug.WriteLine("settings load failed: " + ex.Message);
            }
            return new Settings();
        }

        public static void Save(Settings s)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonConvert.SerializeObject(s, Formatting.Indented);
                var tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(SettingsPath)) File.Replace(tmp, SettingsPath, null);
                else File.Move(tmp, SettingsPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("settings save failed: " + ex.Message);
            }
        }
    }
}
