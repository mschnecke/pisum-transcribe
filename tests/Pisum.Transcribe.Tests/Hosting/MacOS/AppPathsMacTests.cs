using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AppPathsMacTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Constructor_Default_RootInApplicationSupportAndLogsInLibraryLogs()
    {
        // Act
        var sut = new AppPaths();

        // Assert
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        sut.Root.ShouldBe(Path.Combine(home, "Library", "Application Support", "Pisum Transcribe"));
        sut.LogsDirectory.ShouldBe(Path.Combine(home, "Library", "Logs", "Pisum Transcribe"));
        sut.ModelsDirectory.ShouldBe(Path.Combine(sut.Root, "models"));
    }

    [Fact]
    public void EnsureRootExists_LogsOutsideRoot_CreatesBoth()
    {
        // Arrange
        var root = Path.Combine(_temp.Path, "Application Support", "Pisum Transcribe");
        var logs = Path.Combine(_temp.Path, "Logs", "Pisum Transcribe");
        var sut = new AppPaths(root, logs);

        // Act
        sut.EnsureRootExists();

        // Assert
        Directory.Exists(root).ShouldBeTrue();
        Directory.Exists(logs).ShouldBeTrue();
    }
}
