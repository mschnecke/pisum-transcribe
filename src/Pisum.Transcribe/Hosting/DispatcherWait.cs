using Avalonia.Threading;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Waits for a task on the UI thread without blocking it.
/// </summary>
internal static class DispatcherWait
{
    /// <summary>
    /// Processes the UI thread's messages until <paramref name="task"/> has completed. Call it on the UI thread.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    public static void Until(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        var frame = new DispatcherFrame();

        // Not an await: its continuation would be queued as an operation of its own, behind the ones that the task's last
        // step queued. A synchronous continuation ends the frame in the operation that completes the task. Avalonia still
        // runs the operations already queued before it leaves the frame, unlike WPF, so nothing may rely on running
        // after this method has returned. The lifetime's shutdown doesn't: it runs synchronously in the task's last step.
        _ = task.ContinueWith(_ => frame.Continue = false, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
    }
}
