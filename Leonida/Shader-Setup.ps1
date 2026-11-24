param([ValidateSet('InstallTools','Prepare','Check','Plan')][string]$Action = 'Plan', [string]$GameExe)
$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$stateLink = Get-Item -LiteralPath (Join-Path $workspace 'launcher-data') -Force
$project = if ($stateLink.Attributes -band [IO.FileAttributes]::ReparsePoint) { Split-Path -Parent ([string]$stateLink.Target) } else { $workspace }
$project = [IO.Path]::GetFullPath($project)
$tools = Join-Path $project 'tools'
$prefix = Join-Path $tools 'shader-env'
$toolBin = Join-Path $prefix 'Library\bin'
$llvm = Join-Path $toolBin 'llvm-mc.exe'
$spirvAs = Join-Path $toolBin 'spirv-as.exe'
$spirvVal = Join-Path $toolBin 'spirv-val.exe'
$work = Join-Path $project 'work'
$catalog = Join-Path $work 'shaders\spirv'
. (Join-Path $workspace 'Engine-Install.ps1')
$lock = Open-EngineLock $workspace
try {
    if ($Action -eq 'Plan') {
        [ordered]@{ action=$Action; project=$project; tools=$prefix; work=$work; shaders=$catalog; stages=@('check','shaders'); assets=$false; game=$GameExe } | ConvertTo-Json
        return
    }
    $temporary = Join-Path $project 'temp'
    New-Item -ItemType Directory -Path $temporary -Force | Out-Null
    $env:TEMP = $temporary; $env:TMP = $temporary
    if ($Action -eq 'InstallTools') {
        $bootstrap = Join-Path $tools 'micromamba'
        $mamba = Join-Path $bootstrap 'Library\bin\micromamba.exe'
        New-Item -ItemType Directory -Path $bootstrap -Force | Out-Null
        if (-not (Test-Path -LiteralPath $mamba)) {
            $archive = Join-Path $bootstrap 'micromamba.tar.bz2'
            Write-Output 'Downloading micromamba from its official distribution endpoint...'
            & curl.exe -L --fail --retry 2 'https://micro.mamba.pm/api/micromamba/win-64/latest' -o $archive
            if ($LASTEXITCODE -ne 0) { throw 'Micromamba download failed.' }
            & tar.exe -xf $archive -C $bootstrap 'Library/bin/micromamba.exe'
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $mamba)) { throw 'Micromamba extraction failed.' }
        }
        $env:MAMBA_ROOT_PREFIX = Join-Path $tools 'mamba-cache'
        $env:CONDA_PKGS_DIRS = Join-Path $tools 'mamba-cache\pkgs'
        $command = if (Test-Path -LiteralPath (Join-Path $prefix 'conda-meta')) { 'install' } else { 'create' }
        Write-Output "Installing LLVM (AMDGPU) and SPIR-V Tools into $prefix"
        & $mamba --no-rc --root-prefix $env:MAMBA_ROOT_PREFIX $command --prefix $prefix --channel conda-forge --override-channels --yes 'llvm-tools>=21' 'spirv-tools'
        if ($LASTEXITCODE -ne 0) { throw 'Shader tool installation failed. Select Install shader tools to retry.' }
    }
    $env:PATH = "$toolBin;$(Join-Path $prefix 'bin');$env:PATH"
    foreach ($tool in @($llvm,$spirvAs,$spirvVal)) { if (-not (Test-Path -LiteralPath $tool)) { throw 'Shader tools are missing. Select Install shader tools first.' } }
    $version = (& $llvm --version) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $version -notmatch 'amdgcn' -or $version -notmatch '(?im)(?:LLVM version|LLVM)\s+(\d+)\.') { throw 'llvm-mc must support AMDGPU and report its LLVM version.' }
    if ([int]$Matches[1] -lt 21) { throw 'LLVM 21 or newer is required.' }
    & $spirvAs --version
    if ($LASTEXITCODE -ne 0) { throw 'spirv-as cannot run.' }
    & $spirvVal --version
    if ($LASTEXITCODE -ne 0) { throw 'spirv-val cannot run.' }
    if ($Action -eq 'InstallTools') {
        Write-Output 'Shader tools are ready. Select Prepare shaders next.'
        return
    }
    if (-not $GameExe -or -not (Test-Path -LiteralPath $GameExe -PathType Leaf)) { throw 'Choose the game executable before preparing shaders.' }
    $env:LLVM_MC = $llvm
    Write-Output "Shader output: $catalog. Assets will not be exported."
    [string[]]$checkArgs = @()
    if ($Action -eq 'Check') { $checkArgs = @('--check-only') }
    & python.exe -X utf8 -u -I (Join-Path $workspace 'prepare_shaders.py') --source (Join-Path $workspace 'Source') --game (Split-Path -Parent ([IO.Path]::GetFullPath($GameExe))) --project $project --jobs 4 @checkArgs
    if ($LASTEXITCODE -ne 0) { throw "Shader preparation failed (exit $LASTEXITCODE). See the preceding output; completed work is retained for retry." }
    if ($Action -eq 'Prepare') { Write-Output "Shader preparation complete: $catalog" }
} finally { $lock.Dispose() }
