@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] Windows .NET Framework C# compiler was not found.
  echo Install/enable .NET Framework 4.x or build Program.cs with Visual Studio.
  pause
  exit /b 1
)

echo Building DW2 Mod Launcher BETA v0.4.6 CONFLICT FILTER FIX...
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"DW2ModLauncherBeta.exe" ^
 /reference:System.dll ^
 /reference:System.Core.dll ^
 /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll ^
 /reference:System.Web.Extensions.dll ^
 /reference:System.Xml.dll ^
 "Program.cs"
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  pause
  exit /b 1
)

echo [OK] DW2ModLauncherBeta.exe created.
pause
