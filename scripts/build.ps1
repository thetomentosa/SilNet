param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts'),
    [string]$Dotnet = 'dotnet',
    [string]$Iscc = '',
    [switch]$SkipInstaller,
    [switch]$BuildBootstrapTest
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
    $smallRoot = Join-Path $releaseRoot 'small'
    foreach ($package in @(@{Path=$publishRoot; SelfContained='true'; Report='test-results.json'}, @{Path=$smallRoot; SelfContained='false'; Report='test-results-small.json'})) {
        & $Dotnet publish SilNet.csproj -c Release -r win-x64 --self-contained $package.SelfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $package.Path
        if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
        Copy-Item -LiteralPath LICENSE,README.md -Destination $package.Path
        $exe = Join-Path $package.Path 'SilNet.exe'
        $report = Join-Path $releaseRoot $package.Report
        $test = Start-Process -FilePath $exe -ArgumentList @('--self-test', ('"'+$report+'"')) -WindowStyle Hidden -Wait -PassThru
        if ($test.ExitCode -ne 0) { throw ('Kontroller başarısız: ' + $report) }
    }
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
        $runtime = Get-Content -LiteralPath (Join-Path $repoRoot 'installer/dotnet-runtime.json') -Raw | ConvertFrom-Json
        if ($runtime.url -notmatch '^https://builds\.dotnet\.microsoft\.com/dotnet/WindowsDesktop/10\.0\.[0-9]+/windowsdesktop-runtime-10\.0\.[0-9]+-win-x64\.exe$' -or $runtime.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Geçersiz .NET runtime manifesti.' }
        foreach ($package in @(@{Path=$smallRoot; Full='0'}, @{Path=$publishRoot; Full='1'})) {
            $driverSha = (Get-FileHash -LiteralPath (Join-Path $package.Path 'engines\zapret2\WinDivert64.sys') -Algorithm SHA256).Hash
            & $Iscc ('/DAppVersion='+$version) ('/DPublishDir='+$package.Path) ('/DReleaseDir='+$releaseRoot) ('/DDriverSha='+$driverSha) ('/DFullPackage='+$package.Full) ('/DRuntimeUrl='+$runtime.url) ('/DRuntimeSha='+$runtime.sha256) (Join-Path $repoRoot 'installer\SilNet.iss')
            if ($LASTEXITCODE -ne 0) { throw 'Kurulum paketi oluşturulamadı.' }
        }
        if ($BuildBootstrapTest) {
            if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Runtime kurulum denemesi yalnızca geçici CI makinesinde yapılır.' }
            $bootstrapTestDir = Join-Path $releaseRoot 'bootstrap-test'
            New-Item -ItemType Directory -Force $bootstrapTestDir | Out-Null
            # Test-only binary exercises the real download/install even on a runner with .NET present.
            # It stays in a subdirectory and is never included in release assets.
            & $Iscc ('/DAppVersion='+$version) ('/DPublishDir='+$smallRoot) ('/DReleaseDir='+$bootstrapTestDir) ('/DDriverSha='+$driverSha) /DFullPackage=0 /DForceRuntimeBootstrap=1 ('/DRuntimeUrl='+$runtime.url) ('/DRuntimeSha='+$runtime.sha256) (Join-Path $repoRoot 'installer\SilNet.iss')
            if ($LASTEXITCODE -ne 0) { throw 'Bootstrap test paketi oluşturulamadı.' }
        }
    }
    $checksums = Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object { $_.Name -match '^SilNet-.*\.(zip|exe)$' } | ForEach-Object {
        ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name)
    }
    [IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $checksums)
} finally { Pop-Location }
