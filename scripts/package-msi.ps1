param([Parameter(Mandatory)][string]$PublishDirectory,[string]$InstallerVersion='0.4.0')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$publish=(Resolve-Path -LiteralPath $PublishDirectory).Path
$stage=Join-Path $root 'artifacts/installer'
New-Item -ItemType Directory -Force $stage | Out-Null
$toolDir=Join-Path $root 'artifacts/tools/wix'
$wix=if($env:NULLTV_WIX_PATH) { $env:NULLTV_WIX_PATH } else { Join-Path $toolDir 'wix.exe' }
if(!(Test-Path -LiteralPath $wix)) { & dotnet tool install wix --version 5.0.2 --tool-path $toolDir; if($LASTEXITCODE -ne 0) { throw 'WiX installation failed' } }
$extension=Join-Path $stage 'ui-extension'
$dll=Get-ChildItem -LiteralPath $extension -Recurse -Filter WixToolset.UI.wixext.dll -ErrorAction SilentlyContinue | Select-Object -First 1
if(!$dll) {
 $package=Join-Path $stage 'wix-ui.nupkg'
 Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/wixtoolset.ui.wixext/5.0.2/wixtoolset.ui.wixext.5.0.2.nupkg' -OutFile $package
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::ExtractToDirectory($package,$extension,$true)
 $dll=Get-ChildItem -LiteralPath $extension -Recurse -Filter WixToolset.UI.wixext.dll | Select-Object -First 1
}
function Xml([string]$value) { [Security.SecurityElement]::Escape($value) }
function Id([string]$value) { 'i'+[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($value.ToLowerInvariant()))).Substring(0,28) }
function ComponentGuid([string]$value) { $hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(('NullTV-arm64/'+$value).ToLowerInvariant())); [byte[]]$bytes=$hash[0..15]; $bytes[7]=($bytes[7] -band 15) -bor 80; $bytes[8]=($bytes[8] -band 63) -bor 128; [Guid]::new($bytes).ToString('D') }
$builder=[Text.StringBuilder]::new()
[void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui"><Package Name="NullTV" Manufacturer="NullTV contributors" Version="'+(Xml $InstallerVersion)+'" UpgradeCode="5FF6F3A7-4F45-48C7-8E58-3C80A3CC42FA" Scope="perUser">')
# MSI's built-in WindowsBuild can report 9600 on Windows 11. Search the
# native registry view instead; the ARM64 package template enforces CPU type.
[void]$builder.AppendLine('<MajorUpgrade DowngradeErrorMessage="A newer NullTV version is already installed."/><MediaTemplate EmbedCab="yes" CompressionLevel="high"/><Property Id="NULLTV_WINDOWS_BUILD" Secure="yes"><RegistrySearch Id="NullTVWindowsBuild" Root="HKLM" Key="SOFTWARE\Microsoft\Windows NT\CurrentVersion" Name="CurrentBuildNumber" Type="raw" Bitness="always64"/></Property><Launch Condition="Installed OR (VersionNT64 AND NULLTV_WINDOWS_BUILD &gt;= 22000)" Message="NullTV requires Windows 11 ARM64."/>')
[void]$builder.AppendLine('<Property Id="ARPURLINFOABOUT" Value="https://github.com/nulldev85/NullTV"/><Property Id="ARPPRODUCTICON" Value="NullTVIcon"/><SetProperty Id="ARPINSTALLLOCATION" Value="[INSTALLFOLDER]" After="CostFinalize" Sequence="execute"/>')
[void]$builder.AppendLine('<Icon Id="NullTVIcon" SourceFile="'+(Xml (Join-Path $root 'packaging/Assets/NullTV.ico'))+'"/>')
[void]$builder.AppendLine('<StandardDirectory Id="LocalAppDataFolder"><Directory Id="Programs" Name="Programs"><Directory Id="INSTALLFOLDER" Name="NullTV"/></Directory></StandardDirectory><StandardDirectory Id="ProgramMenuFolder"><Directory Id="AppMenuFolder" Name="NullTV"/></StandardDirectory>')
$directories=@{ ''='INSTALLFOLDER' }
foreach($directory in Get-ChildItem -LiteralPath $publish -Directory -Recurse | Sort-Object { $_.FullName.Length }) {
 $relative=[IO.Path]::GetRelativePath($publish,$directory.FullName); $parent=[IO.Path]::GetDirectoryName($relative); $idValue=Id ('dir/'+$relative); $directories[$relative]=$idValue
 [void]$builder.AppendLine('<DirectoryRef Id="'+$directories[$parent]+'"><Directory Id="'+$idValue+'" Name="'+(Xml $directory.Name)+'"/></DirectoryRef>')
}
$components=[Collections.Generic.List[string]]::new()
foreach($file in Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName) {
 $relative=[IO.Path]::GetRelativePath($publish,$file.FullName); $parent=[IO.Path]::GetDirectoryName($relative); $idValue=Id ('file/'+$relative); $fileId=Id ('payload/'+$relative); $components.Add($idValue)
 [void]$builder.AppendLine('<Component Id="'+$idValue+'" Directory="'+$directories[$parent]+'" Guid="'+(ComponentGuid $relative)+'" Bitness="always64"><File Id="'+$fileId+'" Source="'+(Xml $file.FullName)+'" Name="'+(Xml $file.Name)+'"/><RegistryValue Root="HKCU" Key="Software\NullTV\Installer\Files" Name="'+$idValue+'" Type="integer" Value="1" KeyPath="yes"/></Component>')
}
foreach($entry in $directories.GetEnumerator()) {
 $component=Id ('folder/'+$entry.Key); $components.Add($component)
 [void]$builder.AppendLine('<Component Id="'+$component+'" Directory="'+$entry.Value+'" Guid="*" Bitness="always64"><RemoveFolder Id="'+(Id ('remove/'+$entry.Key))+'" On="uninstall"/><RegistryValue Root="HKCU" Key="Software\NullTV\Installer\Folders" Name="'+$component+'" Type="integer" Value="1" KeyPath="yes"/></Component>')
}
[void]$builder.AppendLine('<Component Id="StartMenuEntry" Directory="AppMenuFolder" Guid="*" Bitness="always64"><Shortcut Id="NullTVShortcut" Name="NullTV" Description="Native Windows ARM64 television and media player" Target="[INSTALLFOLDER]NullTV.exe" WorkingDirectory="INSTALLFOLDER" Icon="NullTVIcon"/><RemoveFolder Id="RemoveAppMenu" On="uninstall"/><RegistryValue Root="HKCU" Key="Software\NullTV\Installer" Name="Installed" Type="integer" Value="1" KeyPath="yes"/></Component>')
[void]$builder.AppendLine('<Feature Id="Complete" Title="NullTV" Level="1"><ComponentRef Id="StartMenuEntry"/>')
foreach($component in $components) { [void]$builder.AppendLine('<ComponentRef Id="'+$component+'"/>') }
& (Join-Path $PSScriptRoot 'create-setup-assets.ps1') -OutputDirectory $stage
[void]$builder.AppendLine('</Feature><ui:WixUI Id="WixUI_Minimal"/><WixVariable Id="WixUILicenseRtf" Value="'+(Xml (Join-Path $stage 'license.rtf'))+'"/><WixVariable Id="WixUIDialogBmp" Value="'+(Xml (Join-Path $stage 'setup-dialog.bmp'))+'"/><WixVariable Id="WixUIBannerBmp" Value="'+(Xml (Join-Path $stage 'setup-banner.bmp'))+'"/></Package></Wix>')
$license=[IO.File]::ReadAllText((Join-Path $root 'LICENSE')).Replace('\','\\').Replace('{','\{').Replace('}','\}').Replace([string][char]13,'').Replace([string][char]10,'\par ')
[IO.File]::WriteAllText((Join-Path $stage 'license.rtf'),'{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 '+$license+'}')
$source=Join-Path $stage 'NullTV.wxs'; [IO.File]::WriteAllText($source,$builder.ToString())
$output=Join-Path $root 'artifacts/NullTV-Setup-arm64.msi'
& $wix build $source -arch arm64 -ext $dll.FullName -o $output -intermediatefolder (Join-Path $stage 'obj') 2>&1 | Out-File -LiteralPath (Join-Path $stage 'build.log')
if($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $stage 'build.log') -Tail 12; throw 'MSI packaging failed' }
& (Join-Path $PSScriptRoot 'verify-installer.ps1') -InstallerPath $output
Write-Host "Windows ARM64 installer: $output"
