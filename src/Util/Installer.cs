using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace RestMode.Util;

/// <summary>
/// Optional per-user install. RestMode.exe is portable and works from any
/// folder without this. `RestMode.exe --install` (or Settings > General >
/// Install) copies it to %LOCALAPPDATA%\Programs\RestMode, adds a Start menu
/// shortcut and an entry in Settings > Apps (uninstall runs --uninstall).
/// No admin rights are needed; nothing is written outside the user's profile.
/// </summary>
internal static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RestMode";

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RestMode");

    public static string InstalledExe => Path.Combine(InstallDir, "RestMode.exe");

    public static bool RunningInstalledCopy =>
        string.Equals(Path.GetFullPath(AppPaths.ExePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return k != null && File.Exists(InstalledExe);
        }
        catch { return false; }
    }

    /// <summary>Copies the running exe into place and registers it. Returns an error message, or null on success.</summary>
    public static string? Install()
    {
        try
        {
            string src = AppPaths.ExePath;
            if (File.Exists(Path.ChangeExtension(src, ".dll")))
                return "Only the single-file RestMode.exe (from build.cmd) can be installed.";

            Directory.CreateDirectory(InstallDir);
            if (!RunningInstalledCopy)
                File.Copy(src, InstalledExe, overwrite: true);

            ShortcutHelper.CreateStartMenuShortcut(InstalledExe);
            if (File.Exists(ShortcutHelper.DesktopShortcutPath)) ShortcutHelper.CreateDesktopShortcut(InstalledExe);
            if (StartupManager.IsEnabled()) StartupManager.SetEnabled(true, InstalledExe);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
                k.SetValue("DisplayName", "RestMode");
                k.SetValue("DisplayVersion", version);
                k.SetValue("Publisher", "RestMode");
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
            Logger.Info($"Installed for this user: {InstalledExe}");
            return null;
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020)) // sharing violation
        {
            return "The installed copy is running. Close RestMode (tray icon > Exit) and try again.";
        }
        catch (Exception ex)
        {
            Logger.Error("Install failed", ex);
            return ex.Message;
        }
    }

    /// <summary>Removes the Start menu shortcut and the Apps-list entry.</summary>
    public static void RemoveRegistration()
    {
        ShortcutHelper.RemoveStartMenuShortcut();
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); }
        catch (Exception ex) { Logger.Warn($"Could not remove the Apps entry: {ex.Message}"); }
    }

    /// <summary>If this is the installed copy, deletes it (and its folder) a few seconds after RestMode exits.</summary>
    public static bool ScheduleSelfDelete()
    {
        if (!RunningInstalledCopy) return false;
        try
        {
            string cmd = $"/c ping 127.0.0.1 -n 5 >nul & del /f /q \"{InstalledExe}\" & rmdir \"{InstallDir}\"";
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd)
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not schedule deleting {InstalledExe}: {ex.Message}");
            return false;
        }
    }
}
