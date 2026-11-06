<#
.SYNOPSIS
  Builds RestMode into a single .exe in .\dist

.PARAMETER Small
  Build a ~1 MB exe that needs the ".NET 8 Desktop Runtime" installed,
  instead of the ~65 MB self-contained exe that runs anywhere.

.PARAMETER Arm64
  Build for Windows on ARM (e.g. Snapdragon laptops).

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Small
#>
param([switch]$Small, [switch]$Arm64)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$rid = if ($Arm64) { 'win-arm64' } else { 'win-x64' }
$selfContained = if ($Small) { 'false' } else { 'true' }

dotnet publish src/RestMode.csproj -c Release -r $rid --self-contained $selfContained -p:PublishSingleFile=true -o dist
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$exe = Join-Path $PSScriptRoot 'dist\RestMode.exe'
"{0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)
