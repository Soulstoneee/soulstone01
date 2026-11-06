using RestMode.Util;

namespace RestMode.Core;

public sealed class SessionStat
{
    public DateTime StartUtc { get; set; }
    public double Minutes { get; set; }
    public long Downloaded { get; set; }
    public long Uploaded { get; set; }
    public double? AvgCpuC { get; set; }
    public double? AvgGpuC { get; set; }
    public double? PeakC { get; set; }
    public string End { get; set; } = "";
}

public sealed class StatsData
{
    public List<SessionStat> Sessions { get; set; } = new();
}

/// <summary>Stats page: time in Rest Mode, data downloaded, average temperatures (last 500 sessions).</summary>
internal static class StatsStore
{
    public static StatsData Load() => JsonFile.Read<StatsData>(AppPaths.StatsFile) ?? new StatsData();

    public static void Add(SessionStat s)
    {
        try
        {
            var d = Load();
            d.Sessions.Add(s);
            if (d.Sessions.Count > 500) d.Sessions.RemoveRange(0, d.Sessions.Count - 500);
            JsonFile.Write(AppPaths.StatsFile, d);
        }
        catch (Exception ex) { Logger.Warn($"Saving stats failed: {ex.Message}"); }
    }

    public static void Clear() => JsonFile.Delete(AppPaths.StatsFile);
}
