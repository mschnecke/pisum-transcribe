using System.Globalization;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// Formats model sizes, download progress and languages for display.
/// </summary>
internal static class ModelText
{
    private const double BytesPerMegabyte = 1024 * 1024;
    private const double BytesPerGigabyte = 1024 * 1024 * 1024;

    /// <summary>
    /// Formats a size in binary units with three significant digits, as Windows Explorer does, such as <c>701 MB</c>.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>The formatted size.</returns>
    public static string FormatSize(long bytes)
    {
        var (bytesPerUnit, unit) = ChooseUnit(bytes);
        var value = bytes / bytesPerUnit;
        return $"{value.ToString(NumberFormat(value), CultureInfo.InvariantCulture)} {unit}";
    }

    /// <summary>
    /// Formats the download progress in the unit and precision of the total, such as <c>0.50 of 1.07 GB</c>.
    /// </summary>
    /// <param name="bytesReceived">The bytes received.</param>
    /// <param name="totalBytes">The total bytes.</param>
    /// <returns>The formatted progress.</returns>
    public static string FormatProgress(long bytesReceived, long totalBytes)
    {
        var (bytesPerUnit, unit) = ChooseUnit(totalBytes);
        var total = totalBytes / bytesPerUnit;
        var format = NumberFormat(total);
        var received = (bytesReceived / bytesPerUnit).ToString(format, CultureInfo.InvariantCulture);
        return $"{received} of {total.ToString(format, CultureInfo.InvariantCulture)} {unit}";
    }

    /// <summary>
    /// Formats language codes as their English names, sorted, such as <c>English, French, German, Spanish</c>.
    /// </summary>
    /// <param name="languageCodes">ISO 639-1 codes.</param>
    /// <returns>The formatted languages.</returns>
    public static string FormatLanguages(IEnumerable<string> languageCodes)
    {
        var names = languageCodes.Select(code => CultureInfo.GetCultureInfo(code).EnglishName).Order();
        return string.Join(", ", names);
    }

    private static (double BytesPerUnit, string Unit) ChooseUnit(long bytes)
    {
        // From 999.5 MB, which would round to four digits, sizes are shown in GB.
        return bytes < 999.5 * BytesPerMegabyte ? (BytesPerMegabyte, "MB") : (BytesPerGigabyte, "GB");
    }

    private static string NumberFormat(double value)
    {
        return value switch
        {
            < 9.995 => "0.00",
            < 99.95 => "0.0",
            _ => "0",
        };
    }
}
