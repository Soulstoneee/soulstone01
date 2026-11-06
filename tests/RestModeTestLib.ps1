# RestMode automated test library (used by run-tests.ps1). All waking is automatic (simulated mouse input).
$ErrorActionPreference = 'Continue'
$script:RM = (Resolve-Path (Join-Path $PSScriptRoot '..\dist\RestMode.exe')).Path
$script:Data = Join-Path $env:LOCALAPPDATA 'RestMode'
$script:LogFile = Join-Path $script:Data 'restmode.log'
$script:Results = New-Object System.Collections.ArrayList
# Admin checks (Lenovo fan mode, helper task install) run through an elevated runner script you provide:
$script:Admin = $env:RESTMODE_TEST_ADMIN
$script:Out = 'C:\Restmode\test-results\v1'
New-Item -ItemType Directory -Force $script:Out | Out-Null

Add-Type -Namespace RT -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
[DllImport("powrprof.dll")] public static extern uint PowerGetEffectiveOverlayScheme(out Guid g);
'@

function Say($m) { $line = "$((Get-Date).ToString('HH:mm:ss')) $m"; Write-Output $line; $line | Out-File "$script:Out\run.log" -Append -Encoding utf8 }
function LogCount { if (Test-Path $script:LogFile) { @(Get-Content $script:LogFile).Count } else { 0 } }
function LogSince([int]$m) { if (Test-Path $script:LogFile) { Get-Content $script:LogFile | Select-Object -Skip $m } }
function WaitLog([string]$pattern, [int]$since, [int]$timeout = 60) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) {
        $hit = LogSince $since | Select-String -Pattern $pattern | Select-Object -First 1
        if ($hit) { return $hit.Line }
        Start-Sleep -Milliseconds 700
    }
    return $null
}
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    [void]$script:Results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Say "  [$(if ($ok) { 'PASS' } else { 'FAIL' })] $name $(if ($detail) { "- $detail" })"
}
function Nudge { [RT.U]::mouse_event(1, 60, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 120; [RT.U]::mouse_event(1, -60, 0, 0, [UIntPtr]::Zero) }
function Sim([string]$c) { & $script:RM --simulate $c | Out-Null; Start-Sleep -Milliseconds 900 }
function ActivePlanName { $t = (powercfg /getactivescheme) -join ''; if ($t -match '[0-9a-f-]{36}\s+\((.+)\)\s*$') { $Matches[1].Trim() } else { $t } }
function TempPlans { @(powercfg /list | Select-String 'RestMode \(temporary\)').Count }
function Lid {
    $q = (powercfg /qh SCHEME_CURRENT SUB_BUTTONS LIDACTION) -join "`n"
    $ac = if ($q -match 'Current AC Power Setting Index: 0x([0-9a-f]+)') { [Convert]::ToInt32($Matches[1], 16) } else { -1 }
    $dc = if ($q -match 'Current DC Power Setting Index: 0x([0-9a-f]+)') { [Convert]::ToInt32($Matches[1], 16) } else { -1 }
    "$ac/$dc"
}
function PowerMode { $g = [Guid]::Empty; [void][RT.U]::PowerGetEffectiveOverlayScheme([ref]$g); $g.ToString() }
function LenovoMode {
    $f = Join-Path $env:TEMP 'rm-lenovo-mode.ps1'
    '(Invoke-CimMethod -InputObject @(Get-CimInstance -Namespace root\WMI -ClassName LENOVO_GAMEZONE_DATA)[0] -MethodName GetSmartFanMode).Data' | Out-File $f -Encoding utf8
    $o = & $script:Admin -File $f -TimeoutSec 60
    ($o | Select-Object -Last 1).ToString().Trim()
}
function Running { [bool](Get-Process RestMode -ErrorAction SilentlyContinue) }
function StartRM {
    $m = LogCount
    Start-Process $script:RM -ArgumentList '--startup'
    $l = WaitLog 'RestMode 1\.0\.0 started' $m 40
    Start-Sleep 3
    return [bool]$l
}
function StopRM { Get-Process RestMode -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2 }
function Enter([int]$timeout = 120) {
    $m = LogCount
    & $script:RM --enter | Out-Null
    $l = WaitLog '^\S+ \S+ \[INFO \] Resting\.' $m $timeout
    return @{ Mark = $m; Ok = [bool]$l }
}
function Wake([int]$timeout = 90, [int]$since = -1) {
    # If the session already ended (e.g. a real key press), report that instead of waiting.
    if ($since -ge 0) {
        $early = LogSince $since | Select-String '\[SESSN\]' | Select-Object -First 1
        if ($early) { return @{ Mark = $since; Ok = $true; Line = "$($early.Line) (ended before the test's nudge)" } }
    }
    $m = LogCount
    Nudge
    $l = WaitLog '\[SESSN\]' $m $timeout
    if (-not $l) { Nudge; $l = WaitLog '\[SESSN\]' $m 30 }
    return @{ Mark = $m; Ok = [bool]$l; Line = $l }
}
function SetSettings([hashtable]$changes) {
    $f = Join-Path $script:Data 'settings.json'
    $j = Get-Content $f -Raw | ConvertFrom-Json
    foreach ($k in $changes.Keys) {
        if ($j.PSObject.Properties.Name -contains $k) { $j.$k = $changes[$k] } else { $j | Add-Member -NotePropertyName $k -NotePropertyValue $changes[$k] }
    }
    ($j | ConvertTo-Json -Depth 5) | Out-File $f -Encoding utf8
    Sim 'reload-settings'
}
function StateClean {
    -not (Test-Path (Join-Path $script:Data 'state.json')) -and -not (Test-Path (Join-Path $script:Data 'helper-state.json'))
}
