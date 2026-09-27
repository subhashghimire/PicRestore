; Inno Setup 6 script for PicRestore.
; Normally compiled by build\package.ps1, which publishes the app first and passes the defines below.
; Manual use:  ISCC.exe /DAppSource=<published folder> /DAppVersion=1.0.0 installer\PicRestore.iss

#ifndef AppSource
  #error AppSource must point at the published app folder - run build\package.ps1
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

#define AppName "PicRestore"
#define AppExe "PicRestore.App.exe"

[Setup]
; AppId identifies the product for upgrades and uninstall - never change it.
AppId={{8B3F2C1E-6A4D-4E7B-9C21-5D0F7A3B9E42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppComments=Faithful restoration of old photographs
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs for the current user without admin rights; the user can choose "all users" in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=PicRestore-Setup-{#AppVersion}-{#Arch}
SetupIconFile=..\src\PicRestore.App\Assets\PicRestore.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef RuntimeInstaller
Source: "{#RuntimeInstaller}"; DestDir: "{tmp}"; DestName: "WindowsAppRuntimeInstall.exe"; Flags: deleteafterinstall
#endif

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
#ifdef RuntimeInstaller
Filename: "{tmp}\WindowsAppRuntimeInstall.exe"; Parameters: "--quiet"; StatusMsg: "Installing the Windows App Runtime..."; Flags: waituntilterminated runhidden
#endif
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Uninstall removes the program only. Your training pairs, trained model and downloaded AI model in
; %LOCALAPPDATA%\PicRestore are kept, so reinstalling or upgrading doesn't lose them.
