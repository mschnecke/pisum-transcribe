using System.Globalization;
using Avalonia.Data.Converters;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Shows a section of the settings window: converts the navigation's selected index to <see langword="true"/> when it
/// equals the section index given as the converter parameter.
/// </summary>
internal sealed class SectionIndexConverter : IValueConverter
{
    /// <summary>
    /// The shared instance.
    /// </summary>
    public static readonly SectionIndexConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int selectedIndex && parameter is string section &&
               selectedIndex == int.Parse(section, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
