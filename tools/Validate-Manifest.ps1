param([Parameter(Mandatory=$true)][string]$Root,[string]$Manifest)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root).TrimEnd('\')
if (-not $Manifest) { $Manifest=Join-Path $Root 'release-files.json' }
$entries=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$seen=@{}
foreach ($entry in $entries) {
    if ($entry -isnot [string] -or $entry -match '(^|[/\\])(Source|Engine|launcher-data|LauncherApp[^/\\]*|bin|obj|__pycache__)([/\\]|$)' -or $entry -match '(^|[/\\])\.\.' -or [IO.Path]::IsPathRooted($entry)) { throw "Forbidden release source path: $entry" }
    $full=[IO.Path]::GetFullPath((Join-Path $Root $entry))
    if (-not $full.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($full)) { throw 'Duplicate or escaping release path.' }
    $seen[$full]=$true
    $extension=[IO.Path]::GetExtension($full).ToLowerInvariant()
    if ($extension -notin @('.cs','.csproj','.props','.ps1','.py','.cmd','.json','.manifest','.md','.txt','.yml','.yaml','.gitignore','.gitattributes','') -or [IO.Path]::GetFileName($full) -in @('settings.json','launcher-setup.json')) { throw "Disallowed release source type: $entry" }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Missing release source file: $entry" }
    if ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked release source: $entry" }
    for ($cursor=Get-Item -LiteralPath (Split-Path -Parent $full) -Force; $cursor -and $cursor.FullName -ne $Root; $cursor=$cursor.Parent) {
        if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release source may not traverse folder links: $entry" }
    }
    if ((Get-Item -LiteralPath $full).Length -gt 1MB) { throw "Unexpectedly large source file: $entry" }
    $text=[IO.File]::ReadAllText($full)
    if ($text.StartsWith('MZ') -or $text -match '(?i)[A-Z]:\\(?:Users\\[^\\]+|Jogos\\|foudasse\\|Scripts\\gta-vi)') { throw "Binary or machine-specific source content: $entry" }
    Write-Output $entry
}
