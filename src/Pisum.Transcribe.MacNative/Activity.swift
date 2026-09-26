// App Nap (design D6 of add-macos-dictation): a menu bar app without a visible window can be napped, with a lower CPU
// priority and coalesced timers. Work the user waits for runs inside a user-initiated activity instead.
//
// ProcessInfo's activity methods are thread-safe, so these functions may be called on any thread, unlike most of this
// library.

import Foundation

/// Begins a user-initiated activity with the reason, which `pmset -g assertions` lists. Returns a retained token that
/// pisum_activity_end ends and releases, or null when the reason is null.
@_cdecl("pisum_activity_begin")
public func pisumActivityBegin(_ reason: UnsafePointer<CChar>?) -> UnsafeMutableRawPointer? {
    guard let reason else {
        return nil
    }

    let token = ProcessInfo.processInfo.beginActivity(options: .userInitiated, reason: String(cString: reason))
    return Unmanaged.passRetained(token as AnyObject).toOpaque()
}

/// Ends the activity of a token from pisum_activity_begin and releases the token. Does nothing for null.
@_cdecl("pisum_activity_end")
public func pisumActivityEnd(_ token: UnsafeMutableRawPointer?) {
    guard let token else {
        return
    }

    let activity = Unmanaged<AnyObject>.fromOpaque(token).takeRetainedValue()
    ProcessInfo.processInfo.endActivity(activity as! NSObjectProtocol)
}
