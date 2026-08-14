using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DshNotifyicon.Services
{
    public enum EnvStatus { Ok, Missing, Outdated, Error }

    public class EnvItem
    {
        public string Name;
        public EnvStatus Status;
        public string Detail;
    }

    /// <summary>
    /// 一键体检：Node.js / npm 镜像源 / dsh 安装与版本，聚合为可展示的条目列表。
    /// 整体限时 120s（取消信号贯穿 npm 子进程），网络不可达时快速降级，绝不卡死界面。
    /// </summary>
    public static class EnvironmentCheckService
    {
        public static async Task<List<EnvItem>> CheckAllAsync(Settings settings, string envPath, Action<string> log = null)
        {
            var items = new List<EnvItem>();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
            {
                var ct = cts.Token;

                // Node.js
                log?.Invoke("正在检查 Node.js…");
                try
                {
                    var node = await NodeService.DetectAsync(settings.NodePath, envPath);
                    if (node.NodeExe == null)
                    {
                        items.Add(new EnvItem { Name = "Node.js", Status = EnvStatus.Missing, Detail = "未检测到 Node.js 运行环境，点击下方按钮一键安装" });
                    }
                    else
                    {
                        items.Add(new EnvItem
                        {
                            Name = "Node.js",
                            Status = EnvStatus.Ok,
                            Detail = "node " + (node.NodeVersion ?? "?") + " / npm " + (node.NpmVersion ?? "?") + "  @ " + node.NodeExe
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    items.Add(new EnvItem { Name = "Node.js", Status = EnvStatus.Error, Detail = "检查超时" });
                }
                catch (Exception ex)
                {
                    items.Add(new EnvItem { Name = "Node.js", Status = EnvStatus.Error, Detail = "检查失败: " + ex.Message });
                }

                // npm 镜像源
                log?.Invoke("正在读取 npm registry…");
                string reg = "";
                try { reg = (await NpmService.GetRegistryAsync(settings.MirrorUrl, envPath, ct)).Trim(); }
                catch (OperationCanceledException) { reg = "检查超时"; }
                catch (Exception ex) { reg = "读取失败: " + ex.Message; }
                items.Add(new EnvItem
                {
                    Name = "npm 镜像源",
                    Status = reg.StartsWith("读取失败") || reg.StartsWith("检查超时") ? EnvStatus.Error : EnvStatus.Ok,
                    Detail = "npm 全局配置: " + reg +
                             (string.IsNullOrEmpty(settings.MirrorUrl) ? "" : "\n本工具命令将附加 --registry=" + settings.MirrorUrl)
                });

                // dsh 本地版本（无网络）
                log?.Invoke("正在检查 dsh 安装…");
                string local = "";
                try { local = await NpmService.GetDshLocalVersionAsync(envPath, ct); }
                catch { }
                if (string.IsNullOrEmpty(local))
                {
                    items.Add(new EnvItem { Name = "dsh", Status = EnvStatus.Missing, Detail = "未检测到 @deepseek-ai/dsh，点击下方按钮一键安装" });
                }
                else
                {
                    // dsh 远端版本（网络操作，45s 内必返回）
                    log?.Invoke("正在查询 dsh 最新版本…");
                    string latest = null;
                    try { latest = (await NpmService.GetDshLatestVersionAsync(settings.MirrorUrl, envPath, ct)).Trim(); }
                    catch (Exception ex)
                    {
                        latest = null;
                        log?.Invoke("dsh 远端版本查询失败（网络不可达或镜像异常）: " + ex.Message);
                    }
                    if (string.IsNullOrEmpty(latest))
                    {
                        items.Add(new EnvItem { Name = "dsh", Status = EnvStatus.Ok, Detail = "已安装 " + local + "（远端版本查询失败，可能网络不可达）" });
                    }
                    else if (Semver.Compare(local, latest) < 0)
                    {
                        items.Add(new EnvItem { Name = "dsh", Status = EnvStatus.Outdated, Detail = "已安装 " + local + "，远端最新 " + latest + " —— 可更新" });
                    }
                    else
                    {
                        items.Add(new EnvItem { Name = "dsh", Status = EnvStatus.Ok, Detail = "已安装 " + local + "，已是最新版本" });
                    }
                }
            }
            return items;
        }
    }
}
