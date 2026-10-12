param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference='Stop'
$installer=$null; $session=$null; $database=$null; $view=$null; $record=$null; $summary=$null
function Get-Property([string]$name) {
 $session.GetType().InvokeMember('Property',[Reflection.BindingFlags]::GetProperty,$null,$session,@($name))
}
function Set-Property([string]$name,[string]$value) {
 [void]$session.GetType().InvokeMember('Property',[Reflection.BindingFlags]::SetProperty,$null,$session,@($name,$value))
}
function Read-Field($row,[int]$index) {
 $row.GetType().InvokeMember('StringData',[Reflection.BindingFlags]::GetProperty,$null,$row,@($index))
}
function Assert-Gate([string]$name,[int]$expected) {
 $actual=$session.EvaluateCondition($condition)
 if($actual -ne $expected) { throw "Installer prerequisite regression: $name (expected $expected, got $actual)" }
 Write-Host "PASS: $name"
}
try {
 $installer=New-Object -ComObject WindowsInstaller.Installer
 $installer.UILevel=2
 # OpenPackage option 1 is the documented safe session: no installation,
 # user profile changes, or registration. Only the read-only AppSearch action runs.
 $session=$installer.OpenPackage((Resolve-Path -LiteralPath $InstallerPath).Path,1)
 $database=$installer.OpenDatabase((Resolve-Path -LiteralPath $InstallerPath).Path,0)
 $summary=$database.SummaryInformation(0)
 $template=$summary.GetType().InvokeMember('Property',[Reflection.BindingFlags]::GetProperty,$null,$summary,@(7))
 if($template -ne 'Arm64;1033') { throw "Unexpected installer architecture/language: $template" }
 $view=$database.OpenView('SELECT `Condition` FROM `LaunchCondition`')
 $view.Execute(); $condition=$null
 while($record=$view.Fetch()) {
  $candidate=Read-Field $record 1
  if($candidate -match '(?<!_)WindowsBuild') { throw 'Installer uses the unreliable WindowsBuild property' }
  if($candidate -match 'NULLTV_WINDOWS_BUILD') { $condition=$candidate }
  [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record); $record=$null
 }
 if(!$condition) { throw 'Missing Windows prerequisite condition' }
 $view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view); $view=$null
 foreach($table in @('InstallUISequence','InstallExecuteSequence')) {
  $view=$database.OpenView("SELECT ``Action``, ``Sequence`` FROM ``$table`` WHERE ``Action`` = 'AppSearch' OR ``Action`` = 'LaunchConditions'")
  $view.Execute(); $sequence=@{}
  while($record=$view.Fetch()) {
   $sequence[(Read-Field $record 1)]=[int](Read-Field $record 2)
   [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record); $record=$null
  }
  $view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view); $view=$null
  if(!$sequence.ContainsKey('AppSearch') -or !$sequence.ContainsKey('LaunchConditions') -or $sequence.AppSearch -ge $sequence.LaunchConditions) { throw "AppSearch must precede LaunchConditions in $table" }
 }
 [void]$session.DoAction('AppSearch')
 $build=Get-Property 'NULLTV_WINDOWS_BUILD'
 $registryBuild=(Get-ItemProperty -LiteralPath 'HKLM:/SOFTWARE/Microsoft/Windows NT/CurrentVersion' -Name CurrentBuildNumber).CurrentBuildNumber
 if($build -ne $registryBuild) { throw "Installer AppSearch did not retrieve the actual Windows build (got '$build', expected '$registryBuild')" }
 $is64=Get-Property 'VersionNT64'
 Set-Property 'Installed' ''
 Assert-Gate "Actual OS build $build" ([int]([int]$build -ge 22000 -and !!$is64))
 Set-Property 'WindowsBuild' '9600'
 Set-Property 'VersionNT64' '603'
 Assert-Gate 'Windows 11 with MSI compatibility build 9600' ([int]([int]$build -ge 22000))
 Set-Property 'NULLTV_WINDOWS_BUILD' '22000'
 Assert-Gate 'Windows 11 minimum build' 1
 Set-Property 'NULLTV_WINDOWS_BUILD' '21999'
 Assert-Gate 'Below Windows 11 minimum build' 0
 Set-Property 'NULLTV_WINDOWS_BUILD' '19045'
 Assert-Gate 'Windows 10 remains unsupported' 0
 Set-Property 'NULLTV_WINDOWS_BUILD' ''
 Assert-Gate 'Missing build cannot silently pass' 0
 Set-Property 'NULLTV_WINDOWS_BUILD' '26200'
 Set-Property 'VersionNT64' ''
 Assert-Gate 'Missing 64-bit OS cannot silently pass' 0
 Set-Property 'Installed' '1'
 Assert-Gate 'Maintenance and uninstall remain accessible' 1
 Write-Host 'Installer verified: ARM64 template, both action sequences, native registry search and 8 prerequisite cases. No installation performed.'
} finally {
 foreach($comObject in @($record,$view,$summary,$database,$session,$installer)) {
  if($null -ne $comObject -and [Runtime.InteropServices.Marshal]::IsComObject($comObject)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) }
 }
}
