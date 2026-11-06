using System.Runtime.InteropServices;
using System.Text;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// Creates the temporary "RestMode" power plan.
///
/// Rule #1: the user's own plan is NEVER edited. We duplicate it
/// (same as `powercfg -duplicatescheme`), change the copy, activate the copy,
/// and on exit re-activate the original and delete the copy. Because the
/// original is untouched, all of its AC (plugged in) and DC (battery) values
/// come back exactly as they were.
///
/// Uses the documented powrprof.dll API (PowerDuplicateScheme,
/// PowerWriteACValueIndex/PowerWriteDCValueIndex, PowerSetActiveScheme).
/// Each individual setting is best-effort: if a setting does not exist on this
/// PC (e.g. no PCIe ASPM, no "boost mode"), it is skipped and logged.
/// </summary>
internal static class PowerPlanManager
{
    public const string TempPlanName = "RestMode (temporary)";

    /// <summary>
    /// On Modern Standby (S0ix) PCs the screen is turned off by this short idle
    /// timeout instead of SC_MONITORPOWER: on those PCs Windows treats
    /// "monitor off" like the power button and enters standby (network off,
    /// desktop apps frozen). An idle screen-off keeps the PC fully running
    /// while RestMode holds its "system required" request.
    /// </summary>
    public const int ModernStandbyScreenOffSeconds = 15;

    public static Guid? GetActiveScheme()
    {
        uint err = PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr);
        if (err != 0 || ptr == IntPtr.Zero)
        {
            Logger.Warn($"PowerGetActiveScheme failed (error {err}).");
            return null;
        }
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { Marshal.FreeHGlobal(ptr); } // LocalFree == FreeHGlobal on Windows
    }

    /// <summary>Duplicates <paramref name="source"/> and writes the Rest Mode values into the copy.</summary>
    /// <summary>Set while writing a plan: desktops get plugged-in (AC) values only, battery devices AC + DC.</summary>
    private static bool _writeDc = true;

    /// <summary>True if the last plan could cap the CPU (some devices/BIOSes hide processor settings).</summary>
    public static bool CpuLimitApplied { get; private set; }

    public static Guid CreateRestPlan(Guid source, AppSettings s, DeviceProfile dev)
    {
        var caps = dev.Caps;
        _writeDc = dev.UsesBattery;
        IntPtr dest = IntPtr.Zero;
        uint err = PowerDuplicateScheme(IntPtr.Zero, ref source, ref dest);
        if (err != 0 || dest == IntPtr.Zero)
            throw new InvalidOperationException($"PowerDuplicateScheme failed (error {err}).");

        Guid temp;
        try { temp = Marshal.PtrToStructure<Guid>(dest); }
        finally { Marshal.FreeHGlobal(dest); }

        // Name it so leftovers can be found and cleaned up later.
        byte[] name = Encoding.Unicode.GetBytes(TempPlanName + "\0");
        err = PowerWriteFriendlyName(IntPtr.Zero, ref temp, IntPtr.Zero, IntPtr.Zero, name, (uint)name.Length);
        if (err != 0) Logger.Warn($"Could not name the temporary plan (error {err}).");

        uint cpuMax = (uint)s.CpuMaxPercent;
        uint cpuMin = (uint)Math.Min(s.CpuMinPercent, s.CpuMaxPercent);

        Logger.Info($"  plan: writing {(_writeDc ? "plugged-in and battery" : "plugged-in (AC) only - no battery")} values; {dev.CpuText} CPU.");

        // Processor: capped, no turbo, prefer efficiency, passive cooling (throttle before fans spin up).
        // These setting GUIDs are the same on Intel, AMD and ARM; any one a CPU/BIOS doesn't offer is skipped.
        // ("E-cores" variants only exist on Intel hybrid CPUs.)
        CpuLimitApplied = Write(temp, PowerGuids.SubProcessor, PowerGuids.ProcThrottleMax, cpuMax, cpuMax, "Maximum processor state", optional: true);
        if (!CpuLimitApplied)
            Logger.Warn("  plan: this device does not allow limiting the processor speed - CPU limit skipped (the rest of Rest Mode still works).");
        if (dev.Cpu == CpuVendor.Intel)
        {
            Write(temp, PowerGuids.SubProcessor, PowerGuids.ProcThrottleMax1, cpuMax, cpuMax, "Maximum processor state (E-cores)", optional: true);
            Write(temp, PowerGuids.SubProcessor, PowerGuids.ProcThrottleMin1, cpuMin, cpuMin, "Minimum processor state (E-cores)", optional: true);
            Write(temp, PowerGuids.SubProcessor, PowerGuids.PerfEpp1, 100, 100, "Energy performance preference (E-cores)", optional: true);
        }
        Write(temp, PowerGuids.SubProcessor, PowerGuids.ProcThrottleMin, cpuMin, cpuMin, "Minimum processor state", optional: true);
        // Boost off: Intel Turbo Boost / AMD Precision Boost. Same setting for both vendors.
        Write(temp, PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 0, 0, "Processor performance boost mode = Disabled", optional: true);
        Write(temp, PowerGuids.SubProcessor, PowerGuids.PerfEpp, 100, 100, "Energy performance preference = max savings", optional: true);
        Write(temp, PowerGuids.SubProcessor, PowerGuids.SysCoolPol, 0, 0, "System cooling policy = Passive", optional: true);

        // PCIe link power management: Maximum power savings (L1 ASPM).
        Write(temp, PowerGuids.SubPciExpress, PowerGuids.Aspm, 2, 2, "PCI Express Link State Power Management = Maximum", optional: true);

        // Hard disks spin down when idle. A disk that is being written to (a
        // download, an update unpacking) is not idle, so transfers are never cut.
        uint disk = (uint)s.DiskIdleSeconds;
        Write(temp, PowerGuids.SubDisk, PowerGuids.DiskIdle, disk, disk, "Turn off hard disk after");

        if (caps.ModernStandby)
        {
            // Modern Standby: this idle timer IS how the screen goes off (see ModernStandbyScreenOffSeconds).
            uint off = ModernStandbyScreenOffSeconds;
            Write(temp, PowerGuids.SubVideo, PowerGuids.VideoIdle, off, off, $"Turn off display after {off} s idle (Modern Standby PC)");
            Write(temp, PowerGuids.SubVideo, PowerGuids.VideoAdapt, 0, 0, "Adaptive display timeout = Off", optional: true);
            Write(temp, PowerGuids.SubVideo, PowerGuids.VideoConLock, off, off, "Lock screen display off timeout", optional: true);
        }
        else
        {
            // If something wakes the screen without user input, Windows turns it off again after 60 s.
            Write(temp, PowerGuids.SubVideo, PowerGuids.VideoIdle, 60, 60, "Turn off display after");
        }

        // RestMode itself decides when to sleep (after downloads finish), so idle sleep is off in the temp plan.
        Write(temp, PowerGuids.SubSleep, PowerGuids.StandbyIdle, 0, 0, "Sleep after = Never");
        Write(temp, PowerGuids.SubSleep, PowerGuids.HibernateIdle, 0, 0, "Hibernate after = Never");

        // Wi-Fi radio at full power so downloads are not slowed down by power saving.
        Write(temp, PowerGuids.SubWireless, PowerGuids.WirelessPowerSaving, 0, 0, "Wireless adapter = Maximum Performance", optional: true);

        // Lid: only devices that have one. Otherwise the user's own lid setting (copied from their plan) applies.
        if (caps.LidPresent && s.AllowLidClosed)
            Write(temp, PowerGuids.SubButtons, PowerGuids.LidAction, 0, 0, "Lid close action = Do nothing", optional: true);
        else if (caps.LidPresent)
            Logger.Info("  plan: lid close action left as in your plan (closing the lid ends Rest Mode as usual).");

        return temp;
    }

    /// <returns>true if the (AC) value was written.</returns>
    private static bool Write(Guid scheme, Guid sub, Guid setting, uint ac, uint dc, string label, bool optional = false)
    {
        uint e1 = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, ac);
        uint e2 = _writeDc ? PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, dc) : 0;
        if (e1 == 0 && e2 == 0)
            Logger.Info(_writeDc ? $"  plan: {label} (AC {ac} / DC {dc})" : $"  plan: {label} (AC {ac})");
        else if (optional)
            Logger.Info($"  plan: {label} not available on this PC - skipped");
        else
            Logger.Warn($"  plan: could not set {label} (AC error {e1}, DC error {e2}) - skipped");
        return e1 == 0;
    }

    public static bool Activate(Guid scheme)
    {
        uint err = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (err != 0) Logger.Warn($"PowerSetActiveScheme({scheme}) failed (error {err}).");
        return err == 0;
    }

    public static bool Delete(Guid scheme)
    {
        uint err = PowerDeleteScheme(IntPtr.Zero, ref scheme);
        if (err != 0) Logger.Warn($"PowerDeleteScheme({scheme}) failed (error {err}).");
        return err == 0;
    }

    public static string? GetName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0)
            return null;
        var buf = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) != 0)
            return null;
        return Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    public static List<Guid> EnumerateSchemes()
    {
        var list = new List<Guid>();
        for (uint i = 0; i < 256; i++)
        {
            Guid g = Guid.Empty;
            uint size = (uint)Marshal.SizeOf<Guid>();
            uint err = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, ref g, ref size);
            if (err != 0) break; // ERROR_NO_MORE_ITEMS or failure
            list.Add(g);
        }
        return list;
    }

    public static bool Exists(Guid scheme) => EnumerateSchemes().Contains(scheme);

    /// <summary>
    /// Puts the user's plan back and deletes the temporary one.
    /// If the original plan no longer exists, falls back to Balanced.
    /// </summary>
    public static void Restore(Guid? original, Guid? temp)
    {
        Guid target = original is Guid o && Exists(o) ? o : PowerGuids.Balanced;
        if (Activate(target))
            Logger.Info($"Power plan restored: {GetName(target) ?? target.ToString()}");
        if (temp is Guid t && t != target && Exists(t))
        {
            if (Delete(t)) Logger.Info("Temporary RestMode power plan deleted.");
        }
    }

    /// <summary>
    /// Deletes any "RestMode (temporary)" plan left behind (e.g. a crash happened
    /// between creating the plan and saving state). If one is active, the
    /// Balanced plan is activated first.
    /// </summary>
    public static void CleanupOrphans(Guid? keep = null)
    {
        try
        {
            Guid? active = GetActiveScheme();
            foreach (var g in EnumerateSchemes())
            {
                if (g == keep || GetName(g) != TempPlanName) continue;
                if (g == active)
                {
                    Logger.Warn("A leftover RestMode plan was active; switching to Balanced.");
                    Activate(PowerGuids.Balanced);
                }
                if (Delete(g)) Logger.Info($"Deleted leftover RestMode plan {g}.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Cleaning up leftover plans failed", ex);
        }
    }
}
