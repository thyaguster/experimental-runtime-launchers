param([Parameter(Mandatory=$true)][string]$ProjectRoot, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
function Get-SharedFileHash([string]$Path) {
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)) }
    finally { $hash.Dispose(); $stream.Dispose() }
}
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$projectName = Split-Path -Leaf $ProjectRoot
if ($projectName -notin @('VIctory','XCL','Leonida','Xbox2Windows')) { throw 'Unknown launcher project.' }
$stateLink = Join-Path $ProjectRoot 'launcher-data'
$stateItem = Get-Item -LiteralPath $stateLink -Force
if (-not ($stateItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Current project storage is not linked. Migration stopped without changes.' }
$oldState = [IO.Path]::GetFullPath([string]$stateItem.Target).TrimEnd('\')
$oldRoot = Split-Path -Parent $oldState
if ($Destination.Equals($oldRoot, [StringComparison]::OrdinalIgnoreCase)) { Write-Output 'This project folder is already selected.'; return }
if ($Destination.StartsWith($oldRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $oldRoot.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a separate project folder, not a parent or child of the current folder.' }
if ($Destination.Equals($ProjectRoot, [StringComparison]::OrdinalIgnoreCase) -or $Destination.StartsWith($ProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $ProjectRoot.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep generated project data separate from the launcher source folder.' }
$gameProcess = switch ($projectName) { 'VIctory' {'victory'} 'XCL' {'xcl'} 'Leonida' {'gta6rt'} 'Xbox2Windows' {'offscreen_check'} }
if (Get-Process -Name $gameProcess,'cargo','rustc' -ErrorAction SilentlyContinue) { throw 'Stop game sessions and builds before changing the project folder.' }
if (@(Get-Process -Name "$projectName Launcher" -ErrorAction SilentlyContinue).Count -gt 1) { throw 'Close other instances of this launcher first.' }
$lock = [IO.File]::Open((Join-Path $oldState 'engine-update.lock'), 'OpenOrCreate', 'ReadWrite', 'Read')
try {
    $mappings = @(
        @{Link='launcher-data';Relative='launcher-data'}, @{Link='Engine';Relative='Engine'},
        @{Link='Source';Relative='source'}, @{Link='Launcher\bin';Relative='build\launcher-bin'},
        @{Link='Launcher\obj';Relative='build\launcher-obj'}
    )
    if ($projectName -eq 'Leonida') {
        $mappings += @{Link='build';Relative='build\native'}
        $mappings += @{Link='work';Relative='work'}
    }
    # The old AppData cache alias must follow XCL's project-folder selection too.
    if ($projectName -eq 'XCL') {
        $appCache = Join-Path $env:LOCALAPPDATA 'xcl\dxil'
        if (Test-Path -LiteralPath $appCache) {
            $item = Get-Item -LiteralPath $appCache -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and ([IO.Path]::GetFullPath([string]$item.Target) -eq (Join-Path $oldState 'dxil'))) {
                $mappings += @{ Link=$appCache; Relative='launcher-data\dxil'; Absolute=$true }
            }
        }
    }
    foreach ($mapping in $mappings) {
        $link = if ($mapping.Absolute) { $mapping.Link } else { Join-Path $ProjectRoot $mapping.Link }
        $item = Get-Item -LiteralPath $link -Force
        $expected = Join-Path $oldRoot $mapping.Relative
        if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or [IO.Path]::GetFullPath([string]$item.Target) -ne $expected) { throw "Unexpected folder link; no changes made: $link" }
    }
    if (Test-Path -LiteralPath $Destination) {
        $item = Get-Item -LiteralPath $Destination -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or (Get-ChildItem -LiteralPath $Destination -Force)) { throw 'Choose an empty destination folder. Existing files will not be overwritten.' }
    }
    $children = @(Get-ChildItem -LiteralPath $oldRoot -Recurse -Force)
    if ($children | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'The project data contains another folder link; migrate it manually.' }
    $files = @($children | Where-Object { -not $_.PSIsContainer })
    Write-Output ("Copying {0} files to {1}..." -f $files.Count, $Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($child in Get-ChildItem -LiteralPath $oldRoot -Force) { Copy-Item -LiteralPath $child.FullName -Destination $Destination -Recurse -Force }
    if (@(Get-ChildItem -LiteralPath $Destination -Recurse -File -Force).Count -ne $files.Count) { throw 'Copy count verification failed; the original project folder is retained.' }
    foreach ($file in $files) {
        $copy = Join-Path $Destination $file.FullName.Substring($oldRoot.Length + 1)
        if ((Get-Item -LiteralPath $copy).Length -ne $file.Length -or (Get-SharedFileHash $file.FullName) -ne (Get-SharedFileHash $copy)) { throw "Copy verification failed; original data retained: $($file.Name)" }
    }
    Write-Output 'Copied files verified. Switching project folder links...'
    $switched = @()
    try {
        foreach ($mapping in $mappings) {
            $link = if ($mapping.Absolute) { $mapping.Link } else { Join-Path $ProjectRoot $mapping.Link }
            # Delete only the verified junction, never its contents.
            [IO.Directory]::Delete($link)
            $switched += $mapping
            New-Item -ItemType Junction -Path $link -Target (Join-Path $Destination $mapping.Relative) | Out-Null
        }
    } catch {
        foreach ($mapping in $switched) {
            $link = if ($mapping.Absolute) { $mapping.Link } else { Join-Path $ProjectRoot $mapping.Link }
            if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
            New-Item -ItemType Junction -Path $link -Target (Join-Path $oldRoot $mapping.Relative) | Out-Null
        }
        throw
    }
    # Update the default Leonida shader catalog if it belonged to the old project.
    $settingsPath = Join-Path $Destination 'launcher-data\settings.json'
    if ($projectName -eq 'Leonida' -and (Test-Path -LiteralPath $settingsPath)) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($settings.ShaderDir -and $settings.ShaderDir.StartsWith($oldRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            $settings.ShaderDir = Join-Path $Destination $settings.ShaderDir.Substring($oldRoot.Length + 1)
            $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
        }
    }
    $lock.Dispose(); $lock = $null
    # Absolute source and destination are disjoint, all junctions now point at verified copies.
    if (-not $oldRoot -or $oldRoot -eq [IO.Path]::GetPathRoot($oldRoot) -or $oldRoot -eq $ProjectRoot) { throw 'Refusing to remove an invalid old project path.' }
    Remove-Item -LiteralPath $oldRoot -Recurse -Force
    Write-Output "Project folder changed to $Destination. The previous copy was removed after verification."
} finally { if ($lock) { $lock.Dispose() } }
