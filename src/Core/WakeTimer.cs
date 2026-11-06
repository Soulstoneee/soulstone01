using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// "Wake at": a waitable timer with resume=true, so Windows wakes the PC from
/// the end-action sleep at that time. Windows only honours it if wake timers
/// are allowed (Power Options > Sleep > Allow wake timers); the log says so.
/// </summary>
internal static class WakeTimer
{
    private static IntPtr _timer;

    public static void Arm(DateTime localTime)
    {
        Cancel();
        try
        {
            _timer = CreateWaitableTimerW(IntPtr.Zero, true, null);
            if (_timer == IntPtr.Zero) return;
            long due = localTime.ToUniversalTime().ToFileTimeUtc();
            if (SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, true))
                Logger.Info($"Wake timer set for {localTime:HH:mm} (needs \"Allow wake timers\" to be on in Power Options).");
            else
                Logger.Warn("Could not set the wake timer.");
        }
        catch (Exception ex) { Logger.Warn($"Wake timer unavailable: {ex.Message}"); }
    }

    public static void Cancel()
    {
        if (_timer == IntPtr.Zero) return;
        try { CancelWaitableTimer(_timer); CloseHandle(_timer); } catch { }
        _timer = IntPtr.Zero;
    }

    /// <summary>Next occurrence of "HH:mm" (today if still ahead, else tomorrow).</summary>
    public static DateTime? Next(string hhmm)
    {
        if (string.IsNullOrEmpty(hhmm) || !TimeSpan.TryParse(hhmm, out var t)) return null;
        var today = DateTime.Today + t;
        return today > DateTime.Now.AddSeconds(30) ? today : today.AddDays(1);
    }
}
