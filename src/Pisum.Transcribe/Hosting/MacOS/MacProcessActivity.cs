using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Runs work inside a user-initiated <c>ProcessInfo</c> activity through the Swift helper, so App Nap doesn't throttle
/// it (design D6 of add-macos-dictation). Without the helper, an activity does nothing.
/// </summary>
internal sealed class MacProcessActivity : IProcessActivity
{
    private readonly MacNativeLibrary _library;
    private readonly ILogger<MacProcessActivity> _logger;
    private int _unavailableLogged;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="library">Tells whether the helper may be called.</param>
    /// <param name="logger">The logger.</param>
    public MacProcessActivity(MacNativeLibrary library, ILogger<MacProcessActivity> logger)
    {
        _library = library;
        _logger = logger;
    }

    /// <inheritdoc />
    public IDisposable Begin(string reason)
    {
        if (!_library.IsAvailable)
        {
            if (Interlocked.Exchange(ref _unavailableLogged, 1) == 0)
            {
                _logger.LogWarning("Without the helper libPisumMac, App Nap may throttle dictations and model loads");
            }

            return NoActivity.Instance;
        }

        return new Activity(PisumMac.ActivityBegin(reason));
    }

    private sealed class Activity(nint token) : IDisposable
    {
        private nint _token = token;

        public void Dispose()
        {
            // The token is released by the end, so it must never be ended twice.
            var token = Interlocked.Exchange(ref _token, 0);
            if (token != 0)
            {
                PisumMac.ActivityEnd(token);
            }
        }
    }

    private sealed class NoActivity : IDisposable
    {
        public static readonly NoActivity Instance = new();

        public void Dispose()
        {
        }
    }
}
