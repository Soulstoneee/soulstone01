using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using RestMode.Core;
using RestMode.Util;

namespace RestMode.Elevation;

/// <summary>
/// Used by the normal (non-admin) app to get admin-only work done:
///  1. If RestMode already runs as admin: do it in-process.
///  2. Else if the "RestMode Helper" task is installed: start it (no prompt).
///  3. Else, if allowed, start an elevated copy of RestMode.exe (one UAC prompt).
///     Standard (non-admin) accounts are never prompted: the step is skipped.
///  4. Else skip; the restore is retried later (helper-state.json remembers it).
/// </summary>
internal static class HelperClient
{
    public static bool IsElevated => HelperMain.IsElevated();

    public static HelperState? LoadHelperState() => JsonFile.Read<HelperState>(AppPaths.HelperStateFile);

    public static bool HasPendingRestore() => LoadHelperState()?.HasPendingRestore == true;

    /// <summary>Restore everything the helper changed.</summary>
    public static Task<HelperResult?> SyncAsync(bool wantActive, AppSettings s, bool allowPrompt) =>
        RunAsync(wantActive, wantActive ? new HelperWork() : HelperWork.ForRestore(s), s, allowPrompt);

    /// <returns>The helper's result, or null if it did not run (declined, not allowed, timed out).</returns>
    public static async Task<HelperResult?> RunAsync(bool wantActive, HelperWork work, AppSettings s, bool allowPrompt)
    {
        var req = new HelperRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            WantActive = wantActive,
            Adapters = work.Adapters,
            BuiltInFan = work.BuiltInId,
            Fan = work.FanExe != null,
            FanQuietConfig = work.FanQuiet.Length > 0 ? work.FanQuiet : s.FanQuietConfig,
            FanNormalConfig = work.FanNormal.Length > 0 ? work.FanNormal : s.FanNormalConfig,
            SensorsOnly = work.SensorsOnly,
            FansOnly = work.FansOnly,
        };
        try { JsonFile.Write(AppPaths.HelperRequestFile, req); }
        catch (Exception ex) { Logger.Error("Could not write helper request", ex); return null; }

        try
        {
            if (IsElevated)
            {
                await Task.Run(() => HelperMain.Sync(work.FanExe));
                return ReadResult(req.Id);
            }

            if (TaskInstaller.IsInstalled() && TaskInstaller.RunNow())
            {
                var r = await WaitForResultAsync(req.Id, TimeSpan.FromSeconds(30));
                if (r != null) return r;
                Logger.Warn("Helper task did not answer in time (moved RestMode.exe? reinstall the helper in Settings).");
            }

            if (!allowPrompt)
            {
                Logger.Info("Admin helper needed but no prompt allowed right now - will retry later.");
                return null;
            }
            if (IsStandardAccount())
            {
                Logger.Info("This is a standard (non-administrator) account: admin-only steps are skipped instead of asking for an administrator password.");
                return null;
            }

            string args = $"--helper sync --data {Quote(AppPaths.DataDir)}";
            if (work.FanExe != null) args += $" --fan {Quote(work.FanExe)}";
            // Entering: the user just pressed the hotkey, so 60 s is plenty. Waking: allow a bit longer.
            var timeout = TimeSpan.FromSeconds(wantActive ? 60 : 120);
            int? exit = await RunElevatedAsync(args, timeout, onTimeout: () =>
            {
                // Nobody answered. If someone clicks "Yes" later, the helper must not apply
                // Rest Mode changes any more: turn the waiting request into a harmless restore.
                if (wantActive)
                {
                    try { JsonFile.Write(AppPaths.HelperRequestFile, new HelperRequest { Id = req.Id, WantActive = false }); }
                    catch (Exception ex) { Logger.Warn($"Could not cancel the pending admin request: {ex.Message}"); }
                }
            });
            return exit == null ? null : ReadResult(req.Id);
        }
        catch (Exception ex)
        {
            Logger.Error("Admin helper failed", ex);
            return null;
        }
    }

    /// <summary>True when admin work can run silently (RestMode elevated, or the helper task installed).</summary>
    public static bool CanRunSilently => IsElevated || TaskInstaller.IsInstalled();

    /// <summary>Vendor sensors via the helper - only when that needs no prompt. Null otherwise.</summary>
    public static async Task<Core.Safety.SensorReading?> ReadVendorSensorsAsync(string providerId, AppSettings s)
    {
        if (!CanRunSilently) return null;
        var r = await RunAsync(false, new HelperWork { BuiltInId = providerId, SensorsOnly = true }, s, allowPrompt: false);
        return r?.Sensors;
    }

    /// <summary>Overheat protection: put the fans back to normal, keep everything else in Rest Mode.</summary>
    public static Task<HelperResult?> RestoreFansAsync(AppSettings s, bool allowPrompt)
    {
        var w = HelperWork.ForRestore(s);
        return RunAsync(false, new HelperWork { FanExe = w.FanExe, FanNormal = w.FanNormal, FansOnly = true }, s, allowPrompt);
    }

    /// <summary>Settings "Test" button: Fan Control to Quiet for 10 seconds, then back to Normal.</summary>
    public static async Task<string> TestFanControlAsync(AppSettings s, string exe)
    {
        var work = new HelperWork { FanExe = exe, FanQuiet = s.FanQuietConfig, FanNormal = s.FanNormalConfig };
        var on = await RunAsync(true, work, s, allowPrompt: true);
        if (on == null) return "The test did not run (admin approval declined or not available).";
        string first = string.Join("\n", on.Messages);
        if (!(LoadHelperState()?.FanSwitched ?? false)) return "Fan Control was not switched:\n" + first;
        await Task.Delay(TimeSpan.FromSeconds(10));
        var off = await RunAsync(false, work, s, allowPrompt: true);
        return first + "\n" + (off != null ? string.Join("\n", off.Messages) : "Switching back did not run - use \"Restore everything now\".");
    }

    public static bool IsStandardAccount()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return false;
            return !(Native.NativeMethods.GetTokenInformation(id.Token, Native.NativeMethods.TokenElevationType, out int t, sizeof(int), out _) && t == 3);
        }
        catch { return false; }
    }

    /// <summary>
    /// Starts RestMode.exe elevated (UAC prompt). Returns exit code, or null if declined/timed out.
    /// Process.Start with "runas" blocks until the user answers the UAC prompt, so it runs on a
    /// worker thread: the UI thread (hotkey, wake detection, power events) must never wait on it.
    /// The timeout covers the time the prompt is waiting for an answer too.
    /// </summary>
    public static async Task<int?> RunElevatedAsync(string args, TimeSpan timeout, Action? onTimeout = null)
    {
        var psi = new ProcessStartInfo(AppPaths.ExePath, args) { UseShellExecute = true, Verb = "runas" };
        var run = Task.Run<int?>(() =>
        {
            try
            {
                using var p = Process.Start(psi);
                if (p == null) return null;
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Logger.Info("UAC prompt was declined; admin-only steps skipped.");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error("Starting the admin helper failed", ex);
                return null;
            }
        });

        if (await Task.WhenAny(run, Task.Delay(timeout)) != run)
        {
            Logger.Warn($"No answer to the admin prompt within {timeout.TotalSeconds:0} s - continuing without the admin-only steps.");
            onTimeout?.Invoke();
            return null;
        }
        return await run;
    }

    public static async Task<bool> InstallTaskAsync(AppSettings s)
    {
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string args = $"--helper install-task --data {Quote(AppPaths.DataDir)} --user-sid {sid}";
        string? fan = HelperWork.ForRestore(s).FanExe;
        if (fan != null) args += $" --fan {Quote(fan)}";
        if (IsElevated) return TaskInstaller.Install(sid, fan);
        return await RunElevatedAsync(args, TimeSpan.FromSeconds(120)) == 0;
    }

    public static async Task<bool> RemoveTaskAsync()
    {
        if (!TaskInstaller.IsInstalled()) return true;
        if (IsElevated) return TaskInstaller.Remove();
        return await RunElevatedAsync($"--helper remove-task --data {Quote(AppPaths.DataDir)}", TimeSpan.FromSeconds(120)) == 0;
    }

    private static async Task<HelperResult?> WaitForResultAsync(string id, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var r = ReadResult(id);
            if (r != null) return r;
            await Task.Delay(300);
        }
        return null;
    }

    private static HelperResult? ReadResult(string id)
    {
        var r = JsonFile.Read<HelperResult>(AppPaths.HelperResultFile);
        return r != null && r.Id == id ? r : null;
    }

    private static string Quote(string s) => "\"" + s.TrimEnd('\\') + "\"";
}
