#define MyAppName "Awagaman ERP"
#define MyAppVersion "1.0.52"
#define MyAppPublisher "Awagaman ERP"
#define MyAppExeName "Awagaman ERP.exe"
#define MySourceDir "c:\amit sagu\awagaman project\ATL ERP_pre_multiuser\Awagaman ERP\bin\Release"

[Setup]
AppId={{CC0D8D4A-A778-4CD8-9F47-D4C6AA12E33A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Awagaman ERP
DefaultGroupName=Awagaman ERP
DisableProgramGroupPage=yes
OutputDir=c:\amit sagu\awagaman project\ATL ERP_pre_multiuser\dist
OutputBaseFilename=AwagamanERP-Setup-v1.0.52
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=c:\amit sagu\awagaman project\ATL ERP_pre_multiuser\Awagaman ERP\logo.ico
UninstallDisplayIcon={app}\Awagaman ERP.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "c:\amit sagu\awagaman project\ATL ERP_pre_multiuser\installer\prereqs\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "c:\amit sagu\awagaman project\ATL ERP_pre_multiuser\installer\prereqs\VC_redist.x86.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
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

[Code]
var
  InstallModePage: TInputOptionWizardPage;
  ServerUrlPage: TInputQueryWizardPage;

function NormalizeApiUrl(const Value: string): string;
var
  Text: string;
begin
  Text := Trim(Value);
  if Text = '' then
    Text := 'http://localhost:5088';
  if Pos('http://', LowerCase(Text)) <> 1 then
    if Pos('https://', LowerCase(Text)) <> 1 then
      Text := 'http://' + Text;
  if (Length(Text) > 0) and (Text[Length(Text)] = '/') then
    Delete(Text, Length(Text), 1);
  Result := Text;
end;

function NetworkSettingsPath: string;
begin
  Result := ExpandConstant('{commonappdata}\Awagaman ERP\network.settings.json');
end;

procedure InitializeWizard;
begin
  InstallModePage := CreateInputOptionPage(
    wpSelectDir,
    'Connection Mode',
    'Choose how this installation should connect to data',
    'Use the VPS cloud server unless you are testing another API server.',
    False,
    False);
  InstallModePage.Add('Use Awagaman VPS cloud server');
  InstallModePage.Add('Use custom API URL');
  InstallModePage.SelectedValueIndex := 0;

  ServerUrlPage := CreateInputQueryPage(
    InstallModePage.ID,
    'Server Address',
    'Enter the server URL',
    'Leave the default for the Awagaman VPS cloud server.');
  ServerUrlPage.Add('API Base URL:', False);
  ServerUrlPage.Values[0] := 'http://187.127.157.47:5088';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = ServerUrlPage.ID then
  begin
    if InstallModePage.SelectedValueIndex = 0 then
      ServerUrlPage.Values[0] := 'http://187.127.157.47:5088';
  end;
end;

procedure WriteNetworkSettings;
var
  JsonText: string;
  ApiUrl: string;
  DirName: string;
  ServerMode: Boolean;
  LocalApiPath: string;
begin
  ServerMode := False;
  if InstallModePage.SelectedValueIndex = 0 then
    ApiUrl := 'http://187.127.157.47:5088'
  else
    ApiUrl := NormalizeApiUrl(ServerUrlPage.Values[0]);

  DirName := ExtractFileDir(NetworkSettingsPath);
  if not DirExists(DirName) then
    ForceDirectories(DirName);

  LocalApiPath := ExpandConstant('{app}\ApiServer\Awagaman.Api.exe');
  StringChangeEx(LocalApiPath, '\', '\\', True);

  JsonText :=
    '{' + #13#10 +
    '  "UseRemoteApi": true,' + #13#10 +
    '  "ApiBaseUrl": "' + ApiUrl + '",' + #13#10 +
    '  "RunLocalApiServer": __SERVERMODE__,' + #13#10 +
    '  "LocalApiExecutablePath": "' + LocalApiPath + '"' + #13#10 +
    '}';
  if ServerMode then
    StringChangeEx(JsonText, '__SERVERMODE__', 'true', True)
  else
    StringChangeEx(JsonText, '__SERVERMODE__', 'false', True);
  SaveStringToFile(NetworkSettingsPath, JsonText, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteNetworkSettings;
end;




