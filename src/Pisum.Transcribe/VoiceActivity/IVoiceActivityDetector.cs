using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Detects speech in recorded audio.
/// </summary>
internal interface IVoiceActivityDetector
{
    /// <summary>
    /// Finds the speech in a recording. Blocks until the detector is loaded.
    /// </summary>
    /// <param name="samples">Mono samples at <see cref="AudioClip.SampleRate"/>.</param>
    /// <param name="cancellationToken">A token that aborts the detection between two model windows.</param>
    /// <returns>The speech segments in ascending order, or an empty list if there is no speech.</returns>
    /// <exception cref="OperationCanceledException">The detection was aborted.</exception>
    /// <exception cref="Exception">The detector could not be loaded, or the detection failed.</exception>
    IReadOnlyList<SpeechSegment> DetectSpeech(ReadOnlySpan<float> samples, CancellationToken cancellationToken);
}
