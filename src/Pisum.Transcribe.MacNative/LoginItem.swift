// "Open at login" (design D7 of add-macos-packaging): the running app bundle as a login item through SMAppService,
// which macOS lists in System Settings → General → Login Items and drops when the app is moved to the Trash.
// SMAppService.mainApp is the bundle that runs, so a development build registers itself, not the installed app.

import Foundation
import ServiceManagement

/// The login item's status: 0 not registered, 1 enabled, 2 requires approval in Login Items, 3 not found, such as
/// outside an app bundle.
@_cdecl("pisum_login_item_status")
public func pisumLoginItemStatus() -> Int32 {
    switch SMAppService.mainApp.status {
    case .notRegistered:
        return 0
    case .enabled:
        return 1
    case .requiresApproval:
        return 2
    default:
        return 3
    }
}

/// Registers the app as a login item. Returns 0, or the error code of ServiceManagement when it failed.
@_cdecl("pisum_login_item_register")
public func pisumLoginItemRegister() -> Int32 {
    return loginItemStatusCode { try SMAppService.mainApp.register() }
}

/// Unregisters the app as a login item. Returns 0, or the error code of ServiceManagement when it failed.
@_cdecl("pisum_login_item_unregister")
public func pisumLoginItemUnregister() -> Int32 {
    return loginItemStatusCode { try SMAppService.mainApp.unregister() }
}

private func loginItemStatusCode(_ action: () throws -> Void) -> Int32 {
    do {
        try action()
        return 0
    } catch let error as NSError {
        // Never 0, which means success.
        return error.code == 0 ? -1 : Int32(truncatingIfNeeded: error.code)
    }
}
