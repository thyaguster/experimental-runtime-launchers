param([switch]$UpdateLockHeld)
$ErrorActionPreference='Stop'
& (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/Build-RustProject.ps1') -ProjectRoot $PSScriptRoot -UpdateLockHeld:$UpdateLockHeld
