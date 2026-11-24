param([Parameter(Mandatory=$true)][string]$Stage,[Parameter(Mandatory=$true)][string]$Fixture)
$ErrorActionPreference='Stop'
. (Join-Path $Stage 'Leonida\Integrated-Backend.ps1')
$workspace=Join-Path $Fixture 'backend-manifests'
New-Item -ItemType Directory -Path (Join-Path $workspace 'Engine\versions\one'),(Join-Path $workspace 'Engine\versions\two') -Force | Out-Null
$native=Join-Path $workspace 'Engine\current.json'
'{"commit":"native-marker","executable":"native.exe"}' | Set-Content -LiteralPath $native
$before=(Get-FileHash -LiteralPath $native).Hash
foreach ($name in @('one','two')) {
    'synthetic-not-executable' | Set-Content -LiteralPath (Join-Path $workspace "Engine\versions\$name\victory.exe")
    Write-IntegratedManifest $workspace @{commit=$name;executable="versions\$name\victory.exe"}
}
if ((Read-IntegratedManifest $workspace).commit -ne 'two') { throw 'Wrong integrated current version.' }
Restore-IntegratedEngine $workspace
if ((Read-IntegratedManifest $workspace).commit -ne 'one') { throw 'Integrated rollback failed.' }
if ((Read-IntegratedManifest $workspace 'previous.json').commit -ne 'two') { throw 'Integrated rollback lost its previous version.' }
if ((Get-FileHash -LiteralPath $native).Hash -ne $before) { throw 'Integrated install changed the native manifest.' }
Write-IntegratedManifest $workspace @{commit='bad';executable='..\..\outside.exe'}
# The valid previous version is retained by the atomic writer.
$bad=Join-Path $workspace 'Engine\integrated-previous.json'
'{"commit":"bad","executable":"..\\..\\outside.exe"}' | Set-Content -LiteralPath $bad
$refused=$false
try { Restore-IntegratedEngine $workspace } catch { $refused=$true }
if (-not $refused) { throw 'Rollback accepted an escaping executable path.' }
Write-Output 'Leonida: independent backend manifests, rollback and invalid-path refusal: PASS'
