# DshNotifyicon — DSH 托盘助手

基于 WPF（.NET Framework 4.6.2）的 DeepSeek Harness（`dsh`）桌面助手：托盘常驻，
一键解决环境配置与 Web UI 启停的易用性问题，无需手动打开命令行窗口。

## 功能

- **环境体检与一键修复**（环境页）
  - Node.js：检测版本；未安装时 winget 安装 OpenJS.NodeJS.LTS，失败回退官方 MSI（自动取最新 LTS）
  - npm 镜像：显示当前 registry；可为**本工具的命令**单次指定源（`--registry`，不改全局配置）；
    可选"写入全局 npmrc"（需确认）
  - dsh：本地版本 vs 远端最新（显式 `@latest`，规避自定义 `tag` 配置陷阱）；一键安装/更新
- **DSH 服务启停**（服务页 / 托盘菜单）
  - 端口可配（1-65535）或随机端口（`--port 0`）；隐藏窗口后台启动
  - 解析 dsh 输出中的实际 URL（随机端口场景），健康探测后自动打开浏览器（可关）
  - 启动前端口占用预检 + **外部 dsh 实例扫描**（防止双实例并发写同一 DSH_HOME 损坏会话）
  - 停止 = 杀进程树（`taskkill /T /F`）；意外退出自动通知并复位状态
- **托盘**：启动/停止/重启、打开 Web UI、复制 URL、环境体检、主窗口、开机自启、退出；
  **退出 = 先停止 DSH 服务再退出**，不会遗留难以清理的 node 进程；运行态图标带绿点；
  关闭主窗口 = 隐藏到托盘；单实例运行
- **设置持久化**：`%APPDATA%\DshNotifyicon\settings.json`（原子写，损坏自动回退）

## 构建

```
msbuild DshNotifyicon.slnx /restore /p:Configuration=Release
```

产物：`DshNotifyicon\bin\Release\DshNotifyicon.exe`（依赖 .NET Framework 4.6.2，Win10+ 自带）。
NuGet 包：`Hardcodet.NotifyIcon.Wpf`（托盘）、`Newtonsoft.Json`（设置）。

## 无 UI 冒烟验证

```
DshNotifyicon.exe --smoke
```

执行环境检查 + dsh 真实启停（随机端口）+ HTTP 探测，结果写入 `%TEMP%\DshNotifyiconSmoke.txt`，
退出码 0/1。不创建托盘与窗口。

## 已知限制

- 停止为强杀（`taskkill /T /F`）：dsh 会话每 ~5s 持久化一次，最多丢失约 5 秒对话尾部
- 镜像源默认仅作用于本工具发起的 npm 命令；"写入全局 npmrc"会永久影响该用户所有 npm 操作
- 工具被任务管理器强杀时 dsh 可能残留：下次启动会检测到并提示接管/停止
- `--host` 仅支持 127.0.0.1（dsh 自身的限制，工具不提供该配置项）

## 目录结构

```
DshNotifyicon/
├─ App.xaml(.cs)            单实例、托盘生命周期、--smoke 模式、事件接线
├─ MainWindow.xaml(.cs)     环境 / 服务 / 设置 三页
├─ TrayIcon.cs              托盘图标与菜单（Hardcodet 代码构建）
├─ AppServices.cs           服务容器：设置/DSH 进程/主窗口/托盘
├─ Services/
│  ├─ Settings.cs           设置模型与原子持久化
│  ├─ ProcessRunner.cs      隐藏进程执行、stdout/stderr 分离、超时、进程树杀
│  ├─ NodeService.cs        Node.js 检测 / winget+MSI 安装 / PATH 刷新
│  ├─ NpmService.cs         npm 封装（@latest、--registry 单次源、串行队列、semver）
│  ├─ DshProcessManager.cs  状态机、前置检查、URL 解析、健康探测、启停
│  └─ EnvironmentCheckService.cs  一键体检聚合
└─ Assets/app.ico           图标（DeepSeek 官方 favicon.svg 渲染；app-running 带绿点）
```

图标再生成：`node tools/gen-icons.js`（依赖 dsh 依赖树中的 sharp，或自行安装）。
