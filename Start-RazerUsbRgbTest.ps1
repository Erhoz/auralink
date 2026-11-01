[CmdletBinding()]
param([ValidateRange(1,300)][int]$Seconds=180,[switch]$RestoreRainbow,[switch]$RainbowBaseline,[switch]$NativeHal,[ValidateSet('Pipe','Log')][string]$FrameTransport='Pipe')
$ErrorActionPreference='Stop'
if(-not $RestoreRainbow -and -not $RainbowBaseline){throw 'Declare the known rainbow baseline with -RainbowBaseline; arbitrary prior effects cannot be restored.'}
if($RestoreRainbow -and $NativeHal){throw 'Recovery and native HAL modes are mutually exclusive.'}
& (Join-Path $PSScriptRoot 'Read-RazerHid.ps1') | Out-Null
$exe=Join-Path $PSScriptRoot 'artifacts\usb-investigation\AuraLink.RazerHidProbe.exe'
& $exe --lifecycle-tests (Join-Path $PSScriptRoot 'artifacts\usb-rgb-tests')
if($LASTEXITCODE -ne 0){throw 'Simulated restoration tests failed.'}
$run=Join-Path $PSScriptRoot ('artifacts\usb-rgb-test\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
@{Directory=$run;Seconds=$Seconds} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'artifacts\usb-rgb-test\latest-run.json') -Encoding UTF8
$helper=Join-Path $PSScriptRoot 'tools\Invoke-RazerUsbRgbTest.ps1'
$engine=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$argsText='-NoProfile -NonInteractive -File "{0}" -RunDirectory "{1}" -Seconds {2} -ExpectedProbeHash {3}' -f $helper,$run,$Seconds,(Get-FileHash -LiteralPath $exe).Hash
if($RestoreRainbow){$argsText+=' -RestoreRainbow'}
else{$argsText+=' -RainbowBaseline'}
if($NativeHal){$argsText+=' -NativeHal'}
if($NativeHal -and $FrameTransport -eq 'Pipe'){
    & (Join-Path $PSScriptRoot 'Build-DiagnosticHal.ps1') | Out-Null
    $halHash=(Get-FileHash (Join-Path $PSScriptRoot 'artifacts\diagnostic-hal\AuraLink.DiagnosticHal.dll')).Hash
    $argsText+=' -FrameTransport Pipe -ExpectedHalHash '+$halHash
}elseif($NativeHal){$argsText+=' -FrameTransport Log'}
$process=Start-Process -FilePath $engine -Verb RunAs -ArgumentList $argsText -WindowStyle Hidden -PassThru
$null=$process.Handle
try{
    while(-not $process.WaitForExit(1000)){Write-Verbose 'Volatile USB experiment is running.'}
    $process.Refresh()
    $report=Get-Content (Join-Path $run 'operation.json') -Raw | ConvertFrom-Json
    $report | ConvertTo-Json -Depth 14
    if($process.ExitCode -ne 0 -or -not $report.Completed){exit 2}
}finally{$process.Dispose()}
