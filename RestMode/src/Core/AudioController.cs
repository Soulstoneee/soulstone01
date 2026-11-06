using System.Runtime.InteropServices;
using RestMode.Util;

namespace RestMode.Core;

/// <summary>
/// Mutes / restores the default playback device through the Windows Core Audio
/// API (IAudioEndpointVolume). The device id is saved so the SAME device is
/// restored even if the default device changes while resting.
/// </summary>
internal static class AudioController
{
    private const int eRender = 0;
    private const int eConsole = 0;
    private const int CLSCTX_ALL = 0x17;

    public static AudioSnapshot? Capture()
    {
        var device = GetDefaultDevice();
        if (device == null) return null;
        try
        {
            device.GetId(out string id);
            var vol = Activate(device);
            Marshal.ThrowExceptionForHR(vol.GetMasterVolumeLevelScalar(out float level));
            Marshal.ThrowExceptionForHR(vol.GetMute(out bool muted));
            Marshal.ReleaseComObject(vol);
            return new AudioSnapshot { DeviceId = id, Volume = level, Muted = muted };
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public static void Mute(string? deviceId) => Apply(deviceId, mute: true, volume: null);

    public static void Restore(AudioSnapshot snap) => Apply(snap.DeviceId, snap.Muted, snap.Volume);

    private static void Apply(string? deviceId, bool mute, float? volume)
    {
        IMMDevice? device = null;
        try
        {
            device = deviceId != null ? GetDevice(deviceId) : null;
            device ??= GetDefaultDevice();
            if (device == null) throw new InvalidOperationException("No playback device found.");
            var vol = Activate(device);
            Guid ctx = Guid.Empty;
            if (volume is float v)
                Marshal.ThrowExceptionForHR(vol.SetMasterVolumeLevelScalar(Math.Clamp(v, 0f, 1f), ref ctx));
            Marshal.ThrowExceptionForHR(vol.SetMute(mute, ref ctx));
            Marshal.ReleaseComObject(vol);
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    private static IMMDevice? GetDefaultDevice()
    {
        var en = CreateEnumerator();
        try
        {
            int hr = en.GetDefaultAudioEndpoint(eRender, eConsole, out IMMDevice dev);
            return hr == 0 ? dev : null;
        }
        finally { Marshal.ReleaseComObject(en); }
    }

    private static IMMDevice? GetDevice(string id)
    {
        var en = CreateEnumerator();
        try
        {
            int hr = en.GetDevice(id, out IMMDevice dev);
            return hr == 0 ? dev : null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Saved audio device not found ({ex.Message}); using the default device.");
            return null;
        }
        finally { Marshal.ReleaseComObject(en); }
    }

    private static IAudioEndpointVolume Activate(IMMDevice device)
    {
        Guid iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object o));
        return (IAudioEndpointVolume)o;
    }

    // ---- minimal Core Audio COM definitions (methods must stay in vtable order) ----

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
