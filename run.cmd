@echo off
setlocal
cd /d "%~dp0"
call build.cmd %*
set "EXE=%~dp0src\DW2ModLauncher.App\bin\Release\net10.0-windows\DW2ModLauncher.exe"
if not exist "%EXE%" exit /b 1
start "" "%EXE%"
