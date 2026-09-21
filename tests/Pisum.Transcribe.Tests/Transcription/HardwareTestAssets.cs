using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using TranscribeCppSharp;

namespace Pisum.Transcribe.Tests.Transcription;

/// <summary>
/// The models and audio clips that the hardware tests need. A test skips when one is missing.
/// </summary>
internal static class HardwareTestAssets
{
    /// <summary>
    /// The environment variable with the path of a WAV file of about 10 s of German speech, 16 kHz mono.
    /// </summary>
    public const string GermanAudioVariable = "PISUM_TRANSCRIBE_TEST_AUDIO";

    /// <summary>
    /// The environment variable with the path of a WAV file of about 10 s of English speech, 16 kHz mono.
    /// </summary>
    public const string EnglishAudioVariable = "PISUM_TRANSCRIBE_TEST_AUDIO_EN";

    /// <summary>
    /// The model store over the application's real models folder.
    /// </summary>
    public static ModelStore ModelStore { get; } = new(new AppPaths(), A.Fake<IHttpClientFactory>(),
        A.Fake<IHostApplicationLifetime>(), A.Fake<ISettingsStore>(), NullLogger<ModelStore>.Instance);

    /// <summary>
    /// Gets the installed catalog models, default first. Skips the test if none is installed.
    /// </summary>
    public static IReadOnlyList<SpeechModel> InstalledModelsOrSkip()
    {
        var models = ModelCatalog.Models.Where(ModelStore.IsInstalled).ToList();
        Assert.SkipWhen(models.Count == 0,
            $"No catalog model is installed in {new AppPaths().ModelsDirectory}. Start the app to download one.");
        return models;
    }

    /// <summary>
    /// Reads the audio clip named by an environment variable. Skips the test if the variable is not set or the file is
    /// missing.
    /// </summary>
    public static float[] ReadAudioOrSkip(string variable)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        Assert.SkipWhen(string.IsNullOrEmpty(path) || !File.Exists(path),
            $"Set {variable} to a 16 kHz mono WAV file of about 10 s of speech.");
        return PcmExtensions.ReadWavToPcm(path!);
    }
}
