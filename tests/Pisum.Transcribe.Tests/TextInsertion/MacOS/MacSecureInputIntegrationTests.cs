using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class MacSecureInputIntegrationTests
{
    [Fact]
    public void IsEnabled_TestHostWithoutPasswordField_ReturnsFalse()
    {
        // Arrange
        var sut = new MacSecureInput();

        // Act
        var isEnabled = sut.IsEnabled;

        // Assert
        isEnabled.ShouldBeFalse();
    }
}
