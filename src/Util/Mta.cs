namespace RestMode.Util;

/// <summary>
/// Runs WMI / COM work off the UI thread.
///
/// Why: a WMI call made on the UI (STA) thread keeps processing window messages
/// while it waits. One of those messages can start a second WMI call in the
/// middle of the first (e.g. a power notification during hardware detection),
/// and the two then deadlock - RestMode froze in exactly this way. Running the
/// work on a thread-pool (MTA) thread with a time limit and a safe fallback
/// makes that impossible.
/// </summary>
internal static class Mta
{
    public static T Run<T>(Func<T> work, int timeoutMs, T fallback)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return work();
        var task = Task.Run(work);
        try
        {
            if (task.Wait(timeoutMs)) return task.Result;
            Logger.Warn($"A system query took longer than {timeoutMs / 1000} s - skipped.");
            return fallback;
        }
        catch (Exception ex)
        {
            Logger.Info($"System query failed: {ex.GetBaseException().Message}");
            return fallback;
        }
    }
}
