[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedProbeHash)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $repo 'artifacts\usb-investigation'
$exe=Join-Path $stage 'AuraLink.RazerHidProbe.exe'
$names=@('Razer Chroma SDK Server','Razer Chroma SDK Service')
$report=[ordered]@{StartedUtc=[DateTime]::UtcNow.ToString('o');ProbeSHA256=$ExpectedProbeHash;Completed=$false;Error=$null;LightingCommandsSent=0;Stopped=@();Restored=@()}
$before=@()
function Save-Operation {
    $report | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $stage 'state-operation.json') -Encoding UTF8
}
try{
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator token required for temporary service isolation.'}
    if((Get-FileHash -LiteralPath $exe).Hash -ne $ExpectedProbeHash){throw 'Own probe hash changed.'}
    if(@(Get-Process RazerAppEngine -ErrorAction SilentlyContinue).Count){throw 'Close AppEngine before the isolated read.'}
    $before=@(Get-CimInstance Win32_Service | Where-Object {$_.Name -in $names} | Select-Object Name,State,StartMode,ProcessId)
    $report.Before=$before
    Save-Operation
    foreach($name in $names){
        $service=Get-Service -Name $name
        try{
            if(@($service.DependentServices | Where-Object Status -ne Stopped).Count){throw "Running dependents of $name"}
            if($service.Status -eq 'Running'){
                $report.Stopped += $name
                Save-Operation
                $service.Stop()
                $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(20))
            }elseif($service.Status -ne 'Stopped'){throw "Unexpected state of $name"}
        }finally{$service.Dispose()}
    }
    $stdout=Join-Path $stage 'hid-state.json'
    $stderr=Join-Path $stage 'hid-state.stderr.txt'
    $process=Start-Process -FilePath $exe -ArgumentList '--state' -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $null=$process.Handle
    try{
        if(-not $process.WaitForExit(15000)){$process.Kill();$process.WaitForExit();throw 'Own USB read timed out.'}
        $process.Refresh()
        $report.QueryResult=Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
        if($process.ExitCode -ne 0){throw 'USB logical query failed; see QueryResult.'}
    }finally{if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
    $report.IsolationMaintained=(@(Get-Service -Name $names | Where-Object Status -ne Stopped).Count -eq 0)
    if(-not $report.IsolationMaintained){throw 'SDK services restarted during query.'}
    $report.Completed=$true
}catch{$report.Error=$_.Exception.ToString()}
finally{
    foreach($name in @('Razer Chroma SDK Service','Razer Chroma SDK Server')){
        if(@($before | Where-Object {$_.Name -eq $name -and $_.State -eq 'Running'}).Count){
            $service=Get-Service -Name $name
            try{
                $service.Refresh()
                if($service.Status -eq 'StopPending'){$service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(20))}
                $service.Refresh()
                if($service.Status -eq 'Stopped'){$service.Start()}
                $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(30))
                $report.Restored += $name
            }catch{$report["RestoreError_$name"]=$_.Exception.Message;$report.Completed=$false}
            finally{$service.Dispose()}
        }
    }
    $report.After=@(Get-CimInstance Win32_Service | Where-Object {$_.Name -in $names} | Select-Object Name,State,StartMode,ProcessId)
    $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
    Save-Operation
}
if(-not $report.Completed){exit 2}
