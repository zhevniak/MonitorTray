@echo off
rem === Сборка MonitorTray штатным компилятором Windows (.NET Framework 4.x) ===
rem Ничего устанавливать не нужно: используется csc.exe из состава Windows.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set REF=/r:System.dll /r:System.Core.dll /r:System.Management.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll

rem --- иконка (генерируется, если её ещё нет) ---
if not exist MonitorTray.ico (
  "%CSC%" /nologo /target:exe /r:System.Drawing.dll /out:icongen.exe icongen.cs || exit /b 1
  icongen.exe || exit /b 1
)

rem --- основная программа ---
"%CSC%" /nologo /codepage:65001 /target:winexe /win32icon:MonitorTray.ico /res:MonitorTray.ico /win32manifest:app.manifest /out:MonitorTray.exe %REF% MonitorTray.cs
if errorlevel 1 exit /b 1

rem --- деинсталлятор (промежуточный файл) ---
if not exist build mkdir build
"%CSC%" /nologo /codepage:65001 /target:winexe /win32icon:MonitorTray.ico /out:build\Uninstall.exe /r:System.dll /r:System.Windows.Forms.dll Uninstall.cs
if errorlevel 1 exit /b 1

rem --- установщик (упаковывает программу и деинсталлятор внутрь себя) ---
"%CSC%" /nologo /codepage:65001 /target:winexe /win32icon:MonitorTray.ico /out:MonitorTraySetup.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /res:MonitorTray.exe,MonitorTray.exe /res:build\Uninstall.exe,Uninstall.exe Setup.cs
if errorlevel 1 exit /b 1

echo Build OK: MonitorTray.exe + MonitorTraySetup.exe
