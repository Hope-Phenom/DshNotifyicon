# dsh-desktop-tray — DSH Tray Helper

**English** | [简体中文](README_zh.md)

[![Build](https://github.com/Hope-Phenom/dsh-desktop-tray/actions/workflows/build.yml/badge.svg)](https://github.com/Hope-Phenom/dsh-desktop-tray/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Hope-Phenom/dsh-desktop-tray)](https://github.com/Hope-Phenom/dsh-desktop-tray/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)

A WPF (.NET Framework 4.6.2) desktop tray assistant for [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) (`dsh`). It lives in the tray and solves the usability pain of environment setup and Web UI start/stop — **no more manual terminal windows, no more memorizing URLs**.

> Project page: [hope-phenom.github.io/dsh-desktop-tray](https://hope-phenom.github.io/dsh-desktop-tray)
> Repository: [github.com/Hope-Phenom/dsh-desktop-tray](https://github.com/Hope-Phenom/dsh-desktop-tray)

## Features

### Environment check & one-click fix (Environment tab)

| Item | Capability |
|---|---|
| Node.js | Auto-detects version (PATH + common install locations); one-click install when missing: `winget install OpenJS.NodeJS.LTS` first, falling back to the official MSI (latest LTS fetched live); PATH refreshed automatically after install |
| npm mirror | Shows the current registry; can specify a mirror **per-command for npm operations issued by this tool** (`--registry`, your global config is untouched); optional "write global npmrc" (only affects the global config after explicit confirmation) |
| dsh | Local version vs. latest remote version (built-in semver comparison); one-click install/update (`npm install -g @deepseek-ai/dsh@latest`, explicit `@latest` avoids custom `tag` config traps); tray balloon when an update is available |

> The whole health check is time-bounded (~2 min) with step-by-step progress; unreachable-network items degrade gracefully (e.g. "remote version query failed") — **the UI never freezes**.

### DSH service start/stop (Service tab / tray menu)

- Configurable port (1-65535) or random port (`--port 0`, OS-assigned), launched hidden in the background
- Parses the real URL from dsh output (accurate for random ports too); opens the browser automatically once the health probe (HTTP 200) passes (can be disabled)
- **Port-in-use preflight** + **external dsh instance scan** before start: prevents two instances from corrupting sessions by writing the same `DSH_HOME` concurrently
- Stop = kill process tree (`taskkill /T /F`); unexpected exits are detected, notified, and state is reset
- Full runtime log panel (stdout/stderr streamed live for diagnostics)

### Tray

- Menu: Start / Stop / Restart DSH, Open Web UI, Copy URL, Environment Check, Main Window, Auto-start at logon, Exit
- **Exit = stops the DSH service first**, so no orphaned node processes are left behind
- Running-state icon shows a green badge; closing the main window hides to tray; single-instance (launching again activates the existing window)
- Double-click the tray icon opens the main window by default; you can change it to open the Web UI in Settings

### Notification enhancements (Notification Enhancements tab)

- Notifies on every dsh `turn/end` (each response round)
- Optionally also notifies for subagents/subtasks
- Shows tray notifications and/or runs a user-defined external command (e.g. an existing Python notification script)
- One-click install/update/uninstall of the bundled `dsh-notify-hook` plugin
- Notification payload includes `sessionId`, `parentSessionId`, `turn`, `reason`, and `durationMs`

> Notifications are only emitted when dsh is launched through dsh-desktop-tray (it injects `DSH_NOTIFY_ENABLED=1`).

### Other

- **Lightweight**: built with native WPF for Windows only; fast startup, low memory footprint, and no heavy runtime
- **Bilingual UI (中文 / English)**: auto-detects the system language at startup — no configuration needed; switch anytime from the Settings page, **takes effect immediately and is saved automatically**
- Auto-start uses the **HKCU registry Run key, no admin rights required** (current user only)
- Optional auto-start of the DSH service when the tray app launches
- **Clean up dsh environment** (Settings page): stop dsh → uninstall the global npm package → rename the data directory (API credentials & sessions) to `.dsh.bak-<date>` as a **backup, not a delete** (recoverable) → remove the logon auto-start entry; full log streaming, cancellable; **Node.js is NOT uninstalled**
- Settings persisted to `%APPDATA%\DshNotifyicon\settings.json` (atomic write; corrupt files are backed up and defaults restored)
- All background operations (npm/winget/install) run async and stream to the UI without blocking it

## Requirements

- Windows 10 / 11 (.NET Framework 4.6.2 ships with the OS — no extra runtime install)
- Node.js ≥ 18 (**one-click installable from the tool**, Environment tab)
- dsh: `npm install -g @deepseek-ai/dsh` (**one-click installable/updatable from the tool**)

## Building

Requires Visual Studio (with the .NET Framework 4.6.2 targeting pack) or a command line with MSBuild.

```
msbuild DshNotifyicon.slnx /restore /p:Configuration=Release
```

Output: `DshNotifyicon\bin\Release\DshNotifyicon.exe` (double-click to run, no install).
NuGet dependencies: `Hardcodet.NotifyIcon.Wpf` (tray), `Newtonsoft.Json` (settings serialization).

> To distribute, copy the exe together with `Hardcodet.NotifyIcon.Wpf.dll`, `Newtonsoft.Json.dll`, and the `tools\dsh-notify-hook` folder from the same output directory.

## Usage

1. **First launch**: the main window is shown; afterwards it hides to the tray by default (changeable via "Show main window on startup" in Settings).
2. **Environment tab → Health Check**: see Node.js / npm mirror / dsh status; click the button of any missing item to fix it.
3. **Service tab**: set the port (or tick random port) → **Start DSH** → the browser opens the Web UI automatically.
4. **Tray**: day-to-day operations live here — the icon gains a green dot while running; hover to see the current URL.
5. **UI Language** (optional): follows the system by default; switch to 中文 / English anytime from the "UI Language" dropdown in Settings — takes effect immediately.
6. **Notification Enhancements tab** (optional): install/update the dsh notification plugin, then choose tray notifications and/or an external command.

> Install-type operations (Node.js / dsh) automatically switch to the Service tab's log panel to stream progress, while the Environment tab shows a progress bar;
> the install button turns into "Cancel" — click it anytime to abort (the process tree is cleaned up); on success the app returns to the Environment tab and re-runs the health check.

### Common scenarios

- **Port in use**: detected before start; a dialog offers "open the browser on that port directly" or cancel.
- **Other dsh instances found** (e.g. a manually opened terminal window): three choices — stop them and start a new instance / only open the browser / abort. This prevents two instances from writing the same data directory concurrently and corrupting sessions.
- **Random port mode**: the URL is parsed from dsh output automatically; tray tooltip, log panel and "Open Web UI" all show the real address.

### Notification Enhancements (optional)

1. Open the **Notification Enhancements** tab.
2. Click **Install/Update dsh Notification Plugin** to install the bundled `dsh-notify-hook` plugin into the web profile.
   > dsh's plugin management requires `pnpm`. If pnpm is not installed, the tool automatically runs `npm install -g pnpm` and then continues; you can also check it under the Health Check on the Environment tab and install it with one click.
3. Choose your options:
   - **Enable notification enhancements** — master switch.
   - **Notify for subagents/subtasks too** — also notify on subagent `turn/end`.
   - **Show tray notifications** — show a native tray balloon after each response.
   - **Enable external command** — run a custom command/script, e.g. an existing Python notification script.
4. Click **Save Settings** and restart dsh if it is already running.
5. To remove the plugin later, click **Uninstall dsh Notification Plugin**.

External command placeholders:

```text
{event} {title} {sessionId} {parentSessionId} {turn} {reason} {durationMs}
```

Example:

```text
Command: python
Arguments: E:\QuickStart\send_notification.py {sessionId} {reason} {durationMs}
```

### Tray settings

- **Double-click tray icon**: can be set to open the main window or open the Web UI directly.
- **Auto-start DSH after tray launch**: when enabled, DSH starts automatically as soon as dsh-desktop-tray launches.

## Headless smoke test

```
DshNotifyicon.exe --smoke
```

Runs the environment check + a real dsh start/stop (random port) + HTTP probe, writing results to `%TEMP%\DshNotifyiconSmoke.txt` and exiting 0/1. No tray or window is created — suitable for automated regression.

## Known limitations

- Stop is a force kill (`taskkill /T /F`): dsh persists sessions roughly every ~5s, so at most ~5s of trailing conversation may be lost (not a graceful shutdown)
- The mirror applies only to npm commands issued by this tool by default; "Write Global npmrc" permanently affects all npm operations of that user (confirmed in the UI)
- If the tool itself is force-killed from Task Manager, dsh may be left behind: the external-instance scan detects it on next start and prompts for handling
- `--host` only supports `127.0.0.1` (a dsh limitation; the tool does not expose this option)
- Debug and Release builds share the single-instance mutex and cannot run at the same time
- The black transparent icon has low contrast on dark Windows taskbars (the running-state green dot still shows status)
- The UI language switch takes effect immediately and is saved; tray balloon buttons are localized by Windows itself (the content follows the UI language)

## Troubleshooting

| Symptom | Fix |
|---|---|
| Manual `npm` in PowerShell reports "running scripts is disabled" | Execution policy blocks `npm.ps1` (PowerShell resolves .ps1 before .cmd). **This tool is unaffected** (it invokes npm-cli.js directly via node, no script shims). For manual use, call `npm.cmd` or run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` (no admin needed) |
| Start DSH stays on "Starting" forever | Check the log panel: first launch initializes the web profile and may be slow; if the port is reported as busy, follow the dialog |
| dsh install fails (npm E404/ETARGET) | The tool always uses `@latest` to avoid custom `tag` issues; if it still fails, copy the log panel contents and investigate (network/mirror unreachable) |
| Node.js UAC prompt cancelled | The tool prints manual-install instructions for nodejs.org; run the health check again after installing |
| Auto-start doesn't work | Check the checkbox in Settings; enterprise/domain policy environments may disable the HKCU Run key (not an issue on personal PCs) |
| Health check seems stuck | Cannot happen: the whole check is time-bounded (120s), network queries cap at 45s each, and failures degrade to "check timed out / remote version query failed" instead of hanging; if still abnormal, check the log panel |
| Want data back after cleanup | Stop dsh, then rename `%USERPROFILE%\.dsh.bak-<date>` back to `.dsh` (contains credentials & sessions) |
| Tray icon disappeared | Single-instance behavior: launching the exe again activates the running instance; if it really exited, end DshNotifyicon.exe in Task Manager and relaunch |
| Tool crashes / becomes unresponsive | All exceptions are written to `%APPDATA%\DshNotifyicon\crash-*.log` (exception details + recent log snapshot) and a tray balloon is shown on next start; send that file to the developer after reproducing |

## Directory structure

```
DshNotifyicon/
├─ DshNotifyicon.slnx        Solution
├─ DshNotifyicon/
│  ├─ App.xaml(.cs)          Single instance, tray lifecycle, --smoke mode, event wiring, applies UI language at startup
│  ├─ MainWindow.xaml(.cs)   Environment / Service / Settings / Notification Enhancements / About tabs; refreshes all static texts on language switch
│  ├─ TrayIcon.cs            Tray icon & menu (built in code with Hardcodet; texts refresh with the language)
│  ├─ AppServices.cs         Service container: settings / DSH process / main window / tray
│  ├─ Services/
│  │  ├─ Settings.cs         Settings model (incl. Language field & notification settings) & atomic persistence
│  │  ├─ Localization.cs     CN/EN string table, auto-detection & language switching (Loc.T / Loc.Changed)
│  │  ├─ ProcessRunner.cs    Hidden process execution, separated stdout/stderr, timeout, process-tree kill
│  │  ├─ NodeService.cs      Node.js detection / winget+MSI install / PATH refresh
│  │  ├─ NpmService.cs       npm wrapper (@latest, per-command --registry, serialized queue, semver)
│  │  ├─ DshProcessManager.cs  State machine, preflight, URL parsing, health probe, start/stop, DSH_NOTIFY parsing
│  │  └─ EnvironmentCheckService.cs  Health-check aggregation
│  └─ Assets/app.ico         Icon (rendered from DeepSeek's official favicon.svg; app-running has a green dot)
└─ tools/
   ├─ dsh-notify-hook/       dsh plugin that emits DSH_NOTIFY lines on turn/end
   ├─ gen-icons.js           Icon regeneration script (node)
   └─ favicon.svg            Official icon source
```

`site/` is the GitHub Pages landing-page source (single-file bilingual `index.html` + icon assets), auto-deployed by `.github/workflows/pages.yml` to [hope-phenom.github.io/dsh-desktop-tray](https://hope-phenom.github.io/dsh-desktop-tray).

## Development notes

- **Stack**: .NET Framework 4.6.2 (legacy csproj, `LangVersion=7.3`), no runtime to ship; the service layer has no WPF dependency for headless verification
- **New files must be registered in the csproj**: the legacy csproj uses explicit `<Compile Include>` entries — a new source file (e.g. `Services\Localization.cs`) must be added manually
- **UI texts**: always fetch via `Loc.T("key")` — hardcoded strings are forbidden; register new texts in the `Localization.cs` table first ([0]=zh, [1]=en); language switches propagate through the `Loc.Changed` event
- **Icon regeneration**: `node tools/gen-icons.js` (reuses `sharp` from the dsh dependency tree; `npm i -g sharp` also works), then rebuild (icons are embedded resources)
- **Design invariants**: npm package names are always explicit `@latest`; mirrors are injected per-command via `--registry`; external-instance scanning uses PowerShell `-EncodedCommand` to avoid quote-escaping issues; stdout/stderr are collected separately to keep parsing clean
