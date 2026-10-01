# MonitorTray

**Turn individual monitors on and off right from the Windows system tray — no DDC/CI required.**

Do you have a second (or third) monitor that you only use sometimes, but it stays on and glows in the corner? MonitorTray adds a small icon to the system tray where every monitor is one click away: click to turn it off, click again to turn it back on.

It works with monitors that **don't support DDC/CI power control** — tools like ScreenOff (DDC "Power Control") fail on those, while MonitorTray uses the same mechanism as the built-in Windows *Settings → Display → "Disconnect this display"* (the Windows Display Config API / CCD). The monitor actually loses signal and goes to sleep, exactly as if you had unplugged the cable.

## Features

- 🖥 **Per-monitor power control from the tray** — left- or right-click the icon, then click a monitor to toggle it
- 🔌 **No DDC/CI needed** — uses the Windows Display Config API, works on monitors where DDC tools give up
- 📏 **Remembers resolution & refresh rate** — when a monitor is turned back on, its previous mode (e.g. a custom 1600×1024@165) is restored automatically instead of jumping to the native default
- 🌙 **"Put all screens to sleep"** — one menu item sends all monitors to DPMS sleep (they wake on any input)
- 🚀 **Startup entry** — optional autostart, straight from the tray menu
- 🪶 **Tiny (~90 KB), no dependencies, no admin rights** — plain .NET Framework 4.x app (built into Windows 10/11)
- 📦 **Single-file installer + clean uninstall** — installs per-user, shows up in *Settings → Apps*

## Install

Two options on the [Releases](../../releases) page:

1. **`MonitorTray.exe`** (or `MonitorTray-1.0-portable.zip`) — recommended. Fully portable: just run it, nothing is installed. Add it to startup from the tray menu (*Start with Windows*).
2. **`MonitorTraySetup.exe`** — optional installer: installs per-user into `%LOCALAPPDATA%`, adds Start-menu/desktop shortcuts and a proper entry in *Settings → Apps* for uninstalling.

No admin rights required.

## How to use

Left-click (or right-click) the tray icon:

```
Monitors — on: 2
──────────────────────────
●  DELL S2721DGF  — primary      ← click to turn OFF
●  Redmi 27 NQ                    ← click to turn OFF
──────────────────────────
Put all screens to sleep (until first mouse move)
Language  ▸  English / Русский
──────────────────────────
Start with Windows
About
Exit
```

- `●` — monitor is on; `○` — monitor is off (shown in orange, click to turn back on)
- English is the default language; switch to Russian from the **Language** menu item (the choice is remembered)
- Turning off the primary monitor is supported: the remaining one becomes primary, and everything is restored when you turn it back on
- Turning off the **last** active monitor is blocked on purpose (so you never end up with a black screen)

## Command line

`MonitorTray.exe` also works from the console (useful for scripts and global hotkeys via AutoHotkey etc.):

```
MonitorTray list          show monitors (index, name, state, resolution)
MonitorTray off 2         turn monitor #2 off
MonitorTray on 2          turn monitor #2 on
MonitorTray toggle 2      toggle monitor #2
MonitorTray restore       restore the topology saved by Windows (emergency)
MonitorTray dpms-off      put all screens to sleep
MonitorTray dbg           dump display diagnostics (read-only)
```

Note: after turning a monitor off, it moves to the end of the list — run `list` again to see fresh numbering.

## How it works (technical notes)

MonitorTray talks to the Windows **Connect and Configure Display (CCD)** API:

- `QueryDisplayConfig` / `SetDisplayConfig` — to list and change the active display topology
- turning a monitor off = submitting the topology without that monitor's path, which makes Windows drop the video output (the monitor loses signal and sleeps — same as "Disconnect this display" in Settings)
- turning it on = re-adding a stored path from `QDC_ALL_PATHS`, plus `ChangeDisplaySettingsEx` attach as a fallback
- monitor names are resolved from WMI (`WmiMonitorID`) and the EDID registry entries, mapped to CCD targets by UID and source-mode position

Some real-world quirks it already handles (found on Windows 10 22H2 + NVIDIA):

- `QueryDisplayConfig` / `DisplayConfigGetDeviceInfo` fail with `ERROR_INVALID_PARAMETER` when a non-NULL `topologyId` pointer is passed, or when the driver uses *virtual mode* (WDDM 2.7+) and the query is made without `SDC_VIRTUAL_MODE_AWARE` — both are detected and worked around
- some systems reject re-indexed mode arrays — MonitorTray always passes the original mode array untouched
- disabling the primary monitor requires patching the remaining source mode's position to (0,0)

Everything is verified to work with real hardware (Dell S2721DGF + Redmi 27 NQ on an RTX 4060 Ti), including full off/on cycles for both the primary and secondary monitor with custom resolutions.

## Compatibility

| OS | Status |
|---|---|
| Windows 11 | ✅ works |
| Windows 10 (2004+) | ✅ tested (22H2) |
| Windows 8/8.1 | ⚠️ should work, untested |
| Windows 7 | ⚠️ needs .NET Framework 4.x installed, untested |
| macOS / Linux | ❌ Windows-only (uses Win32 display APIs) |

No administrator rights required. Single user session.

## Build from source

No Visual Studio or SDK needed — the build script uses the C# compiler that ships with Windows:

```
build.cmd
```

Outputs: `MonitorTray.exe` (the app) and `MonitorTraySetup.exe` (single-file installer with the app and uninstaller embedded).

Project structure: everything is in a single small C# file (`MonitorTray.cs`, ~1200 lines, C# 5 compatible), the icon is generated by `icongen.cs`, installer/uninstaller are `Setup.cs` / `Uninstall.cs`.

## Antivirus false positives

MonitorTray is a small **unsigned** .NET executable that changes display topology, can add a registry autostart entry, and the installer unpacks embedded executables — a combination that machine-learning engines of some antiviruses flag generically (`Wacatac.B!ml`, `MSILHeracles`, etc.). This is a **false positive**: the project is fully open source, every line is in this repository, and the binaries in Releases are built from exactly this source by GitHub Actions.

What we do about it:
- the **portable `MonitorTray.exe` / zip is the recommended download** — it triggers far fewer heuristics than any installer
- false-positive reports are filed to major vendors (Microsoft, BitDefender, McAfee and their engine clones) — detections typically clear within days/weeks
- a code-signing certificate for open-source developers is on the roadmap — that removes the root cause

If your antivirus complains, you can: check the file on [VirusTotal](https://www.virustotal.com) and compare with the hashes published in the release notes, build the exe yourself from source with `build.cmd` (any Windows machine, no tools needed), or add an exclusion.

### SmartScreen warning ("Windows protected your PC")

When you run a freshly downloaded `MonitorTray.exe`, Windows SmartScreen may say *"unknown publisher"*. That is standard Windows behavior for **any** application that has no (expensive) code-signing certificate — it says nothing about this particular app. The binaries in Releases are built automatically from this very repository by GitHub Actions, so anyone can verify that nothing is added to them.

Two ways to run it:

- click **More info → Run anyway**, or
- right-click the file → **Properties** → tick **Unblock** → **OK** — then it starts normally without any warning.

A code-signing certificate for open-source developers is on the roadmap; it removes this warning completely.

## Troubleshooting

- **A monitor didn't come back on** — run `MonitorTray restore`, or open *Settings → Display*, or replug the cable
- **After turning a monitor on the resolution changed** — it is restored automatically on the *next* off/on cycle (the current mode is saved when you turn the monitor off); the mapping file is `%APPDATA%\MonitorTray_modes.txt`
- **Icon disappeared** — check the `^` overflow area in the tray

## License

[MIT](LICENSE) — free for everyone.

## Support

Issues and PRs are welcome.
