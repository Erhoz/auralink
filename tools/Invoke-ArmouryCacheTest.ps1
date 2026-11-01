[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Begin','End')][string]$Action,
      [ValidateSet('Headset','HeadphoneStand')][string]$Variant='Headset')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $repo 'artifacts\armoury-cache-test'
if($Variant -eq 'HeadphoneStand'){$stage=Join-Path $repo 'artifacts\headphonestand-test'}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$manifest=Join-Path $repo 'artifacts\diagnostic-hal\registration-Machine.json'
$register=Join-Path $repo 'Register-DiagnosticHal.ps1'
$sessionPath=Join-Path $stage 'session.json'
$reportPath=Join-Path $stage ($Action+'.json')
$expectedHash='DECDD07B43EAF69785A0199BE9712D76E44A7C3215F0E5CD224C72B356ACE41B'
$dll=Join-Path $env:ProgramFiles 'AuraLink\DiagnosticHal\v0.1-decdd07b43eaf697\AuraLink.DiagnosticHal.dll'
$report=[ordered]@{Action=$Action;Variant=$Variant;StartedUtc=[DateTime]::UtcNow.ToString('o');Completed=$false;Error=$null;Events=@();HardwareOutput=$false;NativeIntegrationProven=$false}
$touched=$false
$installedHere=$false
function Save-Report {
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
function Write-Event([string]$Operation){
    $report.Events += [pscustomobject]@{Utc=[DateTime]::UtcNow.ToString('o');Operation=$Operation}
    Save-Report
}
function Get-ServiceSnapshot {
    @(Get-CimInstance Win32_Service -Filter "Name='ArmouryCrateService' OR Name='LightingService' OR Name='CorsairDeviceControlService'" | Select-Object Name,State,StartMode,ProcessId)
}
function Stop-Tracked([string]$Name){
    $service=Get-Service -Name $Name
    try{
        if(@($service.DependentServices | Where-Object Status -ne Stopped).Count){throw "Running dependents of $Name; refusing forced stop."}
        if($service.Status -ne 'Running'){throw "$Name is not running."}
        $script:touched=$true
        Write-Event "Stopping $Name"
        $service.Stop()
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(25))
        Write-Event "Stopped $Name"
    }finally{$service.Dispose()}
}
function Ensure-Running([string]$Name){
    $service=Get-Service -Name $Name
    try{
        $service.Refresh()
        if($service.Status -eq 'StopPending'){$service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(25))}
        $service.Refresh()
        if($service.Status -eq 'Stopped'){$service.Start()}
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(40))
        Write-Event "Running $Name"
    }finally{$service.Dispose()}
}
try{
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator token required.'}
    if($Variant -eq 'HeadphoneStand'){
        $installation=Get-Content -LiteralPath (Join-Path $stage 'installation.json') -Raw | ConvertFrom-Json
        if(-not $installation.Completed -or $installation.Action -ne 'Install'){throw 'Expected a completed protected installation.'}
        $dll=[IO.Path]::GetFullPath((Join-Path $installation.InstallDirectory 'AuraLink.DiagnosticHal.dll'))
        $protectedRoot=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'AuraLink\DiagnosticHal'))+'\'
        if(-not $dll.StartsWith($protectedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Assembly is outside the protected AuraLink directory.'}
        $expectedHash=$installation.AssemblyHash
        if((Get-FileHash -LiteralPath $dll).Hash -ne $expectedHash){throw 'Protected assembly hash mismatch.'}
    }
    $report.Before=Get-ServiceSnapshot
    if($Action -eq 'Begin'){
        if(Test-Path -LiteralPath $sessionPath){throw 'An experiment session already exists; end it before another Begin.'}
        if($Variant -eq 'Headset' -and (Test-Path -LiteralPath $manifest)){throw 'Existing tracked registration; refusing to replace it.'}
        if($Variant -eq 'HeadphoneStand'){
            $tracked=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
            if($tracked.Owner -ne 'AuraLink.DiagnosticHal/0.1' -or ([Uri]$tracked.CodeBase).LocalPath -ne $dll){throw 'Registration does not match this protected installation.'}
        }
        foreach($name in @('ArmouryCrateService','LightingService')){
            if((Get-Service -Name $name).Status -ne 'Running'){throw "$name must be running before the experiment."}
        }
        if((Get-FileHash -LiteralPath $dll).Hash -ne $expectedHash){throw 'The protected previous HAL differs; refusing to change the tested binary.'}
        $report.AssemblyPath=$dll
        $report.AssemblyHash=$expectedHash
        $report.ProfileSnapshot=Join-Path $stage 'LastProfile-before.xml'
        Copy-Item -LiteralPath 'C:\Program Files (x86)\LightingService\LastProfile.xml' -Destination $report.ProfileSnapshot
        $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $sessionPath -Encoding UTF8
    }else{
        if(-not(Test-Path -LiteralPath $sessionPath)){throw 'No recorded cache experiment to end.'}
        $session=Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
        if($session.AssemblyPath -ne $dll -or $session.AssemblyHash -ne $expectedHash){throw 'Experiment identity mismatch.'}
        if(-not(Test-Path -LiteralPath $manifest)){throw 'Tracked registration is missing; inspect recovery before restarting services.'}
    }
    Stop-Tracked 'ArmouryCrateService'
    Stop-Tracked 'LightingService'
    if($Action -eq 'Begin'){
        if($Variant -eq 'Headset'){& $register -Action Install -Scope Machine -Discovery -AssemblyPath $dll -DeviceType Headset | Out-Null}
        $installedHere=$true
        Write-Event ('Using protected diagnostic HAL: '+$Variant)
    }else{
        & $register -Action Remove -Scope Machine | Out-Null
        Write-Event 'Removed diagnostic HAL registration'
    }
    Ensure-Running 'LightingService'
    Ensure-Running 'ArmouryCrateService'
    $report.Completed=$true
}catch{
    $report.Error=$_.Exception.ToString()
    # Recovery removes only the registration owned by this experiment.
    if($installedHere -or ($Action -eq 'End' -and $touched)){
        # An already restarted LightingService may hold the HAL even after its
        # registry entry is removed. Stop both consumers before rollback too.
        foreach($name in @('ArmouryCrateService','LightingService')){
            try{if((Get-Service -Name $name).Status -eq 'Running'){Stop-Tracked $name}}
            catch{$report["RecoveryStopError_$name"]=$_.Exception.ToString()}
        }
        if(Test-Path -LiteralPath $manifest){
            try{& $register -Action Remove -Scope Machine | Out-Null;Write-Event 'Recovery removed own registration'}
            catch{$report.RegistryRecoveryError=$_.Exception.ToString()}
        }
    }
    if($touched){
        foreach($name in @('LightingService','ArmouryCrateService')){
            try{Ensure-Running $name}catch{$report["RecoveryError_$name"]=$_.Exception.ToString()}
        }
    }
}finally{
    $report.After=Get-ServiceSnapshot
    $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
    Save-Report
    if($Action -eq 'End' -and $report.Completed){
        Copy-Item -LiteralPath $sessionPath -Destination (Join-Path $stage 'session-ended.json')
        Remove-Item -LiteralPath $sessionPath
    }
    if($Action -eq 'Begin' -and -not $report.Completed -and (Test-Path -LiteralPath $sessionPath) -and -not(Test-Path -LiteralPath $manifest)){
        Copy-Item -LiteralPath $sessionPath -Destination (Join-Path $stage 'session-failed.json')
        Remove-Item -LiteralPath $sessionPath
    }
}
if(-not $report.Completed){exit 2}
