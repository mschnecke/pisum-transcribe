namespace Pisum.Transcribe.Tests;

/// <summary>
/// A unique path under the temp folder that is deleted on dispose. The folder itself is not created.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Pisum.Transcribe.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}
