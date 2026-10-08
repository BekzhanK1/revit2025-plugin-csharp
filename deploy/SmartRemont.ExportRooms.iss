#define MyAppName "Smart Remont — Revit 2025"
#define MyAppVersion "dev1.0.1"
#define MyAppPublisher "Smart Remont"
#define AddinsDir "{commonappdata}\Autodesk\Revit\Addins\2025"
#define PluginDir "{commonappdata}\Autodesk\Revit\Addins\2025\SmartRemont"

[Setup]
AppId={{B9E4D1C2-3A5F-4E7B-9D0E-1F2A3B4C5D6E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={#PluginDir}
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=out
OutputBaseFilename=SmartRemont-Revit-2025-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=payload\smartremont.ico
WizardSmallImageFile=payload\SmartRemont\Resources\export_32.png
UninstallDisplayIcon={uninstallexe}
UninstallDisplayName={#MyAppName}
CloseApplications=yes
CloseApplicationsFilter=Revit.exe
RestartApplications=no
SetupLogging=yes
MinVersion=10.0
UsedUserAreasWarning=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
russian.WelcomeLabel2=Установка плагина Smart Remont для Autodesk Revit 2025.%n%nЗакройте Revit перед установкой.

[Files]
Source: "payload\SmartRemont\*"; DestDir: "{#PluginDir}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\SmartRemont.ExportRooms.addin"; DestDir: "{#AddinsDir}"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{#PluginDir}\logs"

[Code]
function IsRevitRunning(): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\CIMV2', '', '');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name=''Revit.exe''');
    Result := not VarIsNull(Processes) and (Processes.Count > 0);
  except
    Result := False;
  end;
end;

function InitializeSetup(): Boolean;
var
  ExistingDll: String;
  FileVersion: String;
  Msg: String;
begin
  Result := True;

  if IsRevitRunning() then
  begin
    MsgBox('Закройте Autodesk Revit и запустите установщик снова.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Плагин уже стоит (этот же установщик раньше или ручной деплой в ту же папку) —
  // предупреждаем и переписываем только с согласия. [Files] ниже и так всегда
  // перезатирает (ignoreversion), поэтому без этой проверки замена шла бы molча.
  ExistingDll := ExpandConstant('{#PluginDir}\SmartRemont.ExportRooms.dll');
  if FileExists(ExistingDll) then
  begin
    Msg := 'Плагин Smart Remont уже установлен:' + #13#10 + ExpandConstant('{#PluginDir}') + #13#10;
    if GetVersionNumbersString(ExistingDll, FileVersion) then
      Msg := Msg + #13#10 + 'Версия установленного файла: ' + FileVersion + '.';
    Msg := Msg + #13#10 + #13#10 + 'Заменить текущую версию на {#MyAppVersion}?';

    if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
end;
