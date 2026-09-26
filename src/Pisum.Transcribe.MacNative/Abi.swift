// The C ABI of libPisumMac. Only @_cdecl functions with C types cross it: UTF-8 `const char*` in, Int32 status codes
// out, and memory this library allocates for the caller is freed with pisum_free. No Objective-C or Swift object
// crosses it (design D3 of add-macos-shell).

import Foundation

/// The version of the ABI. C# checks it at startup and doesn't call the library when it differs, so raise it with
/// every change of a function's signature or meaning.
@_cdecl("pisum_abi_version")
public func pisumAbiVersion() -> Int32 {
    return 5
}

/// Frees memory that a function of this library allocated for the caller.
@_cdecl("pisum_free")
public func pisumFree(_ pointer: UnsafeMutableRawPointer?) {
    free(pointer)
}
