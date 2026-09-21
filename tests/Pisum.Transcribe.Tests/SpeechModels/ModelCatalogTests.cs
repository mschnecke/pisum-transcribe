using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.SpeechModels;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ModelCatalogTests
{
    [Fact]
    public void Models_Catalog_HasThreeModelsWithUniqueIds()
    {
        // Act
        var ids = ModelCatalog.Models.Select(model => model.Id).ToList();

        // Assert
        ids.ShouldBe(["canary-1b-v2-q8_0", "canary-1b-v2-q4_k_m", "canary-180m-flash-q8_0"]);
        ids.ShouldBeUnique();
    }

    [Fact]
    public void Models_Catalog_HaveAttribution()
    {
        // Act & Assert
        ModelCatalog.Models.ShouldAllBe(model =>
            !string.IsNullOrWhiteSpace(model.Creator) &&
            !string.IsNullOrWhiteSpace(model.LicenseName) &&
            model.LicenseUrl.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(model.Changes) &&
            !string.IsNullOrWhiteSpace(model.SourceRepository));
    }

    [Fact]
    public void Models_Catalog_DownloadFromPinnedHttpsSource()
    {
        // Act & Assert
        ModelCatalog.Models.ShouldAllBe(model =>
            model.DownloadUrl.Scheme == Uri.UriSchemeHttps &&
            model.DownloadUrl.AbsolutePath.StartsWith("/" + model.SourceRepository + "/resolve/") &&
            model.DownloadUrl.AbsolutePath.EndsWith("/" + model.FileName) &&
            model.Sha256.Length == 64);
    }

    [Fact]
    public void Models_Canary180MFlash_SupportsGermanEnglishSpanishFrench()
    {
        // Act
        var model = ModelCatalog.Resolve("canary-180m-flash-q8_0");

        // Assert
        model.Languages.ShouldBe(["de", "en", "es", "fr"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("canary-1b-v2-q8_0", "canary-1b-v2-q8_0")]
    [InlineData("canary-1b-v2-q4_k_m", "canary-1b-v2-q4_k_m")]
    [InlineData("unknown-model", "canary-1b-v2-q8_0")]
    [InlineData("", "canary-1b-v2-q8_0")]
    [InlineData(null, "canary-1b-v2-q8_0")]
    public void Resolve_Id_ReturnsModelOrDefault(string? id, string expectedId)
    {
        // Act
        var model = ModelCatalog.Resolve(id);

        // Assert
        model.Id.ShouldBe(expectedId);
    }
}
