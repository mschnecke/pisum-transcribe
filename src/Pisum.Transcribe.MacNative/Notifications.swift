// Notifications through UNUserNotificationCenter (design D8 of add-macos-shell). It needs an app bundle with a bundle
// identifier and crashes without one, so every function checks for it first and returns a status instead.
//
// Call pisum_notifications_start and pisum_notify on the main thread, because the first sets the center's delegate.

import Foundation
import UserNotifications

/// The request was accepted. For pisum_notifications_start's callback: the user allowed notifications.
let pisumNotificationsOk: Int32 = 0
/// The process doesn't run as an app bundle, so it can't show notifications.
let pisumNotificationsNoBundle: Int32 = 1
/// The user refused notifications.
let pisumNotificationsDenied: Int32 = 2
/// The authorization request failed.
let pisumNotificationsFailed: Int32 = 3

/// Shows notifications as banners while the app is active too, for example with the settings window in front.
private final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate {
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .list])
    }
}

// The center keeps only a weak reference to its delegate.
private let notificationDelegate = NotificationDelegate()

private var hasBundleIdentifier: Bool {
    return Bundle.main.bundleIdentifier != nil
}

/// Sets the delegate and asks for permission to show alerts with sound. macOS asks the user once and remembers the
/// answer, so later calls only report it. The callback runs later on a background thread with the context and
/// pisumNotificationsOk, pisumNotificationsDenied or pisumNotificationsFailed.
///
/// Returns pisumNotificationsOk when the request was made, or pisumNotificationsNoBundle without calling the callback.
@_cdecl("pisum_notifications_start")
public func pisumNotificationsStart(_ callback: @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void,
                                    _ context: UnsafeMutableRawPointer?) -> Int32 {
    guard hasBundleIdentifier else {
        return pisumNotificationsNoBundle
    }

    let center = UNUserNotificationCenter.current()
    center.delegate = notificationDelegate
    center.requestAuthorization(options: [.alert, .sound]) { granted, error in
        callback(context, granted ? pisumNotificationsOk : (error == nil ? pisumNotificationsDenied : pisumNotificationsFailed))
    }
    return pisumNotificationsOk
}

/// Adds a notification with a title and a body, UTF-8, with the default sound and no actions. It stays in the
/// notification center until the user clears it.
///
/// Returns pisumNotificationsOk when the request was added, or pisumNotificationsNoBundle.
@_cdecl("pisum_notify")
public func pisumNotify(_ title: UnsafePointer<CChar>, _ body: UnsafePointer<CChar>) -> Int32 {
    guard hasBundleIdentifier else {
        return pisumNotificationsNoBundle
    }

    let content = UNMutableNotificationContent()
    content.title = String(cString: title)
    content.body = String(cString: body)
    content.sound = .default
    let request = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
    UNUserNotificationCenter.current().add(request)
    return pisumNotificationsOk
}
