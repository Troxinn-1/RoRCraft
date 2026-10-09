[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$MinecraftInstance,
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

$package = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if(-not $GamePath) { $GamePath = Find-RoR2 }
if(-not $MinecraftInstance) { $MinecraftInstance = Find-MinecraftInstance }
if(-not $GamePath) { throw 'Risk of Rain 2 was not found. Pass -GamePath explicitly.' }
if(-not $MinecraftInstance) { throw 'A Prism/MultiMC Minecraft instance was not found. Pass -MinecraftInstance explicitly.' }

$installer = Join-Path $PSScriptRoot 'Install-RoRCraft.ps1'
& $installer -PackagePath $package -GamePath $GamePath -MinecraftInstance $MinecraftInstance
if($NoLaunch) { exit 0 }

$mcRoot = if(Test-Path (Join-Path $MinecraftInstance '.minecraft')) { Join-Path $MinecraftInstance '.minecraft' } else { $MinecraftInstance }
$jar = Join-Path $mcRoot 'mods\skycraft-0.1.0.jar'
if(Test-Path $jar) { Write-Host "Minecraft bridge installed: $jar" }
$ror2 = Join-Path $GamePath 'Risk of Rain 2.exe'
Write-Host 'Starting Risk of Rain 2. Start the matching Minecraft instance from Prism Launcher if it is not already running.'
Start-Process -FilePath $ror2 -WorkingDirectory $GamePath
