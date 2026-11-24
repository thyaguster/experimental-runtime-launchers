param([string]$ProjectRoot = $PSScriptRoot, [switch]$Confirmed)
$ErrorActionPreference = 'Stop'
if (-not $Confirmed) { throw 'Deletion requires explicit confirmation from the launcher.' }
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$stateLink = Get-Item -LiteralPath (Join-Path $ProjectRoot 'launcher-data') -Force
if (-not ($stateLink.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Expected project data link was not found; nothing was deleted.' }
$data = [IO.Path]::GetFullPath([string]$stateLink.Target)
$project = Split-Path -Parent $data
$work = [IO.Path]::GetFullPath((Join-Path $project 'work'))
$cache = [IO.Path]::GetFullPath((Join-Path $work 'shaders'))
if (-not $project -or $project -eq [IO.Path]::GetPathRoot($project) -or -not $cache.StartsWith($project.TrimEnd('\') + '\work\', [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $cache) -ne 'shaders') { throw 'Invalid cache path; nothing was deleted.' }
. (Join-Path $PSScriptRoot 'Engine-Install.ps1')
$lock = Open-EngineLock $ProjectRoot
try {
    if (Get-Process -Name gta6rt -ErrorAction SilentlyContinue) { throw 'Stop the game before deleting its shader cache.' }
    if (Test-Path -LiteralPath $work) {
        if ((Get-Item -LiteralPath $work -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The work folder redirects elsewhere; deletion refused.' }
    }
    if (-not (Test-Path -LiteralPath $cache)) { Write-Output 'No managed shader cache exists. Nothing was deleted.'; return }
    if ((Get-Item -LiteralPath $cache -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The cache folder is a link; deletion refused.' }
    $items = @(Get-ChildItem -LiteralPath $cache -Recurse -Force)
    if ($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'The cache contains a folder link; deletion refused.' }
    $settingsPath = Join-Path $data 'settings.json'
    if (Test-Path -LiteralPath $settingsPath) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($settings.GameExe) {
            $game = Split-Path -Parent ([IO.Path]::GetFullPath($settings.GameExe))
            if ($cache -eq $game -or $game.StartsWith($cache + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Cache overlaps game files; deletion refused.' }
        }
    }
    # Only this absolute, checked, non-linked directory is recursively removed.
    Remove-Item -LiteralPath $cache -Recurse -Force
    Write-Output "Deleted generated shader cache: $cache"
    Write-Output 'Saves, overlay, installed tools, engines and game files were preserved. Generate shaders before the next Vulkan launch.'
} finally { $lock.Dispose() }
