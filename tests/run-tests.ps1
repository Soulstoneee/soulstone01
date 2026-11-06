# RestMode v1.0 automated test run on this laptop. Everything wakes automatically.
param([string[]]$Only = @())
. "$PSScriptRoot\RestModeTestLib.ps1"
# Whatever happens, clean up at the end (test files also expire on their own after 3 hours).
try {
function Want($n) { $Only.Count -eq 0 -or $Only -contains $n }
$originalLid = Lid
Say "=== RestMode v1.0 tests - lid before: $originalLid, plan: $(ActivePlanName), power mode: $(PowerMode)"

# ---------------------------------------------------------------- setup
New-Item -ItemType File -Force (Join-Path $Data 'testmode.flag') | Out-Null
New-Item -ItemType File -Force (Join-Path $Data 'testmode-nosleep.flag') | Out-Null
$settings = [ordered]@{
    CpuMaxPercent = 25; CpuMinPercent = 5; DiskIdleSeconds = 180; LidAction = 'Rest'
    IdleThresholdKBps = 50; DiskThresholdKBps = 2048; IdleMinutes = 60; EndAction = 'Stay'; WatchApps = @()
    LowBatteryPercent = 20; StricterOnBattery = $true; OverheatProtection = $true; TempLimitC = 0
    Hotkey = 'Ctrl+Alt+Shift+R'; MuteAudio = $true; MouseMoveThresholdPx = 5; WakeWithController = $true
    RestAt = ''; WakeAt = ''; ThrottleApps = $false; KeepNetworkAdaptersPowered = $true
    FanMode = 'Auto'; UseBuiltInFanModes = $true; FanControlExe = 'C:\Users\isufi\OneDrive\Desktop\FanControl_285_net_10_0'
    FanQuietConfig = 'Quiet.json'; FanNormalConfig = 'Normal.json'
    RestAnimation = $true; Sounds = $false; ShowNotifications = $true; FirstRunDone = $true
}
StopRM
$settings | ConvertTo-Json | Out-File (Join-Path $Data 'settings.json') -Encoding utf8

if (Want 'helper') {
    Say "--- install the RestMode Helper task (via the dev admin task, no prompt)"
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $job = Join-Path $env:TEMP 'rm-install-helper.ps1'
    "& '$RM' --helper install-task --data '$Data' --user-sid $sid --fan 'C:\Users\isufi\OneDrive\Desktop\FanControl_285_net_10_0\FanControl.exe'; `"exit `$LASTEXITCODE`"" | Out-File $job -Encoding utf8
    $o = & $Admin -File $job -TimeoutSec 120
    $null = schtasks /query /tn 'RestMode Helper' 2>$null
    Check 'helper task installed' ($LASTEXITCODE -eq 0) "$o"
}

# ---------------------------------------------------------------- T0 startup
Say "--- T0 startup, self-check, lid = Rest"
$m = LogCount
Check 'app starts' (StartRM)
Check 'self-check ran' ([bool](LogSince $m | Select-String 'Self-check done'))
Start-Sleep 2
Check 'lid set to Do nothing while RestMode runs' ((Lid) -eq '0/0') "lid=$(Lid)"
Check 'lid backup saved first' (Test-Path (Join-Path $Data 'lid-backup.json'))
Check 'reset script written' (Test-Path (Join-Path $Data 'RestMode-Reset.cmd'))

# ---------------------------------------------------------------- T1 five enter/wake cycles
if (Want 'cycles') {
    for ($i = 1; $i -le 5; $i++) {
        Say "--- T1 cycle $i"
        $e = Enter
        Check "cycle $i entered" $e.Ok
        Start-Sleep 4
        Check "cycle $i Rest Mode plan active" ((ActivePlanName) -eq 'RestMode (temporary)') (ActivePlanName)
        if ($i -eq 1) { Check 'cycle 1 Lenovo Quiet mode during rest' ((LenovoMode) -eq '1') "mode=$(LenovoMode)" }
        $log = LogSince $e.Mark
        Check "cycle $i no admin prompt needed (helper task)" (-not ($log | Select-String 'No answer to the admin prompt'))
        Start-Sleep 15
        $w = Wake -since $e.Mark
        Check "cycle $i woke automatically" $w.Ok $w.Line
        Start-Sleep 3
        Check "cycle $i plan restored" ((ActivePlanName) -eq 'Balanced' -and (TempPlans) -eq 0) "$(ActivePlanName), temp plans=$(TempPlans)"
        Check "cycle $i state clean" (StateClean)
        if ($i -eq 1) { Check 'cycle 1 Lenovo mode restored' ((LenovoMode) -eq '2') "mode=$(LenovoMode)" }
        Check "cycle $i no freeze" (-not (LogSince $e.Mark | Select-String 'PROBLEM|frozen'))
    }
}

# ---------------------------------------------------------------- T2 lid
if (Want 'lid') {
    Say "--- T2 lid close -> Rest, lid open -> wake"
    $m = LogCount
    Sim 'lid-close'
    $r = WaitLog '\[INFO \] Resting\.' $m 120
    Check 'lid close enters Rest Mode' ([bool]$r)
    Check 'lid close: no overlay animation / prompt' (-not (LogSince $m | Select-String 'No answer to the admin prompt'))
    Start-Sleep 5
    $m2 = LogCount
    Sim 'lid-open'
    Check 'lid open wakes' ([bool](WaitLog '\[SESSN\].*lid opened' $m2 60))
    Start-Sleep 3
    Check 'lid test: state clean' (StateClean)
}

# ---------------------------------------------------------------- T3 download during rest + auto-end
if (Want 'download') {
    Say "--- T3 download during Rest (1 MB/s, 150 s), then auto-end after 1 idle minute"
    SetSettings @{ IdleMinutes = 1; EndAction = 'Stay' }
    $e = Enter
    Check 'download test entered' $e.Ok
    $curl = Start-Process curl.exe -PassThru -WindowStyle Hidden -RedirectStandardOutput "$Out\curl.txt" -ArgumentList '-s','-o','NUL','--limit-rate','1M','--max-time','150','-w','%{size_download}','https://fsn1-speed.hetzner.com/1GB.bin'
    $pings = 0; $ok = 0
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 150) { $pings++; if (Test-Connection 1.1.1.1 -Count 1 -Quiet) { $ok++ }; Start-Sleep 4 }
    $curl.WaitForExit(30000) | Out-Null
    $bytes = [long](Get-Content "$Out\curl.txt" -ErrorAction SilentlyContinue)
    Check 'download kept running during Rest' ($bytes -gt 100MB) "$([math]::Round($bytes/1MB)) MB"
    Check 'network stayed up during Rest' ($ok -ge $pings - 1) "$ok/$pings pings"
    Check 'did not end while downloading' (-not (LogSince $e.Mark | Select-String 'Downloads look finished'))
    $fin = WaitLog 'Downloads look finished' $e.Mark 150
    Check 'auto-end detected downloads finished' ([bool]$fin) $fin
    $w = Wake
    Check 'download test woke' $w.Ok $w.Line
    SetSettings @{ IdleMinutes = 60 }
}

# ---------------------------------------------------------------- T4 watchdog
if (Want 'watchdog') {
    Say "--- T4 watchdog: stay-awake request killed, plan hijacked"
    $e = Enter
    Check 'watchdog test entered' $e.Ok
    $m = LogCount
    Sim 'drop-awake'
    Sim 'watchdog'
    Check 'watchdog restores the stay-awake request' ([bool](WaitLog 'stay-awake request was gone - restored' $m 20))
    $m = LogCount
    Sim 'hijack-plan'
    Start-Sleep 1
    Sim 'watchdog'
    Check 'watchdog switches back to the Rest Mode plan' ([bool](WaitLog 'another program switched the power plan' $m 20))
    Start-Sleep 2
    Check 'Rest Mode plan active again' ((ActivePlanName) -eq 'RestMode (temporary)') (ActivePlanName)
    $w = Wake
    Check 'watchdog test woke' $w.Ok
}

# ---------------------------------------------------------------- T5 overheat
if (Want 'overheat') {
    Say "--- T5 simulated overheat"
    $e = Enter
    Check 'overheat test entered' $e.Ok
    $m = LogCount
    Sim 'temp=78'
    Check 'overheat step 1: fans back to normal' ([bool](WaitLog 'Overheat protection: .*fans back to normal' $m 40))
    Start-Sleep 3
    Check 'overheat step 1: Lenovo mode Balanced again while resting' ((LenovoMode) -eq '2') "mode=$(LenovoMode)"
    $m = LogCount
    Sim 'temp=86'
    Check 'overheat step 2: sleep' ([bool](WaitLog 'putting the PC to sleep to cool down' $m 40))
    Check 'overheat: session ended + would sleep' ([bool](WaitLog '\[TEST\] would sleep' $m 60))
    Sim 'temp=off'
    Start-Sleep 3
    Check 'overheat: state clean' (StateClean)
    Nudge
}

# ---------------------------------------------------------------- T6 battery / charger
if (Want 'battery') {
    Say "--- T6 charger unplugged + low battery (simulated)"
    $e = Enter
    Check 'battery test entered' $e.Ok
    $m = LogCount
    Sim 'ac=off'
    Check 'charger unplug warning' ([bool](WaitLog 'Charger unplugged during Rest Mode' $m 20))
    $m = LogCount
    Sim 'battery=15'
    Check 'battery limit ends Rest Mode' ([bool](WaitLog 'Battery at 15% \(limit 20%\)' $m 30))
    Check 'battery: Stay -> sleep' ([bool](WaitLog '\[TEST\] would sleep' $m 60))
    Sim 'battery=off'; Sim 'ac=real'
    Start-Sleep 3
    Check 'battery: state clean' (StateClean)
    Nudge
    Say "--- T6b lid closed on battery -> sleep at the limit"
    SetSettings @{ EndAction = 'Hibernate' }
    $m = LogCount
    Sim 'ac=off'
    Sim 'lid-close'
    Check 'lid+battery entered' ([bool](WaitLog '\[INFO \] Resting\.' $m 120))
    $m = LogCount
    Sim 'battery=10'
    Check 'lid closed on battery -> Sleep (not Hibernate)' ([bool](WaitLog 'Ending Rest Mode \(battery at 10%\) -> Sleep' $m 30))
    Sim 'battery=off'; Sim 'ac=real'; Sim 'lid-open'
    SetSettings @{ EndAction = 'Stay' }
    Start-Sleep 3
    Nudge
}

# ---------------------------------------------------------------- T7 Windows power mode
if (Want 'powermode') {
    Say "--- T7 Windows power mode switch"
    SetSettings @{ FanMode = 'WindowsPowerMode' }
    $before = PowerMode
    $e = Enter
    Check 'power mode test entered' $e.Ok
    $during = PowerMode
    Check 'power mode = Best power efficiency while resting' ($during -eq '961cc777-2547-4f9d-8174-7d86181b8a7a') "before=$before during=$during"
    $w = Wake
    Start-Sleep 2
    Check 'power mode restored on wake' ((PowerMode) -eq $before) "after=$(PowerMode)"
    SetSettings @{ FanMode = 'Auto' }
}

# ---------------------------------------------------------------- T8 schedule
if (Want 'schedule') {
    Say "--- T8 schedule (rest at / wake at)"
    $t1 = (Get-Date).AddMinutes(1).ToString('HH:mm'); $t2 = (Get-Date).AddMinutes(3).ToString('HH:mm')
    $m = LogCount
    SetSettings @{ RestAt = $t1; WakeAt = $t2 }
    Check 'scheduled rest' ([bool](WaitLog "Schedule: it's $t1 - entering" $m 130))
    Check 'scheduled rest reached Resting' ([bool](WaitLog '\[INFO \] Resting\.' $m 120))
    Check 'scheduled wake' ([bool](WaitLog "Schedule: it's $t2 - waking" $m 200))
    SetSettings @{ RestAt = ''; WakeAt = '' }
    Start-Sleep 3
    Nudge
}

# ---------------------------------------------------------------- T9 crash recovery + reset script
if (Want 'crash') {
    Say "--- T9 crash recovery"
    $e = Enter
    Check 'crash test entered' $e.Ok
    Start-Sleep 3
    StopRM
    Check 'after kill: Rest Mode plan still active (expected)' ((ActivePlanName) -eq 'RestMode (temporary)')
    Nudge
    $m = LogCount
    Check 'restart after crash' (StartRM)
    Check 'crash recovery restored settings' ([bool](WaitLog 'did not exit cleanly' $m 20))
    Start-Sleep 20
    Check 'after recovery: plan Balanced, no temp plan' ((ActivePlanName) -eq 'Balanced' -and (TempPlans) -eq 0)
    Check 'after recovery: Lenovo mode restored by the helper task' ((LenovoMode) -eq '2') "mode=$(LenovoMode)"
    Check 'after recovery: state clean' (StateClean)

    Say "--- T9b RestMode-Reset.cmd after a crash"
    $e = Enter
    Start-Sleep 3
    StopRM
    Nudge
    cmd /c "echo.| `"$Data\RestMode-Reset.cmd`"" | Out-File "$Out\reset-cmd.txt" -Encoding utf8
    Start-Sleep 5
    Check 'Reset.cmd restored the plan' ((ActivePlanName) -eq 'Balanced' -and (TempPlans) -eq 0) (ActivePlanName)
    Check 'Reset.cmd restored the lid' ((Lid) -eq $originalLid) "lid=$(Lid)"
    Check 'Reset.cmd: state clean' (StateClean)
    Check 'restart after Reset.cmd' (StartRM)
}

# ---------------------------------------------------------------- T10 exit restores the lid
if (Want 'exit') {
    Say "--- T10 exiting RestMode gives the lid its own setting back"
    Start-Sleep 3
    Check 'lid Do nothing while running' ((Lid) -eq '0/0') "lid=$(Lid)"
    & $RM --restore --quiet | Out-Null
    Start-Sleep 5
    Check 'RestMode exited' (-not (Running))
    Check 'lid back to original after exit' ((Lid) -eq $originalLid) "lid=$(Lid) original=$originalLid"
    Check 'no lid backup left' (-not (Test-Path (Join-Path $Data 'lid-backup.json')))
}

# ---------------------------------------------------------------- results
$script:Results | Export-Csv "$Out\results.csv" -NoTypeInformation -Encoding utf8
$fail = @($script:Results | Where-Object Result -eq 'FAIL')
Say "=== DONE: $($script:Results.Count) checks, $($fail.Count) failed. Lid now $(Lid), plan $(ActivePlanName), power mode $(PowerMode)"
$fail | Format-Table -AutoSize | Out-String -Width 250
} finally {
    & "$PSScriptRoot\cleanup.ps1"
}
