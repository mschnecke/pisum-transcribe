namespace Pisum.Transcribe.Transcription;

/// <summary>
/// A compute backend of the native speech engine.
/// </summary>
internal enum NativeBackend
{
    /// <summary>
    /// The Vulkan GPU backend.
    /// </summary>
    Vulkan,

    /// <summary>
    /// The CPU backend.
    /// </summary>
    Cpu,
}
