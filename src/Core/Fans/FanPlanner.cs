using RestMode.Elevation;

namespace RestMode.Core.Fans;

/// <summary>What Rest Mode will do with the fans this time, and why.</summary>
internal sealed class FanPlan
{
    public IBuiltInFanModes? BuiltIn { get; init; }
    public string? FanControlExe { get; init; }
    public bool WindowsPowerMode { get; init; }
    /// <summary>Human-readable reasons, logged at entry and shown in Settings.</summary>
    public List<string> Notes { get; init; } = new();

    public bool UseFanControl => FanControlExe != null;
    public bool NeedsAdmin => BuiltIn != null || UseFanControl;

    public string Describe() =>
        BuiltIn != null ? $"built-in {BuiltIn.Brand} Quiet mode"
        : UseFanControl ? "Fan Control quiet profile"
        : WindowsPowerMode ? "Windows power mode \"Best power efficiency\""
        : "fans left as they are";
}

/// <summary>
/// Picks the fan method for this PC from the user's priority setting and
/// what is actually available. Every option that isn't available is skipped
/// with a reason; the CPU limit and passive cooling in the Rest Mode power
/// plan always apply, so the CPU fan slows down on every PC regardless.
/// </summary>
internal static class FanPlanner
{
    /// <param name="sensorsAvailable">false = no temperature sensor works: quiet profiles are not used (safety).</param>
    public static FanPlan Resolve(AppSettings s, DeviceProfile dev, bool sensorsAvailable = true)
    {
        var notes = new List<string>();
        if (s.FanMode == FanStrategy.Off)
            return new FanPlan { Notes = { "Fan switching is off in Settings." } };

        bool allowQuiet = sensorsAvailable || !s.OverheatProtection;
        if (!allowQuiet)
            notes.Add("No temperature sensor works on this PC, so the fans are kept on their normal profile for safety (only the CPU limit and the Windows power mode are used).");

        // --- built-in manufacturer modes
        IBuiltInFanModes? builtIn = null;
        bool wantBuiltIn = allowQuiet && s.FeatureEnabled("builtin-fan") && s.FanMode is FanStrategy.Auto or FanStrategy.BuiltIn;
        if (wantBuiltIn && s.UseBuiltInFanModes)
        {
            var (p, msg) = BuiltInFanModes.Detect();
            notes.Add(msg);
            if (p is { Implemented: true })
            {
                if (dev.Account == AccountKind.Standard && !dev.Elevated)
                    notes.Add("Built-in fan modes need administrator rights, and this is a standard account - skipped.");
                else
                    builtIn = p;
            }
        }
        else if (wantBuiltIn)
        {
            notes.Add("Built-in fan modes are turned off in Settings.");
        }

        // --- Fan Control
        string? fanExe = null;
        bool wantFanControl = allowQuiet && s.FeatureEnabled("fancontrol") &&
                              (s.FanMode == FanStrategy.FanControl || (s.FanMode == FanStrategy.Auto && builtIn == null));
        if (wantFanControl)
        {
            string? exe = FanControlSwitcher.NormalizeExe(s.FanControlExe) ?? (string.IsNullOrWhiteSpace(s.FanControlExe) ? FanControlSwitcher.Detect() : null);
            if (exe == null)
                notes.Add(string.IsNullOrWhiteSpace(s.FanControlExe) ? "Fan Control not found." : $"Fan Control not found at {s.FanControlExe}.");
            else if (FanControlSwitcher.CheckRuntime(exe) is string rt)
                notes.Add(rt);
            else if (!FanControlSwitcher.IsRunning())
                notes.Add("Fan Control is installed but not running - RestMode won't start it.");
            else if (!FanControlSwitcher.ListConfigs(exe).Contains(s.FanQuietConfig, StringComparer.OrdinalIgnoreCase))
                notes.Add($"Fan Control config '{s.FanQuietConfig}' not found in its Configurations folder.");
            else if (dev.Account == AccountKind.Standard && !dev.Elevated)
                notes.Add("Fan Control switching needs administrator rights, and this is a standard account - skipped.");
            else
                fanExe = exe;
        }

        // --- Windows power mode (no admin needed, works on any PC)
        bool windows = s.FeatureEnabled("powermode") &&
                       (s.FanMode == FanStrategy.WindowsPowerMode || (s.FanMode == FanStrategy.Auto && builtIn == null && fanExe == null));

        if (s.FanMode == FanStrategy.BuiltIn && builtIn == null) notes.Add("No usable built-in fan modes - fans are left as they are.");
        if (s.FanMode == FanStrategy.FanControl && fanExe == null) notes.Add("Fan Control can't be used - fans are left as they are.");

        return new FanPlan { BuiltIn = builtIn, FanControlExe = fanExe, WindowsPowerMode = windows, Notes = notes };
    }
}
