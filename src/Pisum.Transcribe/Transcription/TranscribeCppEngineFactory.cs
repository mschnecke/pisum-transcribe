using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using NativeTask = TranscribeCppSharp.TranscriptionTask;

namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The native speech engine on TranscribeCppSharp, the only code that references it. The native API blocks and allows
/// one compute call per model at a time, so call it from a single worker thread.
/// </summary>
internal sealed class TranscribeCppEngineFactory : INativeSpeechEngineFactory
{
#if WINDOWS
    /// <summary>
    /// The name of the platform's GPU backend, as the user sees it.
    /// </summary>
    public const string GpuBackendName = "Vulkan";

    private const BackendRequest GpuRequest = BackendRequest.BackendVulkan;
#else
    /// <summary>
    /// The name of the platform's GPU backend, as the user sees it.
    /// </summary>
    public const string GpuBackendName = "Metal";

    private const BackendRequest GpuRequest = BackendRequest.BackendMetal;
#endif

    private bool _backendsInitialized;

    /// <inheritdoc />
    public bool IsGpuAvailable()
    {
        EnsureBackendsInitialized();
        return Backends.BackendAvailable(GpuRequest);
    }

    /// <inheritdoc />
    public INativeSpeechEngine Load(string modelPath, NativeBackend backend)
    {
        EnsureBackendsInitialized();
        var request = backend == NativeBackend.Gpu ? GpuRequest : BackendRequest.BackendCpu;
        try
        {
            var model = Model.Load(modelPath, builder => builder.WithBackend(request));
            try
            {
                var session = model.CreateSession();
                return new Engine(model, session, TimeSpan.FromMilliseconds(session.GetLimits().EffectiveMaxAudioMs));
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }
        catch (TranscribeException exception)
        {
            throw new NativeEngineException((NativeStatus) exception.StatusCode, exception);
        }
    }

    private void EnsureBackendsInitialized()
    {
        if (_backendsInitialized)
        {
            return;
        }

        try
        {
            Backends.InitDefault();
        }
        catch (TranscribeException exception)
        {
            throw new NativeEngineException((NativeStatus) exception.StatusCode, exception);
        }

        _backendsInitialized = true;
    }

    private sealed class Engine : INativeSpeechEngine
    {
        private readonly Model _model;
        private readonly Session _session;

        public Engine(Model model, Session session, TimeSpan maxAudio)
        {
            _model = model;
            _session = session;
            MaxAudio = maxAudio;
        }

        public TimeSpan MaxAudio { get; }

        public NativeRunOutput Run(float[] samples,
                                   TranscriptionTask task,
                                   string sourceLanguage,
                                   string targetLanguage,
                                   CancellationToken cancellationToken)
        {
            // Canary v2 translates whenever source and target differ, so a transcription passes the source as target.
            var nativeTask = task == TranscriptionTask.Translate ? NativeTask.Translate : NativeTask.Transcribe;
            var nativeTarget = task == TranscriptionTask.Translate ? targetLanguage : sourceLanguage;
            try
            {
                var transcript = _session.Run(samples,
                    builder => builder.WithTask(nativeTask).WithLanguage(sourceLanguage)
                        .WithTargetLanguage(nativeTarget),
                    cancellationToken);
                return new NativeRunOutput(transcript.FullText, transcript.WasTruncated);
            }
            catch (TranscribeException exception) when (exception.StatusCode == Status.ErrOutputTruncated)
            {
                // Run throws for truncated output too, but the session still holds the partial text.
                return new NativeRunOutput(_session.FullText, true);
            }
            catch (TranscribeException exception)
            {
                throw new NativeEngineException((NativeStatus) exception.StatusCode, exception);
            }
        }

        public void Dispose()
        {
            // The native contract requires the model to outlive its sessions.
            _session.Dispose();
            _model.Dispose();
        }
    }
}
