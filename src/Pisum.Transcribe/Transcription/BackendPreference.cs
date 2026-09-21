namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The compute backend the transcription engine loads the model on.
/// </summary>
internal enum BackendPreference
{
    /// <summary>
    /// The Vulkan GPU backend when it is available and loads and warms up, otherwise the CPU backend.
    /// </summary>
    Auto,

    /// <summary>
    /// Only the Vulkan GPU backend. A Vulkan failure fails the engine, with no CPU fallback.
    /// </summary>
    Vulkan,

    /// <summary>
    /// Only the CPU backend.
    /// </summary>
    Cpu,
}
