[CmdletBinding()]
param([string]$OutputPath,[switch]$AllowGlobalHardwareEnumeration)
$ErrorActionPreference = 'Stop'
if(-not $AllowGlobalHardwareEnumeration){throw 'Global provider enumeration is disabled by default: it left vendor helper processes alive during investigation. Use the targeted Diagnostic HAL tests.'}
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot '..\artifacts\aura-devices.json' }
$sdk = $null
$devices = $null
$report = [ordered]@{ TimestampUtc = [DateTime]::UtcNow.ToString('o'); CalledSwitchMode = $false; AcquiredControl = $null; Devices = @(); Error = $null }
try {
    $sdk = [Activator]::CreateInstance([type]::GetTypeFromCLSID([guid]'05921124-5057-483e-a037-e9497b523590', $true))
    # Deliberately never call SwitchMode, Apply, setters, or ReleaseControl.
    $devices = $sdk.Enumerate(0)
    for ($i = 0; $i -lt $devices.Count; $i++) {
        $device = $null
        $lights = $null
        try {
            $device = $devices.Item($i)
            $lights = $device.Lights
            $report.Devices += [ordered]@{ Name = $device.Name; Type = $device.Type; Width = $device.Width; Height = $device.Height; LedCount = $lights.Count }
        } finally {
            if ($null -ne $lights) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($lights) }
            if ($null -ne $device) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($device) }
        }
    }
} catch { $report.Error = $_.Exception.ToString() }
finally {
    if ($null -ne $devices) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($devices) }
    if ($null -ne $sdk) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($sdk) }
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))) | Out-Null
$json = $report | ConvertTo-Json -Depth 8
$json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$json
if ($report.Error) { exit 2 }
