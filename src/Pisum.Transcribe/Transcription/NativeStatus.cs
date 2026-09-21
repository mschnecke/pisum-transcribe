namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The status codes of transcribe.cpp v0.2.3 (<c>transcribe_status</c>), with the same names and values as
/// <c>TranscribeCppSharp.Interop.Status</c>. Mirrored here, so only <see cref="TranscribeCppEngineFactory"/> references
/// TranscribeCppSharp.
/// </summary>
internal enum NativeStatus
{
    Ok = 0,
    ErrInvalidArg = 1,
    ErrNotImplemented = 2,
    ErrFileNotFound = 3,
    ErrGguf = 4,
    ErrUnsupportedArch = 5,
    ErrUnsupportedVariant = 6,
    ErrOom = 7,
    ErrBackend = 8,
    ErrSampleRate = 9,
    ErrUnsupportedLanguage = 10,
    ErrUnsupportedTask = 11,
    ErrUnsupportedTimestamps = 12,
    ErrAborted = 13,
    ErrBadStructSize = 14,
    ErrUnsupportedPnc = 15,
    ErrUnsupportedItn = 16,
    ErrInputTooLong = 17,
    ErrOutputTruncated = 18,
}
