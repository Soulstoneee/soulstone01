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

    private static bool TryAcquire(Mutex m, int timeoutMs)
    {
        try { return m.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; } // previous copy crashed; we own it now
    }
}
