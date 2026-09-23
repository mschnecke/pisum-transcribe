using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Permissions;

namespace Pisum.Transcribe.Tests.Permissions;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class PermissionsViewModelTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly IPermissions _permissions = A.Fake<IPermissions>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<string> _openedUrls = [];
    private readonly Dictionary<Permission, PermissionState> _states = new()
    {
        [Permission.Accessibility] = PermissionState.NotDetermined,
        [Permission.Microphone] = PermissionState.NotDetermined,
        [Permission.PasteFromOtherApps] = PermissionState.Granted,
    };

    private PermissionState _notificationsState = PermissionState.Granted;

    public PermissionsViewModelTests()
    {
        A.CallTo(() => _permissions.GetState(A<Permission>._))
            .ReturnsLazily((Permission permission) => _states[permission]);
        A.CallTo(() => _permissions.GetNotificationsStateAsync()).ReturnsLazily(() => Task.FromResult(_notificationsState));
        A.CallTo(() => _permissions.IsAccessibilityGrantedAtStart).Returns(true);
    }

    [Theory]
    [InlineData(nameof(PermissionState.NotDetermined), "Not allowed yet", "Allow…")]
    [InlineData(nameof(PermissionState.Denied), "Denied", "Allow…")]
    [InlineData(nameof(PermissionState.Granted), "Allowed", null)]
    public void Constructor_MicrophoneState_ShowsTextAndButton(string stateName, string statusText, string? actionText)
    {
        // Arrange
        _states[Permission.Microphone] = Enum.Parse<PermissionState>(stateName);

        // Act
        var sut = CreateSut();

        // Assert
        sut.Microphone.StatusText.ShouldBe(statusText);
        sut.Microphone.ActionText.ShouldBe(actionText);
        sut.Microphone.HasAction.ShouldBe(actionText is not null);
        sut.Microphone.ActionCommand.CanExecute(null).ShouldBe(actionText is not null);
    }

    [Theory]
    [InlineData(nameof(PermissionState.NotDetermined), "Not allowed yet", "Allow…")]
    [InlineData(nameof(PermissionState.Granted), "Allowed", null)]
    public void Constructor_AccessibilityState_ShowsTextAndButton(string stateName, string statusText, string? actionText)
    {
        // Arrange
        _states[Permission.Accessibility] = Enum.Parse<PermissionState>(stateName);

        // Act
        var sut = CreateSut();

        // Assert
        sut.Accessibility.StatusText.ShouldBe(statusText);
        sut.Accessibility.ActionText.ShouldBe(actionText);
    }

    [Theory]
    [InlineData(nameof(PermissionState.NotDetermined), "Not allowed yet", null)]
    [InlineData(nameof(PermissionState.AsksEachTime), "Asks each time", "Open Settings…")]
    [InlineData(nameof(PermissionState.Denied), "Denied", "Open Settings…")]
    [InlineData(nameof(PermissionState.Granted), "Allowed", null)]
    public void Constructor_PasteState_ShowsTextAndButton(string stateName, string statusText, string? actionText)
    {
        // Arrange
        _states[Permission.PasteFromOtherApps] = Enum.Parse<PermissionState>(stateName);

        // Act
        var sut = CreateSut();

        // Assert
        sut.PasteFromOtherApps.StatusText.ShouldBe(statusText);
        sut.PasteFromOtherApps.ActionText.ShouldBe(actionText);
    }

    [Theory]
    [InlineData(nameof(PermissionState.NotDetermined), "Not allowed yet")]
    [InlineData(nameof(PermissionState.Denied), "Denied")]
    [InlineData(nameof(PermissionState.Granted), "Allowed")]
    public async Task RefreshAsync_NotificationsState_ShowsTextWithoutButton(string stateName, string statusText)
    {
        // Arrange
        _notificationsState = Enum.Parse<PermissionState>(stateName);
        var sut = CreateSut();

        // Act
        await sut.RefreshAsync();

        // Assert
        sut.Notifications.StatusText.ShouldBe(statusText);
        sut.Notifications.HasAction.ShouldBeFalse();
    }

    [Fact]
    public void Rows_Always_AccessibilityAndMicrophoneRequiredOthersOptional()
    {
        // Act
        var sut = CreateSut();

        // Assert
        sut.Rows.Select(row => row.Name).ShouldBe(["Accessibility", "Microphone", "Notifications", "Paste from other apps"]);
        sut.Rows.Select(row => row.RequiredText).ShouldBe(["Required", "Required", "Optional", "Optional"]);
    }

    [Fact]
    public void AreRequiredGranted_BothGrantedAtStart_IsTrue()
    {
        // Arrange
        _states[Permission.Accessibility] = PermissionState.Granted;
        _states[Permission.Microphone] = PermissionState.Granted;

        // Act
        var sut = CreateSut();

        // Assert
        sut.AreRequiredGranted.ShouldBeTrue();
    }

    [Fact]
    public void AreRequiredGranted_AccessibilityGrantedWhileRunning_IsFalseUntilRestart()
    {
        // Arrange
        A.CallTo(() => _permissions.IsAccessibilityGrantedAtStart).Returns(false);
        _states[Permission.Accessibility] = PermissionState.Granted;
        _states[Permission.Microphone] = PermissionState.Granted;

        // Act
        var sut = CreateSut();

        // Assert
        sut.Accessibility.State.ShouldBe(PermissionState.Granted);
        sut.AreRequiredGranted.ShouldBeFalse();
    }

    [Fact]
    public async Task MicrophoneAction_NotDetermined_AsksMacOSWithoutOpeningSettings()
    {
        // Arrange
        A.CallTo(() => _permissions.RequestMicrophoneAsync()).Returns(PermissionState.Granted);
        var sut = CreateSut();

        // Act
        await sut.Microphone.ActionCommand.ExecuteAsync(null);

        // Assert
        A.CallTo(() => _permissions.RequestMicrophoneAsync()).MustHaveHappenedOnceExactly();
        _openedUrls.ShouldBeEmpty();
        sut.Microphone.State.ShouldBe(PermissionState.Granted);
    }

    [Fact]
    public async Task MicrophoneAction_Denied_OpensMicrophoneSettingsWithoutAsking()
    {
        // Arrange
        _states[Permission.Microphone] = PermissionState.Denied;
        var sut = CreateSut();

        // Act
        await sut.Microphone.ActionCommand.ExecuteAsync(null);

        // Assert
        A.CallTo(() => _permissions.RequestMicrophoneAsync()).MustNotHaveHappened();
        _openedUrls.ShouldBe(["x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone"]);
    }

    [Fact]
    public async Task AccessibilityAction_FirstTime_ShowsPromptWithoutOpeningSettings()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Accessibility.ActionCommand.ExecuteAsync(null);

        // Assert
        A.CallTo(() => _permissions.PromptForAccessibility()).MustHaveHappenedOnceExactly();
        _openedUrls.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccessibilityAction_SecondTime_ShowsPromptAndOpensAccessibilitySettings()
    {
        // Arrange
        var sut = CreateSut();
        await sut.Accessibility.ActionCommand.ExecuteAsync(null);

        // Act
        await sut.Accessibility.ActionCommand.ExecuteAsync(null);

        // Assert
        A.CallTo(() => _permissions.PromptForAccessibility()).MustHaveHappenedTwiceExactly();
        _openedUrls.ShouldBe(["x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"]);
    }

    [Theory]
    [InlineData(nameof(PermissionState.AsksEachTime))]
    [InlineData(nameof(PermissionState.Denied))]
    public async Task PasteAction_AsksEachTimeOrDenied_OpensPasteSettings(string stateName)
    {
        // Arrange
        _states[Permission.PasteFromOtherApps] = Enum.Parse<PermissionState>(stateName);
        var sut = CreateSut();

        // Act
        await sut.PasteFromOtherApps.ActionCommand.ExecuteAsync(null);

        // Assert
        _openedUrls.ShouldBe(["x-apple.systempreferences:com.apple.preference.security?Privacy_Pasteboard"]);
    }

    [Fact]
    public async Task OpenAsync_PasteAtDefault_ProbesOnceOffTheCallingThread()
    {
        // Arrange
        _states[Permission.PasteFromOtherApps] = PermissionState.NotDetermined;
        var callingThread = Environment.CurrentManagedThreadId;
        var probeThreads = new List<int>();
        A.CallTo(() => _permissions.ProbePasteboard()).Invokes(() =>
        {
            probeThreads.Add(Environment.CurrentManagedThreadId);
            _states[Permission.PasteFromOtherApps] = PermissionState.Granted;
        });
        var sut = CreateSut();

        // Act
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        sut.Close();
        _states[Permission.PasteFromOtherApps] = PermissionState.NotDetermined;
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        probeThreads.Count.ShouldBe(1);
        probeThreads.Single().ShouldNotBe(callingThread);
        sut.PasteFromOtherApps.State.ShouldBe(PermissionState.NotDetermined);
    }

    [Theory]
    [InlineData(nameof(PermissionState.AsksEachTime))]
    [InlineData(nameof(PermissionState.Denied))]
    [InlineData(nameof(PermissionState.Granted))]
    public async Task OpenAsync_PasteNotAtDefault_DoesNotProbe(string stateName)
    {
        // Arrange
        _states[Permission.PasteFromOtherApps] = Enum.Parse<PermissionState>(stateName);
        var sut = CreateSut();

        // Act
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        A.CallTo(() => _permissions.ProbePasteboard()).MustNotHaveHappened();
    }

    [Fact]
    public async Task OpenAsync_NotificationsNotDetermined_RequestsOncePerProcess()
    {
        // Arrange
        _notificationsState = PermissionState.NotDetermined;
        var sut = CreateSut();

        // Act
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        sut.Close();
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        A.CallTo(() => _permissions.RequestNotificationsAsync()).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(nameof(PermissionState.Granted))]
    [InlineData(nameof(PermissionState.Denied))]
    public async Task OpenAsync_NotificationsAnswered_DoesNotRequest(string stateName)
    {
        // Arrange
        _notificationsState = Enum.Parse<PermissionState>(stateName);
        var sut = CreateSut();

        // Act
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        A.CallTo(() => _permissions.RequestNotificationsAsync()).MustNotHaveHappened();
    }

    [Fact]
    public async Task OpenAsync_StateChangesInSystemSettings_RowShowsItAfterOneSecond()
    {
        // Arrange
        var sut = CreateSut();
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _states[Permission.Microphone] = PermissionState.Granted;

        // Act
        _timeProvider.Advance(PermissionsViewModel.RefreshInterval - TimeSpan.FromTicks(1));
        var stateBefore = sut.Microphone.State;
        _timeProvider.Advance(TimeSpan.FromTicks(1));

        // Assert
        PermissionsViewModel.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(1));
        stateBefore.ShouldBe(PermissionState.NotDetermined);
        sut.Microphone.State.ShouldBe(PermissionState.Granted);
    }

    [Fact]
    public async Task Close_WindowClosed_StopsRefresh()
    {
        // Arrange
        var sut = CreateSut();
        await sut.OpenAsync().WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        sut.Close();
        _states[Permission.Microphone] = PermissionState.Granted;
        _timeProvider.Advance(TimeSpan.FromSeconds(5));

        // Assert
        sut.Microphone.State.ShouldBe(PermissionState.NotDetermined);
    }

    private PermissionsViewModel CreateSut()
    {
        return new PermissionsViewModel(_permissions, new InlineUiDispatcher(), _timeProvider, _openedUrls.Add);
    }
}
