@echo off
setlocal
cd /d "%~dp0"
rem Builds NookSetup.exe from ..\Nook.exe (run ..\build.bat first)
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "..\Nook.exe" (
  echo ..\Nook.exe not found - run build.bat in the main folder first.
  pause
  exit /b 1
)
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:..\NookSetup.exe /win32icon:..\nook.ico /win32manifest:setup.manifest /resource:..\Nook.exe,Nook.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll Setup.cs SetupVersion.cs
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
echo Done: NookSetup.exe
pause
