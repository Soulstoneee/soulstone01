using System.Runtime.InteropServices;

namespace RestMode.Util;

/// <summary>Desktop / Start menu shortcuts (.lnk files).</summary>
internal static class ShortcutHelper
{
    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RestMode – Rest now.lnk");

    /// <summary>The v0.x desktop shortcut name, removed on uninstall too.</summary>
    public static string OldDesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Enter Rest Mode.lnk");

    public static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "RestMode.lnk");

    public static string StartMenuRestShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "RestMode – Rest now.lnk");

    /// <summary>"Rest now" desktop shortcut (RestMode.exe --enter).</summary>
    public static bool CreateDesktopShortcut(string? exe = null) =>
        Create(DesktopShortcutPath, exe ?? AppPaths.ExePath, "--enter", "Put the PC into Rest Mode now", (exe ?? AppPaths.ExePath) + ",0");

    /// <summary>Start menu "RestMode – Rest now" (RestMode.exe --enter).</summary>
    public static bool CreateStartMenuRestShortcut(string? exe = null) =>
        Create(StartMenuRestShortcutPath, exe ?? AppPaths.ExePath, "--enter", "Put the PC into Rest Mode now", (exe ?? AppPaths.ExePath) + ",0");

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

    public static void RemoveDesktopShortcut()
    {
        Delete(DesktopShortcutPath);
        Delete(OldDesktopShortcutPath);
    }

    public static void RemoveStartMenuShortcut()
    {
        Delete(StartMenuShortcutPath);
        Delete(StartMenuRestShortcutPath);
    }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Logger.Warn($"Could not delete {path}: {ex.Message}"); }
    }
}
