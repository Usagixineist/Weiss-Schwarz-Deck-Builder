<#
.SYNOPSIS
    一键把游戏目录里的 DeckBuilder 与 BepInEx 插件同步到 _repo 仓库。

.DESCRIPTION
    日常开发都在游戏目录（含 "Weiss Schwarz.exe" 的那一层）里进行，仓库 <游戏目录>\_repo
    只是它的一份可发布副本。双击 同步到仓库.cmd 时执行本脚本，它做四件事：

      1. 定位游戏目录与仓库目录（都可用参数覆盖）；
      2. 调用仓库里的 tools\sync-from-game.ps1 同步（该脚本不存在时用相同的 robocopy 规则兜底）；
      3. 打印 git status，并把不该进仓库的构建产物/日志单独挑出来警告；
      4. 可选 -Stage / -Commit / -Push，一条命令直接提交并推送，不必再打开 GitHub Desktop。

.PARAMETER GameRoot
    游戏目录。默认从本脚本所在目录向上最多 6 层找 "Weiss Schwarz.exe"。

.PARAMETER RepoRoot
    仓库目录。默认取 <游戏目录>\_repo；从仓库内部（被同步进 _repo 之后）运行时自动认上一级。

.PARAMETER DryRun
    只打印会复制什么，不复制、不提交。

.PARAMETER Stage
    同步后执行 git add -A（只暂存，不提交）。

.PARAMETER Commit
    同步后直接提交；提交信息取 -Message，不写则自动用 "chore: 同步 <时间>"。

.PARAMETER Message
    提交信息，配合 -Commit 使用。

.PARAMETER Push
    推送。配合 -Commit 是"提交完就推"；单独用时推已有的本地提交。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File DeckBuilder\sync-to-repo.ps1 -DryRun

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File DeckBuilder\sync-to-repo.ps1 -Commit -Push -Message "fix: 卡组导出乱码"
#>
[CmdletBinding()]
param(
    [string]$GameRoot = '',
    [string]$RepoRoot = '',
    [switch]$DryRun,
    [switch]$Stage,
    [switch]$Commit,
    [string]$Message = '',
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$deckRoot = $PSScriptRoot
$exeName = 'Weiss Schwarz.exe'
$repoSyncName = 'tools\sync-from-game.ps1'

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

# git 输出文件名用的是 UTF-8，而 Windows PowerShell 5.1 默认按控制台代码页（简体中文是 GBK）
# 解码子进程输出，中文路径会直接变成乱码。读输出时临时切到 UTF-8，读完还原。
function Invoke-GitText {
    param(
        [string]$Repository,
        [string[]]$GitArgs
    )
    $previous = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        $output = @(& git -C $Repository @GitArgs)
        $script:LastGitExit = $LASTEXITCODE
        return $output
    }
    finally {
        [Console]::OutputEncoding = $previous
    }
}


# ---- 定位游戏目录 ----
if ($GameRoot) {
    if (-not (Test-Path -LiteralPath $GameRoot)) {
        throw "游戏目录不存在：$GameRoot"
    }
    $GameRoot = (Resolve-Path -LiteralPath $GameRoot).ProviderPath
}
else {
    $GameRoot = Find-GameRootFrom $deckRoot
}
if (-not $GameRoot) {
    throw ("找不到游戏目录（缺 " + $exeName + "）：" + $deckRoot + "`n用 -GameRoot 指定游戏目录。")
}

# ---- 定位仓库目录：优先"脚本上一级就是仓库"，否则 <游戏目录>\_repo ----
if ($RepoRoot) {
    if (-not (Test-Path -LiteralPath $RepoRoot)) {
        throw "仓库目录不存在：$RepoRoot"
    }
    $RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).ProviderPath
}
else {
    $parent = Split-Path -Parent $deckRoot
    if ($parent -and (Test-Path -LiteralPath (Join-Path $parent $repoSyncName))) {
        $RepoRoot = $parent
    }
    else {
        $RepoRoot = Join-Path $GameRoot '_repo'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot '.git'))) {
    throw ("不是仓库目录（缺 .git）：" + $RepoRoot + "`n默认应位于 <游戏目录>\_repo，用 -RepoRoot 指定。")
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).ProviderPath
if ($RepoRoot -eq $GameRoot) {
    throw "仓库目录不能等于游戏目录：$RepoRoot"
}

Write-Host ('游戏目录 : ' + $GameRoot)
Write-Host ('仓库目录 : ' + $RepoRoot)
if ($DryRun) {
    Write-Host '模式     : 干跑（只打印，不复制、不提交）' -ForegroundColor Yellow
}
Write-Host ''

# ---- 1. 同步：优先复用仓库里的 tools\sync-from-game.ps1，避免两处规则不一致 ----
$repoSync = Join-Path $RepoRoot $repoSyncName
if (Test-Path -LiteralPath $repoSync) {
    $syncArgs = @{ GameRoot = $GameRoot }
    if ($DryRun) { $syncArgs.DryRun = $true }
    & $repoSync @syncArgs
}
else {
    Write-Host ('仓库里没有 ' + $repoSyncName + '，改用内置 robocopy 规则同步。') -ForegroundColor Yellow
    $flags = @('/E', '/XJ', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    if ($DryRun) { $flags += '/L' }
    $flags += @('/XD', 'build', 'dist', 'plugin', 'dumps', '/XF', 'test-results.txt', 'deck-sync-state.txt')
    foreach ($relative in @('DeckBuilder', 'BepInEx\plugins', 'BepInEx\config')) {
        $source = Join-Path $GameRoot $relative
        $target = Join-Path $RepoRoot $relative
        if (-not (Test-Path -LiteralPath $source)) { continue }
        Write-Host ('同步 ' + $source + '  ->  ' + $target)
        robocopy $source $target @flags | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw ('robocopy 失败：' + $source + '（exit ' + $LASTEXITCODE + '）')
        }
    }
}

if ($DryRun) {
    Write-Host ''
    Write-Host '干跑结束：没有复制任何文件，git 索引也没有改动。' -ForegroundColor Yellow
    return
}

# ---- 2. 看仓库状态（core.quotepath=false：中文路径原样显示，不转成 \346\226\207） ----
$changed = @(Invoke-GitText $RepoRoot @('-c', 'core.quotepath=false', 'status', '--short'))
$changed = @($changed | Where-Object { $_ -ne '' })
Write-Host ''
if ($changed.Count -eq 0) {
    Write-Host '仓库没有改动：游戏目录里的内容已经同步过了。' -ForegroundColor Green
    return
}
Write-Host ('仓库改动 ' + $changed.Count + ' 项：') -ForegroundColor Cyan
$changed | ForEach-Object { Write-Host ('  ' + $_) }

# 这些正常会被 .gitignore 挡住，出现即说明规则被删过或文件被强加过
$suspectPatterns = @('dist/', 'build/', 'tests/dist/', 'mod/plugin/', '.log', 'data-root.txt',
                     'test-results.txt', '.bak-', 'BepInEx/cache/', 'BepInEx/core/')
$suspects = @()
foreach ($line in $changed) {
    $path = $line.Substring(3).Trim('"').Replace('\', '/')
    foreach ($pattern in $suspectPatterns) {
        if ($path.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $suspects += $line
            break
        }
    }
}
if ($suspects.Count -gt 0) {
    Write-Host ''
    Write-Host '警告：下面这些是构建产物 / 日志 / 本机路径文件，不该进仓库：' -ForegroundColor Red
    $suspects | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor Red }
    Write-Host ('处理：git -C "' + $RepoRoot + '" rm --cached -r <路径>，再检查 .gitignore 是否还在。') -ForegroundColor Red
    Write-Host '直接提交会把构建垃圾重新塞回仓库（之前已经清过一次）。' -ForegroundColor Red
}

# ---- 3. 可选：暂存 / 提交 / 推送 ----
if ($Stage -or $Commit) {
    & git -C $RepoRoot add -A
    if ($LASTEXITCODE -ne 0) {
        throw "git add 失败（exit $LASTEXITCODE）"
    }
    Write-Host ''
    Write-Host '已暂存全部改动（git add -A）。' -ForegroundColor Cyan
}

if ($Commit) {
    if (-not $Message) {
        $Message = 'chore: 同步游戏目录 -> 仓库（' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + '）'
    }
    & git -C $RepoRoot commit -m $Message
    if ($LASTEXITCODE -ne 0) {
        throw "git commit 失败（exit $LASTEXITCODE）"
    }
    Write-Host ('已提交：' + $Message) -ForegroundColor Green
}

if ($Push) {
    # 凭据缺失时直接报错，不卡在终端等输入（GitHub Desktop 登录过就已有凭据）
    $env:GIT_TERMINAL_PROMPT = '0'
    & git -C $RepoRoot push
    if ($LASTEXITCODE -ne 0) {
        throw "git push 失败（exit $LASTEXITCODE）：先在 GitHub Desktop 里确认已登录，或检查网络/代理"
    }
    Write-Host '已推送到 GitHub。' -ForegroundColor Green
}

Write-Host ''
if (-not ($Commit -or $Push)) {
    Write-Host '下一步（任选一种）：' -ForegroundColor Cyan
    Write-Host '  A. GitHub Desktop：左侧仓库选 _repo → Changes → 填 Summary (required) → Commit to main → 右上角 Push origin'
    Write-Host ('  B. 命令行      ：git -C "' + $RepoRoot + '" add -A; git -C "' + $RepoRoot + '" commit -m "fix: ..."; git -C "' + $RepoRoot + '" push')
    Write-Host '  C. 一条命令    ：同步到仓库.cmd -Commit -Push -Message "fix: ..."'
    Write-Host ''
}
Write-Host ('完成。仓库：' + $RepoRoot) -ForegroundColor Green
$branchLine = @(Invoke-GitText $RepoRoot @('status', '-sb')) | Select-Object -First 1
Write-Host ('状态：' + $branchLine)


