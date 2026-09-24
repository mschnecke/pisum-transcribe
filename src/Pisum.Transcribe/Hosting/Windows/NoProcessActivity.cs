namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Windows has no App Nap, so an activity does nothing there.
/// </summary>
internal sealed class NoProcessActivity : IProcessActivity
{
    /// <inheritdoc />
    public IDisposable Begin(string reason)
    {
        return NoActivity.Instance;
    }

    private sealed class NoActivity : IDisposable
    {
        public static readonly NoActivity Instance = new();

        public void Dispose()
        {
        }
    }
}
