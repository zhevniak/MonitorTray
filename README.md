<p align="center">
  <img src="docs/icon.png?v=1.4" width="96" alt="MonitorTray icon">
</p>

<h1 align="center">MonitorTray</h1>

<p align="center">
  <b>All your monitors, one click away in the Windows tray.</b><br>
  Turn screens off and on, put them to sleep, set brightness, switch HDMI / DP and choose the primary display.<br>
  Turning monitors off and on works with <b>any</b> monitor, no DDC/CI needed.
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/zhevniak/MonitorTray?label=download&style=for-the-badge" alt="Download"></a>
</p>
<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/size-~280%20KB-success" alt="Size">
  <img src="https://img.shields.io/badge/admin%20rights-not%20needed-success" alt="No admin rights">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT license"></a>
</p>

<p align="center">
  <img src="docs/hero.png?v=1.4" width="820" alt="MonitorTray in the light and dark theme">
</p>

Got a second monitor that you only use sometimes, but it keeps glowing in the corner? MonitorTray turns it off with one click and brings it back just as easily, with your windows, resolution and refresh rate restored. Tools that rely on DDC/CI give up on many monitors; MonitorTray turns them off the same way as Windows' own *Disconnect this display*, so it works everywhere.

## Features

| | | |
|:-:|---|---|
| <img src="docs/features/power.png?v=1.4" width="32" alt=""> | **Turn monitors off and on** | One switch per monitor. Works with any monitor and puts your windows back when it returns |
| <img src="docs/features/sleep.png?v=1.4" width="32" alt=""> | **Sleep without flicker** | Monitors that support it just fall asleep: other screens don't blink, windows stay in place, and the monitor wakes when the cursor arrives |
| <img src="docs/features/brightness.png?v=1.4" width="32" alt=""> | **Brightness** | One slider for all screens or one per monitor, including the built-in screen of a laptop |
| <img src="docs/features/inputs.png?v=1.4" width="32" alt=""> | **HDMI / DP switch** | Flip a monitor between this PC and a laptop or console |
| <img src="docs/features/primary.png?v=1.4" width="32" alt=""> | **Primary monitor** | Make any monitor the main one right from the window |
| <img src="docs/features/afk.png?v=1.4" width="32" alt=""> | **AFK mode** | A monitor you haven't looked at for a while turns off by itself, which is handy for OLED. Off by default |
| <img src="docs/features/sleep-all.png?v=1.4" width="32" alt=""> | **Sleep all screens** | One click; they wake on the first mouse move |
| <img src="docs/features/update.png?v=1.4" width="32" alt=""> | **Automatic updates** | New versions install with one click and are checked against SHA-256 |
| <img src="docs/features/design.png?v=1.4" width="32" alt=""> | **Windows 11 design** | Light and dark theme, rounded corners and shadow (on Windows 10 too), English / Русский |
| <img src="docs/features/portable.png?v=1.4" width="32" alt=""> | **Tiny and portable** | ~280 KB, no installation and no admin rights; command line for scripts and hotkeys |

> **What needs DDC/CI?** Turning monitors off and on works with every monitor. Brightness, sleep and input switching use DDC/CI, the monitor's control channel over the video cable. MonitorTray checks what each monitor supports and shows only what actually works.

## Screenshots

<table>
  <tr>
    <td align="center" width="33%"><img src="docs/window-inputs.png?v=1.4" alt="HDMI / DP switch and the Monitor inputs card"><br><sub>HDMI / DP switch and <i>Monitor inputs</i></sub></td>
    <td align="center" width="33%"><img src="docs/window-russian.png?v=1.4" alt="Russian interface with an update banner"><br><sub>Russian interface, update banner, AFK mode</sub></td>
    <td align="center" width="33%"><img src="docs/window-compact.png?v=1.4" alt="Compact view"><br><sub>Compact view with settings collapsed</sub><br><br><img src="docs/tray-menu.png?v=1.4" alt="Right-click menu"><br><sub>Right-click menu</sub></td>
  </tr>
</table>

## Download

Get it from the [Releases](../../releases/latest) page:

- **`MonitorTray.exe`** (recommended): portable, just run it. To start it with Windows, right-click the tray icon and turn on *Start with Windows*
- **`MonitorTraySetup.exe`**: a standard Inno Setup installer; installs per-user (no admin rights), adds Start-menu / desktop shortcuts and an entry in *Settings → Apps*

Updates arrive by themselves: when a new version is out, a banner appears in the window, and one click installs it.

## How to use

- **Left-click** the tray icon to open the window. **Right-click** for *Start with Windows*, *Check for updates*, *Language*, *About* and *Exit*
- **Monitor row:** the switch turns the monitor off and on. Hover over a monitor and press **Make primary** to make it the main display. The second line shows how it's connected (DisplayPort, HDMI…)
- **Brightness:** *All screens* changes everything at once; **Per monitor** shows a slider for each screen. The mouse wheel works on sliders too
- **Actions & settings:** *Put all screens to sleep*, *AFK mode* (pick the time and the monitors) and *Monitor inputs*
- **✕**, a click outside the window or **Esc** hides it to the tray. Theme, language and all settings are remembered

<details>
<summary><b>How a monitor is turned off: sleep or disconnect</b></summary>

MonitorTray picks the best way for each monitor by itself:

- **Sleep** (badge *Sleep*): for monitors that support the DDC/CI power command. Windows doesn't rebuild the desktop, so other screens don't blink and windows stay where they are. The monitor wakes up when you move the cursor onto it, or with its switch
- **Disconnect** (badge *Off*): for all other monitors, the same as *Disconnect this display* in Windows. The monitor loses signal; its windows move to the other screens and come back when it's turned on again
- Turning off the **last** monitor that is on is blocked, so you never end up with a black screen
</details>

<details>
<summary><b>HDMI / DP input switch</b></summary>

If a laptop or a console is plugged into the same monitor, MonitorTray can switch between them. Monitors don't report which inputs have a cable in them, so mark them once in **Actions & settings → Monitor inputs**. The input of this PC is marked automatically, so you can always switch back. Then a switch like `[ DP 1 | HDMI 1 ]` appears in the monitor's row.

Mark only inputs that really have a device plugged in: switching to an empty input leaves the monitor without a picture, and you'll need the monitor's own input button to come back.
</details>

<details>
<summary><b>AFK mode</b></summary>

A monitor the cursor hasn't visited for the time you set (1 minute to 4 hours, 30 minutes by default) turns off by itself. Choose which monitors take part; by default it's every monitor except the primary one.

- It never interrupts you: the monitor with the cursor and the last monitor that is on are never turned off, and nothing happens while a video is playing or a fullscreen game or movie is on that monitor
- A monitor that went to *sleep* wakes when the cursor comes back. A *disconnected* one returns when you push the mouse against the screen edge on its side or click it in the window; if you were away from the PC, your first mouse move or key press brings it back
- When you exit MonitorTray, every monitor is turned back on
</details>

## FAQ

<details>
<summary><b>Other screens blink for a moment when I turn a monitor off</b></summary>

That's the NVIDIA driver with **G-SYNC** on: when a monitor is disconnected, it restarts the G-SYNC display. Monitors that can *sleep* never cause it; for the others it goes away with G-SYNC off. Turning a monitor back **on** doesn't blink.
</details>

<details>
<summary><b>A monitor didn't come back on</b></summary>

Run `MonitorTray restore`, open *Settings → Display*, or replug the cable.
</details>

<details>
<summary><b>The resolution changed after turning a monitor on</b></summary>

It's restored on the next off/on cycle (the mode is saved when the monitor turns off).
</details>

<details>
<summary><b>Windows says "Windows protected your PC" / my antivirus complains</b></summary>

MonitorTray isn't signed with a (paid) code-signing certificate yet, so SmartScreen shows *"unknown publisher"*: click **More info → Run anyway**, or right-click the file → **Properties** → **Unblock**.

Some antiviruses flag small unsigned tools that change display settings (`Wacatac.B!ml`, `MSILHeracles`…). This is a **false positive**: all the code is in this repository, and the release files are built from it by GitHub Actions. You can check a file on [VirusTotal](https://www.virustotal.com) or build it yourself with `build.cmd`.
</details>

<details>
<summary><b>The tray icon disappeared / I want to reset everything</b></summary>

Look in the `^` overflow area of the tray. Settings live in `%APPDATA%` as `MonitorTray_*.txt`; delete them to reset.
</details>

<details>
<summary><b>Command line</b></summary>

`MonitorTray.exe` also works from the console, which is handy for scripts and global hotkeys (AutoHotkey etc.):

```
MonitorTray list          show monitors (index, name, state, resolution)
MonitorTray off 2         turn monitor #2 off
MonitorTray on 2          turn monitor #2 on
MonitorTray toggle 2      toggle monitor #2
MonitorTray primary 2     make monitor #2 the primary one
MonitorTray restore       restore the topology saved by Windows (emergency)
MonitorTray dpms-off      put all screens to sleep
MonitorTray bright        show the brightness of DDC/CI monitors and the laptop screen
MonitorTray dbg           dump display diagnostics (read-only)
```

After turning a monitor off it moves to the end of the list, so run `list` again to see the fresh numbering.
</details>

<details>
<summary><b>How it works</b></summary>

- **Off / on:** Windows **CCD** API. `QueryDisplayConfig` / `SetDisplayConfig` apply the topology without the monitor's path (like *Disconnect this display*) and add it back later. On new NVIDIA drivers (580.x) the monitor returns through a path with a fresh source and synthesized modes; the method that works is remembered
- **Sleep, inputs, brightness:** DDC/CI VCP codes `0xD6` (power mode), `0x60` (input source) and the Monitor Configuration API for brightness. What a monitor supports is read once from its MCCS capabilities string
- **Laptop screen brightness:** WMI (`WmiMonitorBrightnessMethods`), the same as the Windows brightness slider
- **Windows return** to their monitor after it's turned on (including maximized / minimized ones); the resolution and refresh rate are saved and restored
- Handled quirks: *virtual mode* drivers (WDDM 2.7+) without `SDC_VIRTUAL_MODE_AWARE`, systems that reject re-indexed mode arrays, disabling the primary monitor

Verified on Windows 10 22H2 with an RTX 4060 Ti, a Dell S2721DGF (DisplayPort, DDC sleep) and a Redmi 27 NQ (HDMI).
</details>

## Compatibility

| OS | Status |
|---|---|
| Windows 11 | ✅ works |
| Windows 10 (2004+) | ✅ tested (22H2) |
| Windows 8 / 8.1 | ⚠️ should work, untested |

## Build from source

No Visual Studio or SDK needed; the script uses the C# compiler that ships with Windows:

```
build.cmd
```

It builds `MonitorTray.exe`; if [Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed, it also builds the installer `MonitorTraySetup.exe`.

| File | What it is |
|---|---|
| `MonitorTray.cs` | the whole app: display control, DDC/CI, window, tray, CLI (C# 5) |
| `installer.iss` | installer script (Inno Setup): per-user install, shortcuts, optional autostart, uninstall |
| `fonts/` | Roboto (Regular / Medium / SemiBold, Latin + Cyrillic), embedded into the exe |
| `MonitorTray.ico`, `app.manifest` | app icon and manifest (per-monitor DPI aware) |
| `docs/` | images for this README |
| `.github/workflows/build.yml` | builds every push; for `v*` tags publishes a release with `SHA256SUMS.txt` (used by the auto-updater) |

## License

[MIT](LICENSE). The embedded [Roboto](https://github.com/googlefonts/roboto-classic) font is licensed under the [SIL Open Font License 1.1](fonts/OFL.txt).

Issues and pull requests are welcome.
