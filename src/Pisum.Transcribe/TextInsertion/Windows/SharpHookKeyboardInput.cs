using Windows.Win32;
using Microsoft.Extensions.Logging;
using SharpHook.Data;
using SharpHook.Simulation;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Keystrokes through SharpHook's <see cref="EventSimulator"/>, which uses <c>SendInput</c>, and modifier keys through
/// <c>GetAsyncKeyState</c>.
/// </summary>
/// <remarks>
/// A result other than <see cref="UioHookResult.Success"/> is logged only. Input into elevated windows is excluded
/// before, and a silent partial failure cannot be detected reliably.
/// </remarks>
internal sealed class SharpHookKeyboardInput : IKeyboardInput, IDisposable
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    private readonly EventSimulator _simulator = EventSimulator.Create("Pisum Transcribe");
    private readonly ILogger<SharpHookKeyboardInput> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public SharpHookKeyboardInput(ILogger<SharpHookKeyboardInput> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void SendPaste()
    {
        // One sequence, posted in one call.
        using var sequence = _simulator.Sequence();
        var result = sequence
            .AddKeyPress(KeyCode.VcLeftControl)
            .AddKeyPress(KeyCode.VcV)
            .AddKeyRelease(KeyCode.VcV)
            .AddKeyRelease(KeyCode.VcLeftControl)
            .Simulate();
        LogFailure(result, "Ctrl+V");
    }

    /// <inheritdoc />
    public void TypeText(string text)
    {
        var lines = SplitLines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                LogFailure(_simulator.SimulateKeyStroke(KeyCode.VcEnter), "Enter");
            }

            if (lines[i].Length > 0)
            {
                LogFailure(_simulator.SimulateTextEntry(lines[i]), "text entry");
            }
        }
    }

    /// <inheritdoc />
    public bool AreModifiersDown(bool includePasteModifier)
    {
        return IsDown(VkShift) || IsDown(VkMenu) || IsDown(VkLeftWindows) || IsDown(VkRightWindows) ||
               (includePasteModifier && IsDown(VkControl));
    }

    /// <summary>
    /// Disposes the simulator.
    /// </summary>
    public void Dispose()
    {
        _simulator.Dispose();
    }

    /// <summary>
    /// Splits text into lines after normalizing all line endings to <c>\n</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The lines, without line endings. Text without a line break is one line.</returns>
    internal static IReadOnlyList<string> SplitLines(string text)
    {
        return text.ReplaceLineEndings("\n").Split('\n');
    }

    private static bool IsDown(int virtualKey)
    {
        return (PInvoke.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private void LogFailure(UioHookResult result, string input)
    {
        if (result != UioHookResult.Success)
        {
            _logger.LogWarning("Simulating {Input} returned {Result}", input, result);
        }
    }
}
