using System.Windows.Forms;
namespace RestMode.Util;

/// <summary>
/// Only one RestMode runs per user session. A second launch (e.g. the
/// "Enter Rest Mode" desktop shortcut) just signals the running copy through
/// a named event and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\RestMode.SingleInstance";
    public const string EnterEvent = @"Local\RestMode.Enter";
    public const string RestoreEvent = @"Local\RestMode.Restore";
    public const string ShowEvent = @"Local\RestMode.Show";
    public const string ExitEvent = @"Local\RestMode.Exit";
    public const string SimulateEvent = @"Local\RestMode.Simulate";
    public const string WakeEvent = @"Local\RestMode.Wake";

    private readonly List<(EventWaitHandle ev, RegisteredWaitHandle reg)> _waits = new();

    public static bool Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            return ev.Set();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Runs <paramref name="onSignal"/> on <paramref name="uiThread"/> whenever the event is set.</summary>
    public void Listen(string name, Action onSignal, Control uiThread)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        var reg = ThreadPool.RegisterWaitForSingleObject(ev, (_, _) =>
        {
            try { if (!uiThread.IsDisposed) uiThread.BeginInvoke(onSignal); } catch { /* shutting down */ }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        _waits.Add((ev, reg));
    }

    public void Dispose()
    {
        foreach (var (ev, reg) in _waits)
        {
            reg.Unregister(null);
            ev.Dispose();
        }
        _waits.Clear();
    }
}
