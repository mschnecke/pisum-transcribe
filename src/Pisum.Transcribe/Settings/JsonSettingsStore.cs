using System.Text.Json;
using System.Text.Json.Nodes;
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
            Updates = settings.Updates ?? new UpdateSettings(),
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
            var root = JsonNode.Parse(stream);
            if (Migrate(root, out var fromVersion))
            {
                _logger.LogInformation("The settings file of format {FromVersion} is read as format {ToVersion}",
                    fromVersion, AppSettings.CurrentSchemaVersion);
            }

            return root.Deserialize<AppSettings>(SerializerOptions);
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

    // Brings a file of an older format to the current one before it is read, so a renamed value doesn't make it count
    // as corrupt. Only the content in memory changes; the next save writes the file in the current format. A version
    // that isn't a number is left alone, so the file still counts as corrupt.
    private static bool Migrate(JsonNode? root, out int fromVersion)
    {
        fromVersion = 1;
        if (root is not JsonObject settings)
        {
            return false;
        }

        if (settings.TryGetPropertyValue("schemaVersion", out var version)
            && !(version is JsonValue value && value.TryGetValue(out fromVersion)))
        {
            return false;
        }

        if (fromVersion >= AppSettings.CurrentSchemaVersion)
        {
            return false;
        }

        // Format 2: the backend value "vulkan" became "gpu".
        if (settings["transcription"] is JsonObject transcription
            && transcription["backend"] is JsonValue backend
            && backend.TryGetValue(out string? backendValue)
            && backendValue == "vulkan")
        {
            transcription["backend"] = "gpu";
        }

        settings["schemaVersion"] = AppSettings.CurrentSchemaVersion;
        return true;
    }
}
