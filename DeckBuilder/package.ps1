<#
.SYNOPSIS
    打包可分发版本：dist\WeissDeckBuilder-<版本>.zip

.DESCRIPTION
    把运行所需的文件收进 staging（build\package\DeckBuilder），再压缩成一个 zip。
    压缩包解开后是 DeckBuilder\ 文件夹，整个丢进游戏目录即可，解压即用。
    绝不打包 data-root.txt、build\、tests\ 和日志文件。

.PARAMETER Version
    版本号。默认读 version.txt。

.PARAMETER OutputPath
    输出 zip 路径。默认 dist\WeissDeckBuilder-<版本>.zip。

.PARAMETER NoDocs
    不包含 docs\ 截图（README 里的图片会显示不出来，一般不用）。

.PARAMETER IncludeSource
    额外打包源码与测试（给想自己编译的人）。

.PARAMETER KeepStaging
    保留 build\package 下的 staging 目录，便于检查。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File package.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$OutputPath = '',
    [switch]$NoDocs,
    [switch]$IncludeSource,
    [switch]$KeepStaging
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not $Version) {
    $versionFile = Join-Path $root 'version.txt'
    if (Test-Path -LiteralPath $versionFile) {
        $Version = (Get-Content -LiteralPath $versionFile -TotalCount 1).Trim()
    }
}
if (-not $Version) {
    throw "没有版本号：请在 DeckBuilder\version.txt 里写一行版本号，或用 -Version 1.2.3"
}
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') {
    throw "版本号只能包含字母、数字、点、下划线和连字符：$Version"
}

$distDir = Join-Path $root 'dist'
$stageRoot = Join-Path $root 'build\package'
$stage = Join-Path $stageRoot 'DeckBuilder'
if (-not $OutputPath) {
    $OutputPath = Join-Path $distDir ("WeissDeckBuilder-$Version.zip")
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

Write-Host "版本 : $Version"
Write-Host "输出 : $OutputPath"

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# ---- 必需项 / 可选项 ----
$entries = @(
    @{ Source = (Join-Path $root 'README.md');                              Target = 'README.md';                              Required = $true },
    @{ Source = (Join-Path $root 'THIRD_PARTY_NOTICES.md');                 Target = 'THIRD_PARTY_NOTICES.md';                 Required = $true },
    @{ Source = (Join-Path $root 'install.ps1');                            Target = 'install.ps1';                            Required = $true },
    @{ Source = (Join-Path $root '安装.cmd');                                Target = '安装.cmd';                                Required = $true },
    @{ Source = (Join-Path $distDir 'WeissDeckBuilder.exe');                Target = 'dist\WeissDeckBuilder.exe';              Required = $true },
    @{ Source = (Join-Path $distDir 'SharpCompress.dll');                   Target = 'dist\SharpCompress.dll';                 Required = $true },
    @{ Source = (Join-Path $distDir 'THIRD_PARTY_NOTICES.md');              Target = 'dist\THIRD_PARTY_NOTICES.md';            Required = $false },
    @{ Source = (Join-Path $root 'mod\plugin\WeissSchwarzDeckSync.dll');    Target = 'mod\plugin\WeissSchwarzDeckSync.dll';    Required = $true }
)

$missing = @()
foreach ($entry in $entries) {
    if (-not (Test-Path -LiteralPath $entry.Source)) {
        if ($entry.Required) {
            $missing += $entry.Source
        }
        continue
    }
    $target = Join-Path $stage $entry.Target
    $targetDir = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    }
    Copy-Item -LiteralPath $entry.Source -Destination $target -Force
}

if ($missing.Count -gt 0) {
    throw ("缺少打包必需的文件：`n  " + ($missing -join "`n  ") + "`n" +
        "先运行 build.ps1 和 mod\build.ps1 生成程序与插件。")
}

if (-not $NoDocs) {
    $docsDir = Join-Path $root 'docs'
    if (Test-Path -LiteralPath $docsDir) {
        $docsTarget = Join-Path $stage 'docs'
        New-Item -ItemType Directory -Force -Path $docsTarget | Out-Null
        Get-ChildItem -LiteralPath $docsDir -Filter '*.png' -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $docsTarget -Force
        }
    }
}

if ($IncludeSource) {
    Write-Host "源码 : 一并打包"
    foreach ($relative in @('src', 'tests', 'lib', 'mod\src')) {
        $sourceDir = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $sourceDir)) { continue }
        $targetDir = Join-Path $stage $relative
        robocopy $sourceDir $targetDir /E /XD dist build /XF test-results.txt /NFL /NDL /NJH /NJS /NP | Out-Null
    }
    foreach ($relative in @('build.ps1', 'run-tests.ps1', 'mod\build.ps1', 'mod\README.md')) {
        $sourceFile = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $sourceFile)) { continue }
        $targetFile = Join-Path $stage $relative
        $targetDir = Split-Path -Parent $targetFile
        if (-not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }
        Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force
    }
}

# ---- 压缩 ----
$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}
if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $OutputPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)

# ---- 校验 ----
$archive = [System.IO.Compression.ZipFile]::OpenRead($OutputPath)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
}
finally {
    $archive.Dispose()
}

$forbidden = @('data-root.txt', 'settings.txt', '.bak-', '/build/', 'test-results.txt', '.log')
if (-not $IncludeSource) {
    $forbidden += '/tests/'
}
$bad = @()
foreach ($name in $names) {
    foreach ($pattern in $forbidden) {
        if ($name.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            if ($bad -notcontains $name) { $bad += $name }
            break
        }
    }
}
if ($bad.Count -gt 0) {
    throw ("压缩包里出现了不该分发的文件：`n  " + ($bad -join "`n  "))
}
if (-not ($names -contains 'DeckBuilder/dist/WeissDeckBuilder.exe')) {
    throw "压缩包里缺少 DeckBuilder/dist/WeissDeckBuilder.exe"
}

$info = Get-Item -LiteralPath $OutputPath
Write-Host ("完成 : " + $names.Count + " 个条目，" + [math]::Round($info.Length / 1MB, 2) + " MB")
Write-Host "压缩包内容（前 16 项）："
$names | Select-Object -First 16 | ForEach-Object { Write-Host ("  " + $_) }

if ($KeepStaging) {
    Write-Host ("staging 保留在：" + $stageRoot)
}
else {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}

Write-Host ''
Write-Host "使用方式：解压到游戏目录（和 Weiss Schwarz.exe 同一层），进入 DeckBuilder 双击 安装.cmd，"
Write-Host "或直接运行 DeckBuilder\dist\WeissDeckBuilder.exe。"
