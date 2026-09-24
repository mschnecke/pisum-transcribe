namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The compute backend the transcription engine loads the model on.
/// </summary>
internal enum BackendPreference
{
    /// <summary>
    /// The GPU backend, Vulkan on Windows and Metal on macOS, when it is available and loads and warms up, otherwise the
    /// CPU backend.
    /// </summary>
    Auto,

    /// <summary>
    /// Only the GPU backend. A GPU backend failure fails the engine, with no CPU fallback.
    /// </summary>
    Gpu,

    /// <summary>
    /// Only the CPU backend.
    /// </summary>
    Cpu,
}
