param([Parameter(Mandatory=$true)][string]$ProjectRoot,[Parameter(Mandatory=$true)][string]$DataRoot,[string]$GameExe='')
$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
if (-not [IO.Path]::IsPathRooted($DataRoot)) { throw 'Choose an absolute data folder.' }
$DataRoot=[IO.Path]::GetFullPath($DataRoot).TrimEnd('\')
$name=Split-Path -Leaf $ProjectRoot
if ($name -notin @('VIctory','XCL','Leonida','Xbox2Windows')) { throw 'Unknown project.' }
if ($DataRoot -eq [IO.Path]::GetPathRoot($DataRoot) -or $DataRoot -eq $ProjectRoot -or $DataRoot.StartsWith($ProjectRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $ProjectRoot.StartsWith($DataRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Select a separate, empty project data folder outside the launcher installation.' }
if ($GameExe) {
    if (-not (Test-Path -LiteralPath $GameExe -PathType Leaf)) { throw 'The selected game EXE does not exist.' }
    $game=Split-Path -Parent ([IO.Path]::GetFullPath($GameExe))
    if ($game -eq $DataRoot -or $game.StartsWith($DataRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'The data folder cannot contain or replace the game folder.' }
}
if (Test-Path -LiteralPath $DataRoot) {
    $item=Get-Item -LiteralPath $DataRoot -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or (Get-ChildItem -LiteralPath $DataRoot -Force)) { throw 'First-time setup requires an empty data folder. Existing files will not be overwritten.' }
}
$mappings=@(
    @{Link='launcher-data';Target='launcher-data'},@{Link='Engine';Target='Engine'},
    @{Link='Source';Target='source'},@{Link='Launcher\bin';Target='build\launcher-bin'},@{Link='Launcher\obj';Target='build\launcher-obj'}
)
if ($name -eq 'Leonida') { $mappings+=@{Link='work';Target='work'}; $mappings+=@{Link='build';Target='build\native'} }
foreach ($map in $mappings) { if (Test-Path -LiteralPath (Join-Path $ProjectRoot $map.Link)) { throw "Setup target already exists: $($map.Link). Use a clean extracted release folder." } }
New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null
$created=@()
try {
    foreach ($map in $mappings) {
        $link=Join-Path $ProjectRoot $map.Link; $target=Join-Path $DataRoot $map.Target
        New-Item -ItemType Directory -Path $target,(Split-Path -Parent $link) -Force | Out-Null
        New-Item -ItemType Junction -Path $link -Target $target | Out-Null
        $created+=$link
    }
    $settings=[ordered]@{GameExe=$GameExe;Warp=($name -eq 'VIctory' -or $name -eq 'Xbox2Windows')}
    if ($name -eq 'Leonida') { $settings=[ordered]@{GameExe=$GameExe;Backend='Integrated';Vulkan=$true;ShaderDir=(Join-Path $DataRoot 'work\shaders\spirv')} }
    $settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $DataRoot 'launcher-data\settings.json') -Encoding UTF8
    Write-Output "Configured $name. Data: $DataRoot. No upstream source was downloaded or executed."
} catch {
    foreach ($link in $created) { if ((Get-Item -LiteralPath $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($link) } }
    throw
}
