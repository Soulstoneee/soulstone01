using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// "When I close the lid: Rest / Sleep / Do nothing".
///
/// Windows can't be given a new lid action, so for "Rest" (and "Do nothing")
/// RestMode sets the lid close action of the user's active plan to
/// "Do nothing" while RestMode runs, and reacts to the lid itself
/// (GUID_LIDSWITCH_STATE_CHANGE). The original values (plugged in and
/// battery, per plan) are saved to lid-backup.json BEFORE anything is
/// written, and put back exactly when the option is turned off, when RestMode
/// exits, on the next start after a crash, and on uninstall.
/// </summary>
internal static class LidManager
{
    private sealed class Entry
    {
        public Guid Scheme { get; set; }
        public uint Ac { get; set; }
        public uint Dc { get; set; }
    }

    private sealed class Backup
    {
        public List<Entry> Entries { get; set; } = new();
    }

    public static bool HasBackup => File.Exists(AppPaths.LidBackupFile);

    /// <summary>Makes the active plan's lid action match the setting. Safe to call often (watchdog / plan changes).</summary>
    public static void Apply(LidBehavior behavior, PowerCapabilities caps)
    {
        if (!caps.LidPresent) return;
        if (behavior == LidBehavior.Sleep) { RestoreAll(); return; }

        var active = PowerPlanManager.GetActiveScheme();
        if (active is not Guid scheme || PowerPlanManager.GetName(scheme) == PowerPlanManager.TempPlanName) return;

        var backup = JsonFile.Read<Backup>(AppPaths.LidBackupFile) ?? new Backup();
        if (backup.Entries.Any(e => e.Scheme == scheme))
        {
            // Already handled; just make sure nothing (e.g. a vendor app) changed it back.
            if (Read(scheme) is (0, 0)) return;
        }
        else
        {
            if (Read(scheme) is not (uint ac, uint dc)) { Logger.Warn("Lid: could not read the lid setting - left unchanged."); return; }
            backup.Entries.Add(new Entry { Scheme = scheme, Ac = ac, Dc = dc });
            JsonFile.Write(AppPaths.LidBackupFile, backup); // save BEFORE changing anything
            Logger.Info($"Lid: saved your lid close action (plugged in {Name(ac)}, battery {Name(dc)}).");
        }

        if (Write(scheme, 0, 0))
            Logger.Info($"Lid: closing the lid now {(behavior == LidBehavior.Rest ? "enters Rest Mode" : "does nothing")} while RestMode runs.");
        else
            Logger.Warn("Lid: could not change the lid setting (Windows refused) - closing the lid keeps its usual action.");
    }

    /// <summary>Puts every saved lid value back exactly and deletes the backup.</summary>
    public static void RestoreAll()
    {
        var backup = JsonFile.Read<Backup>(AppPaths.LidBackupFile);
        if (backup == null) return;
        var failed = new List<Entry>();
        foreach (var e in backup.Entries)
        {
            if (!PowerPlanManager.Exists(e.Scheme)) continue; // plan was deleted by the user
            if (Write(e.Scheme, e.Ac, e.Dc))
                Logger.Info($"Lid: your lid close action is back (plugged in {Name(e.Ac)}, battery {Name(e.Dc)}).");
            else
                failed.Add(e);
        }
        if (failed.Count == 0) JsonFile.Delete(AppPaths.LidBackupFile);
        else
        {
            Logger.Warn("Lid: could not restore the lid setting yet - will retry.");
            JsonFile.Write(AppPaths.LidBackupFile, new Backup { Entries = failed });
        }
    }

    private static (uint, uint)? Read(Guid scheme)
    {
        Guid sub = PowerGuids.SubButtons, set = PowerGuids.LidAction;
        if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out uint ac) != 0) return null;
        if (PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out uint dc) != 0) return null;
        return (ac, dc);
    }

    private static bool Write(Guid scheme, uint ac, uint dc)
    {
        Guid sub = PowerGuids.SubButtons, set = PowerGuids.LidAction;
        bool ok = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, ac) == 0 &
                  PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, dc) == 0;
        // Values of the ACTIVE plan only take effect after it is (re-)activated.
        if (ok && PowerPlanManager.GetActiveScheme() == scheme) PowerPlanManager.Activate(scheme);
        return ok;
    }

    private static string Name(uint v) => v switch { 0 => "Do nothing", 1 => "Sleep", 2 => "Hibernate", 3 => "Shut down", _ => v.ToString() };
}
