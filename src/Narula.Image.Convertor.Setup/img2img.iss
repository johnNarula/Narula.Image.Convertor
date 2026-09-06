; Installer for Image to Image Convertor.
;
; Built by the project beside this file rather than by hand, so the version and the staged
; binaries always come from the same build:
;
;     dotnet build src/Narula.Image.Convertor.Setup -t:Installer
;
; AppVersion and StageDir are passed in by that target.

#ifndef AppVersion
  #define AppVersion "0.0.0.0"
#endif
#ifndef StageDir
  #define StageDir "obj\stage"
#endif

; Must stay identical to AppId below, which cannot be written as a define because Inno expands
; constants in that field and so needs the doubled brace.
#define AppGuid "{A6D1F2C4-8E3B-4A5D-9C71-2F0B6E4D8A31}"

#define AppName "Image to Image Convertor"
#define AppShortName "img2img"
#define AppPublisher "9th Act, LLC"
#define AppUrl "https://www.linkedin.com/in/johnNarula"
#define WindowExe "img2imgUI.exe"
#define McpExe "img2imgMcp.exe"
#define ConsoleExe "img2img.exe"

[Setup]
; Never change AppId: it is how Windows recognises an existing install and upgrades it.
AppId={{A6D1F2C4-8E3B-4A5D-9C71-2F0B6E4D8A31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}

; Per-user by default, which needs no administrator and no prompt. The dialog offers an
; all-users install to anyone who wants one.
PrivilegesRequired=lowest
; dialog: ask the person. commandline: let /CURRENTUSER and /ALLUSERS decide, which a scripted
; or silent install needs, since it never sees the dialog.
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\{#AppShortName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#WindowExe}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; The application's own icon throughout: on setup.exe itself, on the panel of the last page,
; and in the corner of every other one. Both bitmaps are drawn from icon.ico by
; make-wizard-images.ps1 beside this file, so they cannot disagree with the shipped icon.
SetupIconFile=..\Narula.Image.Convertor\icon.ico
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputBaseFilename={#AppShortName}-setup-{#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "addtopath"; Description: "Add img2img to my PATH, so it runs from any terminal"
Name: "explorermenu"; Description: "Add ""Convert images here..."" to the folder right-click menu"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#StageDir}\{#ConsoleExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\{#WindowExe}"; DestDir: "{app}"; Flags: ignoreversion
; The MCP server, so an AI agent can drive the same engine. Nothing launches it but an agent;
; About in the window shows the one line needed to register it.
Source: "{#StageDir}\{#McpExe}"; DestDir: "{app}"; Flags: ignoreversion
; A starting point only. Your own copy in %AppData%\9thAct\img2img wins over this one, and
; onlyifdoesntexist means reinstalling never overwrites an edited file.
Source: "settings.default.json"; DestDir: "{app}"; DestName: "settings.json"; Flags: onlyifdoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#WindowExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#WindowExe}"; Tasks: desktopicon

[Registry]
; Right-clicking a folder, and right-clicking inside one. HKA lands in HKCU for a per-user
; install and HKLM for an all-users install, matching whichever was chosen.
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#AppShortName}"; ValueType: string; ValueData: "Convert images here..."; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#AppShortName}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#WindowExe}"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#AppShortName}\command"; ValueType: string; ValueData: """{app}\{#WindowExe}"" -s ""%V"""; Tasks: explorermenu

Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\{#AppShortName}"; ValueType: string; ValueData: "Convert images here..."; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\{#AppShortName}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#WindowExe}"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\{#AppShortName}\command"; ValueType: string; ValueData: """{app}\{#WindowExe}"" -s ""%V"""; Tasks: explorermenu

[Run]
; The runtime installer only appears here when the check below found nothing suitable.
Filename: "{tmp}\dotnet-runtime.exe"; Parameters: "/install /quiet /norestart"; \
  StatusMsg: "Installing the .NET runtime..."; Flags: waituntilterminated; Check: NeedsDotNet

Filename: "{app}\{#WindowExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RuntimeUrl = 'https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe';
  EnvironmentKey = 'Environment';

var
  DotNetChecked: Boolean;
  DotNetMissing: Boolean;

{ The executables are framework-dependent, so .NET 10 has to be present. Look for the shared
  framework directly rather than asking the dotnet CLI, which need not be on PATH. }
function DotNetIsInstalled: Boolean;
var
  FindRec: TFindRec;
  Root: String;
begin
  Result := False;
  Root := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.NETCore.App');

  if FindFirst(Root + '\10.*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function NeedsDotNet: Boolean;
begin
  if not DotNetChecked then
  begin
    DotNetMissing := not DotNetIsInstalled;
    DotNetChecked := True;
  end;

  Result := DotNetMissing;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

{ Per-user and all-users installs keep separate uninstall records, so without this check both
  can sit on one machine at once: two copies, two PATH entries, two right-click menus, and no
  way to tell which one a shortcut points at. Returns the uninstaller of a copy installed in
  the mode this run is NOT using. }
function OtherModeUninstaller(var Where: String): String;
var
  Key: String;
  Root: Integer;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1';

  if IsAdminInstallMode then
  begin
    Root := HKEY_CURRENT_USER;
    Where := 'just for you';
  end
  else
  begin
    Root := HKEY_LOCAL_MACHINE;
    Where := 'for all users';
  end;

  if not RegQueryStringValue(Root, Key, 'UninstallString', Result) then
    Result := '';
end;

{ Both of these run here rather than from the wizard, because PrepareToInstall runs for a
  silent install too. On the Ready page instead, /VERYSILENT would quietly install a program
  that cannot start, beside a copy of itself. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller, Where: String;
  ResultCode: Integer;
begin
  Result := '';

  Uninstaller := OtherModeUninstaller(Where);

  if Uninstaller <> '' then
  begin
    { A silent install takes the default and removes it: finishing with two copies is never
      what was wanted, and there is nobody there to be asked. }
    if SuppressibleMsgBox(
         '{#AppName} is already installed ' + Where + '.' + #13#10#13#10 +
         'Two copies on one machine means two entries in Add/Remove Programs and two ' +
         'right-click menus. Remove the other one first?',
         mbConfirmation, MB_YESNO, IDYES) = IDYES then
    begin
      Exec(RemoveQuotes(Uninstaller), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART',
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;

  if not NeedsDotNet then
    Exit;

  try
    DownloadTemporaryFile(RuntimeUrl, 'dotnet-runtime.exe', '', @OnDownloadProgress);
  except
    Result :=
      'The .NET runtime could not be downloaded.' + #13#10#13#10 +
      GetExceptionMessage + #13#10#13#10 +
      'Install .NET 10 from https://dotnet.microsoft.com/download and run this setup again.';
  end;
end;

{ PATH is always the user's own, even for an all-users install: it needs no elevation and it is
  the one that a terminal actually inherits for the person who installed this. }
function PathNeedsAdding(Folder: String): Boolean;
var
  Existing: String;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Existing) then
    Existing := '';

  Result := Pos(';' + Uppercase(Folder) + ';', ';' + Uppercase(Existing) + ';') = 0;
end;

procedure AddToPath(Folder: String);
var
  Existing: String;
begin
  if not PathNeedsAdding(Folder) then
    Exit;

  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Existing) then
    Existing := '';

  if (Existing <> '') and (Copy(Existing, Length(Existing), 1) <> ';') then
    Existing := Existing + ';';

  RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Existing + Folder);
end;

procedure RemoveFromPath(Folder: String);
var
  Existing: String;
  Position: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Existing) then
    Exit;

  Position := Pos(';' + Uppercase(Folder) + ';', ';' + Uppercase(Existing) + ';');
  if Position = 0 then
    Exit;

  Delete(Existing, Position, Length(Folder) + 1);
  RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Existing);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;
