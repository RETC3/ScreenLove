; Installer di ScreenLove (Inno Setup 6).
; Di solito si crea con:  scripts\publish -Installer
; oppure a mano, dopo la pubblicazione:  ISCC.exe /DAppVersion=0.1.0 installer\ScreenLove.iss
;
; Installazione per l'utente corrente (non chiede i permessi di amministratore):
; %LOCALAPPDATA%\Programs\ScreenLove, collegamento nel menu Start, disinstallazione da "App installate".

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\dist\win-x64\ScreenLove.exe")
#endif
#define AppExe "ScreenLove.exe"

[Setup]
AppId={{1162A7F1-B15B-41DF-8754-16C9B61DCDF8}
AppName=ScreenLove
AppVersion={#AppVersion}
AppVerName=ScreenLove {#AppVersion}
AppPublisher=ScreenLove
AppPublisherURL=https://github.com/RETC3/ScreenLove
AppSupportURL=https://github.com/RETC3/ScreenLove/issues
AppUpdatesURL=https://github.com/RETC3/ScreenLove/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\ScreenLove
DefaultGroupName=ScreenLove
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SourceDir=..
OutputDir=dist
OutputBaseFilename=ScreenLove-Setup-{#AppVersion}
SetupIconFile=src\ScreenLove\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=ScreenLove
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; se ScreenLove è aperto durante un'installazione manuale, l'installer propone di chiuderlo
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
italian.AutoStart=Avvia ScreenLove con Windows (icona nell'area di notifica)
english.AutoStart=Start ScreenLove with Windows (notification area icon)
italian.StillRunning=ScreenLove è in esecuzione.%n%nChiudilo (icona nell'area di notifica → Esci) e riprova.
english.StillRunning=ScreenLove is running.%n%nClose it (notification area icon → Exit) and try again.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "dist\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ScreenLove"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\ScreenLove"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; stesso valore che scrive l'app con "Avvia con Windows": l'opzione resta coerente nei due posti
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenLove"; \
    ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart

[Run]
; installazione normale: casella "Avvia ScreenLove" nell'ultima pagina
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,ScreenLove}"; Flags: nowait postinstall skipifsilent
; aggiornamento dall'app (installazione silenziosa): riapre ScreenLove alla fine
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsUpdate

[Code]
const
  AppMutexName = 'ScreenLove.SingleInstance';

function IsUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function InitializeSetup(): Boolean;
var
  i: Integer;
begin
  // L'aggiornamento automatico avvia l'installer e poi chiude ScreenLove:
  // si aspetta (al massimo 15 s) che l'app sia uscita prima di copiare i file.
  if IsUpdate() then
  begin
    i := 0;
    while CheckForMutexes(AppMutexName) and (i < 60) do
    begin
      Sleep(250);
      i := i + 1;
    end;
  end;
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  i: Integer;
begin
  Result := True;
  if UninstallSilent() then
  begin
    // senza finestre non si può chiedere di chiudere l'app: si aspetta un po' e poi si rinuncia
    i := 0;
    while CheckForMutexes(AppMutexName) and (i < 60) do
    begin
      Sleep(250);
      i := i + 1;
    end;
    Result := not CheckForMutexes(AppMutexName);
    Exit;
  end;
  while CheckForMutexes(AppMutexName) do
  begin
    if MsgBox(CustomMessage('StillRunning'), mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // "Avvia con Windows" può essere stato attivato anche dall'app: lo si toglie comunque.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ScreenLove');
end;
