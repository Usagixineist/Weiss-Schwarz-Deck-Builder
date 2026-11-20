param(
    # 连续跑几次打开系列，取每次的分段耗时，噪声大时看最小值。
    [int]$Runs = 3
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot

for ($run = 1; $run -le $Runs; $run++) {
    $scriptPath = Join-Path $scriptDir 'profile-load.ps1'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $scriptPath + '"'
    $p = Start-Process powershell -ArgumentList $arguments `
        -RedirectStandardOutput (Join-Path $scriptDir 'profile-run.txt') `
        -RedirectStandardError (Join-Path $scriptDir 'profile-run-err.txt') `
        -PassThru -WindowStyle Hidden
    $null = $p.WaitForExit(180000)
    if (-not $p.HasExited) {
        $p.Kill()
        Write-Host "run ${run}: TIMEOUT"
        continue
    }

    $log = Join-Path $env:TEMP 'wsdeck-load.log'
    if (-not (Test-Path -LiteralPath $log)) {
        Write-Host "run ${run}: no log"
        continue
    }

    $parseRead = 0.0
    $firstOpen = $null
    $secondOpen = $null
    $seen = 0
    foreach ($line in (Get-Content -LiteralPath $log -Encoding UTF8)) {
        if ($line -match '\[ParseFile\] read [^:]+: ([0-9.]+) ms') {
            $parseRead += [double]$matches[1]
        }
        elseif ($line -match '\[OpenSeries\] handed to the grid') {
            if ($line -match ': ([0-9.]+) ms') {
                if ($seen -eq 0) { $firstOpen = [double]$matches[1] }
                elseif ($seen -eq 1) { $secondOpen = [double]$matches[1] }
                $seen++
            }
        }
    }

    Write-Host ("run {0}: parse-read {1:0.0} ms, first open {2} ms, second open {3} ms" -f `
        $run, $parseRead, $firstOpen, $secondOpen)
}
