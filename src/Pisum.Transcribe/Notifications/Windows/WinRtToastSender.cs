using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// Shows a toast through <see cref="ToastNotificationManager"/>.
/// </summary>
internal sealed class WinRtToastSender : IToastSender
{
    /// <inheritdoc />
    public void Send(string toastXml)
    {
        var document = new XmlDocument();
        document.LoadXml(toastXml);

        // Not ToastNotifier.Setting first: it throws until the application has shown its first toast (design D1).
        ToastNotificationManager.CreateToastNotifier(ToastRegistration.AppUserModelId)
            .Show(new ToastNotification(document));
    }
}
