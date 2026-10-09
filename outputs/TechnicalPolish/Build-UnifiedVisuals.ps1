$ErrorActionPreference='Stop'
$managed='C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed'
$core=Join-Path (Split-Path $PSScriptRoot -Parent) 'Minecraft-RoR2-Bridge\profile\BepInEx\core'
$risk=Join-Path $PSScriptRoot 'lib\RiskOfOptions\RiskOfOptions.dll'
$refs=@('/reference:'+ (Join-Path $PSScriptRoot '..\RoRCraft\Runtime\RoRCraft.dll'))+@(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {'/reference:'+ $_.FullName})
$arguments=@('/nologo','/noconfig','/target:library','/nostdlib+','/optimize+',('/out:'+ (Join-Path $PSScriptRoot 'bin\RoRCraft.Visuals.dll')),('/reference:'+ (Join-Path $core 'BepInEx.dll')),('/reference:'+ (Join-Path $core '0Harmony.dll')),('/reference:'+ $risk))+$refs
$sources=@('MaterialPolish.cs','MinecraftBlockShader.cs','SurfaceGroups.cs','EnvironmentColorBalance.cs','AtlasTiles.cs','AtlasTexture.cs','MashupSettings.cs','MinecraftSettingsScreen.cs','MinecraftGlyphs.cs','MinecraftMenuAudio.cs','WindowsMenuWave.cs','CursorSafety.cs','LobbySkinAvatar.cs','LobbySkinPolish.cs','SessionSkinData.cs')
$blockShader=Join-Path $PSScriptRoot 'shader-build\rorcraft-block-shaders'
$sources+='GameplaySkinVisibility.cs'
$sources+='MinecraftEnemyCombat.cs'
$sources+='SurvivalBalance.cs'
$sources+=@('SurvivorSkinStore.cs','SurvivorSkinMenu.cs')
$sources+='LobbyIdleController.cs'
$sources+='WindowsDanceWave.cs'
$sources+='LobbyDanceScene.cs'
if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ui-assets\pigstep-dance.wav'))) {throw 'Generate the installed Pigstep dance excerpt before packaging.'}
$sources+='ProbeFriend.cs'
if(!(Test-Path -LiteralPath $blockShader)) {throw 'Build and validate the block shader bundle before building the visual module.'}
$arguments+=('/resource:'+$blockShader+',MinecraftWorld.BlockShaders')
if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ui-assets\click.wav'))) {throw 'Generate the validated PCM menu sound before packaging.'}
$arguments+=@($sources | ForEach-Object {Join-Path $PSScriptRoot ('src\'+$_)})
$arguments+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ui-assets') | Where-Object {$_.Extension -in @('.png','.ogg','.wav')} | ForEach-Object {'/resource:'+$_.FullName+',MinecraftUi.'+$_.Name})
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') @arguments
if($LASTEXITCODE -ne 0) {throw 'Unified RoRCraft visuals/settings build failed'}
