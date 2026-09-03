@echo off
setlocal
cd /d "%~dp0"
call BUILD_BETA.cmd
if not exist "DW2ModLauncherBeta.exe" exit /b 1
start "" "%~dp0DW2ModLauncherBeta.exe"
