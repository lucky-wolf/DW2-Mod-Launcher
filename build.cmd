@echo off
setlocal
cd /d "%~dp0"

set "VALIDATE=1"
for %%A in (%*) do if /I "%%A"=="--no-validate" set "VALIDATE=0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK was not found on PATH.
  echo Install the .NET 10 SDK from https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

if "%VALIDATE%"=="1" (
  echo Fixing formatting...
  dotnet format DW2ModLauncher.sln
  if errorlevel 1 (
    echo.
    echo [ERROR] dotnet format failed to run.
    pause
    exit /b 1
  )
)

echo Building DW2 Mod Launcher...
dotnet build DW2ModLauncher.sln -c Release
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  pause
  exit /b 1
)

if "%VALIDATE%"=="1" (
  echo Running tests...
  dotnet test src\DW2ModLauncher.Tests -c Release --no-build
  if errorlevel 1 (
    echo.
    echo [ERROR] Tests failed - this would fail CI.
    echo Use "build.cmd --no-validate" to skip validation.
    pause
    exit /b 1
  )
)

echo [OK] Build complete: src\DW2ModLauncher.Avalonia\bin\Release\net10.0\DW2ModLauncher.exe
pause
