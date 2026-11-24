param([ValidateSet('Check', 'Update', 'Rollback')][string]$Action = 'Check')
$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$repo = Join-Path $workspace 'Source'
. (Join-Path $workspace 'Engine-Install.ps1')
$engineLock = Open-EngineLock $workspace
try {
    if ($Action -eq 'Rollback') { Restore-PreviousEngine $workspace; return }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is required to check for updates.' }
    if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw "Xbox2Windows checkout is missing: $repo" }
    function Invoke-RepoGit {
        $output = & git -c "safe.directory=$repo" -c core.hooksPath=NUL -C $repo @args
        if ($LASTEXITCODE -ne 0) { throw "Git failed (exit $LASTEXITCODE): $args" }
        return $output
    }
    $env:GIT_TERMINAL_PROMPT = '0'
    Write-Output 'Checking https://github.com/R0Y00/Xbox2Windows (main)...'
    # Use the fixed upstream URL, regardless of local remote configuration.
    Invoke-RepoGit fetch --no-tags 'https://github.com/R0Y00/Xbox2Windows.git' '+refs/heads/main:refs/remotes/xbox2windows-upstream/main'
    $latest = Invoke-RepoGit rev-parse refs/remotes/xbox2windows-upstream/main
    $source = Invoke-RepoGit rev-parse HEAD
    $installed = Read-EngineManifest $workspace
    Write-Output "Latest upstream: $latest"
    Write-Output "Local source:    $source"
    if ($installed) { Write-Output "Installed engine: $($installed.commit)" }
    else { Write-Output 'Installed engine: legacy build (select Update engine to register it)' }
    if ($installed -and $installed.commit -eq $latest -and (Test-Path -LiteralPath (Join-Path $workspace "Engine\$($installed.executable)"))) {
        Write-Output 'Your installed engine is up to date.'
        return
    }
    if ($Action -eq 'Check') {
        Write-Output 'An engine update/build is available. Select Update engine to install it.'
        Invoke-RepoGit log -5 --format='%h %s' refs/remotes/xbox2windows-upstream/main
        return
    }
    if (Invoke-RepoGit status --porcelain) { throw 'Local source changes found. Update stopped to preserve them; commit or move those changes first.' }
    $branch = Invoke-RepoGit symbolic-ref --short HEAD
    if ($branch -ne 'main') { throw "Source is on branch '$branch'. Switch to main before updating." }
    # Never reset or discard local commits. Divergence stops here, before building.
    Invoke-RepoGit merge --ff-only --no-edit $latest
    if ((Invoke-RepoGit rev-parse HEAD) -ne $latest) { throw 'Local source is ahead of upstream. Update stopped without installing an engine.' }
    Write-Output 'Building Xbox2Windows release engine. The installed version remains available until this succeeds.'
    & (Join-Path $workspace 'Build-Xbox2Windows.ps1') -UpdateLockHeld
    if (-not $?) { throw 'Engine build failed; the previous installed engine remains selected.' }
    Write-Output 'Update complete. Your game files, saves and settings were preserved.'
} finally { $engineLock.Dispose() }
