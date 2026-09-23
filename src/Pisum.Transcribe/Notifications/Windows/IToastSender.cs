namespace Pisum.Transcribe.Notifications;

/// <summary>
/// Shows a Windows toast, the seam between <see cref="ToastNotifier"/> and the Windows notification API.
/// </summary>
internal interface IToastSender
{
    /// <summary>
    /// Shows a toast as the application's AppUserModelID (<see cref="ToastRegistration.AppUserModelId"/>).
    /// </summary>
    /// <param name="toastXml">The toast content, in the toast XML schema.</param>
    void Send(string toastXml);
}
