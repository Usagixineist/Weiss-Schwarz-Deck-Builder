param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    # 只验证改动相关的分组，例如 -Group import 或 -Group sync,cardcode。
    # 留空即跑全部；-List 打印所有分组名。
    [string[]]$Group = @(),
    [switch]$List,
    # 源码未变时默认复用上次的 SmokeTests.exe，-Rebuild 强制重新编译。
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$src = Join-Path $projectRoot 'src'
$testsSrc = Join-Path $projectRoot 'tests'
$buildDir = Join-Path $projectRoot 'tests\dist'
$csc = Join-Path $GameRoot '_tools\roslyn\tasks\net472\csc.exe'
$refRoot = Join-Path $GameRoot '_tools\refasm\build\.NETFramework\v4.7.2'
$sharpCompress = Join-Path $projectRoot 'lib\SharpCompress.dll'
$outExe = Join-Path $buildDir 'SmokeTests.exe'
$rsp = Join-Path $buildDir 'tests.rsp'
$buildLog = Join-Path $buildDir 'tests-build.log'
$resultLog = Join-Path $projectRoot 'tests\test-results.txt'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "Roslyn csc not found: $csc"
}
if (-not (Test-Path -LiteralPath $sharpCompress)) {
    throw "SharpCompress not found: $sharpCompress"
}

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
if (Test-Path -LiteralPath $resultLog) {
    Remove-Item -LiteralPath $resultLog -Force
}

$references = @(
    (Join-Path $refRoot 'mscorlib.dll'),
    (Join-Path $refRoot 'System.dll'),
    (Join-Path $refRoot 'System.Core.dll'),
    (Join-Path $refRoot 'System.Drawing.dll'),
    (Join-Path $refRoot 'System.Windows.Forms.dll'),
    (Join-Path $refRoot 'System.IO.Compression.dll'),
    (Join-Path $refRoot 'System.IO.Compression.FileSystem.dll'),
    (Join-Path $refRoot 'Facades\netstandard.dll'),
    $sharpCompress
)

$sourceFiles = @()
$sourceFiles += Get-ChildItem -LiteralPath (Join-Path $src 'Models') -Filter '*.cs' -File
$sourceFiles += Get-ChildItem -LiteralPath (Join-Path $src 'Data') -Filter '*.cs' -File
$sourceFiles += Get-ChildItem -LiteralPath (Join-Path $src 'Search') -Filter '*.cs' -File
$sourceFiles += Get-Item -LiteralPath (Join-Path $src 'Infrastructure\DataPathResolver.cs')
# The 导入卡组 layer is checked headless: its two-click delete and its Escape
# order must not depend on which child control holds the focus, so the test
# builds the layer itself and only the sources it needs are compiled in.
$sourceFiles += Get-Item -LiteralPath (Join-Path $src 'UI\AppTheme.cs')
$sourceFiles += Get-Item -LiteralPath (Join-Path $src 'UI\ThemeControls.cs')
$sourceFiles += Get-Item -LiteralPath (Join-Path $src 'UI\ThumbnailCache.cs')
$sourceFiles += Get-Item -LiteralPath (Join-Path $src 'UI\DeckImportTilesControl.cs')
$sourceFiles += Get-Item -LiteralPath (Join-Path $src 'UI\DeckImportOverlay.cs')
# The deck-sync plugin's pure helpers live with the plugin so the DeckBuilder
# folder stays self-contained; they are compiled here so they stay tested.
$sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot 'mod\src\DeckSyncPlanner.cs')
$sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot 'mod\src\DeckSyncState.cs')
$sourceFiles += Get-Item -LiteralPath (Join-Path $testsSrc 'SmokeTests.cs')

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('/nologo')
$lines.Add('/target:exe')
$lines.Add('/platform:anycpu')
$lines.Add('/langversion:7.3')
$lines.Add('/optimize+')
$lines.Add('/debug-')
$lines.Add('/deterministic+')
$lines.Add('/utf8output')
# Same binding-redirect noise as build.ps1: Compression 4.0 vs 4.2.
$lines.Add('/nowarn:1701')
$lines.Add('/out:"' + $outExe + '"')
foreach ($reference in $references) {
    $lines.Add('/reference:"' + $reference + '"')
}
$sourceFiles | Sort-Object FullName | ForEach-Object {
    $lines.Add('"' + $_.FullName + '"')
}
[System.IO.File]::WriteAllLines($rsp, $lines, [System.Text.Encoding]::UTF8)

# 增量编译：源码没动就复用上次的 SmokeTests.exe，-Rebuild 才强制重编。
$newestSource = @($sourceFiles) + @(Get-Item -LiteralPath $sharpCompress) |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
$exeExists = Test-Path -LiteralPath $outExe
$needsBuild = $Rebuild -or (-not $exeExists) -or
    ($newestSource -ne $null -and
     $newestSource.LastWriteTimeUtc -gt
     (Get-Item -LiteralPath $outExe).LastWriteTimeUtc)

if ($needsBuild) {
    & $csc "@$rsp" *> $buildLog
    $exitCode = $LASTEXITCODE
    Get-Content -LiteralPath $buildLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "Test compilation failed with exit code $exitCode. See $buildLog"
    }
}
else {
    Write-Host 'test sources unchanged, reusing the built SmokeTests.exe'
}

Copy-Item -LiteralPath $sharpCompress `
    -Destination (Join-Path $buildDir 'SharpCompress.dll') -Force

$testArgs = @('--log', $resultLog)
if ($List) {
    $testArgs += '--list'
}
elseif ($Group.Count -gt 0) {
    $names = $Group |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ }
    if ($names) {
        $testArgs += '--only'
        $testArgs += ($names -join ',')
    }
}

& $outExe @testArgs 2>&1 | Out-Null
$testExit = $LASTEXITCODE
# 只打印一次结果：测试进程的 stdout 和 test-results.txt 内容相同，
# 都打出来会让日志变成两份。
if (Test-Path -LiteralPath $resultLog) {
    Get-Content -LiteralPath $resultLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }
}
else {
    Write-Host "Test run produced no log; exit code $testExit."
}
exit $testExit
