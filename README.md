<p align="center">
  <img src="docs/icon.png?v=1.3" width="96" alt="MonitorTray icon">
</p>

<h1 align="center">MonitorTray</h1>

<p align="center">
  <b>Turn individual monitors on and off right from the Windows system tray — no DDC/CI required.</b><br>
  Plus brightness control, a one-click "sleep all screens" and a clean Windows 11-style window.
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/zhevniak/MonitorTray?label=download" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/size-~260%20KB-success" alt="Size">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT license"></a>
</p>

<p align="center">
  <img src="docs/window-light.png?v=1.3" width="400" alt="MonitorTray — light theme">
  <img src="docs/window-dark.png?v=1.3" width="400" alt="MonitorTray — dark theme, per-monitor brightness">
</p>

Do you have a second (or third) monitor that you only use sometimes, but it stays on and glows in the corner? MonitorTray puts every monitor one click away: click to turn it off, click again to turn it back on.

It works even with monitors that **don't support DDC/CI power control** — tools like ScreenOff fail on those, while MonitorTray uses the same mechanism as Windows' own *Settings → Display → "Disconnect this display"* (the Display Config API). The monitor really loses signal and goes to sleep, exactly as if you had unplugged the cable — and everything comes back the way it was when you turn it on again.

## Features

- 🖥 **Per-monitor power** — turn any monitor off / on with its switch; works without DDC/CI
- 🪟 **Puts your windows back** — windows that lived on a monitor return to their places when it comes back, including maximized / minimized ones
- 📏 **Remembers resolution & refresh rate** — a custom mode like 1600×1024@165 is restored instead of jumping to the native default
- ☀️ **Brightness control** — one slider for all screens, or a separate slider for each monitor (for monitors that support DDC/CI)
- 🌙 **Put all screens to sleep** — one click, they wake on the first mouse move
- 💤 **AFK mode** — a monitor the cursor hasn't visited for a while turns off by itself and comes back with a push of the mouse (off by default)
- 🔄 **Automatic updates** — new versions are found on GitHub and installed with one click, verified by SHA-256
- 🎨 **Windows 11-style window** — light and dark theme, rounded corners and a soft shadow (on Windows 10 too), crisp Roboto font
- 🔵 **Colorful tray icon** that follows the theme you pick
- 🌐 **English / Русский**, switchable on the fly
- 🖱 **Tray menu** — right-click the icon for *Start with Windows*, *Check for updates*, *Language*, *About* and *Exit*
- ⌨️ **Command line** for scripts and hotkeys
- 🪶 **Tiny (~260 KB), no dependencies, no admin rights** — plain .NET Framework 4.x, which is built into Windows 10/11

## Install

Download from the [Releases](../../releases/latest) page:

1. **`MonitorTray.exe`** (or the portable zip) — recommended. Fully portable: just run it, nothing is installed. Turn on *Start with Windows* in the tray icon's right-click menu to launch it automatically.
2. **`MonitorTraySetup.exe`** — optional installer: installs per-user into `%LOCALAPPDATA%`, adds Start-menu / desktop shortcuts and an entry in *Settings → Apps* for uninstalling.

No admin rights required. If Windows shows *"Windows protected your PC"*, see [SmartScreen warning](#smartscreen-warning-windows-protected-your-pc) below.

## How to use

**Left-click** the tray icon — the MonitorTray window opens next to the tray. **Right-click** it for a small menu with *Start with Windows*, *Check for updates*, *Language* (EN / RU), *Open*, *About* and *Exit*.

### Monitors

Click a monitor row or its switch to turn that monitor off; click again to turn it back on. The **Primary** badge marks the main display, a turned-off monitor shows a dark screen and an **Off** badge.

- Turning off the primary monitor is supported: the remaining one becomes primary, and everything is restored when you turn it back on
- Turning off the **last** active monitor is blocked on purpose, so you never end up with a black screen

### Brightness

The **All screens** slider changes every monitor at once. Press **Per monitor** to show a separate slider for each monitor — press it again to hide them. You can also use the mouse wheel over any slider.

Brightness works over DDC/CI; if no monitor supports it, the section is simply hidden (power control does not need DDC/CI).

### Actions & settings

**Put all screens to sleep** and **AFK mode**. Click the section header to collapse it when you don't need it:

<p align="center">
  <img src="docs/window-compact.png?v=1.3" width="400" alt="Compact view with the settings collapsed">
  <img src="docs/window-russian.png?v=1.3" width="400" alt="Russian interface, dark theme">
</p>

### AFK mode

Turn it on in *Actions & settings* and set your own time with **−** / **+** or the mouse wheel — from 1 minute to 4 hours, 30 minutes by default. A monitor the mouse cursor hasn't visited for that long turns off by itself (handy for OLED screens and for a second monitor you only glance at). It is **off by default**.

- **Turning it back on:** push the mouse against the screen edge on the side where that monitor is, or click it in the window. If it turned off while you were away from the PC, it comes back on by itself with your first mouse move or key press
- **Choose the monitors:** each monitor has its own switch in the AFK card — by default all of them except the primary one
- **It never interrupts you:** the monitor with the cursor and the last monitor that is still on are never turned off, and nothing happens while a video is playing or a fullscreen game / movie is on that monitor
- Monitors turned off by AFK mode show an **AFK** badge, and they are turned back on when you exit MonitorTray

### Updates

Once a day MonitorTray checks GitHub for a new release (*Check for updates* in the right-click menu, on by default — it is a single request to the GitHub API). When there is one, a banner appears at the top of the window: press **Update**, and the new version is downloaded, checked against the release's `SHA256SUMS.txt` and installed — the portable exe replaces itself, the installed version runs the new installer silently (your autostart and shortcut choices are kept).

### Tray menu, theme and closing

- The **moon / sun** button in the top corner switches between the light and dark theme — the tray icon changes with it:

  <img src="docs/tray-icon.png?v=1.3" width="384" alt="Tray icon in the light and dark theme, on a dark and a light taskbar">

- The **✕** button, a click anywhere outside the window or **Esc** hide it to the tray. To quit, right-click the tray icon → **Exit**:

  <img src="docs/tray-menu.png?v=1.3" width="300" alt="Tray icon right-click menu">
- All settings — theme, language, AFK mode, per-monitor sliders, collapsed sections — are remembered

## Command line

`MonitorTray.exe` also works from the console — handy for scripts and global hotkeys (AutoHotkey etc.):

```
MonitorTray list          show monitors (index, name, state, resolution)
MonitorTray off 2         turn monitor #2 off
MonitorTray on 2          turn monitor #2 on
MonitorTray toggle 2      toggle monitor #2
MonitorTray restore       restore the topology saved by Windows (emergency)
MonitorTray dpms-off      put all screens to sleep
MonitorTray bright        show the brightness of DDC/CI monitors
MonitorTray dbg           dump display diagnostics (read-only)
```

After turning a monitor off it moves to the end of the list — run `list` again to see the fresh numbering.

## How it works

MonitorTray talks to the Windows **Connect and Configure Display (CCD)** API:

- `QueryDisplayConfig` / `SetDisplayConfig` list and change the active display topology
- turning a monitor off = applying the topology without that monitor's path, so Windows drops the video output (same as "Disconnect this display" in Settings)
- turning it on = re-adding the stored path from `QDC_ALL_PATHS`, with `ChangeDisplaySettingsEx` as a fallback
- monitor names come from WMI (`WmiMonitorID`) and EDID, mapped to CCD targets by UID and source-mode position
- brightness uses the Monitor Configuration API (DDC/CI: `GetMonitorBrightness` / `SetMonitorBrightness`)

Real-world quirks it already handles (Windows 10 22H2 + NVIDIA, incl. 580.x drivers):

- `QueryDisplayConfig` fails with `ERROR_INVALID_PARAMETER` when a `topologyId` pointer is passed or when the driver uses *virtual mode* (WDDM 2.7+) without `SDC_VIRTUAL_MODE_AWARE` — both are detected and worked around
- some systems reject re-indexed mode arrays — the original array is always passed untouched
- disabling the primary monitor requires moving the remaining source to (0,0)

Verified on real hardware (Dell S2721DGF + Redmi 27 NQ, RTX 4060 Ti), including full off/on cycles of both the primary and the secondary monitor with custom resolutions.

## Compatibility

| OS | Status |
|---|---|
| Windows 11 | ✅ works |
| Windows 10 (2004+) | ✅ tested (22H2) |
| Windows 8 / 8.1 | ⚠️ should work, untested |
| macOS / Linux | ❌ Windows-only (Win32 display APIs) |

## Antivirus false positives

MonitorTray is a small **unsigned** .NET program that changes the display topology, can add an autostart entry, and its installer unpacks embedded executables — a combination that machine-learning engines of some antiviruses flag generically (`Wacatac.B!ml`, `MSILHeracles`, etc.). This is a **false positive**: every line of code is in this repository, and the binaries in Releases are built from exactly this source by GitHub Actions.

- the **portable `MonitorTray.exe`** triggers far fewer heuristics than any installer — it is the recommended download
- you can check the file on [VirusTotal](https://www.virustotal.com), build it yourself with `build.cmd`, or add an exclusion

### SmartScreen warning ("Windows protected your PC")

Windows SmartScreen says *"unknown publisher"* for **any** app without an (expensive) code-signing certificate — it says nothing about this particular app. To run it:

- click **More info → Run anyway**, or
- right-click the file → **Properties** → tick **Unblock** → **OK**

## Troubleshooting

- **A monitor didn't come back on** — run `MonitorTray restore`, open *Settings → Display*, or replug the cable
- **The resolution changed after turning a monitor on** — it is restored on the next off/on cycle (the mode is saved when the monitor turns off)
- **The tray icon disappeared** — look in the `^` overflow area of the tray
- **Settings files** live in `%APPDATA%` as `MonitorTray_*.txt` — delete them to reset everything

## Build from source

No Visual Studio or SDK needed — the script uses the C# compiler that ships with Windows:

```
build.cmd
```

It produces `MonitorTray.exe` (the app) and `MonitorTraySetup.exe` (a single-file installer with the app and the uninstaller inside).

| File | What it is |
|---|---|
| `MonitorTray.cs` | the whole app: display control, brightness, window, tray, CLI (C# 5) |
| `Setup.cs`, `Uninstall.cs` | installer and uninstaller |
| `fonts/` | Roboto (Regular / Medium / SemiBold, Latin + Cyrillic), embedded into the exe |
| `MonitorTray.ico`, `app.manifest` | app icon and manifest (per-monitor DPI aware) |
| `docs/` | images for this README |
| `.github/workflows/build.yml` | builds every push; for `v*` tags publishes a release with `SHA256SUMS.txt` (used by the auto-updater) |

## License

[MIT](LICENSE) — free for everyone.

The embedded [Roboto](https://github.com/googlefonts/roboto-classic) font is licensed under the [SIL Open Font License 1.1](fonts/OFL.txt).

Issues and pull requests are welcome.
