[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$GamePath,
    [Parameter(Mandatory=$true)][string]$MinecraftInstance,
    [switch]$BackupOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$Message) { throw "RoRCraft installer: $Message" }
function RequireFile([string]$Path,[string]$What) {
    if(-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "Missing ${What}: $Path" }
}
function RequireDirectory([string]$Path,[string]$What) {
    if(-not (Test-Path -LiteralPath $Path -PathType Container)) { Fail "Missing ${What}: $Path" }
}

$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$MinecraftInstance = (Resolve-Path -LiteralPath $MinecraftInstance).Path
$manifestPath = Join-Path $PackagePath 'manifest.json'
RequireFile $manifestPath 'package manifest'
RequireFile (Join-Path $GamePath 'Risk of Rain 2.exe') 'Risk of Rain 2 executable'
RequireDirectory (Join-Path $GamePath 'BepInEx') 'BepInEx installation'
RequireDirectory $MinecraftInstance 'Minecraft instance'

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach($entry in $manifest.files) {
    $file = Join-Path $PackagePath $entry.path
    RequireFile $file "package file $($entry.path)"
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if($actual -ne $entry.sha256) { Fail "Hash mismatch in package file $($entry.path)" }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $PackagePath "backups\before-install-$stamp"
$nativePlugins = Join-Path $GamePath 'BepInEx\plugins\RoRCraft'
$mods = Join-Path $MinecraftInstance '.minecraft\mods'
$mcJar = Join-Path $PackagePath 'Minecraft\RoRCraft.jar'
$runtime = Join-Path $PackagePath 'Runtime'

RequireFile (Join-Path $runtime 'RoRCraft.dll') 'native host module'
RequireFile (Join-Path $runtime 'RoRCraft.Visuals.dll') 'visual module'
RequireFile $mcJar 'Minecraft bridge jar'

New-Item -ItemType Directory -Path $backup,$mods -Force | Out-Null
if(Test-Path -LiteralPath $nativePlugins) {
    Copy-Item -LiteralPath $nativePlugins -Destination (Join-Path $backup 'RoRCraft') -Recurse -Force -ErrorAction SilentlyContinue
}
$existingJar = Join-Path $mods 'skycraft-0.1.0.jar'
if(Test-Path -LiteralPath $existingJar) { Copy-Item -LiteralPath $existingJar -Destination (Join-Path $backup 'skycraft-0.1.0.jar') -Force }
if($BackupOnly) { Write-Output "Backup created: $backup"; return }

if(-not $PSCmdlet.ShouldProcess("$GamePath and $MinecraftInstance",'Install RoRCraft')) { return }
New-Item -ItemType Directory -Path $nativePlugins -Force | Out-Null
foreach($file in Get-ChildItem -LiteralPath $runtime -File -Recurse) {
    $target = Join-Path $nativePlugins $file.FullName.Substring($runtime.Length+1)
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
Copy-Item -LiteralPath $mcJar -Destination $existingJar -Force
Write-Output "RoRCraft $($manifest.version) installed."
Write-Output "Backup: $backup"
Write-Output 'Start Risk of Rain 2 through BepInEx and keep the matching Minecraft instance configured.'
