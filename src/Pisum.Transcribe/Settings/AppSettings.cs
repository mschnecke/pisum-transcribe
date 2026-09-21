namespace Pisum.Transcribe.Settings;

/// <summary>
/// The user settings, saved as <c>settings.json</c> in the application data folder.
/// </summary>
/// <remarks>
/// Each feature adds its own section record. Follow these rules, so that partial and older files load correctly:
/// <list type="bullet">
/// <item><description>
/// <b>Sections:</b> add each section as an <c>init</c> property with a <c>new()</c> default, such as
/// <c>public ModelSettings Model { get; init; } = new();</c>. Do not add sections as constructor parameters of
/// <see cref="AppSettings"/>. A section parameter cannot have a non-null default, so an older file without that
/// section would load it as <see langword="null"/>.
/// </description></item>
/// <item><description>
/// <b>Section records:</b> these may use constructor parameters with default values, such as
/// <c>ModelSettings(string SelectedModelId = "canary-1b-v2-q8_0")</c>. System.Text.Json uses a parameter's default
/// value when the property is missing from the file.
/// </description></item>
/// <item><description>
/// <b>Non-constant defaults:</b> a setting whose default is not a compile-time constant, such as a list, is an
/// <c>init</c> property with an initializer, not a constructor parameter.
/// </description></item>
/// <item><description>
/// <b><see langword="null"/> in the file:</b> a file whose whole content is <c>null</c> loads as the defaults. A
/// section that the file sets to <c>null</c> must be replaced by its default after loading. Add that replacement to
/// <see cref="JsonSettingsStore"/>, together with its test, when you add the section.
/// </description></item>
/// <item><description>
/// <b>Names:</b> properties are written in camelCase and enum values as camelCase strings, such as
/// <c>clipboardPaste</c>.
/// </description></item>
/// </list>
/// </remarks>
internal sealed record AppSettings
{
    /// <summary>
    /// The version of the settings file format.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// The speech model settings.
    /// </summary>
    public ModelSettings Model { get; init; } = new();

    /// <summary>
    /// The transcription settings.
    /// </summary>
    public TranscriptionSettings Transcription { get; init; } = new();

    /// <summary>
    /// The recording settings.
    /// </summary>
    public RecordingSettings Recording { get; init; } = new();

    /// <summary>
    /// The text insertion settings.
    /// </summary>
    public TextInsertionSettings TextInsertion { get; init; } = new();

    /// <summary>
    /// The voice activity detection settings.
    /// </summary>
    public VoiceActivitySettings VoiceActivity { get; init; } = new();
}
