param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [switch]$NoInstall
)

# Builds the DeckBuilder deck-sync BepInEx plugin. Everything this project needs
# lives below DeckBuilder\mod: the sources, the built DLL (mod\plugin) and the
# optional install step that copies that DLL into BepInEx\plugins. Nothing else
# in the repository is touched, so the DeckBuilder folder can be packed on its own.

$ErrorActionPreference = 'Stop'
$modRoot = $PSScriptRoot
$src = Join-Path $modRoot 'src'
$pluginDir = Join-Path $modRoot 'plugin'
$buildDir = Join-Path $modRoot 'build'
$csc = Join-Path $GameRoot '_tools\roslyn\tasks\net472\csc.exe'
$refRoot = Join-Path $GameRoot '_tools\refasm\build\.NETFramework\v4.7.2'
$managed = Join-Path $GameRoot 'Weiss Schwarz_Data\Managed'
$bepInExCore = Join-Path $GameRoot 'BepInEx\core'
$outDll = Join-Path $pluginDir 'WeissSchwarzDeckSync.dll'
$rsp = Join-Path $buildDir 'build.rsp'
$log = Join-Path $buildDir 'build.log'

if (-not (Test-Path -LiteralPath $csc)) { throw "Roslyn csc not found: $csc" }
if (-not (Test-Path -LiteralPath $refRoot)) { throw ".NET 4.7.2 reference assemblies not found: $refRoot" }
if (-not (Test-Path -LiteralPath $managed)) { throw "Game Managed folder not found: $managed" }
if (-not (Test-Path -LiteralPath $bepInExCore)) { throw "BepInEx core not found: $bepInExCore" }

New-Item -ItemType Directory -Force -Path $pluginDir, $buildDir | Out-Null

$references = @(
    (Join-Path $refRoot 'mscorlib.dll'),
    (Join-Path $refRoot 'System.dll'),
    (Join-Path $refRoot 'System.Core.dll'),
    (Join-Path $refRoot 'Facades\netstandard.dll'),
    (Join-Path $managed 'UnityEngine.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $bepInExCore 'BepInEx.dll')
)

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Missing reference: $reference" }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('/nologo')
$lines.Add('/target:library')
$lines.Add('/platform:x64')
$lines.Add('/langversion:7.3')
$lines.Add('/optimize+')
$lines.Add('/debug-')
$lines.Add('/deterministic+')
$lines.Add('/utf8output')
$lines.Add('/out:"' + $outDll + '"')
foreach ($reference in $references) {
    $lines.Add('/reference:"' + $reference + '"')
}
Get-ChildItem -LiteralPath $src -Filter '*.cs' -File | Sort-Object Name | ForEach-Object {
    $lines.Add('"' + $_.FullName + '"')
}
[System.IO.File]::WriteAllLines($rsp, $lines, [System.Text.Encoding]::UTF8)

& $csc "@$rsp" *> $log
$exitCode = $LASTEXITCODE
Get-Content -LiteralPath $log -Encoding UTF8 | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0) { throw "Compilation failed with exit code $exitCode. See $log" }
if (-not (Test-Path -LiteralPath $outDll)) { throw "Compilation reported success but output is missing: $outDll" }

Write-Host "Built: $outDll"

if (-not $NoInstall) {
    $gamePluginDir = Join-Path $GameRoot 'BepInEx\plugins\WeissSchwarzDeckSync'
    New-Item -ItemType Directory -Force -Path $gamePluginDir | Out-Null
    Copy-Item -LiteralPath $outDll -Destination (Join-Path $gamePluginDir 'WeissSchwarzDeckSync.dll') -Force
    Write-Host "Installed: $gamePluginDir\WeissSchwarzDeckSync.dll"
}
