using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using SharpHook.Data;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class DictationSectionViewModelTests
{
    private readonly IPushToTalkHotkey _hotkey = A.Fake<IPushToTalkHotkey>();

    [Fact]
    public void ChangeHotkey_LetterOnMacOS_ShowsMacRejectedMessage()
    {
        // Arrange
        var sut = CreateSut(["VcRightMeta"], HotkeyKeyNames.MacOS);

        // Act
        sut.ChangeHotkeyCommand.Execute(null);
        RaiseRawKey(KeyCode.VcA, true);
        RaiseRawKey(KeyCode.VcA, false);

        // Assert
        sut.HotkeyError.ShouldBe(HotkeyKeyNames.MacOS.RejectedMessage);
        sut.HotkeyName.ShouldBe("Right Command");
    }

    private DictationSectionViewModel CreateSut(IReadOnlyList<string> hotkey, HotkeyKeyNames keyNames)
    {
        var settings = new AppSettings {Recording = new RecordingSettings {Hotkey = hotkey}};
        return new DictationSectionViewModel(settings, ModelCatalog.Resolve(null), _hotkey, new InlineUiDispatcher(),
            keyNames);
    }

    private void RaiseRawKey(KeyCode key, bool isPressed)
    {
        _hotkey.RawKey += Raise.With(new RawKeyEventArgs(key, isPressed));
    }
}
