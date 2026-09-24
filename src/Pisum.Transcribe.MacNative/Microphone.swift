// The microphone permission through AVCaptureDevice (design D3 of add-macos-setup). macOS delivers silence and no error
// to an app without it, so the state is read from here and never guessed from the audio.

import AVFoundation

/// The microphone permission: 0 not asked yet, 1 restricted, for example by a device management profile, 2 denied, 3
/// allowed.
@_cdecl("pisum_microphone_status")
public func pisumMicrophoneStatus() -> Int32 {
    return Int32(AVCaptureDevice.authorizationStatus(for: .audio).rawValue)
}

/// Asks for the microphone permission. macOS shows its prompt, with the app's NSMicrophoneUsageDescription, only while
/// the permission wasn't asked yet; later it only reports the answer. The callback runs later on a background thread
/// with the context and the new status of pisum_microphone_status.
@_cdecl("pisum_microphone_request")
public func pisumMicrophoneRequest(_ callback: @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void,
                                   _ context: UnsafeMutableRawPointer?) {
    AVCaptureDevice.requestAccess(for: .audio) { _ in
        callback(context, pisumMicrophoneStatus())
    }
}
