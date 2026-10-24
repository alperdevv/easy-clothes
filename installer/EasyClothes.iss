; Installer for EasyClothes, built by installer/build.sh with Inno Setup 6.
; Installs per user (no administrator prompt) and opens .ecproj projects with the app.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<published app folder>
#endif

[Setup]
AppId={{1ADBEC9E-D749-4398-947B-4A9E2C878A49}
AppName=EasyClothes
AppVersion={#AppVersion}
AppVerName=EasyClothes {#AppVersion}
AppPublisher=David
VersionInfoVersion={#AppVersion}
VersionInfoProductName=EasyClothes
VersionInfoDescription=Instalador de EasyClothes
DefaultDirName={autopf}\EasyClothes
DefaultGroupName=EasyClothes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=EasyClothes-{#AppVersion}-setup
SetupIconFile=..\src\EasyClothes.App\Assets\icon.ico
UninstallDisplayIcon={app}\EasyClothes.App.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
ChangesAssociations=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\EasyClothes"; Filename: "{app}\EasyClothes.App.exe"
Name: "{autodesktop}\EasyClothes"; Filename: "{app}\EasyClothes.App.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.ecproj"; ValueType: string; ValueName: ""; ValueData: "EasyClothes.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\EasyClothes.Project"; ValueType: string; ValueName: ""; ValueData: "Proyecto de EasyClothes"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\EasyClothes.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\EasyClothes.App.exe,0"
Root: HKA; Subkey: "Software\Classes\EasyClothes.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\EasyClothes.App.exe"" ""%1"""

[Run]
Filename: "{app}\EasyClothes.App.exe"; Description: "{cm:LaunchProgram,EasyClothes}"; Flags: nowait postinstall skipifsilent
; The app's update button runs the setup with /SILENT /RELAUNCH and closes; this opens it again.
Filename: "{app}\EasyClothes.App.exe"; Flags: nowait; Check: Relaunch

[Code]
function Relaunch: Boolean;
var
  i: Integer;
begin
  Result := False;
  for i := 1 to ParamCount do
    if CompareText(ParamStr(i), '/RELAUNCH') = 0 then
      Result := True;
end;
