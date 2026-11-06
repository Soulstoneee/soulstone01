namespace RestMode.Elevation;

/// <summary>What the normal (non-admin) app asks the elevated helper to do.</summary>
public sealed class HelperRequest
{
    public string Id { get; set; } = "";
    /// <summary>true = apply Rest Mode changes, false = restore everything the helper changed.</summary>
    public bool WantActive { get; set; }
    public bool Adapters { get; set; }
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

    public bool HasPendingRestore => Adapters.Count > 0 || FanSwitched;
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
