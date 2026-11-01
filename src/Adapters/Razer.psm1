function Get-RazerEvidence {
    # PresentOnly avoids treating stale USB registry entries as connected hardware.
    $devices = @(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object { $_.InstanceId -match '(?i)VID_1532&PID_0F08(?:&|\\)' } | ForEach-Object {
        [pscustomobject]@{ Name=$_.FriendlyName; Class=$_.Class; Status=[string]$_.Status }
    })
    $dlls = @("$env:WINDIR\System32\RzChromaSDK.dll", "$env:WINDIR\System32\RzChromaSDK64.dll", "$env:WINDIR\SysWOW64\RzChromaSDK.dll")
    $libraries = @($dlls | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
        [pscustomobject]@{ Path=$_; Version=(Get-Item -LiteralPath $_).VersionInfo.FileVersion }
    })
    $status = if ($devices.Count) { 'Observed' } else { 'Inconclusive' }
    New-AuraLinkEvidence -Vendor Razer -Hypothesis 'Base Station Chroma 1532:0F08 is present' -Status $status -Data $devices
    $status = if ($libraries.Count) { 'Observed' } else { 'MissingDependency' }
    New-AuraLinkEvidence -Vendor Razer -Hypothesis 'Chroma native SDK exists at documented/default paths; no SDK initialization performed' -Status $status -Data $libraries
}
Export-ModuleMember -Function Get-RazerEvidence
