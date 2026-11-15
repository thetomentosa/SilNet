$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Bu kurulum testi yalnızca geçici GitHub Actions makinesinde çalıştırılır.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repoRoot 'artifacts'
$target = [IO.Path]::GetFullPath((Join-Path $artifacts 'install-check'))
if (!$target.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Geçersiz test hedefi.' }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{E581D4D0-6AD9-4ED3-8A84-7570DE5359DE}_is1'
if (Test-Path $key) { throw 'Mevcut kurulumun üzerine test yapılmaz.' }
$version = ([xml](Get-Content (Join-Path $repoRoot 'SilNet.csproj'))).Project.PropertyGroup.Version
$results = [Collections.Generic.List[string]]::new()
$installed = $false
try {
    foreach ($kind in @('small', 'full', 'small-again')) {
        $suffix = if ($kind -eq 'full') { 'Full-' } else { '' }
        $setup = Join-Path $artifacts "SilNet-Setup-$suffix$version-win-x64.exe"
        $logPath = Join-Path $artifacts "installer-$kind.log"
        $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS', ('/DIR="'+$target+'"'), ('/LOG="'+$logPath+'"'))
        $process = Start-Process $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin @(0,3010)) { throw "Kurulum başarısız: $kind, $($process.ExitCode). $(Get-Content $logPath -Tail 40)" }
        $installed = $true
        $report = Join-Path $artifacts "test-results-installed-$kind.json"
        $process = Start-Process (Join-Path $target 'SilNet.exe') -ArgumentList @('--self-test', ('"'+$report+'"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Kurulu uygulama testleri başarısız: $kind" }
        $results.Add("PASS: $kind installer and installed application self-tests")
        if ($kind -eq 'small-again') {
            if ([IO.File]::GetLastWriteTimeUtc($driver) -ne $driverStamp) { throw 'Aynı sürücü güncellemede değiştirildi.' }
            $results.Add('PASS: full-to-small upgrade preserves the identical driver')
        }
        $driver = Join-Path $target 'engines\zapret2\WinDivert64.sys'
        $driverStamp = [IO.File]::GetLastWriteTimeUtc($driver)
    }
} finally {
    if ($installed) {
        $uninstaller = Join-Path $target 'unins000.exe'
        $process = Start-Process $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0 -or (Test-Path (Join-Path $target 'SilNet.exe'))) { throw 'Geçici kurulum kaldırılamadı.' }
        $results.Add('PASS: uninstall removes application; shared .NET runtime is not removed')
    }
    $results | ConvertTo-Json | Set-Content (Join-Path $artifacts 'installer-tests.json')
}
$results
