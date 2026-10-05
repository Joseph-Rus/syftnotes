; Study-Stash-Setup.exe, the one Windows download (built by windows/build.ps1 with Inno Setup 6). The app has no
; role of its own: setup asks what this computer is for, and Settings can change it later (docs/one-download.md).
; The AppId is the one the old Laptop and Library Setup.exe had, so installing over any earlier copy is an in-place
; upgrade: same folder, and its study-stash.ini keeps the role= line an old Setup.exe wrote (Apps.RolePreset reads it
; for a copy that never finished setup), so nothing changes for a computer already set up.
; Per-user install (no admin rights), so [Files] copies the tree for whichever processor this Windows is
; (an arm64 Windows can also run the x64 tree under emulation, but its own tree is faster).
;   ISCC /Qp /DAppVersion=0.9.0 /DSource=<dist\windows, holding win-x64\ and win-arm64\> /Odist\windows setup.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Source
  #define Source "..\dist\windows"
#endif

[Setup]
AppId={{6F2C9A51-3B7E-4D28-9C41-8E5A0B7D2F63}
AppName=Study Stash
AppVersion={#AppVersion}
AppVerName=Study Stash {#AppVersion}
AppPublisher=Study Stash
AppPublisherURL=https://github.com/Joseph-Rus/study-stash
AppSupportURL=https://github.com/Joseph-Rus/study-stash/issues
AppCopyright=MIT License.
DefaultDirName={localappdata}\Programs\Study Stash
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputBaseFilename=Study-Stash-Setup
SetupIconFile=..\assets\study-stash.ico
UninstallDisplayIcon={app}\StudyStash.exe
UninstallDisplayName=Study Stash
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
#ifdef SignInstaller
; windows\sign-release.ps1 passes /DSignInstaller and the sign tool's command: Setup.exe, the uninstaller and any
; temporary copy of it that Setup makes are all signed (docs/signing.md).
SignTool=studystash
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Add a desktop icon"; Flags: unchecked

[Files]
Source: "{#Source}\win-arm64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsArm64
Source: "{#Source}\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: not IsArm64

[INI]
; the version an installed copy reports without running .NET (Updates.InstalledVersion); no role: setup asks
Filename: "{app}\study-stash.ini"; Section: "app"; Key: "version"; String: "{#AppVersion}"

[Icons]
Name: "{userprograms}\Study Stash"; Filename: "{app}\StudyStash.exe"; Comment: "Open Study Stash"
Name: "{userdesktop}\Study Stash"; Filename: "{app}\StudyStash.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\StudyStash.exe"; Description: "Open Study Stash"; Flags: nowait postinstall skipifsilent
; a silent update (/relaunch=1) reopens the app once the new files are in place
Filename: "{app}\StudyStash.exe"; Parameters: "--background"; Flags: nowait; Check: Relaunch

[UninstallRun]
; Restart Manager (CloseApplications) already closed a foreground copy; this catches one running in the
; tray with no window for it to find.
Filename: "{cmd}"; Parameters: "/c taskkill /f /im StudyStash.exe"; Flags: runhidden; RunOnceId: "KillStudyStash"

[UninstallDelete]
; only the app itself: lectures live in the home folder and Documents\Study Stash, untouched.
Type: filesandordirs; Name: "{app}"

[Code]
var
  UpdateFinished: Boolean;

function Relaunch: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

// Every process of this account running this install's StudyStash.exe (the app, and the library it runs), counted;
// with Stop, each is stopped too. WMI can't see another account's: those are left alone.
function AppCopies(Stop: Boolean): Integer;
var
  Wmi, Found, Proc: Variant;
  I, Pid, ResultCode: Integer;
  Exe: String;
begin
  Result := 0;
  Wmi := CreateOleObject('WbemScripting.SWbemLocator');
  Wmi := Wmi.ConnectServer('.', 'root\CIMV2');
  Found := Wmi.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''StudyStash.exe''');
  for I := 0 to Found.Count - 1 do
  begin
    Proc := Found.ItemIndex(I);
    if not VarIsNull(Proc.ExecutablePath) then
    begin
      Exe := Proc.ExecutablePath;
      if CompareText(Exe, ExpandConstant('{app}\StudyStash.exe')) = 0 then
      begin
        Result := Result + 1;
        if Stop then
        begin
          Pid := Proc.ProcessId;
          Log('Stopping Study Stash (' + IntToStr(Pid) + ')');
          Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /pid ' + IntToStr(Pid), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        end;
      end;
    end;
  end;
end;

// A copy before 0.10.1 that updated itself also left a hidden PowerShell waiting for it to quit, to open it again.
// That copy would start in the middle of this install and hold the old files open, and the install would stop half
// done. Those waiters go first: this Setup opens the app again itself when it's finished.
procedure StopOldRelaunchers;
var
  Wmi, Found, Proc: Variant;
  I, Pid, ResultCode: Integer;
  Cmd: String;
begin
  Wmi := CreateOleObject('WbemScripting.SWbemLocator');
  Wmi := Wmi.ConnectServer('.', 'root\CIMV2');
  Found := Wmi.ExecQuery('SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = ''powershell.exe''');
  for I := 0 to Found.Count - 1 do
  begin
    Proc := Found.ItemIndex(I);
    if not VarIsNull(Proc.CommandLine) then
    begin
      Cmd := Proc.CommandLine;
      if (Pos('Wait-Process', Cmd) > 0) and (Pos('StudyStash.exe', Cmd) > 0) then
      begin
        Pid := Proc.ProcessId;
        Log('Stopping an older Study Stash''s relaunch waiter (' + IntToStr(Pid) + ')');
        Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /pid ' + IntToStr(Pid), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;
    end;
  end;
end;

// A silent update (/relaunch=1): the copy that started it is quitting by itself. It gets a moment to, then whatever
// still runs from this folder is stopped (again, should something have opened one meanwhile), so no file is in use
// when the new ones go in. Anything that goes wrong here is only logged: Restart Manager (CloseApplications) still
// closes what it finds.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Tries: Integer;
begin
  Result := '';
  if not Relaunch then Exit;
  try
    StopOldRelaunchers;
    Tries := 0;
    while (Tries < 20) and (AppCopies(False) > 0) do
    begin
      Sleep(500);
      Tries := Tries + 1;
    end;
    Tries := 0;
    while (Tries < 5) and (AppCopies(True) > 0) do
    begin
      Sleep(500);
      Tries := Tries + 1;
    end;
  except
    Log('Couldn''t check for a running Study Stash: ' + GetExceptionMessage);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then UpdateFinished := True;
end;

// A silent update that stopped short opens the copy that's here again, so the student isn't left without Study
// Stash (it says the update didn't take); one that finished is opened by [Run].
procedure DeinitializeSetup();
var
  ResultCode: Integer;
  Exe: String;
begin
  if (not Relaunch) or UpdateFinished then Exit;
  try
    Exe := ExpandConstant('{app}\StudyStash.exe');
    if FileExists(Exe) then
    begin
      Log('The update didn''t finish: opening Study Stash again');
      Exec(Exe, '--background', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  except
    Log('Couldn''t open Study Stash again: ' + GetExceptionMessage);
  end;
end;

// The app's own start-at-login entry (Desktop.StartAtLogin): removed only if it still points at this
// install, so a second profile's entry (a different value name) or someone else's app is left alone.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunValue: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Study Stash', RunValue) then
      if Pos(ExpandConstant('{app}'), RunValue) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Study Stash');
end;
