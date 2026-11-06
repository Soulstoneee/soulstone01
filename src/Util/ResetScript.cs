namespace RestMode.Util;

/// <summary>
/// Writes RestMode-Reset.cmd: a separate "put everything back" script that works
/// even if RestMode.exe is broken or deleted. It is saved in RestMode's data
/// folder and next to the installed exe.
/// </summary>
internal static class ResetScript
{
    public const string FileName = "RestMode-Reset.cmd";

    public static void WriteTo(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), Content, System.Text.Encoding.ASCII);
        }
        catch (Exception ex) { Logger.Warn($"Could not write {FileName}: {ex.Message}"); }
    }

    private const string Content = """
@echo off
rem RestMode-Reset.cmd - puts back everything RestMode changes:
rem power plan, Windows power mode, lid setting, volume, fan mode, network adapter option.
rem Safe to run any time. Double-click it, or run it from a Command Prompt.
title RestMode reset
echo RestMode reset: putting your normal settings back...
taskkill /im RestMode.exe /f >nul 2>&1
set "RM=%~dp0RestMode.exe"
if not exist "%RM%" set "RM=%LOCALAPPDATA%\Programs\RestMode\RestMode.exe"
if exist "%RM%" (
  "%RM%" --restore --quiet
  echo Done. RestMode restored everything it had changed.
  goto end
)
echo RestMode.exe not found - doing it by hand.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$d=Join-Path $env:LOCALAPPDATA 'RestMode';" ^
 "$st=$null; try { $st=Get-Content (Join-Path $d 'state.json') -Raw | ConvertFrom-Json } catch {};" ^
 "$target= if ($st -and $st.OriginalScheme) { $st.OriginalScheme } else { '381b4222-f694-41f0-9685-ff5bb260df2e' };" ^
 "powercfg /setactive $target 2>$null; if ($LASTEXITCODE) { powercfg /setactive SCHEME_BALANCED };" ^
 "(powercfg /list) | Select-String 'RestMode \(temporary\)' | ForEach-Object { if ($_ -match '([0-9a-f-]{36})') { powercfg /delete $Matches[1] } };" ^
 "try { $lb=Get-Content (Join-Path $d 'lid-backup.json') -Raw | ConvertFrom-Json; foreach ($e in $lb.Entries) { powercfg /setacvalueindex $e.Scheme SUB_BUTTONS LIDACTION $e.Ac; powercfg /setdcvalueindex $e.Scheme SUB_BUTTONS LIDACTION $e.Dc }; powercfg /setactive SCHEME_CURRENT; Remove-Item (Join-Path $d 'lid-backup.json') } catch {};" ^
 "Remove-Item (Join-Path $d 'state.json') -ErrorAction SilentlyContinue;" ^
 "'Power plan and lid setting restored.'"
echo.
echo Also check by hand: the sound (taskbar speaker icon), Settings ^> Power ^> Power mode,
echo and on a gaming laptop your usual fan mode (e.g. Fn+Q).
:end
echo.
pause
""";
}
