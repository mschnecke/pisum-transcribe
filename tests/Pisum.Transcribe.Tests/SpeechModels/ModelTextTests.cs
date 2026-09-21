using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.SpeechModels;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ModelTextTests
{
    [Theory]
    [InlineData(1_144_290_016, "1.07 GB")]
    [InlineData(735_476_448, "701 MB")]
    [InlineData(218_447_552, "208 MB")]
    [InlineData(12_900_000, "12.3 MB")]
    [InlineData(1_048_051_712, "0.98 GB")]
    public void FormatSize_Bytes_UsesBinaryUnitsWithThreeDigits(long bytes, string expected)
    {
        // Act
        var text = ModelText.FormatSize(bytes);

        // Assert
        text.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 1_144_290_016, "0.00 of 1.07 GB")]
    [InlineData(536_870_912, 1_144_290_016, "0.50 of 1.07 GB")]
    [InlineData(104_857_600, 218_447_552, "100 of 208 MB")]
    [InlineData(218_447_552, 218_447_552, "208 of 208 MB")]
    public void FormatProgress_BytesReceived_UsesUnitAndPrecisionOfTotal(long bytesReceived, long totalBytes,
                                                                         string expected)
    {
        // Act
        var text = ModelText.FormatProgress(bytesReceived, totalBytes);

        // Assert
        text.ShouldBe(expected);
    }

    [Fact]
    public void FormatLanguages_Codes_AreEnglishNamesSorted()
    {
        // Act
        var text = ModelText.FormatLanguages(["fr", "de", "en", "es"]);

        // Assert
        text.ShouldBe("English, French, German, Spanish");
    }
}
