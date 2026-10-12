param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$tool=Get-ChildItem -LiteralPath (Join-Path $env:NUGET_PACKAGES 'microsoft.windows.sdk.buildtools') -Filter makeappx.exe -Recurse | Where-Object { $_.FullName -match '\\arm64\\' } | Select-Object -First 1
if(!$tool) { Write-Warning 'ARM64 MakeAppx unavailable; portable ZIP remains usable.'; return }
$stage=Join-Path $root 'artifacts/msix-stage'
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item -Path (Join-Path $PublishDirectory '*') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'packaging/AppxManifest.xml') -Destination (Join-Path $stage 'AppxManifest.xml') -Force
Copy-Item -LiteralPath (Join-Path $root 'packaging/Assets') -Destination $stage -Recurse -Force
$output=Join-Path $root 'artifacts/NullTV-arm64.msix'
& $tool.FullName pack /d $stage /p $output /o 2>&1 | Out-File -LiteralPath (Join-Path $root 'artifacts/msix-build.log')
if($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
Write-Host "Unsigned MSIX: $output (sign with your trusted certificate before installation)."
