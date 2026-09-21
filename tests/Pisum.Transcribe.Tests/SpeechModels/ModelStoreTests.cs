using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.SpeechModels;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ModelStoreTests : IDisposable
{
    private static readonly TimeSpan PromptStopTimeout = TimeSpan.FromSeconds(2);

    private readonly TempDirectory _root = new();
    private readonly AppPaths _paths;
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly IHttpClientFactory _httpClientFactory = A.Fake<IHttpClientFactory>();
    private readonly IHostApplicationLifetime _lifetime = A.Fake<IHostApplicationLifetime>();
    private readonly CancellationTokenSource _applicationStopping = new();
    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly IProgress<DownloadProgress> _progress = A.Fake<IProgress<DownloadProgress>>();
    private readonly List<SpeechModel> _installedEvents = [];
    private readonly ModelStore _sut;

    public ModelStoreTests()
    {
        _paths = new AppPaths(_root.Path);
        A.CallTo(() => _httpClientFactory.CreateClient(ModelStore.HttpClientName))
            .ReturnsLazily(() => new HttpClient(_handler, false) {Timeout = Timeout.InfiniteTimeSpan});
        A.CallTo(() => _lifetime.ApplicationStopping).Returns(_applicationStopping.Token);
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings());
        _sut = new ModelStore(_paths, _httpClientFactory, _lifetime, _settingsStore, NullLogger<ModelStore>.Instance,
            _ => long.MaxValue);
        _sut.ModelInstalled += (_, model) => _installedEvents.Add(model);
    }

    public void Dispose()
    {
        _applicationStopping.Dispose();
        _root.Dispose();
    }

    [Fact]
    public void GetModelPath_Model_IsFileNameInModelsDirectory()
    {
        // Arrange
        var model = ModelCatalog.Resolve("canary-180m-flash-q8_0");

        // Act
        var path = _sut.GetModelPath(model);

        // Assert
        path.ShouldBe(Path.Combine(_paths.ModelsDirectory, "canary-180m-flash-Q8_0.gguf"));
    }

    [Fact]
    public void IsInstalled_FileMissing_ReturnsFalse()
    {
        // Arrange
        var model = CreateModel([1, 2, 3]);

        // Act
        var installed = _sut.IsInstalled(model);

        // Assert
        installed.ShouldBeFalse();
    }

    [Fact]
    public void IsInstalled_SizeDiffers_ReturnsFalse()
    {
        // Arrange
        var model = CreateModel([1, 2, 3]);
        WriteModelsFile(model.FileName, [1, 2]);

        // Act
        var installed = _sut.IsInstalled(model);

        // Assert
        installed.ShouldBeFalse();
    }

    [Fact]
    public void IsInstalled_SizeMatches_ReturnsTrueWithoutCheckingHash()
    {
        // Arrange
        var model = CreateModel([1, 2, 3]);
        WriteModelsFile(model.FileName, [9, 9, 9]);

        // Act
        var installed = _sut.IsInstalled(model);

        // Assert
        installed.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_PartialFiles_DeletesOnlyPartialFiles()
    {
        // Arrange
        WriteModelsFile("model-a.gguf.partial", [1]);
        WriteModelsFile("model-b.gguf.partial", [1]);
        WriteModelsFile("model-c.gguf", [1]);

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        ModelsDirectoryFiles().ShouldBe(["model-c.gguf"]);
    }

    [Fact]
    public async Task StartAsync_ModelsDirectoryMissing_Completes()
    {
        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Directory.Exists(_paths.ModelsDirectory).ShouldBeFalse();
    }

    [Fact]
    public async Task InstallAsync_MatchingDownload_InstallsModelAndRaisesEvent()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content);
        RespondWith(new ByteArrayContent(content));

        // Act
        await _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        _sut.IsInstalled(model).ShouldBeTrue();
        (await File.ReadAllBytesAsync(_sut.GetModelPath(model), TestContext.Current.CancellationToken))
            .ShouldBe(content);
        ModelsDirectoryFiles().ShouldBe([model.FileName]);
        _installedEvents.ShouldBe([model]);
        _handler.Requests.Single().RequestUri.ShouldBe(model.DownloadUrl);
        A.CallTo(() => _progress.Report(A<DownloadProgress>._)).MustHaveHappened();
        A.CallTo(() => _progress.Report(A<DownloadProgress>.That.Matches(p => p.TotalBytes != model.SizeBytes)))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task InstallAsync_HashDiffers_ThrowsAndDeletesFile()
    {
        // Arrange
        var model = CreateModel(RandomContent(200_000));
        RespondWith(new ByteArrayContent(RandomContent(200_000)));

        // Act
        var install = _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<ModelIntegrityException>(install);
        _sut.IsInstalled(model).ShouldBeFalse();
        ModelsDirectoryFiles().ShouldBeEmpty();
        _installedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_ContentLengthDiffersFromCatalogSize_ThrowsBeforeWritingFile()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content);
        var body = new ResponseBodyStream(content);
        var responseContent = new StreamContent(body);
        responseContent.Headers.ContentLength = content.Length + 1;
        RespondWith(responseContent);

        // Act
        var install = _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<ModelIntegrityException>(install);
        body.BytesRead.ShouldBe(0);
        ModelsDirectoryFiles().ShouldBeEmpty();
        _installedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_MoreBytesThanCatalogSize_ThrowsAndDeletesFile()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content[..100_000]);
        var body = new ResponseBodyStream(content);
        RespondWith(new StreamContent(body));

        // Act
        var install = _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<ModelIntegrityException>(install);
        body.BytesRead.ShouldBeLessThan(content.Length);
        ModelsDirectoryFiles().ShouldBeEmpty();
        _installedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_ConnectionFailsMidStream_ThrowsAndDeletesFile()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content);
        RespondWith(new StreamContent(new ResponseBodyStream(content[..100_000], ResponseBodyStream.End.ConnectionReset)));

        // Act
        var install = _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<IOException>(install);
        ModelsDirectoryFiles().ShouldBeEmpty();
        _installedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_CallerCancels_StopsPromptlyAndDeletesFile()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content);
        var body = new ResponseBodyStream(content[..100_000], ResponseBodyStream.End.Stall);
        RespondWith(new StreamContent(body));
        using var cancellation = new CancellationTokenSource();
        var install = _sut.InstallAsync(model, _progress, cancellation.Token);
        await body.AllDataRead.Task.WaitAsync(PromptStopTimeout, TestContext.Current.CancellationToken);
        var partialFileExisted = File.Exists(_sut.GetModelPath(model) + ".partial");

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(install.WaitAsync(PromptStopTimeout,
            TestContext.Current.CancellationToken));
        partialFileExisted.ShouldBeTrue();
        ModelsDirectoryFiles().ShouldBeEmpty();
        _installedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_ApplicationStopping_CancelsAndDeletesFile()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content);
        var body = new ResponseBodyStream(content[..100_000], ResponseBodyStream.End.Stall);
        RespondWith(new StreamContent(body));
        var install = _sut.InstallAsync(model, _progress, CancellationToken.None);
        await body.AllDataRead.Task.WaitAsync(PromptStopTimeout, TestContext.Current.CancellationToken);

        // Act
        await _applicationStopping.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(install.WaitAsync(PromptStopTimeout,
            TestContext.Current.CancellationToken));
        ModelsDirectoryFiles().ShouldBeEmpty();
        _installedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_NotEnoughDiskSpace_ThrowsWithoutSendingRequest()
    {
        // Arrange
        var content = RandomContent(1000);
        var model = CreateModel(content);
        var requiredBytes = content.Length + 100L * 1024 * 1024;
        var sut = new ModelStore(_paths, _httpClientFactory, _lifetime, _settingsStore, NullLogger<ModelStore>.Instance,
            _ => requiredBytes - 1);
        RespondWith(new ByteArrayContent(content));

        // Act
        var install = sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<InsufficientDiskSpaceException>(install);
        exception.RequiredBytes.ShouldBe(requiredBytes);
        exception.AvailableBytes.ShouldBe(requiredBytes - 1);
        _handler.Requests.ShouldBeEmpty();
        sut.IsInstalled(model).ShouldBeFalse();
    }

    [Fact]
    public async Task InstallAsync_SameModelAlreadyDownloading_RejectsAndFirstDownloadInstalls()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RespondWith(new GatedContent(content, gate.Task));
        var first = _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Act
        var second = _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Should.ThrowAsync<ModelDownloadInProgressException>(second);
        exception.ModelId.ShouldBe(model.Id);
        _handler.Requests.Count.ShouldBe(1);
        gate.SetResult();
        await first.WaitAsync(PromptStopTimeout, TestContext.Current.CancellationToken);
        _sut.IsInstalled(model).ShouldBeTrue();
        _installedEvents.ShouldBe([model]);
    }

    [Fact]
    public async Task InstallAsync_AfterFailedDownload_CanDownloadAgain()
    {
        // Arrange
        var content = RandomContent(200_000);
        var model = CreateModel(content);
        RespondWith(new ByteArrayContent(RandomContent(200_000)));
        await Should.ThrowAsync<ModelIntegrityException>(
            _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken));
        RespondWith(new ByteArrayContent(content));

        // Act
        await _sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert
        _sut.IsInstalled(model).ShouldBeTrue();
    }

    [Fact]
    public void Delete_InstalledModelNotSelected_RemovesFile()
    {
        // Arrange
        var content = RandomContent(1000);
        var model = CreateModel(content);
        WriteModelsFile(model.FileName, content);

        // Act
        _sut.Delete(model);

        // Assert
        _sut.IsInstalled(model).ShouldBeFalse();
        ModelsDirectoryFiles().ShouldBeEmpty();
    }

    [Fact]
    public void Delete_SelectedModel_ThrowsAndKeepsFile()
    {
        // Arrange
        var model = ModelCatalog.Resolve("canary-180m-flash-q8_0");
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings {Model = new ModelSettings(model.Id)});
        WriteModelsFile(model.FileName, [1, 2, 3]);

        // Act
        var delete = () => _sut.Delete(model);

        // Assert
        delete.ShouldThrow<InvalidOperationException>();
        ModelsDirectoryFiles().ShouldBe([model.FileName]);
    }

    [Fact]
    public void Delete_FileInUse_ThrowsAndStaysInstalled()
    {
        // Arrange
        var content = RandomContent(1000);
        var model = CreateModel(content);
        WriteModelsFile(model.FileName, content);
        using var openFile = new FileStream(_sut.GetModelPath(model), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        var delete = () => _sut.Delete(model);

        // Assert
        delete.ShouldThrow<IOException>();
        _sut.IsInstalled(model).ShouldBeTrue();
    }

    private static byte[] RandomContent(int length)
    {
        return RandomNumberGenerator.GetBytes(length);
    }

    private static SpeechModel CreateModel(byte[] content)
    {
        var catalogModel = ModelCatalog.Resolve(null);
        return catalogModel with
        {
            Id = "test-model",
            FileName = "test-model.gguf",
            SizeBytes = content.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
        };
    }

    private void WriteModelsFile(string fileName, byte[] content)
    {
        Directory.CreateDirectory(_paths.ModelsDirectory);
        File.WriteAllBytes(Path.Combine(_paths.ModelsDirectory, fileName), content);
    }

    private void RespondWith(HttpContent content)
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) {Content = content};
    }

    private List<string?> ModelsDirectoryFiles()
    {
        return Directory.Exists(_paths.ModelsDirectory)
            ? Directory.GetFiles(_paths.ModelsDirectory).Select(Path.GetFileName).ToList()
            : [];
    }

    /// <summary>
    /// A response body that is delivered only after <c>gate</c> completes.
    /// </summary>
    private sealed class GatedContent(byte[] data, Task gate) : HttpContent
    {
        protected override async Task<Stream> CreateContentReadStreamAsync()
        {
            await gate;
            return new MemoryStream(data, false);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await gate;
            await stream.WriteAsync(data);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = data.Length;
            return true;
        }
    }
}
