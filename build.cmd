@echo off
rem === Сборка MonitorTray штатным компилятором Windows (.NET Framework 4.x) ===
rem Ничего устанавливать не нужно: используется csc.exe из состава Windows.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set REF=/r:System.dll /r:System.Core.dll /r:System.Management.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll

rem --- основная программа (шрифт Roboto встраивается из папки fonts) ---
"%CSC%" /nologo /codepage:65001 /target:winexe /win32icon:MonitorTray.ico /win32manifest:app.manifest /res:fonts\Roboto-Regular.ttf.gz,Roboto-Regular.ttf.gz /res:fonts\Roboto-Medium.ttf.gz,Roboto-Medium.ttf.gz /res:fonts\Roboto-SemiBold.ttf.gz,Roboto-SemiBold.ttf.gz /out:MonitorTray.exe %REF% MonitorTray.cs
if errorlevel 1 exit /b 1

rem --- установщик (Inno Setup): собирается, если найден ISCC.exe ---
set ISCC=
if exist "build\inno\ISCC.exe" set ISCC=build\inno\ISCC.exe
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not defined ISCC (
  echo Build OK: MonitorTray.exe  ^(Inno Setup not found - installer skipped^)
  exit /b 0
)
"%ISCC%" /Q installer.iss
if errorlevel 1 exit /b 1

echo Build OK: MonitorTray.exe + MonitorTraySetup.exe
