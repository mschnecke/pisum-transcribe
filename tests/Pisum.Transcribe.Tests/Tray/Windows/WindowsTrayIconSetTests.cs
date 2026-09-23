using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class WindowsTrayIconSetTests
{
    [Fact]
    public Task IconFor_EachStatusAndMode_ReturnsMatchingIcon()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var icons = WindowsTrayIconSet.LoadIcons();

            // Act
            var iconsByStatusAndMode = Enum.GetValues<TrayStatus>()
                .SelectMany(_ => Enum.GetValues<TaskbarMode>(), (status, mode) => (status, mode))
                .ToDictionary(key => key, key => WindowsTrayIconSet.IconFor(key.status, key.mode, icons));

            // Assert
            iconsByStatusAndMode.Count.ShouldBe(8);
            iconsByStatusAndMode[(TrayStatus.Ready, TaskbarMode.Light)].ShouldBeSameAs(icons.ReadyLight);
            iconsByStatusAndMode[(TrayStatus.Ready, TaskbarMode.Dark)].ShouldBeSameAs(icons.ReadyDark);
            iconsByStatusAndMode[(TrayStatus.Unavailable, TaskbarMode.Light)].ShouldBeSameAs(icons.UnavailableLight);
            iconsByStatusAndMode[(TrayStatus.Unavailable, TaskbarMode.Dark)].ShouldBeSameAs(icons.UnavailableDark);
            iconsByStatusAndMode[(TrayStatus.Recording, TaskbarMode.Light)].ShouldBeSameAs(icons.Recording);
            iconsByStatusAndMode[(TrayStatus.Recording, TaskbarMode.Dark)].ShouldBeSameAs(icons.Recording);
            iconsByStatusAndMode[(TrayStatus.Transcribing, TaskbarMode.Light)].ShouldBeSameAs(icons.Transcribing);
            iconsByStatusAndMode[(TrayStatus.Transcribing, TaskbarMode.Dark)].ShouldBeSameAs(icons.Transcribing);
        });
    }

    [Fact]
    public Task For_TaskbarModeFlips_FollowsModeAndNeverTemplate()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var taskbarMode = A.Fake<ITaskbarModeWatcher>();
            A.CallTo(() => taskbarMode.Current).Returns(TaskbarMode.Light);
            var sut = new WindowsTrayIconSet(taskbarMode);

            // Act
            var light = sut.For(TrayStatus.Ready);
            A.CallTo(() => taskbarMode.Current).Returns(TaskbarMode.Dark);
            var dark = sut.For(TrayStatus.Ready);

            // Assert
            light.Icon.ShouldBeSameAs(sut.Icons.ReadyLight);
            dark.Icon.ShouldBeSameAs(sut.Icons.ReadyDark);
            new[] {light.IsTemplate, dark.IsTemplate, sut.Initial.IsTemplate}.ShouldAllBe(isTemplate => !isTemplate);
        });
    }

    [Fact]
    public Task Changed_TaskbarModeChanged_IsRaised()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var taskbarMode = A.Fake<ITaskbarModeWatcher>();
            var sut = new WindowsTrayIconSet(taskbarMode);
            var raised = 0;
            sut.Changed += (_, _) => raised++;

            // Act
            taskbarMode.Changed += Raise.WithEmpty();

            // Assert
            raised.ShouldBe(1);
        });
    }
}
