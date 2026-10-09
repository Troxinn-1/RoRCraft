[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [Parameter(Mandatory=$true)][string]$MinecraftRoot,
    [string]$MinecraftVersion = '26.3',
    [string]$Java = 'java.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Need([string]$p,[string]$n){if(!(Test-Path -LiteralPath $p)){throw "Missing ${n}: $p"}}
Need $GamePath 'Risk of Rain 2 directory'
Need (Join-Path $GamePath 'Risk of Rain 2.exe') 'Risk of Rain 2 executable'
New-Item -ItemType Directory -Path $MinecraftRoot -Force | Out-Null
$mcJava = Get-ChildItem (Join-Path $MinecraftRoot 'runtime') -Recurse -Filter 'java.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if($mcJava) { $Java = $mcJava.FullName }
if($Java -eq 'java.exe' -and !(Get-Command $Java -ErrorAction SilentlyContinue)) { throw 'Java was not found. Install Java or pass -Java with the Minecraft runtime java.exe path.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('rorcraft-deps-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $bepUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
    $bepZip = Join-Path $temp 'BepInEx.zip'
    Invoke-WebRequest -Uri $bepUrl -OutFile $bepZip
    if($PSCmdlet.ShouldProcess($GamePath,'Install BepInEx')) { Expand-Archive -LiteralPath $bepZip -DestinationPath $GamePath -Force }

    $installerInfo = [pscustomobject]@{version='1.1.2';url='https://maven.fabricmc.net/net/fabricmc/fabric-installer/1.1.2/fabric-installer-1.1.2.jar'}
    $fabricJar = Join-Path $temp 'fabric-installer.jar'
    Invoke-WebRequest -Uri ([string]$installerInfo.url) -OutFile $fabricJar
    $mcDir = if(Test-Path (Join-Path $MinecraftRoot '.minecraft')) { Join-Path $MinecraftRoot '.minecraft' } else { $MinecraftRoot }
    New-Item -ItemType Directory -Path $mcDir -Force | Out-Null
    if($PSCmdlet.ShouldProcess($mcDir,"Install Fabric Loader $($installerInfo.version)")) {
        & $Java -jar $fabricJar client -dir $mcDir -mcversion $MinecraftVersion -noprofile
        if($LASTEXITCODE -ne 0) { throw "Fabric installer failed with exit code $LASTEXITCODE." }
    }
    Write-Output "Dependencies prepared. BepInEx: $GamePath; Fabric: $mcDir"
} finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
