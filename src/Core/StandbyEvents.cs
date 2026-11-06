using System.Diagnostics.Eventing.Reader;
using RestMode.Util;

namespace RestMode.Core;

/// <summary>
/// Reads Kernel-Power events 506 ("entering Modern Standby"), 507 ("exiting")
/// and 172 (network connectivity in standby) from the System log, so each
/// session's log shows what Windows did and why. The System log is readable
/// by standard users.
///
/// Note: on Modern Standby PCs Windows also logs 506 "Reason: Idle Timeout"
/// when the screen merely times out. While RestMode holds its "system
/// required" request the PC keeps running in that state, so these events
/// alone don't prove anything was paused. The controller decides that from
/// gaps in its own 5-second timer.
/// </summary>
internal static class StandbyEvents
{
    public static List<string> Since(DateTime sinceUtc)
    {
        var list = new List<string>();
        try
        {
            string ts = sinceUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            // 506/507 = entering/exiting Modern Standby, 172 = network connectivity state in standby.
            string xpath = "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=506 or EventID=507 or EventID=172) " +
                           $"and TimeCreated[@SystemTime>='{ts}']]]";
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
            for (EventRecord? e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
            {
                using (e)
                {
                    string msg;
                    try { msg = (e.FormatDescription() ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }
                    catch { msg = ""; }
                    if (msg.Length == 0) msg = e.Id switch { 506 => "entering Modern Standby", 507 => "exiting Modern Standby", _ => "standby connectivity changed" };
                    while (msg.Contains("  ")) msg = msg.Replace("  ", " ");
                    list.Add($"{e.TimeCreated:HH:mm:ss} {msg}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"Could not read standby events: {ex.Message}");
        }
        return list;
    }
}
