; Launchly kurulum betiği (Inno Setup 6).
; Derleme: installer\build.ps1 (önce self-contained publish alır, sonra ISCC çalıştırır).

#define AppName "Launchly"
#define AppVersion "1.1.0"
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
OutputBaseFilename=Launchly-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Launchly.App\Assets\Launchly.ico
; Exe ikonu taşıdığı için kaldırma girdisi de logoyu gösterir.
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

[CustomMessages]
; Sihirbazın kendi özel metinleri iki dilde. Görev adları ve "Launchly'yi başlat" satırı Inno'nun yerleşik
; (cm:CreateDesktopIcon, cm:AdditionalIcons, cm:LaunchProgram, cm:UninstallProgram) iki dilli metinleridir.
turkish.DeleteUserData=Ayarlar ve önbellek (%1) de silinsin mi?
english.DeleteUserData=Also delete settings and cache (%1)?

[Code]
const
  // Uygulama bu dosyayı açılışta okur (Launchly.Core: InstallerLanguage) ve ilk açılış dilini belirler.
  LanguageMarkerName = 'install-language.txt';

procedure CurStepChanged(CurStep: TSetupStep);
var
  DataDir, Lang: String;
begin
  if CurStep = ssPostInstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Launchly');
    // Yalnızca İLK kurulumda (ayar dosyası yokken) yazılır: güncelleme/yeniden kurulum kullanıcının
    // ayarlardan seçtiği dili ezmez. Uygulama dosyayı bir kez okuyup siler ve yalnızca dil ayarı Auto iken uygular.
    if not FileExists(DataDir + '\settings.json') then
    begin
      if ActiveLanguage = 'turkish' then
        Lang := 'tr'
      else
        Lang := 'en';
      ForceDirectories(DataDir);
      SaveStringToFile(DataDir + '\' + LanguageMarkerName, Lang, False);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  // Kullanıcı verisi (%AppData%\Launchly: ayarlar, önbellek, loglar) yalnızca onayla silinir.
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Launchly');
    if MsgBox(FmtMessage(CustomMessage('DeleteUserData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
  end;
end;
