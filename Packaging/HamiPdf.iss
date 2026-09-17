#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef AppVersion
  #define AppVersion "0.9.2"
#endif
[Setup]
AppId={{8B43D632-67AA-45C5-BE93-F759615B6C19}
AppName=HamiPdf
AppVersion={#AppVersion}
AppPublisher=Awadh Faghmah
AppCopyright=Copyright © 2026 Awadh Faghmah
DefaultDirName={commonpf64}\HamiPdf
UsePreviousAppDir=no
DisableDirPage=yes
DefaultGroupName=HamiPdf
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
ChangesAssociations=yes
SetupIconFile=..\Assets\HamiPdf.ico
UninstallDisplayIcon={app}\HamiPdf.exe
OutputDir={#OutputDir}
OutputBaseFilename=HamiPdf-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{commonprograms}\HamiPdf"; Filename: "{app}\HamiPdf.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\HamiPdf"; Filename: "{app}\HamiPdf.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Registry]
Root: HKLM; Subkey: "Software\Classes\.hamipdf"; ValueType: string; ValueName: ""; ValueData: "HamiPdf.Project"; Flags: createvalueifdoesntexist uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\.hamipdf\OpenWithProgids"; ValueType: string; ValueName: "HamiPdf.Project"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\HamiPdf.Project"; ValueType: string; ValueName: ""; ValueData: "HamiPdf Project"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\HamiPdf.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\HamiPdf.exe"",0"
Root: HKLM; Subkey: "Software\Classes\HamiPdf.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\HamiPdf.exe"" ""%1"""
Root: HKLM; Subkey: "Software\Classes\Applications\HamiPdf.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "HamiPdf"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Applications\HamiPdf.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\HamiPdf.exe"" ""%1"""
Root: HKLM; Subkey: "Software\Classes\Applications\HamiPdf.exe\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\HamiPdf.exe\SupportedTypes"; ValueType: string; ValueName: ".hamipdf"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\.pdf\OpenWithList\HamiPdf.exe"; Flags: uninsdeletekey
[Run]
Filename: "{app}\HamiPdf.exe"; Description: "Launch HamiPdf"; Flags: nowait postinstall skipifsilent
[Code]
function InitializeSetup: Boolean;
var
  OldKey: String;
begin
  OldKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B43D632-67AA-45C5-BE93-F759615B6C19}_is1';
  Result := not (RegKeyExists(HKCU64, OldKey) or RegKeyExists(HKCU32, OldKey));
  if not Result then
    MsgBox('A per-user HamiPdf installation still exists. Cancel Setup and run install-machine.ps1 from your normal PowerShell session, or uninstall the old HamiPdf first from Windows Settings.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CurrentHandler: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKLM64, 'Software\Classes\.hamipdf', '', CurrentHandler) then
      if CurrentHandler = 'HamiPdf.Project' then
        RegDeleteValue(HKLM64, 'Software\Classes\.hamipdf', '');
end;
