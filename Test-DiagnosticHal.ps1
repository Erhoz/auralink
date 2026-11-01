[CmdletBinding()]
param([switch]$IncludeAsusSdk,[ValidateRange(5,60)][int]$TimeoutSeconds=15)
$ErrorActionPreference='Stop'
$output=Join-Path $PSScriptRoot 'artifacts\diagnostic-tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$registered=$false
$tests=@()
$failure=$null
$cleanupFailure=$null
$collisionRefused=$false
$foreignOwnerRefused=$false
try{
    & (Join-Path $PSScriptRoot 'Build-DiagnosticHal.ps1') | Out-Null
    & (Join-Path $PSScriptRoot 'Register-DiagnosticHal.ps1') -Action Install -Scope User | Out-Null
    $registered=$true
    try{& (Join-Path $PSScriptRoot 'Register-DiagnosticHal.ps1') -Action Install -Scope User | Out-Null}
    catch{$collisionRefused=$true}
    if(-not $collisionRefused){throw 'Existing registration was overwritten.'}
    # Exercise removal refusal in the second registry view. The first view must
    # remain intact, proving that all targets are checked before deletion.
    $guardRoot=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry32)
    $guardKey=$guardRoot.OpenSubKey('Software\Classes\CLSID\{9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6}',$true)
    try{
        $guardKey.SetValue('AuraLinkOwner','ForeignOwnerForTest')
        try{& (Join-Path $PSScriptRoot 'Register-DiagnosticHal.ps1') -Action Remove -Scope User | Out-Null}
        catch{$foreignOwnerRefused=$true}
        if(-not $foreignOwnerRefused){throw 'Foreign registration was removed.'}
        $checkRoot=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
        try{
            $checkKey=$checkRoot.OpenSubKey('Software\Classes\CLSID\{9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6}')
            if($null -eq $checkKey){throw 'Removal partially deleted the first registry view.'}
            $checkKey.Dispose()
        }finally{$checkRoot.Dispose()}
    }finally{
        $guardKey.SetValue('AuraLinkOwner','AuraLink.DiagnosticHal/0.1');$guardKey.Dispose();$guardRoot.Dispose()
    }
    foreach($architecture in @('x64','x86')){
        $modes=@('native')
        if($IncludeAsusSdk){$modes += 'sdk'}
        foreach($mode in $modes){
            $exe=Join-Path $PSScriptRoot "artifacts\diagnostic-hal\AuraLink.HalClient.$architecture.exe"
            $stdout=Join-Path $output "$architecture-$mode.json"
            $stderr=Join-Path $output "$architecture-$mode.stderr.txt"
            $arguments=if($mode -eq 'sdk'){'sdk'}else{'native'}
            $started=[DateTime]::UtcNow
            $process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $null=$process.Handle
            $probePid=$process.Id
            try{
                if(-not $process.WaitForExit($TimeoutSeconds*1000)){
                    $process.Kill();$process.WaitForExit();throw "$architecture/$mode timed out"
                }
                $process.Refresh()
                if($process.ExitCode -ne 0){throw "$architecture/$mode failed; see $stderr"}
            }finally{
                if(-not $process.HasExited){$process.Kill();$process.WaitForExit()}
                $process.Dispose()
            }
            $result=Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
            $log=Join-Path $PSScriptRoot "artifacts\diagnostic-hal\logs\events-$probePid.jsonl"
            $events=@(Get-Content -LiteralPath $log | ForEach-Object {ConvertFrom-Json $_} | Where-Object {[DateTime]::Parse($_.timestampUtc).ToUniversalTime() -ge $started})
            $effects=@($events | Where-Object operation -eq 'SetEffect')
            if($mode -eq 'native'){
                if(-not $result.passed){throw 'Native assertions did not pass.'}
                if($effects.Count -ne 1 -or $effects[0].data.effectId -ne 0 -or $effects[0].data.colors.Count -ne 1 -or $effects[0].data.colors[0] -ne 0x00112233){throw 'Native effect log mismatch.'}
                $sync=@($events | Where-Object operation -eq 'Synchronize')
                if($sync.Count -ne 1 -or $sync[0].data.milliseconds -ne 4294967297){throw '64-bit synchronization value was lost.'}
            }elseif(-not $result.metadataPassed){throw 'SDK metadata assertions did not pass.'}
            $tests += [pscustomobject]@{Architecture=$architecture;Mode=$mode;Result=$result;SetEffectCallbacks=$effects.Count;LogFile=$log}
        }
    }
}catch{$failure=$_.Exception.Message}
finally{
    if($registered){
        try{& (Join-Path $PSScriptRoot 'Register-DiagnosticHal.ps1') -Action Remove -Scope User | Out-Null}
        catch{$cleanupFailure=$_.Exception.Message}
    }
}
$removed=$registered -and -not $cleanupFailure
if($removed){
    foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
        $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
        try{
            $key=$root.OpenSubKey('Software\Classes\CLSID\{9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6}')
            if($null -ne $key){$key.Dispose();$removed=$false;$cleanupFailure='User COM registration remains.'}
        }finally{$root.Dispose()}
    }
}
$report=[ordered]@{
    TimestampUtc=[DateTime]::UtcNow.ToString('o');Tests=$tests;CollisionRefused=$collisionRefused;ForeignOwnerRemovalRefused=$foreignOwnerRefused
    TemporaryRegistrationRemoved=$removed;Failure=$failure;CleanupFailure=$cleanupFailure
    MachineRegistrationAttempted=$false;DiscoveryRegistrationAttempted=$false
    HardwareOutput=$false;ArmouryUiValidated=$false;NativeIntegrationProven=$false
}
$json=$report | ConvertTo-Json -Depth 12
$json | Set-Content -LiteralPath (Join-Path $output 'report.json') -Encoding UTF8
$json
if($failure -or $cleanupFailure){exit 2}
