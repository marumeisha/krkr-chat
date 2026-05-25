#define MyAppName "SecureChat Client"
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
AppId={{A4B94D5E-7E17-47EA-9EC7-8D9D9A7FA4AF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=SecureChat
DefaultDirName={autopf}\SecureChat Client
DefaultGroupName=SecureChat
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=SecureChat.Client.Setup
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
Name: "{group}\SecureChat Client"; Filename: "{app}\SecureChat.Client.exe"
Name: "{autodesktop}\SecureChat Client"; Filename: "{app}\SecureChat.Client.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SecureChat.Client.exe"; Description: "Launch SecureChat Client"; Flags: nowait postinstall skipifsilent
