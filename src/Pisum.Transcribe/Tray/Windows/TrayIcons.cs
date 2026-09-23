using System.Drawing;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The status icons of the notification area, loaded from the ICOs in <c>Tray/Windows</c>. Light and Dark name the
/// taskbar's mode: a dark glyph on a light taskbar, a light one on a dark taskbar.
/// </summary>
/// <remarks>
/// The icons live for the application's lifetime, so their icon handles are never released.
/// </remarks>
/// <param name="ReadyLight">Ready, on a light taskbar.</param>
/// <param name="ReadyDark">Ready, on a dark taskbar.</param>
/// <param name="UnavailableLight">No model, loading or failed, on a light taskbar.</param>
/// <param name="UnavailableDark">No model, loading or failed, on a dark taskbar.</param>
/// <param name="Recording">Recording, on both taskbars.</param>
/// <param name="Transcribing">Transcribing, on both taskbars.</param>
internal sealed record TrayIcons(
    Icon ReadyLight,
    Icon ReadyDark,
    Icon UnavailableLight,
    Icon UnavailableDark,
    Icon Recording,
    Icon Transcribing);
