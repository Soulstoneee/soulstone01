using System.Runtime.InteropServices;

namespace RestMode.Util;

/// <summary>Desktop / Start menu shortcuts (.lnk files).</summary>
internal static class ShortcutHelper
{
    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Enter Rest Mode.lnk");

    public static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "RestMode.lnk");

    /// <summary>"Enter Rest Mode" desktop shortcut (RestMode.exe --enter).</summary>
    public static bool CreateDesktopShortcut(string? exe = null) =>
        Create(DesktopShortcutPath, exe ?? AppPaths.ExePath, "--enter", "Put the PC into PS4-style Rest Mode",
            Path.Combine(Environment.SystemDirectory, "shell32.dll") + ",27"); // power icon

    /// <summary>Start menu entry that starts RestMode in the tray.</summary>
    public static bool CreateStartMenuShortcut(string exe) =>
        Create(StartMenuShortcutPath, exe, "", "RestMode - PS4-style Rest Mode for Windows", exe + ",0");

    private static bool Create(string path, string target, string args, string description, string icon)
    {
        object? shell = null, link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell missing");
            shell = Activator.CreateInstance(type)!;
            dynamic sh = shell;
            link = sh.CreateShortcut(path);
            dynamic lnk = link!;
            lnk.TargetPath = target;
            lnk.Arguments = args;
            lnk.WorkingDirectory = Path.GetDirectoryName(target);
            lnk.Description = description;
            lnk.IconLocation = icon;
            lnk.Save();
            Logger.Info($"Shortcut created: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Creating the shortcut {Path.GetFileName(path)} failed", ex);
            return false;
        }
        finally
        {
            if (link != null) Marshal.FinalReleaseComObject(link);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }

    public static void RemoveDesktopShortcut() => Delete(DesktopShortcutPath);

    public static void RemoveStartMenuShortcut() => Delete(StartMenuShortcutPath);

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Logger.Warn($"Could not delete {path}: {ex.Message}"); }
    }
}
