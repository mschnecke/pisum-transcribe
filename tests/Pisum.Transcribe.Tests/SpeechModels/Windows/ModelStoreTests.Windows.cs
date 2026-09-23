namespace Pisum.Transcribe.Tests.SpeechModels;

// Only Windows refuses to delete a file that is open. macOS removes it, and the open handle keeps working.
public sealed partial class ModelStoreTests
{
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
}
