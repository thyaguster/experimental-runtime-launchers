$ErrorActionPreference = 'Stop'
& dotnet publish (Join-Path $PSScriptRoot 'Launcher/XCLLauncher.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $PSScriptRoot 'LauncherApp')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
