; =====================================================================
; ZenArchieve Inno Setup Script (Windows 10 / 11 64-bit)
; Clean alternative to WinRAR/7-Zip in .NET 9 WPF
; =====================================================================

#define MyAppName "ZenArchieve"
#define MyAppVersion "2.1.0"
#define MyAppPublisher "ZenArchieve"
#define MyAppURL "https://github.com/Tabish955/ZenArchieve"
#define MyAppExeName "Archieve App.exe"

[Setup]
; Unique Application ID
AppId={{8F15A36B-B68C-4F7E-976E-6F3993E1B9A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Destination Directories
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Output Configuration
OutputDir=..\bin\installer
OutputBaseFilename=ZenArchieve_Setup_v2.1
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName},0

; Modern Styling for Windows 10 & 11
WizardStyle=modern

; Target 64-bit Windows
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Compression Settings
Compression=lzma2/ultra64
SolidCompression=yes

; Privileges & Changes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate_archives"; Description: "Register ZenArchieve as default handler for .zip, .7z, .rar, .tar, .gz"; GroupDescription: "File Associations:"
Name: "context_menu"; Description: "Add Explorer right-click context menus (Smart Extract & Compress)"; GroupDescription: "Shell Integration:"

[Files]
; Self-contained published .NET 9 application binaries
Source: "..\bin\Release\net9.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\assets\app.ico"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
; Start Menu and Desktop Shortcuts
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; ---------------------------------------------------------------------
; 1. File Associations (.zip, .7z, .rar, .tar, .gz)
; ---------------------------------------------------------------------
Root: HKA; Subkey: "Software\Classes\.zip"; ValueType: string; ValueData: "ZenArchieve.Archive"; Flags: uninsdeletevalue; Tasks: associate_archives
Root: HKA; Subkey: "Software\Classes\.7z"; ValueType: string; ValueData: "ZenArchieve.Archive"; Flags: uninsdeletevalue; Tasks: associate_archives
Root: HKA; Subkey: "Software\Classes\.rar"; ValueType: string; ValueData: "ZenArchieve.Archive"; Flags: uninsdeletevalue; Tasks: associate_archives
Root: HKA; Subkey: "Software\Classes\.tar"; ValueType: string; ValueData: "ZenArchieve.Archive"; Flags: uninsdeletevalue; Tasks: associate_archives
Root: HKA; Subkey: "Software\Classes\.gz"; ValueType: string; ValueData: "ZenArchieve.Archive"; Flags: uninsdeletevalue; Tasks: associate_archives

; Program ID Registration
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive"; ValueType: string; ValueData: "ZenArchieve Compressed Archive"; Flags: uninsdeletekey; Tasks: associate_archives
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associate_archives
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate_archives

; ---------------------------------------------------------------------
; 2. Shell Context Menu on Archives (Open, Extract Here, Extract Files..., Extract to Dedicated Folder)
; ---------------------------------------------------------------------
; A. CompressedFolder (Windows default ZIP handler on Windows 10/11)
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveOpen"; ValueType: string; ValueData: "Open with ZenArchieve"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveOpen"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveOpen\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveExtractFiles"; ValueType: string; ValueData: "Extract Files..."; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveExtractFiles"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveExtractFiles\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-files ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveSmartExtract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\CompressedFolder\shell\ZenArchieveSmartExtract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

; B. Wildcard with AppliesTo (Matches any .zip, .7z, .rar, .tar, .gz regardless of ProgID or default app)
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "AppliesTo"; ValueData: "System.FileExtension:=.zip OR System.FileExtension:=.7z OR System.FileExtension:=.rar OR System.FileExtension:=.tar OR System.FileExtension:=.gz"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractFiles"; ValueType: string; ValueData: "Extract Files..."; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractFiles"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractFiles"; ValueType: string; ValueName: "AppliesTo"; ValueData: "System.FileExtension:=.zip OR System.FileExtension:=.7z OR System.FileExtension:=.rar OR System.FileExtension:=.tar OR System.FileExtension:=.gz"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveExtractFiles\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-files ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveSmartExtract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "AppliesTo"; ValueData: "System.FileExtension:=.zip OR System.FileExtension:=.7z OR System.FileExtension:=.rar OR System.FileExtension:=.tar OR System.FileExtension:=.gz"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveSmartExtract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

; C. ZenArchieve.Archive ProgID
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\open"; ValueType: string; ValueData: "Open with ZenArchieve"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\extracthere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\extracthere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\extracthere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\extractfiles"; ValueType: string; ValueData: "Extract Files..."; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\extractfiles"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\extractfiles\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-files ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\smartextract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\smartextract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\ZenArchieve.Archive\shell\smartextract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

; D. SystemFileAssociations for Explorer right-click integration (.zip, .7z, .rar, .tar, .gz)
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveOpen"; ValueType: string; ValueData: "Open with ZenArchieve"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveOpen"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveOpen\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveExtractFiles"; ValueType: string; ValueData: "Extract Files..."; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveExtractFiles"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveExtractFiles\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-files ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveSmartExtract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\ZenArchieveSmartExtract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveOpen"; ValueType: string; ValueData: "Open with ZenArchieve"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveOpen"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveOpen\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractFiles"; ValueType: string; ValueData: "Extract Files..."; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractFiles"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractFiles\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-files ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveSmartExtract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\ZenArchieveSmartExtract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveOpen"; ValueType: string; ValueData: "Open with ZenArchieve"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveOpen"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveOpen\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveSmartExtract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\ZenArchieveSmartExtract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveOpen"; ValueType: string; ValueData: "Open with ZenArchieve"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveOpen"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveOpen\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveSmartExtract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\ZenArchieveSmartExtract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveOpen"; ValueType: string; ValueData: "Open with ZenArchieve"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveOpen"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveOpen\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveExtractHere"; ValueType: string; ValueData: "Extract Here"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveExtractHere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveExtractHere\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -extract-here ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveSmartExtract"; ValueType: string; ValueData: "Extract to \<ArchiveName>\"; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveSmartExtract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\ZenArchieveSmartExtract\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -smart-extract ""%1"""; Tasks: context_menu

; ---------------------------------------------------------------------
; 3. Shell Context Menu on Files and Directories (Add to ZenArchieve...)
; ---------------------------------------------------------------------
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveCompress"; ValueType: string; ValueData: "Add to ZenArchieve..."; Flags: uninsdeletekey; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveCompress"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\*\shell\ZenArchieveCompress\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -compress ""%1"""; Tasks: context_menu

Root: HKA; Subkey: "Software\Classes\Directory\shell\ZenArchieveCompress"; ValueType: string; ValueData: "Add to ZenArchieve..."; Flags: uninsdeletekey; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\Directory\shell\ZenArchieveCompress"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: context_menu
Root: HKA; Subkey: "Software\Classes\Directory\shell\ZenArchieveCompress\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" -compress ""%1"""; Tasks: context_menu

; ---------------------------------------------------------------------
; 4. Windows 11 Direct Context Menu (Enables classic menu directly on right-click without "Show more options")
; ---------------------------------------------------------------------
Root: HKCU; Subkey: "Software\Classes\CLSID\{{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"; ValueType: string; ValueData: ""; Flags: uninsdeletekey; Tasks: context_menu

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet9Installed(): Boolean;
var
  ErrorCode: Integer;
  DesktopFxDir: String;
  FindRec: TFindRec;
begin
  Result := False;
  DesktopFxDir := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');

  if DirExists(DesktopFxDir) then
  begin
    if FindFirst(DesktopFxDir + '\9.*', FindRec) then
    begin
      try
        Result := True;
      finally
        FindClose(FindRec);
      end;
    end;
  end;

  if not Result then
  begin
    // Check via dotnet --list-runtimes
    if Exec('cmd.exe', '/c dotnet --list-runtimes | findstr "Microsoft.WindowsDesktop.App 9."', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode) then
    begin
      if ErrorCode = 0 then
        Result := True;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not IsDotNet9Installed() then
  begin
    if MsgBox('ZenArchieve requires Microsoft .NET 9 Desktop Runtime (x64) to run.' + #13#10 + #13#10 +
              'Would you like to download and install .NET 9 now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/9.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;

// Custom Inno Setup Pascal Script procedures for clean uninstallation notification
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Explorer icon cache notification is broadcast automatically by Inno Setup ChangesAssociations=yes
  end;
end;
