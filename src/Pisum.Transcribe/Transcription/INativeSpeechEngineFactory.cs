namespace Pisum.Transcribe.Transcription;

/// <summary>
/// Loads models into the native speech engine. Not thread-safe: call it from one thread at a time.
/// </summary>
internal interface INativeSpeechEngineFactory
{
    /// <summary>
    /// Checks whether a Vulkan device is available. Initializes the native backends on first use.
    /// </summary>
    /// <returns><see langword="true"/> if the Vulkan backend can be requested.</returns>
    /// <exception cref="NativeEngineException">The backends could not be initialized.</exception>
    /// <exception cref="DllNotFoundException">The native library is missing.</exception>
    bool IsVulkanAvailable();

    /// <summary>
    /// Loads a model file on a backend and creates a session for it. Blocks until loading has finished.
    /// </summary>
    /// <param name="modelPath">The GGUF model file.</param>
    /// <param name="backend">The backend.</param>
    /// <returns>The loaded engine. Dispose it to release the model.</returns>
    /// <exception cref="NativeEngineException">Loading failed.</exception>
    /// <exception cref="DllNotFoundException">The native library is missing.</exception>
    INativeSpeechEngine Load(string modelPath, NativeBackend backend);
}
