@echo off
setlocal
cd /d "%~dp0"
call BUILD_BETA.cmd
set "EXE=%~dp0src\DW2ModLauncher.App\bin\Release\net8.0-windows\DW2ModLauncherBeta.exe"
if not exist "%EXE%" exit /b 1
start "" "%EXE%"
