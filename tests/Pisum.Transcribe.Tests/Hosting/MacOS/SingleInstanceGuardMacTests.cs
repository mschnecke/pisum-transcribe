using System.Diagnostics;
using System.Runtime.InteropServices;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// The per-user scope on macOS, across processes. The other process is this test host, started again to run only
/// <see cref="Child_StartedByOtherTests_TriesOrHoldsGuard"/>, which the environment tells what to do and which writes
/// whether it acquired the guard to a file.
/// </summary>
/// <remarks>
/// Guards are acquired and disposed on the test's thread without an await in between, because mutex ownership belongs
/// to a thread.
/// </remarks>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed partial class SingleInstanceGuardMacTests : IDisposable
{
    private const string ModeVariable = "PISUM_TRANSCRIBE_TEST_GUARD_MODE";
    private const string NameVariable = "PISUM_TRANSCRIBE_TEST_GUARD_NAME";
    private const string OutcomeVariable = "PISUM_TRANSCRIBE_TEST_GUARD_OUTCOME";
    private const string Acquired = "acquired";
    private const string Refused = "refused";
    private const string TryMode = "try";
    private const string TryInNewSessionMode = "try-in-new-session";
    private const string HoldMode = "hold";

    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(30);

    private static readonly NamedWaitHandleOptions PerUser =
        new() {CurrentUserOnly = true, CurrentSessionOnly = false};

    private readonly string _mutexName = $"Pisum.Transcribe.Tests.{Guid.NewGuid():N}";
    private readonly TempDirectory _temp = new();

    public SingleInstanceGuardMacTests()
    {
        Directory.CreateDirectory(_temp.Path);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Theory]
    [InlineData(TryMode)]
    [InlineData(TryInNewSessionMode)]
    public void TryAcquire_HeldByThisProcess_RefusesOtherProcess(string mode)
    {
        // Arrange
        using var sut = new SingleInstanceGuard(_mutexName, PerUser);
        sut.TryAcquire(ShortWait).ShouldBeTrue();

        // Act
        using var child = StartChild(mode);
        var exited = child.WaitForExit(ChildTimeout);

        // Assert
        exited.ShouldBeTrue();
        ReadOutcome().ShouldBe(Refused, child.StandardOutput.ReadToEnd());
    }

    [Fact]
    public void TryAcquire_OwnerKilled_AcquiresAbandonedMutex()
    {
        // Arrange
        using var child = StartChild(HoldMode);
        var stopwatch = Stopwatch.StartNew();
        while (ReadOutcome() is null && !child.HasExited && stopwatch.Elapsed < ChildTimeout)
        {
            Thread.Sleep(50);
        }

        ReadOutcome().ShouldBe(Acquired);
        child.Kill();
        child.WaitForExit(ChildTimeout).ShouldBeTrue();
        using var sut = new SingleInstanceGuard(_mutexName, PerUser);

        // Act
        var acquired = sut.TryAcquire(ShortWait);

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact(Explicit = true)]
    public void Child_StartedByOtherTests_TriesOrHoldsGuard()
    {
        // Arrange
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        Assert.SkipWhen(mode is null, "Runs only in the process that the other tests of this class start.");
        if (mode == TryInNewSessionMode)
        {
            // As Finder and launchd start apps. The child isn't a process group leader, so setsid can't fail.
            setsid().ShouldBePositive();
            getsid(0).ShouldBe(Environment.ProcessId);
        }

        using var sut = new SingleInstanceGuard(Environment.GetEnvironmentVariable(NameVariable)!, PerUser);

        // Act
        var acquired = sut.TryAcquire(ShortWait);

        // Assert: the other test checks the outcome.
        File.WriteAllText(Environment.GetEnvironmentVariable(OutcomeVariable)!, acquired ? Acquired : Refused);
        if (mode == HoldMode && acquired)
        {
            // Until the other test kills this process.
            Thread.Sleep(ChildTimeout);
        }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int setsid();

    [LibraryImport("libc", SetLastError = true)]
    private static partial int getsid(int pid);

    private string OutcomeFile => Path.Combine(_temp.Path, "outcome");

    private string? ReadOutcome()
    {
        return File.Exists(OutcomeFile) ? File.ReadAllText(OutcomeFile) : null;
    }

    private Process StartChild(string mode)
    {
        // The test host's apphost, next to the test assembly.
        var startInfo = new ProcessStartInfo(Path.ChangeExtension(typeof(SingleInstanceGuardMacTests).Assembly.Location,
            null))
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        // Started directly, the test host takes xunit's own options.
        startInfo.ArgumentList.Add("-method");
        startInfo.ArgumentList.Add($"*.{nameof(SingleInstanceGuardMacTests)}.{nameof(Child_StartedByOtherTests_TriesOrHoldsGuard)}");
        startInfo.ArgumentList.Add("-explicit");
        startInfo.ArgumentList.Add("on");
        startInfo.ArgumentList.Add("-noColor");
        startInfo.Environment[ModeVariable] = mode;
        startInfo.Environment[NameVariable] = _mutexName;
        startInfo.Environment[OutcomeVariable] = OutcomeFile;

        return Process.Start(startInfo)!;
    }
}
