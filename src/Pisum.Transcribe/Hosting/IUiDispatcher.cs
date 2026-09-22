namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Runs code on the UI thread. Services that touch the UI or the tray icon reach the UI thread through it.
/// </summary>
internal interface IUiDispatcher
{
    /// <summary>
    /// Queues an action on the UI thread. It may be called from any thread.
    /// </summary>
    /// <remarks>
    /// The action is queued, also when the caller is on the UI thread, so it runs after the caller's current work.
    /// An exception thrown by the action faults the returned task and never reaches the dispatcher's
    /// unhandled-exception handler, which would end the application. An implementation must keep both rules.
    /// </remarks>
    /// <param name="action">The action to run on the UI thread.</param>
    /// <returns>A task that completes when the action has run, and faults if it threw.</returns>
    Task InvokeAsync(Action action);
}
