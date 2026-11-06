using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using RestMode.Util;

namespace RestMode.Elevation;

/// <summary>
/// Optional "no more UAC prompts" mode. Registers an on-demand scheduled task
/// that runs `RestMode.exe --helper sync` with highest privileges for the
/// current user. The non-admin app can then start it with `schtasks /run`.
/// Creating it needs one UAC approval.
///
/// The task is created from XML (not plain schtasks switches) because the
/// defaults would stop it from running on battery - which breaks laptops.
/// The FanControl.exe path and data folder are baked into the task's
/// arguments, which only an administrator can change.
///
/// Security: a task with highest privileges must never run a file that a
/// normal user can replace, or any program could swap it and get admin
/// rights. So the task runs a COPY of RestMode.exe placed in
/// "%ProgramFiles%\RestMode Helper" (writable by administrators only).
/// </summary>
internal static class TaskInstaller
{
    public const string TaskName = "RestMode Helper";

    public static string SecureDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RestMode Helper");

    public static bool IsInstalled() => RunSchtasks(new[] { "/query", "/tn", TaskName }, out _) == 0;

    public static bool Install(string? userSid, string? fanExe)
    {
        string user = !string.IsNullOrEmpty(userSid) ? userSid : WindowsIdentity.GetCurrent().User!.Value;
        string? helperExe = CopyToSecureLocation();
        if (helperExe == null) return false;
        string args = $"--helper sync --data \"{AppPaths.DataDir.TrimEnd('\\')}\"";
        if (!string.IsNullOrWhiteSpace(fanExe) && File.Exists(fanExe))
            args += $" --fan \"{fanExe}\"";

        string xml = $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Lets RestMode change network adapter power flags and the Fan Control profile without a UAC prompt each time.</Description>
  </RegistrationInfo>
  <Principals>
    <Principal id="Author">
      <UserId>{SecurityElement.Escape(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{SecurityElement.Escape(helperExe)}</Command>
      <Arguments>{SecurityElement.Escape(args)}</Arguments>
    </Exec>
  </Actions>
</Task>
""";
        // Written under C:\Windows\Temp: normal users cannot modify or replace a
        // file an administrator created there, so the XML can't be swapped
        // before schtasks reads it.
        string tmpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        if (!Directory.Exists(tmpDir)) tmpDir = Path.GetTempPath();
        string tmp = Path.Combine(tmpDir, $"RestModeTask-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            int code = RunSchtasks(new[] { "/create", "/tn", TaskName, "/xml", tmp, "/f" }, out string output);
            Logger.Info($"schtasks /create exit {code}: {output.Trim()}");
            return code == 0;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public static bool Remove()
    {
        int code = RunSchtasks(new[] { "/delete", "/tn", TaskName, "/f" }, out string output);
        Logger.Info($"schtasks /delete exit {code}: {output.Trim()}");
        try
        {
            if (Directory.Exists(SecureDir)) Directory.Delete(SecureDir, recursive: true);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not delete {SecureDir}: {ex.Message}");
        }
        return code == 0 || !IsInstalled();
    }

    /// <summary>Copies the running single-file RestMode.exe into Program Files; returns the copy's path.</summary>
    private static string? CopyToSecureLocation()
    {
        string src = AppPaths.ExePath;
        if (File.Exists(Path.ChangeExtension(src, ".dll")))
        {
            // A multi-file debug build can't be copied as one file.
            Logger.Error("The helper task needs the single-file RestMode.exe (run build.cmd).");
            return null;
        }
        try
        {
            Directory.CreateDirectory(SecureDir);
            string dest = Path.Combine(SecureDir, "RestMode.exe");
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                File.Copy(src, dest, overwrite: true);
            Logger.Info($"Helper copy installed at {dest}.");
            return dest;
        }
        catch (Exception ex)
        {
            Logger.Error("Copying RestMode.exe to Program Files failed", ex);
            return null;
        }
    }

    public static bool RunNow() => RunSchtasks(new[] { "/run", "/tn", TaskName }, out _) == 0;

    private static int RunSchtasks(string[] args, out string output)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(20_000)) { try { p.Kill(); } catch { } return -1; }
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return -1;
        }
    }
}
