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

/// <summary>Laptops: what closing the lid does while RestMode is running.</summary>
public enum LidBehavior
{
    /// <summary>Closing the lid enters Rest Mode; opening it wakes. (Windows' lid action is set to "Do nothing" while RestMode runs, and put back exactly when it stops.)</summary>
    Rest,
    /// <summary>Windows default: RestMode doesn't touch the lid setting.</summary>
    Sleep,
    /// <summary>Closing the lid does nothing (lid action set to "Do nothing" while RestMode runs).</summary>
    DoNothing,
}

public enum AccentColor { Silver, Gold, Blue }

/// <summary>Color of the neon frame in the Rest animation.</summary>
public enum FrameColorChoice { Orange, White, Custom }

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

    // --- Lid (laptops) -----------------------------------------------------
    public LidBehavior LidAction { get; set; } = LidBehavior.Sleep;

    // --- "Downloads finished" detection ----------------------------------
    /// <summary>Network traffic (down + up) below this is treated as "idle".</summary>
    public int IdleThresholdKBps { get; set; } = 50;
    /// <summary>Disk traffic must also be below this (game updates unpack after downloading). 0 = ignore disk.</summary>
    public int DiskThresholdKBps { get; set; } = 2048;
    /// <summary>How long traffic must stay below the thresholds before the end action runs.</summary>
    public int IdleMinutes { get; set; } = 10;
    public EndAction EndAction { get; set; } = EndAction.Sleep;
    /// <summary>Apps to watch (e.g. steam, qbittorrent): Rest Mode ends when THEIR transfers stop. Empty = watch all traffic.</summary>
    public List<string> WatchApps { get; set; } = new();

    // --- Safety -------------------------------------------------------------
    /// <summary>On battery, run the end action at or below this charge (0 = off).</summary>
    public int LowBatteryPercent { get; set; } = 20;
    /// <summary>Lower CPU limit while running on battery.</summary>
    public bool StricterOnBattery { get; set; } = true;
    public bool OverheatProtection { get; set; } = true;
    /// <summary>Temperature limit in °C. 0 = automatic (desktop 80, gaming laptop 75, thin laptop / handheld 70).</summary>
    public int TempLimitC { get; set; } = 0;

    // --- Enter / wake -----------------------------------------------------
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+R";
    public bool MuteAudio { get; set; } = true;
    /// <summary>The mouse must move at least this far to wake (filters out sensor jitter). 0 = any movement.</summary>
    public int MouseMoveThresholdPx { get; set; } = 5;
    /// <summary>A button press on an XInput (Xbox / DS4Windows / Steam Input) controller also wakes.</summary>
    public bool WakeWithController { get; set; } = true;

    // --- Schedule ---------------------------------------------------------
    /// <summary>"HH:mm" to enter Rest Mode automatically every day, or empty.</summary>
    public string RestAt { get; set; } = "";
    /// <summary>"HH:mm" to wake from Rest Mode (or from the end-action sleep) every day, or empty.</summary>
    public string WakeAt { get; set; } = "";

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

    // --- Look & feel ------------------------------------------------------
    public bool RestAnimation { get; set; } = true;
    /// <summary>Text shown in the middle of the Rest animation.</summary>
    public string OverlayText { get; set; } = "Resting…";
    /// <summary>Show the "Welcome back" screen (time rested, data downloaded) on wake.</summary>
    public bool WelcomeScreen { get; set; } = true;
    public FrameColorChoice FrameColor { get; set; } = FrameColorChoice.Orange;
    /// <summary>"#RRGGBB" used when FrameColor is Custom.</summary>
    public string CustomFrameColor { get; set; } = "#FF7A1A";
    public string WelcomeText { get; set; } = "Welcome back";
    public AccentColor Accent { get; set; } = AccentColor.Silver;
    /// <summary>Background ornament brightness, 10-100 %.</summary>
    public int OrnamentBrightness { get; set; } = 55;
    /// <summary>Animation speed multiplier (0.5 = slower, 2 = faster).</summary>
    public double AnimationSpeed { get; set; } = 1.0;
    public bool Sounds { get; set; } = true;

    // --- Misc -------------------------------------------------------------
    public bool ShowNotifications { get; set; } = true;
    public bool FirstRunDone { get; set; } = false;
    /// <summary>Features the startup self-check found broken on this PC (skipped until it passes again).</summary>
    public List<string> DisabledFeatures { get; set; } = new();

    private static readonly Regex ConfigNamePattern = new(@"^[A-Za-z0-9 _.\-]{1,100}\.json$", RegexOptions.Compiled);
    private static readonly Regex TimePattern = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    /// <summary>FanControl config names are passed to an elevated process, so only plain file names are allowed.</summary>
    public static bool IsSafeConfigName(string? name) => name != null && ConfigNamePattern.IsMatch(name) && !name.Contains("..");

    public static bool IsValidColor(string? c) => c != null && System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$");

    public static bool IsValidTime(string? t) => string.IsNullOrEmpty(t) || TimePattern.IsMatch(t);

    public bool FeatureEnabled(string id) => !DisabledFeatures.Contains(id, StringComparer.OrdinalIgnoreCase);

    public AppSettings Normalize()
    {
        CpuMaxPercent = Math.Clamp(CpuMaxPercent, 5, 100);
        CpuMinPercent = Math.Clamp(CpuMinPercent, 0, CpuMaxPercent);
        DiskIdleSeconds = Math.Clamp(DiskIdleSeconds, 0, 3600);
        IdleThresholdKBps = Math.Clamp(IdleThresholdKBps, 1, 1_000_000);
        DiskThresholdKBps = Math.Clamp(DiskThresholdKBps, 0, 10_000_000);
        IdleMinutes = Math.Clamp(IdleMinutes, 1, 24 * 60);
        LowBatteryPercent = Math.Clamp(LowBatteryPercent, 0, 90);
        TempLimitC = TempLimitC == 0 ? 0 : Math.Clamp(TempLimitC, 55, 95);
        MouseMoveThresholdPx = Math.Clamp(MouseMoveThresholdPx, 0, 200);
        OrnamentBrightness = Math.Clamp(OrnamentBrightness, 10, 100);
        AnimationSpeed = double.IsFinite(AnimationSpeed) ? Math.Clamp(AnimationSpeed, 0.25, 4) : 1;
        if (!Enum.IsDefined(EndAction)) EndAction = EndAction.Sleep;
        if (!Enum.IsDefined(LidAction)) LidAction = LidBehavior.Sleep;
        if (!Enum.IsDefined(Accent)) Accent = AccentColor.Silver;
        if (!Enum.IsDefined(FrameColor)) FrameColor = FrameColorChoice.Orange;
        if (!IsValidColor(CustomFrameColor)) CustomFrameColor = "#FF7A1A";
        if (!Enum.IsDefined(FanMode)) FanMode = FanStrategy.Auto;
        Hotkey ??= "";
        if (!IsValidTime(RestAt)) RestAt = "";
        if (!IsValidTime(WakeAt)) WakeAt = "";
        RestAt ??= "";
        WakeAt ??= "";
        OverlayText = string.IsNullOrWhiteSpace(OverlayText) || OverlayText == "Entering Rest Mode…" ? "Resting…" : OverlayText.Trim()[..Math.Min(80, OverlayText.Trim().Length)];
        WelcomeText = string.IsNullOrWhiteSpace(WelcomeText) ? "Welcome back" : WelcomeText.Trim()[..Math.Min(80, WelcomeText.Trim().Length)];
        Apps ??= new List<AppRule>();
        Apps = Apps.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name))
                   .Select(a => new AppRule { Name = a.Name.Trim(), Action = Enum.IsDefined(a.Action) ? a.Action : AppAction.LowPriority })
                   .ToList();
        WatchApps = (WatchApps ?? new()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        DisabledFeatures ??= new();
        FanControlExe ??= "";
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

    /// <summary>Raised after Save (on the caller's thread).</summary>
    public static event Action? Saved;

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
        Saved?.Invoke();
    }

    /// <summary>Settings > Export.</summary>
    public static void Export(string path) => JsonFile.Write(path, Current);

    /// <summary>Settings > Import. Returns false if the file isn't a RestMode settings file.</summary>
    public static bool Import(string path)
    {
        var s = JsonFile.Read<AppSettings>(path);
        if (s == null) return false;
        s.FirstRunDone = true;
        Save(s);
        return true;
    }

    /// <summary>Settings > Reset to defaults (keeps "first run done").</summary>
    public static void ResetToDefaults() => Save(new AppSettings { FirstRunDone = true });
}
