[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$MinecraftInstance,
    [string]$MinecraftLauncherPath,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Find-RoR2 {
    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Risk of Rain 2'),
        (Join-Path ${env:ProgramFiles} 'Steam\steamapps\common\Risk of Rain 2'),
        (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data')
    ) | Where-Object { $_ }
    foreach($root in $roots) {
        if(Test-Path -LiteralPath (Join-Path $root 'Risk of Rain 2.exe')) { return (Resolve-Path $root).Path }
    }
    return $null
}

function Find-MinecraftInstance {
    $roots = @(
        (Join-Path $env:USERPROFILE 'AppData\Roaming\PrismLauncher\instances'),
        (Join-Path $env:USERPROFILE 'AppData\Roaming\MultiMC\instances'),
        (Join-Path $env:USERPROFILE 'AppData\Roaming\.minecraft')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    foreach($root in $roots) {
        $candidates = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)
        foreach($candidate in $candidates) {
            $instance = Join-Path $candidate.FullName '.minecraft'
            if(Test-Path -LiteralPath (Join-Path $instance 'mods')) { return $candidate.FullName }
        }
        if(Test-Path -LiteralPath (Join-Path $root 'mods')) { return $root }
    }
    return $null
}

function Find-MinecraftLauncher {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Minecraft Launcher\MinecraftLauncher.exe'),
        (Join-Path ${env:ProgramFiles} 'Minecraft Launcher\MinecraftLauncher.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Minecraft Launcher\MinecraftLauncher.exe')
    ) | Where-Object { $_ }
    foreach($candidate in $candidates) {
        if(Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-Path $candidate).Path }
    }
    return $null
}

if(-not $GamePath) { $GamePath = Find-RoR2 }
if(-not $MinecraftInstance) { $MinecraftInstance = Find-MinecraftInstance }
if(-not $MinecraftLauncherPath) { $MinecraftLauncherPath = Find-MinecraftLauncher }
if(-not $GamePath) { throw 'Risk of Rain 2 was not found. Pass -GamePath explicitly.' }
if(-not $MinecraftInstance) { throw 'A Prism/MultiMC Minecraft instance was not found. Pass -MinecraftInstance explicitly.' }

if(!(Test-Path -LiteralPath (Join-Path $GamePath 'BepInEx') -PathType Container)) {
    $dependencyInstaller = Join-Path $PSScriptRoot 'Install-RoRCraftDependencies.ps1'
    if(!(Test-Path -LiteralPath $dependencyInstaller)) { throw 'BepInEx is missing and the dependency installer is unavailable.' }
    & $dependencyInstaller -GamePath $GamePath -MinecraftRoot $MinecraftInstance
    if($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
}

$package = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if(-not (Test-Path -LiteralPath (Join-Path $package 'manifest.json'))) {
    throw 'This launcher must run beside a prepared RoRCraft package containing manifest.json, Runtime, and Minecraft. The public release does not bundle the visual module; build the local package first.'
}

$installer = Join-Path $PSScriptRoot 'Install-RoRCraft.ps1'
& $installer -PackagePath $package -GamePath $GamePath -MinecraftInstance $MinecraftInstance
if($NoLaunch) { exit 0 }

$mcRoot = if(Test-Path (Join-Path $MinecraftInstance '.minecraft')) { Join-Path $MinecraftInstance '.minecraft' } else { $MinecraftInstance }
$jar = Join-Path $mcRoot 'mods\skycraft-0.1.0.jar'
if(Test-Path $jar) { Write-Host "Minecraft bridge installed: $jar" }
$ror2 = Join-Path $GamePath 'Risk of Rain 2.exe'
if($MinecraftLauncherPath) {
    Write-Host 'Starting the official Minecraft Launcher. Select the installed Fabric profile if it is not already selected.'
    Start-Process -FilePath $MinecraftLauncherPath
} else {
    try {
        Start-Process 'minecraft://'
        Write-Host 'Started the registered Minecraft Launcher protocol.'
    } catch {
        Write-Host 'Minecraft Launcher was not found automatically; start your Fabric profile manually.'
    }
}
Write-Host 'Starting Risk of Rain 2 through BepInEx.'
Start-Process -FilePath $ror2 -WorkingDirectory $GamePath
