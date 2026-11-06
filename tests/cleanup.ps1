# Removes everything a test run leaves behind: stops RestMode and test downloads, restores every
# setting with RestMode's own restore, and deletes the test-mode files. Safe to run any time.
param([string]$Exe = (Join-Path $PSScriptRoot '..\dist\RestMode.exe'))
$data = Join-Path $env:LOCALAPPDATA 'RestMode'
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -match 'run-tests\.ps1' -and $_.ProcessId -ne $PID } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-Process curl -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
# After the uninstall test the data folder is gone on purpose: then there is nothing to restore.
if ((Test-Path $Exe) -and (Test-Path $data)) { & $Exe --restore --quiet | Out-Null; Start-Sleep 5 }
Get-Process RestMode -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
foreach ($f in 'testmode.flag', 'testmode-nosleep.flag', 'simulate.txt') {
    Remove-Item (Join-Path $data $f) -Force -ErrorAction SilentlyContinue
}
# Put the user's own settings back (run-tests.ps1 saved them before writing test settings).
$saved = Join-Path $data 'settings.json.before-tests'
if (Test-Path $saved) { Move-Item $saved (Join-Path $data 'settings.json') -Force }
"cleanup done: RestMode running=$([bool](Get-Process RestMode -ErrorAction SilentlyContinue)), test flags left=$((Test-Path (Join-Path $data 'testmode.flag')))"
