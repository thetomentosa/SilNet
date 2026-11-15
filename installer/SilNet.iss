#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\portable"
#endif
#ifndef ReleaseDir
  #define ReleaseDir SourcePath + "..\artifacts"
#endif
#ifndef DriverSha
  #error DriverSha must be supplied by scripts/build.ps1
#endif
[Setup]
AppId={{E581D4D0-6AD9-4ED3-8A84-7570DE5359DE}
AppName=SilNet
AppVersion={#AppVersion}
AppPublisher=SilNet contributors
AppPublisherURL=https://github.com/thetomentosa/SilNet
AppSupportURL=https://github.com/thetomentosa/SilNet/issues
AppUpdatesURL=https://github.com/thetomentosa/SilNet/releases/latest
DefaultDirName={localappdata}\Programs\SilNet
DefaultGroupName=SilNet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
UninstallDisplayIcon={app}\SilNet.exe
SetupIconFile=..\Assets\SilNet.ico
LicenseFile=..\LICENSE
OutputDir={#ReleaseDir}
OutputBaseFilename=SilNet-Setup-{#AppVersion}-win-x64
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=SilNet.UI
SetupLogging=yes
[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; Flags: unchecked
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "engines\zapret2\WinDivert64.sys"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\engines\zapret2\WinDivert64.sys"; DestDir: "{app}\engines\zapret2"; Flags: ignoreversion; Check: DriverNeedsCopy
[Icons]
Name: "{userprograms}\SilNet"; Filename: "{app}\SilNet.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SilNet"; Filename: "{app}\SilNet.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\SilNet.exe"; Description: "SilNet’i aç"; Flags: nowait postinstall skipifsilent
[Code]
function DriverNeedsCopy: Boolean;
var DriverPath: String;
begin
  DriverPath := ExpandConstant('{app}\engines\zapret2\WinDivert64.sys');
  Result := True;
  if FileExists(DriverPath) then
  begin
    try
      Result := CompareText(GetSHA256OfFile(DriverPath), '{#DriverSha}') <> 0;
    except
      Result := True;
    end;
  end;
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var DataPath: String;
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    if MsgBox('SilNet kişisel ayarları, bağlantı hafızası ve günlükleri de silinsin mi? Diğer programların dosyaları ve ortak sürücüler silinmez.', mbConfirmation, MB_YESNO) = IDYES then
    begin
      DataPath := ExpandConstant('{localappdata}\SilNet');
      DelTree(DataPath, True, True, True);
    end;
  end;
end;
