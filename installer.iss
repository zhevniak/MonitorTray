; Установщик MonitorTray (Inno Setup 6). Ставит программу для текущего пользователя в
; %LOCALAPPDATA%\Programs\MonitorTray — без прав администратора; ярлыки, автозапуск по выбору,
; запись в «Параметры → Приложения» и удаление делает сам Inno Setup.
;
; Сборка: build.cmd (или ISCC installer.iss) — рядом должен лежать собранный MonitorTray.exe
; Тихая установка (так ставит автообновление): MonitorTraySetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

#ifndef AppVersion
  ; версия — из самого MonitorTray.exe, три числа («1.4.2»)
  #define VerMajor
  #define VerMinor
  #define VerRev
  #define VerBuild
  #expr GetVersionComponents("MonitorTray.exe", VerMajor, VerMinor, VerRev, VerBuild)
  #define AppVersion Str(VerMajor) + "." + Str(VerMinor) + "." + Str(VerRev)
#endif

[Setup]
AppId={{8C6A2B9E-4F1D-4E7A-9B3C-5D2E1F0A7B64}
AppName=MonitorTray
AppVersion={#AppVersion}
AppVerName=MonitorTray {#AppVersion}
AppPublisher=MonitorTray project
AppPublisherURL=https://github.com/zhevniak/MonitorTray
AppSupportURL=https://github.com/zhevniak/MonitorTray/issues
AppUpdatesURL=https://github.com/zhevniak/MonitorTray/releases
DefaultDirName={localappdata}\Programs\MonitorTray
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=MonitorTraySetup
SetupIconFile=MonitorTray.ico
UninstallDisplayIcon={app}\MonitorTray.exe
UninstallDisplayName=MonitorTray
WizardStyle=modern
; язык установщика — по языку Windows, без лишнего окна выбора
ShowLanguageDialog=auto
Compression=lzma2/max
SolidCompression=yes
; запущенную программу закрыть перед заменой файла (в тихом режиме — без вопросов)
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany=MonitorTray project
VersionInfoDescription=MonitorTray Setup
VersionInfoProductName=MonitorTray
VersionInfoCopyright=Copyright (c) 2026 MonitorTray contributors (MIT)

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.AutoStart=Start MonitorTray when I sign in to Windows
ru.AutoStart=Запускать MonitorTray при входе в Windows

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "MonitorTray.exe"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; деинсталлятор от прежнего самодельного установщика (версии до 1.4.2)
Type: files; Name: "{app}\Uninstall.exe"

[UninstallDelete]
; папку мог создать ещё прежний установщик — Inno Setup такую сам не удаляет
Type: dirifempty; Name: "{app}"

[Icons]
Name: "{userprograms}\MonitorTray"; Filename: "{app}\MonitorTray.exe"
Name: "{userdesktop}\MonitorTray"; Filename: "{app}\MonitorTray.exe"; Tasks: desktopicon; Check: DesktopIconWanted

[Registry]
; запись прежнего установщика в «Приложениях» — теперь её ведёт Inno Setup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorTray"; Flags: deletekey dontcreatekey
; автозапуск: включить по выбору, иначе убрать (при тихом обновлении не трогаем — выбор остаётся
; тем, что пользователь задал в меню программы); при удалении программы — убрать в любом случае
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MonitorTray"; ValueData: """{app}\MonitorTray.exe"""; Tasks: autostart; Check: not WizardSilent
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MonitorTray"; Flags: deletevalue; Tasks: not autostart; Check: not WizardSilent
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MonitorTray"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MonitorTray.exe"; Description: "{cm:LaunchProgram,MonitorTray}"; Flags: nowait postinstall skipifsilent
; тихая установка (автообновление) — сразу запустить новую версию
Filename: "{app}\MonitorTray.exe"; Flags: nowait; Check: WizardSilent

[Code]
// Закрыть запущенную программу: сначала попросить её выйти самой (MonitorTray.exe --exit —
// она разбудит спящие мониторы и уберёт значок), версии до 1.4.2 этого не умеют — тогда принудительно.
procedure CloseRunningApp();
var
  Code: Integer;
begin
  if FileExists(ExpandConstant('{app}\MonitorTray.exe')) then
    Exec(ExpandConstant('{app}\MonitorTray.exe'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im MonitorTray.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningApp();
  Result := True;
end;

// ярлык на рабочем столе: при обычной установке — по галочке, при тихом обновлении — только если он уже был
function DesktopIconWanted(): Boolean;
begin
  Result := (not WizardSilent) or FileExists(ExpandConstant('{userdesktop}\MonitorTray.lnk'));
end;
