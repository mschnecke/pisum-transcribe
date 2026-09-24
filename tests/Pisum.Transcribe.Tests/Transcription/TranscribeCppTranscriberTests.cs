using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.Transcription;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TranscribeCppTranscriberTests : IAsyncDisposable
{
    private const string Gpu = TranscribeCppEngineFactory.GpuBackendName;

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TranscriptionOptions GermanToEnglish = new(TranscriptionTask.Translate, "de", "en");

    private readonly TempDirectory _root = new();
    private readonly FakeNativeSpeechEngineFactory _engineFactory = new();
    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly SpeechModel _model = ModelCatalog.Resolve(null);
    private readonly List<TranscriberStatus> _statusChanges = [];
    private readonly List<string?> _readyBackends = [];
    private readonly TranscribeCppTranscriber _sut;

    public TranscribeCppTranscriberTests()
    {
        A.CallTo(() => _modelStore.GetModelPath(A<SpeechModel>._))
            .ReturnsLazily((SpeechModel model) => Path.Combine(_root.Path, model.FileName));
        _sut = new TranscribeCppTranscriber(_engineFactory, _modelStore, NullLogger<TranscribeCppTranscriber>.Instance);
        _sut.StatusChanged += (_, status) =>
        {
            lock (_statusChanges)
            {
                _statusChanges.Add(status);
                if (status == TranscriberStatus.Ready)
                {
                    _readyBackends.Add(_sut.ActiveBackend);
                }
            }
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        _root.Dispose();
    }

    [Fact]
    public async Task LoadAsync_NotLoaded_StatusGoesFromLoadingToReady()
    {
        // Arrange
        var initialStatus = _sut.Status;

        // Act
        await LoadAsync(BackendPreference.Auto);

        // Assert
        initialStatus.ShouldBe(TranscriberStatus.NotLoaded);
        StatusChanges().ShouldBe([TranscriberStatus.Loading, TranscriberStatus.Ready]);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe(Gpu);
        _sut.FailureMessage.ShouldBeNull();
        _engineFactory.Calls.ShouldBe(["IsGpuAvailable", "Load Gpu", "WarmUp Gpu"]);
    }

    [Fact]
    public async Task LoadAsync_Failed_LoadsAgainAndBecomesReady()
    {
        // Arrange
        _engineFactory.OnLoad = _ => throw new NativeEngineException(NativeStatus.ErrFileNotFound);
        await LoadAsync(BackendPreference.Cpu);
        var statusAfterFailure = _sut.Status;
        _engineFactory.OnLoad = null;

        // Act
        await LoadAsync(BackendPreference.Cpu);

        // Assert
        statusAfterFailure.ShouldBe(TranscriberStatus.Failed);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Failed, TranscriberStatus.Loading, TranscriberStatus.Ready,
        ]);
        _sut.ActiveBackend.ShouldBe("CPU");
        _sut.FailureMessage.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_WhileReady_ReleasesPreviousModelAndBecomesReadyOnNewBackend()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);

        // Act
        await LoadAsync(BackendPreference.Cpu);

        // Assert
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
        ]);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Dispose Gpu", "Load Cpu", "WarmUp Cpu",
        ]);
    }

    [Fact]
    public async Task LoadAsync_RequestsQueuedBeforeReload_CompleteOnPreviousModel()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var runGate = new ManualResetEventSlim();
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Gpu})
            {
                runStarted.TrySetResult();
                runGate.Wait(SignalTimeout);
            }

            return new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var running = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await runStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var queued = _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);

        // Act
        var reload = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);
        runGate.Set();

        // Assert
        (await running.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text
            .ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        (await queued.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text
            .ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        await reload.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _engineFactory.Runs.Select(run => run.Backend).ShouldBe([NativeBackend.Gpu, NativeBackend.Gpu]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Run Gpu", "Dispose Gpu",
            "Load Cpu", "WarmUp Cpu",
        ]);
        _sut.ActiveBackend.ShouldBe("CPU");
    }

    [Fact]
    public async Task TranscribeAsync_AfterReloadStarted_IsRejectedAsStillLoading()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = _ => loadGate.Wait(SignalTimeout);
        var reload = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);

        try
        {
            // Act
            var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
                TestContext.Current.CancellationToken);

            // Assert
            var exception = await Should.ThrowAsync<TranscriberNotReadyException>(transcription);
            exception.Status.ShouldBe(TranscriberStatus.Loading);
            _sut.Status.ShouldBe(TranscriberStatus.Loading);
            _sut.ActiveBackend.ShouldBeNull();
        }
        finally
        {
            loadGate.Set();
        }

        await reload.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _engineFactory.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_WhileLoading_EndsReadyWithNewestBackendWithoutReadyInBetween()
    {
        // Arrange
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = _ => loadGate.Wait(SignalTimeout);
        var firstLoad = _sut.LoadAsync(_model, BackendPreference.Auto, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Gpu"));

        // Act
        var secondLoad = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);
        loadGate.Set();

        // Assert
        await firstLoad.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        await secondLoad.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        StatusChanges().ShouldBe([TranscriberStatus.Loading, TranscriberStatus.Ready]);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Dispose Gpu", "Load Cpu", "WarmUp Cpu",
        ]);
        _engineFactory.Overlapped.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_QueuedLoadReplacedByNewerOne_IsNeverLoaded()
    {
        // Arrange
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = _ => loadGate.Wait(SignalTimeout);
        var firstLoad = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Cpu"));

        // Act
        var replacedLoad = _sut.LoadAsync(_model, BackendPreference.Gpu, TestContext.Current.CancellationToken);
        var newestLoad = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);
        loadGate.Set();

        // Assert
        await Task.WhenAll(firstLoad, replacedLoad, newestLoad)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _engineFactory.Calls.ShouldBe(["Load Cpu", "WarmUp Cpu", "Dispose Cpu", "Load Cpu", "WarmUp Cpu"]);
        StatusChanges().ShouldBe([TranscriberStatus.Loading, TranscriberStatus.Ready]);
    }

    [Fact]
    public async Task LoadAsync_ReplacedLoadFails_ReportsNoFailure()
    {
        // Arrange
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = backend =>
        {
            loadGate.Wait(SignalTimeout);
            if (backend == NativeBackend.Gpu)
            {
                throw new NativeEngineException(NativeStatus.ErrBackend);
            }
        };
        var failingLoad = _sut.LoadAsync(_model, BackendPreference.Gpu, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Gpu"));

        // Act
        var newestLoad = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);
        loadGate.Set();

        // Assert
        await Task.WhenAll(failingLoad, newestLoad).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        StatusChanges().ShouldBe([TranscriberStatus.Loading, TranscriberStatus.Ready]);
        _sut.FailureMessage.ShouldBeNull();
        _sut.ActiveBackend.ShouldBe("CPU");
    }

    [Fact]
    public async Task TranscribeAsync_GpuBackendErrorWhileReloadQueued_SkipsCpuFallbackAndLoadsNewest()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var runGate = new ManualResetEventSlim();
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Gpu})
            {
                runStarted.TrySetResult();
                runGate.Wait(SignalTimeout);
                throw new NativeEngineException(NativeStatus.ErrBackend);
            }

            return new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var failing = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await runStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var reload = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);

        // Act
        runGate.Set();

        // Assert
        await Should.ThrowAsync<TranscriptionFailedException>(
            failing.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        await reload.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
        ]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu",
        ]);
        _sut.ActiveBackend.ShouldBe("CPU");
    }

    [Fact]
    public async Task TranscribeAsync_GpuBackendErrorAndReloadRequestedDuringCpuFallback_IsRejectedAndLoadsNewest()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Cpu)
            {
                loadGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = run => run is {IsWarmUp: false, Backend: NativeBackend.Gpu}
            ? throw new NativeEngineException(NativeStatus.ErrBackend)
            : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        var failing = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Cpu"));
        var reload = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);

        // Act
        loadGate.Set();

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(
            failing.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        exception.StatusCode.ShouldBe(NativeStatus.ErrBackend);
        await reload.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
        ]);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Dispose Cpu", "Load Cpu", "WarmUp Cpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_WhileLoading_IsRejectedAsStillLoading()
    {
        // Arrange
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = _ => loadGate.Wait(SignalTimeout);
        var load = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);

        try
        {
            // Act
            var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
                TestContext.Current.CancellationToken);

            // Assert
            var exception = await Should.ThrowAsync<TranscriberNotReadyException>(transcription);
            exception.Status.ShouldBe(TranscriberStatus.Loading);
            exception.Message.ShouldContain("still loading");
        }
        finally
        {
            loadGate.Set();
        }

        await load.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _engineFactory.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task TranscribeAsync_NotLoaded_IsRejected()
    {
        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<TranscriberNotReadyException>(transcription);
        exception.Status.ShouldBe(TranscriberStatus.NotLoaded);
    }

    [Fact]
    public async Task TranscribeAsync_Ready_ReturnsTextAndPassesOptions()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        var samples = Audio(2, 0.1f);

        // Act
        var result = await _sut.TranscribeAsync(samples, GermanToEnglish, TestContext.Current.CancellationToken);

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        result.AudioDuration.ShouldBe(TimeSpan.FromSeconds(2));
        var run = _engineFactory.Runs.ShouldHaveSingleItem();
        run.Samples.ShouldBeSameAs(samples);
        run.Task.ShouldBe(TranscriptionTask.Translate);
        run.SourceLanguage.ShouldBe("de");
        run.TargetLanguage.ShouldBe("en");
    }

    [Fact]
    public async Task TranscribeAsync_EmptySamples_ReturnsEmptyTextWithoutRunningModel()
    {
        // Arrange
        await LoadAsync(BackendPreference.Cpu);

        // Act
        var result = await _sut.TranscribeAsync([], GermanToEnglish, TestContext.Current.CancellationToken);

        // Assert
        result.Text.ShouldBeEmpty();
        result.AudioDuration.ShouldBe(TimeSpan.Zero);
        _engineFactory.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task TranscribeAsync_AudioLongerThanModelMaximum_IsRejectedWithoutRunningModel()
    {
        // Arrange
        _engineFactory.MaxAudio = TimeSpan.FromSeconds(401);
        await LoadAsync(BackendPreference.Cpu);

        // Act
        var transcription = _sut.TranscribeAsync(Audio(401, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<AudioTooLongException>(transcription);
        exception.AudioDuration.ShouldBe(TimeSpan.FromSeconds(401));
        exception.MaxInputDuration.ShouldBe(TimeSpan.FromSeconds(400));
        _engineFactory.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task MaxInputDuration_NativeEngineReports400Seconds_Is399Seconds()
    {
        // Arrange
        _engineFactory.MaxAudio = TimeSpan.FromSeconds(400);

        // Act
        await LoadAsync(BackendPreference.Cpu);

        // Assert
        _sut.MaxInputDuration.ShouldBe(TimeSpan.FromSeconds(399));
    }

    [Fact]
    public async Task MaxInputDuration_NativeEngineReportsNoLimit_Is400Seconds()
    {
        // Arrange
        _engineFactory.MaxAudio = TimeSpan.Zero;

        // Act
        await LoadAsync(BackendPreference.Cpu);

        // Assert
        _sut.MaxInputDuration.ShouldBe(TimeSpan.FromSeconds(400));
    }

    [Fact]
    public async Task TranscribeAsync_LanguageNotSupportedByModel_IsRejectedWithoutRunningModel()
    {
        // Arrange
        await LoadAsync(BackendPreference.Cpu);

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f),
            new TranscriptionOptions(TranscriptionTask.Translate, "de", "fr"), TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<LanguageNotSupportedException>(transcription);
        _engineFactory.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task TranscribeAsync_OverlappingRequests_RunOneAfterAnotherInArrivalOrder()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var firstRunGate = new ManualResetEventSlim();
        var firstRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run.Samples.Length == 16_000)
            {
                firstRunStarted.TrySetResult();
                firstRunGate.Wait(SignalTimeout);
            }

            return new NativeRunOutput($"text {run.Samples.Length}", false);
        };
        var first = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await firstRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        var second = _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        var third = _sut.TranscribeAsync(Audio(3, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        var secondCompletedWhileFirstRan = second.IsCompleted;
        firstRunGate.Set();

        // Assert
        (await first.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text.ShouldBe("text 16000");
        (await second.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text.ShouldBe("text 32000");
        (await third.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text.ShouldBe("text 48000");
        secondCompletedWhileFirstRan.ShouldBeFalse();
        _engineFactory.Runs.Select(run => run.Samples.Length).ShouldBe([16_000, 32_000, 48_000]);
        _engineFactory.Overlapped.ShouldBeFalse();
        _engineFactory.Calls.Count(call => call.StartsWith("Load", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task TranscribeAsync_QueuedRequestCancelled_EndsCancelledAtOnceWithoutRunning()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var runGate = new ManualResetEventSlim();
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (!run.IsWarmUp)
            {
                runStarted.TrySetResult();
                runGate.Wait(SignalTimeout);
            }

            return new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var running = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await runStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        using var queuedCancellation = new CancellationTokenSource();
        var queued = _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, queuedCancellation.Token);

        try
        {
            // Act
            await queuedCancellation.CancelAsync();

            // Assert
            await Should.ThrowAsync<OperationCanceledException>(
                queued.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
            running.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            runGate.Set();
        }

        await running.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        await _sut.StopAsync(TestContext.Current.CancellationToken);
        _engineFactory.Runs.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task TranscribeAsync_CallerCancelsRunningRequest_EndsCancelledAtOnceAndAbortsRun()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runAborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            runStarted.TrySetResult();
            run.CancellationToken.WaitHandle.WaitOne(SignalTimeout);
            if (run.CancellationToken.IsCancellationRequested)
            {
                runAborted.TrySetResult();
            }

            throw new OperationCanceledException(run.CancellationToken);
        };
        using var callerCancellation = new CancellationTokenSource();
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, callerCancellation.Token);
        await runStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        await callerCancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(
            transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        await runAborted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe(Gpu);
        StatusChanges().ShouldBe([TranscriberStatus.Loading, TranscriberStatus.Ready]);
    }

    [Fact]
    public async Task TranscribeAsync_CallerCancelsRunThatReturnsLate_NextRequestRunsAfterIt()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var cancelledRunGate = new ManualResetEventSlim();
        var cancelledRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run.Samples.Length == 16_000)
            {
                // Ignores the cancellation, as the native engine does during an encoder pass.
                cancelledRunStarted.TrySetResult();
                cancelledRunGate.Wait(SignalTimeout);
            }

            return new NativeRunOutput($"text {run.Samples.Length}", false);
        };
        using var callerCancellation = new CancellationTokenSource();
        var cancelled = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, callerCancellation.Token);
        await cancelledRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        Task<TranscriptionResult> next;

        try
        {
            // Act
            await callerCancellation.CancelAsync();
            next = _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);

            // Assert
            await Should.ThrowAsync<OperationCanceledException>(
                cancelled.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
            next.IsCompleted.ShouldBeFalse();
            _engineFactory.Runs.ShouldHaveSingleItem();
        }
        finally
        {
            cancelledRunGate.Set();
        }

        (await next.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text.ShouldBe("text 32000");
        _engineFactory.Runs.Select(run => run.Samples.Length).ShouldBe([16_000, 32_000]);
        _engineFactory.Overlapped.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_AutoAndGpuLoadFails_IsReadyOnCpu()
    {
        // Arrange
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Gpu)
            {
                throw new NativeEngineException(NativeStatus.ErrBackend);
            }
        };

        // Act
        await LoadAsync(BackendPreference.Auto);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.ShouldBe(["IsGpuAvailable", "Load Gpu", "Load Cpu", "WarmUp Cpu"]);
    }

    [Fact]
    public async Task LoadAsync_AutoAndGpuWarmUpFails_DisposesGpuAndIsReadyOnCpu()
    {
        // Arrange
        _engineFactory.OnRun = run => run.Backend == NativeBackend.Gpu
            ? throw new NativeEngineException(NativeStatus.ErrBackend)
            : new NativeRunOutput(string.Empty, false);

        // Act
        await LoadAsync(BackendPreference.Auto);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Dispose Gpu", "Load Cpu", "WarmUp Cpu",
        ]);
    }

    [Fact]
    public async Task LoadAsync_AutoAndGpuUnavailable_IsReadyOnCpu()
    {
        // Arrange
        _engineFactory.GpuAvailable = false;

        // Act
        await LoadAsync(BackendPreference.Auto);

        // Assert
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.ShouldBe(["IsGpuAvailable", "Load Cpu", "WarmUp Cpu"]);
    }

    [Fact]
    public async Task LoadAsync_ForcedGpuFails_IsFailedWithoutCpuAttempt()
    {
        // Arrange
        _engineFactory.OnLoad = _ => throw new NativeEngineException(NativeStatus.ErrBackend);

        // Act
        await LoadAsync(BackendPreference.Gpu);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _sut.ActiveBackend.ShouldBeNull();
        _sut.FailureMessage.ShouldBe(TranscribeCppTranscriber.LoadFailedMessage);
        _engineFactory.Calls.ShouldBe(["Load Gpu"]);
    }

    [Fact]
    public async Task LoadAsync_ForcedCpu_NeverTouchesGpu()
    {
        // Act
        await LoadAsync(BackendPreference.Cpu);

        // Assert
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.ShouldBe(["Load Cpu", "WarmUp Cpu"]);
    }

    [Fact]
    public async Task LoadAsync_AutoAndInvalidModelOnGpu_IsFailedWithoutCpuAttempt()
    {
        // Arrange
        _engineFactory.OnLoad = _ => throw new NativeEngineException(NativeStatus.ErrGguf);

        // Act
        await LoadAsync(BackendPreference.Auto);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _engineFactory.Calls.ShouldBe(["IsGpuAvailable", "Load Gpu"]);
    }

    [Fact]
    public async Task LoadAsync_WarmUpOutputTruncated_IsReadyOnGpu()
    {
        // Arrange
        _engineFactory.OnRun = _ => new NativeRunOutput("partial", true);

        // Act
        await LoadAsync(BackendPreference.Auto);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe(Gpu);
        _engineFactory.Calls.ShouldBe(["IsGpuAvailable", "Load Gpu", "WarmUp Gpu"]);
    }

    [Fact]
    public async Task LoadAsync_OnGpu_WarmsUpOnTenSecondsOfFixedLowLevelNoise()
    {
        // Act
        await LoadAsync(BackendPreference.Gpu);

        // Assert
        var warmUp = _engineFactory.AllRuns.ShouldHaveSingleItem();
        warmUp.Backend.ShouldBe(NativeBackend.Gpu);
        warmUp.Task.ShouldBe(TranscriptionTask.Transcribe);
        warmUp.SourceLanguage.ShouldBe("en");
        warmUp.Samples.Length.ShouldBe(10 * TranscribeCppTranscriber.SampleRate);
        warmUp.Samples.ShouldAllBe(sample => sample >= -0.1f && sample <= 0.1f);
        warmUp.Samples.Max().ShouldBeGreaterThan(0.09f);
        warmUp.Samples.Min().ShouldBeLessThan(-0.09f);
        warmUp.Samples.ShouldBe(TranscribeCppTranscriber.CreateWarmUpSamples(NativeBackend.Gpu));
    }

    [Fact]
    public async Task LoadAsync_OnCpu_WarmsUpOnOneSecondOfSilence()
    {
        // Act
        await LoadAsync(BackendPreference.Cpu);

        // Assert
        var warmUp = _engineFactory.AllRuns.ShouldHaveSingleItem();
        warmUp.Backend.ShouldBe(NativeBackend.Cpu);
        warmUp.Task.ShouldBe(TranscriptionTask.Transcribe);
        warmUp.SourceLanguage.ShouldBe("en");
        warmUp.Samples.Length.ShouldBe(TranscribeCppTranscriber.SampleRate);
        warmUp.Samples.ShouldAllBe(sample => sample == 0);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendError_ReloadOnCpuWarmsUpOnOneSecondOfSilence()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = run => run.Backend == NativeBackend.Gpu
            ? throw new NativeEngineException(NativeStatus.ErrBackend)
            : new NativeRunOutput(string.Empty, false);

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        await transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe("CPU");
        var runs = _engineFactory.AllRuns;
        runs.Select(run => (run.Backend, run.Samples.Length)).ShouldBe([
            (NativeBackend.Gpu, 10 * TranscribeCppTranscriber.SampleRate),
            (NativeBackend.Gpu, TranscribeCppTranscriber.SampleRate),
            (NativeBackend.Cpu, TranscribeCppTranscriber.SampleRate),
            (NativeBackend.Cpu, TranscribeCppTranscriber.SampleRate),
        ]);
        runs[2].Samples.ShouldAllBe(sample => sample == 0);
        runs[2].Task.ShouldBe(TranscriptionTask.Transcribe);
        runs[3].Samples.ShouldBeSameAs(runs[1].Samples);
    }

    [Fact]
    public async Task LoadAsync_InvalidModelWithHashMismatch_DeletesFileAndReportsDamagedModel()
    {
        // Arrange
        var model = WriteModelFile([1, 2, 3], [4, 5, 6]);
        _engineFactory.OnLoad = _ => throw new NativeEngineException(NativeStatus.ErrGguf);

        // Act
        await _sut.LoadAsync(model, BackendPreference.Cpu, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        File.Exists(_modelStore.GetModelPath(model)).ShouldBeFalse();
        _sut.FailureMessage.ShouldBe("Model file was damaged and has been removed. Download it again.");
    }

    [Fact]
    public async Task LoadAsync_InvalidModelWithMatchingHash_KeepsFileAndReportsIncompatibleModel()
    {
        // Arrange
        var model = WriteModelFile([1, 2, 3], [1, 2, 3]);
        _engineFactory.OnLoad = _ => throw new NativeEngineException(NativeStatus.ErrUnsupportedArch);

        // Act
        await _sut.LoadAsync(model, BackendPreference.Cpu, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        File.Exists(_modelStore.GetModelPath(model)).ShouldBeTrue();
        _sut.FailureMessage.ShouldBe("This model can't be loaded by this version of Pisum Transcribe.");
    }

    [Fact]
    public async Task LoadAsync_BackendFailureWithHashMismatch_KeepsFile()
    {
        // Arrange
        var model = WriteModelFile([1, 2, 3], [4, 5, 6]);
        _engineFactory.OnLoad = _ => throw new NativeEngineException(NativeStatus.ErrBackend);

        // Act
        await _sut.LoadAsync(model, BackendPreference.Gpu, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        File.Exists(_modelStore.GetModelPath(model)).ShouldBeTrue();
        _sut.FailureMessage.ShouldBe(TranscribeCppTranscriber.LoadFailedMessage);
    }

    [Fact]
    public async Task StopAsync_RunStopsWhenCancelled_CompletesPromptlyCancelsQueuedRequestAndDisposes()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            runStarted.TrySetResult();
            run.CancellationToken.WaitHandle.WaitOne(SignalTimeout);
            throw new OperationCanceledException(run.CancellationToken);
        };
        var running = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await runStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var queued = _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        var stopwatch = Stopwatch.StartNew();

        // Act
        await _sut.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3.5));
        await Should.ThrowAsync<OperationCanceledException>(running);
        await Should.ThrowAsync<OperationCanceledException>(queued);
        _engineFactory.Runs.ShouldHaveSingleItem();
        _engineFactory.Calls[^1].ShouldBe("Dispose Gpu");
    }

    [Fact]
    public async Task StopAsync_RunIgnoresCancellation_ReturnsAfterStopTimeoutWithoutDisposing()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var runGate = new ManualResetEventSlim();
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = _ =>
        {
            runStarted.TrySetResult();
            runGate.Wait(SignalTimeout);
            return new NativeRunOutput(string.Empty, false);
        };
        var running = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await runStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var stopwatch = Stopwatch.StartNew();

        // Act
        await _sut.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        var elapsed = stopwatch.Elapsed;
        var callsWhileBlocked = _engineFactory.Calls;
        runGate.Set();
        await running.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Dispose Gpu"));

        elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2.9));
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3.5));
        callsWhileBlocked.ShouldNotContain("Dispose Gpu");
        _engineFactory.Overlapped.ShouldBeFalse();
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendError_RetriesOnCpuBeforeQueuedRequest()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var firstRunGate = new ManualResetEventSlim();
        using var queuedRunGate = new ManualResetEventSlim();
        var firstRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Gpu})
            {
                firstRunStarted.TrySetResult();
                firstRunGate.Wait(SignalTimeout);
                throw new NativeEngineException(NativeStatus.ErrBackend);
            }

            if (run.Samples.Length == 32_000)
            {
                // Holds the queued request, so the failed request can only return if it ran first.
                queuedRunGate.Wait(SignalTimeout);
            }

            return new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var failing = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        await firstRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var queued = _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);

        // Act
        firstRunGate.Set();

        // Assert
        try
        {
            (await failing.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text
                .ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
            queued.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            queuedRunGate.Set();
        }

        (await queued.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text
            .ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
        ]);
        _sut.ActiveBackend.ShouldBe("CPU");
        var runs = _engineFactory.Runs;
        runs.Select(run => (run.Backend, run.Samples.Length)).ShouldBe([
            (NativeBackend.Gpu, 16_000), (NativeBackend.Cpu, 16_000), (NativeBackend.Cpu, 32_000),
        ]);
        runs[1].Samples.ShouldBeSameAs(runs[0].Samples);
        (runs[1].Task, runs[1].SourceLanguage, runs[1].TargetLanguage)
            .ShouldBe((runs[0].Task, runs[0].SourceLanguage, runs[0].TargetLanguage));
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Run Cpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendErrorOnLongestClip_RetriesOnCpuAndReturnsText()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = run => run is {IsWarmUp: false, Backend: NativeBackend.Gpu}
            ? throw new NativeEngineException(NativeStatus.ErrBackend)
            : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);

        // Act
        var result = await _sut.TranscribeAsync(Audio(399, 0.1f), GermanToEnglish,
                TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        result.AudioDuration.ShouldBe(TimeSpan.FromSeconds(399));
        _engineFactory.Runs.Select(run => (run.Backend, run.Samples.Length)).ShouldBe([
            (NativeBackend.Gpu, 399 * TranscribeCppTranscriber.SampleRate),
            (NativeBackend.Cpu, 399 * TranscribeCppTranscriber.SampleRate),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendErrorAndCpuRetryBackendError_IsFailedWithoutSecondRetry()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = run => run.IsWarmUp
            ? new NativeRunOutput(string.Empty, false)
            : throw new NativeEngineException(NativeStatus.ErrBackend);

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(
            transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        exception.StatusCode.ShouldBe(NativeStatus.ErrBackend);
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _sut.FailureMessage.ShouldBe(TranscribeCppTranscriber.BackendFailedMessage);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Dispose Cpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendErrorAndCpuRetryOtherError_IsRejectedAndStaysReadyOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = run => run switch
        {
            {IsWarmUp: true} => new NativeRunOutput(string.Empty, false),
            {Backend: NativeBackend.Gpu} => throw new NativeEngineException(NativeStatus.ErrBackend),
            _ => throw new NativeEngineException(NativeStatus.ErrInvalidArg),
        };

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(
            transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        exception.StatusCode.ShouldBe(NativeStatus.ErrInvalidArg);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Calls.Count(call => call == "Run Cpu").ShouldBe(1);
        _engineFactory.Calls.ShouldNotContain("Dispose Cpu");
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendErrorAndCpuLoadFails_IsRejectedWithGpuStatus()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = run => run is {IsWarmUp: false, Backend: NativeBackend.Gpu}
            ? throw new NativeEngineException(NativeStatus.ErrBackend)
            : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Cpu)
            {
                throw new NativeEngineException(NativeStatus.ErrOom);
            }
        };

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(
            transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        exception.StatusCode.ShouldBe(NativeStatus.ErrBackend);
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _sut.FailureMessage.ShouldBe(TranscribeCppTranscriber.LoadFailedMessage);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
        ]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopAsync_DuringCpuReloadAfterGpuBackendError_CancelsRequestWithoutRetry(
        bool warmUpObservesCancellation)
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Cpu)
            {
                loadGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Gpu})
            {
                throw new NativeEngineException(NativeStatus.ErrBackend);
            }

            if (warmUpObservesCancellation)
            {
                run.CancellationToken.ThrowIfCancellationRequested();
            }

            return new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Cpu"));
        var stopwatch = Stopwatch.StartNew();

        // Act
        var stopping = _sut.StopAsync(TestContext.Current.CancellationToken);
        loadGate.Set();
        await stopping;

        // Assert
        stopwatch.Elapsed.ShouldBeLessThan(TranscribeCppTranscriber.StopTimeout);
        await Should.ThrowAsync<OperationCanceledException>(
            transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        _engineFactory.Calls.ShouldNotContain("Run Cpu");
        _engineFactory.Overlapped.ShouldBeFalse();
    }

    [Fact]
    public async Task TranscribeAsync_CallerCancelsDuringCpuReloadAfterGpuBackendError_EndsCancelledWithoutRetry()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Cpu)
            {
                loadGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = run => run is {IsWarmUp: false, Backend: NativeBackend.Gpu}
            ? throw new NativeEngineException(NativeStatus.ErrBackend)
            : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        using var callerCancellation = new CancellationTokenSource();
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, callerCancellation.Token);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Cpu"));

        try
        {
            // Act
            await callerCancellation.CancelAsync();

            // Assert
            await Should.ThrowAsync<OperationCanceledException>(
                transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
            _sut.Status.ShouldBe(TranscriberStatus.Loading);
        }
        finally
        {
            loadGate.Set();
        }

        await WaitUntilAsync(() => _sut is {Status: TranscriberStatus.Ready, ActiveBackend: "CPU"});

        // The next request runs only after the worker has finished with the cancelled one.
        await _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _engineFactory.Runs.Select(run => (run.Backend, run.Samples.Length)).ShouldBe([
            (NativeBackend.Gpu, 16_000), (NativeBackend.Cpu, 32_000),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendErrorAndDisposeFails_ReloadsOnCpuAndRunsNextRequest()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = run => run is {IsWarmUp: false, Backend: NativeBackend.Gpu}
            ? throw new NativeEngineException(NativeStatus.ErrBackend)
            : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        _engineFactory.OnDispose = _ => throw new InvalidOperationException("Releasing the model failed.");

        // Act
        var failing = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);

        // Assert
        (await failing.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text
            .ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        _sut.ActiveBackend.ShouldBe("CPU");
        (await _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken)
                .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text
            .ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Run Cpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_ForcedGpuBackendError_IsFailedAndDisposesModel()
    {
        // Arrange
        await LoadAsync(BackendPreference.Gpu);
        _engineFactory.OnRun = _ => throw new NativeEngineException(NativeStatus.ErrBackend);

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<TranscriptionFailedException>(transcription);
        await WaitUntilAsync(() => _sut.Status == TranscriberStatus.Failed);
        _sut.FailureMessage.ShouldBe(TranscribeCppTranscriber.BackendFailedMessage);
        _engineFactory.Calls.ShouldBe(["Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu"]);
    }

    [Fact]
    public async Task TranscribeAsync_BackendErrorOnCpu_IsFailedAndDisposesModel()
    {
        // Arrange
        _engineFactory.GpuAvailable = false;
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = _ => throw new NativeEngineException(NativeStatus.ErrOom);

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(transcription);
        exception.StatusCode.ShouldBe(NativeStatus.ErrOom);
        await WaitUntilAsync(() => _sut.Status == TranscriberStatus.Failed);
        _engineFactory.Calls.ShouldBe(["IsGpuAvailable", "Load Cpu", "WarmUp Cpu", "Run Cpu", "Dispose Cpu"]);
    }

    [Fact]
    public async Task TranscribeAsync_OutputTruncated_ReturnsPartialTextAndStaysReady()
    {
        // Arrange
        await LoadAsync(BackendPreference.Cpu);
        _engineFactory.OnRun = _ => new NativeRunOutput("partial text", true);

        // Act
        var result = await _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish,
            TestContext.Current.CancellationToken);

        // Assert
        result.Text.ShouldBe("partial text");
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
    }

    [Fact]
    public async Task TranscribeAsync_OtherNativeError_FailsRequestAndStaysReady()
    {
        // Arrange
        await LoadAsync(BackendPreference.Cpu);
        _engineFactory.OnRun = run => run.Samples.Length == 16_000
            ? throw new NativeEngineException(NativeStatus.ErrUnsupportedLanguage)
            : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);

        // Act
        var failing = _sut.TranscribeAsync(Audio(1, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);
        var next = _sut.TranscribeAsync(Audio(2, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(failing);
        exception.StatusCode.ShouldBe(NativeStatus.ErrUnsupportedLanguage);
        (await next.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).Text
            .ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        _engineFactory.Calls.ShouldNotContain("Dispose Cpu");
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuOutOfMemoryOnLongClip_RetriesOnCpuThenReturnsToGpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);

        // Act
        var result = await TranscribeAsync(300);
        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);
        await TranscribeAsync(10);

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
            TranscriberStatus.Ready,
        ]);
        ReadyBackends().ShouldBe([Gpu, "CPU", Gpu]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Dispose Cpu", "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu",
        ]);
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 300), (NativeBackend.Cpu, 300), (NativeBackend.Gpu, 10),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuOutOfMemory_QueuedRequestRunsOnCpuBeforeReturn()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var failingRunGate = new ManualResetEventSlim();
        var failingRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Gpu} && Seconds(run) == 300)
            {
                failingRunStarted.TrySetResult();
                failingRunGate.Wait(SignalTimeout);
                throw new NativeEngineException(NativeStatus.ErrOom);
            }

            return new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var failing = TranscribeAsync(300);
        await failingRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var queued = TranscribeAsync(20);

        // Act
        failingRunGate.Set();

        // Assert
        await failing;
        (await queued).Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 300), (NativeBackend.Cpu, 300), (NativeBackend.Cpu, 20),
        ]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Run Cpu", "Dispose Cpu", "IsGpuAvailable", "Load Gpu", "WarmUp Gpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_DuringReturnToGpu_IsAcceptedAndRunsOnGpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var returnGate = new ManualResetEventSlim();
        var returnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Gpu)
            {
                returnStarted.TrySetResult();
                returnGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);
        await TranscribeAsync(300);
        await returnStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        Task<TranscriptionResult> waiting;

        try
        {
            // Act
            waiting = TranscribeAsync(20);

            // Assert
            waiting.IsCompleted.ShouldBeFalse();
            _sut.Status.ShouldBe(TranscriberStatus.Ready);
            _sut.ActiveBackend.ShouldBe("CPU");
        }
        finally
        {
            returnGate.Set();
        }

        (await waiting).Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        _engineFactory.Runs[^1].Backend.ShouldBe(NativeBackend.Gpu);
        _sut.ActiveBackend.ShouldBe(Gpu);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
            TranscriberStatus.Ready,
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_GpuFailsDuringReturn_WaitingRequestRunsOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var returnGate = new ManualResetEventSlim();
        var returnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Gpu)
            {
                returnStarted.TrySetResult();
                returnGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = run => run switch
        {
            {IsWarmUp: true, Backend: NativeBackend.Gpu} => throw new NativeEngineException(NativeStatus.ErrBackend),
            {Backend: NativeBackend.Gpu} when Seconds(run) == 300 =>
                throw new NativeEngineException(NativeStatus.ErrOom),
            _ => new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false),
        };
        await TranscribeAsync(300);
        await returnStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var waiting = TranscribeAsync(20);

        // Act
        returnGate.Set();

        // Assert
        (await waiting).Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        _engineFactory.Runs[^1].Backend.ShouldBe(NativeBackend.Cpu);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        ReadyBackends().ShouldBe([Gpu, "CPU", "CPU"]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Dispose Cpu", "IsGpuAvailable", "Load Gpu", "WarmUp Gpu",
            "Dispose Gpu", "Load Cpu", "WarmUp Cpu", "Run Cpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_LoadFailsOnBothBackendsDuringReturn_IsFailedAndRejectsWaitingRequest()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var returnGate = new ManualResetEventSlim();
        var returnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cpuLoads = 0;
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Gpu)
            {
                returnStarted.TrySetResult();
                returnGate.Wait(SignalTimeout);
                throw new NativeEngineException(NativeStatus.ErrBackend);
            }

            // The first load on the CPU backend is the recovery, the second is the fallback of the return.
            if (++cpuLoads == 2)
            {
                throw new NativeEngineException(NativeStatus.ErrBackend);
            }
        };
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);
        await TranscribeAsync(300);
        await returnStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var waiting = TranscribeAsync(20);

        // Act
        returnGate.Set();

        // Assert
        var exception = await Should.ThrowAsync<TranscriberNotReadyException>(waiting);
        exception.Status.ShouldBe(TranscriberStatus.Failed);
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _sut.FailureMessage.ShouldBe(TranscribeCppTranscriber.LoadFailedMessage);
        _engineFactory.Runs.Select(run => run.Backend).ShouldBe([NativeBackend.Gpu, NativeBackend.Cpu]);
    }

    [Fact]
    public async Task TranscribeAsync_BackendChangeDuringReturn_RejectsWaitingRequestAndEndsReadyOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var returnGate = new ManualResetEventSlim();
        var returnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Gpu)
            {
                returnStarted.TrySetResult();
                returnGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);
        await TranscribeAsync(300);
        await returnStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var waiting = TranscribeAsync(20);
        Task reload;

        try
        {
            // Act
            reload = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);

            // Assert
            _sut.Status.ShouldBe(TranscriberStatus.Loading);
            StatusChanges()[^1].ShouldBe(TranscriberStatus.Loading);
        }
        finally
        {
            returnGate.Set();
        }

        var exception = await Should.ThrowAsync<TranscriberNotReadyException>(waiting);
        exception.Status.ShouldBe(TranscriberStatus.Loading);
        await reload.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _sut.Status.ShouldBe(TranscriberStatus.Ready);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
            TranscriberStatus.Loading, TranscriberStatus.Ready,
        ]);
        ReadyBackends().ShouldBe([Gpu, "CPU", "CPU"]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Dispose Cpu", "IsGpuAvailable", "Load Gpu", "WarmUp Gpu",
            "Dispose Gpu", "Load Cpu", "WarmUp Cpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_BackendChangeBeforeReturnStarts_SkipsReturnAndRunsWaitingRequestOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var failingRunGate = new ManualResetEventSlim();
        using var queuedRunGate = new ManualResetEventSlim();
        var failingRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Gpu} && Seconds(run) == 300)
            {
                failingRunStarted.TrySetResult();
                failingRunGate.Wait(SignalTimeout);
                throw new NativeEngineException(NativeStatus.ErrOom);
            }

            if (Seconds(run) == 20)
            {
                // Holds the queued request, so the return waits behind it.
                queuedRunStarted.TrySetResult();
                queuedRunGate.Wait(SignalTimeout);
            }

            return new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var failing = TranscribeAsync(300);
        await failingRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var queued = TranscribeAsync(20);
        failingRunGate.Set();
        await failing;
        await queuedRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        var reload = _sut.LoadAsync(_model, BackendPreference.Cpu, TestContext.Current.CancellationToken);
        queuedRunGate.Set();

        // Assert
        (await queued).Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        await reload.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 300), (NativeBackend.Cpu, 300), (NativeBackend.Cpu, 20),
        ]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Run Cpu", "Dispose Cpu", "Load Cpu", "WarmUp Cpu",
        ]);
        ReadyBackends().ShouldBe([Gpu, "CPU", "CPU"]);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuBackendErrorOnLongClip_StaysOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrBackend, 300);

        // Act
        var result = await TranscribeAsync(300);
        await RunTwoRequestsAsync();

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        GpuLoads().ShouldBe(1);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Runs.Select(run => run.Backend).ShouldBe([
            NativeBackend.Gpu, NativeBackend.Cpu, NativeBackend.Cpu, NativeBackend.Cpu,
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_ForcedGpuOutOfMemory_IsFailedWithoutReturn()
    {
        // Arrange
        await LoadAsync(BackendPreference.Gpu);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);

        // Act
        var transcription = TranscribeAsync(300);

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(transcription);
        exception.StatusCode.ShouldBe(NativeStatus.ErrOom);
        await _sut.StopAsync(TestContext.Current.CancellationToken);
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _engineFactory.Calls.ShouldBe(["Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu"]);
    }

    [Fact]
    public async Task TranscribeAsync_AutoOnGpuOutOfMemoryAndCpuRetryBackendError_IsFailedWithoutReturn()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = run => run switch
        {
            {IsWarmUp: true} => new NativeRunOutput(string.Empty, false),
            {Backend: NativeBackend.Gpu} => throw new NativeEngineException(NativeStatus.ErrOom),
            _ => throw new NativeEngineException(NativeStatus.ErrBackend),
        };

        // Act
        var transcription = TranscribeAsync(300);

        // Assert
        var exception = await Should.ThrowAsync<TranscriptionFailedException>(transcription);
        exception.StatusCode.ShouldBe(NativeStatus.ErrBackend);
        await _sut.StopAsync(TestContext.Current.CancellationToken);
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Dispose Cpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_CpuBackendErrorBeforeReturn_StaysFailed()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var failingRunGate = new ManualResetEventSlim();
        var failingRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Gpu} && Seconds(run) == 300)
            {
                failingRunStarted.TrySetResult();
                failingRunGate.Wait(SignalTimeout);
                throw new NativeEngineException(NativeStatus.ErrOom);
            }

            return run is {IsWarmUp: false, Backend: NativeBackend.Cpu} && Seconds(run) == 20
                ? throw new NativeEngineException(NativeStatus.ErrBackend)
                : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
        };
        var failing = TranscribeAsync(300);
        await failingRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var queued = TranscribeAsync(20);

        // Act
        failingRunGate.Set();

        // Assert
        (await failing).Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        await Should.ThrowAsync<TranscriptionFailedException>(queued);
        await _sut.StopAsync(TestContext.Current.CancellationToken);
        _sut.Status.ShouldBe(TranscriberStatus.Failed);
        _sut.FailureMessage.ShouldBe(TranscribeCppTranscriber.BackendFailedMessage);
        GpuLoads().ShouldBe(1);
    }

    [Fact]
    public async Task TranscribeAsync_OutOfMemoryOnClipNotLongerThanCompletedRun_StaysOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 90);
        await TranscribeAsync(120);

        // Act
        var result = await TranscribeAsync(90);
        await RunTwoRequestsAsync();

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        GpuLoads().ShouldBe(1);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 120), (NativeBackend.Gpu, 90), (NativeBackend.Cpu, 90), (NativeBackend.Cpu, 10),
            (NativeBackend.Cpu, 10),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_OutOfMemoryAfterTruncatedLongerRun_StaysOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        var failOnGpu = FailOnGpu(NativeStatus.ErrOom, 90);
        _engineFactory.OnRun = run => Seconds(run) == 120 ? new NativeRunOutput("partial", true) : failOnGpu(run);
        (await TranscribeAsync(120)).Text.ShouldBe("partial");

        // Act
        var result = await TranscribeAsync(90);
        await RunTwoRequestsAsync();

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        GpuLoads().ShouldBe(1);
        _sut.ActiveBackend.ShouldBe("CPU");
    }

    [Fact]
    public async Task TranscribeAsync_OutOfMemoryOnShortClip_StaysOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 8);

        // Act
        var result = await TranscribeAsync(8);
        await RunTwoRequestsAsync();

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        GpuLoads().ShouldBe(1);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 8), (NativeBackend.Cpu, 8), (NativeBackend.Cpu, 10), (NativeBackend.Cpu, 10),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_OutOfMemoryAgainRightAfterReturn_StaysOnCpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300, 200);
        await TranscribeAsync(300);
        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);

        // Act
        var result = await TranscribeAsync(200);
        await RunTwoRequestsAsync();

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        GpuLoads().ShouldBe(2);
        _sut.ActiveBackend.ShouldBe("CPU");
        _engineFactory.AllRuns.Count(run => run is {IsWarmUp: true, Backend: NativeBackend.Gpu}).ShouldBe(2);
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 300), (NativeBackend.Cpu, 300), (NativeBackend.Gpu, 200),
            (NativeBackend.Cpu, 200), (NativeBackend.Cpu, 10), (NativeBackend.Cpu, 10),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_OutOfMemoryAgainAfterRunCompletedOnGpu_ReturnsAgain()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);
        await TranscribeAsync(300);
        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);
        await TranscribeAsync(10);

        // Act
        var result = await TranscribeAsync(300);
        await WaitUntilAsync(() => GpuLoads() == 3 && _sut.ActiveBackend == Gpu);

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        ReadyBackends().ShouldBe([Gpu, "CPU", Gpu, "CPU", Gpu]);
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 300), (NativeBackend.Cpu, 300), (NativeBackend.Gpu, 10),
            (NativeBackend.Gpu, 300), (NativeBackend.Cpu, 300),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_OutOfMemoryAfterModelChange_ReturnsDespiteLongerEarlierRun()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 90);
        await TranscribeAsync(120);
        var otherModel = ModelCatalog.Models.First(model => model.Id != _model.Id);
        await _sut.LoadAsync(otherModel, BackendPreference.Auto, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        var result = await TranscribeAsync(90);
        await WaitUntilAsync(() => GpuLoads() == 3 && _sut.ActiveBackend == Gpu);

        // Assert
        result.Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        ReadyBackends().ShouldBe([Gpu, Gpu, "CPU", Gpu]);
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 120), (NativeBackend.Gpu, 90), (NativeBackend.Cpu, 90),
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_CallerCancelsDuringCpuReloadAfterOutOfMemory_EndsCancelledAndReturnsToGpu()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Cpu)
            {
                loadGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);
        using var callerCancellation = new CancellationTokenSource();
        var transcription = _sut.TranscribeAsync(Audio(300, 0.1f), GermanToEnglish, callerCancellation.Token);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Cpu"));

        try
        {
            // Act
            await callerCancellation.CancelAsync();

            // Assert
            await Should.ThrowAsync<OperationCanceledException>(
                transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
            _sut.Status.ShouldBe(TranscriberStatus.Loading);
        }
        finally
        {
            loadGate.Set();
        }

        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);
        ReadyBackends().ShouldBe([Gpu, "CPU", Gpu]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Dispose Cpu", "IsGpuAvailable", "Load Gpu", "WarmUp Gpu",
        ]);
    }

    [Fact]
    public async Task TranscribeAsync_CallerCancelsCpuRetryAfterOutOfMemory_ReturnsToGpuAfterRunReturned()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        var cpuRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failOnGpu = FailOnGpu(NativeStatus.ErrOom, 300);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Cpu})
            {
                cpuRunStarted.TrySetResult();
                run.CancellationToken.WaitHandle.WaitOne(SignalTimeout);
                throw new OperationCanceledException(run.CancellationToken);
            }

            return failOnGpu(run);
        };
        using var callerCancellation = new CancellationTokenSource();
        var transcription = _sut.TranscribeAsync(Audio(300, 0.1f), GermanToEnglish, callerCancellation.Token);
        await cpuRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        await callerCancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(
            transcription.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);
        StatusChanges().ShouldBe([
            TranscriberStatus.Loading, TranscriberStatus.Ready, TranscriberStatus.Loading, TranscriberStatus.Ready,
            TranscriberStatus.Ready,
        ]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Dispose Cpu", "IsGpuAvailable", "Load Gpu", "WarmUp Gpu",
        ]);
        _engineFactory.Overlapped.ShouldBeFalse();
    }

    [Fact]
    public async Task TranscribeAsync_RequestDuringCancelledCpuRetryAfterOutOfMemory_RunsOnCpuBeforeReturn()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var cpuRunGate = new ManualResetEventSlim();
        var cpuRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failOnGpu = FailOnGpu(NativeStatus.ErrOom, 300);
        _engineFactory.OnRun = run =>
        {
            if (run is {IsWarmUp: false, Backend: NativeBackend.Cpu} && Seconds(run) == 300)
            {
                // Ignores the cancellation, as the native engine does during an encoder pass.
                cpuRunStarted.TrySetResult();
                cpuRunGate.Wait(SignalTimeout);
            }

            return failOnGpu(run);
        };
        using var callerCancellation = new CancellationTokenSource();
        var cancelled = _sut.TranscribeAsync(Audio(300, 0.1f), GermanToEnglish, callerCancellation.Token);
        await cpuRunStarted.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(
            cancelled.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken));
        Task<TranscriptionResult> next;

        try
        {
            // Act
            next = TranscribeAsync(20);

            // Assert
            next.IsCompleted.ShouldBeFalse();
            _sut.Status.ShouldBe(TranscriberStatus.Ready);
        }
        finally
        {
            cpuRunGate.Set();
        }

        (await next).Text.ShouldBe(FakeNativeSpeechEngineFactory.DefaultText);
        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);
        _engineFactory.Runs.Select(run => (run.Backend, Seconds(run))).ShouldBe([
            (NativeBackend.Gpu, 300), (NativeBackend.Cpu, 300), (NativeBackend.Cpu, 20),
        ]);
        _engineFactory.Calls.ShouldBe([
            "IsGpuAvailable", "Load Gpu", "WarmUp Gpu", "Run Gpu", "Dispose Gpu", "Load Cpu",
            "WarmUp Cpu", "Run Cpu", "Run Cpu", "Dispose Cpu", "IsGpuAvailable", "Load Gpu", "WarmUp Gpu",
        ]);
    }

    [Fact]
    public async Task StopAsync_DuringCpuReloadAfterOutOfMemory_CancelsRequestWithoutReturn()
    {
        // Arrange
        await LoadAsync(BackendPreference.Auto);
        using var loadGate = new ManualResetEventSlim();
        _engineFactory.OnLoad = backend =>
        {
            if (backend == NativeBackend.Cpu)
            {
                loadGate.Wait(SignalTimeout);
            }
        };
        _engineFactory.OnRun = FailOnGpu(NativeStatus.ErrOom, 300);
        var transcription = TranscribeAsync(300);
        await WaitUntilAsync(() => _engineFactory.Calls.Contains("Load Cpu"));

        // Act
        var stopping = _sut.StopAsync(TestContext.Current.CancellationToken);
        loadGate.Set();
        await stopping;

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(transcription);
        GpuLoads().ShouldBe(1);
        _engineFactory.Calls.ShouldNotContain("Run Cpu");
        _engineFactory.Overlapped.ShouldBeFalse();
    }

    private static float[] Audio(int seconds, float value)
    {
        var samples = new float[seconds * TranscribeCppTranscriber.SampleRate];
        Array.Fill(samples, value);
        return samples;
    }

    private static int Seconds(FakeRun run)
    {
        return run.Samples.Length / TranscribeCppTranscriber.SampleRate;
    }

    /// <summary>
    /// Fails the GPU runs of requests with one of the given lengths in seconds, and completes every other run.
    /// </summary>
    private static Func<FakeRun, NativeRunOutput> FailOnGpu(NativeStatus status, params int[] seconds)
    {
        return run => run is {IsWarmUp: false, Backend: NativeBackend.Gpu} && seconds.Contains(Seconds(run))
            ? throw new NativeEngineException(status)
            : new NativeRunOutput(FakeNativeSpeechEngineFactory.DefaultText, false);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.ShouldBeLessThan(SignalTimeout);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private Task LoadAsync(BackendPreference backend)
    {
        return _sut.LoadAsync(_model, backend, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
    }

    private Task<TranscriptionResult> TranscribeAsync(int seconds)
    {
        return _sut.TranscribeAsync(Audio(seconds, 0.1f), GermanToEnglish, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs two 10 s requests one after the other. The first runs after the worker has finished the requests before it,
    /// so the second is queued after anything the worker queued meanwhile, such as a return to the GPU.
    /// </summary>
    private async Task RunTwoRequestsAsync()
    {
        await TranscribeAsync(10);
        await TranscribeAsync(10);
    }

    private int GpuLoads()
    {
        return _engineFactory.Calls.Count(call => call == "Load Gpu");
    }

    private List<TranscriberStatus> StatusChanges()
    {
        lock (_statusChanges)
        {
            return _statusChanges.ToList();
        }
    }

    /// <summary>
    /// The active backend at each change to <see cref="TranscriberStatus.Ready"/>.
    /// </summary>
    private List<string?> ReadyBackends()
    {
        lock (_statusChanges)
        {
            return _readyBackends.ToList();
        }
    }

    /// <summary>
    /// Writes a model file and returns a catalog model whose hash is the hash of <paramref name="catalogContent"/>.
    /// </summary>
    private SpeechModel WriteModelFile(byte[] fileContent, byte[] catalogContent)
    {
        var model = _model with
        {
            FileName = "test-model.gguf",
            SizeBytes = fileContent.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(catalogContent)),
        };
        Directory.CreateDirectory(_root.Path);
        File.WriteAllBytes(_modelStore.GetModelPath(model), fileContent);
        return model;
    }
}
