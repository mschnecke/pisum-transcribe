using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.Transcription;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TranscriptionOptionsValidatorTests
{
    private static readonly SpeechModel Canary1B = ModelCatalog.Resolve("canary-1b-v2-q8_0");
    private static readonly SpeechModel Canary180MFlash = ModelCatalog.Resolve("canary-180m-flash-q8_0");

    [Fact]
    public void Validate_TranslateGermanToEnglish_IsValid()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary1B,
            new TranscriptionOptions(TranscriptionTask.Translate, "de", "en"));

        // Assert
        error.ShouldBeNull();
    }

    [Fact]
    public void Validate_TranslateEnglishToGerman_IsValid()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary1B,
            new TranscriptionOptions(TranscriptionTask.Translate, "en", "de"));

        // Assert
        error.ShouldBeNull();
    }

    [Fact]
    public void Validate_TranslateGermanToFrench_IsRejected()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary1B,
            new TranscriptionOptions(TranscriptionTask.Translate, "de", "fr"));

        // Assert
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Validate_TranslateGermanToGerman_IsRejected()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary1B,
            new TranscriptionOptions(TranscriptionTask.Translate, "de", "de"));

        // Assert
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Validate_EnglishToEnglishTranslate_IsRejected()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary1B,
            new TranscriptionOptions(TranscriptionTask.Translate, "en", "en"));

        // Assert
        error.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Translate")]
    [InlineData("Transcribe")]
    public void Validate_PolishSourceOn180MFlash_IsRejected(string task)
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary180MFlash,
            new TranscriptionOptions(Enum.Parse<TranscriptionTask>(task), "pl", "en"));

        // Assert
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Validate_TranslateToTargetUnsupportedByModel_IsRejected()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary180MFlash,
            new TranscriptionOptions(TranscriptionTask.Translate, "en", "pl"));

        // Assert
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Validate_TranscribeWithUnsupportedTarget_IgnoresTarget()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary180MFlash,
            new TranscriptionOptions(TranscriptionTask.Transcribe, "de", "pl"));

        // Assert
        error.ShouldBeNull();
    }

    [Fact]
    public void Validate_TranscribeGermanWithEnglishTarget_IsValid()
    {
        // Act
        var error = TranscriptionOptionsValidator.Validate(Canary1B,
            new TranscriptionOptions(TranscriptionTask.Transcribe, "de", "en"));

        // Assert
        error.ShouldBeNull();
    }
}
