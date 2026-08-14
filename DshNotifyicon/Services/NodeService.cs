using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace DshNotifyicon.Services
{
    public class NodeInfo
    {
        /// <summary>node.exe 完整路径；null = 未安装。</summary>
        public string NodeExe;
        public string NodeVersion;
        public string NpmVersion;
        /// <summary>npm-cli.js 完整路径（node 直调 npm 用）。</summary>
        public string NpmCliJs;
    }

    /// <summary>
    /// Node.js 运行环境：检测（PATH + 常见路径兜底）、winget/MSI 一键安装、PATH 刷新。
    /// </summary>
    public static class NodeService
    {
        /// <summary>
        /// 刷新 PATH：合并注册表用户/系统 Path（REG_EXPAND_SZ 展开）与当前进程 PATH，去重。
        /// winget/MSI 安装 Node 后，新 PATH 对后续子进程立即可见。
        /// </summary>
        public static string RefreshPath()
        {
            var parts = new List<string>();
            Action<string> add = (v) =>
            {
                if (string.IsNullOrEmpty(v)) return;
                foreach (var seg in v.Split(';'))
                {
                    var s = seg.Trim();
                    if (s.Length > 0) parts.Add(s);
                }
            };
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey("Environment"))
                {
                    if (k != null) add(Environment.ExpandEnvironmentVariables((string)k.GetValue("Path", "")));
                }
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                {
                    if (k != null) add(Environment.ExpandEnvironmentVariables((string)k.GetValue("Path", "")));
                }
            }
            catch { }
            add(Environment.GetEnvironmentVariable("Path"));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<string>();
            foreach (var p in parts) if (seen.Add(p)) merged.Add(p);
            return string.Join(";", merged);
        }

        /// <summary>检测 node/npm：PATH 扫描 + 常见安装路径兜底；读取版本。</summary>
        public static async Task<NodeInfo> DetectAsync(string nodeExeOverride = null)
        {
            var info = new NodeInfo();
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(nodeExeOverride)) candidates.Add(nodeExeOverride);
            var pathEnv = Environment.GetEnvironmentVariable("Path") ?? "";
            foreach (var seg in pathEnv.Split(';'))
            {
                var dir = seg.Trim();
                if (dir.Length == 0) continue;
                var p = Path.Combine(dir, "node.exe");
                if (File.Exists(p)) candidates.Add(p);
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvm", "node.exe"));

            string nodeExe = null;
            foreach (var c in candidates)
            {
                if (File.Exists(c)) { nodeExe = c; break; }
            }
            if (nodeExe == null) return info;
            info.NodeExe = nodeExe;

            var npmCli = Path.Combine(Path.GetDirectoryName(nodeExe), "node_modules", "npm", "bin", "npm-cli.js");
            if (File.Exists(npmCli)) info.NpmCliJs = npmCli;

            try
            {
                info.NodeVersion = (await ProcessRunner.RunAsync(
                    new ProcessSpec { FileName = nodeExe, Arguments = "--version", TimeoutMs = 15000 },
                    CancellationToken.None, null)).Output.Trim();
            }
            catch { }
            if (info.NpmCliJs != null)
            {
                try
                {
                    info.NpmVersion = (await ProcessRunner.RunAsync(
                        new ProcessSpec
                        {
                            FileName = nodeExe,
                            Arguments = ProcessRunner.Quote(info.NpmCliJs) + " --version",
                            TimeoutMs = 15000
                        },
                        CancellationToken.None, null)).Output.Trim();
                }
                catch { }
            }
            return info;
        }

        /// <summary>
        /// 一键安装 Node.js：优先 winget（OpenJS.NodeJS.LTS），失败回退官方 MSI（index.json 取最新 LTS）。
        /// 安装后调用方需 RefreshPath() 再检测。
        /// </summary>
        public static async Task<bool> InstallNodeAsync(Action<string> log, CancellationToken ct)
        {
            // 1) winget
            try
            {
                log("尝试 winget 安装 OpenJS.NodeJS.LTS …（可能弹出 UAC 确认）");
                var r = await ProcessRunner.RunAsync(new ProcessSpec
                {
                    FileName = "winget.exe",
                    Arguments = "install --id OpenJS.NodeJS.LTS -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                    TimeoutMs = 15 * 60 * 1000
                }, ct, log);
                if (!r.TimedOut && !r.Cancelled && r.ExitCode == 0) return true;
                log("winget 未成功（exit " + r.ExitCode + "），改用官方 MSI 安装…");
            }
            catch (Exception ex)
            {
                log("winget 不可用（" + ex.Message + "），改用官方 MSI 安装…");
            }

            // 2) 官方 MSI
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(60);
                    log("获取最新 LTS 版本信息…");
                    var idx = await http.GetStringAsync("https://nodejs.org/dist/index.json");
                    string version = null;
                    foreach (var item in JArray.Parse(idx))
                    {
                        if (item["lts"] != null && item["lts"].Type != JTokenType.Null)
                        {
                            version = (string)item["version"];
                            break;
                        }
                    }
                    if (version == null) { log("无法从 nodejs.org 获取最新 LTS 版本"); return false; }
                    var url = "https://nodejs.org/dist/" + version + "/node-" + version + "-x64.msi";
                    var msi = Path.Combine(Path.GetTempPath(), "node-" + version + "-x64.msi");
                    log("下载 " + url);
                    var bytes = await http.GetByteArrayAsync(url);
                    File.WriteAllBytes(msi, bytes);
                    log("下载完成，启动静默安装（可能弹出 UAC 确认）…");
                    var mr = await ProcessRunner.RunAsync(new ProcessSpec
                    {
                        FileName = "msiexec.exe",
                        Arguments = "/i " + ProcessRunner.Quote(msi) + " /qn",
                        TimeoutMs = 10 * 60 * 1000
                    }, ct, log);
                    if (mr.ExitCode == 0 || mr.ExitCode == 3010) return true;
                    if (mr.ExitCode == 1602)
                        log("安装被取消（UAC 未确认）。可手动从 https://nodejs.org/zh-cn/download 下载安装。");
                    else
                        log("MSI 安装失败，退出码 " + mr.ExitCode + "。可手动从 https://nodejs.org/zh-cn/download 下载安装。");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log("MSI 安装失败: " + ex.Message);
                return false;
            }
        }
    }
}
