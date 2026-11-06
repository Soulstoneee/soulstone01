using RestMode.Util;

namespace RestMode.Core;

/// <summary>
/// Test-only overrides, used by the automated test runs (`RestMode.exe --simulate ...`).
/// Accepted ONLY when %LOCALAPPDATA%\RestMode\testmode.flag exists, so normal
/// users can't trigger them by accident. Commands:
///   lid-close | lid-open | temp=NN | temp=off | battery=NN | battery=off |
///   ac=on | ac=off | ac=real | drop-awake | hijack-plan | watchdog | reload-settings | enter | wake
/// </summary>
internal static class TestHooks
{
    public static double? Temperature { get; private set; }
    public static int? BatteryPercent { get; private set; }
    /// <summary>true = pretend plugged in, false = pretend on battery, null = real.</summary>
    public static bool? OnAc { get; private set; }

    public static bool Enabled => File.Exists(AppPaths.TestModeFlag);

    /// <summary>Automated tests: log "would sleep / shut down" instead of doing it (a sleeping PC can't wake itself up for the next test).</summary>
    public static bool NoSleep => Enabled && File.Exists(Path.Combine(AppPaths.DataDir, "testmode-nosleep.flag"));

    /// <summary>Applies value overrides; returns the command so the controller can act on events.</summary>
    public static string? ReadCommand()
    {
        if (!Enabled) return null;
        string cmd;
        try
        {
            if (!File.Exists(AppPaths.SimulateFile)) return null;
            cmd = File.ReadAllText(AppPaths.SimulateFile).Trim().ToLowerInvariant();
            File.Delete(AppPaths.SimulateFile);
        }
        catch { return null; }

        Logger.Info($"[TEST] simulate: {cmd}");
        if (cmd.StartsWith("temp=")) Temperature = cmd == "temp=off" ? null : double.TryParse(cmd[5..], out var t) ? t : null;
        else if (cmd.StartsWith("battery=")) BatteryPercent = cmd == "battery=off" ? null : int.TryParse(cmd[8..], out var b) ? b : null;
        else if (cmd == "ac=on") OnAc = true;
        else if (cmd == "ac=off") OnAc = false;
        else if (cmd == "ac=real") OnAc = null;
        return cmd;
    }

    public static void ClearAll()
    {
        Temperature = null;
        BatteryPercent = null;
        OnAc = null;
    }
}
