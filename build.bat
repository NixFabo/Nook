@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
title Building Nook

rem --- Find the C# compiler (ships with Windows, .NET Framework 4.x) -----
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo The C# compiler from .NET Framework 4 was not found.
  pause
  exit /b 1
)

rem --- A folder with "test" in its name builds the test channel NookTest.exe -
set "OUT=Nook.exe"
for %%I in ("%~dp0.") do set "DIRNAME=%%~nxI"
echo %DIRNAME% | findstr /i "test" >nul
if not errorlevel 1 set "OUT=NookTest.exe"

rem --- Quit a running instance cleanly (restores the Windows taskbar) ------
tasklist /fi "imagename eq %OUT%" 2>nul | find /i "%OUT%" >nul
if not errorlevel 1 (
  echo Closing running %OUT% ...
  if exist "%OUT%" start "" /wait "%OUT%" --quit
  timeout /t 2 /nobreak >nul
)

set BASE=/nologo /codepage:65001 /target:winexe /optimize+ /out:%OUT% /lib:"%FW%\WPF" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll
if exist "nook.ico" set BASE=%BASE% /win32icon:nook.ico

rem --- Full variant with the Windows media API (cover art, progress) ------
set "WM=%WINDIR%\System32\WinMetadata"
set WRT=/define:WINRT /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Storage.winmd"
for %%F in (System.Runtime.dll System.Runtime.InteropServices.WindowsRuntime.dll System.Threading.Tasks.dll System.IO.dll) do (
  if exist "%FW%\%%F" set WRT=!WRT! /r:"%FW%\%%F"
)

echo Building Nook ...
"%FW%\csc.exe" %BASE% %WRT% Nook.cs > build.log 2>&1
if errorlevel 1 (
  echo   Full variant not possible, building the basic variant without cover art ...
  echo   ^(the reason is in build.log^)
  "%FW%\csc.exe" %BASE% Nook.cs > build-basic.log 2>&1
  if errorlevel 1 (
    type build-basic.log
    echo.
    echo Build failed. Details are in build.log and build-basic.log
    pause
    exit /b 1
  )
  set "VARIANT=basic (media keys only, no cover art)"
) else (
  set "VARIANT=full (cover art and progress)"
  if exist build-basic.log del build-basic.log
)

echo.
echo Done: %OUT%  -  variant: !VARIANT!
echo.
choice /c YN /m "Start Nook now"
if errorlevel 2 exit /b 0
start "" "%OUT%"
exit /b 0
