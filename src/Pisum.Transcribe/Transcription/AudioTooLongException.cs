namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The audio is longer than the loaded model accepts.
/// </summary>
internal sealed class AudioTooLongException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="audioDuration">The duration of the audio.</param>
    /// <param name="maxInputDuration">The longest audio the model accepts.</param>
    public AudioTooLongException(TimeSpan audioDuration, TimeSpan maxInputDuration)
        : base(
            $"The audio is {audioDuration.TotalSeconds:0.#} s long, but the model accepts at most {maxInputDuration.TotalSeconds:0.#} s.")
    {
        AudioDuration = audioDuration;
        MaxInputDuration = maxInputDuration;
    }

    /// <summary>
    /// The duration of the audio.
    /// </summary>
    public TimeSpan AudioDuration { get; }

    /// <summary>
    /// The longest audio the model accepts.
    /// </summary>
    public TimeSpan MaxInputDuration { get; }
}
