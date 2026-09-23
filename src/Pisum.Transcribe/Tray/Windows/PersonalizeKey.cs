using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The <c>Personalize</c> key in the current user's registry hive, open for the application's lifetime.
/// </summary>
internal sealed class PersonalizeKey : IPersonalizeKey
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly RegistryKey? _key = Registry.CurrentUser.OpenSubKey(KeyPath);

    /// <inheritdoc />
    public object? ReadSystemUsesLightTheme()
    {
        return _key?.GetValue("SystemUsesLightTheme");
    }

    /// <inheritdoc />
    public bool NotifyOnChange(EventWaitHandle changed)
    {
        if (_key is null)
        {
            return false;
        }

        // Asynchronous: the call returns at once and the event is signaled on the change. Windows ends the watch when
        // the calling thread exits, so call it from a thread that lives as long as the watch.
        return PInvoke.RegNotifyChangeKeyValue(_key.Handle, false, REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET,
            changed.SafeWaitHandle, true) == WIN32_ERROR.ERROR_SUCCESS;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key?.Dispose();
    }
}
