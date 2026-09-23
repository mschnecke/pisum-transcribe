using System.Runtime.InteropServices;
using System.Text;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The C APIs of CoreFoundation and of the Accessibility API that Pisum Transcribe calls (designs D2 and D9 of
/// add-macos-setup): the free space for a download, the exclusion from backups, and the Accessibility grant.
/// </summary>
internal static class CoreFoundation
{
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string ApplicationServicesPath =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    // kCFNumberSInt64Type
    private const int NumberSInt64Type = 4;

    private static readonly Lazy<Constants> Exports = new(ReadConstants);

    /// <summary>
    /// The capacity the volume of a folder makes available for a download the user starts, which includes space the
    /// system can free, such as purgeable caches, as Finder shows it.
    /// </summary>
    /// <param name="folder">An existing folder on the volume.</param>
    /// <returns>The available bytes.</returns>
    /// <exception cref="IOException">The capacity could not be read.</exception>
    public static long GetAvailableCapacityForImportantUsage(string folder)
    {
        var url = CreateUrl(folder);
        try
        {
            if (!CFURLCopyResourcePropertyForKey(url, Exports.Value.VolumeAvailableCapacityForImportantUsageKey,
                    out var number, out var error) || number == 0)
            {
                Release(error);
                throw new IOException($"Could not read the available capacity of the volume of {folder}.");
            }

            try
            {
                return CFNumberGetValue(number, NumberSInt64Type, out var bytes)
                    ? bytes
                    : throw new IOException($"The available capacity of the volume of {folder} is not a number.");
            }
            finally
            {
                CFRelease(number);
            }
        }
        finally
        {
            CFRelease(url);
        }
    }

    /// <summary>
    /// Excludes a folder with its contents from Time Machine backups. Setting it again has no effect.
    /// </summary>
    /// <param name="folder">An existing folder.</param>
    /// <exception cref="IOException">The exclusion could not be set.</exception>
    public static void ExcludeFromBackup(string folder)
    {
        var url = CreateUrl(folder);
        try
        {
            if (!CFURLSetResourcePropertyForKey(url, Exports.Value.UrlIsExcludedFromBackupKey, Exports.Value.BooleanTrue,
                    out var error))
            {
                Release(error);
                throw new IOException($"Could not exclude {folder} from backups.");
            }
        }
        finally
        {
            CFRelease(url);
        }
    }

    /// <summary>
    /// Whether the process has the Accessibility grant. It turns true at once after the grant, but the keyboard hook
    /// sees the grant only in a new process.
    /// </summary>
    /// <returns><see langword="true"/> if granted.</returns>
    public static bool IsProcessTrusted()
    {
        return AXIsProcessTrusted();
    }

    /// <summary>
    /// Shows macOS's Accessibility prompt, which leads to System Settings, unless the process has the grant. macOS
    /// shows the prompt only the first time.
    /// </summary>
    /// <returns><see langword="true"/> if the process already has the grant.</returns>
    public static bool PromptForAccessibility()
    {
        var constants = Exports.Value;
        nint[] keys = [constants.TrustedCheckOptionPromptKey];
        nint[] values = [constants.BooleanTrue];
        var options = CFDictionaryCreate(0, keys, values, 1, constants.TypeDictionaryKeyCallBacks,
            constants.TypeDictionaryValueCallBacks);
        try
        {
            return AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            CFRelease(options);
        }
    }

    private static nint CreateUrl(string folder)
    {
        var path = Encoding.UTF8.GetBytes(folder);
        var url = CFURLCreateFromFileSystemRepresentation(0, path, path.Length, true);
        return url != 0 ? url : throw new IOException($"Could not create a URL for {folder}.");
    }

    private static void Release(nint reference)
    {
        if (reference != 0)
        {
            CFRelease(reference);
        }
    }

    private static Constants ReadConstants()
    {
        // The frameworks stay loaded for the process, as the DllImports below load them too.
        var coreFoundation = NativeLibrary.Load(CoreFoundationPath);
        var applicationServices = NativeLibrary.Load(ApplicationServicesPath);

        // The CFStringRef and CFBooleanRef constants are variables that hold the reference, while the dictionary
        // callbacks are structs whose address is passed.
        nint ReadReference(nint library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

        return new Constants(
            ReadReference(coreFoundation, "kCFURLVolumeAvailableCapacityForImportantUsageKey"),
            ReadReference(coreFoundation, "kCFURLIsExcludedFromBackupKey"),
            ReadReference(coreFoundation, "kCFBooleanTrue"),
            NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks"),
            ReadReference(applicationServices, "kAXTrustedCheckOptionPrompt"));
    }

    [DllImport(CoreFoundationPath)]
    private static extern nint CFURLCreateFromFileSystemRepresentation(nint allocator,
                                                                      byte[] buffer,
                                                                      nint bufferLength,
                                                                      [MarshalAs(UnmanagedType.U1)] bool isDirectory);

    [DllImport(CoreFoundationPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFURLCopyResourcePropertyForKey(nint url, nint key, out nint value, out nint error);

    [DllImport(CoreFoundationPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFURLSetResourcePropertyForKey(nint url, nint key, nint value, out nint error);

    [DllImport(CoreFoundationPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(nint number, nint type, out long value);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFDictionaryCreate(nint allocator,
                                                  nint[] keys,
                                                  nint[] values,
                                                  nint count,
                                                  nint keyCallBacks,
                                                  nint valueCallBacks);

    [DllImport(CoreFoundationPath)]
    private static extern void CFRelease(nint reference);

    [DllImport(ApplicationServicesPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServicesPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrustedWithOptions(nint options);

    private sealed record Constants(
        nint VolumeAvailableCapacityForImportantUsageKey,
        nint UrlIsExcludedFromBackupKey,
        nint BooleanTrue,
        nint TypeDictionaryKeyCallBacks,
        nint TypeDictionaryValueCallBacks,
        nint TrustedCheckOptionPromptKey);
}
