param([Parameter(Mandatory=$true)][string]$ProjectRoot)
$ErrorActionPreference='Stop'
$name=Split-Path -Leaf $ProjectRoot
$missing=@()
foreach ($tool in @('git.exe','python.exe')) { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { $missing+=$tool } }
if ($name -in @('VIctory','XCL','Leonida')) { foreach ($tool in @('cargo.exe','rustup.exe')) { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { $missing+=$tool } } }
. (Join-Path $PSScriptRoot 'BuildEnvironment.ps1')
try { Enter-NativeBuild } catch { $missing+=$_.Exception.Message }
if ($name -in @('Leonida','Xbox2Windows')) { foreach ($tool in @('cmake.exe','ninja.exe')) { try { Find-NativeTool $tool | Out-Null } catch { $missing+=$_.Exception.Message } } }
if ($missing.Count) {
    foreach ($item in $missing) { Write-Output "MISSING: $item" }
    Write-Output 'Official setup links: https://git-scm.com/downloads/win | https://www.python.org/downloads/ | https://rustup.rs/ | https://visualstudio.microsoft.com/downloads/ | https://cmake.org/download/'
    throw 'Install the missing prerequisites, then reopen the launcher and check again.'
}
Write-Output 'Required build commands were found. Individual upstream builds perform additional version and SDK checks.'
