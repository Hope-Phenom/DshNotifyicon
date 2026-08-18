using System;
using System.Collections.Generic;
using System.Globalization;

namespace DshNotifyicon.Services
{
    /// <summary>语言设置取值（settings.json 持久化字符串：auto / zh / en）。</summary>
    public enum AppLanguage { Auto, Zh, En }

    /// <summary>
    /// 中英文界面文案表：T(key) / T(key, args) 取当前语言文案。
    /// auto = 跟随系统（CurrentUICulture 以 zh 开头 → 中文，否则英文）。
    /// Apply() 在语言实际变化时触发 Changed 事件，UI 据此刷新全部静态文案。
    /// </summary>
    public static class Loc
    {
        static bool _isZh = true;

        // [0]=中文, [1]=English
        static readonly Dictionary<string, string[]> _t = new Dictionary<string, string[]>
        {
            { "app.name", new[] { "dsh-desktop-tray", "dsh-desktop-tray" } },

            { "tab.env", new[] { "环境", "Environment" } },
            { "tab.service", new[] { "服务", "Service" } },
            { "tab.settings", new[] { "设置", "Settings" } },
            { "tab.notify", new[] { "通知增强", "Notification Enhancements" } },
            { "tab.about", new[] { "关于", "About" } },

            { "env.check", new[] { "一键体检", "Health Check" } },
            { "env.checking", new[] { "正在检查…", "Checking…" } },
            { "env.checkDone", new[] { "检查完成", "Check complete" } },
            { "env.checkFailed", new[] { "检查失败: {0}", "Check failed: {0}" } },
            { "env.nodeGroup", new[] { "Node.js 运行环境", "Node.js Runtime" } },
            { "env.installNode", new[] { "一键安装 Node.js", "Install Node.js" } },
            { "env.mirrorGroup", new[] { "npm 镜像源（仅对本工具生效，不修改全局配置）", "npm Mirror (tool-scoped only; global config untouched)" } },
            { "env.mirrorDefault", new[] { "默认（跟随 npm 全局配置）", "Default (follow npm global config)" } },
            { "env.mirrorNpmmirror", new[] { "npmmirror（https://registry.npmmirror.com）", "npmmirror (https://registry.npmmirror.com)" } },
            { "env.mirrorCustom", new[] { "自定义…", "Custom…" } },
            { "env.mirrorTooltip", new[] { "自定义 registry URL，例如 https://registry.npmjs.org", "Custom registry URL, e.g. https://registry.npmjs.org" } },
            { "env.applyMirror", new[] { "应用镜像", "Apply Mirror" } },
            { "env.globalNpmrc", new[] { "写入全局 npmrc…", "Write Global npmrc…" } },
            { "env.dshGroup", new[] { "dsh 安装与版本", "dsh Install & Version" } },
            { "env.pnpmGroup", new[] { "pnpm（dsh 插件管理必需）", "pnpm (required for dsh plugin management)" } },
            { "env.installPnpm", new[] { "一键安装 pnpm", "Install pnpm" } },
            { "env.installPnpmTitle", new[] { "安装 pnpm", "Install pnpm" } },
            { "env.installDsh", new[] { "安装 / 更新 dsh", "Install / Update dsh" } },
            { "env.installDshShort", new[] { "安装 dsh", "Install dsh" } },
            { "env.updateDshShort", new[] { "更新 dsh", "Update dsh" } },
            { "env.checkUpdate", new[] { "检查更新", "Check Update" } },
            { "env.installNodeTitle", new[] { "安装 Node.js", "Install Node.js" } },
            { "env.installNodeFail", new[] { "安装未成功，详见日志（可手动从 nodejs.org 下载安装）", "Install did not complete; see the log (or download it from nodejs.org manually)" } },
            { "env.verifyAfterInstall", new[] { "安装完成，正在验证…", "Install finished, verifying…" } },
            { "env.nodeDetected", new[] { "检测到 Node.js: {0}", "Node.js detected: {0}" } },
            { "env.nodeNotFound", new[] { "未检测到可执行文件（可尝试重新体检或检查安装路径）", "Executable not found (try Health Check again or check the install path)" } },
            { "env.customUrlReq", new[] { "请输入自定义 registry URL", "Enter a custom registry URL" } },
            { "env.mirrorApplied", new[] { "已应用镜像: {0}", "Mirror applied: {0}" } },
            { "env.followGlobal", new[] { "跟随 npm 全局配置", "follow npm global config" } },
            { "env.mirrorFirst", new[] { "请先选择或输入一个镜像源", "Select or enter a mirror first" } },
            { "env.globalNpmrcTitle", new[] { "写入全局 npmrc", "Write Global npmrc" } },
            { "env.globalNpmrcConfirm", new[] { "将 registry {0} 写入全局 npmrc（影响你所有 npm 命令）。\n\n确定继续？", "Write registry {0} to the global npmrc (affects all your npm commands).\n\nContinue?" } },
            { "env.globalNpmrcDone", new[] { "已写入全局 npmrc", "Global npmrc updated" } },
            { "env.globalNpmrcFail", new[] { "写入失败: {0}", "Write failed: {0}" } },
            { "env.dshInstallTitle", new[] { "安装 / 更新 dsh", "Install / Update dsh" } },
            { "env.installedVersion", new[] { "已安装版本: {0}", "Installed version: {0}" } },

            { "svc.port", new[] { "端口:", "Port:" } },
            { "svc.randomPort", new[] { "随机端口（--port 0）", "Random port (--port 0)" } },
            { "svc.bindAddr", new[] { "绑定地址: 127.0.0.1（dsh 仅支持回环地址）", "Bind address: 127.0.0.1 (dsh only supports loopback)" } },
            { "svc.start", new[] { "启动 DSH", "Start DSH" } },
            { "svc.stop", new[] { "停止 DSH", "Stop DSH" } },
            { "svc.restart", new[] { "重启 DSH", "Restart DSH" } },
            { "svc.openUi", new[] { "打开 Web UI", "Open Web UI" } },
            { "svc.logLabel", new[] { "运行日志（dsh 输出与操作记录）:", "Runtime log (dsh output & operations):" } },
            { "svc.running", new[] { "● 运行中: {0}", "● Running: {0}" } },
            { "svc.starting", new[] { "… 正在启动（首次启动需初始化 profile，可能较慢，请观察日志）", "… Starting (first run initializes the profile and may be slow; watch the log)" } },
            { "svc.stopping", new[] { "… 正在停止", "… Stopping" } },
            { "svc.error", new[] { "✗ 启动失败（查看下方日志定位原因）", "✗ Start failed (see the log below for the reason)" } },
            { "svc.idle", new[] { "○ 未运行", "○ Not running" } },
            { "svc.startFailBalloonTitle", new[] { "DSH 启动失败", "DSH start failed" } },
            { "svc.startFailBalloonText", new[] { "请查看主窗口日志面板", "See the log panel in the main window" } },
            { "svc.notRunning", new[] { "DSH 未运行", "DSH not running" } },
            { "svc.startFirst", new[] { "请先启动 DSH 服务", "Start the DSH service first" } },
            { "svc.portInvalid", new[] { "端口必须是 1-65535 之间的整数", "Port must be an integer between 1 and 65535" } },
            { "svc.portInvalidSettings", new[] { "设置中的端口无效（应为 1-65535 的整数），请在设置页修改", "The configured port is invalid (must be 1-65535); fix it in the Settings tab" } },
            { "svc.noNode", new[] { "未检测到 Node.js，请先到环境页一键安装", "Node.js not found — install it from the Environment tab first" } },
            { "svc.noDsh", new[] { "未检测到 dsh，请先到环境页安装", "dsh not found — install it from the Environment tab first" } },
            { "svc.startFailed", new[] { "启动失败: {0}", "Start failed: {0}" } },
            { "svc.portBusyTitle", new[] { "端口被占用", "Port in use" } },
            { "svc.portBusyChoices", new[] { "{0}。\n\n是 = 直接打开浏览器访问该端口\n否 = 取消", "{0}.\n\nYes = open the browser on that port\nNo = cancel" } },
            { "svc.externalTitle", new[] { "检测到其他 dsh 实例", "Other dsh instances found" } },
            { "svc.externalChoices", new[] { "{0}：\n{1}\n是 = 停止这些实例并启动新实例\n否 = 仅打开浏览器\n取消 = 放弃", "{0}:\n{1}\nYes = stop them and start a new instance\nNo = only open the browser\nCancel = abort" } },

            { "set.autoOpen", new[] { "启动 DSH 成功后自动打开浏览器", "Open the browser automatically after DSH starts" } },
            { "set.autoStart", new[] { "开机自动启动本工具", "Auto-start this tool at logon" } },
            { "set.showMain", new[] { "启动时显示主窗口（默认隐藏到托盘）", "Show the main window on startup (hidden to tray by default)" } },
            { "set.autoStartDsh", new[] { "托盘启动后自动启动 DSH", "Auto-start DSH after the tray app launches" } },
            { "set.exitNote", new[] { "退出本工具时会自动停止 DSH 服务", "Exiting this tool always stops the DSH service" } },
            { "set.langGroup", new[] { "界面语言", "UI Language" } },
            { "set.langAuto", new[] { "自动（跟随系统）", "Auto (follow system)" } },
            { "set.langZh", new[] { "中文", "中文" } },
            { "set.langEn", new[] { "English", "English" } },
            { "set.langHint", new[] { "更改后立即生效并保存", "Takes effect immediately and is saved" } },
            { "set.advanced", new[] { "高级", "Advanced" } },
            { "set.trustedHosts", new[] { "trusted-host（可选，逗号/分号分隔，可重复传给 dsh）:", "trusted-host (optional, comma/semicolon separated; the flag may repeat):" } },
            { "set.nodePath", new[] { "Node.js 可执行文件路径（留空自动检测）:", "Node.js executable path (empty = auto-detect):" } },
            { "set.cleanup", new[] { "清理", "Cleanup" } },
            { "set.cleanupDesc", new[] { "移除 dsh 相关环境：停止 dsh 服务 → 卸载全局 npm 包 @deepseek-ai/dsh → 将数据目录（含 API 凭据与会话）重命名备份为 .dsh.bak-日期（不直接删除）。不会卸载 Node.js；如确需卸载 Node.js，请到系统 设置→应用 或执行 winget uninstall OpenJS.NodeJS.LTS。", "Removes the dsh environment: stop dsh → uninstall the global npm package @deepseek-ai/dsh → rename the data directory (API credentials & sessions) to .dsh.bak-date as a backup (not deleted). Node.js is NOT uninstalled; to remove Node.js use Settings → Apps or run winget uninstall OpenJS.NodeJS.LTS." } },
            { "set.cleanupBtn", new[] { "清理 dsh 环境…", "Clean up dsh environment…" } },
            { "set.save", new[] { "保存设置", "Save Settings" } },
            { "set.openDir", new[] { "打开配置目录", "Open Settings Folder" } },
            { "set.saved", new[] { "设置已保存", "Settings saved" } },
            { "set.openDirFail", new[] { "打开失败: {0}", "Failed to open: {0}" } },
            { "set.trayGroup", new[] { "托盘", "Tray" } },
            { "set.doubleClick", new[] { "双击托盘图标:", "Double-click tray icon:" } },
            { "set.doubleClickMain", new[] { "打开主面板", "Open main window" } },
            { "set.doubleClickWeb", new[] { "打开 Web UI", "Open Web UI" } },
            { "set.doubleClickHint", new[] { "更改后立即生效并保存。", "Takes effect immediately and is saved." } },

            { "notify.enable", new[] { "启用通知增强", "Enable notification enhancements" } },
            { "notify.subagents", new[] { "子代理/子任务完成时也通知", "Notify for subagents/subtasks too" } },
            { "notify.tray", new[] { "显示托盘通知", "Show tray notifications" } },
            { "notify.externalGroup", new[] { "外部命令", "External Command" } },
            { "notify.externalEnable", new[] { "启用外部命令", "Enable external command" } },
            { "notify.command", new[] { "命令:", "Command:" } },
            { "notify.arguments", new[] { "参数模板:", "Arguments template:" } },
            { "notify.hint", new[] { "支持占位符: {event} {title} {sessionId} {parentSessionId} {turn} {reason} {durationMs}", "Placeholders: {event} {title} {sessionId} {parentSessionId} {turn} {reason} {durationMs}" } },
            { "notify.installPlugin", new[] { "安装/更新 dsh 通知插件", "Install/Update dsh Notification Plugin" } },
            { "notify.uninstallPlugin", new[] { "卸载 dsh 通知插件", "Uninstall dsh Notification Plugin" } },
            { "notify.uninstallConfirmTitle", new[] { "卸载 dsh 通知插件", "Uninstall dsh Notification Plugin" } },
            { "notify.uninstallConfirm", new[] { "确定要从 web profile 中移除 dsh-notify-hook 插件吗？\n\n将执行：\n1. 从 profile 的 package.json 移除依赖\n2. 从 cordis.patch.yml 移除插件条目\n\n不会影响已有会话数据。", "Remove the dsh-notify-hook plugin from the web profile?\n\nThis will:\n1. Remove the dependency from profile package.json\n2. Remove the plugin entry from cordis.patch.yml\n\nExisting session data will not be affected." } },
            { "notify.uninstalling", new[] { "正在卸载 dsh 通知插件…", "Uninstalling dsh notification plugin…" } },
            { "notify.uninstallDone", new[] { "dsh 通知插件已卸载。若 dsh 正在运行，请重启 dsh 后生效。", "dsh notification plugin uninstalled. Restart dsh if it is currently running." } },
            { "notify.uninstallFail", new[] { "卸载失败（exit {0}）: {1}", "Uninstall failed (exit {0}): {1}" } },
            { "notify.test", new[] { "测试通知", "Test Notification" } },
            { "notify.save", new[] { "保存设置", "Save Settings" } },
            { "notify.pluginNotFound", new[] { "未找到 dsh-notify-hook 插件目录，请确认 tools/dsh-notify-hook 存在", "dsh-notify-hook plugin directory not found; ensure tools/dsh-notify-hook exists" } },
            { "notify.installing", new[] { "正在安装 dsh 通知插件: {0}", "Installing dsh notification plugin: {0}" } },
            { "notify.installDone", new[] { "dsh 通知插件已安装/更新。若 dsh 正在运行，请重启 dsh 后生效。", "dsh notification plugin installed/updated. Restart dsh if it is currently running." } },
            { "notify.installFail", new[] { "安装/更新失败（exit {0}）: {1}", "Install/update failed (exit {0}): {1}" } },
            { "notify.testTitle", new[] { "通知增强测试", "Notification Enhancement Test" } },
            { "notify.testText", new[] { "如果你看到这条消息，说明托盘通知正常。", "If you see this message, tray notifications are working." } },
            { "notify.untitled", new[] { "未命名会话", "Untitled session" } },
            { "notify.turnEndTitle", new[] { "DSH 回答完成", "DSH response complete" } },
            { "notify.turnEndText", new[] { "标题：{0}\n会话：{1}\n第 {2} 轮\n状态：{3}\n耗时：{4}", "Title: {0}\nSession: {1}\nTurn: {2}\nStatus: {3}\nDuration: {4}" } },
            { "notify.subTurnEndTitle", new[] { "DSH 子任务完成", "DSH subtask complete" } },
            { "notify.subTurnEndText", new[] { "标题：{0}\n子会话：{1}\n父会话：{2}\n第 {3} 轮\n状态：{4}\n耗时：{5}", "Title: {0}\nSub-session: {1}\nParent: {2}\nTurn: {3}\nStatus: {4}\nDuration: {5}" } },
            { "notify.parseFail", new[] { "通知解析失败: {0}", "Notification parse failed: {0}" } },
            { "notify.hookFail", new[] { "外部命令执行失败: {0}", "External command failed: {0}" } },

            { "about.name", new[] { "dsh-desktop-tray", "dsh-desktop-tray" } },
            { "about.desc", new[] { "DeepSeek Harness（dsh）的桌面托盘助手：一键完成环境配置（Node.js / npm 镜像源 / dsh 安装与更新）与 Web UI 启停，自动解析实际地址并打开浏览器——无需手动打开命令行窗口。", "A desktop tray assistant for DeepSeek Harness (dsh): one-click environment setup (Node.js / npm mirror / dsh install & update) and Web UI start/stop, with automatic URL resolution and browser launch — no more manual terminal windows." } },
            { "about.techGroup", new[] { "技术栈", "Tech Stack" } },
            { "about.tech1", new[] { "· WPF · .NET Framework 4.6.2 · C# 7.3", "· WPF · .NET Framework 4.6.2 · C# 7.3" } },
            { "about.tech2", new[] { "· Hardcodet.NotifyIcon.Wpf（托盘）· Newtonsoft.Json（设置）", "· Hardcodet.NotifyIcon.Wpf (tray) · Newtonsoft.Json (settings)" } },
            { "about.tech3", new[] { "· 服务对象：DeepSeek Harness CLI（dsh）", "· Serves: DeepSeek Harness CLI (dsh)" } },
            { "about.linksGroup", new[] { "链接", "Links" } },
            { "about.linkDh", new[] { "DeepSeek Harness: ", "DeepSeek Harness: " } },
            { "about.linkRepo", new[] { "本工具仓库: ", "Repository: " } },
            { "about.linkSite", new[] { "在线页面: ", "Project page: " } },
            { "about.licenseGroup", new[] { "版权与致谢", "License & Credits" } },
            { "about.license", new[] { "License: MIT（见仓库 LICENSE.txt）", "License: MIT (see LICENSE.txt in the repo)" } },
            { "about.credits", new[] { "图标基于 DeepSeek 官方 favicon 渲染；感谢 DeepSeek Harness 团队以及 Hardcodet.NotifyIcon.Wpf、Newtonsoft.Json 开源项目。", "Icon rendered from DeepSeek's official favicon; thanks to the DeepSeek Harness team and the Hardcodet.NotifyIcon.Wpf & Newtonsoft.Json open-source projects." } },
            { "about.version", new[] { "版本 {0} · {1} · .NET Framework 4.6.2", "Version {0} · {1} · .NET Framework 4.6.2" } },

            { "cleanup.confirmTitle", new[] { "清理 dsh 环境", "Clean up dsh environment" } },
            { "cleanup.confirm", new[] { "将执行以下清理：\n\n1. 停止正在运行的 dsh 服务\n2. 卸载全局 npm 包 @deepseek-ai/dsh\n3. 将数据目录（含 API 凭据与会话记录）重命名备份为 .dsh.bak-日期（不直接删除，可恢复）\n4. 移除本工具的开机自启项\n\n不会卸载 Node.js。确定继续？", "This will:\n\n1. Stop the running dsh service\n2. Uninstall the global npm package @deepseek-ai/dsh\n3. Rename the data directory (API credentials & sessions) to .dsh.bak-date as a backup (recoverable, not deleted)\n4. Remove this tool's logon auto-start entry\n\nNode.js will NOT be uninstalled. Continue?" } },
            { "cleanup.title", new[] { "清理 dsh 环境", "Clean up dsh environment" } },
            { "cleanup.stopLog", new[] { "停止 dsh 服务…", "Stopping dsh service…" } },
            { "cleanup.skipStop", new[] { "dsh 未在运行，跳过停止", "dsh is not running, skipping stop" } },
            { "cleanup.uninstallLog", new[] { "卸载全局包 @deepseek-ai/dsh…", "Uninstalling global package @deepseek-ai/dsh…" } },
            { "cleanup.uninstallDone", new[] { "npm 全局包已卸载", "Global npm package uninstalled" } },
            { "cleanup.backupLog", new[] { "数据目录备份为: {0}（含凭据，确认不再需要后可手动删除）", "Data directory backed up as: {0} (contains credentials; delete it manually once you confirm they are no longer needed)" } },
            { "cleanup.noHome", new[] { "未发现数据目录 {0}，跳过备份", "Data directory {0} not found, skipping backup" } },
            { "cleanup.done", new[] { "清理完成。本工具删除自身文件夹即卸载；设置目录可一并删除: {0}", "Cleanup done. Deleting this tool's own folder uninstalls it; the settings folder can be deleted too: {0}" } },

            { "tray.start", new[] { "启动 DSH", "Start DSH" } },
            { "tray.stop", new[] { "停止 DSH", "Stop DSH" } },
            { "tray.restart", new[] { "重启 DSH", "Restart DSH" } },
            { "tray.openUi", new[] { "打开 Web UI", "Open Web UI" } },
            { "tray.copyUrl", new[] { "复制 URL", "Copy URL" } },
            { "tray.envCheck", new[] { "环境体检", "Environment Check" } },
            { "tray.mainWindow", new[] { "打开主窗口", "Main Window" } },
            { "tray.autoStart", new[] { "开机自启", "Auto-start at logon" } },
            { "tray.exit", new[] { "退出", "Exit" } },
            { "tray.state.starting", new[] { "正在启动…", "Starting…" } },
            { "tray.state.stopping", new[] { "正在停止…", "Stopping…" } },
            { "tray.state.error", new[] { "启动失败", "Start failed" } },
            { "tray.state.idle", new[] { "未运行", "Not running" } },
            { "tray.running", new[] { "DSH 运行中: {0}", "DSH running: {0}" } },

            { "app.errBalloon", new[] { "发生错误: {0}", "An error occurred: {0}" } },
            { "app.crashRecent", new[] { "检测到最近一次运行发生异常，详情已记录: {0}", "The previous run ended with an error; details were saved: {0}" } },
            { "app.crashInternal", new[] { "发生内部错误，详情已记录: {0}", "An internal error occurred; details were saved: {0}" } },
            { "app.startedTitle", new[] { "DSH 已启动", "DSH started" } },
            { "app.startedText", new[] { "Web UI: {0}", "Web UI: {0}" } },
            { "app.exitedTitle", new[] { "DSH 已退出", "DSH exited" } },

            { "link.fail", new[] { "打开链接失败: {0}", "Failed to open link: {0}" } },
            { "autostart.fail", new[] { "设置开机自启失败: {0}", "Failed to set auto-start: {0}" } },
            { "browser.fail", new[] { "打开浏览器失败: {0}", "Failed to open browser: {0}" } },
            { "copy.fail", new[] { "复制失败: {0}", "Copy failed: {0}" } },

            { "op.cancel", new[] { "取消", "Cancel" } },
            { "op.done", new[] { "✔ {0} 完成", "✔ {0} done" } },
            { "op.doneShort", new[] { "{0} 完成", "{0} done" } },
            { "op.cancelled", new[] { "✖ {0} 已取消", "✖ {0} cancelled" } },
            { "op.failed", new[] { "✖ {0} 失败: {1}", "✖ {0} failed: {1}" } },
            { "op.failedShort", new[] { "✖ {0} 失败", "✖ {0} failed" } },

            { "ec.checkingNode", new[] { "正在检查 Node.js…", "Checking Node.js…" } },
            { "ec.nodeMissing", new[] { "未检测到 Node.js 运行环境，点击下方按钮一键安装", "Node.js runtime not detected — click the button below to install it" } },
            { "ec.timeout", new[] { "检查超时", "Check timed out" } },
            { "ec.checkFailed", new[] { "检查失败: {0}", "Check failed: {0}" } },
            { "ec.checkingRegistry", new[] { "正在读取 npm registry…", "Reading npm registry…" } },
            { "ec.regTimeout", new[] { "检查超时", "Check timed out" } },
            { "ec.regReadFail", new[] { "读取失败: {0}", "Read failed: {0}" } },
            { "ec.regGlobal", new[] { "npm 全局配置: {0}", "npm global config: {0}" } },
            { "ec.regToolAppend", new[] { "本工具命令将附加 --registry={0}", "tool commands will append --registry={0}" } },
            { "ec.checkingDsh", new[] { "正在检查 dsh 安装…", "Checking dsh installation…" } },
            { "ec.checkingPnpm", new[] { "正在检查 pnpm…", "Checking pnpm…" } },
            { "ec.pnpmMissing", new[] { "未检测到 pnpm（dsh 插件管理必需），点击下方按钮一键安装", "pnpm not detected (required for dsh plugin management) — click the button below to install it" } },
            { "ec.pnpmOk", new[] { "已安装: {0}", "Installed: {0}" } },
            { "ec.dshMissing", new[] { "未检测到 @deepseek-ai/dsh，点击下方按钮一键安装", "@deepseek-ai/dsh not detected — click the button below to install it" } },
            { "ec.checkingDshLatest", new[] { "正在查询 dsh 最新版本…", "Querying the latest dsh version…" } },
            { "ec.dshLatestFail", new[] { "dsh 远端版本查询失败（网络不可达或镜像异常）: {0}", "Failed to query the remote dsh version (network or mirror issue): {0}" } },
            { "ec.dshOkNoLatest", new[] { "已安装 {0}（远端版本查询失败，可能网络不可达）", "Installed {0} (remote version query failed; the network may be unreachable)" } },
            { "ec.dshOutdated", new[] { "已安装 {0}，远端最新 {1} —— 可更新", "Installed {0}, latest remote {1} — update available" } },
            { "ec.dshUpToDate", new[] { "已安装 {0}，已是最新版本", "Installed {0} (up to date)" } },

            { "node.wingetTry", new[] { "尝试 winget 安装 OpenJS.NodeJS.LTS …（可能弹出 UAC 确认）", "Trying winget install OpenJS.NodeJS.LTS … (a UAC prompt may appear)" } },
            { "node.wingetFail", new[] { "winget 未成功（exit {0}），改用官方 MSI 安装…", "winget failed (exit {0}); falling back to the official MSI…" } },
            { "node.wingetUnavailable", new[] { "winget 不可用（{0}），改用官方 MSI 安装…", "winget unavailable ({0}); falling back to the official MSI…" } },
            { "node.fetchLts", new[] { "获取最新 LTS 版本信息…", "Fetching the latest LTS version info…" } },
            { "node.ltsFail", new[] { "无法从 nodejs.org 获取最新 LTS 版本", "Could not fetch the latest LTS version from nodejs.org" } },
            { "node.download", new[] { "下载 {0}", "Downloading {0}" } },
            { "node.downloadDone", new[] { "下载完成，启动静默安装（可能弹出 UAC 确认）…", "Download complete, starting silent install (a UAC prompt may appear)…" } },
            { "node.installCancelled", new[] { "安装被取消（UAC 未确认）。可手动从 {0} 下载安装。", "Install cancelled (UAC declined). You can download it manually from {0}." } },
            { "node.msiFail", new[] { "MSI 安装失败，退出码 {0}。可手动从 {1} 下载安装。", "MSI install failed with exit code {0}. You can download it manually from {1}." } },
            { "node.msiFailMsg", new[] { "MSI 安装失败: {0}", "MSI install failed: {0}" } },
            { "node.downloadUrl", new[] { "https://nodejs.org/zh-cn/download", "https://nodejs.org/en/download" } },

            { "npm.noNode", new[] { "未检测到 Node.js，请先在环境页安装", "Node.js not found — install it from the Environment tab first" } },
            { "npm.noCli", new[] { "未找到 npm-cli.js（Node.js 安装可能不完整）", "npm-cli.js not found (the Node.js install may be incomplete)" } },
            { "npm.timeout", new[] { "npm 命令超时（{0}）", "npm command timed out ({0})" } },
            { "npm.fail", new[] { "npm 命令失败（exit {0}）:\n{1}{2}", "npm command failed (exit {0}):\n{1}{2}" } },
            { "npm.allowScripts", new[] { "检测到 npm {0}，附加 --allow-scripts 以构建原生依赖", "npm {0} detected; appending --allow-scripts to build native dependencies" } },
            { "npm.pnpmInstalling", new[] { "未检测到 pnpm，正在通过 npm 安装（dsh 插件管理必需）…", "pnpm not found — installing it via npm (required for dsh plugin management)…" } },
            { "npm.pnpmInstallFail", new[] { "pnpm 已安装但未出现在 PATH 中。请重启应用后重试，或手动执行 npm install -g pnpm", "pnpm installed but not found on PATH. Restart the app and retry, or run npm install -g pnpm manually" } },

            { "dsh.alreadyRunning", new[] { "dsh 已在运行", "dsh is already running" } },
            { "dsh.starting", new[] { "启动: {0} {1}", "Starting: {0} {1}" } },
            { "dsh.startFail", new[] { "启动失败: {0}", "Start failed: {0}" } },
            { "dsh.exited", new[] { "dsh 进程意外退出（exit {0}）", "dsh exited unexpectedly (exit {0})" } },
            { "dsh.exitedMsg", new[] { "dsh 进程意外退出（exit {0}）。会话已持久化，重新启动即可继续。", "dsh exited unexpectedly (exit {0}). Sessions are persisted — just start it again." } },
            { "dsh.urlParsed", new[] { "已解析 URL: {0}", "URL resolved: {0}" } },
            { "dsh.urlUnresolved", new[] { "未解析到 URL 且端口由 OS 分配，无法确认服务地址", "No URL was resolved and the port was OS-assigned; the service address cannot be confirmed" } },
            { "dsh.earlyExit", new[] { "dsh 进程提前退出，exit code {0}。查看上方日志定位原因（如端口占用）。", "dsh exited early with code {0}. See the log above for the reason (e.g. port in use)." } },
            { "dsh.notReady", new[] { "服务在超时时间内未就绪（120s）。可能首次初始化 profile 较慢，可查看日志重试。", "The service did not become ready within the timeout (120s). First-run profile init may be slow; check the log and retry." } },
            { "dsh.ready", new[] { "dsh 已就绪: {0}", "dsh ready: {0}" } },
            { "dsh.portBusy", new[] { "端口 {0} 已被占用，可能已有 dsh 或其他服务在运行", "Port {0} is already in use — another dsh or service may be running" } },
            { "dsh.externalFound", new[] { "检测到 {0} 个正在运行的 dsh 实例。双实例并发写同一数据目录可能损坏会话，请选择处理方式", "Found {0} running dsh instance(s). Two instances writing the same data directory concurrently may corrupt sessions — choose how to proceed" } },
            { "dsh.stop", new[] { "停止 dsh（PID {0}）…", "Stopping dsh (PID {0})…" } },
            { "dsh.stopped", new[] { "dsh 已停止。注：强停不保证优雅停机，会话至多丢失约 5 秒尾部。", "dsh stopped. Note: force stop is not graceful — at most ~5s of trailing session may be lost." } }
        };

        /// <summary>当前是否中文界面。</summary>
        public static bool IsZh { get { return _isZh; } }

        /// <summary>语言变化事件（同步回调；调用方在 UI 线程时直接刷新界面）。</summary>
        public static event EventHandler Changed;

        public static AppLanguage Resolve(string setting)
        {
            if (string.Equals(setting, "zh", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Zh;
            if (string.Equals(setting, "en", StringComparison.OrdinalIgnoreCase)) return AppLanguage.En;
            return AppLanguage.Auto;
        }

        /// <summary>应用语言设置（auto = 跟随系统）。语言实际变化时触发 Changed。</summary>
        public static void Apply(string setting)
        {
            var lang = Resolve(setting);
            bool zh = lang != AppLanguage.En;
            if (lang == AppLanguage.Auto)
                zh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            if (zh == _isZh) return;
            _isZh = zh;
            try { Changed?.Invoke(null, EventArgs.Empty); } catch { }
        }

        /// <summary>取当前语言文案；key 不存在时原样返回 key（便于发现遗漏）。</summary>
        public static string T(string key)
        {
            string[] v;
            if (!_t.TryGetValue(key, out v)) return key;
            return v[_isZh ? 0 : 1];
        }

        /// <summary>取当前语言文案并格式化占位符。</summary>
        public static string T(string key, params object[] args)
        {
            string s = T(key);
            try { return string.Format(s, args); } catch { return s; }
        }
    }
}
