using System.Diagnostics;
using System.Globalization;
using System.Text;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.Transcription;

/// <summary>
/// Measures German to English latency for each installed catalog model on Vulkan and CPU, how long a cancelled run of
/// the default model takes to return on CPU, and how long clips of the default model behave on Vulkan. Needs the German
/// clip from <see cref="HardwareTestAssets"/>. Asserts nothing about speed or outcome; the tables go to the diagnostic
/// messages, which <c>dotnet test</c> does not print. Run the test executable instead:
/// <c>Pisum.Transcribe.Tests.exe -explicit only -class "*.TranscribeCppBenchmarkTests" -diagnostics</c>.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class TranscribeCppBenchmarkTests
{
    private const int WarmRuns = 5;
    private const string ForcedAllocationSizeVariable = "GGML_VK_FORCE_MAX_ALLOCATION_SIZE";
    private const string ForcedBufferSizeVariable = "GGML_VK_FORCE_MAX_BUFFER_SIZE";

    // 400 s would be 5001 encoder frames, one more than the default model accepts.
    private static readonly int[] CancelledClipSeconds = [60, 120, 399];
    private static readonly TimeSpan CancelAfter = TimeSpan.FromMilliseconds(100);

    // In increasing order, like a real session, because the compute buffers only grow.
    private static readonly int[] LongClipSeconds = [30, 60, 120, 240, 399];

    [Fact(Explicit = true)]
    public void Run_InstalledModelsOnVulkanAndCpu_ReportsLatencyTable()
    {
        // Arrange
        var samples = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable);
        var models = HardwareTestAssets.InstalledModelsOrSkip();
        var factory = new TranscribeCppEngineFactory();
        var vulkanAvailable = factory.IsVulkanAvailable();
        var table = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture,
                $"Clip: {(double) samples.Length / TranscribeCppTranscriber.SampleRate:0.0} s, de→en translate, median of {WarmRuns} warm runs")
            .AppendLine("| Model | Backend | Load | Warm-up | Warm median |")
            .AppendLine("|---|---|---|---|---|");

        // Act
        foreach (var model in models)
        {
            foreach (var backend in new[] {NativeBackend.Vulkan, NativeBackend.Cpu})
            {
                var row = backend == NativeBackend.Vulkan && !vulkanAvailable
                    ? "unavailable | | "
                    : Measure(factory, HardwareTestAssets.ModelStore.GetModelPath(model), backend, samples);
                table.AppendLine(CultureInfo.InvariantCulture, $"| {model.Id} | {backend} | {row} |");
            }
        }

        // Assert
        TestContext.Current.SendDiagnosticMessage(table.ToString());
    }

    /// <summary>
    /// A run cancelled early returns at its next abort check, after about one encoder pass over the clip. That is the
    /// longest a dictation waits behind a cancelled one.
    /// </summary>
    [Fact(Explicit = true)]
    public void Run_CancelledEarlyOnCpu_ReportsTimeToReturn()
    {
        // Arrange
        var clip = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable);
        var model = ModelCatalog.Resolve(null);
        Assert.SkipUnless(HardwareTestAssets.ModelStore.IsInstalled(model),
            $"The default model {model.Id} is not installed. Start the app to download it.");
        using var engine = new TranscribeCppEngineFactory().Load(HardwareTestAssets.ModelStore.GetModelPath(model),
            NativeBackend.Cpu);
        engine.Run(TranscribeCppTranscriber.CreateWarmUpSamples(NativeBackend.Cpu), TranscriptionTask.Transcribe, "en",
            "en", TestContext.Current.CancellationToken);
        var table = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture,
                $"{model.Id} on CPU, de→en translate, cancelled {CancelAfter.TotalMilliseconds:0} ms after the start")
            .AppendLine("| Clip | Time to return | Outcome |")
            .AppendLine("|---|---|---|");

        // Act
        foreach (var seconds in CancelledClipSeconds)
        {
            var (elapsed, outcome) = RunCancelled(engine, Repeat(clip, seconds * TranscribeCppTranscriber.SampleRate));
            table.AppendLine(CultureInfo.InvariantCulture,
                $"| {seconds} s | {elapsed.TotalSeconds:0.00} s | {outcome} |");
        }

        // Assert
        TestContext.Current.SendDiagnosticMessage(table.ToString());
    }

    /// <summary>
    /// Runs longer and longer clips on Vulkan until one fails. After the failure, runs the warm-up input on the same
    /// engine, then on the model loaded on Vulkan again, which the return to Vulkan after an out-of-memory error depends
    /// on. Each row is sent as soon as it is measured, so the rows so far survive a native crash.
    /// </summary>
    [Fact(Explicit = true)]
    public void Run_LongClipsOnVulkan_ReportsOutcomeAndReload()
    {
        // Arrange
        var clip = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable);
        var model = ModelCatalog.Resolve(null);
        Assert.SkipUnless(HardwareTestAssets.ModelStore.IsInstalled(model),
            $"The default model {model.Id} is not installed. Start the app to download it.");
        var factory = new TranscribeCppEngineFactory();
        Assert.SkipUnless(factory.IsVulkanAvailable(), "No Vulkan device is available.");
        var modelPath = HardwareTestAssets.ModelStore.GetModelPath(model);
        var warmUpSamples = TranscribeCppTranscriber.CreateWarmUpSamples(NativeBackend.Vulkan);
        var engine = factory.Load(modelPath, NativeBackend.Vulkan);
        try
        {
            Report(
                $"{model.Id} on Vulkan, de→en translate, max audio {engine.MaxAudio.TotalSeconds:0.00} s, {ForcedAllocationSizeVariable}={Environment.GetEnvironmentVariable(ForcedAllocationSizeVariable) ?? "not set"}, {ForcedBufferSizeVariable}={Environment.GetEnvironmentVariable(ForcedBufferSizeVariable) ?? "not set"}");
            var (warmUpOutcome, warmUpElapsed, _) = RunTimed(engine, warmUpSamples, TranscriptionTask.Transcribe, "en");
            Report($"Warm-up input: {warmUpOutcome} in {warmUpElapsed.TotalSeconds:0.00} s");
            Report($"| Clip | Outcome | Time until Run returned |");
            Report($"|---|---|---|");

            // Act
            var failed = false;
            foreach (var seconds in LongClipSeconds)
            {
                var samples = Repeat(clip, seconds * TranscribeCppTranscriber.SampleRate);
                (var outcome, var elapsed, failed) = RunTimed(engine, samples, TranscriptionTask.Translate, "de");
                Report($"| {seconds} s | {outcome} | {elapsed.TotalSeconds:0.00} s |");
                if (failed)
                {
                    break;
                }
            }

            if (!failed)
            {
                Report($"No clip failed, so the engine was not loaded again.");
            }
            else
            {
                (warmUpOutcome, warmUpElapsed, _) =
                    RunTimed(engine, warmUpSamples, TranscriptionTask.Transcribe, "en");
                Report(
                    $"Same engine after the failure, warm-up input: {warmUpOutcome} in {warmUpElapsed.TotalSeconds:0.00} s");

                engine.Dispose();
                engine = null;
                var started = Stopwatch.GetTimestamp();
                try
                {
                    engine = factory.Load(modelPath, NativeBackend.Vulkan);
                    var load = Stopwatch.GetElapsedTime(started);
                    (warmUpOutcome, warmUpElapsed, _) =
                        RunTimed(engine, warmUpSamples, TranscriptionTask.Transcribe, "en");
                    Report(
                        $"Reload on Vulkan: loaded in {load.TotalSeconds:0.00} s, warm-up input: {warmUpOutcome} in {warmUpElapsed.TotalSeconds:0.00} s");
                }
                catch (NativeEngineException exception)
                {
                    Report($"Reload on Vulkan: load failed: {exception.Status}");
                }
            }
        }
        finally
        {
            engine?.Dispose();
        }

        using var cpuEngine = factory.Load(modelPath, NativeBackend.Cpu);
        Report($"{model.Id} on CPU, max audio {cpuEngine.MaxAudio.TotalSeconds:0.00} s");

        // Assert: nothing, the rows are the result.
    }

    private static void Report(FormattableString row)
    {
        TestContext.Current.SendDiagnosticMessage(FormattableString.Invariant(row));
    }

    /// <summary>
    /// Runs a request, from German to English for a translation, and times it.
    /// </summary>
    private static (string Outcome, TimeSpan Elapsed, bool Failed) RunTimed(INativeSpeechEngine engine,
                                                                            float[] samples,
                                                                            TranscriptionTask task,
                                                                            string sourceLanguage)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var output = engine.Run(samples, task, sourceLanguage, "en", TestContext.Current.CancellationToken);
            return (output.Truncated ? "completed, truncated" : "completed", Stopwatch.GetElapsedTime(started), false);
        }
        catch (NativeEngineException exception)
        {
            return ($"failed: {exception.Status}", Stopwatch.GetElapsedTime(started), true);
        }
    }

    private static (TimeSpan Elapsed, string Outcome) RunCancelled(INativeSpeechEngine engine, float[] samples)
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = Stopwatch.GetTimestamp();
        cancellation.CancelAfter(CancelAfter);
        string outcome;
        try
        {
            engine.Run(samples, TranscriptionTask.Translate, "de", "en", cancellation.Token);
            outcome = "completed";
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
        }
        catch (NativeEngineException exception)
        {
            outcome = $"failed: {exception.Status}";
        }

        return (Stopwatch.GetElapsedTime(started), outcome);
    }

    /// <summary>
    /// Repeats a clip until it has <paramref name="length"/> samples.
    /// </summary>
    private static float[] Repeat(float[] clip, int length)
    {
        var samples = new float[length];
        for (var offset = 0; offset < length; offset += clip.Length)
        {
            clip.AsSpan(0, Math.Min(clip.Length, length - offset)).CopyTo(samples.AsSpan(offset));
        }

        return samples;
    }

    private static string Measure(TranscribeCppEngineFactory factory,
                                  string modelPath,
                                  NativeBackend backend,
                                  float[] samples)
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            using var engine = factory.Load(modelPath, backend);
            var load = Stopwatch.GetElapsedTime(started);

            started = Stopwatch.GetTimestamp();
            engine.Run(TranscribeCppTranscriber.CreateWarmUpSamples(backend), TranscriptionTask.Transcribe, "en", "en",
                TestContext.Current.CancellationToken);
            var warmUp = Stopwatch.GetElapsedTime(started);

            var runs = new List<TimeSpan>();
            for (var run = 0; run < WarmRuns; run++)
            {
                started = Stopwatch.GetTimestamp();
                engine.Run(samples, TranscriptionTask.Translate, "de", "en", TestContext.Current.CancellationToken);
                runs.Add(Stopwatch.GetElapsedTime(started));
            }

            var median = runs.Order().ElementAt(WarmRuns / 2);
            return FormattableString.Invariant($"{load.TotalSeconds:0.00} s | {warmUp.TotalSeconds:0.00} s | {median.TotalSeconds:0.00} s");
        }
        catch (Exception exception) when (exception is NativeEngineException or DllNotFoundException)
        {
            return $"failed: {exception.Message} | | ";
        }
    }
}
