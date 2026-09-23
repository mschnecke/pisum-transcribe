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

- **`Tray/TrayGlyph.svg`** is the microphone of `TrayIcon.svg` without the tile, in one color, redrawn for the notification area:
  - Its viewBox fits the microphone with about one unit of padding, so the glyph fills the frame as Windows' own tray glyphs do. On the 32-unit grid of `TrayIcon.svg` it would cover only about 18 × 25 units, about 9 × 12 px at 16 px.
  - The stroke width is tuned so that the capsule, the arc and the stand stay distinct and crisp at 16 px.
  - The shape stays that of the app icon's microphone, but it isn't a pixel copy.
- **Variants:**

  | Status | Windows | macOS (used from `add-macos-shell`) |
  |---|---|---|
  | Ready | black on a light taskbar, white on a dark one | template image |
  | Unavailable | the same at 50 % opacity | template image at 50 % alpha |
  | Recording | red `#E53935` | red, not a template |
  | Transcribing | amber `#C26A00` | amber, not a template |

- **Contrast:**
  - Red and amber reach at least 3:1, the contrast non-text elements need, against a light (`#F3F3F3`) and a dark (`#202020`) taskbar. Red is about 3.8:1 on both, and amber about 3.5:1 on light and 4.2:1 on dark.
  - Today's amber `#FFB300` gets only about 1.6:1 on a light taskbar.
- **The overlay** keeps its current colors.

*Rejected:*
- **Keeping the colored app icon with a colored dot:** the macOS menu bar wants monochrome template glyphs, and the icons are to be the same on both platforms (user decision).

### D2: Following the taskbar's mode

- **The value:** `TaskbarModeWatcher` reads `SystemUsesLightTheme` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`. That's the mode of the taskbar and Start, not `AppsUseLightTheme`.
- **A missing value** counts as dark.
- **Changes:** a registry change notification on that key (`RegNotifyChangeKeyValue` through CsWin32) makes the tray apply the current status again. A switch of the Windows mode then reaches the icon at once.
- **The watcher:** `TaskbarModeWatcher` is a singleton in `Tray/Windows/`, in the `Pisum.Transcribe.Tray` namespace.
  - It offers `Current` (light or dark) and a `Changed` event behind a small interface. The interface stays free of Windows types, so the Avalonia shell's tray service can subscribe to it as well.
  - Its background thread waits on the registry event and a stop event together. It re-arms `RegNotifyChangeKeyValue` after each notification, because a notification fires only once.
  - It raises `Changed` only when the mode flips, because the `Personalize` key also changes for the accent color and transparency.
  - `Dispose` sets the stop event, so the thread ends well within `HostOptions.ShutdownTimeout`. The thread is a background thread in case it doesn't.
- **The tray:** `TrayIconService` subscribes to `Changed` and applies the last status again on the UI thread, through `IUiDispatcher`.

*Rejected:*
- **Following the app's theme variant**, as WPF or Avalonia report it: it reflects the apps' mode, and many users have a dark taskbar with light apps.
- **`WM_SETTINGCHANGE`:** it needs a window procedure. The Avalonia shell exposes none, so the registry notification survives the move.
- **The watch inside `TrayIconService`:** fewer types, but the tray service would own a thread and be harder to test, and the Avalonia shell's tray service would have to repeat it.

### D3: Rendering at build time, on SkiaSharp

- **`tools/generate-tray-icon.cs`** moves from `Svg` and `System.Drawing` to SkiaSharp with an SVG renderer for Skia. It then runs on Windows and macOS alike.
- **It renders:**
  - `TrayIcon.ico` from `TrayIcon.svg`, as today
  - `TrayIcon.png` at 256 px, the largest frame of `TrayIcon.ico`, for the notification registration of `show-windows-notifications`
  - one ICO per status and taskbar mode from `TrayGlyph.svg`, at the sizes of today's `TrayIcon.ico` that the notification area uses (16, 20, 24, 32, 40, 48 and 64 px)
  - the macOS PNGs at 18 × 18 and 36 × 36 (see *The macOS menu bar glyphs* below)
- **The files** are committed, as `TrayIcon.ico` is today:

  ```
  src/Pisum.Transcribe/Tray/
    TrayIcon.svg, TrayIcon.ico          the app icon (as today)
    TrayIcon.png                        256 px, for the notification registration
    TrayGlyph.svg                       the status glyph
    Windows/
      TrayGlyph.Ready.Light.ico         Light and Dark name the taskbar's mode:
      TrayGlyph.Ready.Dark.ico            a black glyph on Light, a white one on Dark
      TrayGlyph.Unavailable.Light.ico
      TrayGlyph.Unavailable.Dark.ico
      TrayGlyph.Recording.ico           the same on both taskbars
      TrayGlyph.Transcribing.ico
    MacOS/
      TrayGlyph.ReadyTemplate.png, TrayGlyph.ReadyTemplate@2x.png
      TrayGlyph.UnavailableTemplate.png, TrayGlyph.UnavailableTemplate@2x.png
      TrayGlyph.Recording.png, TrayGlyph.Recording@2x.png
      TrayGlyph.Transcribing.png, TrayGlyph.Transcribing@2x.png
  ```

  - **The Windows ICOs** are named by the taskbar's mode, because that's the value `TaskbarModeWatcher` reports and `IconFor` maps. They are embedded with one item, `<EmbeddedResource Include="Tray\Windows\*.ico"/>`. Their resource names contain `.Tray.Windows.` (for example `Pisum.Transcribe.Tray.Windows.TrayGlyph.Ready.Light.ico`), although the platform folder adds nothing to the namespace.
  - **`TrayIcon.png`** isn't copied to the output by this change. `show-windows-notifications` adds the copy next to the exe when it uses the file; the MSI then picks it up from the publish folder without a change to the WiX source.
  - **The macOS PNGs** aren't referenced by the project until `add-macos-shell` wires them up. `AppIcon.icns` also comes with `add-macos-shell`, rendered by the same tool.
- **The macOS menu bar glyphs** follow Apple's Human Interface Guidelines for menu bar extras:
  - **Template images** for ready and unavailable: black with alpha only, no color, so macOS draws them in the menu bar's color on a light, dark or tinted menu bar, and dims them on an inactive display. The file names end in `Template`, the AppKit convention that marks a template image, in addition to `MacOSProperties.IsTemplateIcon` in `add-macos-shell`.
  - **Unavailable** is the template at 50 % alpha, which macOS keeps when it tints a template.
  - **Color** is used only to convey status: red while recording and amber while transcribing, as full-color images that aren't templates. They are drawn without a tile, a shadow or a gradient, like the template.
  - **Size:** an 18 × 18 pt canvas, 18 px at `@1x` and 36 px at `@2x`, with the glyph about 16 pt tall and centered, so it sits inside the menu bar's height without touching its edges. Each size is rendered from the SVG, not scaled from the other, so the strokes stay on the pixel grid.
  - **The files** are sRGB PNGs, and the `@2x` files declare 144 DPI, so that each file reports its size in points, 18 pt, when it is loaded without the `@2x` convention.
- **`DictationIcons`** goes. The tray service loads the ICOs from the resources once at startup, each at `SystemInformation.SmallIconSize`: 16 px at 100 %, 24 px at 150 % and 32 px at 200 % scaling, all frames the tool renders. H.NotifyIcon passes a `System.Drawing.Icon` to the tray as it is, so the frame chosen at load is the one Windows shows. Loaded at the default size, the 32 px frame would be scaled down and look soft at 150 %.

*Rejected:*
- **Drawing the icons at runtime with Skia:** the icons never change, so rendering them once is simpler, and they can be checked by looking at them.

## Risks / Trade-offs

- [The monochrome glyph is harder to spot than the colored app icon in a crowded tray] → Accepted: it's the look of current Windows tray icons, and the tooltip names the app. The check below looks at both modes.
- [A change of the Windows mode doesn't reach the icon] → The registry notification (D2). If it fails, the icon is corrected at the next status change.
- [In a high-contrast theme the glyph doesn't read, because the taskbar's colors there don't follow `SystemUsesLightTheme`] → The manual check includes a high-contrast theme. If the glyph fails there, following the high-contrast colors is a follow-up.
- [The DPI of the primary display changes while the app runs, and the tray keeps the frame loaded at startup, which Windows then scales] → Accepted: the icons are loaded once, and the next start picks the right frame.
- [The macOS glyphs can't be checked in the menu bar by this change, which runs on Windows] → They are reviewed as images in the pull request. The spike M1 of `move-windows-shell-to-avalonia` and `add-macos-shell` check them in the menu bar, including that Avalonia loads the `@2x` file at 18 pt.
- [On macOS, the red and amber glyphs aren't dimmed on an inactive display, as template images are] → Accepted: they convey a status for the few seconds of a dictation.
- [While recording, macOS shows its own orange microphone indicator next to the red glyph] → Accepted: it confirms the recording. The amber of transcribing shows only after the recording, when the system indicator has gone.
- [The SVG renderer for Skia draws the arc differently from `Svg`] → The rendered files are committed and reviewed as images in the pull request.

## Migration Plan

1. The tool on SkiaSharp. It renders the existing `TrayIcon.ico` unchanged, compared by eye.
2. `TrayGlyph.svg` and the new icons.
3. The tray service loads them, and follows the taskbar mode.
4. A manual check: every status on a light and a dark taskbar at 100 %, 150 % and 200 %, while switching the Windows mode, and in a high-contrast theme.
5. The spec delta for "Tray icon states".

**Rollback:** revert the pull request. The old icons come back with `DictationIcons`.
