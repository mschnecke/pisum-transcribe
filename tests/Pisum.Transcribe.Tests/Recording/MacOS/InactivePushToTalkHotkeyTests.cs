using Pisum.Transcribe.Recording;
using SharpHook.Data;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class InactivePushToTalkHotkeyTests
{
    [Fact]
    public void SetHotkeySuspendResume_Called_RaiseNoEvent()
    {
        // Arrange
        IPushToTalkHotkey sut = new InactivePushToTalkHotkey();
        var raised = new List<string>();
        sut.Pressed += (_, _) => raised.Add(nameof(sut.Pressed));
        sut.Released += (_, _) => raised.Add(nameof(sut.Released));
        sut.Cancelled += (_, _) => raised.Add(nameof(sut.Cancelled));
        sut.RawKey += (_, _) => raised.Add(nameof(sut.RawKey));

        // Act
        sut.SetHotkey(new HashSet<KeyCode> {KeyCode.VcRightMeta});
        sut.Suspend();
        sut.Resume();

        // Assert
        raised.ShouldBeEmpty();
    }
}
