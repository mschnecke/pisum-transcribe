using System.Windows.Threading;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Waits for a task on the UI thread without blocking it.
/// </summary>
internal static class DispatcherWait
{
    /// <summary>
    /// Processes the current dispatcher's messages until <paramref name="task"/> has completed.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    public static void Until(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        var frame = new DispatcherFrame();

        // Not an await: its continuation would be queued behind the operations that the task's last step queued, such
        // as WPF's shutdown callback, and they would run inside this frame. A synchronous continuation ends the frame in
        // the operation that completes the task, so those operations run only after this method has returned.
        _ = task.ContinueWith(_ => frame.Continue = false, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
    }
}
