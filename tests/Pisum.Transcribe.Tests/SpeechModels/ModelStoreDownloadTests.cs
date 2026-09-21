using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.SpeechModels;

/// <summary>
/// Downloads a real model from Hugging Face. Needs internet access and about 210 MB of free space.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class ModelStoreDownloadTests : IDisposable
{
    private readonly TempDirectory _root = new();

    public void Dispose()
    {
        _root.Dispose();
    }

    [Fact(Explicit = true)]
    public async Task InstallAsync_Canary180MFlash_InstallsVerifiedModel()
    {
        // Arrange
        var model = ModelCatalog.Resolve("canary-180m-flash-q8_0");
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpClientFactory.CreateClient(ModelStore.HttpClientName))
            .ReturnsLazily(() => new HttpClient {Timeout = Timeout.InfiniteTimeSpan});
        var sut = new ModelStore(new AppPaths(_root.Path), httpClientFactory, A.Fake<IHostApplicationLifetime>(),
            A.Fake<ISettingsStore>(), NullLogger<ModelStore>.Instance);
        var installedEvents = new List<SpeechModel>();
        sut.ModelInstalled += (_, installed) => installedEvents.Add(installed);

        // Act
        await sut.InstallAsync(model, new Progress<DownloadProgress>(), TestContext.Current.CancellationToken);

        // Assert
        sut.IsInstalled(model).ShouldBeTrue();
        installedEvents.ShouldBe([model]);
        File.Exists(sut.GetModelPath(model) + ".partial").ShouldBeFalse();
    }
}
