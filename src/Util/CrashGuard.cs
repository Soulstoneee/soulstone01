namespace RestMode.Util;

/// <summary>
/// Stops a crash / restart loop. A marker file exists while RestMode runs and
/// is removed on a normal exit. If RestMode finds the marker at start, the
/// last run ended unexpectedly. After <see cref="Limit"/> unexpected endings
/// within 24 hours RestMode puts the settings back and does NOT start
/// automatically any more (it shows a message instead); starting it by hand
/// resets the count.
/// </summary>
internal static class CrashGuard
{
    public const int Limit = 3;

    private sealed class History
    {
        public List<DateTime> UncleanExitsUtc { get; set; } = new();
    }

    private static string Marker => Path.Combine(AppPaths.DataDir, "running.marker");
    private static string HistoryFile => Path.Combine(AppPaths.DataDir, "unclean-exits.json");

    /// <summary>Call once at start. Returns the number of unexpected endings in the last 24 hours.</summary>
    public static int OnStart()
    {
        try
        {
            var h = JsonFile.Read<History>(HistoryFile) ?? new History();
            h.UncleanExitsUtc.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromHours(24));
            if (File.Exists(Marker))
            {
                h.UncleanExitsUtc.Add(DateTime.UtcNow);
                Logger.Warn($"RestMode did not close normally last time ({h.UncleanExitsUtc.Count} time(s) in the last 24 hours).");
            }
            JsonFile.Write(HistoryFile, h);
            File.WriteAllText(Marker, DateTime.UtcNow.ToString("o"));
            return h.UncleanExitsUtc.Count;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Crash guard unavailable: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Normal exit (tray Exit, restore / uninstall, sign-out / shutdown).</summary>
    public static void OnCleanExit()
    {
        try { if (File.Exists(Marker)) File.Delete(Marker); } catch { /* ignore */ }
    }

    /// <summary>The user started RestMode by hand after the limit: start counting again.</summary>
    public static void Reset()
    {
        try { if (File.Exists(HistoryFile)) File.Delete(HistoryFile); } catch { /* ignore */ }
    }
}
