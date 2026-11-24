param([switch]$UpdateLockHeld,[ValidateSet('Native','Integrated')][string]$Backend='Native')
$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$repo = Join-Path $workspace 'Source'
$sharedTools = Join-Path (Split-Path -Parent $workspace) 'tools'
. (Join-Path $workspace 'Engine-Install.ps1')
$engineLock = $null
if (-not $UpdateLockHeld) { $engineLock = Open-EngineLock $workspace }
try {
    if ($Backend -eq 'Integrated') {
        . (Join-Path $workspace 'Integrated-Backend.ps1')
        Build-IntegratedEngine $workspace
        return
    }
    $project=Split-Path -Parent ([string](Get-Item -LiteralPath (Join-Path $workspace 'launcher-data') -Force).Target)
    $env:TEMP=Join-Path $project 'temp'; $env:TMP=$env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    . (Join-Path (Split-Path -Parent $workspace) 'tools/BuildEnvironment.ps1')
    Enter-NativeBuild
    $cmake = Find-NativeTool 'cmake.exe'
    $ninja = Find-NativeTool 'ninja.exe'
    $build = Join-Path $workspace 'build'
    # Compile the native Leonida runtime, not the fork's optional VIctory backend.
    # These options fetch only hash-pinned open-source dependencies, never game data.
    & $cmake -S (Join-Path $repo 'runtime') -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DBUILD_TESTING=OFF -DLEONIDA_VULKAN_HEADERS_FETCH=ON -DLEONIDA_REQUIRE_VULKAN=ON -DLEONIDA_SDL3_FETCH=ON
    if ($LASTEXITCODE -ne 0) { throw "Leonida configure failed (exit $LASTEXITCODE)." }
    & $cmake --build $build --target gta6rt --parallel 4
    if ($LASTEXITCODE -ne 0) { throw "Leonida build failed (exit $LASTEXITCODE)." }
    $commit = & git -c "safe.directory=$repo" -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the built source version.' }
    Publish-Engine $workspace (Join-Path $build 'gta6rt.exe') $commit
} finally { if ($engineLock) { $engineLock.Dispose() } }
