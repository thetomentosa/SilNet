param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts'),
    [string]$Dotnet = 'dotnet',
    [string]$Iscc = '',
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseRoot = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $releaseRoot | Out-Null
$publishRoot = Join-Path $releaseRoot 'portable'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$version = ([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'SilNet.csproj'))).Project.PropertyGroup.Version
Push-Location $repoRoot
try {
    python prepare_engine.py
    if ($LASTEXITCODE -ne 0) { throw 'Motor dosyaları doğrulanamadı.' }
    & $Dotnet publish SilNet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
    Copy-Item -LiteralPath LICENSE,README.md -Destination $publishRoot
    $exe = Join-Path $publishRoot 'SilNet.exe'
    $report = Join-Path $releaseRoot 'test-results.json'
    $test = Start-Process -FilePath $exe -ArgumentList @('--self-test', ('"'+$report+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw ('Kontroller başarısız: ' + $report) }
    $zipPath = Join-Path $releaseRoot ("SilNet-"+$version+'-win-x64-portable.zip')
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -Force
    if (!$SkipInstaller) {
        if (!$Iscc) {
            $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($command) { $Iscc = $command.Source }
            elseif (Test-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe') { $Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
            elseif (Test-Path 'C:\Program Files\Inno Setup 7\ISCC.exe') { $Iscc = 'C:\Program Files\Inno Setup 7\ISCC.exe' }
            elseif (Test-Path 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe') { $Iscc = 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe' }
            else { throw 'Inno Setup derleyicisi bulunamadı. -Iscc ile yolunu belirtin.' }
        }
        $driverSha = (Get-FileHash -LiteralPath (Join-Path $publishRoot 'engines\zapret2\WinDivert64.sys') -Algorithm SHA256).Hash
        & $Iscc ('/DAppVersion='+$version) ('/DPublishDir='+$publishRoot) ('/DReleaseDir='+$releaseRoot) ('/DDriverSha='+$driverSha) (Join-Path $repoRoot 'installer\SilNet.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Kurulum paketi oluşturulamadı.' }
    }
    $checksums = Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object { $_.Name -match '^SilNet-.*\.(zip|exe)$' } | ForEach-Object {
        ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name)
    }
    [IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $checksums)
} finally { Pop-Location }
