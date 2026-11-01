[CmdletBinding()]
param(
    [switch]$Runtime,
    [ValidateRange(5,120)][int]$TimeoutSeconds=30,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'artifacts' }
if (-not [Environment]::Is64BitProcess) { throw 'Run in x64 PowerShell.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Import-Module (Join-Path $PSScriptRoot 'src\Core.psm1') -Force
$evidence = @()
foreach ($vendor in @('Asus','Corsair','Razer')) {
    Import-Module (Join-Path $PSScriptRoot "src\Adapters\$vendor.psm1") -Force
    try { $evidence += & "Get-${vendor}Evidence" }
    catch { $evidence += New-AuraLinkEvidence -Vendor $vendor -Hypothesis 'Local inspection' -Status Inconclusive -Data $_.Exception.Message }
}
$services = @(Get-Service -Name ArmouryCrateService,LightingService,CorsairDeviceControlService -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{Name=$_.Name; Status=[string]$_.Status} })
$software = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' | Get-ItemProperty | Where-Object { $_.DisplayName -match 'Razer|Chroma|Aura|Corsair|Armoury' } | Select-Object DisplayName,DisplayVersion)
$runtimeResults = @()
if ($Runtime) {
    # Each COM operation runs in a disposable child process. A blocked vendor call
    # cannot keep this command alive indefinitely. No service process is killed.
    # Global SDK enumeration was retired from this runner: it can leave separate
    # vendor COM servers alive after the client exits and disrupt DRAM discovery.
    foreach ($script in @('Read-CorsairHal')) {
        $scriptPath = Join-Path $PSScriptRoot "tools\$script.ps1"
        $jsonPath = Join-Path $OutputDirectory "$script.json"
        $stdout = Join-Path $OutputDirectory "$script.stdout.txt"
        $stderr = Join-Path $OutputDirectory "$script.stderr.txt"
        # Unique output prevents stale successful reports from surviving a timed-out run.
        if (Test-Path -LiteralPath $jsonPath) { Remove-Item -LiteralPath $jsonPath }
        $engine = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
        $arguments = '-NoProfile -NonInteractive -Mta -File "{0}" -OutputPath "{1}"' -f $scriptPath,$jsonPath
        $process = Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        try {
            $null = $process.Handle # Preserve exit status in Windows PowerShell 5.1.
            $finished = $process.WaitForExit($TimeoutSeconds * 1000)
            if (-not $finished) { $process.Kill(); $process.WaitForExit() }
            $process.Refresh()
            $state = if (-not $finished) {'Timeout'} elseif ($process.ExitCode -eq 0) {'Completed'} else {'Failed'}
            $runtimeResults += [pscustomobject]@{ Probe=$script; State=$state; ExitCode=$process.ExitCode; ReportExists=(Test-Path -LiteralPath $jsonPath) }
        } finally {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
    }
}
$report = [ordered]@{
    SchemaVersion=1; TimestampUtc=[DateTime]::UtcNow.ToString('o'); PowerShell=$PSVersionTable.PSVersion.ToString()
    ProcessBits=64; CalledSwitchMode=$false
    # A vendor COM constructor can acquire backend control without SwitchMode.
    # The Corsair service logs observed during investigation establish this risk.
    AcquiredLightingControl=$(if($Runtime){$null}else{$false})
    VendorRuntimeSideEffectsPossible=[bool]$Runtime; NativeIntegrationProven=$false
    GlobalProviderScanAttempted=$false
    ServicesBefore=$services
    ServicesAfter=@(Get-Service -Name ArmouryCrateService,LightingService,CorsairDeviceControlService -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{Name=$_.Name; Status=[string]$_.Status} })
    Software=$software; Evidence=$evidence; Runtime=$runtimeResults
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'inventory.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 12
if (@($runtimeResults | Where-Object { $_.State -ne 'Completed' }).Count) { exit 2 }
