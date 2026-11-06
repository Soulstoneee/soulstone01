using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Elevation;

/// <summary>
/// Optional integration with Rem0o's "Fan Control" (https://getfancontrol.com).
/// FanControl.exe -c &lt;config.json&gt; switches the running instance to that
/// config. Fan Control runs as administrator, so the switch is done from
/// RestMode's elevated helper.
///
/// Fan Control is optional: if it isn't installed, isn't running, can't see
/// the fans, or the chosen config doesn't exist, this step is skipped with a
/// clear message and the rest of Rest Mode works as usual. RestMode never
/// starts Fan Control by itself (that would change how your fans behave), and
/// never edits Fan Control's configs: the Quiet/Normal configs are made in
/// Fan Control (see README for safe curve settings).
///
/// Security: the helper only launches a FanControl.exe path that came from
/// its own command line (shown in the UAC prompt / baked into the admin-only
/// helper task) or one inside Program Files. Paths are never read from the
/// user-writable request file.
/// </summary>
internal static class FanControlSwitcher
{
    /// <summary>Paths that only an administrator can write to.</summary>
    public static IEnumerable<string> TrustedCandidates()
    {
        foreach (var root in ProgramFilesRoots())
        {
            yield return Path.Combine(root, "FanControl", "FanControl.exe");
            yield return Path.Combine(root, "Fan Control", "FanControl.exe");
        }
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        foreach (var r in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
            if (!string.IsNullOrEmpty(r)) yield return r;
    }

    /// <summary>Accepts the Fan Control folder or FanControl.exe itself; returns the exe path or null.</summary>
    public static string? NormalizeExe(string? pathOrFolder)
    {
        if (string.IsNullOrWhiteSpace(pathOrFolder)) return null;
        string p = Environment.ExpandEnvironmentVariables(pathOrFolder.Trim().Trim('"'));
        try
        {
            if (Directory.Exists(p))
            {
                string exe = Path.Combine(p, "FanControl.exe");
                return File.Exists(exe) ? exe : null;
            }
            return File.Exists(p) && string.Equals(Path.GetFileName(p), "FanControl.exe", StringComparison.OrdinalIgnoreCase) ? p : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// For the settings window (non-elevated): finds FanControl.exe from the
    /// running process, then common install / download places on any PC:
    /// Program Files, the Desktop (incl. OneDrive Desktop), Downloads, C:\Tools,
    /// C:\FanControl, %LOCALAPPDATA%\Programs and winget packages. Folders named
    /// FanControl* (e.g. "FanControl_285_net_10_0") one level down are searched too.
    /// </summary>
    public static string? Detect()
    {
        string? running = RunningExePath();
        if (running != null) return running;

        var roots = new List<string>();
        roots.AddRange(ProgramFilesRoots());
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        roots.Add(Path.Combine(profile, "Desktop"));
        roots.Add(Path.Combine(profile, "Downloads"));
        roots.Add(Path.Combine(local, "Programs"));
        string sysDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
        roots.Add(Path.Combine(sysDrive, "Tools"));
        roots.Add(sysDrive);

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                if (NormalizeExe(root) is string direct) return direct;
                foreach (var dir in Directory.EnumerateDirectories(root, "*Fan*Control*"))
                    if (NormalizeExe(dir) is string exe) return exe;
            }
            catch { /* access denied etc. */ }
        }

        try
        {
            string winget = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(winget))
                foreach (var dir in Directory.GetDirectories(winget, "Rem0o.FanControl*"))
                    if (NormalizeExe(dir) is string exe) return exe;
        }
        catch { /* ignore */ }
        return null;
    }

    public static string ConfigurationsDir(string exe) => Path.Combine(Path.GetDirectoryName(exe)!, "Configurations");

    /// <summary>Config files Fan Control has saved (plain names, e.g. "Quiet.json").</summary>
    public static List<string> ListConfigs(string? exe)
    {
        try
        {
            if (exe == null) return new();
            string dir = ConfigurationsDir(exe);
            if (!Directory.Exists(dir)) return new();
            return Directory.GetFiles(dir, "*.json")
                .Select(Path.GetFileName)
                .Where(n => n != null && Core.AppSettings.IsSafeConfigName(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Checks that the .NET runtime(s) named in FanControl.runtimeconfig.json are
    /// installed (Fan Control ships builds for different .NET versions).
    /// Returns null if fine (or self-contained), otherwise a message for the user.
    /// </summary>
    public static string? CheckRuntime(string exe)
    {
        try
        {
            string cfg = Path.Combine(Path.GetDirectoryName(exe)!, "FanControl.runtimeconfig.json");
            if (!File.Exists(cfg)) return null; // self-contained build: nothing to check
            using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
            var opts = doc.RootElement.GetProperty("runtimeOptions");
            var needed = new List<(string name, Version version)>();
            if (opts.TryGetProperty("frameworks", out var fws))
                foreach (var fw in fws.EnumerateArray()) Add(fw);
            else if (opts.TryGetProperty("framework", out var one))
                Add(one);
            void Add(JsonElement fw)
            {
                if (Version.TryParse(fw.GetProperty("version").GetString(), out var v))
                    needed.Add((fw.GetProperty("name").GetString() ?? "", v));
            }

            var missing = needed.Where(n => !RuntimeInstalled(n.name, n.version)).ToList();
            if (missing.Count == 0) return null;
            string list = string.Join(", ", missing.Select(m => $"{m.name} {m.version.Major}.x"));
            return $"Fan Control needs the .NET {missing[0].version.Major} Desktop Runtime ({list}), which is not installed. " +
                   $"Get it from https://dotnet.microsoft.com/download/dotnet/{missing[0].version.Major}.0";
        }
        catch (Exception ex)
        {
            Logger.Info($"Could not check Fan Control's .NET runtime: {ex.Message}");
            return null;
        }
    }

    private static bool RuntimeInstalled(string name, Version min)
    {
        var dotnetRoots = new List<string>();
        string? env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(env)) dotnetRoots.Add(env);
        foreach (var r in ProgramFilesRoots()) dotnetRoots.Add(Path.Combine(r, "dotnet"));
        foreach (var root in dotnetRoots)
        {
            string dir = Path.Combine(root, "shared", name);
            if (!Directory.Exists(dir)) continue;
            foreach (var d in Directory.GetDirectories(dir))
            {
                string verText = Path.GetFileName(d).Split('-')[0];
                // Fan Control uses rollForward=LatestMinor: same major, same or newer minor.
                if (Version.TryParse(verText, out var v) && v.Major == min.Major && v >= min) return true;
            }
        }
        return false;
    }

    /// <summary>Warnings about where Fan Control lives (null = fine).</summary>
    public static string? LocationWarning(string exe)
    {
        string full = Path.GetFullPath(exe);
        if (ProgramFilesRoots().Any(r => full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)))
            return null;
        var oneDrives = new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!.TrimEnd('\\') + "\\");
        bool inOneDrive = oneDrives.Any(o => full.StartsWith(o, StringComparison.OrdinalIgnoreCase));
        string why = "RestMode's admin helper starts FanControl.exe as administrator, and any program running as you can replace files in this folder.";
        return inOneDrive
            ? "This folder is synced by OneDrive: OneDrive can make the files online-only (Fan Control then fails to start), creates conflict copies of its configs, and syncs changes from other PCs. " +
              why + @" Recommended: move the whole folder to C:\Program Files\FanControl."
            : why + @" Recommended: move the whole folder to C:\Program Files\FanControl (admin-only).";
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

    /// <summary>Runs `FanControl.exe -c config` (from the elevated helper). Never starts Fan Control if it isn't running.</summary>
    public static bool Switch(string exe, string config, List<string> log)
    {
        if (!File.Exists(exe)) { log.Add($"Fan Control: FanControl.exe not found at {exe} - skipped."); return false; }
        if (!Core.AppSettings.IsSafeConfigName(config)) { log.Add($"Fan Control: rejected config name '{config}'."); return false; }
        if (!IsRunning())
        {
            log.Add("Fan Control is not running - fan profile not changed (RestMode never starts it, so your fans keep their current behaviour).");
            return false;
        }
        if (!File.Exists(Path.Combine(ConfigurationsDir(exe), config)))
        {
            log.Add($"Fan Control: config '{config}' not found in {ConfigurationsDir(exe)} - skipped.");
            return false;
        }

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(config);
        using var p = Process.Start(psi);
        // Fan Control is already running, so this short-lived process just messages it and exits.
        p?.WaitForExit(10_000);
        log.Add($"Fan Control switched to {config}.");
        return true;
    }
}
