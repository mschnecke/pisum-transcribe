using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The transcription engine on transcribe.cpp. A single dedicated worker runs every native call (load, warm-up, run and
/// dispose) from a FIFO queue, because the native engine allows one compute call per model at a time.
/// </summary>
/// <remarks>
/// With <see cref="BackendPreference.Auto"/>, a backend failure on the GPU backend during load, warm-up or a run moves
/// the engine to the CPU backend. If the reload makes the model ready on the CPU backend, the request whose run failed
/// runs once more on it, unless the application is stopping or the caller stopped waiting. A model file rejected as
/// invalid is hashed: a damaged file is deleted, so it can be downloaded again. On shutdown the worker releases the
/// model, but only after its native call has returned.
/// <para>
/// After a run ran out of memory on the GPU backend, the engine returns to it once the request has run on the CPU
/// backend or was cancelled: it queues a reload of the same model behind the queued requests. The status stays
/// <see cref="TranscriberStatus.Ready"/> during the reload, so new requests are accepted and wait for it. The engine
/// returns only if the audio is longer than every request that completed on the GPU backend, and longer than the 10 s
/// warm-up input, and if a request has completed on the GPU backend since the last return. Otherwise it stays on the
/// CPU backend. Each <see cref="LoadAsync"/> call resets both checks.
/// </para>
/// <para>
/// Each <see cref="LoadAsync"/> call takes the next load generation. The worker skips a queued load that a newer call
/// replaced, and a load that a newer call replaced while it ran is released without reporting
/// <see cref="TranscriberStatus.Ready"/>, because a running native call cannot be interrupted.
/// </para>
/// </remarks>
internal sealed class TranscribeCppTranscriber : ITranscriber, IHostedService
{
    /// <summary>
    /// The sample rate of the audio input.
    /// </summary>
    public const int SampleRate = 16_000;

    /// <summary>
    /// The notification text when a model file failed to load and its hash did not match the catalog.
    /// </summary>
    public const string DamagedModelMessage = "Model file was damaged and has been removed. Download it again.";

    /// <summary>
    /// The notification text when a model file failed to load although its hash matches the catalog.
    /// </summary>
    public const string IncompatibleModelMessage = "This model can't be loaded by this version of Pisum Transcribe.";

    /// <summary>
    /// The notification text when a model failed to load for another reason.
    /// </summary>
    public const string LoadFailedMessage = "The speech model could not be loaded. Details are in the log.";

    /// <summary>
    /// The notification text when the backend failed during a transcription and no fallback is left.
    /// </summary>
    public const string BackendFailedMessage =
        "The speech model stopped working. Change the backend in Settings, or restart Pisum Transcribe to load it " +
        "again. Details are in the log.";

    /// <summary>
    /// The reason of the process activity that each load runs in, as macOS lists it.
    /// </summary>
    public const string LoadActivityReason = "Loading the speech model";

    /// <summary>
    /// The reason of the process activity that each transcription runs in, as macOS lists it.
    /// </summary>
    public const string RunActivityReason = "Transcription";

    /// <summary>
    /// The maximum input duration while no model is loaded, or when the native engine reports none.
    /// </summary>
    public static readonly TimeSpan DefaultMaxInputDuration = TimeSpan.FromSeconds(400);

    /// <summary>
    /// How long <see cref="StopAsync"/> waits for the worker. Below the host's 4 s shutdown timeout.
    /// </summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private const string WarmUpLanguage = "en";
    private const int GpuWarmUpSeconds = 10;
    private const int WarmUpNoiseSeed = 42;
    private const double WarmUpNoiseAmplitude = 0.1;

    // How far the maximum input stays below the limit the native engine reports. transcribe.cpp 0.2.3 rejects canary
    // input of exactly that limit: its limit leaves out the centred first mel frame, so 400 s gives 5,001 encoder frames
    // where the model supports 5,000. 1 s is not the smallest margin, but one that a real 399 s run has confirmed.
    private static readonly TimeSpan MaxInputMargin = TimeSpan.FromSeconds(1);

    private readonly INativeSpeechEngineFactory _engineFactory;
    private readonly IModelStore _modelStore;
    private readonly IProcessActivity _processActivity;
    private readonly ILogger<TranscribeCppTranscriber> _logger;

    private readonly Channel<WorkItem> _workItems =
        Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions {SingleReader = true});

    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _stateLock = new();

    // Held while a status change is made and its StatusChanged event is raised, so that LoadAsync and the worker raise
    // their events in the order of their changes. Never held during a native call.
    private readonly Lock _statusEventLock = new();
    private readonly Task _worker;

    // Guarded by _stateLock. Only the worker writes them, except LoadAsync, which sets the status to Loading and takes
    // the next load generation. While a reload waits in the queue, _model and _activeBackend still describe the loaded
    // engine, so requests queued before the reload run on it.
    private TranscriberStatus _status;
    private SpeechModel? _model;
    private NativeBackend? _activeBackend;
    private TimeSpan _maxInputDuration = DefaultMaxInputDuration;
    private string? _failureMessage;
    private long _loadGeneration;

    // Only touched by the worker.
    private INativeSpeechEngine? _engine;
    private BackendPreference _backendPreference;
    private long _engineGeneration;

    // The two checks before a return to the GPU backend after an out-of-memory error. Only touched by the worker, reset
    // by each load that LoadAsync queued but not by a return, and never saved. _longestGpuRun is the longest audio of
    // a request that completed on the GPU backend, at least the GPU warm-up input. _returnedWithoutGpuRun is set while
    // no request has completed on the GPU backend since the last return.
    private TimeSpan _longestGpuRun;
    private bool _returnedWithoutGpuRun;

    /// <summary>
    /// Initializes a new instance and starts its worker.
    /// </summary>
    /// <param name="engineFactory">Loads models into the native engine.</param>
    /// <param name="modelStore">Locates the model files.</param>
    /// <param name="processActivity">Keeps macOS from throttling each load and run through App Nap.</param>
    /// <param name="logger">The logger.</param>
    public TranscribeCppTranscriber(INativeSpeechEngineFactory engineFactory,
                                    IModelStore modelStore,
                                    IProcessActivity processActivity,
                                    ILogger<TranscribeCppTranscriber> logger)
    {
        _engineFactory = engineFactory;
        _modelStore = modelStore;
        _processActivity = processActivity;
        _logger = logger;

        // A dedicated thread, so multi-second native calls do not tie up the thread pool.
        _worker = Task.Factory.StartNew(ProcessWorkItems, CancellationToken.None, TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public event EventHandler<TranscriberStatus>? StatusChanged;

    /// <inheritdoc />
    public TranscriberStatus Status
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    /// <inheritdoc />
    public string? ActiveBackend
    {
        get
        {
            lock (_stateLock)
            {
                return _status == TranscriberStatus.Ready && _activeBackend is { } backend
                    ? BackendName(backend)
                    : null;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan MaxInputDuration
    {
        get
        {
            lock (_stateLock)
            {
                return _maxInputDuration;
            }
        }
    }

    /// <inheritdoc />
    public string? FailureMessage
    {
        get
        {
            lock (_stateLock)
            {
                return _failureMessage;
            }
        }
    }

    /// <inheritdoc />
    public async Task LoadAsync(SpeechModel model, BackendPreference backend, CancellationToken cancellationToken)
    {
        long generation;
        lock (_statusEventLock)
        {
            bool wasLoading;
            lock (_stateLock)
            {
                generation = ++_loadGeneration;
                wasLoading = _status == TranscriberStatus.Loading;
                _status = TranscriberStatus.Loading;
                _failureMessage = null;
            }

            // Raised before the work item is queued, so it cannot arrive after the worker's Ready or Failed.
            if (!wasLoading)
            {
                StatusChanged?.Invoke(this, TranscriberStatus.Loading);
            }
        }

        var item = new LoadWorkItem(model, backend, generation);
        Enqueue(item);
        await item.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(float[] samples,
                                                           TranscriptionOptions options,
                                                           CancellationToken cancellationToken)
    {
        TranscriberStatus status;
        SpeechModel? model;
        TimeSpan maxInputDuration;
        lock (_stateLock)
        {
            status = _status;
            model = _model;
            maxInputDuration = _maxInputDuration;
        }

        if (status != TranscriberStatus.Ready || model is null)
        {
            throw new TranscriberNotReadyException(status);
        }

        if (TranscriptionOptionsValidator.Validate(model, options) is { } error)
        {
            throw new LanguageNotSupportedException(error);
        }

        if (samples.Length == 0)
        {
            return new TranscriptionResult(string.Empty, TimeSpan.Zero, TimeSpan.Zero);
        }

        var audioDuration = TimeSpan.FromSeconds((double) samples.Length / SampleRate);
        if (audioDuration > maxInputDuration)
        {
            throw new AudioTooLongException(audioDuration, maxInputDuration);
        }

        var item = new RunWorkItem(samples, options, audioDuration, cancellationToken);
        Enqueue(item);
        return await item.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels the running native call and the queued requests, and waits up to <see cref="StopTimeout"/> for the worker
    /// to release the model. If the native call has not returned by then, the model stays loaded: releasing it from
    /// another thread would free memory that the native call still uses.
    /// </summary>
    /// <param name="cancellationToken">Not used; the wait is bounded by <see cref="StopTimeout"/>.</param>
    /// <returns>A task that completes when the worker has stopped or the wait has timed out.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _workItems.Writer.TryComplete();
        await _stopping.CancelAsync().ConfigureAwait(false);

        if (await Task.WhenAny(_worker, Task.Delay(StopTimeout, CancellationToken.None)).ConfigureAwait(false) !=
            _worker)
        {
            _logger.LogWarning("The native call did not return within {StopTimeout}, the model is not released",
                StopTimeout);
        }
    }

    /// <summary>
    /// Creates the warm-up input for a backend. On the GPU, 10 s of fixed pseudo-random noise in [-0.1, 0.1], so the
    /// warm-up compiles the GPU pipelines of a normal dictation; silence of that length makes Canary produce text. On
    /// CPU, which compiles nothing, 1 s of silence.
    /// </summary>
    /// <param name="backend">The backend the model is loaded on.</param>
    /// <returns>The samples, the same on every call.</returns>
    public static float[] CreateWarmUpSamples(NativeBackend backend)
    {
        if (backend == NativeBackend.Cpu)
        {
            return new float[SampleRate];
        }

        var random = new Random(WarmUpNoiseSeed);
        var samples = new float[GpuWarmUpSeconds * SampleRate];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float) ((random.NextDouble() * 2 - 1) * WarmUpNoiseAmplitude);
        }

        return samples;
    }

    private static string BackendName(NativeBackend backend)
    {
        return backend == NativeBackend.Gpu ? TranscribeCppEngineFactory.GpuBackendName : "CPU";
    }

    private static FailureClass Classify(Exception exception)
    {
        return exception switch
        {
            NativeEngineException {Status: NativeStatus.ErrBackend or NativeStatus.ErrOom} => FailureClass.Backend,
            DllNotFoundException => FailureClass.Backend,
            NativeEngineException
                {
                    Status: NativeStatus.ErrGguf or NativeStatus.ErrUnsupportedArch
                    or NativeStatus.ErrUnsupportedVariant,
                } =>
                FailureClass.InvalidModel,
            _ => FailureClass.Other,
        };
    }

    private static string DescribeStatus(Exception exception)
    {
        return exception is NativeEngineException nativeException
            ? nativeException.Status.ToString()
            : exception.GetType().Name;
    }

    private void Enqueue(WorkItem item)
    {
        if (!_workItems.Writer.TryWrite(item))
        {
            // The application is stopping.
            item.Cancel();
        }
    }

    private void ProcessWorkItems()
    {
        var reader = _workItems.Reader;

        // Blocks this dedicated thread instead of awaiting, so every native call stays on it.
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var item))
            {
                if (_stopping.IsCancellationRequested)
                {
                    item.Cancel();
                    continue;
                }

                // Loads and runs alike, so the load at start, a reload after a settings change and the return to the GPU
                // backend are covered too (design D6 of add-macos-dictation).
                using var activity = _processActivity.Begin(item is LoadWorkItem ? LoadActivityReason : RunActivityReason);
                switch (item)
                {
                    case LoadWorkItem load:
                        if (IsReplaced(load.Generation))
                        {
                            _logger.LogInformation("Skipping the load of model {ModelId}, a newer load replaced it",
                                load.Model.Id);
                        }
                        else if (load.IsReturn && _engine is null)
                        {
                            // A backend error on the CPU backend released the model after the return was queued.
                            _logger.LogInformation(
                                "Skipping the return of model {ModelId} to the {Backend} backend, the model is no longer loaded",
                                load.Model.Id, TranscribeCppEngineFactory.GpuBackendName);
                        }
                        else if (load.IsReturn)
                        {
                            // Keeps the model, backend and maximum input of the CPU engine, so the status stays Ready and
                            // requests are validated against the same model while it loads. Releases the CPU engine
                            // first, so two models are never loaded at the same time.
                            DisposeEngine();
                            Load(load.Model, load.Backend, load.Generation);
                        }
                        else
                        {
                            // Releases the previous model first, so two models are never loaded at the same time.
                            ReleaseEngine();
                            _backendPreference = load.Backend;
                            _longestGpuRun = TimeSpan.FromSeconds(GpuWarmUpSeconds);
                            _returnedWithoutGpuRun = false;
                            Load(load.Model, load.Backend, load.Generation);
                        }

                        load.Completion.TrySetResult();
                        break;
                    case RunWorkItem run:
                        Run(run);
                        break;
                }
            }
        }

        // Reached only after the last native call has returned.
        DisposeEngine();
    }

    /// <summary>
    /// Loads a model and makes it <see cref="TranscriberStatus.Ready"/>, unless a newer load replaced this one.
    /// </summary>
    /// <returns><see langword="true"/> if the model is <see cref="TranscriberStatus.Ready"/>.</returns>
    private bool Load(SpeechModel model, BackendPreference preference, long generation)
    {
        try
        {
            var (engine, backend) = LoadEngine(model, preference);
            _engine = engine;
            _engineGeneration = generation;
            if (TryPublishReady(generation, model, backend, engine))
            {
                return true;
            }

            // The newer load that replaced this one follows in the queue and decides the status.
            _logger.LogInformation("Releasing model {ModelId} unused, a newer load replaced it", model.Id);
            ReleaseEngine();
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
            SetStatus(TranscriberStatus.NotLoaded);
        }
        catch (Exception exception)
        {
            var failureMessage = HandleLoadFailure(model, exception);
            TrySetStatus(generation, TranscriberStatus.Failed, failureMessage);
        }

        return false;
    }

    private bool IsReplaced(long generation)
    {
        lock (_stateLock)
        {
            return generation != _loadGeneration;
        }
    }

    /// <summary>
    /// Makes a loaded engine <see cref="TranscriberStatus.Ready"/>, unless a newer load was requested.
    /// </summary>
    /// <returns><see langword="false"/> if a newer load replaced this one.</returns>
    private bool TryPublishReady(long generation, SpeechModel model, NativeBackend backend, INativeSpeechEngine engine)
    {
        lock (_statusEventLock)
        {
            lock (_stateLock)
            {
                if (generation != _loadGeneration)
                {
                    return false;
                }

                _status = TranscriberStatus.Ready;
                _model = model;
                _activeBackend = backend;
                _maxInputDuration = engine.MaxAudio > TimeSpan.Zero
                    ? engine.MaxAudio - MaxInputMargin
                    : DefaultMaxInputDuration;
                _failureMessage = null;
            }

            _logger.LogInformation("Model {ModelId} is ready on {Backend}", model.Id, BackendName(backend));
            StatusChanged?.Invoke(this, TranscriberStatus.Ready);
            return true;
        }
    }

    private (INativeSpeechEngine Engine, NativeBackend Backend) LoadEngine(SpeechModel model,
                                                                           BackendPreference preference)
    {
        var modelPath = _modelStore.GetModelPath(model);
        switch (preference)
        {
            case BackendPreference.Gpu:
                return (LoadAndWarmUp(model, modelPath, NativeBackend.Gpu), NativeBackend.Gpu);
            case BackendPreference.Cpu:
                return (LoadAndWarmUp(model, modelPath, NativeBackend.Cpu), NativeBackend.Cpu);
        }

        try
        {
            if (_engineFactory.IsGpuAvailable())
            {
                return (LoadAndWarmUp(model, modelPath, NativeBackend.Gpu), NativeBackend.Gpu);
            }

            _logger.LogInformation("No {Backend} device is available, using the CPU backend",
                TranscribeCppEngineFactory.GpuBackendName);
        }
        catch (Exception exception) when (Classify(exception) == FailureClass.Backend)
        {
            _logger.LogWarning(exception, "The {Backend} backend failed with {Status}, falling back to the CPU backend",
                TranscribeCppEngineFactory.GpuBackendName, DescribeStatus(exception));
        }

        return (LoadAndWarmUp(model, modelPath, NativeBackend.Cpu), NativeBackend.Cpu);
    }

    private INativeSpeechEngine LoadAndWarmUp(SpeechModel model, string modelPath, NativeBackend backend)
    {
        // Logged before the attempt, so the log shows the backend if a native crash ends the process.
        _logger.LogInformation("Loading model {ModelId} on {Backend}", model.Id, BackendName(backend));
        var started = Stopwatch.GetTimestamp();
        var engine = _engineFactory.Load(modelPath, backend);
        var loadDuration = Stopwatch.GetElapsedTime(started);

        try
        {
            // Compiles the GPU pipelines, so the first real request does not pay for it. A truncated output counts as
            // success, and the text is discarded.
            started = Stopwatch.GetTimestamp();
            engine.Run(CreateWarmUpSamples(backend), TranscriptionTask.Transcribe, WarmUpLanguage, WarmUpLanguage,
                _stopping.Token);
        }
        catch
        {
            engine.Dispose();
            throw;
        }

        _logger.LogInformation("Loaded model {ModelId} on {Backend} in {LoadDuration}, warm-up took {WarmUpDuration}",
            model.Id, BackendName(backend), loadDuration, Stopwatch.GetElapsedTime(started));
        return engine;
    }

    private string HandleLoadFailure(SpeechModel model, Exception exception)
    {
        if (Classify(exception) != FailureClass.InvalidModel)
        {
            _logger.LogError(exception, "Loading model {ModelId} failed with {Status}", model.Id,
                DescribeStatus(exception));
            return LoadFailedMessage;
        }

        _logger.LogError(exception, "Model {ModelId} was rejected as invalid with {Status}, verifying its hash",
            model.Id, DescribeStatus(exception));
        var modelPath = _modelStore.GetModelPath(model);
        try
        {
            string hash;
            using (var stream = File.OpenRead(modelPath))
            {
                hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            }

            if (string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Model {ModelId} matches the catalog hash, so it is incompatible and kept",
                    model.Id);
                return IncompatibleModelMessage;
            }

            File.Delete(modelPath);
            _logger.LogWarning("Model {ModelId} does not match the catalog hash and was deleted", model.Id);
            return DamagedModelMessage;
        }
        catch (Exception hashException) when (hashException is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(hashException, "Could not verify or delete model {ModelId}", model.Id);
            return LoadFailedMessage;
        }
    }

    private void Run(RunWorkItem item)
    {
        if (item.CancellationToken.IsCancellationRequested)
        {
            item.Completion.TrySetCanceled(item.CancellationToken);
            return;
        }

        SpeechModel? model;
        NativeBackend? backend;
        lock (_stateLock)
        {
            model = _model;
            backend = _activeBackend;
        }

        if (_engine is null || model is null || backend is null)
        {
            // Queued before a failure that left no engine.
            item.Completion.TrySetException(new TranscriberNotReadyException(Status));
            return;
        }

        if (RunOnEngine(item, _engine, backend.Value) is not { } exception)
        {
            return;
        }

        if (Classify(exception) != FailureClass.Backend)
        {
            item.Completion.TrySetException(new TranscriptionFailedException(exception.Status, exception));
            return;
        }

        // Taken before the recovery, which replaces the engine for the same load generation.
        var generation = _engineGeneration;
        RecoverAndRunAgain(item, model, backend.Value, exception);
        if (exception.Status == NativeStatus.ErrOom && backend == NativeBackend.Gpu &&
            _backendPreference == BackendPreference.Auto)
        {
            ReturnToGpu(model, generation, item.AudioDuration);
        }
    }

    /// <summary>
    /// Handles a backend error of a run and completes the request. If the recovery makes the model ready on the CPU
    /// backend, runs the request once more on it.
    /// </summary>
    private void RecoverAndRunAgain(RunWorkItem item,
                                    SpeechModel model,
                                    NativeBackend failedBackend,
                                    NativeEngineException exception)
    {
        var readyOnCpu = RecoverFromBackendFailure(model, failedBackend);
        if (_stopping.IsCancellationRequested || item.CancellationToken.IsCancellationRequested)
        {
            // The application is stopping, or the caller stopped waiting, during the recovery.
            item.Cancel();
            return;
        }

        if (readyOnCpu)
        {
            // Runs before the next work item, so the request keeps its place ahead of the queued ones. A backend error
            // on the CPU backend releases the model instead of reloading it, so there is no second retry.
            _logger.LogWarning("Running the failed transcription again on the CPU backend");
            if (RunOnEngine(item, _engine!, NativeBackend.Cpu) is not { } retryException)
            {
                return;
            }

            exception = retryException;
            if (Classify(exception) == FailureClass.Backend)
            {
                RecoverFromBackendFailure(model, NativeBackend.Cpu);
            }
        }

        item.Completion.TrySetException(new TranscriptionFailedException(exception.Status, exception));
    }

    /// <summary>
    /// Queues the return to the GPU backend after a request ran out of memory there, once the request has been
    /// handled. The return is queued only if the model is ready on the CPU backend for the load generation of the failed
    /// run, the application is not stopping, and both checks pass: the audio is longer than every request that
    /// completed on the GPU backend, and a request has completed there since the last return. The status stays
    /// <see cref="TranscriberStatus.Ready"/>, and the load generation stays the same.
    /// </summary>
    private void ReturnToGpu(SpeechModel model, long generation, TimeSpan audioDuration)
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_status != TranscriberStatus.Ready || _activeBackend != NativeBackend.Cpu ||
                generation != _loadGeneration)
            {
                // The recovery failed, or a newer load replaces the model anyway.
                return;
            }
        }

        if (audioDuration <= _longestGpuRun)
        {
            _logger.LogInformation(
                "Staying on the CPU backend, the {AudioDuration} of audio that ran out of memory is not longer than the {LongestGpuRun} that completed on {Backend}",
                audioDuration, _longestGpuRun, TranscribeCppEngineFactory.GpuBackendName);
            return;
        }

        if (_returnedWithoutGpuRun)
        {
            _logger.LogInformation(
                "Staying on the CPU backend, no transcription has completed on {Backend} since the last return",
                TranscribeCppEngineFactory.GpuBackendName);
            return;
        }

        _logger.LogWarning(
            "Reloading model {ModelId} on the {Backend} backend after it ran out of memory on {AudioDuration} of audio",
            model.Id, TranscribeCppEngineFactory.GpuBackendName, audioDuration);
        _returnedWithoutGpuRun = true;
        Enqueue(new LoadWorkItem(model, BackendPreference.Auto, generation, IsReturn: true));
    }

    /// <summary>
    /// Runs a request on the engine and completes it, unless the native engine fails the run.
    /// </summary>
    /// <returns>
    /// The failure of the native engine, which leaves the request open; otherwise <see langword="null"/>.
    /// </returns>
    private NativeEngineException? RunOnEngine(RunWorkItem item, INativeSpeechEngine engine, NativeBackend backend)
    {
        var options = item.Options;
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(item.CancellationToken, _stopping.Token);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var output = engine.Run(item.Samples, options.Task, options.SourceLanguage, options.TargetLanguage,
                cancellation.Token);
            var processingTime = Stopwatch.GetElapsedTime(started);

            if (output.Truncated)
            {
                _logger.LogWarning("The output stopped at the model's output limit, returning the partial text");
            }

            if (backend == NativeBackend.Gpu)
            {
                // Audio of this length fits on the GPU backend. Truncated output counts too, because the whole clip was
                // encoded.
                if (item.AudioDuration > _longestGpuRun)
                {
                    _longestGpuRun = item.AudioDuration;
                }

                _returnedWithoutGpuRun = false;
            }

            _logger.LogInformation(
                "Ran {Task} from {SourceLanguage} to {TargetLanguage} on {AudioDuration} of audio in {ProcessingTime} on {Backend}",
                options.Task, options.SourceLanguage, options.TargetLanguage, item.AudioDuration, processingTime,
                BackendName(backend));
            item.Completion.TrySetResult(new TranscriptionResult(output.Text, item.AudioDuration, processingTime));
        }
        catch (OperationCanceledException)
        {
            item.Completion.TrySetCanceled(cancellation.Token);
        }
        catch (NativeEngineException exception)
        {
            _logger.LogError(exception,
                "The transcription of {AudioDuration} of audio failed with {Status} on {Backend}", item.AudioDuration,
                exception.Status, BackendName(backend));
            return exception;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The transcription failed on {Backend}", BackendName(backend));
            item.Completion.TrySetException(exception);
        }

        return null;
    }

    /// <summary>
    /// Handles a backend error of a run. With <see cref="BackendPreference.Auto"/> on the GPU backend, reloads the model
    /// on the CPU backend; otherwise releases the model and sets the status to <see cref="TranscriberStatus.Failed"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the model is <see cref="TranscriberStatus.Ready"/> on the CPU backend for the load
    /// generation of the failed run.
    /// </returns>
    private bool RecoverFromBackendFailure(SpeechModel model, NativeBackend failedBackend)
    {
        var generation = _engineGeneration;
        if (_backendPreference == BackendPreference.Auto && failedBackend == NativeBackend.Gpu)
        {
            // Runs before the next work item, so requests queued behind the failed one run on the CPU backend.
            if (!TrySetStatus(generation, TranscriberStatus.Loading))
            {
                // A newer load is queued and replaces this model anyway.
                ReleaseEngine();
                return false;
            }

            _logger.LogWarning("Reloading model {ModelId} on the CPU backend after a {Backend} backend failure", model.Id,
                TranscribeCppEngineFactory.GpuBackendName);
            DisposeEngine();
            return Load(model, BackendPreference.Cpu, generation);
        }

        ReleaseEngine();
        TrySetStatus(generation, TranscriberStatus.Failed, BackendFailedMessage);
        return false;
    }

    private void SetStatus(TranscriberStatus status, string? failureMessage = null)
    {
        lock (_statusEventLock)
        {
            lock (_stateLock)
            {
                SetStatusLocked(status, failureMessage);
            }

            StatusChanged?.Invoke(this, status);
        }
    }

    /// <summary>
    /// Sets a status without a loaded model, unless a load newer than <paramref name="generation"/> was requested: that
    /// load decides the status, which stays <see cref="TranscriberStatus.Loading"/> until then.
    /// </summary>
    /// <returns><see langword="false"/> if a newer load replaced the one of <paramref name="generation"/>.</returns>
    private bool TrySetStatus(long generation, TranscriberStatus status, string? failureMessage = null)
    {
        lock (_statusEventLock)
        {
            lock (_stateLock)
            {
                if (generation != _loadGeneration)
                {
                    return false;
                }

                SetStatusLocked(status, failureMessage);
            }

            StatusChanged?.Invoke(this, status);
            return true;
        }
    }

    private void SetStatusLocked(TranscriberStatus status, string? failureMessage)
    {
        _status = status;
        _model = null;
        _activeBackend = null;
        _maxInputDuration = DefaultMaxInputDuration;
        _failureMessage = failureMessage;
    }

    /// <summary>
    /// Releases the loaded model without changing the status.
    /// </summary>
    private void ReleaseEngine()
    {
        DisposeEngine();
        lock (_stateLock)
        {
            _model = null;
            _activeBackend = null;
            _maxInputDuration = DefaultMaxInputDuration;
        }
    }

    private void DisposeEngine()
    {
        var engine = _engine;
        _engine = null;
        try
        {
            engine?.Dispose();
        }
        catch (Exception exception)
        {
            // Must not end the worker, or every later request would wait forever.
            _logger.LogError(exception, "Releasing the model failed");
        }
    }

    private enum FailureClass
    {
        Backend,
        InvalidModel,
        Other,
    }

    private abstract record WorkItem
    {
        public abstract void Cancel();
    }

    // IsReturn marks the return to the GPU backend after an out-of-memory error, which keeps the status Ready, as
    // opposed to a load that LoadAsync queued.
    private sealed record LoadWorkItem(
        SpeechModel Model,
        BackendPreference Backend,
        long Generation,
        bool IsReturn = false) : WorkItem
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Cancel()
        {
            Completion.TrySetCanceled();
        }
    }

    private sealed record RunWorkItem(
        float[] Samples,
        TranscriptionOptions Options,
        TimeSpan AudioDuration,
        CancellationToken CancellationToken) : WorkItem
    {
        public TaskCompletionSource<TranscriptionResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Cancel()
        {
            Completion.TrySetCanceled();
        }
    }
}
