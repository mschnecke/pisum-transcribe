using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacHookAccessTests
{
    private readonly IPermissions _permissions = A.Fake<IPermissions>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsAllowed_AccessibilityInEffect_FollowsIt(bool inEffect)
    {
        // Arrange
        A.CallTo(() => _permissions.IsAccessibilityInEffect).Returns(inEffect);
        var sut = new MacHookAccess(_permissions, new InlineUiDispatcher());

        // Act
        var isAllowed = sut.IsAllowed;

        // Assert
        isAllowed.ShouldBe(inEffect);
    }

    [Fact]
    public void OnRevoked_Inline_ReportsTheRevoke()
    {
        // Arrange
        var sut = new MacHookAccess(_permissions, new InlineUiDispatcher());

        // Act
        sut.OnRevoked();

        // Assert
        A.CallTo(() => _permissions.OnAccessibilityRevoked()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void OnRevoked_Called_ReportsTheRevokeOnlyThroughTheUiDispatcher()
    {
        // Arrange
        var uiDispatcher = A.Fake<IUiDispatcher>();
        Action? queued = null;
        A.CallTo(() => uiDispatcher.InvokeAsync(A<Action>._)).Invokes((Action action) => queued = action)
            .Returns(Task.CompletedTask);
        var sut = new MacHookAccess(_permissions, uiDispatcher);

        // Act
        sut.OnRevoked();

        // Assert
        A.CallTo(() => _permissions.OnAccessibilityRevoked()).MustNotHaveHappened();
        queued.ShouldNotBeNull().Invoke();
        A.CallTo(() => _permissions.OnAccessibilityRevoked()).MustHaveHappenedOnceExactly();
    }
}
