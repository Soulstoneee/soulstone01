using Microsoft.Win32;

namespace RestMode.Util;

/// <summary>"Start with Windows" = a value under HKCU\...\Run (no admin needed).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RestMode";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled, string? exe = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{exe ?? AppPaths.ExePath}\" --startup");
            else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
        }
        catch (Exception ex)
        {
            Logger.Error("Changing start-with-Windows failed", ex);
        }
    }
}
