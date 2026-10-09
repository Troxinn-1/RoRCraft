[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$MinecraftRoot)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$mc = if(Test-Path (Join-Path $MinecraftRoot '.minecraft')) { Join-Path $MinecraftRoot '.minecraft' } else { $MinecraftRoot }
$versions = Join-Path $mc 'versions'
if(!(Test-Path $versions)) { throw "Minecraft versions directory not found: $versions" }
$jar = Get-ChildItem $versions -Directory | ForEach-Object { Get-ChildItem $_.FullName -Filter '*.jar' -File -ErrorAction SilentlyContinue } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if(!$jar) { throw 'No installed Minecraft version JAR was found.' }
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'outputs\TechnicalPolish\ui-assets'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$map = @{
 'assets/minecraft/textures/particle/note.png'='note.png'
 'assets/minecraft/textures/entity/parrot/parrot_green.png'='parrot_green.png'
 'assets/minecraft/textures/block/jukebox_side.png'='jukebox_side.png'
 'assets/minecraft/textures/block/jukebox_top.png'='jukebox_top.png'
 'assets/minecraft/sounds/records/pigstep.ogg'='pigstep.ogg'
 'assets/minecraft/sounds/ui/button/click.ogg'='click.ogg'
}
$archive=[IO.Compression.ZipFile]::OpenRead($jar.FullName)
try { foreach($entry in $map.Keys) { $item=$archive.GetEntry($entry); if(!$item){throw "Minecraft asset missing: $entry"}; $out=Join-Path $target $map[$entry]; $stream=$item.Open(); $file=[IO.File]::Create($out); try{$stream.CopyTo($file)}finally{$file.Dispose();$stream.Dispose()} } }
finally { $archive.Dispose() }
Write-Output "Minecraft assets prepared from $($jar.FullName)"
