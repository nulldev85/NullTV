param([Parameter(Mandatory)][string]$Executable,[string]$DataRoot)
$ErrorActionPreference='Stop'
$Executable=(Resolve-Path -LiteralPath $Executable).Path
if(!$DataRoot) { $DataRoot=Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/ui-validation' }
$reports=@()
foreach($tab in @('Live TV','Guide','Movies & Series','Now Playing','Media Servers','Settings')) {
 $dir=Join-Path $DataRoot ($tab.Replace(' ','-').Replace('&','and')+'-'+[Guid]::NewGuid().ToString('N'))
 $process=Start-Process -FilePath $Executable -ArgumentList @('--data-dir',('"'+$dir+'"'),'--smoke-tab',('"'+$tab+'"'),'--smoke-seconds','3') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(20000)) { throw "UI validation timed out: $tab" }
 $reportFile=Join-Path $dir 'ui-validation.json'
 if($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $reportFile)) { throw "UI validation crashed: $tab (exit $($process.ExitCode))" }
 $report=Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
 if(!$report.Passed -or !$report.DesignResources -or $report.Architecture -ne 'Arm64' -or $report.Width -lt 400) { throw "UI validation failed: $tab" }
 $reports+=$report
 Write-Host "PASS native UI: $tab"
}
$reports | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $DataRoot 'results.json')
