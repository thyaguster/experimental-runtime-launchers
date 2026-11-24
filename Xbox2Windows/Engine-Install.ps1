# Shared engine installation functions. Each build has its own directory;
# current.json switches versions only after a successful build and copy.
function Read-EngineManifest([string]$Workspace, [string]$Name = 'current.json') {
    $path = Join-Path $Workspace "Engine\$Name"
    if (Test-Path -LiteralPath $path) { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    return $null
}

function Write-EngineManifest([string]$Workspace, $Manifest) {
    $engineRoot = Join-Path $Workspace 'Engine'
    New-Item -ItemType Directory -Path $engineRoot -Force | Out-Null
    $current = Join-Path $engineRoot 'current.json'
    $temporary = Join-Path $engineRoot ('manifest-' + [guid]::NewGuid().ToString('N') + '.tmp')
    $Manifest | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
    if (Test-Path -LiteralPath $current) {
        [IO.File]::Replace($temporary, $current, (Join-Path $engineRoot 'previous.json'))
    } else { [IO.File]::Move($temporary, $current) }
}

function Publish-Engine([string]$Workspace, [string]$Binary, [string]$Commit) {
    if ($Commit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid source commit.' }
    if (-not (Test-Path -LiteralPath $Binary -PathType Leaf)) { throw "Built engine not found: $Binary" }
    $bundle = Split-Path -Parent $Binary
    $required = @('d3d12_sdk_check.exe','era_heap_check.exe','offscreen_check.exe','pe_map.dll','xmem.dll','d3d12_xs.dll','fixture-shims\xgameruntime.dll')
    foreach ($file in $required) { if (-not (Test-Path -LiteralPath (Join-Path $bundle $file) -PathType Leaf)) { throw "Built tool is missing: $file" } }
    $pythonTools = Join-Path $Workspace 'Source\tools'
    if (-not (Test-Path -LiteralPath (Join-Path $pythonTools 'compat_report.py'))) { throw 'Offline report tools are missing.' }
    $version = $Commit.Substring(0, 12) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $relative = "versions\$version\offscreen_check.exe"
    $destination = Join-Path $Workspace "Engine\$relative"
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    foreach ($file in $required) { Copy-Item -LiteralPath (Join-Path $bundle $file) -Destination (Split-Path -Parent $destination) -ErrorAction Stop }
    Copy-Item -LiteralPath $pythonTools -Destination (Join-Path (Split-Path -Parent $destination) 'tools') -Recurse -ErrorAction Stop
    Copy-Item -LiteralPath (Join-Path $Workspace 'Source\LICENSE') -Destination (Split-Path -Parent $destination)
    Write-EngineManifest $Workspace ([ordered]@{
        commit = $Commit; executable = $relative
        builtUtc = [DateTime]::UtcNow.ToString('o'); diagnostics = $false
    })
    Write-Output "Installed engine: $destination"
}

function Restore-PreviousEngine([string]$Workspace) {
    $previous = Read-EngineManifest $Workspace 'previous.json'
    if (-not $previous) { throw 'No previous engine is available yet.' }
    $root = [IO.Path]::GetFullPath((Join-Path $Workspace 'Engine')) + [IO.Path]::DirectorySeparatorChar
    $binary = [IO.Path]::GetFullPath((Join-Path $root $previous.executable))
    if (-not $binary.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $binary -PathType Leaf)) {
        throw 'Previous engine manifest is invalid or the executable is missing.'
    }
    Write-EngineManifest $Workspace $previous
    Write-Output "Restored engine: $($previous.commit)"
}

function Open-EngineLock([string]$Workspace) {
    New-Item -ItemType Directory -Path (Join-Path $Workspace 'launcher-data') -Force | Out-Null
    try { return [IO.File]::Open((Join-Path $Workspace 'launcher-data\engine-update.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another engine build or update is running. Wait for it to finish.' }
}
