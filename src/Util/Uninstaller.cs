using System.Windows.Forms;
using RestMode.Core;
using RestMode.Elevation;

namespace RestMode.Util;

/// <summary>
/// `RestMode.exe --restore` and `RestMode.exe --uninstall` (both accept --quiet).
/// Both put every setting back. Uninstall also removes what RestMode added
/// to Windows (autostart entry, shortcuts, Apps-list entry, helper task, lid
/// setting) and its settings/log folder (asked first unless --quiet).
/// </summary>
internal static class Uninstaller
{
    public static async Task RestoreAllAsync(bool quiet)
    {
        Logger.Info("Manual restore requested (--restore / --uninstall).");
        Restorer.RestoreLocal(StateStore.Load());
        PowerPlanManager.CleanupOrphans();
        if (HelperClient.HasPendingRestore())
            await HelperClient.SyncAsync(false, SettingsStore.Current, allowPrompt: !quiet || HelperClient.CanRunSilently);

        if (!quiet)
        {
            string msg = HelperClient.HasPendingRestore()
                ? "Power plan, volume and apps are restored.\n\nNetwork adapter / fan settings still need admin approval: run \"RestMode.exe --restore\" again and accept the prompt."
                : "All settings are back to normal.";
            MessageBox.Show(msg, "RestMode", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public static async Task UninstallAsync(bool quiet)
    {
        if (!quiet)
        {
            var answer = MessageBox.Show(
                "Remove RestMode from this PC?\n\nThis restores your power plan, lid setting, power mode, fan mode and all other settings, and removes the autostart entry, the shortcuts, the Apps-list entry and the admin helper task.",
                "Uninstall RestMode", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
        }

        await RestoreAllAsync(quiet: true);
        LidManager.RestoreAll();
        StartupManager.SetEnabled(false);
        ShortcutHelper.RemoveDesktopShortcut();
        Installer.RemoveRegistration();
        bool taskRemoved = !TaskInstaller.IsInstalled() || await HelperClient.RemoveTaskAsync();

        bool pending = HelperClient.HasPendingRestore() || LidManager.HasBackup;
        bool deleteData = !pending && (quiet ||
            MessageBox.Show("Also delete RestMode's settings, statistics, backups and logs?\n" + AppPaths.DataDir, "Uninstall RestMode",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
        if (deleteData)
        {
            Logger.Init(Path.Combine(Path.GetTempPath(), "restmode-uninstall.log"));
            try { Directory.Delete(AppPaths.DataDir, recursive: true); } catch { /* best effort */ }
        }

        bool selfDelete = Installer.ScheduleSelfDelete();
        if (quiet) return;
        string done = selfDelete ? "RestMode has been removed." : "RestMode has been removed. You can now delete RestMode.exe.";
        if (!taskRemoved) done += "\n\nThe \"RestMode Helper\" scheduled task could not be removed; delete it in Task Scheduler.";
        if (pending) done += "\n\nSome settings were NOT restored yet (admin approval declined). Run \"RestMode.exe --restore\" or RestMode-Reset.cmd before deleting the exe.";
        MessageBox.Show(done, "RestMode", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
