using System.Globalization;
using System.Text.RegularExpressions;

namespace Pisum.Transcribe.Updates;

/// <summary>
/// A version of the form <c>major.minor.patch</c>, optionally marked as a pre-release, such as <c>1.2.0-rc.1</c>.
/// </summary>
/// <param name="Major">The major version.</param>
/// <param name="Minor">The minor version.</param>
/// <param name="Patch">The patch version.</param>
/// <param name="IsPreRelease">Whether the version had a pre-release suffix.</param>
internal sealed partial record ReleaseVersion(int Major, int Minor, int Patch, bool IsPreRelease)
{
    /// <summary>
    /// Parses the application's own informational version, such as <c>1.1.1+b917e0e</c> or <c>0.1.0-rc.2</c>. The
    /// build metadata after <c>+</c> is ignored.
    /// </summary>
    /// <param name="version">The informational version.</param>
    /// <returns>The version, or <see langword="null"/> if it has another form.</returns>
    public static ReleaseVersion? ParseOwn(string? version)
    {
        if (version is null)
        {
            return null;
        }

        var core = version.Split('+', 2)[0];
        var parts = core.Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length != 3
            || !TryParseNumber(numbers[0], out var major)
            || !TryParseNumber(numbers[1], out var minor)
            || !TryParseNumber(numbers[2], out var patch))
        {
            return null;
        }

        return new ReleaseVersion(major, minor, patch, parts.Length == 2);
    }

    /// <summary>
    /// Parses a release tag of the form <c>v&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;</c>. A tag with a pre-release
    /// suffix or of any other form fails.
    /// </summary>
    /// <param name="tag">The tag, such as <c>v1.2.0</c>.</param>
    /// <returns>The version, or <see langword="null"/> if the tag has another form.</returns>
    public static ReleaseVersion? ParseTag(string? tag)
    {
        var match = TagPattern().Match(tag ?? string.Empty);
        if (!match.Success
            || !TryParseNumber(match.Groups[1].Value, out var major)
            || !TryParseNumber(match.Groups[2].Value, out var minor)
            || !TryParseNumber(match.Groups[3].Value, out var patch))
        {
            return null;
        }

        return new ReleaseVersion(major, minor, patch, false);
    }

    /// <summary>
    /// Decides whether this version is newer than <paramref name="running"/>: its numbers are higher, or they are equal
    /// and only <paramref name="running"/> is a pre-release.
    /// </summary>
    /// <param name="running">The version of the running application.</param>
    /// <returns><see langword="true"/> if this version is newer.</returns>
    public bool IsNewerThan(ReleaseVersion running)
    {
        var comparison = (Major, Minor, Patch).CompareTo((running.Major, running.Minor, running.Patch));
        return comparison > 0 || (comparison == 0 && running.IsPreRelease && !IsPreRelease);
    }

    /// <summary>
    /// Returns the numbers without a pre-release suffix, such as <c>1.2.0</c>.
    /// </summary>
    /// <returns>The version as text.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
    }

    private static bool TryParseNumber(string text, out int number)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    // ASCII digits only, and \z so that a trailing line break doesn't match.
    [GeneratedRegex(@"^v([0-9]+)\.([0-9]+)\.([0-9]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
