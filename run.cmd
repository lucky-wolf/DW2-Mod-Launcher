@echo off
setlocal
cd /d "%~dp0"
call build.cmd %*
set "EXE=%~dp0src\DW2ModLauncher.Avalonia\bin\Release\net10.0\DW2ModLauncher.exe"
if not exist "%EXE%" exit /b 1
start "" "%EXE%"
