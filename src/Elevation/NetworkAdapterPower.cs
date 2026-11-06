using System.Management;
using RestMode.Util;

namespace RestMode.Elevation;

/// <summary>
/// Controls the "Allow the computer to turn off this device to save power"
/// checkbox (Device Manager > adapter > Power Management) for physical network
/// adapters. That checkbox is the WMI class root\WMI:MSPower_DeviceEnable,
/// whose InstanceName starts with the adapter's PnP device id. Changing it
/// takes effect immediately - no adapter restart, so downloads are not dropped.
/// Requires administrator rights.
/// </summary>
internal static class NetworkAdapterPower
{
    /// <summary>
    /// Non-admin pre-check: how many CONNECTED adapters still have the checkbox
    /// ticked. 0 means nothing to change (e.g. Wi-Fi cards on Modern Standby PCs
    /// have no such option), so no UAC prompt is needed. -1 = couldn't tell.
    /// </summary>
    public static int CountActiveNeedingChange()
    {
        try
        {
            var adapters = GetPhysicalAdapters(connectedOnly: true);
            if (adapters.Count == 0) return 0;
            int n = 0;
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, Enable FROM MSPower_DeviceEnable");
            foreach (ManagementObject mo in searcher.Get())
                using (mo)
                {
                    string inst = mo["InstanceName"] as string ?? "";
                    if (adapters.Any(a => inst.StartsWith(a.pnp, StringComparison.OrdinalIgnoreCase)) && mo["Enable"] is bool b && b) n++;
                }
            return n;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Unticks the checkbox on every CONNECTED physical adapter (wired or Wi-Fi); returns the ones changed.</summary>
    public static List<AdapterSnapshot> KeepPowered(List<string> log)
    {
        var changed = new List<AdapterSnapshot>();
        var adapters = GetPhysicalAdapters(connectedOnly: true);
        if (adapters.Count == 0) { log.Add("No connected network adapters found."); return changed; }

        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSPower_DeviceEnable");
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                try
                {
                    string inst = mo["InstanceName"] as string ?? "";
                    var match = adapters.FirstOrDefault(a => inst.StartsWith(a.pnp, StringComparison.OrdinalIgnoreCase));
                    if (match.pnp == null) continue;

                    bool enabled = mo["Enable"] is bool b && b;
                    if (!enabled) { log.Add($"{match.name}: already kept powered."); continue; }

                    mo["Enable"] = false;
                    mo.Put();
                    changed.Add(new AdapterSnapshot { InstanceName = inst, DisplayName = match.name, OriginalEnable = true });
                    log.Add($"{match.name}: power saving turned off for Rest Mode.");
                }
                catch (Exception ex)
                {
                    log.Add($"Adapter skipped: {ex.Message}");
                }
            }
        }
        return changed;
    }

    /// <summary>Puts the checkbox back for each saved adapter. Returns the ones that could NOT be restored.</summary>
    public static List<AdapterSnapshot> Restore(List<AdapterSnapshot> saved, List<string> log)
    {
        var remaining = new List<AdapterSnapshot>(saved);
        if (saved.Count == 0) return remaining;

        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSPower_DeviceEnable");
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                string inst = mo["InstanceName"] as string ?? "";
                var snap = remaining.FirstOrDefault(s => string.Equals(s.InstanceName, inst, StringComparison.OrdinalIgnoreCase));
                if (snap == null) continue;
                try
                {
                    mo["Enable"] = snap.OriginalEnable;
                    mo.Put();
                    remaining.Remove(snap);
                    log.Add($"{snap.DisplayName}: power saving setting restored.");
                }
                catch (Exception ex)
                {
                    log.Add($"{snap.DisplayName}: restore failed: {ex.Message}");
                }
            }
        }

        // Adapters that no longer exist (unplugged USB Wi-Fi etc.) cannot be restored; drop them.
        var gone = remaining.Where(r => !AdapterStillPresent(r.InstanceName)).ToList();
        foreach (var g in gone) { remaining.Remove(g); log.Add($"{g.DisplayName}: adapter no longer present - skipped."); }
        return remaining;
    }

    private static bool AdapterStillPresent(string instanceName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName FROM MSPower_DeviceEnable");
            foreach (ManagementObject mo in searcher.Get())
                using (mo)
                    if (string.Equals(mo["InstanceName"] as string, instanceName, StringComparison.OrdinalIgnoreCase))
                        return true;
        }
        catch { return true; }
        return false;
    }

    private static List<(string pnp, string name)> GetPhysicalAdapters(bool connectedOnly)
    {
        var list = new List<(string, string)>();
        // NetConnectionStatus 2 = Connected: only the adapter(s) actually in use are kept powered.
        using var searcher = new ManagementObjectSearcher(@"root\CIMV2",
            "SELECT PNPDeviceID, Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE" + (connectedOnly ? " AND NetConnectionStatus = 2" : ""));
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                if (mo["PNPDeviceID"] is string pnp && pnp.Length > 0)
                    list.Add((pnp, mo["Name"] as string ?? pnp));
            }
        }
        return list;
    }
}
