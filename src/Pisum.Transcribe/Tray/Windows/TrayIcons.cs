using Avalonia.Controls;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The status icons of the notification area, loaded from the ICOs in <c>Tray/Windows</c>. Light and Dark name the
/// taskbar's mode: a dark glyph on a light taskbar, a light one on a dark taskbar.
/// </summary>
/// <param name="ReadyLight">Ready, on a light taskbar.</param>
/// <param name="ReadyDark">Ready, on a dark taskbar.</param>
/// <param name="UnavailableLight">No model, loading or failed, on a light taskbar.</param>
/// <param name="UnavailableDark">No model, loading or failed, on a dark taskbar.</param>
/// <param name="Recording">Recording, on both taskbars.</param>
/// <param name="Transcribing">Transcribing, on both taskbars.</param>
internal sealed record TrayIcons(
    WindowIcon ReadyLight,
    WindowIcon ReadyDark,
    WindowIcon UnavailableLight,
    WindowIcon UnavailableDark,
    WindowIcon Recording,
    WindowIcon Transcribing);
