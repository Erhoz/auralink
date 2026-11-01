[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Begin','End')][string]$Action,
      [ValidateSet('Headset','HeadphoneStand')][string]$Variant='Headset')
$ErrorActionPreference='Stop'
$stage=Join-Path $PSScriptRoot 'artifacts\armoury-cache-test'
if($Variant -eq 'HeadphoneStand'){$stage=Join-Path $PSScriptRoot 'artifacts\headphonestand-test'}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$helper=Join-Path $PSScriptRoot 'tools\Invoke-ArmouryCacheTest.ps1'
$engine=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments='-NoProfile -NonInteractive -File "{0}" -Action {1} -Variant {2}' -f $helper,$Action,$Variant
$process=Start-Process -FilePath $engine -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -PassThru
$null=$process.Handle
try{
    # Only the user handles the Windows UAC prompt.
    while(-not $process.WaitForExit(1000)){Write-Verbose 'Cache experiment is running.'}
    $process.Refresh()
    $report=Get-Content -LiteralPath (Join-Path $stage ($Action+'.json')) -Raw | ConvertFrom-Json
    $report | ConvertTo-Json -Depth 10
    if($process.ExitCode -ne 0 -or -not $report.Completed){exit 2}
}finally{$process.Dispose()}
