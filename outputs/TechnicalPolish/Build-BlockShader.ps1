param([Parameter(Mandatory=$true)][string]$UnityEditor)
$ErrorActionPreference='Stop'
if (!(Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw 'Unity Editor executable is missing.' }
$project=Join-Path $PSScriptRoot 'ShaderProject'
$log=Join-Path $PSScriptRoot 'captures/shader-editor-build.log'
# Batch mode keeps this compiler out of the desktop/input foreground. Do not
# pass account credentials or activate a licence automatically from this script.
$arguments=@('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','BuildRoRCraftShader.Build','-logFile',('"'+$log+'"'))
$started=Get-Date
$process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if($process.ExitCode -ne 0) { throw "Shader build failed ($($process.ExitCode)); inspect $log. An activated Unity licence is required." }
$bundle=Join-Path $PSScriptRoot 'shader-build/rorcraft-block-shaders'
if(!(Test-Path -LiteralPath $bundle)) { throw 'Editor returned without producing the shader bundle.' }
if((Get-Item -LiteralPath $bundle).LastWriteTime -lt $started) { throw 'Stale shader bundle; this run did not build a fresh result.' }
Get-FileHash -LiteralPath $bundle -Algorithm SHA256
Write-Output 'Experimental bundle only. Runtime and stage acceptance required before packaging.'
