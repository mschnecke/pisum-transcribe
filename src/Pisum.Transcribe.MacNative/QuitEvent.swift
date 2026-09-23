// The sender of the quit Apple event, which tells a logout from any other quit (design D5 of add-macos-shell).

import AppKit

/// The process ID of the sender of the Apple event being handled, such as the quit event while the app handles it, or
/// 0 when no event is being handled or it names no sender. Call it on the main thread.
@_cdecl("pisum_current_quit_sender_pid")
public func pisumCurrentQuitSenderPid() -> Int32 {
    guard let event = NSAppleEventManager.shared().currentAppleEvent,
          let sender = event.attributeDescriptor(forKeyword: keySenderPIDAttr) else {
        return 0
    }

    return sender.int32Value
}
