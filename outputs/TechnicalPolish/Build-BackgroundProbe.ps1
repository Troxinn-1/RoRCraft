$ErrorActionPreference='Stop'
$timingHarmony=Join-Path (Split-Path $PSScriptRoot -Parent) 'Minecraft-RoR2-Bridge\profile\BepInEx\core\0Harmony.dll'
$managed='C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed'
$core=Join-Path (Split-Path $PSScriptRoot -Parent) 'Minecraft-RoR2-Bridge\profile\BepInEx\core'
$references=@(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {'/reference:'+ $_.FullName})
$arguments=@('/nologo','/noconfig','/target:library','/nostdlib+','/optimize+',('/out:'+ (Join-Path $PSScriptRoot 'bin\BackgroundAtlasProbe.dll')),('/reference:'+ (Join-Path $core 'BepInEx.dll')),('/reference:'+ (Join-Path $PSScriptRoot 'bin\RoRCraft.Visuals.dll')))+$references+@(Join-Path $PSScriptRoot 'src\BackgroundAtlasProbe.cs')
$arguments+=(Join-Path $PSScriptRoot 'src\NativeTintProbe.cs')
$arguments+=(Join-Path $PSScriptRoot 'src\NativeLightingProbe.cs')
$arguments+=(Join-Path $PSScriptRoot 'src\NativeLobbyProbe.cs')
$arguments+=(Join-Path $PSScriptRoot 'src\SurvivorSkinFeatureProbe.cs')
$arguments+=(Join-Path $PSScriptRoot 'src\DanceSceneProbe.cs')
$arguments+=(Join-Path $PSScriptRoot 'src\GameplayLiveProbe.cs')
$arguments+=('/reference:'+$timingHarmony)
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') @arguments
if($LASTEXITCODE -ne 0) {throw 'Headless atlas probe compilation failed'}
