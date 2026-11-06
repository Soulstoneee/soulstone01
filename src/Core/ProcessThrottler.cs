using System.Diagnostics;
using System.Runtime.InteropServices;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// Optional: lowers the priority of (or pauses) heavy background apps from the
/// user's list while resting, and puts them back afterwards.
///
/// Safety rules (cannot be overridden from settings):
///  * Never touches system processes, download clients, game launchers,
///    cloud-sync / file-transfer tools or ANY web browser (we cannot see
///    whether a browser has a download running, so browsers are always left alone).
///  * Only touches processes in the user's own session.
///  * "Pause" is downgraded to "low priority" if the app is doing a lot of
///    disk/network I/O right now (it may be transferring something).
///  * Each change is written to state.json immediately, and a process is only
///    restored if its PID AND creation time still match (PIDs get reused).
/// </summary>
internal static class ProcessThrottler
{
    /// <summary>I/O above this (bytes/s) means "busy - don't pause".</summary>
    private const double BusyIoBytesPerSec = 256 * 1024;

    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows / system
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "audiodg", "conhost", "taskhostw",
        "runtimebroker", "searchindexer", "searchhost", "searchapp", "msmpeng", "nissrv", "securityhealthservice",
        "securityhealthsystray", "spoolsv", "memory compression", "wudfhost", "dllhost", "startmenuexperiencehost",
        "shellexperiencehost", "textinputhost", "lockapp", "logonui", "userinit", "dashost", "wlanext",
        "trustedinstaller", "tiworker", "wuauclt", "usocoreworker", "mousocoreworker", "musnotification",
        "backgroundtaskhost", "applicationframehost", "systemsettings", "smartscreen", "taskmgr", "mmc",
        "powershell", "pwsh", "cmd", "windowsterminal", "openconsole", "wsl", "wslhost", "vmmem", "vmcompute",
        // Download clients, game launchers, stores
        "steam", "steamwebhelper", "steamservice", "steamerrorreporter", "epicgameslauncher", "epicwebhelper",
        "eadesktop", "eabackgroundservice", "origin", "originwebhelperservice", "battle.net", "galaxyclient",
        "galaxyclient helper", "gogalaxy", "upc", "uplay", "ubisoftconnect", "xboxapp", "xboxpcapp",
        "gamingservices", "gamingservicesnet", "winstore.app", "riotclientservices", "riotclientux",
        "amazongames", "playnite.desktopapp", "playnite.fullscreenapp", "heroicgameslauncher",
        "qbittorrent", "utorrent", "bittorrent", "transmission-qt", "transmission-daemon", "deluge", "deluged",
        "tixati", "vuze", "frostwire", "idman", "fdm", "freedownloadmanager", "jdownloader2", "aria2c", "motrix",
        "nzbget", "sabnzbd",
        // Cloud sync / file transfer
        "onedrive", "dropbox", "googledrivefs", "icloudservices", "icloud", "megasync", "nextcloud", "owncloud",
        "syncthing", "resilio sync", "robocopy", "xcopy", "teracopy", "fastcopy", "rsync", "winscp", "filezilla",
        "curl", "wget", "bitsadmin",
        // Browsers (may have downloads running)
        "chrome", "msedge", "msedgewebview2", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore",
        "arc", "librewolf", "waterfox", "chromium", "thorium", "floorp", "zen", "yandex", "whale",
        // Ourselves and fan tools
        "restmode", "fancontrol",
    };

    public static string NormalizeName(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }

    public static bool IsProtected(string name) => Protected.Contains(NormalizeName(name));

    public static async Task ApplyAsync(IEnumerable<AppRule> rules, Action<ProcessSnapshot> onChanged)
    {
        int ownSession = Process.GetCurrentProcess().SessionId;
        int ownPid = Environment.ProcessId;
        var targets = new List<(int pid, string name, AppAction action)>();

        foreach (var rule in rules)
        {
            string name = NormalizeName(rule.Name);
            if (name.Length == 0) continue;
            if (IsProtected(name))
            {
                Logger.Info($"  apps: '{name}' is on the protected list - never touched.");
                continue;
            }
            foreach (var p in SafeGetProcesses(name))
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == ownPid || p.SessionId != ownSession) continue;
                        if (targets.Any(t => t.pid == p.Id)) continue; // listed twice (e.g. "x" and "x.exe")
                        targets.Add((p.Id, p.ProcessName, rule.Action));
                    }
                    catch { /* process exited */ }
                }
            }
        }
        if (targets.Count == 0) { Logger.Info("  apps: none of the listed apps are running."); return; }

        // Measure I/O of pause candidates for ~1.5 s, so busy apps are not frozen mid-transfer.
        var pauseCandidates = targets.Where(t => t.action == AppAction.Pause).Select(t => t.pid).ToList();
        var io1 = pauseCandidates.ToDictionary(pid => pid, ReadIo);
        if (pauseCandidates.Count > 0) await Task.Delay(1500);
        var busy = new HashSet<int>();
        foreach (var pid in pauseCandidates)
        {
            ulong a = io1[pid], b = ReadIo(pid);
            if (b > a && (b - a) / 1.5 > BusyIoBytesPerSec) busy.Add(pid);
        }

        foreach (var (pid, name, action) in targets)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_INFORMATION | PROCESS_SUSPEND_RESUME, false, pid);
            if (h == IntPtr.Zero)
            {
                Logger.Info($"  apps: cannot open {name} ({pid}) - skipped (error {Marshal.GetLastWin32Error()}).");
                continue;
            }
            try
            {
                if (!GetProcessTimes(h, out long created, out _, out _, out _)) continue;
                var snap = new ProcessSnapshot { Pid = pid, Name = name, CreationFileTime = created, OriginalPriorityClass = GetPriorityClass(h) };

                if (action == AppAction.Pause && !busy.Contains(pid))
                {
                    if (NtSuspendProcess(h) == 0)
                    {
                        snap.Suspended = true;
                        Logger.Info($"  apps: paused {name} ({pid}).");
                    }
                }
                else
                {
                    if (action == AppAction.Pause)
                        Logger.Info($"  apps: {name} ({pid}) is busy with I/O - lowering priority instead of pausing.");
                    if (snap.OriginalPriorityClass != 0 && SetPriorityClass(h, IDLE_PRIORITY_CLASS))
                        snap.PriorityChanged = true;
                    snap.EcoQosSet = SetEcoQos(h, true);
                    Logger.Info($"  apps: {name} ({pid}) -> low priority{(snap.EcoQosSet ? " + efficiency mode" : "")}.");
                }

                if (snap.Suspended || snap.PriorityChanged || snap.EcoQosSet) onChanged(snap);
            }
            catch (Exception ex)
            {
                Logger.Warn($"  apps: {name} ({pid}) skipped: {ex.Message}");
            }
            finally
            {
                CloseHandle(h);
            }
        }
    }

    public static void Restore(IEnumerable<ProcessSnapshot> snapshots)
    {
        foreach (var s in snapshots)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_INFORMATION | PROCESS_SUSPEND_RESUME, false, s.Pid);
            if (h == IntPtr.Zero) continue; // process has exited - nothing to restore
            try
            {
                if (!GetProcessTimes(h, out long created, out _, out _, out _) || created != s.CreationFileTime)
                {
                    Logger.Info($"  apps: PID {s.Pid} is no longer {s.Name} - left alone.");
                    continue;
                }
                if (s.Suspended)
                {
                    int st = NtResumeProcess(h);
                    Logger.Info($"  apps: resumed {s.Name} ({s.Pid}){(st == 0 ? "" : $" (status 0x{st:X})")}.");
                }
                if (s.PriorityChanged && s.OriginalPriorityClass != 0)
                    SetPriorityClass(h, s.OriginalPriorityClass);
                if (s.EcoQosSet)
                    SetEcoQos(h, false);
                if (s.PriorityChanged || s.EcoQosSet)
                    Logger.Info($"  apps: restored priority of {s.Name} ({s.Pid}).");
            }
            catch (Exception ex)
            {
                Logger.Warn($"  apps: restoring {s.Name} ({s.Pid}) failed: {ex.Message}");
            }
            finally
            {
                CloseHandle(h);
            }
        }
    }

    /// <summary>Windows 11 "Efficiency mode" (EcoQoS). On = throttle; off = let Windows decide again.</summary>
    private static bool SetEcoQos(IntPtr h, bool on)
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = on ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
                StateMask = on ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
            };
            return SetProcessInformation(h, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state));
        }
        catch
        {
            return false; // older Windows 10
        }
    }

    private static ulong ReadIo(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return 0;
        try
        {
            return GetProcessIoCounters(h, out var c)
                ? c.ReadTransferCount + c.WriteTransferCount + c.OtherTransferCount
                : 0;
        }
        finally { CloseHandle(h); }
    }

    private static Process[] SafeGetProcesses(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch { return Array.Empty<Process>(); }
    }
}
