function Enter-NativeBuild {
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio Build Tools with Desktop development with C++ and Windows SDK 10.0.26100+: https://visualstudio.microsoft.com/downloads/' }
    $installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'The Visual Studio C++ workload is missing.' }
    & (Join-Path $installation 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
}
function Find-NativeTool([string]$Name) {
    $found=Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { return $found.Source }
    if ($env:VSINSTALLDIR) {
        $relative=if ($Name -eq 'cmake.exe') { 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' } else { 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe' }
        $candidate=Join-Path $env:VSINSTALLDIR $relative
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw "$Name is missing. Install CMake/Ninja or the Visual Studio C++ CMake tools component, then reopen the launcher."
}
