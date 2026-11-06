using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core.Fans;

/// <summary>
/// The Windows "Power mode" slider (Settings > System > Power & battery):
/// Best power efficiency / Balanced / Best performance. Works on any Windows
/// 10/11 PC without admin rights. Used as the universal fallback: in Rest Mode
/// it is set to "Best power efficiency" (less heat, so fans that follow the
/// CPU temperature slow down) and put back on wake.
/// </summary>
internal static class WindowsPowerMode
{
    public static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid BetterPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid? Get()
    {
        try { return PowerGetEffectiveOverlayScheme(out Guid g) == 0 ? g : null; }
        catch (Exception ex) { Logger.Info($"Windows power mode not available: {ex.Message}"); return null; }
    }

    public static bool Set(Guid mode)
    {
        try
        {
            uint err = PowerSetActiveOverlayScheme(ref mode);
            if (err != 0) Logger.Info($"Windows power mode could not be set (error {err}).");
            return err == 0;
        }
        catch (Exception ex)
        {
            Logger.Info($"Windows power mode not available: {ex.Message}");
            return false;
        }
    }

    public static string Name(Guid? g) =>
        g == null ? "unknown"
        : g == Guid.Empty ? "Balanced"
        : g == BestEfficiency ? "Best power efficiency"
        : g == BetterPerformance ? "Better performance"
        : g == BestPerformance ? "Best performance"
        : g.ToString()!;
}
