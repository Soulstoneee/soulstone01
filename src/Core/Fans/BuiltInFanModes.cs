using System.Management;
using RestMode.Util;

namespace RestMode.Core.Fans;

/// <summary>
/// A manufacturer's own fan / performance modes (the "Quiet / Balanced /
/// Performance" modes of gaming laptops, usually switched with a key like Fn+Q).
///
/// Plug-in rules:
///  * Only official, firmware-provided interfaces (ACPI-WMI classes that the
///    manufacturer's own app uses). No drivers, no direct EC/register access.
///  * <see cref="IsPresent"/> must be cheap and need no admin rights.
///  * Read/Set run inside RestMode's elevated helper.
///  * Restore always writes back the exact mode that was active before.
///  * The firmware's Quiet mode keeps its own temperature protection: fans
///    still speed up when the PC gets hot and are never forced to stop.
/// </summary>
internal interface IBuiltInFanModes
{
    /// <summary>Stable id (stored in helper-state.json).</summary>
    string Id { get; }
    string Brand { get; }
    /// <summary>false = detected, but switching is not supported yet.</summary>
    bool Implemented { get; }
    bool IsPresent();
    int QuietMode { get; }
    /// <summary>Admin. Current mode, or null if it can't be read.</summary>
    int? ReadMode(List<string> log);
    /// <summary>Admin. Switches the mode and verifies it.</summary>
    bool SetMode(int mode, List<string> log);
    /// <summary>Values the helper may write (helper-state.json is user-writable, so restore values are checked).</summary>
    bool IsValidMode(int mode);
    string ModeName(int mode);
    /// <summary>Admin. Temperatures / fan speeds from the firmware, or null if this plug-in has none.</summary>
    Safety.SensorReading? ReadSensors(List<string> log);
}

internal static class BuiltInFanModes
{
    public static readonly IBuiltInFanModes[] All =
    {
        new LenovoGameZone(),
        new NotSupportedYet("asus", "ASUS (Armoury Crate silent mode)", "ASUSTeK", "ASUS"),
        new NotSupportedYet("msi", "MSI (MSI Center)", "Micro-Star", "MSI"),
        new NotSupportedYet("hp", "HP OMEN / Victus (OMEN Gaming Hub)", "HP", "Hewlett-Packard"),
        new NotSupportedYet("dell", "Dell / Alienware (thermal modes)", "Dell", "Alienware"),
        new NotSupportedYet("acer", "Acer (PredatorSense / NitroSense)", "Acer"),
        new NotSupportedYet("razer", "Razer (Synapse)", "Razer"),
    };

    public static IBuiltInFanModes? Find(string? id) =>
        id == null ? null : All.FirstOrDefault(p => p.Id == id);

    /// <summary>The first plug-in whose hardware is present (implemented or not), with a message for the UI/log.</summary>
    public static (IBuiltInFanModes? provider, string message) Detect()
    {
        foreach (var p in All)
        {
            bool present;
            try { present = p.IsPresent(); }
            catch { present = false; }
            if (!present) continue;
            return p.Implemented
                ? (p, $"{p.Brand}: built-in fan modes found. Rest Mode will use the Quiet mode and restore your mode on wake (needs admin).")
                : (p, $"{p.Brand} detected, but its fan modes are not supported yet. Fan Control or the Windows power mode is used instead.");
        }
        return (null, "No built-in fan modes found on this PC (normal for desktops). Fan Control or the Windows power mode is used instead.");
    }

    internal static string Manufacturer()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return (k?.GetValue("SystemManufacturer") as string ?? "").Trim();
        }
        catch { return ""; }
    }
}

/// <summary>
/// Lenovo Legion / LOQ / IdeaPad Gaming: the firmware's LENOVO_GAMEZONE_DATA
/// WMI class (root\WMI), the interface Lenovo Vantage / Legion Space use for
/// Fn+Q. SmartFanMode: 1 = Quiet, 2 = Balanced, 3 = Performance,
/// 224/255 = Custom (model dependent). Quiet keeps the firmware's own fan
/// curve: fans still ramp up when temperatures rise.
/// </summary>
internal sealed class LenovoGameZone : IBuiltInFanModes
{
    private const string Ns = @"root\WMI";
    private const string Cls = "LENOVO_GAMEZONE_DATA";
    private static readonly int[] Valid = { 1, 2, 3, 224, 255 };

    public string Id => "lenovo-gamezone";
    public string Brand => "Lenovo (Legion / LOQ, Fn+Q modes)";
    public bool Implemented => true;
    public int QuietMode => 1;

    public bool IsPresent()
    {
        // Reading the class definition needs no admin rights.
        using var c = new ManagementClass(Ns, Cls, null);
        c.Get();
        return c.Methods.Cast<MethodData>().Any(m => m.Name == "SetSmartFanMode");
    }

    public bool IsValidMode(int mode) => Valid.Contains(mode);

    public string ModeName(int mode) => mode switch
    {
        1 => "Quiet",
        2 => "Balanced",
        3 => "Performance",
        224 or 255 => "Custom",
        _ => $"mode {mode}",
    };

    public int? ReadMode(List<string> log)
    {
        try
        {
            uint? supported = Call("IsSupportSmartFan", null);
            if (supported == 0) { log.Add("Lenovo: this model reports no switchable fan modes."); return null; }
            uint? m = Call("GetSmartFanMode", null);
            return m is uint v ? (int)v : null;
        }
        catch (Exception ex)
        {
            log.Add($"Lenovo: reading the fan mode failed: {ex.Message}");
            return null;
        }
    }

    public bool SetMode(int mode, List<string> log)
    {
        if (!IsValidMode(mode)) { log.Add($"Lenovo: refused unknown mode {mode}."); return false; }
        try
        {
            Call("SetSmartFanMode", (uint)mode);
            Thread.Sleep(500);
            uint? now = Call("GetSmartFanMode", null);
            if (now == (uint)mode) { log.Add($"Lenovo fan mode set to {ModeName(mode)}."); return true; }
            log.Add($"Lenovo: asked for {ModeName(mode)} but the firmware reports {(now is uint n ? ModeName((int)n) : "unknown")}.");
            return false;
        }
        catch (Exception ex)
        {
            log.Add($"Lenovo: switching the fan mode failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// CPU/GPU temperature from GameZone. Fan RPM: newer models (e.g. LOQ / Legion 2023+) don't implement
    /// GameZone's GetFan1Speed (it always returns 0 and GetFanCount = 0); their real speeds come from
    /// LENOVO_OTHER_METHOD.GetFeatureValue (feature ids 0x04030001 / 0x04030002), the interface Lenovo's
    /// own app uses. If neither works, fan speed is reported as "not available on this device".
    /// </summary>
    public Safety.SensorReading? ReadSensors(List<string> log)
    {
        var r = new Safety.SensorReading { Source = "Lenovo firmware" };
        try
        {
            if (Call("GetCPUTemp", null) is uint cpu && cpu is > 5 and < 120) r.CpuC = cpu;
            // The GPU reports 0-1 °C while the discrete GPU is powered off: that's "no reading", not a temperature.
            if (Call("GetGPUTemp", null) is uint gpu && gpu is > 5 and < 120) r.GpuC = gpu;
        }
        catch (Exception ex) { log.Add($"Lenovo: temperature read failed: {ex.Message}"); }

        var fans = new List<int>();
        try
        {
            foreach (uint id in new uint[] { 0x04030001, 0x04030002 })
                if (OtherFeature(id) is uint rpm and < 10000) fans.Add((int)rpm);
        }
        catch { fans.Clear(); }
        if (fans.Count == 0)
        {
            try
            {
                if (Call("GetFanCount", null) is uint n && n > 0)
                {
                    if (Call("GetFan1Speed", null) is uint f1) fans.Add((int)f1);
                    if (n > 1 && Call("GetFan2Speed", null) is uint f2) fans.Add((int)f2);
                }
            }
            catch { /* not available */ }
        }
        r.FanRpm = fans.Count > 0 ? fans : null;
        return r.HasTemperature || r.FanRpm != null ? r : null;
    }

    private static uint? OtherFeature(uint id)
    {
        using var searcher = new ManagementObjectSearcher(Ns, "SELECT * FROM LENOVO_OTHER_METHOD");
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                using var inParams = mo.GetMethodParameters("GetFeatureValue");
                inParams["IDs"] = id;
                using var outParams = mo.InvokeMethod("GetFeatureValue", inParams, null);
                return outParams?["value"] is object v ? Convert.ToUInt32(v) : null;
            }
        }
        return null;
    }

    private static uint? Call(string method, uint? arg)
    {
        using var searcher = new ManagementObjectSearcher(Ns, $"SELECT * FROM {Cls}");
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                ManagementBaseObject? inParams = null;
                if (arg is uint a)
                {
                    inParams = mo.GetMethodParameters(method);
                    inParams["Data"] = a;
                }
                using var outParams = mo.InvokeMethod(method, inParams, null);
                inParams?.Dispose();
                object? data = null;
                try { data = outParams?["Data"]; } catch (ManagementException) { /* method has no Data output */ }
                return data == null ? null : Convert.ToUInt32(data);
            }
        }
        throw new InvalidOperationException("LENOVO_GAMEZONE_DATA has no instance");
    }
}

/// <summary>Brands that are recognised but not switchable yet. They never change anything.</summary>
internal sealed class NotSupportedYet : IBuiltInFanModes
{
    private readonly string[] _manufacturers;

    public NotSupportedYet(string id, string brand, params string[] manufacturers)
    {
        Id = id;
        Brand = brand;
        _manufacturers = manufacturers;
    }

    public string Id { get; }
    public string Brand { get; }
    public bool Implemented => false;
    public int QuietMode => 0;

    public bool IsPresent()
    {
        string m = BuiltInFanModes.Manufacturer();
        return m.Length > 0 && _manufacturers.Any(x => m.StartsWith(x, StringComparison.OrdinalIgnoreCase));
    }

    public int? ReadMode(List<string> log) { log.Add($"{Brand}: not supported yet."); return null; }
    public bool SetMode(int mode, List<string> log) { log.Add($"{Brand}: not supported yet."); return false; }
    public bool IsValidMode(int mode) => false;
    public string ModeName(int mode) => $"mode {mode}";
    public Safety.SensorReading? ReadSensors(List<string> log) => null;
}
