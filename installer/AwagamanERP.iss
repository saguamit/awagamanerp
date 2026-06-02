#define MyAppName "Awagaman ERP"
#define MyAppVersion "1.0.10"
#define MyAppPublisher "Awagaman ERP"
#define MyAppExeName "Awagaman ERP.exe"
#define MySourceDir "c:\amit sagu\awagaman project\ATL ERP\Awagaman ERP\bin\Release"

[Setup]
AppId={{CC0D8D4A-A778-4CD8-9F47-D4C6AA12E33A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Awagaman ERP
DefaultGroupName=Awagaman ERP
DisableProgramGroupPage=yes
OutputDir=c:\amit sagu\awagaman project\ATL ERP\dist
OutputBaseFilename=AwagamanERP-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=c:\amit sagu\awagaman project\ATL ERP\Awagaman ERP\logo.ico
UninstallDisplayIcon={app}\Awagaman ERP.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "c:\amit sagu\awagaman project\ATL ERP\installer\prereqs\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "c:\amit sagu\awagaman project\ATL ERP\installer\prereqs\VC_redist.x86.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#MySourceDir}\Awagaman ERP.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Awagaman ERP.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\*.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\logo.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\lr_format_layout.default.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\de\*"; DestDir: "{app}\de"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MySourceDir}\x64\*"; DestDir: "{app}\x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MySourceDir}\x86\*"; DestDir: "{app}\x86"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Awagaman ERP"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\logo.ico"
Name: "{autodesktop}\Awagaman ERP"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\logo.ico"

[Run]
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; Flags: runhidden waituntilterminated
Filename: "{tmp}\VC_redist.x86.exe"; Parameters: "/install /quiet /norestart"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Awagaman ERP}"; Flags: nowait postinstall skipifsilent

