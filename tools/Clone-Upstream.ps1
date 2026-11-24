param([Parameter(Mandatory=$true)][string]$ProjectRoot)
$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot)
$manifest=Get-Content -LiteralPath (Join-Path (Split-Path -Parent $ProjectRoot) 'upstreams.json') -Raw | ConvertFrom-Json
$name=Split-Path -Leaf $ProjectRoot
$project=@($manifest | Where-Object name -eq $name)
if ($project.Count -ne 1 -or $project[0].repository -notmatch '^https://github\.com/[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+\.git$' -or $project[0].testedCommit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid upstream manifest.' }
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { throw 'Install Git for Windows from https://git-scm.com/downloads/win, then reopen the launcher.' }
$repo=Join-Path $ProjectRoot 'Source'
if (Test-Path -LiteralPath (Join-Path $repo '.git')) {
    $origin=& git -c "safe.directory=$repo" -C $repo remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $origin -ne $project[0].repository) { throw 'The existing source checkout has an unexpected origin. No changes made.' }
    Write-Output 'Source already exists. Use Check updates / Update for newer revisions.'; return
}
if (Get-ChildItem -LiteralPath $repo -Force) { throw 'The source folder is not empty. Existing files will not be overwritten.' }
$env:GIT_TERMINAL_PROMPT='0'
& git clone -- $project[0].repository $repo
if ($LASTEXITCODE -ne 0) { throw 'Source clone failed.' }
& git -c "safe.directory=$repo" -c core.hooksPath=NUL -C $repo checkout -B main $project[0].testedCommit
if ($LASTEXITCODE -ne 0) { throw 'Could not select the tested upstream revision.' }
Write-Output "Source ready at tested revision $($project[0].testedCommit). Build it locally; later updates are manual."
