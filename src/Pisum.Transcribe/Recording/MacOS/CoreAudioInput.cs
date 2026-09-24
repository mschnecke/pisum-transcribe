using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// <see cref="IAudioInput"/> on CoreAudio and AudioQueue.
/// </summary>
internal sealed unsafe class CoreAudioInput : IAudioInput
{
    /// <inheritdoc />
    public uint GetDefaultDevice()
    {
        return CoreAudio.GetDefaultInputDevice();
    }

    /// <inheritdoc />
    public bool IsMuted(uint device)
    {
        return CoreAudio.IsInputMuted(device);
    }

    /// <inheritdoc />
    public IDisposable WatchDefaultDevice(Action changed)
    {
        return new DefaultDeviceListener(changed);
    }

    /// <inheritdoc />
    public IAudioInputQueue OpenQueue(uint device, SamplesAvailableHandler samplesAvailable)
    {
        return AudioInputQueue.Open(device, samplesAvailable);
    }

    private sealed class DefaultDeviceListener : IDisposable
    {
        private readonly nint _listener = (nint) (delegate* unmanaged[Cdecl]<uint, uint, nint, nint, int>) &OnChanged;
        private GCHandle _handle;

        public DefaultDeviceListener(Action changed)
        {
            _handle = GCHandle.Alloc(changed);
            var status = CoreAudio.AddSystemListener(CoreAudio.DefaultInputDeviceAddress, _listener,
                GCHandle.ToIntPtr(_handle));
            if (status != 0)
            {
                _handle.Free();
                throw new IOException(
                    $"The change of the input device could not be followed (OSStatus {status}).");
            }
        }

        public void Dispose()
        {
            if (!_handle.IsAllocated)
            {
                return;
            }

            // Removed before the handle is freed, so the listener never sees a freed handle.
            CoreAudio.RemoveSystemListener(CoreAudio.DefaultInputDeviceAddress, _listener, GCHandle.ToIntPtr(_handle));
            _handle.Free();
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int OnChanged(uint objectId, uint addressCount, nint addresses, nint clientData)
        {
            try
            {
                ((Action) GCHandle.FromIntPtr(clientData).Target!)();
            }
            catch
            {
                // An exception must not unwind into CoreAudio.
            }

            return 0;
        }
    }
}
