param(
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$src = Join-Path $projectRoot 'src'
$buildDir = Join-Path $projectRoot 'build'
$distDir = Join-Path $projectRoot 'dist'
$csc = Join-Path $GameRoot '_tools\roslyn\tasks\net472\csc.exe'
$refRoot = Join-Path $GameRoot '_tools\refasm\build\.NETFramework\v4.7.2'
$libDir = Join-Path $projectRoot 'lib'
$sharpCompress = Join-Path $libDir 'SharpCompress.dll'
$thirdPartyNotices = Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'
$outExe = Join-Path $distDir 'WeissDeckBuilder.exe'
$rsp = Join-Path $buildDir 'build.rsp'
$log = Join-Path $buildDir 'build.log'
# 程序图标：assets\make-icon.ps1 用官方鸢尾花徽标生成，既烧进 exe 的资源节
# （资源管理器 / 任务栏 / 快捷方式都读它），也作为托管资源嵌进去供窗口标题栏取值。
$appIcon = Join-Path $projectRoot 'assets\WeissDeckBuilder.ico'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "Roslyn csc not found: $csc"
}
if (-not (Test-Path -LiteralPath $refRoot)) {
    throw ".NET Framework 4.7.2 reference assemblies not found: $refRoot"
}
if (-not (Test-Path -LiteralPath $sharpCompress)) {
    throw "SharpCompress not found: $sharpCompress"
}
if (-not (Test-Path -LiteralPath $thirdPartyNotices)) {
    throw "Third-party notices not found: $thirdPartyNotices"
}

New-Item -ItemType Directory -Force -Path $buildDir, $distDir | Out-Null

# 编译器无法覆盖正在运行的程序：上一次构建会因为文件占用失败，dist 里留着的
# 还是旧程序，界面改动看起来就"没生效"。先点名 PID 说清楚，比直接抛一行
# 文件占用错误更容易看懂。
$running = Get-Process -Name 'WeissDeckBuilder' -ErrorAction SilentlyContinue
if ($running) {
    $ids = ($running | ForEach-Object { $_.Id }) -join ', '
    throw "Deck Builder 正在运行（PID $ids），请先关闭程序再构建：$outExe"
}

$references = @(
    (Join-Path $refRoot 'mscorlib.dll'),
    (Join-Path $refRoot 'System.dll'),
    (Join-Path $refRoot 'System.Core.dll'),
    (Join-Path $refRoot 'System.Drawing.dll'),
    (Join-Path $refRoot 'System.Windows.Forms.dll'),
    # 卡表压缩包导入需要 ZipArchive / ZipFile。
    (Join-Path $refRoot 'System.IO.Compression.dll'),
    (Join-Path $refRoot 'System.IO.Compression.FileSystem.dll'),
    # RAR / 7z 解包由随程序分发的 SharpCompress 负责。
    $sharpCompress,
    (Join-Path $refRoot 'Facades\netstandard.dll')
)
$accessibility = Join-Path $refRoot 'Accessibility.dll'
if (Test-Path -LiteralPath $accessibility) {
    $references += $accessibility
}
$systemConfiguration = Join-Path $refRoot 'System.Configuration.dll'
if (Test-Path -LiteralPath $systemConfiguration) {
    $references += $systemConfiguration
}

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Missing reference: $reference"
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('/nologo')
$lines.Add('/target:winexe')
$lines.Add('/platform:anycpu')
$lines.Add('/langversion:7.3')
$lines.Add('/optimize+')
$lines.Add('/debug-')
$lines.Add('/deterministic+')
$lines.Add('/utf8output')
# System.IO.Compression.FileSystem 引用的是 Compression 4.0，而引用程序集目录里是
# 4.2；运行时统一走 GAC 里的同一份实现，这条绑定重定向提示没有实际意义。
$lines.Add('/nowarn:1701')
$lines.Add('/out:"' + $outExe + '"')
if (Test-Path -LiteralPath $appIcon) {
    $lines.Add('/win32icon:"' + $appIcon + '"')
    # 同一个 ico 再嵌一份托管资源，AppIcon 读它来设置窗口图标，不必依赖 dist 里多一个文件。
    $lines.Add('/resource:"' + $appIcon + '",WeissDeckBuilder.ico')
}
else {
    Write-Host ('[警告] 没有程序图标，exe 会用系统默认图标：' + $appIcon) -ForegroundColor Yellow
    Write-Host '       运行 assets\make-icon.ps1 重新生成。' -ForegroundColor Yellow
}
foreach ($reference in $references) {
    $lines.Add('/reference:"' + $reference + '"')
}
Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' -File |
    Sort-Object FullName |
    ForEach-Object {
        $lines.Add('"' + $_.FullName + '"')
    }
[System.IO.File]::WriteAllLines($rsp, $lines, [System.Text.Encoding]::UTF8)

& $csc "@$rsp" *> $log
$exitCode = $LASTEXITCODE
Get-Content -LiteralPath $log -Encoding UTF8 | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0) {
    throw "Compilation failed with exit code $exitCode. See $log"
}
if (-not (Test-Path -LiteralPath $outExe)) {
    throw "Compilation reported success but output is missing: $outExe"
}

Copy-Item -LiteralPath $sharpCompress `
    -Destination (Join-Path $distDir 'SharpCompress.dll') -Force
Copy-Item -LiteralPath $thirdPartyNotices `
    -Destination (Join-Path $distDir 'THIRD_PARTY_NOTICES.md') -Force

Write-Host "Built: $outExe"
if ($Run) {
    Start-Process -FilePath $outExe -WorkingDirectory $distDir
}
