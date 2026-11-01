[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunDirectory,
      [ValidateRange(1,300)][int]$Seconds=180,
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedProbeHash,
      [switch]$RestoreRainbow,[switch]$RainbowBaseline)
$ErrorActionPreference='Stop'
if(-not $RestoreRainbow -and -not $RainbowBaseline){throw 'Known rainbow baseline required.'}
$repo=Split-Path -Parent $PSScriptRoot
$root=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts\usb-rgb-test'))+'\'
$RunDirectory=[IO.Path]::GetFullPath($RunDirectory)
if(-not $RunDirectory.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Run directory must be inside the experiment artifact root.'}
$exe=Join-Path $repo 'artifacts\usb-investigation\AuraLink.RazerHidProbe.exe'
$names=@('Razer Chroma SDK Server','Razer Chroma SDK Service')
$before=@()
$report=[ordered]@{StartedUtc=[DateTime]::UtcNow.ToString('o');Completed=$false;Error=$null;ProbeSHA256=$ExpectedProbeHash;RunDirectory=$RunDirectory;Stopped=@();Restored=@()}
function Save-Report {$report | ConvertTo-Json -Depth 14 | Set-Content (Join-Path $RunDirectory 'operation.json') -Encoding UTF8}
function Invoke-Probe([string]$Arguments,[string]$Label,[int]$Timeout){
    $stdout=Join-Path $RunDirectory ($Label+'.stdout.txt')
    $stderr=Join-Path $RunDirectory ($Label+'.stderr.txt')
    $process=Start-Process -FilePath $exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $null=$process.Handle
    try{
        if(-not $process.WaitForExit($Timeout*1000)){$process.Kill();$process.WaitForExit();throw "$Label timed out"}
        $process.Refresh()
        return $process.ExitCode
    }finally{if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
}
try{
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator token required for SDK isolation.'}
    if((Get-FileHash -LiteralPath $exe).Hash -ne $ExpectedProbeHash){throw 'Probe hash changed.'}
    if(@(Get-Process RazerAppEngine -ErrorAction SilentlyContinue).Count){throw 'AppEngine must be closed.'}
    $before=@(Get-CimInstance Win32_Service | Where-Object {$_.Name -in $names} | Select-Object Name,State,StartMode,ProcessId)
    $report.Before=$before
    Save-Report
    foreach($name in $names){
        $service=Get-Service -Name $name
        try{
            if(@($service.DependentServices | Where-Object Status -ne Stopped).Count){throw "Running dependents of $name"}
            if($service.Status -eq 'Running'){
                $report.Stopped += $name;Save-Report
                $service.Stop();$service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(20))
            }elseif($service.Status -ne 'Stopped'){throw "Unexpected service state: $name"}
        }finally{$service.Dispose()}
    }
    $probeArguments=if($RestoreRainbow){'--restore-rainbow'}else{'--rgb-from-rainbow "{0}" {1}' -f $RunDirectory,$Seconds}
    $exitCode=Invoke-Probe $probeArguments 'rgb' ($Seconds+20)
    if($RestoreRainbow){$report.RainbowRecovery=Get-Content (Join-Path $RunDirectory 'rgb.stdout.txt') -Raw | ConvertFrom-Json}
    $report.ProbeExitCode=$exitCode
    if($exitCode -ne 0){throw 'Volatile RGB experiment failed; see device-operation.json.'}
    $report.IsolationMaintained=(@(Get-Service $names | Where-Object Status -ne Stopped).Count -eq 0 -and @(Get-Process RazerAppEngine -ErrorAction SilentlyContinue).Count -eq 0)
    if(-not $report.IsolationMaintained){throw 'AppEngine or SDK services became active during test.'}
    $report.Completed=$true
}catch{$report.Error=$_.Exception.ToString()}
finally{
    $journal=Join-Path $RunDirectory 'device-operation.json'
    if(Test-Path -LiteralPath $journal){
        try{
            $device=Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
            if($device.ModeChangeAttempted -and (-not $device.ModeRestored -or -not $device.RecoveryEffectAccepted)){
                if($device.RecoveryEffect -ne 'rainbow-wave'){throw 'Recovery effect is not established.'}
                if($device.OriginalMode.Count -ne 2 -or $device.OriginalMode[0] -ne 0 -or $device.OriginalMode[1] -ne 0){throw 'Recovery cannot establish original normal mode.'}
                if((Get-FileHash -LiteralPath $exe).Hash -ne $ExpectedProbeHash){throw 'Recovery probe hash changed.'}
                $report.RecoveryAttempted=$true
                $report.RecoveryExitCode=Invoke-Probe '--restore-rainbow' 'restore' 15
                $report.RecoveryResult=Get-Content (Join-Path $RunDirectory 'restore.stdout.txt') -Raw | ConvertFrom-Json
                if($report.RecoveryExitCode -ne 0 -or -not $report.RecoveryResult.normalModeConfirmed -or -not $report.RecoveryResult.rainbowCommandAccepted){throw 'Independent rainbow recovery failed.'}
            }
            $report.DeviceResult=$device
        }catch{$report.RecoveryError=$_.Exception.ToString();$report.Completed=$false}
    }
    foreach($name in @('Razer Chroma SDK Service','Razer Chroma SDK Server')){
        if(@($before | Where-Object {$_.Name -eq $name -and $_.State -eq 'Running'}).Count){
            $service=Get-Service $name
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
    Save-Report
}
if(-not $report.Completed){exit 2}
