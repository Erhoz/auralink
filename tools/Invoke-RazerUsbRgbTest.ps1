[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunDirectory,
      [ValidateRange(1,300)][int]$Seconds=180,
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedProbeHash,
      [switch]$RestoreRainbow,[switch]$RainbowBaseline,[switch]$NativeHal,
      [ValidateSet('Pipe','Log')][string]$FrameTransport='Pipe',[string]$ExpectedHalHash)
$ErrorActionPreference='Stop'
if(-not $RestoreRainbow -and -not $RainbowBaseline){throw 'Known rainbow baseline required.'}
$repo=Split-Path -Parent $PSScriptRoot
$root=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts\usb-rgb-test'))+'\'
$RunDirectory=[IO.Path]::GetFullPath($RunDirectory)
if(-not $RunDirectory.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Run directory must be inside the experiment artifact root.'}
$exe=Join-Path $repo 'artifacts\usb-investigation\AuraLink.RazerHidProbe.exe'
$names=@('Razer Chroma SDK Server','Razer Chroma SDK Service')
$before=@()
$nativeOwned=$false
$cacheBegun=$false
$nativeLog=$null
$lightingProcessId=0
$manifest=Join-Path $repo 'artifacts\diagnostic-hal\registration-Machine.json'
$cacheStage=Join-Path $repo 'artifacts\headphonestand-test'
$register=Join-Path $repo 'Register-DiagnosticHal.ps1'
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
function Invoke-CachePhase([string]$Phase){
    $engine=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $helper=Join-Path $PSScriptRoot 'Invoke-ArmouryCacheTest.ps1'
    # Existing helper snapshots ASUS/Corsair, refreshes both consumers and owns removal.
    & $engine -NoProfile -NonInteractive -File $helper -Action $Phase -Variant HeadphoneStand
    if($LASTEXITCODE -ne 0){throw "Native cache phase $Phase failed."}
    $phaseReport=Get-Content (Join-Path $cacheStage ($Phase+'.json')) -Raw | ConvertFrom-Json
    $report["Native$Phase"]=$phaseReport
    if(-not $phaseReport.Completed){throw "Native cache phase $Phase incomplete."}
}
try{
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator token required for SDK isolation.'}
    if((Get-FileHash -LiteralPath $exe).Hash -ne $ExpectedProbeHash){throw 'Probe hash changed.'}
    if(@(Get-Process RazerAppEngine -ErrorAction SilentlyContinue).Count){throw 'AppEngine must be closed.'}
    if($NativeHal){
        if($RestoreRainbow){throw 'Native HAL cannot run in recovery mode.'}
        if((Test-Path -LiteralPath $manifest) -or (Test-Path -LiteralPath (Join-Path $cacheStage 'session.json'))){throw 'Existing HAL registration/session; end it before a native test.'}
        if($FrameTransport -eq 'Pipe'){
            if($ExpectedHalHash -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Expected pipe HAL hash required.'}
            $engine=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $installer=Join-Path $PSScriptRoot 'Set-MachineDiagnostic.ps1'
            $nativeOwned=$true
            & $engine -NoProfile -NonInteractive -File $installer -Action Install -ExpectedAssemblyHash $ExpectedHalHash
            if($LASTEXITCODE -ne 0){throw 'Protected pipe HAL preparation failed.'}
            $preparedPath=Join-Path $repo 'artifacts\armoury-stage3\operation.json'
            $prepared=Get-Content $preparedPath -Raw | ConvertFrom-Json
            if(-not $prepared.Completed -or $prepared.AssemblyHash -ne $ExpectedHalHash){throw 'Protected pipe HAL preparation report mismatch.'}
            $legacy=Join-Path $cacheStage 'installation-log.json'
            if(-not(Test-Path -LiteralPath $legacy)){
                Copy-Item -LiteralPath (Join-Path $cacheStage 'installation.json') -Destination $legacy
            }
            Copy-Item -LiteralPath $preparedPath -Destination (Join-Path $cacheStage 'installation.json')
            $report.PipeHalPreparation=$prepared
        }
        elseif(Test-Path -LiteralPath (Join-Path $cacheStage 'installation-log.json')){
            Copy-Item -LiteralPath (Join-Path $cacheStage 'installation-log.json') -Destination (Join-Path $cacheStage 'installation.json')
        }
        $installation=Get-Content (Join-Path $cacheStage 'installation.json') -Raw | ConvertFrom-Json
        $dll=[IO.Path]::GetFullPath((Join-Path $installation.InstallDirectory 'AuraLink.DiagnosticHal.dll'))
        $protectedRoot=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'AuraLink\DiagnosticHal'))+'\'
        if(-not $installation.Completed -or -not $dll.StartsWith($protectedRoot,[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $dll).Hash -ne $installation.AssemblyHash){throw 'Previously validated protected HeadphoneStand HAL is required.'}
        foreach($name in @('ArmouryCrateService','LightingService')){if((Get-Service $name).Status -ne 'Running'){throw "$name must be running."}}
        $report.NativeInstallationHash=$installation.AssemblyHash
        $nativeOwned=$true
        if($FrameTransport -eq 'Log'){& $register -Action Install -Scope Machine -Discovery -AssemblyPath $dll -DeviceType HeadphoneStand | Out-Null}
        Invoke-CachePhase 'Begin'
        $cacheBegun=$true
        $lightingProcessId=(Get-CimInstance Win32_Service -Filter "Name='LightingService'").ProcessId
        $nativeLog=Join-Path (Join-Path (Split-Path -Parent $dll) 'logs') ('events-'+$lightingProcessId+'.jsonl')
        if($FrameTransport -eq 'Log' -and -not(Test-Path -LiteralPath $nativeLog)){throw 'Real LightingService did not create its HAL log.'}
        $report.NativeSource=@{Transport=$FrameTransport;ProcessId=$lightingProcessId;ColorOrder='AABBGGRR';SingleColorReplicatedToLeds=15}
        if($FrameTransport -eq 'Pipe'){$report.NativeSource.Endpoint='\\.\pipe\AuraLink.Frames.v1.'+$lightingProcessId}
        else{$report.NativeSource.Path=$nativeLog}
        Save-Report
    }
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
    if($NativeHal){$probeArguments='--native-from-rainbow "{0}" {1} "{2}" {3}' -f $RunDirectory,$Seconds,$nativeLog,$lightingProcessId}
    if($NativeHal -and $FrameTransport -eq 'Pipe'){$probeArguments='--pipe-from-rainbow "{0}" {1} {2}' -f $RunDirectory,$Seconds,$lightingProcessId}
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
    if($nativeOwned){
        try{
            if($cacheBegun -or ((Test-Path -LiteralPath (Join-Path $cacheStage 'session.json')) -and (Test-Path -LiteralPath $manifest))){
                Invoke-CachePhase 'End'
            }elseif(Test-Path -LiteralPath $manifest){
                & $register -Action Remove -Scope Machine | Out-Null
            }
            if(Test-Path -LiteralPath $manifest){throw 'Native HAL registration remains.'}
            $report.NativeRegistrationRemoved=$true
        }catch{$report.NativeCleanupError=$_.Exception.ToString();$report.Completed=$false}
    }
    $report.After=@(Get-CimInstance Win32_Service | Where-Object {$_.Name -in $names} | Select-Object Name,State,StartMode,ProcessId)
    $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
    Save-Report
}
if(-not $report.Completed){exit 2}
