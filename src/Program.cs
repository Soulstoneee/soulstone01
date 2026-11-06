using System.Windows.Forms;
using RestMode.Elevation;
using RestMode.UI;
using RestMode.UI.Wpf;
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
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        // Test runs only: hand a --simulate command to the running copy.
        if (cmd.Simulate != null)
        {
            if (!File.Exists(AppPaths.TestModeFlag)) return 2;
            File.WriteAllText(AppPaths.SimulateFile, cmd.Simulate);
            return SingleInstance.Signal(SingleInstance.SimulateEvent) ? 0 : 1;
        }

        // Build / packaging: write RestMode-Reset.cmd into a folder.
        int wr = Array.FindIndex(args, a => a.Equals("--write-reset-script", StringComparison.OrdinalIgnoreCase));
        if (wr >= 0 && wr + 1 < args.Length) { ResetScript.WriteTo(args[wr + 1]); return 0; }

        // Silent install (scripts / tests): RestMode-Setup.exe --install --quiet
        if (cmd.Install && cmd.Quiet)
            return Installer.Install() == null ? 0 : 1;

        bool setupMode = cmd.Install ||
                         (Path.GetFileNameWithoutExtension(AppPaths.ExePath).Contains("setup", StringComparison.OrdinalIgnoreCase) && !Installer.RunningInstalledCopy && !cmd.Uninstall && !cmd.Restore);

        using var mutex = new Mutex(false, SingleInstance.MutexName);
        bool owned = false;
        if (!setupMode)
        {
            owned = TryAcquire(mutex, 0);
            if (!owned)
            {
                if (cmd.Uninstall || cmd.Restore)
                {
                    // Ask the running copy to wake/restore and quit, then continue.
                    SingleInstance.Signal(SingleInstance.ExitEvent);
                    owned = TryAcquire(mutex, 20_000);
                    if (!owned)
                    {
                        if (!cmd.Quiet) MessageBox.Show("Please close RestMode (tray icon > Exit) and try again.", "RestMode");
                        return 1;
                    }
                }
                else
                {
                    // Hand the request to the running copy.
                    SingleInstance.Signal(cmd.Enter ? SingleInstance.EnterEvent
                        : cmd.Wake ? SingleInstance.WakeEvent
                        : SingleInstance.ShowEvent);
                    return 0;
                }
            }
        }

        try
        {
            if (cmd.Uninstall) { Uninstaller.UninstallAsync(cmd.Quiet).GetAwaiter().GetResult(); return 0; }
            if (cmd.Restore)
            {
                Uninstaller.RestoreAllAsync(cmd.Quiet).GetAwaiter().GetResult();
                try { Core.LidManager.RestoreAll(); } catch { /* best effort */ }
                return 0;
            }

            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            ThemeManager.Init();

            // Developer / test tools (test-mode flag file required).
            if (File.Exists(AppPaths.TestModeFlag))
            {
                int i = Array.FindIndex(args, a => a.Equals("--render-ui", StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && i + 1 < args.Length) return DevTools.RenderUi(args[i + 1]);
                i = Array.FindIndex(args, a => a.Equals("--render-icon", StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && i + 1 < args.Length) return DevTools.RenderIcon(args[i + 1]);
            }

            if (setupMode)
            {
                var setup = new SetupWindow();
                setup.Closed += (_, _) => app.Shutdown();
                setup.Show();
                app.Run();
                return 0;
            }

            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => TrayApp.OnProcessCrash(e.ExceptionObject as Exception);

            var tray = new TrayApp(cmd);
            app.DispatcherUnhandledException += (_, e) => { tray.OnFatal(e.Exception); e.Handled = true; };
            System.Windows.Forms.Application.ThreadException += (_, e) => tray.OnFatal(e.Exception);
            app.Run();
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
