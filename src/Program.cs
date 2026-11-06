using RestMode.Elevation;
using RestMode.UI;
using RestMode.Util;

namespace RestMode;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var cmd = CommandLine.Parse(args);

        // Elevated helper: no UI, no single-instance lock.
        if (cmd.HelperVerb != null)
            return HelperMain.Run(cmd);

        try { AppPaths.EnsureDataDir(); } catch { /* logging just won't work */ }
        Logger.Init(AppPaths.LogFile);
        ApplicationConfiguration.Initialize();

        if (cmd.Install) return RunInstall();

        using var mutex = new Mutex(false, SingleInstance.MutexName);
        bool owned = TryAcquire(mutex, 0);
        if (!owned)
        {
            if (cmd.Uninstall)
            {
                // Ask the running copy to wake/restore and quit, then continue.
                SingleInstance.Signal(SingleInstance.ExitEvent);
                owned = TryAcquire(mutex, 20_000);
                if (!owned)
                {
                    MessageBox.Show("Please close RestMode (tray icon > Exit) and try again.", "RestMode");
                    return 1;
                }
            }
            else
            {
                // Hand the request to the running copy.
                SingleInstance.Signal(cmd.Enter ? SingleInstance.EnterEvent
                    : cmd.Restore ? SingleInstance.RestoreEvent
                    : SingleInstance.ShowEvent);
                return 0;
            }
        }

        try
        {
            if (cmd.Uninstall) { Uninstaller.UninstallAsync().GetAwaiter().GetResult(); return 0; }
            if (cmd.Restore) { Uninstaller.RestoreAllAsync(quiet: false).GetAwaiter().GetResult(); return 0; }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => TrayApp.OnProcessCrash(e.ExceptionObject as Exception);

            using var app = new TrayApp(cmd);
            Application.ThreadException += (_, e) => app.OnFatal(e.Exception);
            Application.Run(app);
            return 0;
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    /// <summary>`--install`: per-user install, then hand over from a running (portable) copy to the installed one.</summary>
    private static int RunInstall()
    {
        string? error = Installer.Install();
        if (error != null)
        {
            MessageBox.Show("RestMode could not be installed:\n" + error, "RestMode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        if (!Installer.RunningInstalledCopy)
        {
            // Close a copy that is running from the old (portable) location, then start the installed one.
            using var m = new Mutex(false, SingleInstance.MutexName);
            bool free = TryAcquire(m, 0);
            if (!free)
            {
                SingleInstance.Signal(SingleInstance.ExitEvent);
                free = TryAcquire(m, 20_000);
            }
            if (free) m.ReleaseMutex();
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Error("Starting the installed copy failed", ex); }
        }
        MessageBox.Show($"RestMode is installed for this user:\n{Installer.InstalledExe}\n\n" +
                        "It is in the Start menu and in Settings > Apps (uninstall it from there). " +
                        "The portable RestMode.exe you started this from is no longer needed.",
            "RestMode", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    private static bool TryAcquire(Mutex m, int timeoutMs)
    {
        try { return m.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; } // previous copy crashed; we own it now
    }
}
