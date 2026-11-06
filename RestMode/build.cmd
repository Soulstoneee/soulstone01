@echo off
rem Builds dist\RestMode.exe: one self-contained file, no .NET install needed to run it.
rem Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
setlocal
cd /d "%~dp0"
dotnet publish src\RestMode.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)
echo.
echo Done: %~dp0dist\RestMode.exe
