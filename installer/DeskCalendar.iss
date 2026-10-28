#define AppVersion "1.0.81"
#ifndef PayloadDir
  #define PayloadDir "..\dist\app"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "..\dist"
#endif
[Setup]
AppId={code:GetApplicationId}
AppName=Google Calendar Desktop
AppVersion={#AppVersion}
AppPublisher=Acerocloud
AppPublisherURL=https://github.com/Acerocloud/Google-Calendar-Desktop
AppSupportURL=https://github.com/Acerocloud/Google-Calendar-Desktop/issues
AppUpdatesURL=https://github.com/Acerocloud/Google-Calendar-Desktop/releases
DefaultDirName={localappdata}\Programs\DeskCalendar
DefaultGroupName=Google Calendar Desktop
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
UninstallDisplayIcon={app}\DeskCalendar.exe
WizardStyle=modern
UsePreviousLanguage=no
Compression=lzma2
SolidCompression=yes
OutputDir={#InstallerOutput}
OutputBaseFilename=DeskCalendar-Setup-{#AppVersion}-x64
SetupIconFile=..\source\app.ico
LicenseFile=..\LICENSE
InfoBeforeFile=install-info.txt
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked
[Files]
Source: "{#PayloadDir}\DeskCalendar.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\DeskCalendar.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Google登录步骤.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Google登录步骤.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\验证记录.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\待办使用说明.md"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\Google Calendar Desktop"; Filename: "{app}\DeskCalendar.exe"
Name: "{autodesktop}\Google Calendar Desktop"; Filename: "{app}\DeskCalendar.exe"; Tasks: desktopicon
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskCalendar"; ValueData: """{app}\DeskCalendar.exe"""; Flags: uninsdeletevalue; Check: ShouldMigrateStartup
[Run]
Filename: "{app}\DeskCalendar.exe"; Description: "打开 Google Calendar Desktop（显示右下角托盘图标）"; Flags: nowait postinstall skipifsilent unchecked
[Code]
function IsTestInstall: Boolean;
begin
  Result := ExpandConstant('{param:QA|0}') = '1';
end;
function GetApplicationId(Param: String): String;
begin
  if IsTestInstall then Result := '{B17D6DF4-9EB7-4D91-9943-0322C939D18B}'
  else Result := '{C95D2893-0C50-4D15-8D45-7306843A0427}';
end;
function ShouldMigrateStartup: Boolean;
begin
  Result := (not IsTestInstall) and RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DeskCalendar');
end;
function InitializeSetup: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then MsgBox('需要 Microsoft .NET Framework 4.8 或更高版本。请安装后重试。', mbError, MB_OK);
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DeskCalendar', StartupCommand) then
      if (CompareText(StartupCommand, '"' + ExpandConstant('{app}\DeskCalendar.exe') + '"') = 0) or
         (CompareText(StartupCommand, ExpandConstant('{app}\DeskCalendar.exe')) = 0) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DeskCalendar');
end;