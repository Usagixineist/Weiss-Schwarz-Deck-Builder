<#
.SYNOPSIS
    用官方「Weiß Schwarz」鸢尾花徽标生成程序图标（多尺寸 .ico + 预览图）。

.DESCRIPTION
    ws-emblem.png 是官方媒体包里那张 180×180 的白色底 / 黑色徽标（en.ws-tcg.com 的
    apple-touch-icon）。图标不再直接缩放这张小图，而是先把「黑色像素的覆盖率」转成
    alpha，再按 4 倍超采样重画到 1024px，因此 256 / 128 这些大尺寸边缘依旧干净，
    不会出现把小图放大后的糊边。

    生成的东西：
      WeissDeckBuilder.ico   16/20/24/32/40/48/64/96/128/256 十档，PNG 压缩
      icon-1024.png          1024×1024 母版，方便以后改配色
      icon-preview.png       浅色 / 深色桌面上各尺寸的效果对比图

.PARAMETER Variant
    Dark  = 深蓝底 + 银白徽标（默认，和程序自身的深色界面一致）
    Light = 白底 + 黑徽标（官方 favicon 的原始配色）

.PARAMETER Emblem
    徽标源图。默认用同目录下的 ws-emblem.png。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File DeckBuilder\assets\make-icon.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Dark', 'Light')]
    [string]$Variant = 'Dark',
    [string]$Emblem = '',
    [string]$OutIco = '',
    [string]$OutMaster = '',
    [string]$OutPreview = ''
)

$ErrorActionPreference = 'Stop'

# 双击 / -File 调用时 $PSScriptRoot 指向 assets 目录；从别处 dot-source 时兜底。
$here = $PSScriptRoot
if (-not $here) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if (-not $Emblem) { $Emblem = Join-Path $here 'ws-emblem.png' }
if (-not $OutIco) { $OutIco = Join-Path $here 'WeissDeckBuilder.ico' }
if (-not $OutMaster) { $OutMaster = Join-Path $here 'icon-1024.png' }
if (-not $OutPreview) { $OutPreview = Join-Path $here 'icon-preview.png' }

Add-Type -AssemblyName System.Drawing
if (-not ('WsIcon.Maker' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition (Get-Content -LiteralPath (Join-Path $here 'icon-maker.cs') -Raw)
}

if (-not (Test-Path -LiteralPath $Emblem)) {
    throw "找不到徽标源图：$Emblem"
}

$dark = $Variant -eq 'Dark'

[WsIcon.Maker]::WriteIco($Emblem, $OutIco, $dark)
[WsIcon.Maker]::WriteMaster($Emblem, $OutMaster, $dark)
[WsIcon.Maker]::WritePreview($Emblem, $OutPreview)

Write-Host ('配色     : ' + $Variant)
Write-Host ('图标     : ' + $OutIco + '  ' + (Get-Item -LiteralPath $OutIco).Length + ' bytes')
Write-Host ('母版     : ' + $OutMaster)
Write-Host ('对比图   : ' + $OutPreview)
