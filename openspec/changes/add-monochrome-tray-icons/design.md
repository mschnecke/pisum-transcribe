## Context

See proposal.md, Why. It's one of the five Windows changes before the macOS port (see `extract-ui-seams`' design for the graph). It builds on `extract-ui-seams`' `TrayStatus`.

Current state:
- **`TrayIcon.svg`** is an indigo rounded tile with a white microphone (capsule, arc, stand) on a 32-unit grid.
- **`tools/generate-tray-icon.cs`** renders it into `TrayIcon.ico` at 16–256 px with the `Svg` package and `System.Drawing`, so it runs only on Windows.
- **`DictationIcons`** draws the four state icons at startup with GDI+: the app icon plus a dot in a white ring, red `#E53935`, amber `#FFB300` or grey `#9E9E9E`.
- **The tray** is H.NotifyIcon.Wpf's `TaskbarIcon`, which takes a `System.Drawing.Icon`. After `extract-ui-seams`, `TrayIconService.SetStatus` takes a `TrayStatus`.

## Goals / Non-Goals

**Goals:**
- One glyph set for Windows now and for the macOS menu bar later.
- Every icon is readable at 100 % to 200 % scaling on a light and on a dark taskbar.

**Non-Goals:**
- A new app icon.
- New overlay colors.
- Animation in the tray.

## Decisions

### D1: The glyph and its variants

- **`Tray/TrayGlyph.svg`** is the microphone of `TrayIcon.svg` without the tile, in one color, on the same 32-unit grid.
- **Variants:**

  | Status | Windows | macOS (used from `add-macos-shell`) |
  |---|---|---|
  | Ready | black on a light taskbar, white on a dark one | template image |
  | Unavailable | the same at 50 % opacity | template image at 50 % alpha |
  | Recording | red `#E53935` | red, not a template |
  | Transcribing | amber `#C26A00` | amber, not a template |

- **Contrast:**
  - Red and amber reach at least 3:1, the contrast non-text elements need, against a light (`#F3F3F3`) and a dark (`#202020`) taskbar. Red is about 3.8:1 on both, and amber about 3.5:1 on light and 4.2:1 on dark.
  - Today's amber `#FFB300` gets only about 1.8:1 on a light taskbar.
- **The overlay** keeps its current colors.

*Rejected:*
- **Keeping the colored app icon with a colored dot:** the macOS menu bar wants monochrome template glyphs, and the icons are to be the same on both platforms (user decision).

### D2: Following the taskbar's mode

- **The value:** `TrayIconService` reads `SystemUsesLightTheme` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`. That's the mode of the taskbar and Start, not `AppsUseLightTheme`.
- **A missing value** counts as dark.
- **Changes:** a registry change notification on that key (`RegNotifyChangeKeyValue` through CsWin32, on a background thread) makes the service apply the current status again on the UI thread, through `IUiDispatcher`. A switch of the Windows mode then reaches the icon at once.

*Rejected:*
- **Following the app's theme variant**, as WPF or Avalonia report it: it reflects the apps' mode, and many users have a dark taskbar with light apps.
- **`WM_SETTINGCHANGE`:** it needs a window procedure. The Avalonia shell exposes none, so the registry notification survives the move.

### D3: Rendering at build time, on SkiaSharp

- **`tools/generate-tray-icon.cs`** moves from `Svg` and `System.Drawing` to SkiaSharp with an SVG renderer for Skia. It then runs on Windows and macOS alike.
- **It renders:**
  - `TrayIcon.ico` from `TrayIcon.svg`, as today
  - `TrayIcon.png` for the notification registration of `show-windows-notifications`
  - one ICO per status and taskbar mode from `TrayGlyph.svg`, at the sizes of today's `TrayIcon.ico` that the notification area uses (16, 20, 24, 32, 40, 48 and 64 px)
  - the macOS PNGs at 18 × 18 and 36 × 36
- **The files** are committed, as `TrayIcon.ico` is today, and embedded.
- **`DictationIcons`** goes. The tray service loads the ICOs from the resources.

*Rejected:*
- **Drawing the icons at runtime with Skia:** the icons never change, so rendering them once is simpler, and they can be checked by looking at them.

## Risks / Trade-offs

- [The monochrome glyph is harder to spot than the colored app icon in a crowded tray] → Accepted: it's the look of current Windows tray icons, and the tooltip names the app. The check below looks at both modes.
- [A change of the Windows mode doesn't reach the icon] → The registry notification (D2). If it fails, the icon is corrected at the next status change.
- [In a high-contrast theme the glyph doesn't read, because the taskbar's colors there don't follow `SystemUsesLightTheme`] → The manual check includes a high-contrast theme. If the glyph fails there, following the high-contrast colors is a follow-up.
- [The SVG renderer for Skia draws the arc differently from `Svg`] → The rendered files are committed and reviewed as images in the pull request.

## Migration Plan

1. The tool on SkiaSharp. It renders the existing `TrayIcon.ico` unchanged, compared by eye.
2. `TrayGlyph.svg` and the new icons.
3. The tray service loads them, and follows the taskbar mode.
4. A manual check: every status on a light and a dark taskbar at 100 %, 150 % and 200 %, while switching the Windows mode, and in a high-contrast theme.
5. The spec delta for "Tray icon states".

**Rollback:** revert the pull request. The old icons come back with `DictationIcons`.
