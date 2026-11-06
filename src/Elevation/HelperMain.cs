using System.Security.Principal;
using RestMode.Util;

namespace RestMode.Elevation;

/// <summary>
/// Entry point of the short-lived elevated helper: `RestMode.exe --helper ...`.
/// It has no UI. Verbs:
///   sync          apply or restore adapter power flags + Fan Control profile,
///                 according to helper-request.json
///   install-task  register the "RestMode Helper" scheduled task (run elevated
///                 on demand, so later uses need no UAC prompt)
///   remove-task   delete that task
/// </summary>
internal static class HelperMain
{
    public static int Run(CommandLine cmd)
    {
        if (cmd.DataDir != null)
        {
            if (!Path.IsPathFullyQualified(cmd.DataDir) || JsonFile.IsReparsePoint(cmd.DataDir))
                return 2;
            AppPaths.UseDataDir(cmd.DataDir);
        }
        try { AppPaths.EnsureDataDir(); } catch { return 3; }
        Logger.Init(AppPaths.HelperLogFile);
        Logger.Info($"Helper started: {cmd.HelperVerb} (elevated={IsElevated()})");

        try
        {
            switch (cmd.HelperVerb)
            {
                case "sync":
                    Sync(cmd.FanExe);
                    return 0;
                case "install-task":
                    return TaskInstaller.Install(cmd.UserSid, cmd.FanExe) ? 0 : 1;
                case "remove-task":
                    return TaskInstaller.Remove() ? 0 : 1;
                default:
                    Logger.Warn($"Unknown helper verb '{cmd.HelperVerb}'.");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Helper failed", ex);
            return 1;
        }
    }

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Idempotent: brings adapters and Fan Control to the requested state.
    /// Safe to run any number of times. Also called in-process when RestMode
    /// itself already runs as administrator.
    /// </summary>
    /// <param name="trustedFanExe">FanControl.exe path from the command line (approved via UAC), or null.</param>
    public static void Sync(string? trustedFanExe)
    {
        var log = new List<string>();
        var req = JsonFile.Read<HelperRequest>(AppPaths.HelperRequestFile) ?? new HelperRequest { WantActive = false };
        var state = JsonFile.Read<HelperState>(AppPaths.HelperStateFile) ?? new HelperState();
        bool ok = true;

        if (!IsElevated())
        {
            log.Add("Helper is not running as administrator; nothing changed.");
            WriteResult(req.Id, false, log);
            return;
        }

        if (req.WantActive)
        {
            // ---- network adapters ----
            if (req.Adapters && state.Adapters.Count == 0)
            {
                try
                {
                    state.Adapters = NetworkAdapterPower.KeepPowered(log);
                    SaveState(state);
                }
                catch (Exception ex) { ok = false; log.Add($"Network adapters skipped: {ex.Message}"); }
            }

            // ---- Fan Control ----
            if (req.Fan && !state.FanSwitched)
            {
                string? exe = ResolveFanExe(trustedFanExe);
                if (exe == null)
                    log.Add("Fan Control not found in a trusted location - fan profile not changed.");
                else if (FanControlSwitcher.Switch(exe, req.FanQuietConfig, log))
                {
                    state.FanSwitched = true;
                    state.FanExe = exe;
                    state.FanNormalConfig = AppSettingsSafe(req.FanNormalConfig);
                    SaveState(state);
                }
            }
        }
        else
        {
            if (state.Adapters.Count > 0)
            {
                try
                {
                    state.Adapters = NetworkAdapterPower.Restore(state.Adapters, log);
                    if (state.Adapters.Count > 0) ok = false;
                }
                catch (Exception ex) { ok = false; log.Add($"Restoring network adapters failed: {ex.Message}"); }
                SaveState(state);
            }

            if (state.FanSwitched)
            {
                // Only re-launch the exact exe we used, and only if it is still trusted.
                string? exe = state.FanExe != null && IsTrustedFanExe(state.FanExe, trustedFanExe) ? state.FanExe : ResolveFanExe(trustedFanExe);
                string normal = state.FanNormalConfig ?? "userConfig.json";
                if (exe != null && FanControlSwitcher.Switch(exe, normal, log))
                {
                    state.FanSwitched = false;
                    state.FanExe = null;
                    SaveState(state);
                }
                else
                {
                    ok = false;
                    log.Add("Could not switch Fan Control back - choose your normal profile in Fan Control.");
                    state.FanSwitched = false; // don't nag forever
                    SaveState(state);
                }
            }
        }

        foreach (var line in log) Logger.Info("  " + line);
        WriteResult(req.Id, ok, log);
    }

    private static string AppSettingsSafe(string name) =>
        Core.AppSettings.IsSafeConfigName(name) ? name : "userConfig.json";

    private static string? ResolveFanExe(string? trustedFanExe)
    {
        if (!string.IsNullOrWhiteSpace(trustedFanExe) && File.Exists(trustedFanExe)) return trustedFanExe;
        return FanControlSwitcher.TrustedCandidates().FirstOrDefault(File.Exists);
    }

    private static bool IsTrustedFanExe(string path, string? trustedFanExe) =>
        string.Equals(path, trustedFanExe, StringComparison.OrdinalIgnoreCase) ||
        FanControlSwitcher.TrustedCandidates().Any(c => string.Equals(c, path, StringComparison.OrdinalIgnoreCase));

    private static void SaveState(HelperState state)
    {
        try
        {
            if (state.HasPendingRestore) JsonFile.Write(AppPaths.HelperStateFile, state);
            else JsonFile.Delete(AppPaths.HelperStateFile);
        }
        catch (Exception ex) { Logger.Error("Saving helper state failed", ex); }
    }

    private static void WriteResult(string id, bool ok, List<string> log)
    {
        try { JsonFile.Write(AppPaths.HelperResultFile, new HelperResult { Id = id, Success = ok, Messages = log }); }
        catch (Exception ex) { Logger.Error("Writing helper result failed", ex); }
    }
}
