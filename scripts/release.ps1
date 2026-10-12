param([Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$InstallerPath,[Parameter(Mandatory)][string]$SourceArchive,[Parameter(Mandatory)][string]$NotesPath,[Parameter(Mandatory)][string]$SigningKeyPath,[switch]$Publish)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$repo='nulldev85/NullTV-updates'
if($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a stable major.minor.patch version.' }
$msi=(Resolve-Path -LiteralPath $InstallerPath).Path
$source=(Resolve-Path -LiteralPath $SourceArchive).Path
$notes=(Resolve-Path -LiteralPath $NotesPath).Path
$key=(Resolve-Path -LiteralPath $SigningKeyPath).Path
$props=[xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
if($props.Project.PropertyGroup.Version -ne $Version) { throw 'The source and release versions must match.' }
$stage=Join-Path $root ('artifacts/release-'+$Version)
New-Item -ItemType Directory -Force $stage | Out-Null
$asset=Join-Path $stage ('NullTV-Setup-arm64-'+$Version+'.msi')
Copy-Item -LiteralPath $msi -Destination $asset -Force
Copy-Item -LiteralPath $source -Destination (Join-Path $stage 'NullTV-source.zip') -Force
$manifest=Join-Path $stage 'update.json'
& dotnet run --project (Join-Path $root 'tools/NullTV.Release') -c Release -- sign $key $asset $Version $notes $manifest
if($LASTEXITCODE -ne 0) { throw 'Release signing or MSI verification failed.' }
& dotnet run --project (Join-Path $root 'tools/NullTV.Release') -c Release --no-build -- verify $manifest $asset
if($LASTEXITCODE -ne 0) { throw 'Final release verification failed.' }
$hashes=foreach($file in Get-ChildItem -LiteralPath $stage -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' }) { (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$file.Name }
$hashes | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt')
& gh release create ('v'+$Version) --repo $repo --draft --title ('NullTV '+$Version) --notes-file $notes $asset (Join-Path $stage 'NullTV-source.zip') $manifest (Join-Path $stage 'SHA256SUMS.txt')
if($LASTEXITCODE -ne 0) { throw 'Draft release creation failed. The local signed files remain intact.' }
if($Publish) { & gh release edit ('v'+$Version) --repo $repo --draft=false --latest; if($LASTEXITCODE -ne 0) { throw 'Publishing failed. Review the existing draft before retrying.' } }
Write-Host ('Release '+$Version+' prepared in '+$stage)
