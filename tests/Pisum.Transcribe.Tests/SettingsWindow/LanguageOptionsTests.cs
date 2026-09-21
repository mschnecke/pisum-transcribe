using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class LanguageOptionsTests
{
    private static readonly SpeechModel LargeModel = ModelCatalog.Resolve("canary-1b-v2-q8_0");
    private static readonly SpeechModel SmallModel = ModelCatalog.Resolve("canary-180m-flash-q8_0");

    [Fact]
    public void For_TranslateFromGerman_OffersOnlyEnglishTarget()
    {
        // Act
        var options = LanguageOptions.For(LargeModel, TranscriptionTask.Translate, "de");

        // Assert
        options.Targets.ShouldBe([new LanguageOption("en", "English")]);
    }

    [Fact]
    public void For_TranslateFromEnglishOnLargeModel_OffersTheOther24Languages()
    {
        // Act
        var options = LanguageOptions.For(LargeModel, TranscriptionTask.Translate, "en");

        // Assert
        options.Targets.Count.ShouldBe(24);
        options.Targets.ShouldNotContain(option => option.Code == "en");
        options.Targets.Select(option => option.Name).ShouldBeInOrder();
    }

    [Fact]
    public void For_SmallModel_OffersEnglishFrenchGermanSpanishAsSources()
    {
        // Act
        var options = LanguageOptions.For(SmallModel, TranscriptionTask.Translate, "de");

        // Assert
        options.Sources.Select(option => option.Name).ShouldBe(["English", "French", "German", "Spanish"]);
        options.Sources.Select(option => option.Code).ShouldBe(["en", "fr", "de", "es"]);
    }

    [Fact]
    public void For_Transcribe_OffersNoTargets()
    {
        // Act
        var options = LanguageOptions.For(LargeModel, TranscriptionTask.Transcribe, "de");

        // Assert
        options.Targets.ShouldBeEmpty();
        options.Sources.Count.ShouldBe(25);
    }
}
