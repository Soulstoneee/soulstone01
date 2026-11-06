using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Security.Principal;
using Microsoft.Win32;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

public enum DeviceKind
{
    Desktop,
    Laptop,
    /// <summary>Battery but no lid: tablets and handhelds (ROG Ally, Legion Go, Steam Deck with Windows...).</summary>
    HandheldOrTablet,
}

public enum CpuVendor { Unknown, Intel, Amd, Arm }

public enum AccountKind
{
    /// <summary>Administrator account (UAC prompts are a simple Yes/No).</summary>
    Admin,
    /// <summary>Standard user: admin steps need an administrator's password.</summary>
    Standard,
}

/// <summary>
/// What kind of PC this is, detected at runtime every time RestMode starts or
/// enters Rest Mode. Nothing here is specific to one machine: RestMode adapts
/// its behaviour to these values (see README "Different kinds of PCs").
/// </summary>
internal sealed class DeviceProfile
{
    public required PowerCapabilities Caps { get; init; }
    public DeviceKind Kind { get; init; }
    public int PlatformRole { get; init; }
    public CpuVendor Cpu { get; init; }
    public string CpuName { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public int MonitorCount { get; init; }
    public AccountKind Account { get; init; }
    public bool Elevated { get; init; }
    /// <summary>Connected adapters with a default gateway, e.g. "Wi-Fi (Wi-Fi)" / "Ethernet (Ethernet)".</summary>
    public List<string> ActiveNetworks { get; init; } = new();
    public bool HasActiveWired { get; init; }
    public bool HasActiveWifi { get; init; }
    /// <summary>NVIDIA / AMD Radeon RX / Intel Arc graphics card (a "gaming" laptop runs hotter by design).</summary>
    public bool HasDiscreteGpu { get; init; }
    public List<string> Gpus { get; init; } = new();

    /// <summary>Lenovo / ASUS / MSI / HP / Dell / Acer / Razer / other, from the firmware's manufacturer name.</summary>
    public string Vendor => Manufacturer switch
    {
        var m when m.StartsWith("LENOVO", StringComparison.OrdinalIgnoreCase) => "Lenovo",
        var m when m.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase) => "ASUS",
        var m when m.StartsWith("Micro-Star", StringComparison.OrdinalIgnoreCase) || m.StartsWith("MSI", StringComparison.OrdinalIgnoreCase) => "MSI",
        var m when m.StartsWith("HP", StringComparison.OrdinalIgnoreCase) || m.StartsWith("Hewlett", StringComparison.OrdinalIgnoreCase) => "HP",
        var m when m.StartsWith("Dell", StringComparison.OrdinalIgnoreCase) || m.StartsWith("Alienware", StringComparison.OrdinalIgnoreCase) => "Dell",
        var m when m.StartsWith("Acer", StringComparison.OrdinalIgnoreCase) => "Acer",
        var m when m.StartsWith("Razer", StringComparison.OrdinalIgnoreCase) => "Razer",
        "" => "unknown",
        var m => m,
    };

    /// <summary>Automatic overheat limit: desktop 80 °C, gaming laptop 75 °C, thin laptop / handheld 70 °C.</summary>
    public int DefaultTempLimitC => Kind switch
    {
        DeviceKind.Desktop => 80,
        DeviceKind.Laptop when HasDiscreteGpu => 75,
        _ => 70,
    };

    public string ClassText => Kind switch
    {
        DeviceKind.Desktop => "desktop",
        DeviceKind.Laptop when HasDiscreteGpu => "gaming laptop",
        DeviceKind.Laptop => "laptop",
        _ => "handheld / tablet",
    };

    /// <summary>Laptop/handheld/tablet: battery (DC) values matter. Desktops (including ones on a UPS) only use AC values.</summary>
    public bool UsesBattery => Caps.HasRealBattery;

    public static DeviceProfile Detect()
    {
        var caps = PowerCapabilities.Read();
        int role = Safe(() => PowerDeterminePlatformRoleEx(POWER_PLATFORM_ROLE_V2), 0);

        DeviceKind kind = !caps.HasRealBattery ? DeviceKind.Desktop
            : caps.LidPresent ? DeviceKind.Laptop
            : DeviceKind.HandheldOrTablet;

        var (vendor, cpuName) = Safe(ReadCpu, (CpuVendor.Unknown, ""));
        var (mfr, model) = Safe(ReadSystem, ("", ""));
        var nets = Safe(ReadNetworks, (new List<string>(), false, false));
        var gpus = Safe(ReadGpus, new List<string>());
        bool dgpu = gpus.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                  g.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                                  g.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase) ||
                                  g.Contains("Arc(TM) A", StringComparison.OrdinalIgnoreCase) ||
                                  g.Contains("Arc A", StringComparison.OrdinalIgnoreCase));

        return new DeviceProfile
        {
            Caps = caps,
            Kind = kind,
            PlatformRole = role,
            Cpu = vendor,
            CpuName = cpuName,
            Manufacturer = mfr,
            Model = model,
            MonitorCount = Safe(() => Screen.AllScreens.Length, 1),
            Account = Safe(ReadAccount, AccountKind.Admin),
            Elevated = Safe(() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator), false),
            ActiveNetworks = nets.Item1,
            HasActiveWired = nets.Item2,
            HasActiveWifi = nets.Item3,
            Gpus = gpus,
            HasDiscreteGpu = dgpu,
        };
    }

    private static List<string> ReadGpus()
    {
        var list = new List<string>();
        using var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
        foreach (System.Management.ManagementObject mo in s.Get())
            using (mo)
                if (mo["Name"] is string n && n.Length > 0) list.Add(n.Trim());
        return list;
    }

    public string KindText => Kind switch
    {
        DeviceKind.Desktop => Caps.HasBattery ? "desktop (on a UPS)" : "desktop",
        DeviceKind.Laptop => "laptop",
        _ => "handheld / tablet",
    };

    public string SleepText => Caps.ModernStandby
        ? "Modern Standby (S0ix)" + (Caps.ModernStandbyNetworkConnected ? "" : ", network off in standby")
        : Caps.S3Supported ? "classic S3 sleep" : "no sleep state reported";

    public string CpuText => Cpu switch { CpuVendor.Intel => "Intel", CpuVendor.Amd => "AMD", CpuVendor.Arm => "ARM", _ => "unknown CPU" };

    public string Summary() =>
        $"{ClassText}{(Caps.HasBattery && !Caps.HasRealBattery ? " (on a UPS)" : "")}, {(Manufacturer.Length > 0 ? Manufacturer + " " + Model + ", " : "")}{CpuText} CPU, " +
        $"{(HasDiscreteGpu ? "discrete GPU, " : "")}{SleepText}, " +
        $"hibernate {(Caps.HibernateSupported ? "on" : "off")}, {MonitorCount} monitor(s), " +
        $"network: {(ActiveNetworks.Count > 0 ? string.Join(" + ", ActiveNetworks) : "none connected")}, " +
        $"{(Account == AccountKind.Admin ? "administrator" : "standard")} account{(Elevated ? " (running as admin)" : "")}";

    // ------------------------------------------------------------------ readers

    private static (CpuVendor, string) ReadCpu()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        string id = (k?.GetValue("VendorIdentifier") as string ?? "").Trim();
        string name = (k?.GetValue("ProcessorNameString") as string ?? "").Trim();
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        CpuVendor v = id.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Intel
            : id.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Amd
            : arch == System.Runtime.InteropServices.Architecture.Arm64 ? CpuVendor.Arm
            : CpuVendor.Unknown;
        return (v, name);
    }

    private static (string, string) ReadSystem()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        string mfr = (k?.GetValue("SystemManufacturer") as string ?? "").Trim();
        string family = (k?.GetValue("SystemFamily") as string ?? "").Trim();
        string product = (k?.GetValue("SystemProductName") as string ?? "").Trim();
        return (mfr, family.Length > 0 ? family : product);
    }

    private static (List<string>, bool, bool) ReadNetworks()
    {
        var list = new List<string>();
        bool wired = false, wifi = false;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            bool isWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            bool isWired = ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT;
            if (!isWifi && !isWired) continue;
            if (ni.GetIPProperties().GatewayAddresses.Count == 0) continue; // virtual / host-only adapters
            list.Add($"{(isWifi ? "Wi-Fi" : "wired")} ({ni.Name})");
            wifi |= isWifi;
            wired |= isWired;
        }
        return (list, wired, wifi);
    }

    private static AccountKind ReadAccount()
    {
        using var id = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return AccountKind.Admin; // elevated, or UAC off
        if (GetTokenInformation(id.Token, TokenElevationType, out int type, sizeof(int), out _) && type == 3)
            return AccountKind.Admin; // admin account running with a filtered (non-elevated) token
        return AccountKind.Standard;
    }

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); }
        catch (Exception ex) { Logger.Info($"Device detection step skipped: {ex.Message}"); return fallback; }
    }
}
