<#
.SYNOPSIS
    把 Weiss Schwarz Deck Builder 装到游戏目录里（可选同时安装卡组同步插件）。

.DESCRIPTION
    脚本只做三件事：
      1. 找到游戏目录（含 "Weiss Schwarz.exe" 的那一层）；
      2. 检查程序文件和卡表数据是否就位；
      3. 把卡组同步插件 WeissSchwarzDeckSync.dll 复制到 BepInEx\plugins。
    不会改动 StreamingAssets 里的卡表/卡组/收藏，也不会改游戏本体文件。

.PARAMETER GameRoot
    游戏目录。默认从本脚本所在目录逐级向上查找 "Weiss Schwarz.exe"。

.PARAMETER NoPlugin
    只检查程序，不安装 BepInEx 插件。

.PARAMETER WriteDataRoot
    当 DeckBuilder 不在游戏目录里时，把卡表路径写进 dist\data-root.txt。
    注意：那里保存的是本机绝对路径，换到别的电脑就失效，所以默认不写。

.PARAMETER Preview
    只打印将要做什么，不复制、不写入任何文件。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -Preview
#>
[CmdletBinding()]
param(
    [string]$GameRoot = '',
    [switch]$NoPlugin,
    [switch]$WriteDataRoot,
    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
$installRoot = $PSScriptRoot
$exeName = 'Weiss Schwarz.exe'
$pluginName = 'WeissSchwarzDeckSync'

function Find-GameRootFrom([string]$start) {
    $directory = $start
    for ($level = 0; $level -lt 6 -and -not [string]::IsNullOrEmpty($directory); $level++) {
        if (Test-Path -LiteralPath (Join-Path $directory $exeName)) {
            return $directory
        }
        $parent = Split-Path -Parent $directory
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $directory) {
            break
        }
        $directory = $parent
    }
    return ''
}

if ($GameRoot) {
    if (-not (Test-Path -LiteralPath $GameRoot)) {
        throw "游戏目录不存在：$GameRoot"
    }
    $GameRoot = (Resolve-Path -LiteralPath $GameRoot).ProviderPath
}
else {
    $GameRoot = Find-GameRootFrom $installRoot
    if (-not $GameRoot) {
        throw ("找不到游戏目录：从 " + $installRoot + " 向上没有找到 " + $exeName + "。`n" +
            "请把整个 DeckBuilder 文件夹解压到游戏目录（和 " + $exeName + " 同一层）后重试，`n" +
            "或用 -GameRoot 指定游戏目录：`n" +
            "  powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -GameRoot `"D:\Steam\steamapps\common\Weiss Schwarz`"")
    }
}

Write-Host "游戏目录 : $GameRoot"
Write-Host "程序目录 : $installRoot"
if ($Preview) {
    Write-Host "模式     : Preview（不写入任何文件）" -ForegroundColor Yellow
}

$exePath = Join-Path $installRoot 'dist\WeissDeckBuilder.exe'
$libPath = Join-Path $installRoot 'dist\SharpCompress.dll'
$streamingAssets = Join-Path $GameRoot 'Weiss Schwarz_Data\StreamingAssets'
$cardsPath = Join-Path $streamingAssets 'Cards'

$missing = @()
foreach ($file in @($exePath, $libPath)) {
    if (-not (Test-Path -LiteralPath $file)) {
        $missing += $file
    }
}
if ($missing.Count -gt 0) {
    throw ("缺少程序文件：`n  " + ($missing -join "`n  ") + "`n" +
        "请先运行 build.ps1 构建，或使用完整的发布包（release zip）。")
}

if (-not (Test-Path -LiteralPath $cardsPath)) {
    Write-Warning ("没有找到卡表目录：" + $cardsPath + "`n" +
        "程序启动后会提示找不到数据，请确认游戏数据完整。")
}

$bepInExDll = Join-Path $GameRoot 'BepInEx\core\BepInEx.dll'
if ((-not $NoPlugin) -and (-not (Test-Path -LiteralPath $bepInExDll))) {
    Write-Warning ("没有检测到 BepInEx：" + $bepInExDll + "`n" +
        "卡组同步插件运行需要 BepInEx 5.4.23.x；只想用独立程序可以加 -NoPlugin。")
}

# ---- 卡组同步插件 ----
if ($NoPlugin) {
    Write-Host "插件     : 跳过（-NoPlugin）" -ForegroundColor DarkGray
}
else {
    $pluginSrc = Join-Path $installRoot ('mod\plugin\' + $pluginName + '.dll')
    if (-not (Test-Path -LiteralPath $pluginSrc)) {
        $pluginSrc = Join-Path $installRoot ('dist\' + $pluginName + '.dll')
    }
    $pluginDir = Join-Path $GameRoot ('BepInEx\plugins\' + $pluginName)
    $pluginDst = Join-Path $pluginDir ($pluginName + '.dll')

    if (-not (Test-Path -LiteralPath $pluginSrc)) {
        Write-Warning ("发布包里没有插件文件 " + $pluginName + ".dll，跳过插件安装。")
    }
    else {
        $needCopy = $true
        if (Test-Path -LiteralPath $pluginDst) {
            $srcHash = (Get-FileHash -LiteralPath $pluginSrc -Algorithm SHA256).Hash
            $dstHash = (Get-FileHash -LiteralPath $pluginDst -Algorithm SHA256).Hash
            $needCopy = ($srcHash -ne $dstHash)
        }

        if (-not $needCopy) {
            Write-Host "插件     : 已是最新 $pluginDst" -ForegroundColor Green
        }
        elseif ($Preview) {
            Write-Host "[Preview] 复制 $pluginSrc -> $pluginDst"
        }
        else {
            New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
            if (Test-Path -LiteralPath $pluginDst) {
                $backup = $pluginDst + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
                Copy-Item -LiteralPath $pluginDst -Destination $backup -Force
                Write-Host "旧插件已备份 : $backup" -ForegroundColor DarkGray
            }
            try {
                Copy-Item -LiteralPath $pluginSrc -Destination $pluginDst -Force
            }
            catch {
                throw ("复制插件失败（游戏可能正在运行，请先关闭游戏）：" + $_.Exception.Message)
            }
            Write-Host "插件     : 已安装 $pluginDst" -ForegroundColor Green
        }
    }
}

# ---- 数据路径：只有 DeckBuilder 不在游戏目录里时才需要考虑 ----
$underGameRoot = $installRoot.StartsWith($GameRoot, [System.StringComparison]::OrdinalIgnoreCase)
if (-not $underGameRoot) {
    Write-Warning ("DeckBuilder 不在游戏目录里：" + $installRoot + "`n" +
        "建议把它放到 " + (Join-Path $GameRoot 'DeckBuilder') + "，程序会自动找到卡表。`n" +
        "如果一定要留在当前位置，可以运行时加 --data `"" + $streamingAssets + "`" 或加 -WriteDataRoot。")
    if ($WriteDataRoot) {
        $dataRootFile = Join-Path $installRoot 'dist\data-root.txt'
        if ($Preview) {
            Write-Host "[Preview] 写入 $dataRootFile : $streamingAssets"
        }
        else {
            [System.IO.File]::WriteAllLines($dataRootFile, @($streamingAssets),
                (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "数据路径 : 已写入 $dataRootFile" -ForegroundColor Yellow
            Write-Host "           该文件是绝对路径，换电脑后需要删除或重写。" -ForegroundColor Yellow
        }
    }
}

Write-Host ''
Write-Host "完成。" -ForegroundColor Green
Write-Host "启动程序 : $exePath"
if (-not $NoPlugin) {
    Write-Host ("插件配置 : " + (Join-Path $GameRoot 'BepInEx\config\ws.deckbuilder.decksync.cfg'))
}
Write-Host "卡表数据 : $streamingAssets"
