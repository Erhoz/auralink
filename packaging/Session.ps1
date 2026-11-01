[CmdletBinding()]
param([ValidateSet('Run','Recover')][string]$Action='Run',[int]$OwnerPid=0)
$ErrorActionPreference='Stop'
$app=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$expected=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'AuraLink\App'))
if($app -ne $expected){throw 'Instale o AuraLink antes de iniciar uma sessao.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'A sessao precisa da confirmacao de administrador.'}
$data=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'AuraLink\Runtime'
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
$acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
foreach($sid in @('S-1-5-18','S-1-5-32-544')){$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)),'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')),'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
foreach($directory in @((Split-Path -Parent $data),$data)){
    if(Test-Path -LiteralPath $directory){
        if(((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Diretorio de dados redirecionado; operacao recusada.'}
        $owner=(Get-Acl -LiteralPath $directory).GetOwner([Security.Principal.SecurityIdentifier]).Value
        if($owner -notin @('S-1-5-32-544','S-1-5-18')){throw 'Diretorio de dados preexistente de outro proprietario.'}
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Set-Acl -LiteralPath $directory -AclObject $acl
}
$mutex=New-Object Threading.Mutex($false,'Global\AuraLink.ManualSession.v1')
$locked=$false
try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
if(-not $locked){$mutex.Dispose();throw 'Ja existe uma sessao AuraLink em execucao.'}
$active=Join-Path $data 'active.json'
$last=Join-Path $data 'last-session.json'
$registration=Join-Path $data 'registration'
$register=Join-Path $PSScriptRoot 'Register-DiagnosticHal.ps1'
$workerPath=Join-Path $app 'bin\AuraLink.Worker.exe'
$worker=$null
$state=$null
$ownerStart=$null
function Save-State {
    [IO.File]::WriteAllText($active,($script:state | ConvertTo-Json -Depth 14),(New-Object Text.UTF8Encoding($false)))
}
function Ensure-Running([string]$Name){
    $service=Get-Service -Name $Name
    try{
        if($service.Status -eq 'StopPending'){$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
        $service.Refresh()
        if($service.Status -eq 'Stopped'){$service.Start()}
        $service.WaitForStatus('Running',[TimeSpan]::FromSeconds(40))
    }finally{$service.Dispose()}
}
function Stop-Tracked([string]$Name){
    $service=Get-Service -Name $Name
    try{
        if(@($service.DependentServices | Where-Object Status -ne Stopped).Count){throw "Dependentes ativos de $Name; parada recusada."}
        if($service.Status -eq 'Running'){$service.Stop();$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
        elseif($service.Status -ne 'Stopped'){throw "Estado inesperado de $Name."}
    }finally{$service.Dispose()}
}
function Stop-Asus {
    $script:state.AsusTouched=$true;Save-State
    Stop-Tracked 'ArmouryCrateService';Stop-Tracked 'LightingService'
}
function Start-Asus {Ensure-Running 'LightingService';Ensure-Running 'ArmouryCrateService'}
function Invoke-Recovery {
    $out=Join-Path $script:state.RunDirectory 'restore.stdout.txt'
    $err=Join-Path $script:state.RunDirectory 'restore.stderr.txt'
    $p=Start-Process -FilePath $workerPath -ArgumentList '--restore-rainbow' -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    $null=$p.Handle
    try{
        if(-not $p.WaitForExit(15000)){$p.Kill();$p.WaitForExit();throw 'Recuperacao USB excedeu o limite.'}
        $p.Refresh();if($p.ExitCode -ne 0){throw 'Nao foi possivel recuperar a iluminacao USB; reconecte a base e use Recuperar.'}
    }finally{if(-not $p.HasExited){$p.Kill();$p.WaitForExit()};$p.Dispose()}
}
function Validate-State($Value){
    if($Value.Owner -ne 'AuraLink.ReleaseSession/v1' -or $Value.AppRoot -ne $app){throw 'Estado de sessao nao pertence a esta instalacao.'}
    $sessionRoot=[IO.Path]::GetFullPath((Join-Path $data 'sessions'))+'\'
    if(-not ([IO.Path]::GetFullPath($Value.RunDirectory)).StartsWith($sessionRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Diretorio da sessao invalido.'}
}
try{
    if($Action -eq 'Recover' -and -not(Test-Path -LiteralPath $active) -and (Test-Path -LiteralPath (Join-Path $registration 'registration-Machine.json'))){
        if(-not(Test-Path -LiteralPath $last)){throw 'Registro pendente sem historico de recuperacao. Consulte o suporte do AuraLink.'}
        $saved=Get-Content -LiteralPath $last -Raw | ConvertFrom-Json;Validate-State $saved
        Copy-Item -LiteralPath $last -Destination $active
    }
    if(Test-Path -LiteralPath $active){
        if($Action -ne 'Recover'){throw 'Uma sessao anterior precisa de recuperacao. Use Recuperar.'}
        $loaded=Get-Content -LiteralPath $active -Raw | ConvertFrom-Json
        Validate-State $loaded
        $state=[ordered]@{};foreach($property in $loaded.PSObject.Properties){$state[$property.Name]=$property.Value}
        $state.CleanupErrors=@()
        $state.RecoveryBefore=@(Get-CimInstance Win32_Service | Where-Object Name -in @('Razer Chroma SDK Server','Razer Chroma SDK Service') | Select-Object Name,State,StartMode)
        foreach($snapshot in $state.RecoveryBefore){if($snapshot.State -eq 'Running'){Stop-Tracked $snapshot.Name}}
        if($state.WorkerPid){
            $old=Get-CimInstance Win32_Process -Filter "ProcessId=$($state.WorkerPid)"
            if($old){
                if($old.ExecutablePath -ne $workerPath -or $old.CommandLine -notlike ('*'+$state.RunDirectory+'*')){throw 'Identidade do processo anterior divergente; recuperacao recusada.'}
                New-Item -ItemType File -Path (Join-Path $state.RunDirectory 'stop-test') -Force | Out-Null
                $p=Get-Process -Id $state.WorkerPid
                try{if(-not $p.WaitForExit(20000)){$p.Kill();$p.WaitForExit()}}finally{$p.Dispose()}
            }
        }
    }elseif($Action -eq 'Recover'){
        Write-Output 'Nenhuma sessao pendente.'
        exit 0
    }else{
        $package=Get-Content (Join-Path $app 'payload.manifest.json') -Raw | ConvertFrom-Json
        foreach($item in $package.files.PSObject.Properties){
            $path=[IO.Path]::GetFullPath((Join-Path $app $item.Name))
            if(-not $path.StartsWith($app+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Value){throw 'Arquivo instalado alterado ou ausente. Reinstale o AuraLink.'}
        }
        if(@(Get-Process RazerAppEngine -ErrorAction SilentlyContinue).Count){throw 'Feche o Razer Chroma, inclusive na bandeja, antes de iniciar.'}
        if(Test-Path -LiteralPath (Join-Path $registration 'registration-Machine.json')){throw 'Registro AuraLink anterior precisa de recuperacao.'}
        foreach($name in @('LightingService','ArmouryCrateService')){if((Get-Service $name).Status -ne 'Running'){throw "Inicie o Armoury Crate: $name nao esta ativo."}}
        $run=Join-Path (Join-Path $data 'sessions') ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $run -Force | Out-Null
        $before=@(Get-CimInstance Win32_Service | Where-Object Name -in @('LightingService','ArmouryCrateService','CorsairDeviceControlService','Razer Chroma SDK Server','Razer Chroma SDK Service') | Select-Object Name,State,StartMode,ProcessId)
        $state=[ordered]@{Owner='AuraLink.ReleaseSession/v1';AppRoot=$app;Version=$package.version;RunDirectory=$run;StartedUtc=[DateTime]::UtcNow.ToString('o');Completed=$false;Error=$null;CleanupErrors=@();Before=$before;StoppedSdk=@();AsusTouched=$false;RegistryAttempted=$false;WorkerPid=0;WorkerExitCode=$null}
        Save-State
        $auraProfilePath='C:\Program Files (x86)\LightingService\LastProfile.xml'
        if(Test-Path -LiteralPath $auraProfilePath){Copy-Item -LiteralPath $auraProfilePath -Destination (Join-Path $run 'LastProfile-before.xml')}
        if(Test-Path -LiteralPath (Join-Path $registration 'registration-Machine.json')){throw 'Registro AuraLink anterior precisa de recuperacao.'}
        Stop-Asus
        $state.RegistryAttempted=$true;Save-State
        & $register -Action Install -Scope Machine -Discovery -AssemblyPath (Join-Path $app 'bin\AuraLink.DiagnosticHal.dll') -StateDirectory $registration -ProductVersion '1.0.0' -DisplayName 'AuraLink' | Out-Null
        Start-Asus
        foreach($name in @('Razer Chroma SDK Server','Razer Chroma SDK Service')){
            if(@($before | Where-Object {$_.Name -eq $name -and $_.State -eq 'Running'}).Count){$state.StoppedSdk+= $name;Save-State;Stop-Tracked $name}
        }
        $lightingPid=(Get-CimInstance Win32_Service -Filter "Name='LightingService'").ProcessId
        $state.LightingServicePid=$lightingPid
        if($OwnerPid){$ownerStart=(Get-Process -Id $OwnerPid).StartTime.ToUniversalTime()}
        $arguments='--run "{0}" {1}' -f $run,$lightingPid
        $worker=Start-Process -FilePath $workerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'worker.stdout.txt') -RedirectStandardError (Join-Path $run 'worker.stderr.txt')
        $null=$worker.Handle;$state.WorkerPid=$worker.Id;Save-State
        $stopping=$null
        while(-not $worker.WaitForExit(250)){
            if($OwnerPid){
                $owner=Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue
                if(-not $owner -or $owner.StartTime.ToUniversalTime() -ne $ownerStart){New-Item -ItemType File -Path (Join-Path $run 'stop-test') -Force | Out-Null}
                if($owner){$owner.Dispose()}
            }
            if(Test-Path -LiteralPath (Join-Path $run 'stop-test')){
                if(-not $stopping){$stopping=[Diagnostics.Stopwatch]::StartNew()}
                if($stopping.Elapsed.TotalSeconds -gt 20){throw 'O processo de iluminacao nao encerrou no limite.'}
            }
        }
        $worker.Refresh();$state.WorkerExitCode=$worker.ExitCode
        if($worker.ExitCode -ne 0){throw 'A sessao foi interrompida. Consulte o relatorio e use Recuperar se necessario.'}
        $state.Completed=$true
    }
}catch{
    if($state){$state.Error=$_.Exception.Message;$state.Completed=$false}else{Write-Error $_ -ErrorAction Continue}
}finally{
    if($state){
        try{
            if($worker -and -not $worker.HasExited){
                New-Item -ItemType File -Path (Join-Path $state.RunDirectory 'stop-test') -Force | Out-Null
                if(-not $worker.WaitForExit(20000)){$worker.Kill();$worker.WaitForExit()}
            }
            $journal=Join-Path $state.RunDirectory 'device-operation.json'
            if(Test-Path -LiteralPath $journal){
                $device=Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
                if($device.ModeChangeAttempted -and (-not $device.ModeRestored -or -not $device.RecoveryEffectAccepted)){
                    if($device.OriginalMode[0] -ne 0 -or $device.OriginalMode[1] -ne 0){throw 'Modo original desconhecido; recuperacao USB recusada.'}
                    Invoke-Recovery
                }
            }
        }catch{$state.CleanupErrors+= $_.Exception.Message}
        foreach($name in @('Razer Chroma SDK Service','Razer Chroma SDK Server')){
            if(@($state.Before | Where-Object {$_.Name -eq $name -and $_.State -eq 'Running'}).Count -or @($state.RecoveryBefore | Where-Object {$_.Name -eq $name -and $_.State -eq 'Running'}).Count){try{Ensure-Running $name}catch{$state.CleanupErrors+= $_.Exception.Message}}
        }
        if($state.AsusTouched){
            try{
                $ownedManifest=Join-Path $registration 'registration-Machine.json'
                if(Test-Path -LiteralPath $ownedManifest){Stop-Asus;& $register -Action Remove -Scope Machine -StateDirectory $registration | Out-Null}
            }catch{$state.CleanupErrors+= $_.Exception.Message}
            try{Start-Asus}catch{$state.CleanupErrors+= $_.Exception.Message}
        }
        if($Action -eq 'Recover' -and $state.CleanupErrors.Count -eq 0){$state.Completed=$true;$state.Error=$null}
        if($state.CleanupErrors.Count){$state.Completed=$false}
        $state.FinishedUtc=[DateTime]::UtcNow.ToString('o');Save-State
        Copy-Item -LiteralPath $active -Destination $last -Force
        if($state.CleanupErrors.Count -eq 0){Remove-Item -LiteralPath $active}
    }
    if($worker){$worker.Dispose()}
    if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()
}
if(-not $state -or -not $state.Completed){exit 2}
