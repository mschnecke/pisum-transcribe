using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Settings;

/// <summary>
/// Stores the settings as JSON in <c>settings.json</c> under <see cref="AppPaths.Root"/>.
/// </summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        WriteIndented = true,
        Converters = {new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)},
    };

    private readonly string _settingsFile;
    private readonly ILogger<JsonSettingsStore> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="paths">The application data folders.</param>
    /// <param name="logger">The logger.</param>
    public JsonSettingsStore(AppPaths paths, ILogger<JsonSettingsStore> logger)
    {
        _settingsFile = Path.Combine(paths.Root, "settings.json");
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public AppSettings Current { get; private set; } = new();

    /// <inheritdoc />
    public void Load()
    {
        var settings = Read() ?? new AppSettings();

        // System.Text.Json sets a section to null when the file says so, despite the non-nullable type.
        Current = settings with
        {
            Model = settings.Model ?? new ModelSettings(),
            Transcription = settings.Transcription ?? new TranscriptionSettings(),
            Recording = settings.Recording ?? new RecordingSettings(),
            TextInsertion = settings.TextInsertion ?? new TextInsertionSettings(),
            VoiceActivity = settings.VoiceActivity ?? new VoiceActivitySettings(),
        };
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var tempFile = _settingsFile + ".tmp";
        await using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
            stream.Flush(true);
        }

        // Replaces the file in one step, so a crash leaves either the previous or the new complete file.
        File.Move(tempFile, _settingsFile, true);
        var previous = Current;
        Current = settings;
        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
    }

    private AppSettings? Read()
    {
        if (!File.Exists(_settingsFile))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_settingsFile);
            return JsonSerializer.Deserialize<AppSettings>(stream, SerializerOptions);
        }
        catch (JsonException exception)
        {
            var corruptFile = _settingsFile + ".corrupt";
            File.Move(_settingsFile, corruptFile, true);
            _logger.LogWarning(exception,
                "The settings file is corrupt. It was renamed to {CorruptFile}, and the defaults are used",
                corruptFile);
            return null;
        }
    }
}
