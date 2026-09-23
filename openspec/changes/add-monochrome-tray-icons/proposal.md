## Why

The tray icon is the colored app icon with a colored dot for the state, drawn with GDI+ at startup. The macOS menu bar, which Pisum Transcribe will join, wants monochrome template glyphs. The icons are to be the same on both platforms (user decision in explore mode). Monochrome is also how current Windows tray icons look, and it follows the taskbar's light or dark mode. This change gives the tray one glyph set, rendered once at build time, on today's WPF tray. The macOS menu bar then uses the same files. It carries no regret even if the Avalonia spike fails.

## What Changes

- **The status icon** becomes a monochrome microphone glyph, the microphone of the app icon without its tile:
  - **ready:** black on a light taskbar, white on a dark one. It follows the taskbar's mode, and it changes at once when the user switches the Windows mode.
  - **unavailable:** the same glyph, dimmed
  - **recording:** red
  - **transcribing:** amber, darker than today's so that it reads on a light taskbar
- **The app icon stays** as it is, for the exe, the installed-apps entry, the windows and notifications.
- **The icons are rendered at build time** by `tools/generate-tray-icon.cs`, which moves to SkiaSharp so that it runs on Windows and macOS alike. It also renders the macOS menu bar PNGs, which `add-macos-shell` uses.
- **`DictationIcons` and GDI+ drawing at startup go away.** The tray loads the rendered icons.
- Not included:
  - the overlay's colors, which stay
  - the macOS menu bar itself, which comes with `add-macos-shell`

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `dictation`: "Tray icon states" says that the icon at rest follows the taskbar's light or dark mode, and that the ready icon is drawn in the taskbar's foreground color. The distinct icon for each state, and every tooltip, stay as they are.

## Impact

- **Depends on:** `extract-ui-seams` (`TrayStatus`, so only the tray service changes).
- **New files**, committed as `TrayIcon.ico` is today:
  - `src/Pisum.Transcribe/Tray/TrayGlyph.svg`, the status glyph, and `Tray/TrayIcon.png`, the app icon at 256 px for `show-windows-notifications`
  - `Tray/Windows/`: the status ICOs, embedded, and `TaskbarModeWatcher`
  - `Tray/MacOS/`: the menu bar PNGs at `@1x` and `@2x`, which follow Apple's rules for menu bar extras and which `add-macos-shell` uses
- **Code:**
  - `Tray/TrayIconService`: it loads the icons, reads the taskbar mode, and watches it for changes.
  - `Dictation/DictationIcons` goes.
  - `NativeMethods.txt` gains `RegNotifyChangeKeyValue`.
- **Tool:** `tools/generate-tray-icon.cs` uses SkiaSharp and an SVG renderer for Skia instead of `Svg` and `System.Drawing`. It is a tool dependency in `Directory.Packages.props`' `Tools` group, and isn't shipped.
- **Tests:** the tray service maps each status and taskbar mode to the right icon. The mode reading is tested against a fake registry value.
- **User-visible:** a new tray icon.
