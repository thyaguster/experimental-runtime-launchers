param([Parameter(Mandatory=$true)][string]$ProjectRoot,[switch]$UpdateLockHeld)
$ErrorActionPreference='Stop'
$name=Split-Path -Leaf $ProjectRoot
if ($name -notin @('VIctory','XCL')) { throw 'Unsupported Rust adapter.' }
. (Join-Path $ProjectRoot 'Engine-Install.ps1')
. (Join-Path $PSScriptRoot 'BuildEnvironment.ps1')
$lock=$null
if (-not $UpdateLockHeld) { $lock=Open-EngineLock $ProjectRoot }
try {
    $repo=Join-Path $ProjectRoot 'Source'
    if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw 'Clone upstream first.' }
    foreach ($tool in @('cargo.exe','rustup.exe')) { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw 'Install Rust from https://rustup.rs/, then reopen the launcher.' } }
    Enter-NativeBuild
    $project=Split-Path -Parent ([string](Get-Item -LiteralPath (Join-Path $ProjectRoot 'launcher-data') -Force).Target)
    $env:CARGO_HOME=Join-Path $project 'tools\cargo'; $env:RUSTUP_HOME=Join-Path $project 'tools\rustup'
    $env:TEMP=Join-Path $project 'temp'; $env:TMP=$env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    $channel='stable'
    $toolchain=Join-Path $repo 'rust-toolchain.toml'
    if (Test-Path -LiteralPath $toolchain) {
        $match=[regex]::Match((Get-Content -LiteralPath $toolchain -Raw),'(?m)^channel\s*=\s*"([A-Za-z0-9.\-]+)"')
        if (-not $match.Success) { throw 'Unsupported upstream Rust toolchain format.' }
        $channel=$match.Groups[1].Value
    }
    & rustup.exe toolchain install $channel --profile minimal
    if ($LASTEXITCODE -ne 0) { throw 'Rust toolchain installation failed.' }
    Push-Location $repo
    try {
        $profile=if ($name -eq 'VIctory') {'diag'} else {'release'}
        $package=if ($name -eq 'VIctory') {'victory'} else {'xcl'}
        $stamp=Join-Path $repo 'target\launcher-source-path.txt'
        $old=if (Test-Path -LiteralPath $stamp) {(Get-Content -LiteralPath $stamp -Raw).Trim()} else {''}
        if ($old -and $old -ne $repo) {
            & cargo.exe ("+"+$channel) clean --profile $profile
            if ($LASTEXITCODE -ne 0) { throw 'Stale build-cache cleanup failed.' }
        }
        [string[]]$features=@()
        if ($name -eq 'VIctory') { $features=@('--features','diagnostics') }
        & cargo.exe ("+"+$channel) build --locked --profile $profile -p $package @features
        if ($LASTEXITCODE -ne 0) { throw 'Upstream Rust build failed; installed engine preserved.' }
        Set-Content -LiteralPath $stamp -Value $repo -Encoding UTF8
        $commit=& git -c "safe.directory=$repo" -C $repo rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify built commit.' }
        Publish-Engine $ProjectRoot (Join-Path $repo "target\$profile\$package.exe") $commit
    } finally { Pop-Location }
} finally { if ($lock) { $lock.Dispose() } }
