param([ValidateSet('inspect','package','smoke')][string]$Action, [string]$GameExe, [Parameter(Mandatory=$true)][string]$SuiteDirectory, [switch]$Warp)
$ErrorActionPreference='Stop'
$workspace=$PSScriptRoot
$reports=Join-Path $workspace 'launcher-data\reports'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$env:TEMP=Join-Path $workspace 'launcher-data\temp'; $env:TMP=$env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
. (Join-Path $workspace 'Engine-Install.ps1')
$lock=Open-EngineLock $workspace
try {
    if ($Action -eq 'smoke') {
        $adapter=if ($Warp) {'warp'} else {'hardware'}
        $tests=@(
            @{Name='Desktop root signature';Exe='d3d12_sdk_check.exe';Arguments=@()},
            @{Name='Heap allocation';Exe='era_heap_check.exe';Arguments=@('allocation')},
            @{Name="Offscreen triangle ($adapter)";Exe='offscreen_check.exe';Arguments=@($adapter,(Join-Path $reports "triangle-$adapter.bmp"))}
        )
        $passed=0; $skipped=0; $failed=0
        foreach ($test in $tests) {
            $binary=Join-Path $SuiteDirectory $test.Exe
            if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw "Tool missing: $binary. Build tools first." }
            Write-Output "Running $($test.Name)..."
            & $binary @($test.Arguments)
            if ($LASTEXITCODE -eq 77) { $skipped++; Write-Output "SKIPPED: $($test.Name) (adapter capability unavailable)." }
            elseif ($LASTEXITCODE -ne 0) { $failed++; Write-Output "FAILED: $($test.Name), exit $LASTEXITCODE" }
            else { $passed++; Write-Output "PASSED: $($test.Name)" }
        }
        Write-Output "Source-built checks: $passed passed, $failed failed, $skipped skipped. No game code was executed."
        if ($failed) { throw 'One or more checks failed. See output above.' }
        return
    }
    if (-not $GameExe -or -not (Test-Path -LiteralPath $GameExe -PathType Leaf)) { throw 'Choose an existing package executable.' }
    $python=Join-Path $workspace 'Source\out\pe-env\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $python)) { throw 'Python tool environment is missing. Build tools first.' }
    $stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    if ($Action -eq 'inspect') {
        $json=Join-Path $reports "imports-$stamp.json"
        $markdown=Join-Path $reports "imports-$stamp.md"
        & $python -X utf8 -B (Join-Path $SuiteDirectory 'tools\compat_report.py') $GameExe --json $json --markdown $markdown
        $code=$LASTEXITCODE
        if ($code -notin @(0,1) -or -not (Test-Path -LiteralPath $json)) { throw "Offline inspection failed (exit $code)." }
        Write-Output "Offline report: $markdown"
        Write-Output 'Import routes are candidates, not proof of runtime compatibility. The EXE was not loaded or executed.'
    } else {
        $json=Join-Path $reports "package-$stamp.json"
        $project=Split-Path -Parent ([string](Get-Item -LiteralPath (Join-Path $workspace 'launcher-data') -Force).Target)
        [string[]]$exclusions=@()
        foreach ($otherProject in @('VIctory','XCL','Leonida','Xbox2Windows')) {
            $link=Join-Path (Split-Path -Parent $workspace) "$otherProject\launcher-data"
            if (Test-Path -LiteralPath $link) {
                $target=(Get-Item -LiteralPath $link -Force).Target
                if ($target) { $exclusions += @('--exclude-directory',(Split-Path -Parent ([string]$target))) }
            }
        }
        & $python -X utf8 -B (Join-Path $workspace 'offline_package_report.py') --suite $SuiteDirectory --game-exe $GameExe --project $project --output $json @exclusions
        if ($LASTEXITCODE -ne 0) { throw 'Offline package import inspection failed.' }
        Write-Output "Offline package report: $json"
        Write-Output 'External host providers remain unverified. No game code was loaded or executed.'
    }
} finally { $lock.Dispose() }
