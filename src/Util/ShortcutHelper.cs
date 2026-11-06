using System.Runtime.InteropServices;

namespace RestMode.Util;

/// <summary>Creates the "Enter Rest Mode" desktop shortcut (RestMode.exe --enter).</summary>
internal static class ShortcutHelper
{
    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Enter Rest Mode.lnk");

    public static bool CreateDesktopShortcut()
    {
        object? shell = null, link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell missing");
            shell = Activator.CreateInstance(type)!;
            dynamic sh = shell;
            link = sh.CreateShortcut(DesktopShortcutPath);
            dynamic lnk = link!;
            lnk.TargetPath = AppPaths.ExePath;
            lnk.Arguments = "--enter";
            lnk.WorkingDirectory = Path.GetDirectoryName(AppPaths.ExePath);
            lnk.Description = "Put the PC into PS4-style Rest Mode";
            lnk.IconLocation = Path.Combine(Environment.SystemDirectory, "shell32.dll") + ",27"; // power icon
            lnk.Save();
            Logger.Info("Desktop shortcut created.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Creating the desktop shortcut failed", ex);
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
        try { if (File.Exists(DesktopShortcutPath)) File.Delete(DesktopShortcutPath); }
        catch (Exception ex) { Logger.Warn($"Could not delete the desktop shortcut: {ex.Message}"); }
    }
}
