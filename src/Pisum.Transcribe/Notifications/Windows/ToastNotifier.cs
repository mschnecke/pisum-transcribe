using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The <see cref="INotifier"/> that shows a notification as a Windows toast from Pisum Transcribe. The toast has no
/// click action, and it stays in the notification center after the application has ended.
/// </summary>
internal sealed class ToastNotifier : INotifier
{
    private readonly IToastSender _sender;
    private readonly ILogger<ToastNotifier> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="sender">Shows the toast.</param>
    /// <param name="logger">The logger.</param>
    public ToastNotifier(IToastSender sender, ILogger<ToastNotifier> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Show(string title, string message)
    {
        // Windows shows a toast from any thread. A toast that can't be shown, for example because the user turned the
        // application's notifications off, never reaches the caller.
        try
        {
            _sender.Send(BuildToastXml(title, message));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not show a notification");
        }
    }

    /// <summary>
    /// Builds the content of a <c>ToastGeneric</c> toast with the title and the message as its two texts.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification text.</param>
    /// <returns>The toast XML, with the texts escaped.</returns>
    internal static string BuildToastXml(string title, string message)
    {
        var toast = new XElement("toast",
            new XElement("visual",
                new XElement("binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", title),
                    new XElement("text", message))));
        return toast.ToString(SaveOptions.DisableFormatting);
    }
}
