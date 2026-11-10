; Launchly kurulum betiği (Inno Setup 6).
; Derleme: installer\build.ps1 (önce self-contained publish alır, sonra ISCC çalıştırır).

#define AppName "Launchly"
#define AppVersion "1.0.0"
#define AppExe "Launchly.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
AppId={{6F3C2A1E-8B4D-4C7A-9E2F-1A5B7C9D0E11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=SametDrs
AppPublisherURL=https://github.com/SametDrs/Launchly
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Kullanıcı kapsamında kurulur; yönetici izni gerekmez.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillLaunchly"

[Registry]
; Ayarlardan açılan otomatik başlatma kaydı kaldırmada silinir.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: uninsdeletevalue dontcreatekey

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Kullanıcı verisi (%AppData%\Launchly: ayarlar, önbellek, loglar) yalnızca onayla silinir.
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Ayarlar ve önbellek (%AppData%\Launchly) de silinsin mi?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Launchly'), True, True, True);
end;
