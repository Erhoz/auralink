[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot '..\artifacts\corsair-hal.json' }
if (-not [Environment]::Is64BitProcess) { throw 'Use Windows PowerShell x64.' }
if (-not ('AuraLink.Probe.HalProbe' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'HalProbe.cs') }
# Only the installed Corsair class is allowed; arbitrary downloaded COM classes are not activated.
$result = [AuraLink.Probe.HalProbe]::Read('099B9EEB-5BBA-435C-A89E-212FEEEFA394')
$json = $result | ConvertTo-Json -Depth 8
$parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$json
if (-not $result.SupportsHalInterface) { exit 2 }
