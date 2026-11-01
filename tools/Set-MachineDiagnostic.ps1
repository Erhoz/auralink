[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Install','Remove','RemoveAndRestart','RestartLighting','Inspect','RepairRegistration','UpdateAndRestart')][string]$Action,
      [string]$ExpectedAssemblyHash,[string]$ExpectedProbeHash)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $repo 'artifacts\armoury-stage3'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$report=[ordered]@{StartedUtc=[DateTime]::UtcNow.ToString('o');Action=$Action;Completed=$false;Error=$null;ServicesRestarted=$false;HardwareOutput=$false}
$installedHere=$false
$registrationScript=Join-Path $repo 'Register-DiagnosticHal.ps1'
function Protect-AuraLinkDirectory([string]$Path){
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $admins=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $system=New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
    $users=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
    $acl.SetAccessRuleProtection($true,$false)
    $acl.SetOwner($admins)
    foreach($sid in @($admins,$system)){
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
    }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
    Set-Acl -LiteralPath $Path -AclObject $acl
}
function Restart-TrackedLighting {
    param([bool]$RequireRegistration=$true)
    if($RequireRegistration -and -not(Test-Path -LiteralPath (Join-Path $repo 'artifacts\diagnostic-hal\registration-Machine.json'))){throw 'No tracked machine diagnostic registration.'}
    $service=Get-Service -Name LightingService
    if($service.Status -ne 'Running'){throw 'LightingService was not running; refusing restart operation.'}
    $report.ServiceStateBefore=[string]$service.Status
    try{
        $service.Stop()
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(20))
        $service.Start()
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(30))
        $report.ServicesRestarted=$true
    }catch{
        if(Test-Path -LiteralPath (Join-Path $repo 'artifacts\diagnostic-hal\registration-Machine.json')){try{& $registrationScript -Action Remove -Scope Machine | Out-Null;$report.RolledBackRegistry=$true}catch{$report.RollbackError=$_.Exception.Message}}
        $service.Refresh()
        if($service.Status -eq 'Stopped'){$service.Start();$service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(30))}
        throw
    }finally{$service.Refresh();$report.ServiceStateAfter=[string]$service.Status;$service.Dispose()}
}
try{
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator token required.'}
    if($Action -eq 'Inspect'){
        $service=Get-CimInstance Win32_Service -Filter "Name='LightingService'"
        $report.ServiceProcessId=$service.ProcessId
        $report.RuntimeModules=@((Get-Process -Id $service.ProcessId).Modules | Where-Object {$_.ModuleName -match 'clr|mscor|AuraLink|AuraSdk'} | Select-Object ModuleName,FileName)
        $installation=Get-Content -LiteralPath (Join-Path $stage 'installation.json') -Raw | ConvertFrom-Json
        $probe=Join-Path $stage 'AuraLink.FactoryProbe.x86.exe'
        $actual=(Get-FileHash -LiteralPath $probe).Hash
        if(-not $ExpectedProbeHash -or $actual -ne $ExpectedProbeHash){throw 'Factory probe hash changed.'}
        $target=Join-Path $installation.InstallDirectory ('FactoryProbe-'+$actual.Substring(0,16)+'.exe')
        if(-not(Test-Path -LiteralPath $target)){
            Copy-Item -LiteralPath $probe -Destination $target
            $acl=Get-Acl -LiteralPath $target;$acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')));Set-Acl -LiteralPath $target -AclObject $acl
        }
        if((Get-FileHash -LiteralPath $target).Hash -ne $actual){throw 'Protected factory probe hash mismatch.'}
        $process=Start-Process -FilePath $target -ArgumentList factory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stage 'factory.json') -RedirectStandardError (Join-Path $stage 'factory.stderr.txt')
        $null=$process.Handle
        try{
            if(-not $process.WaitForExit(15000)){$process.Kill();$process.WaitForExit();throw 'Factory probe timed out.'}
            $process.Refresh();$report.ProbeExitCode=$process.ExitCode
        }finally{if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
        $report.FactoryResult=Get-Content -LiteralPath (Join-Path $stage 'factory.json') -Raw | ConvertFrom-Json
        $report.Completed=$true
    }elseif($Action -eq 'RestartLighting'){
        Restart-TrackedLighting
        $report.Completed=$true
    }elseif($Action -eq 'RepairRegistration'){
        $installation=Get-Content -LiteralPath (Join-Path $stage 'installation.json') -Raw | ConvertFrom-Json
        $protectedDll=Join-Path $installation.InstallDirectory 'AuraLink.DiagnosticHal.dll'
        if((Get-FileHash -LiteralPath $protectedDll).Hash -ne $installation.AssemblyHash){throw 'Protected assembly changed; refusing registration repair.'}
        & $registrationScript -Action Remove -Scope Machine | Out-Null
        & $registrationScript -Action Install -Scope Machine -Discovery -AssemblyPath $protectedDll | Out-Null
        $installedHere=$true
        $report.RegistrationRepaired=$true
        Restart-TrackedLighting
        $report.Completed=$true
    }elseif($Action -in @('Remove','RemoveAndRestart')){
        & $registrationScript -Action Remove -Scope Machine | Out-Null
        $report.Completed=$true
        $report.RegistryRemoved=$true
        $report.ProtectedFilesRetainedForLogs=$true
        if($Action -eq 'RemoveAndRestart'){Restart-TrackedLighting -RequireRegistration:$false}
    }else{
        $source=Join-Path $repo 'artifacts\diagnostic-hal'
        $dll=Join-Path $source 'AuraLink.DiagnosticHal.dll'
        $actual=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
        if(-not $ExpectedAssemblyHash -or $actual -ne $ExpectedAssemblyHash){throw 'Assembly hash changed since elevation request.'}
        $root=Join-Path $env:ProgramFiles 'AuraLink\DiagnosticHal'
        $marker=Join-Path $root 'auralink-owner.json'
        $owner='AuraLink.DiagnosticHal/0.1'
        if(Test-Path -LiteralPath $root){
            if(-not (Test-Path -LiteralPath $marker)){throw 'Existing unowned installation directory.'}
            $existing=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
            if($existing.Owner -ne $owner){throw 'Installation ownership mismatch.'}
        }else{
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            Protect-AuraLinkDirectory $root
            @{Owner=$owner;ClassId='9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6'} | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
        }
        Protect-AuraLinkDirectory $root
        $version=Join-Path $root ('v0.1-'+$actual.Substring(0,16).ToLowerInvariant())
        if(Test-Path -LiteralPath $version){throw 'Version directory already exists; refusing to overwrite a possibly loaded DLL.'}
        New-Item -ItemType Directory -Path $version | Out-Null
        foreach($name in @('AuraLink.DiagnosticHal.dll','AuraLink.HalClient.x64.exe','AuraLink.HalClient.x86.exe')){
            $from=Join-Path $source $name
            $to=Join-Path $version $name
            Copy-Item -LiteralPath $from -Destination $to
            if((Get-FileHash -LiteralPath $from).Hash -ne (Get-FileHash -LiteralPath $to).Hash){throw 'Copied binary hash mismatch.'}
            $acl=Get-Acl -LiteralPath $to
            $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
            Set-Acl -LiteralPath $to -AclObject $acl
        }
        $acl=Get-Acl -LiteralPath $version
        $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
        Set-Acl -LiteralPath $version -AclObject $acl
        $protectedDll=Join-Path $version 'AuraLink.DiagnosticHal.dll'
        if($Action -eq 'UpdateAndRestart'){
            & $registrationScript -Action Remove -Scope Machine | Out-Null
            $report.PreviousRegistrationRemoved=$true
        }
        & $registrationScript -Action Install -Scope Machine -Discovery -AssemblyPath $protectedDll | Out-Null
        $installedHere=$true
        $checks=@()
        foreach($architecture in @('x64','x86')){
            $exe=Join-Path $version "AuraLink.HalClient.$architecture.exe"
            $stdout=Join-Path $stage "machine-native-$architecture.json"
            $stderr=Join-Path $stage "machine-native-$architecture.stderr.txt"
            $process=Start-Process -FilePath $exe -ArgumentList native -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $null=$process.Handle
            try{
                if(-not $process.WaitForExit(15000)){$process.Kill();$process.WaitForExit();throw 'Machine COM activation test timed out.'}
                $process.Refresh()
                if($process.ExitCode -ne 0){throw "Machine COM test failed ($architecture); see $stderr"}
            }finally{if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
            $checks += Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
        }
        $report.Completed=$true
        $report.InstallDirectory=$version
        $report.LogDirectory=Join-Path $version 'logs'
        $report.AssemblyHash=$actual
        $report.MachineActivationTests=$checks
        $report.DiscoveryRegistered=$true
        $report.ArmouryUiValidated=$false
        if($Action -eq 'UpdateAndRestart'){Restart-TrackedLighting}
    }
}catch{
    $report.Completed=$false
    $report.Error=$_.Exception.ToString()
    if($installedHere -and (Test-Path -LiteralPath (Join-Path $repo 'artifacts\diagnostic-hal\registration-Machine.json'))){
        try{& $registrationScript -Action Remove -Scope Machine | Out-Null;$report.RolledBackRegistry=$true}
        catch{$report.RollbackError=$_.Exception.ToString()}
    }
}
$report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $stage 'operation.json') -Encoding UTF8
if(-not $report.Completed){exit 2}
