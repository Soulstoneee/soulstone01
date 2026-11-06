using System.Net.NetworkInformation;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// Measures network and disk throughput while resting, to guess when
/// downloads / updates / transfers are finished.
///
/// * Network: byte counters of all physical, connected adapters
///   (loopback, tunnels and virtual switch adapters are skipped so traffic is
///   not counted twice).
/// * Disk: the "\PhysicalDisk(_Total)\Disk Bytes/sec" performance counter,
///   added by its English name so it works on any Windows language.
///
/// Values are averaged over a 30-second window so one short burst (or one
/// short pause) does not reset or trigger the idle timer.
/// </summary>
internal sealed class ActivityMonitor : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, (long rx, long tx)> _lastCounters = new();
    private readonly Queue<(DateTime time, double seconds, long netBytes, double diskBytesPerSec)> _samples = new();
    private DateTime _lastSampleTime;
    private IntPtr _pdhQuery, _pdhCounter;
    private bool _diskAvailable;

    public long TotalReceived { get; private set; }
    public long TotalSent { get; private set; }
    public double NetKBps { get; private set; }
    public double DiskKBps { get; private set; }
    public bool DiskAvailable => _diskAvailable;

    public void Start()
    {
        TotalReceived = TotalSent = 0;
        _samples.Clear();
        _lastCounters.Clear();
        ReadNetwork(out _, out _); // prime counters
        _lastSampleTime = DateTime.UtcNow;
        OpenDiskCounter();
    }

    /// <summary>Takes one sample. Call every few seconds.</summary>
    public void Sample()
    {
        var now = DateTime.UtcNow;
        double seconds = Math.Max(0.5, (now - _lastSampleTime).TotalSeconds);
        _lastSampleTime = now;

        ReadNetwork(out long rx, out long tx);
        TotalReceived += rx;
        TotalSent += tx;
        double disk = ReadDisk();

        _samples.Enqueue((now, seconds, rx + tx, disk));
        while (_samples.Count > 1 && now - _samples.Peek().time > Window) _samples.Dequeue();

        // Time-weighted averages over the window.
        double totalSeconds = _samples.Sum(x => x.seconds);
        NetKBps = _samples.Sum(x => x.netBytes) / totalSeconds / 1024.0;
        DiskKBps = _samples.Sum(x => x.diskBytesPerSec * x.seconds) / totalSeconds / 1024.0;
    }

    private void ReadNetwork(out long rxDelta, out long txDelta)
    {
        rxDelta = txDelta = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                string desc = nic.Description ?? "";
                if (desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    nic.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)) continue;

                var stats = nic.GetIPStatistics();
                long rx = stats.BytesReceived, tx = stats.BytesSent;
                if (_lastCounters.TryGetValue(nic.Id, out var last))
                {
                    // Counters can reset when an adapter reconnects; never count negative traffic.
                    rxDelta += Math.Max(0, rx - last.rx);
                    txDelta += Math.Max(0, tx - last.tx);
                }
                _lastCounters[nic.Id] = (rx, tx);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Reading network counters failed: {ex.Message}");
        }
    }

    private void OpenDiskCounter()
    {
        CloseDiskCounter();
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _pdhQuery) == 0 &&
                PdhAddEnglishCounterW(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Bytes/sec", IntPtr.Zero, out _pdhCounter) == 0)
            {
                PdhCollectQueryData(_pdhQuery); // rate counters need a first sample
                _diskAvailable = true;
            }
            else
            {
                Logger.Warn("Disk performance counter unavailable; only network traffic will be watched.");
                CloseDiskCounter();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Disk performance counter unavailable ({ex.Message}); only network traffic will be watched.");
            _diskAvailable = false;
        }
    }

    private double ReadDisk()
    {
        if (!_diskAvailable) return 0;
        try
        {
            if (PdhCollectQueryData(_pdhQuery) != 0) return 0;
            if (PdhGetFormattedCounterValue(_pdhCounter, PDH_FMT_DOUBLE, out _, out var v) != 0) return 0;
            return v.CStatus == 0 ? Math.Max(0, v.doubleValue) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void CloseDiskCounter()
    {
        if (_pdhQuery != IntPtr.Zero)
        {
            try { PdhCloseQuery(_pdhQuery); } catch { /* ignore */ }
        }
        _pdhQuery = _pdhCounter = IntPtr.Zero;
        _diskAvailable = false;
    }

    public void Stop() => CloseDiskCounter();

    public void Dispose() => CloseDiskCounter();

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}
