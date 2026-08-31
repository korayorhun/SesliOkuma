; Inno Setup script for Sesli Okuma (per-user install, no admin rights needed)
#define MyAppName "Sesli Okuma"
#define MyAppVersion "1.11.0"
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
CloseApplications=yes
CloseApplicationsFilter=*.exe
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoAfterFile: "after-install.en.txt"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"; InfoAfterFile: "after-install.tr.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoAfterFile: "after-install.es.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; InfoAfterFile: "after-install.fr.txt"
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; InfoAfterFile: "after-install.pt.txt"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"; InfoAfterFile: "after-install.ar.txt"
Name: "hindi"; MessagesFile: "compiler:Languages\Hindi.islu"; InfoAfterFile: "after-install.en.txt"

[CustomMessages]
english.StartupTask=Start with Windows
english.RunNow=Start Sesli Okuma now
english.ContextMenu=Read with Sesli Okuma
english.Uninstall=Uninstall
turkish.StartupTask=Windows ile birlikte başlat
turkish.RunNow=Sesli Okuma'yı şimdi başlat
turkish.ContextMenu=Sesli Okuma ile oku
turkish.Uninstall=Kaldır
spanish.StartupTask=Iniciar con Windows
spanish.RunNow=Iniciar Sesli Okuma ahora
spanish.ContextMenu=Leer con Sesli Okuma
spanish.Uninstall=Desinstalar
french.StartupTask=Démarrer avec Windows
french.RunNow=Lancer Sesli Okuma maintenant
french.ContextMenu=Lire avec Sesli Okuma
french.Uninstall=Désinstaller
portuguese.StartupTask=Iniciar com o Windows
portuguese.RunNow=Iniciar o Sesli Okuma agora
portuguese.ContextMenu=Ler com Sesli Okuma
portuguese.Uninstall=Desinstalar
arabic.StartupTask=التشغيل مع Windows
arabic.RunNow=تشغيل Sesli Okuma الآن
arabic.ContextMenu=قراءة باستخدام Sesli Okuma
arabic.Uninstall=إزالة
hindi.StartupTask=Windows के साथ शुरू करें
hindi.RunNow=Sesli Okuma अभी शुरू करें
hindi.ContextMenu=Sesli Okuma से पढ़ें
hindi.Uninstall=अनइंस्टॉल

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"

[Files]
Source: "..\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} - {cm:Uninstall}"; Filename: "{uninstallexe}"
Name: "{userstartup}\SesliOkuma"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:RunNow}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSelfUpdate

[Registry]
; "Read with Sesli Okuma" on plain-text files (per user; removed on uninstall)
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\SesliOkuma"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenu}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\SesliOkuma"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\SesliOkuma\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --read ""%1"""
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\SesliOkuma"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenu}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\SesliOkuma"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\SesliOkuma\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --read ""%1"""

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