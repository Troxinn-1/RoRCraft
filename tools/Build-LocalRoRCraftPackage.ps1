[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [string]$GamePath,
    [string]$MinecraftRoot,
    [switch]$SkipShaderBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$polish = Join-Path $root 'outputs\TechnicalPolish'
if(!(Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Unity Editor not found: $UnityEditor" }
if(!(Test-Path -LiteralPath $polish)) { throw 'TechnicalPolish source tree is missing.' }
if($GamePath) {
    $managed = Join-Path $GamePath 'Risk of Rain 2_Data\Managed'
    if(!(Test-Path -LiteralPath $managed -PathType Container)) { throw "RoR2 Managed directory not found: $managed" }
}
if($MinecraftRoot -and !(Test-Path -LiteralPath $MinecraftRoot -PathType Container)) { throw "Minecraft directory not found: $MinecraftRoot" }

$shader = Join-Path $polish 'shader-build\rorcraft-block-shaders'
if(!$SkipShaderBuild) { & (Join-Path $polish 'Build-BlockShader.ps1') -UnityEditor $UnityEditor }
if(!(Test-Path -LiteralPath $shader -PathType Leaf)) { throw 'Shader bundle is missing after the build.' }

foreach($asset in @((Join-Path $polish 'ui-assets\click.wav'),(Join-Path $polish 'ui-assets\pigstep-dance.wav'))) {
    if(!(Test-Path -LiteralPath $asset -PathType Leaf)) { throw "Generated local asset is missing: $asset" }
}

& (Join-Path $polish 'Build-UnifiedVisuals.ps1')
if($LASTEXITCODE -ne 0) { throw 'Visual module build failed.' }
& (Join-Path $polish 'Build-ModPackage.ps1') -UseBuiltVisuals
if($LASTEXITCODE -ne 0) { throw 'RoRCraft package build failed.' }
Write-Output ('Package ready: ' + (Join-Path $root 'outputs\RoRCraft'))
