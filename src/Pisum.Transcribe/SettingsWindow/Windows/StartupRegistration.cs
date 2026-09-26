using System.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Starts Pisum Transcribe at sign-in through the current user's <c>Run</c> registry key. At application startup it
/// points an existing entry to the current executable, for example after the application moved.
/// </summary>
/// <remarks>
/// Task Manager's startup apps page does not delete a <c>Run</c> value. It disables it with a binary value of the same
/// name under <see cref="StartupApprovedKey"/>, whose first byte is odd while disabled. That format is undocumented, so
/// a missing or unreadable value counts as enabled.
/// </remarks>
internal sealed class StartupRegistration : IStartupRegistration, IHostedService
{
    /// <summary>
    /// The key whose values Windows starts at sign-in.
    /// </summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The key where Task Manager records the startup entries the user disabled.
    /// </summary>
    public const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// The value name of the startup entry.
    /// </summary>
    public const string ValueName = "Pisum Transcribe";

    private readonly IUserRegistry _registry;
    private readonly ILogger<StartupRegistration> _logger;
    private readonly string _command;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="registry">The current user's registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="processPath">
    /// The executable to start, for tests. <see langword="null"/> uses <see cref="Environment.ProcessPath"/>.
    /// </param>
    public StartupRegistration(IUserRegistry registry,
                               ILogger<StartupRegistration> logger,
                               string? processPath = null)
    {
        _registry = registry;
        _logger = logger;
        _command = $"\"{processPath ?? Environment.ProcessPath}\"";
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        if (_registry.GetValue(RunKey, ValueName) is not string)
        {
            return false;
        }

        return !(_registry.GetValue(StartupApprovedKey, ValueName) is byte[] {Length: > 0} approval &&
                 (approval[0] & 1) == 1);
    }

    /// <inheritdoc />
    public bool RequiresApproval()
    {
        return false;
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _registry.SetValue(RunKey, ValueName, _command);
        }
        else
        {
            _registry.DeleteValue(RunKey, ValueName);
        }

        // Without Task Manager's value the entry counts as enabled, so turning it on works even after a disable there.
        _registry.DeleteValue(StartupApprovedKey, ValueName);
        _logger.LogInformation("Start with Windows is now {State}", enabled ? "on" : "off");
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_registry.GetValue(RunKey, ValueName) is string command && command != _command)
            {
                _registry.SetValue(RunKey, ValueName, _command);
                _logger.LogInformation("Pointed the startup entry to the current location of the application");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or SecurityException)
        {
            _logger.LogWarning(exception, "Could not update the startup entry");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
