using RestMode.Core;
using RestMode.Elevation;

namespace RestMode.Util;

/// <summary>
/// `RestMode.exe --restore` and `RestMode.exe --uninstall`.
/// Both put every setting back. Uninstall also removes what RestMode added
/// to Windows (autostart entry, desktop shortcut, helper task) and optionally
/// its settings/log folder. RestMode has no installer: after this, delete the exe.
/// </summary>
internal static class Uninstaller
{
    public static async Task RestoreAllAsync(bool quiet)
    {
        Logger.Info("Manual restore requested (--restore / --uninstall).");
        Restorer.RestoreLocal(StateStore.Load());
        PowerPlanManager.CleanupOrphans();
        if (HelperClient.HasPendingRestore())
            await HelperClient.SyncAsync(false, SettingsStore.Current, allowPrompt: true);

        if (!quiet)
        {
            string msg = HelperClient.HasPendingRestore()
                ? "Power plan, volume and apps are restored.\n\nNetwork adapter / Fan Control settings still need admin approval: run \"RestMode.exe --restore\" again and accept the prompt."
                : "All settings are back to normal.";
            MessageBox.Show(msg, "RestMode", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public static async Task UninstallAsync()
    {
        var answer = MessageBox.Show(
            "Remove RestMode from this PC?\n\nThis restores your power plan and all other settings, and removes the autostart entry, the desktop shortcut and the admin helper task.",
            "Uninstall RestMode", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        await RestoreAllAsync(quiet: true);
        StartupManager.SetEnabled(false);
        ShortcutHelper.RemoveDesktopShortcut();
        bool taskRemoved = await HelperClient.RemoveTaskAsync();

        bool pending = HelperClient.HasPendingRestore();
        if (!pending &&
            MessageBox.Show("Also delete RestMode's settings and log files?\n" + AppPaths.DataDir, "Uninstall RestMode",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            Logger.Init(Path.Combine(Path.GetTempPath(), "restmode-uninstall.log"));
            try { Directory.Delete(AppPaths.DataDir, recursive: true); } catch { /* best effort */ }
        }

        string done = "RestMode has been removed. You can now delete RestMode.exe.";
        if (!taskRemoved) done += "\n\nThe \"RestMode Helper\" scheduled task could not be removed; delete it in Task Scheduler.";
        if (pending) done += "\n\nNetwork adapter / Fan Control settings were NOT restored (admin approval declined). Run \"RestMode.exe --restore\" before deleting the exe.";
        MessageBox.Show(done, "RestMode", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
