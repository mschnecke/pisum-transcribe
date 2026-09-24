using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AudioQueueCaptureSessionFactoryTests
{
    private const uint Device = 42;

    private readonly IAudioInput _input = A.Fake<IAudioInput>();
    private int _microphoneStatus = 3;

    public AudioQueueCaptureSessionFactoryTests()
    {
        A.CallTo(() => _input.GetDefaultDevice()).Returns(Device);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CreateAsync_MicrophoneNotAllowed_ThrowsAccessDeniedWithoutOpening(int status)
    {
        // Arrange
        _microphoneStatus = status;
        var sut = CreateSut();

        // Act
        var create = sut.CreateAsync(Ct);

        // Assert
        await Should.ThrowAsync<MicrophoneAccessDeniedException>(create);
        A.CallTo(() => _input.GetDefaultDevice()).MustNotHaveHappened();
        A.CallTo(() => _input.OpenQueue(A<uint>._, A<SamplesAvailableHandler>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateAsync_NoInputDevice_ThrowsNoMicrophoneWithoutOpening()
    {
        // Arrange
        A.CallTo(() => _input.GetDefaultDevice()).Returns(CoreAudio.UnknownObject);
        var sut = CreateSut();

        // Act
        var create = sut.CreateAsync(Ct);

        // Assert
        await Should.ThrowAsync<NoMicrophoneException>(create);
        A.CallTo(() => _input.OpenQueue(A<uint>._, A<SamplesAvailableHandler>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateAsync_MutedDevice_ThrowsMutedWithoutOpening()
    {
        // Arrange
        A.CallTo(() => _input.IsMuted(Device)).Returns(true);
        var sut = CreateSut();

        // Act
        var create = sut.CreateAsync(Ct);

        // Assert
        await Should.ThrowAsync<MicrophoneMutedException>(create);
        A.CallTo(() => _input.OpenQueue(A<uint>._, A<SamplesAvailableHandler>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateAsync_Allowed_OpensAQueueOnTheDefaultDeviceWithoutStartingIt()
    {
        // Arrange
        var queue = A.Fake<IAudioInputQueue>();
        A.CallTo(() => _input.OpenQueue(Device, A<SamplesAvailableHandler>._)).Returns(queue);
        var sut = CreateSut();

        // Act
        await using var session = await sut.CreateAsync(Ct);

        // Assert
        session.ShouldBeOfType<AudioQueueCaptureSession>();
        A.CallTo(() => _input.OpenQueue(Device, A<SamplesAvailableHandler>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => queue.Start()).MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateAsync_Always_ReadsThePermissionThroughTheUiDispatcher()
    {
        // Arrange
        var uiDispatcher = A.Fake<IUiDispatcher>();
        var readOutside = 0;
        var readInside = 0;
        var insideDispatcher = false;
        A.CallTo(() => uiDispatcher.InvokeAsync(A<Action>._)).ReturnsLazily((Action action) =>
        {
            insideDispatcher = true;
            action();
            insideDispatcher = false;
            return Task.CompletedTask;
        });
        var sut = new AudioQueueCaptureSessionFactory(_input, uiDispatcher, () =>
        {
            _ = insideDispatcher ? readInside++ : readOutside++;
            return 3;
        });

        // Act
        await using var session = await sut.CreateAsync(Ct);

        // Assert
        readInside.ShouldBe(1);
        readOutside.ShouldBe(0);
    }

    private AudioQueueCaptureSessionFactory CreateSut()
    {
        return new AudioQueueCaptureSessionFactory(_input, new InlineUiDispatcher(), () => _microphoneStatus);
    }
}
