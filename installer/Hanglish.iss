; Windows installer for Hanglish (Inno Setup 6.4+ / 7).
; Build: ISCC /DVersion=0.2.0 /DSource=<publish folder> installer\Hanglish.iss
; Installs per user (no administrator prompt) and adds Start menu / desktop shortcuts.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Source
  #define Source "..\out\Hanglish"
#endif

[Setup]
AppId={{6F3B8E2A-5C41-4D7B-9A2E-48D1C7B0A913}
AppName=한글리쉬 Hanglish
AppVersion={#Version}
AppVerName=한글리쉬 {#Version}
AppPublisher=Kim Minjae
AppPublisherURL=https://mingjaam.github.io/hanglish/
AppSupportURL=https://github.com/Mingjaam/hanglish/issues
DefaultDirName={autopf}\Hanglish
DefaultGroupName=한글리쉬
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\out
OutputBaseFilename=Hanglish-Setup
SetupIconFile=..\assets\Hanglish.ico
UninstallDisplayIcon={app}\Hanglish.exe
UninstallDisplayName=한글리쉬 Hanglish
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로가기 만들기"; GroupDescription: "바로가기:"
Name: "startup"; Description: "Windows를 시작할 때 자동으로 실행"; GroupDescription: "실행:"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\한글리쉬"; Filename: "{app}\Hanglish.exe"; Comment: "한/영 키 없이 한글과 영어 치기"
Name: "{autodesktop}\한글리쉬"; Filename: "{app}\Hanglish.exe"; Tasks: desktopicon

[Registry]
; The same value the app's own "Windows 시작할 때 실행" setting writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Hanglish"; \
  ValueData: """{app}\Hanglish.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Hanglish.exe"; Description: "한글리쉬 실행하기"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Hanglish.exe /F"; Flags: runhidden; RunOnceId: "StopHanglish"

[Code]
// An older copy may be running from the tray; stop it so its files can be replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Hanglish.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

// The app's own "start with Windows" setting may have added this value even without the task.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Hanglish');
end;
