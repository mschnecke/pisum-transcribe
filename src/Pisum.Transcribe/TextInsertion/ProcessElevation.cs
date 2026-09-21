using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Reads whether a process runs elevated from its access token.
/// </summary>
internal static class ProcessElevation
{
    /// <summary>
    /// Reads the <c>TokenElevation</c> of a process.
    /// </summary>
    /// <param name="processId">The process identifier.</param>
    /// <param name="error">The Win32 error code when the token could not be read, otherwise 0.</param>
    /// <returns>
    /// Whether the process runs elevated, or <see langword="null"/> if the process or its token could not be opened, for
    /// example with access denied for a process of higher integrity.
    /// </returns>
    public static bool? IsElevated(uint processId, out int error)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false, processId);
        if (process.IsInvalid)
        {
            error = Marshal.GetLastPInvokeError();
            return null;
        }

        var opened = PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token);
        error = opened ? 0 : Marshal.GetLastPInvokeError();
        using (token)
        {
            if (!opened)
            {
                return null;
            }

            Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<TOKEN_ELEVATION>()];
            if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, buffer, out _))
            {
                error = Marshal.GetLastPInvokeError();
                return null;
            }

            return MemoryMarshal.Read<TOKEN_ELEVATION>(buffer).TokenIsElevated != 0;
        }
    }
}
