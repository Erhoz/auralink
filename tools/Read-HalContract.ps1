[CmdletBinding()]
param([string]$OutputPath,[switch]$AllowVendorInitialization)
$ErrorActionPreference='Stop'
if(-not $AllowVendorInitialization){throw 'This experimental Corsair reader initializes its backend/driver. Prefer Test-DiagnosticHal.ps1. Explicit opt-in is required.'}
if (-not $OutputPath) { $OutputPath=Join-Path $PSScriptRoot '..\artifacts\hal-contract.json' }
Add-Type -Path (Join-Path $PSScriptRoot 'ReadHalContract.cs')
$result=[AuraLink.ContractProbe.Reader]::Read()
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
if ($result.ActivationHResult -lt 0 -or $result.CountHResult -lt 0 -or $result.FillHResult -lt 0) { exit 2 }
