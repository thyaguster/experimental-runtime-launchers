param([switch]$UpdateLockHeld)
$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$repo = Join-Path $workspace 'Source'
$shared = Join-Path (Split-Path -Parent $workspace) 'tools'
$dataLink = Get-Item -LiteralPath (Join-Path $workspace 'launcher-data') -Force
$project = Split-Path -Parent ([string]$dataLink.Target)
. (Join-Path $workspace 'Engine-Install.ps1')
$lock = $null
if (-not $UpdateLockHeld) { $lock = Open-EngineLock $workspace }
try {
    $env:TEMP = Join-Path $project 'temp'; $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    $env:PIP_CACHE_DIR = Join-Path $project 'tools\pip-cache'
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $project 'tools\zig-global'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $project 'tools\zig-local'
    $venv = Join-Path $repo 'out\pe-env'
    $python = Join-Path $venv 'Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $python)) { & python.exe -m venv $venv; if ($LASTEXITCODE -ne 0) { throw 'Python environment creation failed.' } }
    & $python -m pip install -r (Join-Path $repo 'requirements-dev.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Pinned Python dependency installation failed.' }
    . (Join-Path (Split-Path -Parent $workspace) 'tools/BuildEnvironment.ps1')
    Enter-NativeBuild
    $cmake = Find-NativeTool 'cmake.exe'
    $ninja = Find-NativeTool 'ninja.exe'
    $sdkBins = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $dxc = Get-ChildItem -LiteralPath $sdkBins -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64\dxc.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $dxc) { throw 'DXC from the Windows SDK is required.' }
    $build = Join-Path $repo 'out\build\release'
    & $cmake -S $repo -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" -DCMAKE_BUILD_TYPE=Release "-DINTAKE_PYTHON=$python" "-DDXC_EXECUTABLE=$dxc"
    if ($LASTEXITCODE -ne 0) { throw 'Xbox2Windows CMake configure failed.' }
    & $cmake --build $build --target d3d12_sdk_check era_heap_check offscreen_check pe_map xmem_shim d3d12_xs_shim xgameruntime_shim --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Xbox2Windows tool build failed.' }
    & $cmake -E chdir $build (Join-Path (Split-Path -Parent $cmake) 'ctest.exe') --no-tests=error --output-on-failure -R '^(desktop_root_signature|era_heap_allocation|offscreen_warp)$'
    if ($LASTEXITCODE -ne 0) { throw 'Required source-built smoke tests did not pass; the installed suite was preserved.' }
    $commit = & git -c "safe.directory=$repo" -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not read source commit.' }
    Publish-Engine $workspace (Join-Path $build 'offscreen_check.exe') $commit
} finally { if ($lock) { $lock.Dispose() } }
