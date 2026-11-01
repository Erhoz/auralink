[CmdletBinding()]
param([ValidateSet('Install','Remove','RemoveAndRestart','RestartLighting','Inspect','RepairRegistration','UpdateAndRestart')][string]$Action='Install')
$ErrorActionPreference='Stop'
$stage=Join-Path $PSScriptRoot 'artifacts\armoury-stage3'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$helper=Join-Path $PSScriptRoot 'tools\Set-MachineDiagnostic.ps1'
$engine=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments='-NoProfile -NonInteractive -File "{0}" -Action {1}' -f $helper,$Action
if($Action -in @('Install','UpdateAndRestart')){
    $dll=Join-Path $PSScriptRoot 'artifacts\diagnostic-hal\AuraLink.DiagnosticHal.dll'
    $hash=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $arguments += ' -ExpectedAssemblyHash '+$hash
}
if($Action -eq 'Inspect'){
    $probe=Join-Path $stage 'AuraLink.FactoryProbe.x86.exe'
    $arguments += ' -ExpectedProbeHash '+(Get-FileHash -LiteralPath $probe).Hash
}
$process=Start-Process -FilePath $engine -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -PassThru
$null=$process.Handle
try{
    # UAC is handled by Windows/user. This helper never clicks security prompts.
    while(-not $process.WaitForExit(1000)){Write-Verbose 'Elevated operation is running.'}
    $process.Refresh()
    $status=Get-Content -LiteralPath (Join-Path $stage 'operation.json') -Raw | ConvertFrom-Json
    $status | ConvertTo-Json -Depth 10
    if($process.ExitCode -ne 0 -or -not $status.Completed){exit 2}
}finally{$process.Dispose()}
