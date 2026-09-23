using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The host's lifetime on macOS. It handles no signals, unlike the default console lifetime, which would stop the host
/// by itself on <c>SIGTERM</c>. <see cref="ShutdownCoordinator"/> ends the application, and <c>App</c> handles
/// <c>SIGTERM</c> as a termination request. Like the console lifetime, it logs when the host has started.
/// </summary>
internal sealed class SignalFreeHostLifetime : IHostLifetime
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<SignalFreeHostLifetime> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="applicationLifetime">The host's application lifetime.</param>
    /// <param name="logger">The logger.</param>
    public SignalFreeHostLifetime(IHostApplicationLifetime applicationLifetime, ILogger<SignalFreeHostLifetime> logger)
    {
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        _applicationLifetime.ApplicationStarted.Register(() => _logger.LogInformation("Application started"));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
