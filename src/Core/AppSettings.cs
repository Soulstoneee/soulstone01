using System.Text.RegularExpressions;
using RestMode.Util;

namespace RestMode.Core;

/// <summary>What to do once downloads look finished.</summary>
public enum EndAction
{
    Sleep,
    Hibernate,
    Shutdown,
    Stay,
}

/// <summary>What to do with a heavy background app while resting.</summary>
public enum AppAction
{
    /// <summary>Idle priority + Windows "Efficiency mode" (EcoQoS). Safe for almost anything.</summary>
    LowPriority,
    /// <summary>Freeze the process completely until you wake the PC (it is resumed afterwards).</summary>
    Pause,
}

/// <summary>How Rest Mode makes the fans quieter.</summary>
public enum FanStrategy
{
    /// <summary>Built-in manufacturer modes if found, else Fan Control if set up and running, else the Windows power mode.</summary>
    Auto,
    BuiltIn,
    FanControl,
    WindowsPowerMode,
    Off,
}

public sealed class AppRule
{
    public string Name { get; set; } = "";
    public AppAction Action { get; set; } = AppAction.LowPriority;
}

/// <summary>
/// User settings, stored as %LOCALAPPDATA%\RestMode\settings.json.
/// Every value is clamped to a sane range on load so a hand-edited file
/// cannot make RestMode do something dangerous (e.g. a 0% CPU cap).
/// </summary>
public sealed class AppSettings
{
    // --- Power plan -------------------------------------------------------
    /// <summary>Maximum processor state while resting (PS4 Rest Mode keeps the APU at a crawl).</summary>
    public int CpuMaxPercent { get; set; } = 25;
    /// <summary>Minimum processor state while resting.</summary>
    public int CpuMinPercent { get; set; } = 5;
    /// <summary>Hard disks spin down after this many idle seconds (active transfers keep them spinning).</summary>
    public int DiskIdleSeconds { get; set; } = 180;
    /// <summary>Laptops: keep resting when the lid is closed instead of sleeping.</summary>
    public bool AllowLidClosed { get; set; } = false;

    // --- "Downloads finished" detection ----------------------------------
    /// <summary>Network traffic (down + up) below this is treated as "idle".</summary>
    public int IdleThresholdKBps { get; set; } = 50;
    /// <summary>Disk traffic must also be below this (game updates unpack after downloading). 0 = ignore disk.</summary>
    public int DiskThresholdKBps { get; set; } = 2048;
    /// <summary>How long traffic must stay below the thresholds before the end action runs.</summary>
    public int IdleMinutes { get; set; } = 10;
    public EndAction EndAction { get; set; } = EndAction.Sleep;
    /// <summary>On battery, run the end action straight away at or below this charge (0 = off).</summary>
    public int LowBatteryPercent { get; set; } = 15;

    // --- Enter / wake -----------------------------------------------------
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+R";
    public bool MuteAudio { get; set; } = true;
    /// <summary>The mouse must move at least this far to wake (filters out sensor jitter). 0 = any movement.</summary>
    public int MouseMoveThresholdPx { get; set; } = 5;
    /// <summary>A button press on an XInput (Xbox / DS4Windows / Steam Input) controller also wakes.</summary>
    public bool WakeWithController { get; set; } = true;

    // --- Background apps --------------------------------------------------
    public bool ThrottleApps { get; set; } = false;
    public List<AppRule> Apps { get; set; } = new()
    {
        new AppRule { Name = "Spotify", Action = AppAction.LowPriority },
        new AppRule { Name = "Discord", Action = AppAction.LowPriority },
        new AppRule { Name = "ms-teams", Action = AppAction.LowPriority },
        new AppRule { Name = "slack", Action = AppAction.LowPriority },
    };

    // --- Admin features ---------------------------------------------------
    /// <summary>Untick "Allow the computer to turn off this device to save power" on network adapters while resting.</summary>
    public bool KeepNetworkAdaptersPowered { get; set; } = true;

    // --- Fans -------------------------------------------------------------
    public FanStrategy FanMode { get; set; } = FanStrategy.Auto;
    /// <summary>Look for the manufacturer's own Quiet/Balanced/Performance modes (gaming laptops).</summary>
    public bool UseBuiltInFanModes { get; set; } = true;
    /// <summary>Path to FanControl.exe or its folder. Empty = auto-detect.</summary>
    public string FanControlExe { get; set; } = "";
    public string FanQuietConfig { get; set; } = "Quiet.json";
    public string FanNormalConfig { get; set; } = "userConfig.json";

    // --- Misc -------------------------------------------------------------
    public bool ShowNotifications { get; set; } = true;

    private static readonly Regex ConfigNamePattern = new(@"^[A-Za-z0-9 _.\-]{1,100}\.json$", RegexOptions.Compiled);

    /// <summary>FanControl config names are passed to an elevated process, so only plain file names are allowed.</summary>
    public static bool IsSafeConfigName(string? name) => name != null && ConfigNamePattern.IsMatch(name) && !name.Contains("..");

    public AppSettings Normalize()
    {
        CpuMaxPercent = Math.Clamp(CpuMaxPercent, 5, 100);
        CpuMinPercent = Math.Clamp(CpuMinPercent, 0, CpuMaxPercent);
        DiskIdleSeconds = Math.Clamp(DiskIdleSeconds, 0, 3600);
        IdleThresholdKBps = Math.Clamp(IdleThresholdKBps, 1, 1_000_000);
        DiskThresholdKBps = Math.Clamp(DiskThresholdKBps, 0, 10_000_000);
        IdleMinutes = Math.Clamp(IdleMinutes, 1, 24 * 60);
        LowBatteryPercent = Math.Clamp(LowBatteryPercent, 0, 90);
        MouseMoveThresholdPx = Math.Clamp(MouseMoveThresholdPx, 0, 200);
        if (!Enum.IsDefined(EndAction)) EndAction = EndAction.Sleep;
        Hotkey ??= "";
        Apps ??= new List<AppRule>();
        Apps = Apps.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name))
                   .Select(a => new AppRule { Name = a.Name.Trim(), Action = Enum.IsDefined(a.Action) ? a.Action : AppAction.LowPriority })
                   .ToList();
        FanControlExe ??= "";
        if (!Enum.IsDefined(FanMode)) FanMode = FanStrategy.Auto;
        if (!IsSafeConfigName(FanQuietConfig)) FanQuietConfig = "Quiet.json";
        if (!IsSafeConfigName(FanNormalConfig)) FanNormalConfig = "userConfig.json";
        return this;
    }

    public AppSettings Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(this))!.Normalize();
}

/// <summary>Loads/saves <see cref="AppSettings"/>.</summary>
internal static class SettingsStore
{
    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    public static AppSettings Load()
    {
        var s = JsonFile.Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        _current = s.Normalize();
        return _current;
    }

    public static void Save(AppSettings settings)
    {
        _current = settings.Normalize();
        try { JsonFile.Write(AppPaths.SettingsFile, _current); }
        catch (Exception ex) { Logger.Error("Saving settings failed", ex); }
    }
}
