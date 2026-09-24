namespace Pisum.Transcribe.Transcription;

/// <summary>
/// A compute backend of the native speech engine.
/// </summary>
internal enum NativeBackend
{
    /// <summary>
    /// The GPU backend: Vulkan on Windows, Metal on macOS.
    /// </summary>
    Gpu,

    /// <summary>
    /// The CPU backend.
    /// </summary>
    Cpu,
}
