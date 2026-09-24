using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.SpeechModels;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ModelSetupHostedServiceTests
{
    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();
    private readonly IHostApplicationLifetime _lifetime = A.Fake<IHostApplicationLifetime>();
    private readonly IPermissions _permissions = A.Fake<IPermissions>();
    private readonly Dictionary<Permission, PermissionState> _permissionStates = new()
    {
        [Permission.Accessibility] = PermissionState.Granted,
        [Permission.Microphone] = PermissionState.Granted,
        [Permission.PasteFromOtherApps] = PermissionState.Granted,
    };

    private readonly List<(string Header, Func<bool> IsVisible)> _menuItems = [];
    private bool _isModelInstalled;

    public ModelSetupHostedServiceTests()
    {
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings());
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._)).ReturnsLazily(() => _isModelInstalled);
        A.CallTo(() => _permissions.GetState(A<Permission>._))
            .ReturnsLazily((Permission permission) => _permissionStates[permission]);
        A.CallTo(() => _permissions.GetNotificationsStateAsync()).Returns(PermissionState.Granted);
        A.CallTo(() => _permissions.IsAccessibilityInEffect).Returns(true);
        A.CallTo(() => _trayIcon.AddMenuItem(A<string>._, A<Action>._, A<Func<bool>?>._))
            .Invokes((string header, Action _, Func<bool>? isVisible) => _menuItems.Add((header, isVisible!)));
    }

    [Fact]
    public Task StartAsync_MacOSModelInstalledAndMicrophoneMissing_OpensWindow()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            _isModelInstalled = true;
            _permissionStates[Permission.Microphone] = PermissionState.NotDetermined;
            var sut = CreateSut(CreatePermissions());

            // Act
            await sut.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            sut.IsOpen.ShouldBeTrue();
            sut.Window!.IsVisible.ShouldBeTrue();
            sut.Window.Close();
            sut.IsOpen.ShouldBeFalse();
        });
    }

    [Fact]
    public Task StartAsync_MacOSSetupComplete_DoesNotOpenWindow()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            _isModelInstalled = true;
            var sut = CreateSut(CreatePermissions());

            // Act
            await sut.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            sut.IsOpen.ShouldBeFalse();
        });
    }

    [Fact]
    public Task StartAsync_WithoutPermissionsModelMissing_OpensWindow()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var sut = CreateSut(null);

            // Act
            await sut.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            sut.IsOpen.ShouldBeTrue();
            sut.Window!.Close();
        });
    }

    [Fact]
    public Task StartAsync_WithoutPermissions_AddsOnlyDownloadModelFollowingTheModel()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange: Windows, and macOS without an app bundle.
            _isModelInstalled = true;
            _permissionStates[Permission.Microphone] = PermissionState.Denied;
            var sut = CreateSut(null);

            // Act
            await sut.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            var item = _menuItems.ShouldHaveSingleItem();
            item.Header.ShouldBe("Download model…");
            item.IsVisible().ShouldBeFalse();
            _isModelInstalled = false;
            item.IsVisible().ShouldBeTrue();
        });
    }

    [Fact]
    public Task StartAsync_WithPermissions_AddsOnlySetUpFollowingModelAndGrants()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange: macOS in an app bundle.
            _isModelInstalled = true;
            var sut = CreateSut(CreatePermissions());

            // Act
            await sut.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            var item = _menuItems.ShouldHaveSingleItem();
            item.Header.ShouldBe("Set up Pisum Transcribe…");
            var visibleWhenComplete = item.IsVisible();
            _permissionStates[Permission.Microphone] = PermissionState.Denied;
            var visibleWithMicrophoneRevoked = item.IsVisible();
            _permissionStates[Permission.Microphone] = PermissionState.Granted;
            _permissionStates[Permission.Accessibility] = PermissionState.NotDetermined;
            var visibleWithoutAccessibility = item.IsVisible();
            _permissionStates[Permission.Accessibility] = PermissionState.Granted;
            _isModelInstalled = false;
            var visibleWithoutModel = item.IsVisible();

            visibleWhenComplete.ShouldBeFalse();
            visibleWithMicrophoneRevoked.ShouldBeTrue();
            visibleWithoutAccessibility.ShouldBeTrue();
            visibleWithoutModel.ShouldBeTrue();
        });
    }

    private ModelSetupHostedService CreateSut(PermissionsViewModel? permissions)
    {
        return new ModelSetupHostedService(_modelStore, _settingsStore, _trayIcon, new InlineUiDispatcher(), _lifetime,
            permissions);
    }

    private PermissionsViewModel CreatePermissions()
    {
        return new PermissionsViewModel(_permissions, new InlineUiDispatcher(), new FakeTimeProvider(), _ => { });
    }
}
