; Syft-Notes-Setup.exe, the one Windows download (built by windows/build.ps1 with Inno Setup 6). The app has no
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
AppName=Syft Notes
AppVersion={#AppVersion}
AppVerName=Syft Notes {#AppVersion}
AppPublisher=Syft Notes
AppPublisherURL=https://github.com/Joseph-Rus/study-stash
AppSupportURL=https://github.com/Joseph-Rus/study-stash/issues
AppCopyright=MIT License.
DefaultDirName={localappdata}\Programs\Study Stash
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
; One double-click, no questions (the way Slack, Discord and Zoom install): no welcome page, no finished page with a
; checkbox. Setup shows its progress bar, then [Run] below opens the app.
DisableWelcomePage=yes
DisableFinishedPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputBaseFilename=Syft-Notes-Setup
SetupIconFile=..\assets\study-stash.ico
UninstallDisplayIcon={app}\StudyStash.exe
UninstallDisplayName=Syft Notes
; Explorer, the Start menu and the taskbar drop the icons they kept for the old name (Setup tells Windows at the end)
ChangesAssociations=yes
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

[Files]
Source: "{#Source}\win-arm64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsArm64
Source: "{#Source}\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: not IsArm64

[INI]
; the version an installed copy reports without running .NET (Updates.InstalledVersion); no role: setup asks
Filename: "{app}\study-stash.ini"; Section: "app"; Key: "version"; String: "{#AppVersion}"

[InstallDelete]
; The shortcuts an install from before the rename to Syft Notes made (named Study Stash, then Egghead Notes), so an
; update leaves one of each, not two.
Type: files; Name: "{userprograms}\Study Stash.lnk"
Type: files; Name: "{userdesktop}\Study Stash.lnk"
Type: files; Name: "{userprograms}\Egghead Notes.lnk"
Type: files; Name: "{userdesktop}\Egghead Notes.lnk"

[Icons]
; Start menu only, no desktop icon and no question about one (Windows 11's own habit, and what Slack and Notion do);
; a desktop icon an earlier install made stays where it is.
Name: "{userprograms}\Syft Notes"; Filename: "{app}\StudyStash.exe"; Comment: "Open Syft Notes"

[Registry]
; What the app tells Windows about itself the first time it has something to say, so its notifications are Windows'
; own (its name and icon for them, and the link a click on one opens). Nothing is written here; they go with the app.
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\StudyStash.App"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\studystash"; Flags: dontcreatekey uninsdeletekey

[Run]
; opens by itself when a person ran Setup (no postinstall flag: no checkbox, no finished page); a silent run (the
; updater's, CI's) never opens it here, the entry below does that when asked
Filename: "{app}\StudyStash.exe"; Description: "Open Syft Notes"; Flags: nowait skipifsilent
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
          Log('Stopping Syft Notes (' + IntToStr(Pid) + ')');
          Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /pid ' + IntToStr(Pid), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        end;
      end;
    end;
  end;
end;

// Waits, for up to TimeoutMs in all, for every process AppCopies counts to end, on Windows' own signal for a process
// ending (WaitForSingleObject on the process), not on a guessed pause between looks. True when none is left.
function OpenProcess(Access: Cardinal; Inherit: Boolean; Pid: Cardinal): THandle; external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal; external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean; external 'CloseHandle@kernel32.dll stdcall';
function GetTickCount: Cardinal; external 'GetTickCount@kernel32.dll stdcall';

function WaitForAppCopies(TimeoutMs: Cardinal): Boolean;
var
  Wmi, Found, Proc: Variant;
  I: Integer;
  Handle: THandle;
  Started, Spent: Cardinal;
begin
  Result := True;
  Wmi := CreateOleObject('WbemScripting.SWbemLocator');
  Wmi := Wmi.ConnectServer('.', 'root\CIMV2');
  Found := Wmi.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''StudyStash.exe''');
  Started := GetTickCount;
  for I := 0 to Found.Count - 1 do
  begin
    Proc := Found.ItemIndex(I);
    if not VarIsNull(Proc.ExecutablePath) then
      if CompareText(Proc.ExecutablePath, ExpandConstant('{app}\StudyStash.exe')) = 0 then
      begin
        Handle := OpenProcess($00100000, False, Proc.ProcessId); // SYNCHRONIZE
        if Handle <> 0 then
        begin
          Spent := GetTickCount - Started;
          if Spent > TimeoutMs then Spent := TimeoutMs;
          if WaitForSingleObject(Handle, TimeoutMs - Spent) <> 0 then Result := False; // 0 = WAIT_OBJECT_0: it ended
          CloseHandle(Handle);
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
        Log('Stopping an older Syft Notes'' relaunch waiter (' + IntToStr(Pid) + ')');
        Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /pid ' + IntToStr(Pid), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;
    end;
  end;
end;

// The background library ("Answer your phone when Syft Notes is closed": StudyStash.exe ... helper, started from the
// Run key, and the library it keeps going) holds this install's files and never quits by itself. Each copy of it is
// stopped before any file is replaced, whoever ran Setup; the app starts it again on the new version when it opens.
// WMI can't see another account's: those are left alone. True when none is left within TimeoutMs.
function StopBackground(TimeoutMs: Cardinal): Boolean;
var
  Wmi, Found, Proc: Variant;
  I, Pid, ResultCode: Integer;
  Handle: THandle;
  Started, Spent: Cardinal;
  Pids: array of Integer;
  Cmd: String;
begin
  Result := True;
  Wmi := CreateOleObject('WbemScripting.SWbemLocator');
  Wmi := Wmi.ConnectServer('.', 'root\CIMV2');
  Found := Wmi.ExecQuery('SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = ''StudyStash.exe''');
  SetArrayLength(Pids, 0);
  for I := 0 to Found.Count - 1 do
  begin
    Proc := Found.ItemIndex(I);
    if (not VarIsNull(Proc.ExecutablePath)) and (not VarIsNull(Proc.CommandLine)) then
    begin
      // Pos takes a String, not the Variant WMI hands back
      Cmd := Proc.CommandLine;
      if (CompareText(Proc.ExecutablePath, ExpandConstant('{app}\StudyStash.exe')) = 0) and (Pos(' helper', Cmd) > 0) then
      begin
        Pid := Proc.ProcessId;
        Log('Stopping Syft Notes in the background (' + IntToStr(Pid) + ')');
        Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /pid ' + IntToStr(Pid), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        SetArrayLength(Pids, GetArrayLength(Pids) + 1);
        Pids[GetArrayLength(Pids) - 1] := Pid;
      end;
    end;
  end;
  Started := GetTickCount;
  for I := 0 to GetArrayLength(Pids) - 1 do
  begin
    Handle := OpenProcess($00100000, False, Pids[I]); // SYNCHRONIZE
    if Handle <> 0 then
    begin
      Spent := GetTickCount - Started;
      if Spent > TimeoutMs then Spent := TimeoutMs;
      if WaitForSingleObject(Handle, TimeoutMs - Spent) <> 0 then Result := False;
      CloseHandle(Handle);
    end;
  end;
end;

// A silent update (/relaunch=1): the copy that started it is quitting by itself. It gets a moment to, then whatever
// still runs from this folder is stopped (again, should something have opened one meanwhile), so no file is in use
// when the new ones go in. Anything that goes wrong here is only logged: Restart Manager (CloseApplications) still
// closes what it finds.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  try
    if not StopBackground(10000) then Log('Syft Notes in the background was still running 10 s after it was stopped');
  except
    Log('Couldn''t stop Syft Notes in the background: ' + GetExceptionMessage);
  end;
  if not Relaunch then Exit;
  try
    StopOldRelaunchers;
    // The copy that started this is quitting by itself: given up to 10 s, then stopped (and given up to 5 s more).
    if not WaitForAppCopies(10000) then
    begin
      AppCopies(True);
      if not WaitForAppCopies(5000) then Log('Syft Notes was still running 5 s after it was stopped');
    end;
  except
    Log('Couldn''t check for a running Syft Notes: ' + GetExceptionMessage);
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
      Log('The update didn''t finish: opening Syft Notes again');
      Exec(Exe, '--background', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  except
    Log('Couldn''t open Syft Notes again: ' + GetExceptionMessage);
  end;
end;

// The app's own start-at-login entry (Desktop.StartAtLogin), and the background library's ("Syft Notes background",
// one per settings folder: BackgroundLibrary.cs): removed only if they still point at this install, so a second
// profile's entry or someone else's app is left alone. [UninstallRun] has already stopped whatever ran from here.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunValue: String;
  Names: TArrayOfString;
  I: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Study Stash', RunValue) then
      if Pos(ExpandConstant('{app}'), RunValue) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Study Stash');
    if RegGetValueNames(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', Names) then
      for I := 0 to GetArrayLength(Names) - 1 do
        if Pos('Syft Notes background', Names[I]) = 1 then
          if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', Names[I], RunValue) then
            if Pos(ExpandConstant('{app}'), RunValue) > 0 then
              RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', Names[I]);
  end;
end;
