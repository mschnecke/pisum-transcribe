using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The current keyboard layout through Text Input Sources and <c>UCKeyTranslate</c>, and the distributed notification
/// of an input source change (design D4 of add-macos-text-insertion). Only on the UI thread.
/// </summary>
internal sealed unsafe class MacKeyboardLayout : IKeyboardLayout
{
    private const string CarbonPath = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreServicesPath = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string InputSourceChanged = "com.apple.Carbon.TISNotifySelectedKeyboardInputSourceChanged";

    // kCFStringEncodingUTF8
    private const uint StringEncodingUtf8 = 0x08000100;

    // kUCKeyActionDown
    private const ushort KeyActionDown = 0;

    // (cmdKey >> 8) & 0xFF, the Command modifier as UCKeyTranslate takes it.
    private const uint CommandModifierState = 1;

    // kUCKeyTranslateNoDeadKeysMask
    private const uint NoDeadKeys = 1;

    // kCFNotificationSuspensionBehaviorDeliverImmediately
    private const int DeliverImmediately = 4;

    // The virtual key codes 0 to 127 cover every key of a keyboard.
    private const ushort KeyCodeCount = 128;

    // A CFStringRef variable, so the export holds the reference. The framework stays loaded, as the DllImports load it.
    private static readonly Lazy<nint> UnicodeKeyLayoutDataKey = new(() => Marshal.ReadIntPtr(
        NativeLibrary.GetExport(NativeLibrary.Load(CarbonPath), "kTISPropertyUnicodeKeyLayoutData")));

    /// <inheritdoc />
    public ushort? FindPasteKeyCode()
    {
        var source = TISCopyCurrentKeyboardLayoutInputSource();
        if (source == 0)
        {
            return null;
        }

        try
        {
            // Not retained: owned by the input source. Absent for some input methods, which have no layout data.
            var layoutData = TISGetInputSourceProperty(source, UnicodeKeyLayoutDataKey.Value);
            if (layoutData == 0)
            {
                return null;
            }

            var layout = CFDataGetBytePtr(layoutData);
            var keyboardType = (uint) LMGetKbdType();
            for (ushort keyCode = 0; keyCode < KeyCodeCount; keyCode++)
            {
                if (Translate(layout, keyCode, keyboardType) == "v")
                {
                    return keyCode;
                }
            }

            return null;
        }
        finally
        {
            CFRelease(source);
        }
    }

    /// <inheritdoc />
    public IDisposable ObserveChanges(Action changed)
    {
        return new Observer(changed);
    }

    private static string? Translate(nint layout, ushort keyCode, uint keyboardType)
    {
        var deadKeyState = 0u;
        var characters = stackalloc char[4];
        var status = UCKeyTranslate(layout, keyCode, KeyActionDown, CommandModifierState, keyboardType, NoDeadKeys,
            ref deadKeyState, 4, out var length, characters);
        return status == 0 && length > 0 ? new string(characters, 0, (int) length) : null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNotification(nint center, nint observer, nint name, nint obj, nint userInfo)
    {
        if (GCHandle.FromIntPtr(observer).Target is not Action changed)
        {
            return;
        }

        try
        {
            changed();
        }
        catch
        {
            // An exception must not unwind into CoreFoundation.
        }
    }

    [DllImport(CarbonPath)]
    private static extern nint TISCopyCurrentKeyboardLayoutInputSource();

    [DllImport(CarbonPath)]
    private static extern nint TISGetInputSourceProperty(nint source, nint key);

    [DllImport(CarbonPath)]
    private static extern byte LMGetKbdType();

    [DllImport(CoreServicesPath)]
    private static extern int UCKeyTranslate(nint keyLayout,
                                             ushort virtualKeyCode,
                                             ushort keyAction,
                                             uint modifierKeyState,
                                             uint keyboardType,
                                             uint keyTranslateOptions,
                                             ref uint deadKeyState,
                                             nuint maxStringLength,
                                             out nuint actualStringLength,
                                             char* unicodeString);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFDataGetBytePtr(nint data);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFStringCreateWithCString(nint allocator,
                                                         [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
                                                         uint encoding);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFNotificationCenterGetDistributedCenter();

    [DllImport(CoreFoundationPath)]
    private static extern void CFNotificationCenterAddObserver(nint center,
                                                               nint observer,
                                                               nint callback,
                                                               nint name,
                                                               nint obj,
                                                               int suspensionBehavior);

    [DllImport(CoreFoundationPath)]
    private static extern void CFNotificationCenterRemoveObserver(nint center, nint observer, nint name, nint obj);

    [DllImport(CoreFoundationPath)]
    private static extern void CFRelease(nint reference);

    /// <summary>
    /// One registration with the distributed notification center. The observer is a handle to the action, freed
    /// only after the observer is removed.
    /// </summary>
    private sealed class Observer : IDisposable
    {
        private readonly nint _center = CFNotificationCenterGetDistributedCenter();
        private readonly nint _name = CFStringCreateWithCString(0, InputSourceChanged, StringEncodingUtf8);
        private readonly GCHandle _handle;
        private bool _disposed;

        public Observer(Action changed)
        {
            _handle = GCHandle.Alloc(changed);
            var callback = (nint) (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>) &OnNotification;
            CFNotificationCenterAddObserver(_center, GCHandle.ToIntPtr(_handle), callback, _name, 0,
                DeliverImmediately);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CFNotificationCenterRemoveObserver(_center, GCHandle.ToIntPtr(_handle), _name, 0);
            _handle.Free();
            CFRelease(_name);
        }
    }
}
