namespace RestMode.Elevation;

/// <summary>What the normal (non-admin) app asks the elevated helper to do.</summary>
public sealed class HelperRequest
{
    public string Id { get; set; } = "";
    /// <summary>true = apply Rest Mode changes, false = restore everything the helper changed.</summary>
    public bool WantActive { get; set; }
    public bool Adapters { get; set; }
    /// <summary>Id of a built-in fan-mode plug-in (e.g. "lenovo-gamezone"), or null.</summary>
    public string? BuiltInFan { get; set; }
    public bool Fan { get; set; }
    public string FanQuietConfig { get; set; } = "";
    public string FanNormalConfig { get; set; } = "";
}

/// <summary>What the helper changed (so it can be undone, even after a crash). Owned by the helper.</summary>
public sealed class HelperState
{
    public List<AdapterSnapshot> Adapters { get; set; } = new();
    public bool FanSwitched { get; set; }
    public string? FanExe { get; set; }
    public string? FanNormalConfig { get; set; }
    /// <summary>Built-in fan-mode plug-in that was switched to Quiet, and the mode to restore.</summary>
    public string? BuiltInId { get; set; }
    public int? BuiltInSavedMode { get; set; }

    public bool HasPendingRestore => Adapters.Count > 0 || FanSwitched || BuiltInId != null;
}

public sealed class AdapterSnapshot
{
    public string InstanceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Value of "Allow the computer to turn off this device to save power" before Rest Mode.</summary>
    public bool OriginalEnable { get; set; }
}

public sealed class HelperResult
{
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public List<string> Messages { get; set; } = new();
}

/// <summary>The admin-only work for one Rest Mode session (built by the non-admin app).</summary>
internal sealed class HelperWork
{
    public bool Adapters { get; init; }
    public string? BuiltInId { get; init; }
    /// <summary>FanControl.exe to use (passed on the elevated command line, never via the request file).</summary>
    public string? FanExe { get; init; }
    public string FanQuiet { get; init; } = "";
    public string FanNormal { get; init; } = "";

    public bool Any => Adapters || BuiltInId != null || FanExe != null;

    /// <summary>For restoring: no new changes, but tell the helper which FanControl.exe it may re-launch.</summary>
    public static HelperWork ForRestore(Core.AppSettings s) => new()
    {
        FanExe = FanControlSwitcher.NormalizeExe(s.FanControlExe) ?? (string.IsNullOrWhiteSpace(s.FanControlExe) ? FanControlSwitcher.Detect() : null),
        FanNormal = s.FanNormalConfig,
    };
}
