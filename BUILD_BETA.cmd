@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK was not found on PATH.
  echo Install the .NET 8 SDK from https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

echo Building DW2 Mod Launcher BETA...
dotnet build DW2ModLauncher.sln -c Release
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  pause
  exit /b 1
)

echo [OK] Build complete: src\DW2ModLauncher.App\bin\Release\net8.0-windows\DW2ModLauncherBeta.exe
pause
