# Separate installation metadata; native Vulkan engine versions are never replaced.
function Read-IntegratedManifest([string]$Workspace,[string]$Name='current.json') {
    $path=Join-Path $Workspace "Engine\integrated-$Name"
    if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
}
function Write-IntegratedManifest([string]$Workspace,$Manifest) {
    $root=Join-Path $Workspace 'Engine'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $current=Join-Path $root 'integrated-current.json'
    $temp=Join-Path $root ([guid]::NewGuid().ToString('N')+'.tmp')
    $Manifest | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding UTF8
    if (Test-Path -LiteralPath $current) { [IO.File]::Replace($temp,$current,(Join-Path $root 'integrated-previous.json')) }
    else { [IO.File]::Move($temp,$current) }
}
function Restore-IntegratedEngine([string]$Workspace) {
    $manifest=Read-IntegratedManifest $Workspace 'previous.json'
    if (-not $manifest) { throw 'No previous integrated engine is available.' }
    $root=[IO.Path]::GetFullPath((Join-Path $Workspace 'Engine'))+'\'
    $exe=[IO.Path]::GetFullPath((Join-Path $root $manifest.executable))
    if (-not $exe.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Previous integrated engine is missing or invalid.' }
    Write-IntegratedManifest $Workspace $manifest
    Write-Output "Restored integrated engine: $($manifest.commit)"
}
function Build-IntegratedEngine([string]$Workspace) {
    $repo=Join-Path $Workspace 'Source'
    $source=Join-Path $repo 'runtimes\victory'
    if (-not (Test-Path -LiteralPath (Join-Path $source 'Cargo.toml'))) { throw 'Update Leonida source first: runtimes/victory is missing.' }
    $state=Get-Item -LiteralPath (Join-Path $Workspace 'launcher-data') -Force
    $data=if ($state.Attributes -band [IO.FileAttributes]::ReparsePoint) { Split-Path -Parent ([string]$state.Target) } else { Split-Path -Parent $state.FullName }
    $shared=Join-Path (Split-Path -Parent $Workspace) 'tools'
    $localBin=Join-Path $shared 'cargo\bin'
    if (Test-Path -LiteralPath (Join-Path $localBin 'cargo.exe')) { $env:PATH="$localBin;$env:PATH" }
    if (-not (Get-Command rustup.exe -ErrorAction SilentlyContinue)) { throw 'Install Rust/rustup from https://rustup.rs/ and reopen the launcher.' }
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $devShell=Join-Path $shared 'BuildTools\Common7\Tools\Launch-VsDevShell.ps1'
    if (Test-Path -LiteralPath $vswhere) {
        $vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($vs) { $devShell=Join-Path $vs 'Common7\Tools\Launch-VsDevShell.ps1' }
    }
    if (-not (Test-Path -LiteralPath $devShell)) { throw 'Microsoft C++ Build Tools are required.' }
    & $devShell -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
    $env:CARGO_HOME=Join-Path $data 'tools\integrated-cargo'
    $env:RUSTUP_HOME=Join-Path $data 'tools\integrated-rustup'
    $env:CARGO_TARGET_DIR=Join-Path $data 'build\integrated-victory'
    $env:TEMP=Join-Path $data 'temp'; $env:TMP=$env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    $match=[regex]::Match((Get-Content (Join-Path $source 'rust-toolchain.toml') -Raw),'(?m)^channel\s*=\s*"([A-Za-z0-9.\-]+)"')
    if (-not $match.Success) { throw 'Invalid upstream Rust toolchain.' }
    $channel=$match.Groups[1].Value
    & rustup.exe toolchain install $channel --profile minimal --target x86_64-pc-windows-msvc --no-self-update
    if ($LASTEXITCODE -ne 0) { throw 'Rust toolchain installation failed.' }
    Push-Location $source
    try {
        & cargo.exe ("+"+$channel) build --locked --profile diag -p victory --features diagnostics --target x86_64-pc-windows-msvc
        if ($LASTEXITCODE -ne 0) { throw 'Integrated VIctory build failed; previous engine preserved.' }
        $commit=& git -c "safe.directory=$repo" -C $repo rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify source revision.' }
        $binary=Join-Path $env:CARGO_TARGET_DIR 'x86_64-pc-windows-msvc\diag\victory.exe'
        if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw 'Built integrated runtime not found.' }
        $relative='versions\integrated-'+$commit.Substring(0,12)+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)+'\victory.exe'
        $destination=Join-Path $Workspace "Engine\$relative"
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $binary -Destination $destination
        Write-IntegratedManifest $Workspace ([ordered]@{commit=$commit;executable=$relative;diagnostics=$true;backend='Integrated';builtUtc=[DateTime]::UtcNow.ToString('o')})
        Write-Output "Installed integrated VIctory engine: $destination"
    } finally { Pop-Location }
}
