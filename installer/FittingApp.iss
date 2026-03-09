; Inno Setup script for Ul8ziz Fitting App
; Produces a conventional Windows installer (setup.exe): wizard, Start Menu, Add/Remove Programs, optional desktop icon.
; Build: run scripts\publish-release.ps1 -BuildInstaller (Inno Setup is downloaded automatically if missing).

#define AppName "Ul8ziz Fitting App"
#define AppExe "Ul8ziz.FittingApp.App.exe"
#define AppPublisher "Ul8ziz"
#define AppURL "https://github.com/ul8ziz/FattingAPP"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion=1.0.0
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf32}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=..\publish
OutputBaseFilename=FittingApp-Setup-1.0.0
; SetupIconFile requires .ico from ImageMagick; .NET-generated .ico may be invalid for Inno
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; No ArchitecturesAllowed so the installer runs on 64-bit Windows; the installed app remains x86
PrivilegesRequired=admin
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Source = publish\FittingApp (relative to this script in installer\)
Source: "..\publish\FittingApp\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
