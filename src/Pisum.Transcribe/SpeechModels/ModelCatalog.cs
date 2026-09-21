namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The fixed catalog of supported speech models.
/// </summary>
internal static class ModelCatalog
{
    /// <summary>
    /// The identifier of the default model.
    /// </summary>
    public const string DefaultModelId = "canary-1b-v2-q8_0";

    private const string Canary1BRepository = "handy-computer/canary-1b-v2-gguf";
    private const string Canary1BRevision = "e2d8e6d7f2accc1259dc5497b517b4083047e44b";
    private const string Canary180MRepository = "handy-computer/canary-180m-flash-gguf";
    private const string Canary180MRevision = "456e6049062ecf06f1a0f4607f2ee3dc80ebbf8a";
    private const string Nvidia = "NVIDIA";
    private const string CcBy4 = "CC BY 4.0";

    private static readonly Uri CcBy4Url = new("https://creativecommons.org/licenses/by/4.0/");

    private static readonly string[] EuropeanLanguages =
    [
        "bg", "cs", "da", "de", "el", "en", "es", "et", "fi", "fr", "hr", "hu", "it", "lt", "lv", "mt", "nl", "pl",
        "pt",
        "ro", "ru", "sk", "sl", "sv", "uk",
    ];

    /// <summary>
    /// The catalog models, default first.
    /// </summary>
    public static IReadOnlyList<SpeechModel> Models { get; } =
    [
        new(
            DefaultModelId,
            "Canary 1B v2 (Q8_0)",
            "canary-1b-v2-Q8_0.gguf",
            DownloadUrl(Canary1BRepository, Canary1BRevision, "canary-1b-v2-Q8_0.gguf"),
            1_144_290_016,
            "224f83d1bc487b3303b495a7d6874912fdece93de19d1a04b550829c30a5d289",
            EuropeanLanguages,
            Nvidia,
            CcBy4,
            CcBy4Url,
            "Converted from nvidia/canary-1b-v2 to GGUF and quantized to Q8_0 by handy-computer",
            Canary1BRepository),
        new(
            "canary-1b-v2-q4_k_m",
            "Canary 1B v2 (Q4_K_M)",
            "canary-1b-v2-Q4_K_M.gguf",
            DownloadUrl(Canary1BRepository, Canary1BRevision, "canary-1b-v2-Q4_K_M.gguf"),
            735_476_448,
            "49e0a67e219bec95a254c2348460b6350e75a7ac6f93a131e48244b4c7cb53b9",
            EuropeanLanguages,
            Nvidia,
            CcBy4,
            CcBy4Url,
            "Converted from nvidia/canary-1b-v2 to GGUF and quantized to Q4_K_M by handy-computer",
            Canary1BRepository),
        new(
            "canary-180m-flash-q8_0",
            "Canary 180M Flash (Q8_0)",
            "canary-180m-flash-Q8_0.gguf",
            DownloadUrl(Canary180MRepository, Canary180MRevision, "canary-180m-flash-Q8_0.gguf"),
            218_447_552,
            "e13c7f5d0952b056a027cfffec13e3a3a134d1608babed24f983568f141e297c",
            ["de", "en", "es", "fr"],
            Nvidia,
            CcBy4,
            CcBy4Url,
            "Converted from nvidia/canary-180m-flash to GGUF and quantized to Q8_0 by handy-computer",
            Canary180MRepository),
    ];

    /// <summary>
    /// Finds a catalog model by identifier.
    /// </summary>
    /// <param name="id">The model identifier, such as <c>model.selectedModelId</c> from the settings file.</param>
    /// <returns>The model with <paramref name="id"/>, or the default model if the catalog has no such model.</returns>
    public static SpeechModel Resolve(string? id)
    {
        return Models.FirstOrDefault(model => model.Id == id) ?? Models[0];
    }

    private static Uri DownloadUrl(string repository, string revision, string fileName)
    {
        return new Uri($"https://huggingface.co/{repository}/resolve/{revision}/{fileName}");
    }
}
