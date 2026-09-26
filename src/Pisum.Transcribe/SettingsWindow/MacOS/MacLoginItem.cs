using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// "Open at login" through the running app bundle's login item, <c>SMAppService.mainApp</c> in the Swift helper
/// (design D7 of add-macos-packaging). macOS keeps the item, shows it in System Settings → General → Login Items, and
/// drops it when the app is moved to the Trash. Call it on the UI thread, where the settings window runs.
/// </summary>
internal sealed class MacLoginItem : IStartupRegistration
{
    private const int NotRegistered = 0;
    private const int Enabled = 1;
    private const int RequiresUserApproval = 2;

    private readonly bool _isAvailable;
    private readonly Func<int> _readStatus;
    private readonly Func<int> _register;
    private readonly Func<int> _unregister;
    private readonly ILogger<MacLoginItem> _logger;

    /// <summary>
    /// Initializes a new instance on the Swift helper.
    /// </summary>
    /// <param name="library">Tells whether the helper may be called.</param>
    /// <param name="logger">The logger.</param>
    public MacLoginItem(MacNativeLibrary library, ILogger<MacLoginItem> logger)
        : this(library.IsAvailable, PisumMac.LoginItemStatus, PisumMac.LoginItemRegister, PisumMac.LoginItemUnregister,
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance, for tests.
    /// </summary>
    /// <param name="isAvailable">Whether the helper may be called.</param>
    /// <param name="readStatus">Reads the status, as <see cref="PisumMac.LoginItemStatus"/>.</param>
    /// <param name="register">Registers the login item, as <see cref="PisumMac.LoginItemRegister"/>.</param>
    /// <param name="unregister">Unregisters the login item, as <see cref="PisumMac.LoginItemUnregister"/>.</param>
    /// <param name="logger">The logger.</param>
    public MacLoginItem(bool isAvailable,
                        Func<int> readStatus,
                        Func<int> register,
                        Func<int> unregister,
                        ILogger<MacLoginItem> logger)
    {
        _isAvailable = isAvailable;
        _readStatus = readStatus;
        _register = register;
        _unregister = unregister;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        return _isAvailable && _readStatus() == Enabled;
    }

    /// <inheritdoc />
    public bool RequiresApproval()
    {
        return _isAvailable && _readStatus() == RequiresUserApproval;
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (!_isAvailable)
        {
            throw new IOException("The login item can't be changed without the helper libPisumMac.");
        }

        var current = _readStatus();
        if (enabled ? current == Enabled : current is NotRegistered or > RequiresUserApproval)
        {
            return;
        }

        var status = enabled ? _register() : _unregister();
        if (status != 0)
        {
            throw new IOException($"The login item couldn't be {(enabled ? "registered" : "unregistered")} " +
                                  $"(ServiceManagement error {status}).");
        }

        _logger.LogInformation("Open at login is now {State}", enabled ? "on" : "off");
    }
}
