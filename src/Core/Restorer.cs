using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// Puts back everything RestMode changed in-process, from a saved
/// <see cref="RestState"/>. Used by: normal wake, end actions, crash recovery
/// on next launch, log-off/shutdown, "Restore everything now" and uninstall.
/// Every step is independent: one failure never stops the others.
/// (Network adapters and Fan Control are restored by the elevated helper.)
/// </summary>
internal static class Restorer
{
    public static void RestoreLocal(RestState? st)
    {
        // Allow normal sleep/display timeouts again (no-op if this thread never set it).
        Try("execution state", () => SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS));

        if (st != null)
        {
            // Power plan first, so the CPU is at full speed for everything after.
            if (st.OriginalScheme != null || st.TempScheme != null)
                Try("power plan", () => PowerPlanManager.Restore(st.OriginalScheme, st.TempScheme));

            if (st.Processes.Count > 0)
                Try("apps", () => ProcessThrottler.Restore(st.Processes));

            if (st.Audio != null)
                Try("audio", () =>
                {
                    AudioController.Restore(st.Audio);
                    Logger.Info($"Audio restored (volume {st.Audio.Volume:P0}, muted={st.Audio.Muted}).");
                });
        }

        StateStore.Clear();
    }

    private static void Try(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.Error($"Restoring {what} failed (skipped)", ex); }
    }
}
