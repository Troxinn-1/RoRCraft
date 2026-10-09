[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PackagePath)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$root=(Resolve-Path -LiteralPath $PackagePath).Path
$manifest=Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$bad=@()
foreach($entry in $manifest.files) {
    $file=Join-Path $root $entry.path
    if(!(Test-Path -LiteralPath $file)){ $bad+=$entry.path; continue }
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256){$bad+=$entry.path}
}
if($bad.Count){throw ('Package verification failed: '+($bad -join ', '))}
Write-Output "RoRCraft $($manifest.version) package verified ($($manifest.files.Count) files)."
