param([Parameter(Mandatory=$true)][string]$Stage,[Parameter(Mandatory=$true)][string]$TemporaryRoot)
$ErrorActionPreference='Stop'
function Invoke-LauncherCheck([string]$Executable,[string[]]$Arguments) {
    $savedRoot=$env:DOTNET_ROOT; $savedX64=$env:DOTNET_ROOT_X64; $savedLookup=$env:DOTNET_MULTILEVEL_LOOKUP
    try {
        $env:DOTNET_ROOT=Join-Path $TemporaryRoot 'no-installed-dotnet'
        $env:DOTNET_ROOT_X64=$env:DOTNET_ROOT; $env:DOTNET_MULTILEVEL_LOOKUP='0'
        $child=Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    } finally { $env:DOTNET_ROOT=$savedRoot; $env:DOTNET_ROOT_X64=$savedX64; $env:DOTNET_MULTILEVEL_LOOKUP=$savedLookup }
    if (-not $child.WaitForExit(30000)) {
        Stop-Process -Id $child.Id -ErrorAction SilentlyContinue
        throw "Launcher check timed out: $($Arguments -join ' ')"
    }
    $child.WaitForExit()
    return $child.ExitCode
}
$fixture=Join-Path $TemporaryRoot ('package-test-'+[guid]::NewGuid().ToString('N'))
$apps=Join-Path $fixture 'apps'
New-Item -ItemType Directory -Path $apps -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $Stage -Force) { Copy-Item -LiteralPath $item.FullName -Destination $apps -Recurse -Force }
$results=@()
foreach ($name in @('VIctory','XCL','Leonida','Xbox2Windows')) {
    $project=Join-Path $apps $name
    $data=Join-Path $fixture "data\$name"
    & (Join-Path $apps 'tools\Initialize-Project.ps1') -ProjectRoot $project -DataRoot $data
    if (-not (Test-Path -LiteralPath (Join-Path $project 'Source'))) { throw 'First-run source link was not created.' }
    $settings=Get-Content -LiteralPath (Join-Path $data 'launcher-data\settings.json') -Raw | ConvertFrom-Json
    if ($settings.GameExe) { throw 'First-run configuration must not contain a preconfigured game path.' }
    $refused=$false
    try { & (Join-Path $apps 'tools\Initialize-Project.ps1') -ProjectRoot $project -DataRoot $data } catch { $refused=$true }
    if (-not $refused) { throw 'Setup unexpectedly overwrote an existing project.' }
    $exe=Join-Path $project "LauncherApp\$name Launcher.exe"
    $runtime=Get-Content -LiteralPath (Join-Path $project "LauncherApp\$name Launcher.runtimeconfig.json") -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or -not $runtime.runtimeOptions.includedFrameworks) { throw 'Release launcher still requires an installed .NET runtime.' }
    $code=Invoke-LauncherCheck $exe @('--self-test')
    if ($code -ne 0) { Get-Content -LiteralPath (Join-Path $data 'launcher-data\self-test.txt'); throw "$name launcher self-test failed." }
    if ($name -eq 'Leonida') {
        $code=Invoke-LauncherCheck $exe @('--control-tests')
        if ($code -ne 0) { Get-Content -LiteralPath (Join-Path $data 'launcher-data\pause-control-test.txt'); throw 'Pause/resume process tests failed.' }
    }
    $preview=Join-Path $fixture "$name-first-run.png"
    $code=Invoke-LauncherCheck $exe @('--preview-first-run',$preview)
    if ($code -ne 0 -or -not (Test-Path -LiteralPath $preview)) { throw 'First-run dialog render failed.' }
    $results+="$name : self-contained launch with invalid global runtime paths, isolated setup, overwrite refusal, launcher smoke: PASS"
}
# Malicious additions to an allowlist must be rejected, even if someone has a
# private source checkout in the same working directory.
$bad=Join-Path $fixture 'bad-manifest.json'
@('VIctory/Source/game.exe') | ConvertTo-Json | Set-Content -LiteralPath $bad -Encoding UTF8
$refused=$false
try { & (Join-Path $apps 'tools\Validate-Manifest.ps1') -Root $apps -Manifest $bad | Out-Null } catch { $refused=$true }
if (-not $refused) { throw 'Packaging accepted an upstream/game source path.' }
$results | Set-Content -LiteralPath (Join-Path $TemporaryRoot 'verification.txt') -Encoding UTF8
& (Join-Path $apps 'tests\Test-LeonidaBackends.ps1') -Stage $apps -Fixture $fixture
$results | Write-Output
Write-Output 'Packaging exclusion test: PASS. No upstream clone, game code or shader generation was run.'
