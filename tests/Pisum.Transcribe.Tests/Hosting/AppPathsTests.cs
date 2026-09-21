using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AppPathsTests : IDisposable
{
    private readonly TempDirectory _root = new();

    public void Dispose()
    {
        _root.Dispose();
    }

    [Fact]
    public void Constructor_Default_RootIsPisumTranscribeUnderLocalAppData()
    {
        // Act
        var sut = new AppPaths();

        // Assert
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        sut.Root.ShouldBe(Path.Combine(localAppData, "Pisum Transcribe"));
    }

    [Fact]
    public void Paths_InjectedRoot_ResolveUnderRoot()
    {
        // Act
        var sut = new AppPaths(_root.Path);

        // Assert
        sut.Root.ShouldBe(_root.Path);
        sut.LogsDirectory.ShouldBe(Path.Combine(_root.Path, "logs"));
        sut.ModelsDirectory.ShouldBe(Path.Combine(_root.Path, "models"));
    }

    [Fact]
    public void EnsureRootExists_MissingRoot_CreatesRoot()
    {
        // Arrange
        var sut = new AppPaths(_root.Path);

        // Act
        sut.EnsureRootExists();

        // Assert
        Directory.Exists(_root.Path).ShouldBeTrue();
    }
}
