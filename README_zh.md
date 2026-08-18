# DshNotifyicon — DSH 托盘助手

[English](README.md) | **简体中文**

[![Build](https://github.com/Hope-Phenom/dsh-desktop-tray/actions/workflows/build.yml/badge.svg)](https://github.com/Hope-Phenom/dsh-desktop-tray/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Hope-Phenom/dsh-desktop-tray)](https://github.com/Hope-Phenom/dsh-desktop-tray/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)

基于 WPF（.NET Framework 4.6.2）的 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)（`dsh`）桌面助手。托盘常驻，一键解决环境配置与 Web UI 启停的易用性问题——**不再需要手动打开命令行窗口，也不需要自己记 URL**。

> 在线页面：[hope-phenom.github.io/dsh-desktop-tray](https://hope-phenom.github.io/dsh-desktop-tray)
> 项目主页：[github.com/Hope-Phenom/dsh-desktop-tray](https://github.com/Hope-Phenom/dsh-desktop-tray)

## 特性

### 环境体检与一键修复（环境页）

| 项目 | 能力 |
|---|---|
| Node.js | 自动检测版本（PATH + 常见安装路径）；未安装时一键安装：优先 `winget install OpenJS.NodeJS.LTS`，失败自动回退官方 MSI（实时获取最新 LTS）；安装后自动刷新 PATH |
| npm 镜像源 | 显示当前 registry；可为**本工具发起的 npm 命令**单次指定源（`--registry`，不修改你的全局配置）；可选"写入全局 npmrc"（显式确认后才会影响全局） |
| dsh | 本地版本 vs 远端最新版本对比（内置 semver 比较）；一键安装/更新（`npm install -g @deepseek-ai/dsh@latest`，显式 `@latest` 规避自定义 `tag` 配置陷阱）；有新版时托盘气泡提示 |

> 一键体检全程限时（约 2 分钟），分步显示进度；网络不可达时相关项自动降级提示（如"远端版本查询失败"），**绝不会卡死界面**。

### DSH 服务启停（服务页 / 托盘菜单）

- 端口可配（1-65535）或随机端口（`--port 0`，由 OS 分配），隐藏窗口后台启动
- 解析 dsh 输出中的实际 URL（随机端口场景同样准确），健康探测（HTTP 200）通过后自动打开浏览器（可关）
- 启动前**端口占用预检** + **外部 dsh 实例扫描**：防止双实例并发写同一 `DSH_HOME` 损坏会话数据
- 停止 = 杀进程树（`taskkill /T /F`）；进程意外退出自动通知并复位状态
- 完整运行日志面板（stdout/stderr 实时滚动，便于诊断）

### 托盘

- 菜单：启动 / 停止 / 重启 DSH、打开 Web UI、复制 URL、环境体检、主窗口、开机自启、退出
- **退出 = 先停止 DSH 服务再退出**，不会遗留难以清理的 node 进程
- 运行态图标带绿色角标；关闭主窗口 = 隐藏到托盘；单实例运行（重复启动会激活已有窗口）
- 双击托盘图标默认打开主面板；可在设置中改为打开 Web UI

### 通知增强（通知增强页）

- 每轮回答结束（`turn/end`）时通知
- 可选项：子代理/子任务完成时也通知
- 支持托盘通知和/或用户自定义外部命令（例如已有的 Python 通知脚本）
- 提供一键安装/更新/卸载内置 `dsh-notify-hook` 插件
- 通知内容包含 `sessionId`、`parentSessionId`、`turn`、`reason`、`durationMs`

> 只有通过 DshNotifyicon 启动 dsh 时才会注入 `DSH_NOTIFY_ENABLED=1` 并触发通知。

### 其他

- **界面双语（中文 / English）**：启动时自动识别系统语言，无需任何配置；设置页可随时手动切换，**立即生效并自动保存**
- 开机自启写入 **HKCU 注册表 Run 键，无需管理员权限**（仅当前用户生效）
- 可选：托盘程序启动后自动启动 DSH 服务
- **清理 dsh 环境**（设置页）：停止 dsh → 卸载全局 npm 包 → 数据目录（含 API 凭据与会话）**重命名备份**为 `.dsh.bak-日期`（不直接删除，可恢复）→ 移除开机自启；全程日志透传、可取消；**不卸载 Node.js**
- 设置持久化：`%APPDATA%\DshNotifyicon\settings.json`（原子写入，损坏自动备份回退）
- 所有后台操作（npm/winget/安装）异步执行并输出到界面，不卡 UI

## 环境要求

- Windows 10 / 11（.NET Framework 4.6.2，系统自带，无需额外安装运行时）
- Node.js ≥ 18（**工具可一键安装**，见环境页）
- dsh：`npm install -g @deepseek-ai/dsh`（**工具可一键安装/更新**）

## 构建

要求：Visual Studio（含 .NET Framework 4.6.2 目标包）或带 MSBuild 的命令行。

```
msbuild DshNotifyicon.slnx /restore /p:Configuration=Release
```

产物：`DshNotifyicon\bin\Release\DshNotifyicon.exe`（双击即用，免安装）。
NuGet 依赖：`Hardcodet.NotifyIcon.Wpf`（托盘）、`Newtonsoft.Json`（设置序列化）。

> 分发时请拷贝 exe + 同目录的 `Hardcodet.NotifyIcon.Wpf.dll`、`Newtonsoft.Json.dll`，以及 `tools\dsh-notify-hook` 文件夹。

## 使用指南

1. **首次启动**：显示主窗口；之后默认隐藏到托盘（设置页可改"启动时显示主窗口"）。
2. **环境页 → 一键体检**：查看 Node.js / npm 镜像 / dsh 三项状态；缺失项点击对应按钮一键修复。
3. **服务页**：设置端口（或勾选随机端口）→ **启动 DSH** → 自动打开浏览器进入 Web UI。
4. **托盘**：日常操作都在这里——启动后状态图标变为绿点，悬停可看当前 URL。
5. **界面语言**（可选）：默认自动跟随系统；在设置页"界面语言"下拉中选择 中文 / English 可随时切换，立即生效。
6. **通知增强页**（可选）：安装/更新 dsh 通知插件，然后选择托盘通知和/或外部命令。

> 安装类操作（Node.js / dsh）会自动切到服务页日志面板实时透传安装进度，环境页同时显示进度条；
> 安装按钮会变为"取消"，可随时中止（进程树会被清理），完成后自动回到环境页并重新体检。

### 常见场景

- **端口被占用**：启动前自动检测，弹窗提示"直接打开浏览器访问该端口"或取消。
- **检测到其他 dsh 实例**（如之前手动开的命令行窗口）：弹窗三选一——停止它们并启动新实例 / 仅打开浏览器 / 放弃。这是为了防止两个实例并发写同一数据目录导致会话损坏。
- **随机端口模式**：URL 从 dsh 输出自动解析，托盘悬停、日志面板、打开 Web UI 均显示实际地址。

### 通知增强（可选）

1. 打开「通知增强」页。
2. 点击「安装/更新 dsh 通知插件」，将内置的 `dsh-notify-hook` 插件安装到 web profile。
   > dsh 的插件管理依赖 `pnpm`。若本机未安装 pnpm，工具会自动执行 `npm install -g pnpm` 后再继续；也可在「环境」页体检查看并一键安装。
3. 按需勾选：
   - **启用通知增强**：总开关。
   - **子代理/子任务完成时也通知**：子代理 `turn/end` 时也通知。
   - **显示托盘通知**：每轮回答结束后弹出系统通知。
   - **启用外部命令**：执行自定义命令/脚本，例如已有的 Python 通知脚本。
4. 点击「保存设置」；如果 dsh 已在运行，重启 dsh 后生效。
5. 如需移除，点击「卸载 dsh 通知插件」。

外部命令支持占位符：

```text
{event} {title} {sessionId} {parentSessionId} {turn} {reason} {durationMs}
```

示例：

```text
命令：python
参数：E:\QuickStart\send_notification.py {sessionId} {reason} {durationMs}
```

### 托盘设置

- **双击托盘图标**：可设为打开主面板或直接打开 Web UI。
- **托盘启动后自动启动 DSH**：开启后，DshNotifyicon 启动时自动拉起 dsh 服务。

## 无 UI 冒烟验证

```
DshNotifyicon.exe --smoke
```

执行环境检查 + dsh 真实启停（随机端口）+ HTTP 探测，结果写入 `%TEMP%\DshNotifyiconSmoke.txt`，退出码 0/1。不创建托盘与窗口，适合自动化回归。

## 已知限制

- 停止为强杀（`taskkill /T /F`）：dsh 会话每 ~5s 持久化一次，最多丢失约 5 秒对话尾部（非优雅停机）
- 镜像源默认只作用于本工具发起的 npm 命令；"写入全局 npmrc"会永久影响该用户所有 npm 操作（界面有确认）
- 工具被任务管理器强杀时 dsh 可能残留：下次启动时外部实例扫描会检测到并提示处理
- `--host` 仅支持 `127.0.0.1`（dsh 自身的限制，工具不提供该配置项）
- Debug 与 Release 构建共用单实例互斥锁，不能同时运行
- 黑色透明图标在 Windows 深色任务栏上对比度较低（运行态绿点仍可辨识状态）
- 界面语言切换立即生效并保存；托盘气泡由系统按 Windows 语言显示按钮文案（内容本身随界面语言）

## 故障排查

| 现象 | 处理 |
|---|---|
| PowerShell 里手动敲 `npm` 报 "running scripts is disabled" | 执行策略拦截了 `npm.ps1`（PowerShell 优先解析 .ps1 而非 .cmd）。**本工具不受影响**（node 直调 npm-cli.js，不经过任何脚本 shim）。手动使用请用 `npm.cmd`，或执行 `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`（无需管理员） |
| 启动 DSH 一直停在"正在启动" | 查看日志面板：首次启动需初始化 web profile，可能较慢；若提示端口占用按弹窗处理 |
| dsh 安装失败（npm 报 E404/ETARGET） | 工具已强制使用 `@latest` 规避自定义 tag 问题；仍失败时复制日志面板内容排查（网络/镜像不可达） |
| Node.js 安装弹 UAC 被取消 | 工具会给出 nodejs.org 手动安装指引；装完回环境页重新体检即可 |
| 开机自启不生效 | 检查设置页勾选状态；企业/域策略环境可能禁用 HKCU Run 键（个人电脑无此限制） |
| 一键检查长时间没有反应 | 不会发生：体检全程限时（120s），网络查询单项最多 45s，失败会降级显示"检查超时/远端版本查询失败"而非挂起；若仍异常请查看日志面板 |
| 清理 dsh 环境后想恢复数据 | 停止 dsh 后，把 `%USERPROFILE%\.dsh.bak-日期` 改名为 `.dsh` 即可恢复（含凭据与会话） |
| 托盘图标消失 | 属单实例机制：再启动一次 exe 会激活已运行实例；若确实退出，任务管理器结束 DshNotifyicon.exe 后重开 |
| 工具闪退/无响应 | 所有异常都会落盘到 `%APPDATA%\DshNotifyicon\crash-*.log`（异常详情 + 最近日志快照），下次启动会托盘提示；复现后把该文件发给开发者即可定位 |

## 目录结构

```
DshNotifyicon/
├─ DshNotifyicon.slnx        解决方案
├─ DshNotifyicon/
│  ├─ App.xaml(.cs)          单实例、托盘生命周期、--smoke 模式、事件接线、启动时应用界面语言
│  ├─ MainWindow.xaml(.cs)   环境 / 服务 / 设置 / 通知增强 / 关于 五页；语言切换时刷新全部静态文案
│  ├─ TrayIcon.cs            托盘图标与菜单（Hardcodet 代码构建；文案随语言刷新）
│  ├─ AppServices.cs         服务容器：设置 / DSH 进程 / 主窗口 / 托盘
│  ├─ Services/
│  │  ├─ Settings.cs         设置模型（含 Language 字段与通知设置）与原子持久化
│  │  ├─ Localization.cs     中英文文案表、自动识别与语言切换（Loc.T / Loc.Changed）
│  │  ├─ ProcessRunner.cs    隐藏进程执行、stdout/stderr 分离、超时、进程树杀
│  │  ├─ NodeService.cs      Node.js 检测 / winget+MSI 安装 / PATH 刷新
│  │  ├─ NpmService.cs       npm 封装（@latest、--registry 单次源、串行队列、semver）
│  │  ├─ DshProcessManager.cs  状态机、前置检查、URL 解析、健康探测、启停、DSH_NOTIFY 解析
│  │  └─ EnvironmentCheckService.cs  一键体检聚合
│  └─ Assets/app.ico         图标（DeepSeek 官方 favicon.svg 渲染；app-running 带绿点）
└─ tools/
   ├─ dsh-notify-hook/       dsh 插件：turn/end 时输出 DSH_NOTIFY 行
   ├─ gen-icons.js           图标再生成脚本（node）
   └─ favicon.svg            官方图标源文件
```

`site/` 为 GitHub Pages 落地页源码（`index.html` 双语单文件 + 图标素材），由 `.github/workflows/pages.yml` 自动部署到 [hope-phenom.github.io/dsh-desktop-tray](https://hope-phenom.github.io/dsh-desktop-tray)。

## 开发备注

- **技术栈**：.NET Framework 4.6.2（旧式 csproj，`LangVersion=7.3`），无需运行时分发；服务层不依赖 WPF 类型，便于无 UI 验证
- **新增文件记得加进 csproj**：旧式 csproj 使用显式 `<Compile Include>`，新源文件（如 `Services\Localization.cs`）必须手动登记
- **界面文案**：一律通过 `Loc.T("key")` 取词，禁止硬编码中文；新增文案先登记到 `Localization.cs` 的字符串表（[0]=中文, [1]=英文）；语言切换由 `Loc.Changed` 事件驱动各界面刷新
- **图标再生成**：`node tools/gen-icons.js`（复用 dsh 依赖树中的 `sharp`；也可 `npm i -g sharp` 后使用），改完需重新构建（图标为嵌入资源）
- **设计要点**：npm 包名操作一律显式 `@latest`；镜像源按命令注入 `--registry`；外部实例扫描用 PowerShell `-EncodedCommand` 规避引号转义；stdout/stderr 分离收集避免解析污染
