$ErrorActionPreference = 'Stop'
& dotnet publish (Join-Path $PSScriptRoot 'Launcher/LeonidaLauncher.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $PSScriptRoot 'LauncherApp')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
