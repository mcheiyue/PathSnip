<div align="center">
  <img src="src/PathSnip/Resources/PathSnip.ico" width="128" height="128" alt="PathSnip Logo">
  <h1>PathSnip</h1>
  <p>Screenshot & Path Management Tool for Developers</p>
  <p><a href="README.md">简体中文</a> | <a href="README_EN.md">English</a></p>
  <p>
    <img src="https://img.shields.io/badge/version-v1.2.1-brightgreen" alt="Version v1.2.1">
    <img src="https://img.shields.io/badge/.NET-Framework_4.8-blue" alt=".NET Framework 4.8">
    <img src="https://img.shields.io/badge/platform-Windows_10%2F11-blue" alt="Windows 10/11">
    <img src="https://img.shields.io/badge/license-GPL--3.0-blue" alt="License GPL-3.0">
  </p>
</div>

---

## Project Positioning

PathSnip is a desktop screenshot tool that emphasizes **smart snapping, magnifier & color picker, pinned images**, and **path/clipboard management**. It is designed for developers to quickly complete their workflow in scenarios such as capturing screenshots, annotating, and copying paths.

## Key Features

- **Smart Snap Engine**: Supports multiple modes (Auto/WindowOnly/ElementPreferred/ManualOnly), and you can hold Alt to temporarily bypass snapping.
- **Magnifier & Color Picker**: Provides pixel-level zoom during capture; press `C` to copy the color value.
- **Pinned Image**: After capturing, you can pin the image as a floating reference; supports dragging, mouse-wheel zoom, opacity adjustment, and double-click to close.
- **Selection Interaction**: After locking the selection, you can drag inside to pan it without selecting any annotation tool (this only changes the crop region and does not move existing annotations/mosaic).
- **Clipboard Mode**: Supports PathOnly/ImageOnly/ImageAndPath, and you can configure the path format.
- **Filename Template**: Supports multiple time and GUID placeholders for consistent naming.
- **Tray Resident**: Trigger via global hotkeys; tray menu includes recent capture actions and quick toggles (clipboard mode/path format), plus settings/folder/exit.

## Quick Start

1. Download the latest version from [Releases](https://github.com/mcheiyue/PathSnip/releases).
2. Run `PathSnip.exe`; the app will stay in the system tray.
3. Press `Ctrl+Shift+A` to start capture and select an area.
4. Screenshots are automatically saved to `Pictures\PathSnip`, and written to the clipboard according to your configuration.
5. In Settings, you can adjust hotkeys, save directory, clipboard mode, and smart snapping.

> Build & runtime requirements: .NET Framework 4.8 (runtime) / .NET SDK 8.0 (build).

## Hotkeys & Interaction

| Action | Description |
|------|------|
| `Ctrl+Shift+A` | Global screenshot hotkey (configurable in Settings) |
| `Esc` | Cancel capture |
| `Enter` | Overlay: Save and exit (does not steal focus when TextBox is active) |
| `Ctrl+Z` | Overlay: Undo last annotation (does not steal focus when TextBox is active) |
| `Tab` / `Shift+Tab` | Cycle through candidates/controls |
| `T` | Pin image (Pinned Image) |
| `C` | Copy current color value |
| `Alt` | Hold to bypass smart snapping |

## Detailed Features

### Smart Snapping
- Modes: `Auto` / `WindowOnly` / `ElementPreferred` / `ManualOnly`
- Toggles: `EnableSmartSnap`, `EnableElementSnap`
- Bypass: `HoldAltToBypassSnap` (hold Alt to temporarily bypass)

### Magnifier & Color Picker
- Provides pixel-level zoom positioning during capture.
- Press `C` to copy the current pixel color value to the clipboard.

### Pinned Image
- Press `T` after capture to create a pinned image window.
- Supports drag to move, mouse-wheel zoom, opacity adjustment, and double-click to close.

### Clipboard & Path Format
- Clipboard modes: `PathOnly` / `ImageOnly` / `ImageAndPath`
- Path formats: `Text` / `Markdown` / `HTML`
  - Markdown example: `![Screenshot](<file:///C:/Path/to/image.png>)`
  - HTML example: `<img src="file:///C:/Path/to/image.png"/>`
- Markdown/HTML copy mode: `SnippetOnly` / `PlainPathOnly` / `SnippetAndPlainPath` (effective only for Markdown/HTML formats)

### Filename Template
- Default template: `{yyyy}-{MM}-{dd}_{HHmmss}`
- Supported placeholders: `{yyyy}{MM}{dd}{HH}{mm}{ss}{HHmmss}{GUID}` (GUID uses 8 digits)
- Saved file extension: `.png`

## Settings & Configuration

- Config file path: `%APPDATA%\PathSnip\config.json`
- Default save directory: `Pictures\PathSnip`
- Default hotkey: `Ctrl+Shift+A`
- Smart snapping: mode selection and element snapping toggle
- Other options: clipboard mode, path format, filename template, auto-start, notifications

> **Reset default reminder**: The “Reset to defaults” action in Settings will reset the save directory to the *Pictures library root*, which is different from the default configuration `Pictures\PathSnip`. Please confirm this difference during upgrade or migration.

## Paths & Logs

- Config path: `%APPDATA%\PathSnip\config.json`
- Log path: `%APPDATA%\PathSnip\logs\` (retained for 7 days)

## Build & Release

- Local build command:
  ```bash
  dotnet build "d:\github\PathSnip\src\PathSnip\PathSnip.csproj" -c Release
  ```
- Release notes: Create a tag to trigger `release.yml`; artifacts are located at `bin/Release/net48/PathSnip.exe`.
- Release Notes are automatically extracted from `CHANGELOG.md`.

## Change Summary (v1.0.0 → v1.2.1)

- **v1.2.1**: Add “Recent” and quick toggles to tray menu; make Markdown/HTML path copy more robust; fix Settings crash and disabled-state visuals.
- **v1.2.0**: Add `Enter` to save and `Ctrl+Z` to undo in Overlay; fix focus loss after text annotation that could break hotkeys.
- **v1.1.9**: Drag to pan the selection without switching tools; stability and performance improvements for high-frequency usage.
- **v1.1.8**: Stability and UX refinements for fast pointer movement; build/documentation entry improvements.
- **v1.1.7**: Region-level smart snapping engine (UIA/MSAA/region profiling/stabilizer/mode gating/quick-move fallback).
- **v1.1.3**: Pinned image feature (T key, PinnedImageWindow, drag/zoom/opacity). Magnifier rendering and caching optimizations.
- **v1.1.2**: Pixel-level magnifier + color picker (`C` to copy).
- **v1.1.1**: Smart window snapping capability.
- **v1.0.x**: Annotation tool system, three clipboard modes, filename template, and high-DPI fixes.

## Migration & Notes

- It is recommended to back up `%APPDATA%\PathSnip\config.json` before upgrading.
- Check whether the smart snapping mode and `HoldAltToBypassSnap` match your operating habits.
- If you use “Reset to defaults”, the save directory will return to the Pictures library root. Please confirm in advance.

## License

This project is licensed under **GPL-3.0**. See [LICENSE](LICENSE) for details.

---

<div align="center">
  <sub>Made with ❤️ by <a href="https://github.com/mcheiyue">mcheiyue</a></sub>
</div>
