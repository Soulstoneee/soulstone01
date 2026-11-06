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
///  4. Else skip; the restore is retried later (helper-state.json remembers it).
/// </summary>
internal static class HelperClient
{
    public static bool IsElevated => HelperMain.IsElevated();

    public static bool NeedsHelper(AppSettings s) =>
        s.KeepNetworkAdaptersPowered || (s.FanControlEnabled && ResolveFanExe(s) != null);

    public static string? ResolveFanExe(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.FanControlExe) && File.Exists(s.FanControlExe)) return s.FanControlExe;
        return FanControlSwitcher.Detect();
    }

    public static HelperState? LoadHelperState() => JsonFile.Read<HelperState>(AppPaths.HelperStateFile);

    public static bool HasPendingRestore() => LoadHelperState()?.HasPendingRestore == true;

    /// <returns>The helper's result, or null if it did not run (declined, not allowed, timed out).</returns>
    public static async Task<HelperResult?> SyncAsync(bool wantActive, AppSettings s, bool allowPrompt)
    {
        string? fanExe = ResolveFanExe(s);
        var req = new HelperRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            WantActive = wantActive,
            Adapters = s.KeepNetworkAdaptersPowered,
            Fan = s.FanControlEnabled && fanExe != null,
            FanQuietConfig = s.FanQuietConfig,
            FanNormalConfig = s.FanNormalConfig,
        };
        try { JsonFile.Write(AppPaths.HelperRequestFile, req); }
        catch (Exception ex) { Logger.Error("Could not write helper request", ex); return null; }

        try
        {
            if (IsElevated)
            {
                await Task.Run(() => HelperMain.Sync(fanExe));
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

            string args = $"--helper sync --data {Quote(AppPaths.DataDir)}";
            if (fanExe != null) args += $" --fan {Quote(fanExe)}";
            int? exit = await RunElevatedAsync(args, TimeSpan.FromSeconds(120));
            return exit == null ? null : ReadResult(req.Id);
        }
        catch (Exception ex)
        {
            Logger.Error("Admin helper failed", ex);
            return null;
        }
    }

    /// <summary>Starts RestMode.exe elevated (UAC prompt). Returns exit code, or null if declined/timed out.</summary>
    public static async Task<int?> RunElevatedAsync(string args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(AppPaths.ExePath, args) { UseShellExecute = true, Verb = "runas" };
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return null;
            using var cts = new CancellationTokenSource(timeout);
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Logger.Info("UAC prompt was declined; admin-only steps skipped.");
            return null;
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("Elevated helper timed out.");
            return null;
        }
    }

    public static async Task<bool> InstallTaskAsync(AppSettings s)
    {
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string args = $"--helper install-task --data {Quote(AppPaths.DataDir)} --user-sid {sid}";
        string? fan = ResolveFanExe(s);
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
