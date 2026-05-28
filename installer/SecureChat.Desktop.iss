#define MyAppName "SecureChat Desktop"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #error SourceDir is required. Pass /DSourceDir=... when invoking ISCC.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6633B362-470C-4A17-8F25-585A1F5C808A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=SecureChat
DefaultDirName={autopf}\SecureChat Desktop
DefaultGroupName=SecureChat
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=SecureChat.Desktop.Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:";

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SecureChat Desktop"; Filename: "{app}\SecureChat.Desktop.exe"
Name: "{autodesktop}\SecureChat Desktop"; Filename: "{app}\SecureChat.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SecureChat.Desktop.exe"; Description: "Launch SecureChat Desktop"; Flags: nowait postinstall skipifsilent