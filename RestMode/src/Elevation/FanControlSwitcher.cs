using System.Diagnostics;
using System.Text;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Elevation;

/// <summary>
/// Optional integration with Rem0o's "Fan Control" (https://getfancontrol.com).
/// FanControl.exe -c &lt;config.json&gt; switches the running instance to that
/// config (or starts it with that config). Fan Control runs as administrator,
/// so the switch is done from RestMode's elevated helper.
///
/// Security: the helper only ever launches a FanControl.exe path that came from
/// its own command line (set when the admin approved the UAC prompt / helper
/// task) or one inside Program Files. Paths are never read from the
/// user-writable request file.
/// </summary>
internal static class FanControlSwitcher
{
    /// <summary>Paths that only an administrator can write to.</summary>
    public static IEnumerable<string> TrustedCandidates()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "FanControl", "FanControl.exe");
            yield return Path.Combine(root, "Fan Control", "FanControl.exe");
        }
    }

    /// <summary>
    /// For the settings window (non-elevated): finds FanControl.exe from the
    /// running process, Program Files, or common portable/winget locations.
    /// </summary>
    public static string? Detect()
    {
        string? running = RunningExePath();
        if (running != null) return running;

        var candidates = new List<string>(TrustedCandidates());
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Programs", "FanControl", "FanControl.exe"));
        candidates.Add(@"C:\FanControl\FanControl.exe");
        try
        {
            string winget = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(winget))
                foreach (var dir in Directory.GetDirectories(winget, "Rem0o.FanControl*"))
                    candidates.Add(Path.Combine(dir, "FanControl.exe"));
        }
        catch { /* ignore */ }

        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsRunning()
    {
        var ps = Process.GetProcessesByName("FanControl");
        foreach (var p in ps) p.Dispose();
        return ps.Length > 0;
    }

    private static string? RunningExePath()
    {
        foreach (var p in Process.GetProcessesByName("FanControl"))
        {
            using (p)
            {
                // QueryFullProcessImageName works on elevated processes from a normal process.
                IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var sb = new StringBuilder(1024);
                    uint size = (uint)sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref size)) return sb.ToString();
                }
                finally { CloseHandle(h); }
            }
        }
        return null;
    }

    /// <summary>Runs `FanControl.exe -c config` (from the elevated helper).</summary>
    public static bool Switch(string exe, string config, List<string> log)
    {
        if (!File.Exists(exe)) { log.Add($"FanControl.exe not found at {exe}."); return false; }
        if (!Core.AppSettings.IsSafeConfigName(config)) { log.Add($"Rejected Fan Control config name '{config}'."); return false; }

        string dir = Path.GetDirectoryName(exe)!;
        if (!File.Exists(Path.Combine(dir, "Configurations", config)))
            log.Add($"Warning: {config} not found in {Path.Combine(dir, "Configurations")} - trying anyway.");

        bool wasRunning = IsRunning();
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(config);
        using var p = Process.Start(psi);
        // If Fan Control was already running, this short-lived process just messages it and exits.
        // Otherwise it IS the new Fan Control instance and keeps running.
        if (wasRunning) p?.WaitForExit(10_000);
        log.Add($"Fan Control switched to {config}.");
        return true;
    }
}
