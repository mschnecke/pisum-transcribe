## 1. The icon tool on SkiaSharp

- [ ] 1.1 Move `tools/generate-tray-icon.cs` from `Svg` and `System.Drawing` to SkiaSharp with an SVG renderer for Skia, pinned in the `Tools` group of `Directory.Packages.props`. Drop `TargetFramework=net10.0-windows` for `net10.0` (design D3). Verify:
  - `dotnet run tools/generate-tray-icon.cs` runs on Windows and on macOS.
  - The regenerated `TrayIcon.ico` has the same eight sizes and looks the same as the committed one, compared by eye in the pull request.

## 2. The glyph and its variants

- [ ] 2.1 Add `src/Pisum.Transcribe/Tray/TrayGlyph.svg`: the microphone of `TrayIcon.svg` without the tile, in one color, redrawn with a viewBox fitted to the microphone and a stroke width tuned for 16 px (design D1). Verify: at 16 px the glyph fills the frame, with the capsule, the arc and the stand distinct and crisp, checked by eye against a Windows tray glyph such as the volume icon.
- [ ] 2.2 Let the tool render these files, with the names and folders of the layout in design D3:
  - into `src/Pisum.Transcribe/Tray/Windows/`, from `TrayGlyph.svg`: the four ICOs `TrayGlyph.{Ready,Unavailable}.{Light,Dark}.ico`, and `TrayGlyph.Recording.ico` and `TrayGlyph.Transcribing.ico`, each at 16, 20, 24, 32, 40, 48 and 64 px. Ready and unavailable are black for a light taskbar and white for a dark one, unavailable at 50 % opacity, recording `#E53935` and transcribing `#C26A00`.
  - into `src/Pisum.Transcribe/Tray/`, from `TrayIcon.svg`: `TrayIcon.png` at 256 px. It isn't copied to the output; `show-windows-notifications` adds that.
  - into `src/Pisum.Transcribe/Tray/MacOS/`, from `TrayGlyph.svg`, following the macOS rules in design D3: `TrayGlyph.ReadyTemplate` and `TrayGlyph.UnavailableTemplate` (black with alpha only, unavailable at 50 % alpha), `TrayGlyph.Recording` (red) and `TrayGlyph.Transcribing` (amber), each as `.png` at 18 × 18 px and `@2x.png` at 36 × 36 px, with the glyph about 16 pt tall and centered, rendered separately per size, in sRGB, and the `@2x` files at 144 DPI. They aren't referenced by the project.

  Commit the files, and embed the ICOs with `<EmbeddedResource Include="Tray\Windows\*.ico"/>` (design D1, D3). Verify:
  - every file of the layout exists at its path, and the pull request shows each one as an image
  - each ICO holds the seven sizes
  - the template PNGs contain no color: every pixel is black, and only the alpha varies

## 3. The tray

- [ ] 3.1 `TrayIconService` loads the ICOs from the resources and maps `TrayStatus` and the taskbar mode to them. Delete `DictationIcons` and `DictationIconsTests`, and remove its registration from `DictationServiceCollectionExtensions` (design D3). Verify:
  - `IconFor` from `extract-ui-seams` maps the status and the taskbar mode to the loaded icons. A new test `TrayIconServiceTests.IconFor_EachStatusAndMode_ReturnsMatchingIcon` passes for the four statuses in both modes, and replaces `IconFor_EachStatus_ReturnsItsIcon`.
  - The two copy tests from `extract-ui-seams` keep guarding that H.NotifyIcon, which disposes the icon it replaces, never disposes an icon the tray shows again: `SetStatus_StatusShownAgainAfterAnother_DoesNotThrow` stays, and `Remove_AfterSetStatus_LeavesDictationIconsUsable` is rewritten for the loaded icons.
  - The icons are loaded once, at `SystemInformation.SmallIconSize` (design D3). A new test `TrayIconServiceTests.LoadIcons_Always_LoadsSmallIconSize` passes: each loaded icon's size equals `SystemInformation.SmallIconSize`.
  - `grep -rn "System.Drawing.Drawing2D\|GetHicon" src/Pisum.Transcribe` finds nothing.
- [ ] 3.2 Add `TaskbarModeWatcher` in `src/Pisum.Transcribe/Tray/Windows/`, a singleton behind a small interface with `Current` and `Changed` (design D2):
  - It reads `SystemUsesLightTheme` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, with a missing value as dark. The registry reads sit behind a seam, so the tests can fake them.
  - Its background thread waits on the registry event and a stop event, re-arms `RegNotifyChangeKeyValue` (CsWin32) after each notification, and raises `Changed` only when the mode flips. `Dispose` sets the stop event.
  - `TrayIconService` subscribes to `Changed` and applies the current status again through `IUiDispatcher`.
  - `Tray/Windows` gets its "not a namespace provider" entry in `Pisum.Transcribe.csproj.DotSettings` and `Pisum.Transcribe.Tests.csproj.DotSettings`.

  Verify that these new tests pass, the watcher's tests in `tests/Pisum.Transcribe.Tests/Tray/Windows/`:
  - `TaskbarMode_ValueOne_IsLight`
  - `TaskbarMode_ValueZero_IsDark`
  - `TaskbarMode_ValueMissing_IsDark`
  - `Changed_ValueRewrittenWithSameMode_IsNotRaised`
  - `Dispose_WhileWatching_StopsThread`
  - `TaskbarModeChanged_WhileReady_AppliesIconForNewMode` in `TrayIconServiceTests`, with a fake watcher that raises the change

## 4. Checks and specs

- [ ] 4.1 A check by hand on Windows 11 (design Migration Plan, step 4):
  - every status on a light and a dark taskbar at 100 %, 150 % and 200 %
  - switching the Windows mode while the app runs, where the icon follows at once
  - a high-contrast theme, where the result goes into design.md, and a follow-up is noted if the glyph doesn't read

  Verify: the results are in the pull request description, with screenshots.
- [ ] 4.2 Confirm that the spec delta `specs/dictation/spec.md` matches the result of 4.1, and run `openspec validate add-monochrome-tray-icons`. Verify: it reports the change as valid.

## 5. Documentation

- [ ] 5.1 Update `README.md` where it describes or shows the tray icon, and `CLAUDE.md` *Layout* (`Tray/` holds `TrayGlyph.svg` and `TrayIcon.png`, `Tray/Windows/` the status ICOs and `TaskbarModeWatcher`, `Tray/MacOS/` the menu bar PNGs, and the tool renders all of them). Verify: no document mentions the colored dot on the app icon any more.
