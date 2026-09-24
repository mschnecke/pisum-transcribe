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

    [Theory]
    [InlineData("VcFunction", true)]
    [InlineData("vcFunction", true)]
    [InlineData("VcRightMeta", false)]
    public void ShowsFnHint_SavedHotkeyOnMacOS_IsTrueOnlyWithFn(string keyName, bool expected)
    {
        // Act
        var sut = CreateSut([keyName], HotkeyKeyNames.MacOS);

        // Assert
        sut.ShowsFnHint.ShouldBe(expected);
    }

    [Fact]
    public void ShowsFnHint_SavedHotkeyWithFnOnWindows_IsFalse()
    {
        // Act
        var sut = CreateSut(["VcFunction", "VcLeftControl"], HotkeyKeyNames.Windows);

        // Assert
        sut.ShowsFnHint.ShouldBeFalse();
    }

    [Fact]
    public void ShowsFnHint_FnCapturedOnMacOS_TurnsTrueWithNotification()
    {
        // Arrange
        var sut = CreateSut(["VcRightMeta"], HotkeyKeyNames.MacOS);
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        sut.ChangeHotkeyCommand.Execute(null);
        RaiseRawKey(KeyCode.VcFunction, true);
        RaiseRawKey(KeyCode.VcFunction, false);

        // Assert
        sut.HotkeyName.ShouldBe("fn");
        sut.ShowsFnHint.ShouldBeTrue();
        changed.ShouldContain(nameof(DictationSectionViewModel.ShowsFnHint));
    }

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
