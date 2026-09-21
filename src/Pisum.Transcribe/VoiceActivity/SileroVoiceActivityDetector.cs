namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Detects speech with the bundled Silero VAD model on the CPU. The model is loaded on first use, and calls are
/// serialized.
/// </summary>
/// <remarks>
/// A load failure is kept: every later call throws the same exception, so dictation falls back to untrimmed audio.
/// </remarks>
internal sealed class SileroVoiceActivityDetector : IVoiceActivityDetector, IDisposable
{
    private readonly Lazy<SileroVadModel> _model;
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance. The model is not loaded yet.
    /// </summary>
    /// <param name="loadModel">
    /// Loads the model, for tests. <see langword="null"/> loads <see cref="SileroVadModel.BundledModelPath"/>.
    /// </param>
    public SileroVoiceActivityDetector(Func<SileroVadModel>? loadModel = null)
    {
        _model = new Lazy<SileroVadModel>(loadModel ?? (() => new SileroVadModel(SileroVadModel.BundledModelPath)),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public IReadOnlyList<SpeechSegment> DetectSpeech(ReadOnlySpan<float> samples, CancellationToken cancellationToken)
    {
        var model = _model.Value;
        lock (_lock)
        {
            model.Reset();
            const int windowSize = SileroVadModel.WindowSize;
            var probabilities = new float[(samples.Length + windowSize - 1) / windowSize];
            for (var i = 0; i < probabilities.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var offset = i * windowSize;
                probabilities[i] = model.Process(samples.Slice(offset, Math.Min(windowSize, samples.Length - offset)));
            }

            return SileroSpeechDetector.FindSegments(probabilities, samples.Length);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_model.IsValueCreated)
        {
            return;
        }

        lock (_lock)
        {
            _model.Value.Dispose();
        }
    }
}
