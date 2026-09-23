using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacTrayIconSetTests
{
    [Fact]
    public Task For_EachStatus_ReturnsDistinctIconAndTemplateOnlyForReadyAndUnavailable()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new MacTrayIconSet();

            // Act
            var icons = Enum.GetValues<TrayStatus>().ToDictionary(status => status, sut.For);

            // Assert
            icons.Values.Select(icon => icon.Icon).Distinct().Count().ShouldBe(4);
            icons[TrayStatus.Ready].IsTemplate.ShouldBeTrue();
            icons[TrayStatus.Unavailable].IsTemplate.ShouldBeTrue();
            icons[TrayStatus.Recording].IsTemplate.ShouldBeFalse();
            icons[TrayStatus.Transcribing].IsTemplate.ShouldBeFalse();
        });
    }

    [Fact]
    public Task Initial_Always_IsTheReadyTemplate()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Act
            var sut = new MacTrayIconSet();

            // Assert
            sut.Initial.ShouldBeSameAs(sut.For(TrayStatus.Ready));
        });
    }
}
