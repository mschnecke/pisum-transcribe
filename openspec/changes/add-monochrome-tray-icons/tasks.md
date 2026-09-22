## 1. The icon tool on SkiaSharp

- [ ] 1.1 Move `tools/generate-tray-icon.cs` from `Svg` and `System.Drawing` to SkiaSharp with an SVG renderer for Skia, pinned in the `Tools` group of `Directory.Packages.props`. Drop `TargetFramework=net10.0-windows` for `net10.0` (design D3). Verify:
  - `dotnet run tools/generate-tray-icon.cs` runs on Windows and on macOS.
  - The regenerated `TrayIcon.ico` has the same eight sizes and looks the same as the committed one, compared by eye in the pull request.

## 2. The glyph and its variants

- [ ] 2.1 Add `src/Pisum.Transcribe/Tray/TrayGlyph.svg`: the microphone of `TrayIcon.svg` without the tile, in one color, on the 32-unit grid (design D1). Verify: it renders at 16 px with the capsule, the arc and the stand distinct, checked by eye.
- [ ] 2.2 Let the tool render from `TrayGlyph.svg`:
  - one ICO per status and taskbar mode at 16, 20, 24, 32, 40, 48 and 64 px: ready and unavailable in black and white, unavailable at 50 % opacity, recording in `#E53935`, transcribing in `#C26A00`
  - `TrayIcon.png` from `TrayIcon.svg`
  - the macOS PNGs at 18 × 18 and 36 × 36: the template glyph in black with alpha, the template at 50 % alpha, red and amber

  Commit the files, and embed the ICOs as resources (design D1, D3). Verify: the pull request shows every file, and each ICO holds the seven sizes.

## 3. The tray

- [ ] 3.1 `TrayIconService` loads the ICOs from the resources and maps `TrayStatus` and the taskbar mode to them. Delete `DictationIcons` and `DictationIconsTests`, and remove its registration from `DictationServiceCollectionExtensions` (design D3). Verify:
  - A new STA test `TrayIconServiceTests.SetStatus_EachStatusAndMode_ShowsMatchingIcon` passes for the four statuses in both modes.
  - `grep -rn "System.Drawing.Drawing2D\|GetHicon" src/Pisum.Transcribe` finds nothing.
- [ ] 3.2 Read the taskbar mode from `SystemUsesLightTheme` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, with a missing value as dark. Watch the key with `RegNotifyChangeKeyValue` through CsWin32 on a background thread, and apply the current status again through `IUiDispatcher` (design D2). Put the registry reads behind a small interface, so the tests can fake it. Verify that these new tests pass:
  - `TaskbarMode_ValueOne_IsLight`
  - `TaskbarMode_ValueZero_IsDark`
  - `TaskbarMode_ValueMissing_IsDark`
  - `TaskbarModeChanged_WhileReady_AppliesIconForNewMode`, with a fake watcher that raises the change

## 4. Checks and specs

- [ ] 4.1 A check by hand on Windows 11 (design Migration Plan, step 4):
  - every status on a light and a dark taskbar at 100 %, 150 % and 200 %
  - switching the Windows mode while the app runs, where the icon follows at once
  - a high-contrast theme, where the result goes into design.md, and a follow-up is noted if the glyph doesn't read

  Verify: the results are in the pull request description, with screenshots.
- [ ] 4.2 Confirm that the spec delta `specs/dictation/spec.md` matches the result of 4.1, and run `openspec validate add-monochrome-tray-icons`. Verify: it reports the change as valid.

## 5. Documentation

- [ ] 5.1 Update `README.md` where it describes or shows the tray icon, and `CLAUDE.md` *Layout* (`Tray/` holds `TrayGlyph.svg` and the rendered icons, and the tool renders both). Verify: no document mentions the colored dot on the app icon any more.
