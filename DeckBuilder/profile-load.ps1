param(
    # 想量的系列名，默认挑最大的 BanG Dream!（15 包 / 2518 张）。
    [string]$Series = 'BanG Dream!',
    # 第二个系列，用来看“换一个没打开过的系列”要多久。
    [string]$SecondSeries = '加速世界',
    [string]$GameRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$exe = Join-Path $PSScriptRoot 'dist\WeissDeckBuilder.exe'
$dataRoot = Join-Path $GameRoot 'Weiss Schwarz_Data\StreamingAssets'
if (-not (Test-Path -LiteralPath (Join-Path $dataRoot 'Cards'))) {
    throw "Cards folder not found below $dataRoot"
}
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build the app first: $exe"
}

# 计时只写日志、不改行为；打开开关后所有分段都落进 %TEMP%\wsdeck-load.log。
$env:WSDECK_PROFILE = '1'

$asm = [System.Reflection.Assembly]::LoadFrom($exe)
$flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

$profilerType = $asm.GetType('WeissDeckBuilder.Infrastructure.LoadProfiler')
$initArgs = New-Object 'object[]' 1
$profilerType.GetMethod('Initialize').Invoke($null, $initArgs)

$dbType = $asm.GetType('WeissDeckBuilder.Data.CardDatabase')
$loadArgs = New-Object 'object[]' 1
$loadArgs[0] = [string]$dataRoot
$database = $dbType.GetMethod('Load').Invoke($null, $loadArgs)

$formType = $asm.GetType('WeissDeckBuilder.UI.MainForm')
$form = [System.Activator]::CreateInstance($formType, (, $database))

$showList = $formType.GetMethod('ShowSeriesList',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$open = $formType.GetMethod('OpenSeries',
    [System.Reflection.BindingFlags]'Instance,NonPublic')

function Open-Series($name) {
    $metadataArgs = New-Object 'object[]' 1
    $metadataArgs[0] = [string]$name
    $metadata = $dbType.GetMethod('GetSeriesMetadata').Invoke($database, $metadataArgs)
    if ($metadata -eq $null) {
        Write-Host "series not found: $name"
        return
    }
    $openArgs = New-Object 'object[]' 1
    $openArgs[0] = $metadata
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $open.Invoke($form, $openArgs)
    $watch.Stop()
    Write-Host ("open {0}: UI thread blocked {1} ms" -f $name, $watch.ElapsedMilliseconds)
}

function Pump-Messages {
    for ($i = 0; $i -lt 8; $i++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 20
    }
}

$form.Add_Shown({
    # 1) 第一次打开：卡表、滤镜、首帧三样都是冷的。
    Open-Series $Series
    Pump-Messages
    $showList.Invoke($form, $null)
    Pump-Messages
    # 2) 再开一次同一个系列：卡表已缓存，只剩界面重排。
    Open-Series $Series
    Pump-Messages
    $showList.Invoke($form, $null)
    Pump-Messages
    # 3) 换一个系列：卡表是冷的，但界面已经排过一次。
    Open-Series $SecondSeries
    Pump-Messages
    $form.Close()
})

[System.Windows.Forms.Application]::Run($form)

$form.Dispose()

$logPath = $profilerType.GetProperty('LogPath').GetValue($null)
Write-Host "profile log: $logPath"
Get-Content -LiteralPath $logPath -Encoding UTF8
