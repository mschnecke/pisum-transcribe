// Whether the process runs as an app bundle (design D3 of add-macos-setup). Outside one, macOS gives the permissions to
// the terminal that started it, and notifications crash.

import Foundation

var hasBundleIdentifier: Bool {
    return Bundle.main.bundleIdentifier != nil
}

/// 1 when the process runs as an app bundle with a bundle identifier, otherwise 0.
@_cdecl("pisum_has_bundle")
public func pisumHasBundle() -> Int32 {
    return hasBundleIdentifier ? 1 : 0
}
