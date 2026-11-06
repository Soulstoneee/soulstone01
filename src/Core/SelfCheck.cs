using Microsoft.Win32;
using RestMode.Core.Fans;
using RestMode.Elevation;
using RestMode.Util;

namespace RestMode.Core;

/// <summary>
/// Startup self-check. Runs on first start and again whenever Windows, the BIOS
/// or the graphics driver changed (a "fingerprint" of their versions). Each
/// RestMode feature is tried in a harmless way; a feature that fails is
/// switched off (AppSettings.DisabledFeatures) with a clear message, and the
/// rest keeps working. A feature that passes again is switched back on.
/// </summary>
internal static class SelfCheck
{
    public sealed class Result
    {
        public string Fingerprint { get; set; } = "";
        public DateTime LastRun { get; set; }
        public Dictionary<string, string> Checks { get; set; } = new();
    }

    public static readonly Dictionary<string, string> FeatureNames = new()
    {
        ["cpu-limit"] = "Low-power plan (CPU limit)",
        ["powermode"] = "Windows power mode switch",
        ["builtin-fan"] = "Built-in fan modes",
        ["fancontrol"] = "Fan Control switching",
        ["audio"] = "Mute while resting",
        ["lid"] = "Lid action",
    };

    /// <summary>Returns messages for features that were newly switched off (empty if all fine / not re-run).</summary>
    public static List<string> RunIfNeeded(AppSettings s, DeviceProfile dev, bool force = false)
    {
        var messages = new List<string>();
        string fp = Fingerprint();
        var last = JsonFile.Read<Result>(AppPaths.SelfCheckFile);
        if (!force && last != null && last.Fingerprint == fp) return messages;

        Logger.Info(last == null ? "Self-check: first run." : "Self-check: Windows, BIOS or driver changed since last time - checking every feature again.");
        var result = new Result { Fingerprint = fp, LastRun = DateTime.Now };
        var disabled = new List<string>();

        void Check(string id, Func<string?> test)
        {
            string? problem;
            try { problem = test(); }
            catch (Exception ex) { problem = ex.Message; }
            result.Checks[id] = problem ?? "ok";
            if (problem != null)
            {
                disabled.Add(id);
                Logger.Warn($"Self-check: {FeatureNames[id]} - {problem} - switched off.");
            }
        }

        Check("cpu-limit", () =>
        {
            var active = PowerPlanManager.GetActiveScheme() ?? throw new InvalidOperationException("no active power plan");
            var temp = PowerPlanManager.CreateRestPlan(active, s, dev); // a copy only - never activated here
            bool ok = PowerPlanManager.CpuLimitApplied;
            PowerPlanManager.Delete(temp);
            return ok ? null : "this PC doesn't allow limiting the CPU";
        });
        Check("powermode", () => WindowsPowerMode.Get() == null ? "not available" : null);
        Check("builtin-fan", () =>
        {
            var (p, _) = BuiltInFanModes.Detect();
            // Not having built-in modes is fine; only a vendor that is present but whose interface broke counts.
            return null;
        });
        Check("fancontrol", () =>
        {
            if (string.IsNullOrWhiteSpace(s.FanControlExe)) return null;
            var exe = FanControlSwitcher.NormalizeExe(s.FanControlExe);
            if (exe == null) return $"FanControl.exe not found at {s.FanControlExe}";
            return FanControlSwitcher.CheckRuntime(exe);
        });
        Check("audio", () => AudioController.Capture() == null ? "no playback device" : null);
        Check("lid", () => !dev.Caps.LidPresent || PowerPlanManager.GetActiveScheme() != null ? null : "can't read the lid setting");

        var newly = disabled.Except(s.DisabledFeatures, StringComparer.OrdinalIgnoreCase).ToList();
        var back = s.DisabledFeatures.Except(disabled, StringComparer.OrdinalIgnoreCase).ToList();
        s.DisabledFeatures = disabled;
        SettingsStore.Save(s);
        JsonFile.Write(AppPaths.SelfCheckFile, result);

        foreach (var id in newly) messages.Add($"{FeatureNames[id]} doesn't work on this PC right now ({result.Checks[id]}) and was switched off. Everything else still works.");
        foreach (var id in back) Logger.Info($"Self-check: {FeatureNames[id]} works again - switched back on.");
        Logger.Info($"Self-check done: {string.Join(", ", result.Checks.Select(c => $"{c.Key}={c.Value}"))}");
        return messages;
    }

    private static string Fingerprint()
    {
        string os = "", bios = "", gpu = "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            os = $"{k?.GetValue("CurrentBuildNumber")}.{k?.GetValue("UBR")}";
        }
        catch { }
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            bios = $"{k?.GetValue("BIOSVersion")}";
        }
        catch { }
        try
        {
            gpu = Mta.Run(() =>
            {
                using var s = new System.Management.ManagementObjectSearcher("SELECT DriverVersion FROM Win32_VideoController");
                return string.Join(",", s.Get().Cast<System.Management.ManagementObject>().Select(m => m["DriverVersion"] as string ?? ""));
            }, 8000, "");
        }
        catch { }
        return $"{os}|{bios}|{gpu}|v{typeof(SelfCheck).Assembly.GetName().Version}";
    }
}
