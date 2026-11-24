param([string]$OutputRoot=(Join-Path $PSScriptRoot 'artifacts'),[string]$Version='0.1.0-alpha.2',[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+-(alpha|beta|rc)\.\d+$') { throw 'Use an experimental prerelease version such as 0.1.0-alpha.2.' }
$root=$PSScriptRoot
$files=@(& (Join-Path $root 'tools\Validate-Manifest.ps1') -Root $root)
foreach ($file in $files | Where-Object { $_ -like '*.ps1' }) {
    $tokens=$null; $errors=$null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $root $file),[ref]$tokens,[ref]$errors) | Out-Null
    if ($errors.Count) { throw "PowerShell syntax error in $file : $($errors[0].Message)" }
}
$sdk=& $Dotnet --version
if ($LASTEXITCODE -ne 0 -or [version]($sdk -replace '-.*$','') -lt [version]'10.0.100') { throw '.NET 10 SDK is required.' }
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$stage=Join-Path $OutputRoot ('stage-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($relative in $files) {
    $destination=Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $destination
}
foreach ($name in @('VIctory','XCL','Leonida','Xbox2Windows')) {
    $project=Get-ChildItem -LiteralPath (Join-Path $root "$name\Launcher") -Filter '*.csproj' | Select-Object -First 1 -ExpandProperty FullName
    $publish=Join-Path $OutputRoot ('publish-'+$name+'-'+[guid]::NewGuid().ToString('N'))
    & $Dotnet publish $project -c Release -r win-x64 --self-contained false -p:Version=$Version -p:DebugType=None -o $publish
    if ($LASTEXITCODE -ne 0) { throw "$name launcher build failed." }
    $destination=Join-Path $stage "$name\LauncherApp"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($suffix in @('.exe','.dll','.deps.json','.runtimeconfig.json')) {
        $file="$name Launcher$suffix"
        if (-not (Test-Path -LiteralPath (Join-Path $publish $file))) { throw "Expected launcher artifact missing: $file" }
        Copy-Item -LiteralPath (Join-Path $publish $file) -Destination $destination
    }
}
& (Join-Path $root 'tests\Test-Package.ps1') -Stage $stage -TemporaryRoot $OutputRoot
$zip=Join-Path $OutputRoot "experimental-runtime-launchers-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { throw 'Release ZIP already exists; choose a fresh output folder.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ASCII
Write-Output "Release ZIP: $zip"
Write-Output "SHA256: $hash"
Write-Output 'Local package verified. Nothing was uploaded or published.'
