// Pasteboard privacy (design D6 of add-macos-setup): from macOS 15.4 the system may ask the user before an app reads the
// pasteboard, which a text insertion does.

import AppKit

/// How the system lets this app read the general pasteboard: -1 before macOS 15.4, which has no pasteboard privacy,
/// otherwise 0 default (asks at the first read), 1 asks at every read, 2 always allowed, 3 always denied.
@_cdecl("pisum_pasteboard_access_behavior")
public func pisumPasteboardAccessBehavior() -> Int32 {
    guard #available(macOS 15.4, *) else {
        return -1
    }

    return Int32(NSPasteboard.general.accessBehavior.rawValue)
}

/// Reads the general pasteboard's string once and discards it, so that macOS asks the user now and lists the app in its
/// settings. Returns 0.
///
/// Never call it on the main thread: macOS's alert blocks the reading thread until the user answers.
@_cdecl("pisum_pasteboard_probe")
public func pisumPasteboardProbe() -> Int32 {
    _ = NSPasteboard.general.string(forType: .string)
    return 0
}
