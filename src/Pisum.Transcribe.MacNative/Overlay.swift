// The recording overlay's native settings (design D2 of add-macos-dictation), which Avalonia doesn't expose. The overlay
// stays an Avalonia window: spike M2 showed that it keeps the target app frontmost without an NSPanel.

import AppKit

/// The settings were applied.
let pisumOverlayOk: Int32 = 0
/// The window pointer was null.
let pisumOverlayNoWindow: Int32 = 1

/// Makes the window a floating, click-through overlay that stays out of Mission Control and the window cycle, and shows
/// over a full-screen app on that app's Space. Idempotent, so it may run before and after every show.
///
/// Call it on the main thread, with the window's NSWindow pointer. Returns pisumOverlayOk or pisumOverlayNoWindow.
@_cdecl("pisum_overlay_configure")
public func pisumOverlayConfigure(_ window: UnsafeMutableRawPointer?) -> Int32 {
    guard let window else {
        return pisumOverlayNoWindow
    }

    let overlay = Unmanaged<NSWindow>.fromOpaque(window).takeUnretainedValue()
    overlay.level = .floating
    overlay.ignoresMouseEvents = true
    overlay.collectionBehavior = [.transient, .ignoresCycle, .fullScreenAuxiliary, .canJoinAllSpaces]
    return pisumOverlayOk
}
