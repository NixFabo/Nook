<div align="center">

<img src="docs/logo.png" width="96" alt="Nook logo">

# Nook

**A minimal, modern taskbar replacement for Windows 11.**
A small pill at the edge of your screen that expands into a panel for controls, apps, windows, clipboard and more.

![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)
![Single file](https://img.shields.io/badge/source-one%20C%23%20file-D4B266)
![No install](https://img.shields.io/badge/install-none-2ea44f)
[![Build](https://github.com/NixFabo/Nook/actions/workflows/build.yml/badge.svg)](https://github.com/NixFabo/Nook/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-lightgrey)](LICENSE)

[Features](#features) · [Get started](#get-started) · [Usage](#usage) · [Customize](#customize) · [FAQ](#faq) · [Deutsch](README.de.md)

</div>

---

## Why Nook?

- **Light.** One C# file, built with the compiler that already ships with Windows. No runtime to install, no background services, no telemetry.
- **Out of the way.** The pill shows the time, the current song and your open apps, and hides itself in fullscreen games and videos.
- **Keyboard first.** Press <kbd>Win</kbd> and start typing. Every tab works with the arrow keys, <kbd>F1</kbd> lists every shortcut.
- **Yours.** More than 80 options in a built-in settings editor with live preview and search.

## Features

| Tab | What's inside |
|---|---|
| **Controls** | Wi-Fi, Bluetooth, audio, night light, dark mode and microphone tiles · volume · media card with cover art, progress and seeking · audio window to switch output devices and set volume per app |
| **Today** | Clock, week overview, timer with presets, to-dos, weather |
| **Apps** | Fast app search with calculator, favorites, recently used, quick links, web search · right-click: run as admin, file location, uninstall |
| **Taskbar** | All open windows, grouped by app and searchable |
| **Clipboard** | History with search, filters (text, images, links) and pinning |
| **Power** | Lock, sleep, sign out, restart, shut down |

**Pill:** clock, date, current song, running timer, muted-mic indicator and open apps with a hover window picker and jump lists (e.g. "New incognito window"). Scroll to change the volume, drag it anywhere.

**Also:** English and German UI · automatic scaling from Full HD to 4K · hides the Windows taskbar without rearranging desktop icons · optional global shortcut · optional test channel that runs next to your stable install.

## Get started

### Option A: installer (recommended)

1. Download **`NookSetup.exe`** from the [latest release](https://github.com/NixFabo/Nook/releases/latest).
2. Run it and click **Install**. No administrator rights needed: Nook is installed for your user only, to `%LOCALAPPDATA%\Programs\Nook`.
3. Setup adds a **Start menu** shortcut and, if you leave the box checked, starts Nook **with Windows**. A desktop shortcut is optional.

Updating: run the newer `NookSetup.exe`, your settings stay. Uninstall: *Settings → Apps → Nook*.

Windows SmartScreen may warn because the files are unsigned: **More info → Run anyway**.

### Portable

Download **`Nook.exe`** from the same release, put it in its own folder (e.g. `C:\Tools\Nook`) and run it. Settings are stored next to the exe.

### Build it yourself (about 10 seconds)

```bat
git clone https://github.com/NixFabo/Nook.git Nook
cd Nook
build.bat
installer\build-installer.bat   &:: optional, builds NookSetup.exe
```

`build.bat` uses `csc.exe` from .NET Framework 4.x, which is part of every Windows 10/11 installation. Nothing gets installed. It builds the **full** variant (media with cover art and seeking) and falls back to a **basic** variant if the Windows media API isn't available.

On first start Nook creates `config.json` and a `data` folder next to the exe.

> **Start with Windows:** right-click the pill → **Start with Windows**, or *Settings → General → Start with Windows*.

## Usage

| Action | Result |
|---|---|
| <kbd>Win</kbd> alone | Open the panel |
| <kbd>Win</kbd> + any key | Works as usual (<kbd>Win</kbd>+<kbd>E</kbd>, <kbd>Win</kbd>+<kbd>L</kbd>, …) |
| Click the pill | Open the panel |
| Just start typing | Jumps to app search |
| Click outside / <kbd>Esc</kbd> | Close the panel |
| Scroll over the pill | Volume |
| Drag the pill | Move it |
| App icon: click / middle-click / hover | Focus or minimize / close / pick a window |
| Right-click an app | Jump list, windows, close, end task |
| Right-click pill or panel | Settings, shortcuts, Task Manager, config folder, quit |
| <kbd>F1</kbd> in the panel | All keyboard shortcuts |
| <kbd>Ctrl</kbd>+<kbd>,</kbd> in the panel | Settings |

Shortcuts inside tabs use <kbd>Alt</kbd>+letter, because plain typing goes to search.

## Customize

Right-click the pill → **Settings**. Search on the left, options in the middle, live preview on the right. <kbd>Ctrl</kbd>+<kbd>S</kbd> saves and applies instantly. The editor is only loaded while it's open, so Nook stays light.

| Section | Highlights |
|---|---|
| **General** | Language, UI size, Win key, extra global shortcut, type-to-search, Windows taskbar, start with Windows |
| **Pill** | Always / at edge / off · height, font size, roundness, border · order of all elements · clock with seconds, date format · song width · grouped or per-window app icons, icon size · click, scroll and middle-click behavior · edge-line size and delay |
| **Panel** | Size, radius, border · tab style (pill / underline / text), labels (text / icons / both), alignment · start tab · open effect (morph / fade / slide / none), duration, shadow |
| **Design** | Presets (some with gradients) · every color · gradient background · card style (filled / outline / flat) and radius · opacity · font |
| **Position** | Edge, alignment, distance, monitor, drag behavior |
| **Tabs** | Turn tabs on/off and reorder them · reorder control tiles and power buttons · per-tab options |

Everything is stored in `config.json`. Back it up or share it via right-click → **Open config folder**. All keys and defaults are listed in [`config.standard.json`](config.standard.json).

## Test channel

Try a new version without touching your daily install:

1. Put the new files in a folder whose name contains `test`, e.g. `Nook-Test`, and run `build.bat`. It builds **`NookTest.exe`**.
2. On start it closes the stable Nook and copies its settings once.
3. Switch back with right-click → **Switch to stable version**.
4. Happy? Copy `Nook.cs` into your main folder and run `build.bat` there.

## FAQ

<details>
<summary><b>The Windows taskbar is gone after a crash.</b></summary>

Start Nook and quit it from the menu, or restart "Windows Explorer" in Task Manager.
</details>

<details>
<summary><b>"End task" is missing in the app menu.</b></summary>

Windows only allows it when *Settings → System → For developers → End task* is turned on.
</details>

<details>
<summary><b>No cover art or seeking for media.</b></summary>

You're running the basic variant. Check `build.log` to see why the full variant didn't build.
</details>

<details>
<summary><b>Bluetooth and night light open Settings instead of toggling.</b></summary>

Windows doesn't offer a public API for toggling them, so Nook opens the right settings page.
</details>

<details>
<summary><b>Something went wrong.</b></summary>

Errors are written to `data\error.log`. The running version is shown at the bottom of the right-click menu. Please attach both to an [issue](https://github.com/NixFabo/Nook/issues/new/choose).
</details>

## Project layout

```
Nook.cs               the whole app (C# 5, WPF, .NET Framework 4.x)
build.bat             one-click build with the compiler built into Windows
nook.ico              app icon
config.standard.json  all settings with default values
installer/            NookSetup.exe: per-user installer (Setup.cs, manifest, build script)
.github/workflows     CI build; tags like v1.0.0 publish a release with NookSetup.exe and Nook.exe
```

## Releasing

Push a tag and GitHub Actions does the rest:

```bash
git tag v1.0.0
git push --tags
```

The workflow builds `Nook.exe` and `NookSetup.exe`, stamps the version into both and publishes them as a GitHub release.

## Contributing

Issues and pull requests are welcome. Nook targets the C# 5 compiler that ships with Windows, so please avoid newer syntax (`$"..."`, `?.`, `nameof`, `out var`, expression-bodied members). The CI build checks every push.

## License

[MIT](LICENSE)
