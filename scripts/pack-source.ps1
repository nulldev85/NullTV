param([string]$OutputFile)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$OutputFile) { $OutputFile=Join-Path (Split-Path -Parent $root) 'NullTV-source.zip' }
$stage=Join-Path $root ('artifacts/source-stage-'+[Guid]::NewGuid().ToString('N'))
$target=Join-Path $stage 'NullTV'
New-Item -ItemType Directory -Force $target | Out-Null
foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
 $relative=[IO.Path]::GetRelativePath($root,$file.FullName)
 if($relative -match '(^|\\)(artifacts|bin|obj|\.git)(\\|$)' -or $relative -match '^native\\.*\.(dll|7z)$') { continue }
 $destination=Join-Path $target $relative
 New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
 Copy-Item -LiteralPath $file.FullName -Destination $destination
}
Compress-Archive -LiteralPath $target -DestinationPath $OutputFile -Force
Write-Host "Source archive: $OutputFile"
