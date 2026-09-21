using Microsoft.ML.OnnxRuntime;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Runs the Silero VAD model on one 512-sample window at a time, carrying the model state and the last 64 samples from
/// window to window. Not thread-safe.
/// </summary>
/// <remarks>
/// Ported from <c>SileroVadOnnxModel.cs</c> in <c>snakers4/silero-vad</c>, <c>examples/csharp</c> (MIT), for
/// 16 kHz only and with buffers allocated once instead of per window.
/// </remarks>
internal sealed class SileroVadModel : IDisposable
{
    /// <summary>
    /// The number of new samples per inference at <see cref="AudioClip.SampleRate"/>.
    /// </summary>
    public const int WindowSize = 512;

    /// <summary>
    /// The number of samples of the previous window that precede each window.
    /// </summary>
    public const int ContextSize = 64;

    private const int StateSize = 2 * 1 * 128;

    private static readonly string[] InputNames = ["input", "sr", "state"];
    private static readonly string[] OutputNames = ["output", "stateN"];

    private readonly InferenceSession _session;
    private readonly RunOptions _runOptions = new();

    // The context followed by the window.
    private readonly float[] _input = new float[ContextSize + WindowSize];
    private readonly float[] _state = new float[StateSize];
    private readonly float[] _probability = new float[1];
    private readonly float[] _nextState = new float[StateSize];
    private readonly OrtValue[] _inputValues;
    private readonly OrtValue[] _outputValues;

    /// <summary>
    /// Loads the model.
    /// </summary>
    /// <param name="modelPath">The path of <c>silero_vad.onnx</c>.</param>
    public SileroVadModel(string modelPath)
    {
        using var options = new SessionOptions();
        options.IntraOpNumThreads = 1;
        options.InterOpNumThreads = 1;
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        _session = new InferenceSession(modelPath, options);

        _inputValues =
        [
            OrtValue.CreateTensorValueFromMemory(_input, [1, ContextSize + WindowSize]),
            OrtValue.CreateTensorValueFromMemory(new long[] {AudioClip.SampleRate}, [1]),
            OrtValue.CreateTensorValueFromMemory(_state, [2, 1, 128]),
        ];
        _outputValues =
        [
            OrtValue.CreateTensorValueFromMemory(_probability, [1, 1]),
            OrtValue.CreateTensorValueFromMemory(_nextState, [2, 1, 128]),
        ];
    }

    /// <summary>
    /// The path of the model file that ships with the application.
    /// </summary>
    public static string BundledModelPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "VoiceActivity", "Assets", "silero_vad.onnx");

    /// <summary>
    /// Clears the state and the context, so the next window starts a new recording.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_state);
        Array.Clear(_input);
    }

    /// <summary>
    /// Computes the speech probability of the next window.
    /// </summary>
    /// <param name="window">Up to <see cref="WindowSize"/> samples. A shorter last window is padded with zeros.</param>
    /// <returns>The probability, between 0 and 1, that the window contains speech.</returns>
    public float Process(ReadOnlySpan<float> window)
    {
        var windowBuffer = _input.AsSpan(ContextSize);
        window.CopyTo(windowBuffer);
        windowBuffer[window.Length..].Clear();

        _session.Run(_runOptions, InputNames, _inputValues, OutputNames, _outputValues);

        _nextState.CopyTo(_state, 0);
        _input.AsSpan(WindowSize).CopyTo(_input);
        return _probability[0];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var value in _inputValues.Concat(_outputValues))
        {
            value.Dispose();
        }

        _runOptions.Dispose();
        _session.Dispose();
    }
}
