using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DshNotifyicon.Services
{
    /// <summary>子进程启动参数。</summary>
    public class ProcessSpec
    {
        public string FileName;
        public string Arguments;
        public string WorkingDirectory = "";
        /// <summary>追加/覆盖的环境变量（每次启动子进程都注入刷新后的 PATH）。</summary>
        public Dictionary<string, string> Environment = null;
        /// <summary>超时（毫秒），超时后杀进程树。</summary>
        public int? TimeoutMs = null;
        public bool RedirectOutput = true;
    }

    public class ProcessResult
    {
        public int ExitCode;
        /// <summary>stdout 全部内容。</summary>
        public string Output;
        /// <summary>stderr 全部内容（单独收集，避免污染 stdout 解析）。</summary>
        public string Error;
        public bool TimedOut;
        public bool Cancelled;
    }

    /// <summary>
    /// 隐藏进程执行封装：CreateNoWindow + 输出重定向（UTF-8）、超时/取消、进程树终止。
    /// 所有方法都可在后台线程执行；不依赖 UI 上下文。
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>参数引号转义（.NET Framework 无 ArgumentList，手动拼参数）。</summary>
        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// 启动隐藏进程并异步读取输出。返回已启动的 Process（调用方负责 Exited/生命周期）。
        /// </summary>
        public static Process Start(ProcessSpec spec, Action<string> onOutput, Action<string> onError)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = spec.FileName;
            psi.Arguments = spec.Arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            if (!string.IsNullOrEmpty(spec.WorkingDirectory)) psi.WorkingDirectory = spec.WorkingDirectory;
            if (spec.Environment != null)
            {
                foreach (var kv in spec.Environment) psi.EnvironmentVariables[kv.Key] = kv.Value;
            }
            if (spec.RedirectOutput)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }
            var p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            if (spec.RedirectOutput)
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) onError?.Invoke(e.Data); };
            }
            p.Start();
            if (spec.RedirectOutput)
            {
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            return p;
        }

        /// <summary>
        /// 运行命令直到退出/超时/取消，收集全部输出。
        /// 内部在 ThreadPool 上执行，不会与 UI 同步上下文死锁。
        /// </summary>
        public static Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct, Action<string> onLine = null)
        {
            return Task.Run(() =>
            {
                var sbOut = new StringBuilder();
                var sbErr = new StringBuilder();
                var gate = new object();
                var startTime = DateTime.UtcNow;
                var p = Start(spec,
                    line => { lock (gate) sbOut.AppendLine(line); onLine?.Invoke(line); },
                    line => { lock (gate) sbErr.AppendLine(line); onLine?.Invoke(line); });
                var result = new ProcessResult();
                try
                {
                    while (true)
                    {
                        if (ct.IsCancellationRequested) { result.Cancelled = true; KillTree(p); break; }
                        if (spec.TimeoutMs.HasValue &&
                            (DateTime.UtcNow - startTime).TotalMilliseconds > spec.TimeoutMs.Value)
                        {
                            result.TimedOut = true;
                            KillTree(p);
                            break;
                        }
                        if (p.HasExited) { result.ExitCode = p.ExitCode; break; }
                        Thread.Sleep(150);
                    }
                    // 事件式读取需要双 WaitForExit 确保输出全部到达
                    try { p.WaitForExit(2000); } catch { }
                    try { p.WaitForExit(2000); } catch { }
                }
                catch (InvalidOperationException)
                {
                    // 进程启动失败等：尝试取退出码
                    try { result.ExitCode = p.ExitCode; } catch { }
                }
                lock (gate)
                {
                    result.Output = sbOut.ToString();
                    result.Error = sbErr.ToString();
                }
                return result;
            }, ct);
        }

        /// <summary>终止进程树：先 Kill 直系，再 taskkill /T /F 兜底（进程已退出时 taskkill 报 128，忽略）。</summary>
        public static void KillTree(Process p)
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
            try
            {
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                var k = Process.Start(psi);
                k.WaitForExit(3000);
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
        }
    }
}
