using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DshNotifyicon.Services
{
    /// <summary>
    /// 最小 semver 比较器：major.minor.patch[-prerelease]，数字比较，预发布 &lt; 正式版。
    /// 不依赖字符串字典序（"0.1.0" 与 "0.1.0-rc.6" 语义相反）。
    /// </summary>
    public static class Semver
    {
        static readonly Regex Rx = new Regex(@"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$", RegexOptions.Compiled);

        /// <summary>a &lt; b 返回负数；a == b 返回 0；a &gt; b 返回正数。</summary>
        public static int Compare(string a, string b)
        {
            var ma = Rx.Match((a ?? "").Trim().TrimStart('v'));
            var mb = Rx.Match((b ?? "").Trim().TrimStart('v'));
            if (!ma.Success || !mb.Success) return string.CompareOrdinal(a ?? "", b ?? "");
            for (int i = 1; i <= 3; i++)
            {
                var x = int.Parse(ma.Groups[i].Value);
                var y = int.Parse(mb.Groups[i].Value);
                if (x != y) return x < y ? -1 : 1;
            }
            var pa = ma.Groups[4].Success ? ma.Groups[4].Value : null;
            var pb = mb.Groups[4].Success ? mb.Groups[4].Value : null;
            if (pa == null && pb == null) return 0;
            if (pa == null) return 1; // 正式版 > 预发布
            if (pb == null) return -1;
            return ComparePre(pa, pb);
        }

        static int ComparePre(string a, string b)
        {
            var as_ = a.Split('.');
            var bs = b.Split('.');
            int n = Math.Min(as_.Length, bs.Length);
            for (int i = 0; i < n; i++)
            {
                int an, bn;
                bool aNum = int.TryParse(as_[i], out an);
                bool bNum = int.TryParse(bs[i], out bn);
                if (aNum && bNum)
                {
                    if (an != bn) return an < bn ? -1 : 1;
                }
                else
                {
                    int c = string.CompareOrdinal(as_[i], bs[i]);
                    if (c != 0) return c < 0 ? -1 : 1;
                }
            }
            if (as_.Length != bs.Length) return as_.Length < bs.Length ? -1 : 1;
            return 0;
        }
    }

    /// <summary>
    /// npm 操作封装。要点：
    /// 1. 一律用 node 直调 npm-cli.js（不用 .cmd shim），cwd 固定 %USERPROFILE% 避开项目级 .npmrc；
    /// 2. 包名一律显式 @latest（用户 npmrc 若含自定义 tag=stable，裸包名会 E404——已实测）；
    /// 3. 镜像源通过每条命令追加 --registry 实现（单次指定源），不改用户全局配置；
    /// 4. 全部 npm 操作经信号量串行，杜绝并发写 .npmrc/缓存。
    /// </summary>
    public static class NpmService
    {
        static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        static string RegistryArg(string mirrorUrl)
        {
            return string.IsNullOrEmpty(mirrorUrl) ? "" : " --registry=" + mirrorUrl;
        }

        /// <summary>执行 npm 命令（node + npm-cli.js），非零退出码抛异常。</summary>
        static async Task<string> ExecNpmAsync(string args, string envPath, Action<string> log, CancellationToken ct)
        {
            var node = await NodeService.DetectAsync();
            if (node.NodeExe == null) throw new InvalidOperationException("未检测到 Node.js，请先在环境页安装");
            if (node.NpmCliJs == null) throw new InvalidOperationException("未找到 npm-cli.js（Node.js 安装可能不完整）");
            var spec = new ProcessSpec
            {
                FileName = node.NodeExe,
                Arguments = ProcessRunner.Quote(node.NpmCliJs) + " " + args,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                TimeoutMs = 20 * 60 * 1000,
                Environment = new Dictionary<string, string> { { "Path", envPath } }
            };
            var r = await ProcessRunner.RunAsync(spec, ct, log);
            if (r.TimedOut) throw new TimeoutException("npm 命令超时（" + args + "）");
            if (r.Cancelled) throw new OperationCanceledException();
            if (r.ExitCode != 0)
                throw new InvalidOperationException("npm 命令失败（exit " + r.ExitCode + "）:\n" + r.Output + r.Error);
            return r.Output;
        }

        static async Task<T> WithGateAsync<T>(Func<Task<T>> action)
        {
            await _gate.WaitAsync();
            try { return await action(); }
            finally { _gate.Release(); }
        }

        static async Task WithGateAsync(Func<Task> action)
        {
            await _gate.WaitAsync();
            try { await action(); }
            finally { _gate.Release(); }
        }

        /// <summary>当前 registry（含 mirrorUrl 时反映该源的取值）。</summary>
        public static Task<string> GetRegistryAsync(string mirrorUrl, string envPath)
        {
            return WithGateAsync(() => ExecNpmAsync("config get registry" + RegistryArg(mirrorUrl), envPath, null, CancellationToken.None));
        }

        /// <summary>写入全局 npmrc（用户级，影响所有 npm 命令）。显式操作，需 UI 确认。</summary>
        public static Task SetGlobalRegistryAsync(string url, string envPath, Action<string> log)
        {
            return WithGateAsync(() => ExecNpmAsync("config set registry " + url, envPath, log, CancellationToken.None));
        }

        /// <summary>npm 全局前缀（%APPDATA%\npm 等）。</summary>
        public static Task<string> GetGlobalPrefixAsync(string envPath)
        {
            return WithGateAsync(() => ExecNpmAsync("prefix -g", envPath, null, CancellationToken.None));
        }

        /// <summary>本地已安装的 dsh 版本；未安装返回空串。直接读 package.json，无网络。</summary>
        public static async Task<string> GetDshLocalVersionAsync(string envPath)
        {
            try
            {
                var prefix = (await GetGlobalPrefixAsync(envPath)).Trim();
                var pkg = Path.Combine(prefix, "node_modules", "@deepseek-ai", "dsh", "package.json");
                if (File.Exists(pkg))
                {
                    var j = JObject.Parse(File.ReadAllText(pkg));
                    var v = (string)j["version"];
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }
            return "";
        }

        /// <summary>远端最新版本（显式 @latest，规避自定义 tag 陷阱）。</summary>
        public static Task<string> GetDshLatestVersionAsync(string mirrorUrl, string envPath)
        {
            return WithGateAsync(() => ExecNpmAsync("view @deepseek-ai/dsh@latest version" + RegistryArg(mirrorUrl), envPath, null, CancellationToken.None));
        }

        /// <summary>安装/更新 dsh 到最新版。npm ≥ 11 时追加 --allow-scripts 以执行原生依赖安装脚本。</summary>
        public static async Task InstallOrUpdateDshAsync(string mirrorUrl, string envPath, Action<string> log, CancellationToken ct)
        {
            var args = "install -g @deepseek-ai/dsh@latest" + RegistryArg(mirrorUrl);
            try
            {
                var nv = (await WithGateAsync(() => ExecNpmAsync("--version", envPath, null, CancellationToken.None))).Trim();
                int major = 0;
                var m = Regex.Match(nv, @"^(\d+)");
                if (m.Success) int.TryParse(m.Groups[1].Value, out major);
                if (major >= 11)
                {
                    args += " --allow-scripts=koffi,node-pty,@google/genai,protobufjs,@deepseek-ai/dsh-subprocess-local";
                    log("检测到 npm " + nv + "，附加 --allow-scripts 以构建原生依赖");
                }
            }
            catch { }
            await WithGateAsync(() => ExecNpmAsync(args, envPath, log, ct));
        }

        /// <summary>解析 dsh bin.js 路径（%prefix%\node_modules\@deepseek-ai\dsh\lib\bin.js）；未安装返回 null。</summary>
        public static async Task<string> ResolveDshBinJsAsync(string envPath)
        {
            try
            {
                var prefix = (await GetGlobalPrefixAsync(envPath)).Trim();
                var p = Path.Combine(prefix, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }
    }
}
