using System.Windows.Forms;
namespace RestMode.Util;

/// <summary>
/// All files RestMode writes live in one folder: %LOCALAPPDATA%\RestMode.
/// Keeping the saved "before Rest Mode" state on disk is what makes crash
/// recovery possible: if the app dies, the next launch reads state.json and
/// puts everything back.
/// </summary>
internal static class AppPaths
{
    public static string DataDir { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RestMode");

    /// <summary>The elevated helper is told which folder to use on its command line.</summary>
    public static void UseDataDir(string dir) => DataDir = dir;

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static string LogFile => Path.Combine(DataDir, "restmode.log");
    public static string HelperLogFile => Path.Combine(DataDir, "helper.log");
    public static string HelperRequestFile => Path.Combine(DataDir, "helper-request.json");
    public static string HelperStateFile => Path.Combine(DataDir, "helper-state.json");
    public static string HelperResultFile => Path.Combine(DataDir, "helper-result.json");
    /// <summary>Original lid-close values of the user's plans while "Lid = Rest / Do nothing" is active.</summary>
    public static string LidBackupFile => Path.Combine(DataDir, "lid-backup.json");
    public static string StatsFile => Path.Combine(DataDir, "stats.json");
    public static string SelfCheckFile => Path.Combine(DataDir, "selfcheck.json");
    /// <summary>Only when this file exists does RestMode accept `--simulate` test commands.</summary>
    public static string TestModeFlag => Path.Combine(DataDir, "testmode.flag");
    public static string SimulateFile => Path.Combine(DataDir, "simulate.txt");
    public static string ResetScript => Path.Combine(DataDir, "RestMode-Reset.cmd");

    /// <summary>Full path of the running RestMode.exe (works for single-file publish).</summary>
    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);
}
