; Inno Setup script for Sesli Okuma (per-user install, no admin rights needed)
#define MyAppName "Sesli Okuma"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Koray Orhun"
#define MyAppURL "https://github.com/korayorhun/SesliOkuma"
#define MyAppExeName "SesliOkuma.exe"

[Setup]
AppId={{B7E9A1C4-5D2F-4E7A-9C3B-6F1D8E2A4B50}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\SesliOkuma
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=SesliOkuma-Setup-{#MyAppVersion}
SetupIconFile=..\assets\SesliOkuma.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE.txt
InfoAfterFile=after-install.txt
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Windows ile birlikte başlat"; GroupDescription: "Ek seçenekler:"

[Files]
Source: "..\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} - Kaldır"; Filename: "{uninstallexe}"
Name: "{userstartup}\SesliOkuma"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Sesli Okuma'yı şimdi başlat"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSelfUpdate

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillSesliOkuma"

[UninstallDelete]
Type: files; Name: "{userstartup}\SesliOkuma.lnk"
Type: filesandordirs; Name: "{localappdata}\SesliOkuma"

[Code]
function IsSelfUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := '';
end;