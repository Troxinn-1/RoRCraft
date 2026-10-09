$ErrorActionPreference='Stop'
$managed='C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed'
$core=Join-Path (Split-Path $PSScriptRoot -Parent) 'Minecraft-RoR2-Bridge\profile\BepInEx\core'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {'/reference:'+ $_.FullName})
$args=@('/nologo','/noconfig','/target:library','/unsafe+','/nostdlib+','/optimize+',('/out:'+ (Join-Path $PSScriptRoot 'bin\MovementPolish.dll')),('/reference:'+ (Join-Path $core 'BepInEx.dll')),('/reference:'+ (Join-Path $core '0Harmony.dll')),('/reference:'+ (Join-Path $PSScriptRoot 'snapshots\before-polish\installed-0.7-RoRCraft.dll')))+$refs+@(Join-Path $PSScriptRoot 'src\MovementPolish.cs')+@(Join-Path $PSScriptRoot 'src\TickSampling.cs')+@(Join-Path $PSScriptRoot 'src\RenderPacketReuse.cs')+@(Join-Path $PSScriptRoot 'src\RenderPacketBuffers.cs')
$args+=@(Join-Path $PSScriptRoot 'src\NativeTerrain.cs')
& $compiler @args
if($LASTEXITCODE -ne 0) {throw 'Movement polish build failed'}
& $compiler /nologo ('/out:'+ (Join-Path $PSScriptRoot 'bin\TickSamplingChecks.exe')) (Join-Path $PSScriptRoot 'TickSamplingChecks.cs') (Join-Path $PSScriptRoot 'src\TickSampling.cs')
if($LASTEXITCODE -ne 0) {throw 'Tick clock checks build failed'}
& (Join-Path $PSScriptRoot 'bin\TickSamplingChecks.exe')
if($LASTEXITCODE -ne 0) {throw 'Tick clock checks failed'}
& $compiler /nologo ('/out:'+ (Join-Path $PSScriptRoot 'bin\RenderPacketChecks.exe')) (Join-Path $PSScriptRoot 'RenderPacketChecks.cs') (Join-Path $PSScriptRoot 'src\RenderPacketBuffers.cs')
if($LASTEXITCODE -ne 0) {throw 'Render packet checks build failed'}
& (Join-Path $PSScriptRoot 'bin\RenderPacketChecks.exe')
if($LASTEXITCODE -ne 0) {throw 'Render packet checks failed'}

