using Pisum.Transcribe.Permissions;

namespace Pisum.Transcribe.Tests.Permissions;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacPermissionsTests
{
    [Theory]
    [InlineData(0, nameof(PermissionState.NotDetermined))]
    [InlineData(1, nameof(PermissionState.Denied))]
    [InlineData(2, nameof(PermissionState.Denied))]
    [InlineData(3, nameof(PermissionState.Granted))]
    public void ToMicrophoneState_HelperStatus_MapsRestrictedToDenied(int status, string stateName)
    {
        // Act
        var state = MacPermissions.ToMicrophoneState(status);

        // Assert
        state.ShouldBe(Enum.Parse<PermissionState>(stateName));
    }

    [Theory]
    [InlineData(-1, nameof(PermissionState.Granted))]
    [InlineData(0, nameof(PermissionState.NotDetermined))]
    [InlineData(1, nameof(PermissionState.AsksEachTime))]
    [InlineData(2, nameof(PermissionState.Granted))]
    [InlineData(3, nameof(PermissionState.Denied))]
    public void ToPasteState_AccessBehavior_MapsToState(int behavior, string stateName)
    {
        // Act
        var state = MacPermissions.ToPasteState(behavior);

        // Assert
        state.ShouldBe(Enum.Parse<PermissionState>(stateName));
    }
}
