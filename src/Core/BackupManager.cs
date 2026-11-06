using System.Diagnostics;
using System.Management;
using RestMode.Core.Fans;
using RestMode.Elevation;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// "Create a backup" (first-start guide and Settings): a dated folder with the
/// user's current power plan settings, lid / button settings, Windows power
/// mode, network adapter power options, Fan Control configs and RestMode's own
/// settings, plus a README explaining how to put them back by hand. Needs no
/// admin rights.
/// </summary>
internal static class BackupManager
{
    public static string Root => Path.Combine(AppPaths.DataDir, "Backups");

    public static string? Create(DeviceProfile dev, AppSettings s)
    {
        try
        {
            string dir = Path.Combine(Root, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            Directory.CreateDirectory(dir);
            var active = PowerPlanManager.GetActiveScheme();
            File.WriteAllText(Path.Combine(dir, "power-plans.txt"), Run("powercfg.exe", "/list"));
            File.WriteAllText(Path.Combine(dir, "power-plan-settings.txt"), Run("powercfg.exe", "/qh SCHEME_CURRENT"));

            var lines = new List<string> { $"Active plan: {active} ({(active is Guid a ? PowerPlanManager.GetName(a) : "?")})" };
            if (active is Guid scheme)
            {
                foreach (var (name, guid) in new[] { ("Lid close action", PowerGuids.LidAction), ("Power button action", new Guid("7648efa3-dd9c-4e3e-b566-50f929386280")), ("Sleep button action", new Guid("96996bc0-ad50-47ec-923b-6f41874dd9eb")) })
                {
                    Guid sub = PowerGuids.SubButtons, set = guid, sch = scheme;
                    string ac = PowerReadACValueIndex(IntPtr.Zero, ref sch, ref sub, ref set, out uint v1) == 0 ? v1.ToString() : "n/a";
                    string dc = PowerReadDCValueIndex(IntPtr.Zero, ref sch, ref sub, ref set, out uint v2) == 0 ? v2.ToString() : "n/a";
                    lines.Add($"{name}: plugged in {ac}, battery {dc}   (0 Do nothing, 1 Sleep, 2 Hibernate, 3 Shut down)");
                }
            }
            lines.Add($"Windows power mode: {WindowsPowerMode.Name(WindowsPowerMode.Get())} ({WindowsPowerMode.Get()})");
            File.WriteAllLines(Path.Combine(dir, "lid-buttons-power-mode.txt"), lines);

            var nets = Mta.Run(() =>
            {
            var nets = new List<string>();
            try
            {
                using var nics = new ManagementObjectSearcher(@"root\CIMV2", "SELECT PNPDeviceID, Name, NetConnectionStatus FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");
                using var pm = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, Enable FROM MSPower_DeviceEnable");
                var flags = pm.Get().Cast<ManagementObject>().Select(m => (inst: m["InstanceName"] as string ?? "", en: m["Enable"] is bool b && b)).ToList();
                foreach (ManagementObject n in nics.Get())
                {
                    string pnp = n["PNPDeviceID"] as string ?? "";
                    var f = flags.FirstOrDefault(x => pnp.Length > 0 && x.inst.StartsWith(pnp, StringComparison.OrdinalIgnoreCase));
                    nets.Add($"{n["Name"]}: connected={(n["NetConnectionStatus"] is ushort st && st == 2)}, allow the computer to turn off this device = {(f.inst == null ? "no such option" : f.en.ToString())}");
                }
            }
            catch (Exception ex) { nets.Add("could not read: " + ex.Message); }
            return nets;
            }, 15_000, new List<string> { "could not read (timed out)" });
            File.WriteAllLines(Path.Combine(dir, "network-adapters.txt"), nets);

            string? fc = FanControlSwitcher.NormalizeExe(s.FanControlExe) ?? FanControlSwitcher.Detect();
            if (fc != null && Directory.Exists(FanControlSwitcher.ConfigurationsDir(fc)))
            {
                var dest = Path.Combine(dir, "FanControl-Configurations");
                Directory.CreateDirectory(dest);
                foreach (var file in Directory.GetFiles(FanControlSwitcher.ConfigurationsDir(fc), "*.json"))
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            }
            if (File.Exists(AppPaths.SettingsFile)) File.Copy(AppPaths.SettingsFile, Path.Combine(dir, "restmode-settings.json"), true);

            File.WriteAllText(Path.Combine(dir, "README.txt"),
                "RestMode backup - your settings at " + DateTime.Now + "\r\n\r\n" +
                "RestMode restores everything it changes by itself (on wake, after a crash, at log-off, on uninstall,\r\n" +
                "and with RestMode-Reset.cmd). These files are for checking or restoring by hand:\r\n\r\n" +
                " - power-plan-settings.txt: every setting of your active power plan (powercfg /qh).\r\n" +
                "   Switch back to your plan: powercfg /setactive <GUID from power-plans.txt>\r\n" +
                " - lid-buttons-power-mode.txt: lid / power button actions. To set the lid back, e.g. to Sleep (1):\r\n" +
                "   powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1\r\n" +
                "   powercfg /setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1\r\n" +
                "   powercfg /setactive SCHEME_CURRENT\r\n" +
                "   Power mode: Settings > System > Power & battery > Power mode.\r\n" +
                " - network-adapters.txt: Device Manager > adapter > Power Management.\r\n" +
                " - FanControl-Configurations: copy back into Fan Control's Configurations folder.\r\n" +
                " - restmode-settings.json: RestMode Settings > Backup & Reset > Import settings.\r\n");
            Logger.Info("Backup created.");
            return dir;
        }
        catch (Exception ex)
        {
            Logger.Error("Creating the backup failed", ex);
            return null;
        }
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe), args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15_000);
        return o;
    }
}
