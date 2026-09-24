using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.Transcription;

/// <summary>
/// A native engine that records its calls in order, detects overlapping calls, and lets a test fail or block loads and
/// runs.
/// </summary>
internal sealed class FakeNativeSpeechEngineFactory : INativeSpeechEngineFactory
{
    public const string DefaultText = "Hello.";

    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly List<FakeRun> _runs = [];
    private int _activeCalls;

    public bool GpuAvailable { get; set; } = true;

    public TimeSpan MaxAudio { get; set; } = TimeSpan.FromSeconds(400);

    /// <summary>
    /// Runs inside <see cref="Load"/>. Throw to fail the load.
    /// </summary>
    public Action<NativeBackend>? OnLoad { get; set; }

    /// <summary>
    /// Runs inside <see cref="INativeSpeechEngine.Run"/>, for warm-ups too. Returns the output or throws. By default,
    /// every run returns <see cref="DefaultText"/>.
    /// </summary>
    public Func<FakeRun, NativeRunOutput>? OnRun { get; set; }

    /// <summary>
    /// Runs inside <see cref="IDisposable.Dispose"/>. Throw to fail the release.
    /// </summary>
    public Action<NativeBackend>? OnDispose { get; set; }

    /// <summary>
    /// Whether two calls ran at the same time.
    /// </summary>
    public bool Overlapped { get; private set; }

    /// <summary>
    /// The calls in order, such as <c>Load Gpu</c>, <c>WarmUp Gpu</c>, <c>Run Gpu</c> or <c>Dispose Gpu</c>.
    /// </summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToList();
            }
        }
    }

    /// <summary>
    /// The runs that are not warm-ups, in order.
    /// </summary>
    public IReadOnlyList<FakeRun> Runs
    {
        get
        {
            lock (_lock)
            {
                return _runs.Where(run => !run.IsWarmUp).ToList();
            }
        }
    }

    /// <summary>
    /// All runs, warm-ups included, in order.
    /// </summary>
    public IReadOnlyList<FakeRun> AllRuns
    {
        get
        {
            lock (_lock)
            {
                return _runs.ToList();
            }
        }
    }

    public bool IsGpuAvailable()
    {
        Enter("IsGpuAvailable");
        Exit();
        return GpuAvailable;
    }

    public INativeSpeechEngine Load(string modelPath, NativeBackend backend)
    {
        Enter($"Load {backend}");
        try
        {
            OnLoad?.Invoke(backend);
            return new Engine(this, backend);
        }
        finally
        {
            Exit();
        }
    }

    private void Enter(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }

        if (Interlocked.Increment(ref _activeCalls) > 1)
        {
            Overlapped = true;
        }
    }

    private void Exit()
    {
        Interlocked.Decrement(ref _activeCalls);
    }

    private sealed class Engine : INativeSpeechEngine
    {
        private readonly FakeNativeSpeechEngineFactory _factory;
        private readonly NativeBackend _backend;

        public Engine(FakeNativeSpeechEngineFactory factory, NativeBackend backend)
        {
            _factory = factory;
            _backend = backend;
        }

        public TimeSpan MaxAudio => _factory.MaxAudio;

        public NativeRunOutput Run(float[] samples,
                                   TranscriptionTask task,
                                   string sourceLanguage,
                                   string targetLanguage,
                                   CancellationToken cancellationToken)
        {
            var run = new FakeRun(_backend, samples, task, sourceLanguage, targetLanguage, cancellationToken);
            _factory.Enter($"{(run.IsWarmUp ? "WarmUp" : "Run")} {_backend}");
            lock (_factory._lock)
            {
                _factory._runs.Add(run);
            }

            try
            {
                return _factory.OnRun?.Invoke(run) ?? new NativeRunOutput(DefaultText, false);
            }
            finally
            {
                _factory.Exit();
            }
        }

        public void Dispose()
        {
            _factory.Enter($"Dispose {_backend}");
            try
            {
                _factory.OnDispose?.Invoke(_backend);
            }
            finally
            {
                _factory.Exit();
            }
        }
    }
}

/// <summary>
/// The arguments of one native run.
/// </summary>
internal sealed record FakeRun(
    NativeBackend Backend,
    float[] Samples,
    TranscriptionTask Task,
    string SourceLanguage,
    string TargetLanguage,
    CancellationToken CancellationToken)
{
    /// <summary>
    /// Whether this is the warm-up: the backend's warm-up input, transcribed as English.
    /// </summary>
    public bool IsWarmUp => Task == TranscriptionTask.Transcribe && SourceLanguage == "en"
                            && Samples.AsSpan().SequenceEqual(TranscribeCppTranscriber.CreateWarmUpSamples(Backend));
}
