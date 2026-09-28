using System.Reflection;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The version of the running application, as the settings window shows it and the update check compares it: the
/// informational version without the build metadata, the commit after the <c>+</c> that the SDK appends.
/// </summary>
internal static class AppVersion
{
    /// <summary>
    /// The application's version without build metadata, such as <c>1.5.0</c> or <c>1.4.0-rc.1</c>, or
    /// <see langword="null"/> when the assembly has no informational version.
    /// </summary>
    public static string? Current { get; } = WithoutBuildMetadata(typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Removes the build metadata from a version.
    /// </summary>
    /// <param name="version">A version such as <c>1.5.0+fad2d9c</c>.</param>
    /// <returns>The text before the first <c>+</c>, or <see langword="null"/> for <see langword="null"/>.</returns>
    public static string? WithoutBuildMetadata(string? version) => version?.Split('+', 2)[0];
}
