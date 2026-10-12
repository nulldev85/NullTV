param([switch]$SkipTests,[switch]$Msix,[string]$Configuration='Release')
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=$PSScriptRoot
Set-Location -LiteralPath $root
if(!$env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME=Join-Path $root 'artifacts/dotnet-home' }
if(!$env:NUGET_PACKAGES) { $env:NUGET_PACKAGES=Join-Path $root 'artifacts/packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_NOLOGO='1'
function Run-Dotnet { & dotnet @args; if($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" } }
function Assert-Arm64([string]$Path) {
 $stream=[IO.File]::OpenRead($Path); $reader=[IO.BinaryReader]::new($stream)
 try { $stream.Position=0x3c; $offset=$reader.ReadInt32(); $stream.Position=$offset; if($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0xAA64) { throw "Not a native ARM64 PE: $Path" } } finally { $reader.Dispose(); $stream.Dispose() }
}
$lock=Get-Content -LiteralPath (Join-Path $root 'native/mpv.lock.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts/native') | Out-Null
$archive=Join-Path $root 'artifacts/native/mpv.7z'
if(!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $lock.url -OutFile $archive }
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $lock.sha256) { throw 'libmpv checksum mismatch. Delete artifacts/native/mpv.7z and retry.' }
& tar -xf $archive -C (Join-Path $root 'artifacts/native'); if($LASTEXITCODE -ne 0) { throw 'Windows tar could not extract the upstream 7z archive.' }
Copy-Item -LiteralPath (Join-Path $root 'artifacts/native/libmpv-2.dll') -Destination (Join-Path $root 'native/libmpv-2.dll') -Force
Assert-Arm64 (Join-Path $root 'native/libmpv-2.dll')
if(!$SkipTests) { Run-Dotnet @('run','--project','tests/NullTV.Tests','-c',$Configuration) }
$publish=Join-Path $root 'artifacts/NullTV-win-arm64'
Run-Dotnet @('publish','src/NullTV.App','-c',$Configuration,'-r','win-arm64','-p:Platform=ARM64','--self-contained','true','-o',$publish)
Assert-Arm64 (Join-Path $publish 'NullTV.exe')
Assert-Arm64 (Join-Path $publish 'coreclr.dll')
Assert-Arm64 (Join-Path $publish 'Microsoft.UI.Xaml.dll')
Assert-Arm64 (Join-Path $publish 'libmpv-2.dll')
foreach($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md','FEATURES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $publish -Force }
$zip=Join-Path $root 'artifacts/NullTV-win-arm64.zip'
Compress-Archive -Path $publish -DestinationPath $zip -Force
Write-Host "Portable app: $zip"
if($Msix) { & (Join-Path $root 'scripts/package-msix.ps1') -PublishDirectory $publish }
