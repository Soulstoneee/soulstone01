using System.Diagnostics;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// "Watch chosen apps": measures the I/O of the listed programs (e.g. steam,
/// qbittorrent) so Rest Mode can end when THEIR downloads finish, ignoring
/// other background traffic. Uses each process's I/O counters (reads, writes,
/// and network/other transfers); works without admin for your own programs.
/// </summary>
internal sealed class AppWatcher
{
    private readonly Dictionary<int, ulong> _last = new();
    private DateTime _lastTime;

    public bool AnyRunning { get; private set; }
    public double KBps { get; private set; }

    public void Reset()
    {
        _last.Clear();
        _lastTime = DateTime.MinValue;
        KBps = 0;
    }

    public void Sample(IReadOnlyList<string> names)
    {
        var now = DateTime.UtcNow;
        ulong delta = 0;
        var seen = new HashSet<int>();
        AnyRunning = false;
        foreach (var raw in names)
        {
            string name = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    AnyRunning = true;
                    IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        if (!GetProcessIoCounters(h, out var io)) continue;
                        ulong total = io.ReadTransferCount + io.WriteTransferCount + io.OtherTransferCount;
                        if (_last.TryGetValue(p.Id, out ulong prev) && total >= prev) delta += total - prev;
                        _last[p.Id] = total;
                        seen.Add(p.Id);
                    }
                    finally { CloseHandle(h); }
                }
            }
        }
        foreach (var gone in _last.Keys.Where(k => !seen.Contains(k)).ToList()) _last.Remove(gone);

        if (_lastTime != DateTime.MinValue)
        {
            double secs = Math.Max(0.5, (now - _lastTime).TotalSeconds);
            // Light smoothing so one quiet sample doesn't count as "finished".
            double kbps = delta / secs / 1024.0;
            KBps = KBps * 0.5 + kbps * 0.5;
        }
        _lastTime = now;
    }
}
