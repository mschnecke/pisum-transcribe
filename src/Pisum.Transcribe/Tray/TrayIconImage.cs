using Avalonia.Controls;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// An icon of the tray.
/// </summary>
/// <param name="Icon">The image.</param>
/// <param name="IsTemplate">
/// Whether macOS draws the image as a template, in the menu bar's color. Only its alpha channel counts then.
/// </param>
internal sealed record TrayIconImage(WindowIcon Icon, bool IsTemplate);
