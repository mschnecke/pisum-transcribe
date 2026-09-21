namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// A speech model from the <see cref="ModelCatalog"/>: one GGUF file at a pinned download source.
/// </summary>
/// <remarks>
/// Every model translates from each non-English language in <see cref="Languages"/> into English and from English into
/// each of them.
/// </remarks>
/// <param name="Id">The identifier, as stored in <c>model.selectedModelId</c>.</param>
/// <param name="DisplayName">The name shown to the user.</param>
/// <param name="FileName">The file name in the models folder.</param>
/// <param name="DownloadUrl">The download URL at a fixed revision, so the file cannot drift from <paramref name="Sha256"/>.</param>
/// <param name="SizeBytes">The file size in bytes.</param>
/// <param name="Sha256">The SHA-256 hash of the file as lowercase hex.</param>
/// <param name="Languages">The supported source languages as ISO 639-1 codes.</param>
/// <param name="Creator">The creator of the original model.</param>
/// <param name="LicenseName">The name of the license.</param>
/// <param name="LicenseUrl">The link to the license text.</param>
/// <param name="Changes">The changes made to the original model.</param>
/// <param name="SourceRepository">The repository that publishes the file.</param>
internal sealed record SpeechModel(
    string Id,
    string DisplayName,
    string FileName,
    Uri DownloadUrl,
    long SizeBytes,
    string Sha256,
    IReadOnlyList<string> Languages,
    string Creator,
    string LicenseName,
    Uri LicenseUrl,
    string Changes,
    string SourceRepository);
