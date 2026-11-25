<#
.SYNOPSIS
    把游戏目录里正在开发的内容同步到本仓库。

.DESCRIPTION
    日常开发和构建都在游戏目录（例如 E:\Weiss Schwarz CH-0.6）里进行，这个仓库只是它的
    一份可发布副本。跑完本脚本后 git status 就能看到这次改了什么。

    复制是单向的（游戏目录 -> 仓库），且只复制源码、脚本、配置需要的东西：
    构建产物（build、dist、tests\dist、mod\plugin）与本机路径文件不复制。

.PARAMETER GameRoot
    游戏目录（含 Weiss Schwarz.exe）。默认取本仓库的上一级目录。

.PARAMETER DryRun
    只打印 robocopy 计划，不实际复制。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\sync-from-game.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$GameRoot = '',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $GameRoot) {
    $GameRoot = Split-Path -Parent $repoRoot
}
if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'Weiss Schwarz.exe'))) {
    throw ("不是游戏目录（缺少 Weiss Schwarz.exe）：" + $GameRoot + "`n用 -GameRoot 指定游戏目录。")
}
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).ProviderPath

$flags = @('/E', '/XJ', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
if ($DryRun) {
    $flags += '/L'
}
$flags += @('/XD', 'build', 'dist', 'plugin', 'dumps', 'extra',
    '/XF', 'test-results.txt', 'deck-sync-state.txt', 'extras.log')

function Sync-Tree([string]$source, [string]$target) {
    Write-Host ("同步 " + $source + "  ->  " + $target)
    if ($DryRun) {
        robocopy $source $target @flags
    }
    else {
        robocopy $source $target @flags | Out-Null
    }
    if ($LASTEXITCODE -ge 8) {
        throw ("robocopy 失败：" + $source + "（exit " + $LASTEXITCODE + "）")
    }
}

Sync-Tree (Join-Path $GameRoot 'DeckBuilder') (Join-Path $repoRoot 'DeckBuilder')
Sync-Tree (Join-Path $GameRoot 'BepInEx\plugins') (Join-Path $repoRoot 'BepInEx\plugins')
Sync-Tree (Join-Path $GameRoot 'BepInEx\config') (Join-Path $repoRoot 'BepInEx\config')

Write-Host ''
Write-Host "完成。检查改动：" -ForegroundColor Green
Write-Host ('  git -C "' + $repoRoot + '" status --short')
