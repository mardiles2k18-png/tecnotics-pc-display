using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Turing.Sensors;

/// <summary>
/// Valores que no vienen de LibreHardwareMonitor: velocidad de red (sumando todas las interfaces activas)
/// y volumen general de Windows. Se leen una vez por segundo desde el hilo del motor.
/// </summary>
public sealed class SystemCounters
{
    private long _lastSent = -1, _lastReceived = -1;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTime;

    public void AddTo(SensorSnapshot s)
    {
        AddNetwork(s);
        if (OperatingSystem.IsWindows() && Volume.Get() is float v)
            s.Values[SensorIds.Volume] = v;
    }

    private void AddNetwork(SensorSnapshot s)
    {
        long sent = 0, received = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var st = nic.GetIPStatistics();
                sent += st.BytesSent;
                received += st.BytesReceived;
            }
        }
        catch (NetworkInformationException)
        {
            return;
        }

        var now = _clock.Elapsed;
        double seconds = (now - _lastTime).TotalSeconds;
        if (_lastSent >= 0 && seconds > 0.2)
        {
            double up = Math.Max(0, sent - _lastSent) / seconds, down = Math.Max(0, received - _lastReceived) / seconds;
            s.Values[SensorIds.NetUp] = (float)(up / 1048576);
            s.Values[SensorIds.NetDown] = (float)(down / 1048576);
            s.Values[SensorIds.NetUpKb] = (float)(up / 1024);
            s.Values[SensorIds.NetDownKb] = (float)(down / 1024);
        }
        _lastSent = sent;
        _lastReceived = received;
        _lastTime = now;
    }

    /// <summary>Volumen general (0-100, 0 si está silenciado) con la API de audio de Windows.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static class Volume
    {
        public static float? Get()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                try
                {
                    if (enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device) != 0 || device is null)
                        return null;
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj) != 0)
                        return null;
                    var volume = (IAudioEndpointVolume)obj;
                    volume.GetMute(out bool muted);
                    volume.GetMasterVolumeLevelScalar(out float level);
                    Marshal.ReleaseComObject(volume);
                    Marshal.ReleaseComObject(device);
                    return muted ? 0 : level * 100;
                }
                finally
                {
                    Marshal.ReleaseComObject(enumerator);
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                return null;
            }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator;

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr notify);
            int UnregisterControlChangeNotify(IntPtr notify);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float levelDb, ref Guid context);
            int SetMasterVolumeLevelScalar(float level, ref Guid context);
            int GetMasterVolumeLevel(out float levelDb);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
            int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            int GetChannelVolumeLevel(uint channel, out float levelDb);
            int GetChannelVolumeLevelScalar(uint channel, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
