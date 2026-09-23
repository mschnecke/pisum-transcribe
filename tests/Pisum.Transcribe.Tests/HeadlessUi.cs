using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Pisum.Transcribe.Tests;

[assembly: AvaloniaTestApplication(typeof(HeadlessUi))]

namespace Pisum.Transcribe.Tests;

/// <summary>
/// Runs test bodies on the UI thread of Avalonia's headless platform, for tests of windows, the tray and the dispatcher.
/// One session serves the whole test assembly.
/// </summary>
/// <remarks>
/// <c>Avalonia.Headless.XUnit</c> 12.1.1 fails test discovery on xunit.v3 4.x, so the tests are plain
/// <c>[Fact]</c>s that run their body through this helper instead of <c>[AvaloniaFact]</c>.
/// </remarks>
public static class HeadlessUi
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessUi).Assembly);

    /// <summary>
    /// Builds the headless application, with the Fluent theme as the app has. Found by the session through
    /// <see cref="AvaloniaTestApplicationAttribute"/>.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        // Skia and HarfBuzz instead of the headless drawing stubs, so text is measured with the system's fonts as in the
        // app, which the layout tests need.
        return AppBuilder.Configure<HeadlessApplication>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions {UseHeadlessDrawing = false});
    }

    /// <summary>
    /// Runs an action on the UI thread.
    /// </summary>
    /// <param name="action">The test body.</param>
    /// <returns>A task that completes when the action has run, and faults if it threw.</returns>
    public static Task RunAsync(Action action)
    {
        return Session.Dispatch(action, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs an asynchronous action on the UI thread. Its awaits resume on the UI thread.
    /// </summary>
    /// <param name="action">The test body.</param>
    /// <returns>A task that completes when the action's task has completed, and faults if it faulted.</returns>
    public static Task RunAsync(Func<Task> action)
    {
        return Session.Dispatch(async () =>
        {
            await action();
            return true;
        }, TestContext.Current.CancellationToken);
    }

    private sealed class HeadlessApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }
    }
}
