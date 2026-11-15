// Included only in the framework-dependent installer. The full package never downloads a runtime.
var
  RuntimeDownload: TDownloadWizardPage;
  RuntimeNeedsRestart: Boolean;

function HasFramework(const Root, Framework: String): Boolean;
var
  Entry: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(Root) + 'shared\' + Framework + '\10.*', Entry) then
  begin
    try
      repeat
        // Reject prerelease directories and incomplete framework installations.
        if ((Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (Pos('-', Entry.Name) = 0) and
           FileExists(AddBackslash(Root) + 'shared\' + Framework + '\' + Entry.Name + '\' + Framework + '.deps.json') then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function HasDesktopRuntime: Boolean;
var
  RuntimeRoot: String;
begin
  // The x64 apphost uses the registered installation or its default Program Files location.
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', RuntimeRoot) then
    RuntimeRoot := ExpandConstant('{commonpf64}\dotnet');
  Result := FileExists(AddBackslash(RuntimeRoot) + 'dotnet.exe') and
    HasFramework(RuntimeRoot, 'Microsoft.WindowsDesktop.App') and
    HasFramework(RuntimeRoot, 'Microsoft.NETCore.App');
end;

procedure InitializeWizard;
begin
  RuntimeDownload := CreateDownloadPage('Gerekli bileşen hazırlanıyor',
    'Microsoft .NET Desktop Runtime eksikse otomatik indirilir ve kurulur.', nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  RuntimeFile: String;
begin
  Result := '';
  if HasDesktopRuntime then
  begin
    Log('Compatible .NET 10 x64 Desktop Runtime is already installed; skipping download.');
    Exit;
  end;
  try
    RuntimeDownload.Clear;
    RuntimeDownload.Add('{#RuntimeUrl}', 'windowsdesktop-runtime.exe', '{#RuntimeSha}');
    RuntimeDownload.Show;
    try
      RuntimeDownload.Download;
    finally
      RuntimeDownload.Hide;
    end;
    RuntimeFile := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');
    WizardForm.PreparingLabel.Caption := 'Microsoft .NET Desktop Runtime kuruluyor. Windows izin penceresini onayla; kurulum otomatik devam edecek.';
    if not ShellExec('runas', RuntimeFile, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      Result := '.NET kurulumu başlatılamadı veya Windows izni iptal edildi. Tekrar dene ya da tam SilNet paketini kullan. Ayrıntı: ' + SysErrorMessage(ExitCode);
      Exit;
    end;
    Log(Format('Microsoft Desktop Runtime installer exit code: %d', [ExitCode]));
    if (ExitCode <> 0) and (ExitCode <> 3010) then
    begin
      Result := Format('.NET kurulumu tamamlanamadı (kod %d). Tekrar dene ya da tam SilNet paketini kullan.', [ExitCode]);
      Exit;
    end;
    if not HasDesktopRuntime then
    begin
      Result := '.NET Desktop Runtime doğrulanamadı. SilNet dosyaları değiştirilmedi. Tam paketi kullanabilir veya kurulumu tekrar deneyebilirsin.';
      Exit;
    end;
    RuntimeNeedsRestart := ExitCode = 3010;
  except
    Log(GetExceptionMessage);
    if RuntimeDownload.AbortedByUser then
      Result := 'Gerekli bileşenin indirilmesi iptal edildi. SilNet kurulmadı.'
    else
      Result := '.NET indirilemedi veya dosya doğrulanamadı. İnternet bağlantını kontrol edip tekrar dene; internetsiz kurulum için tam paketi kullan. ' + GetExceptionMessage;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeNeedsRestart;
end;
