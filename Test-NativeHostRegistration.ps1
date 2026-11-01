[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$directory=Join-Path $PSScriptRoot 'artifacts\native host regression with spaces'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$dll=Join-Path $directory 'AuraLink.DiagnosticHal.dll'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'artifacts\diagnostic-hal\AuraLink.DiagnosticHal.dll') -Destination $dll
$registered=$false
try{
    & (Join-Path $PSScriptRoot 'Register-DiagnosticHal.ps1') -Action Install -Scope User -AssemblyPath $dll | Out-Null
    $registered=$true
    $json=& python (Join-Path $PSScriptRoot 'tools\probe_native_host.py')
    if($LASTEXITCODE -ne 0){throw 'Native host probe process failed.'}
    $result=$json | ConvertFrom-Json
    foreach($activation in $result.activations){if($activation.hresult -ne '0x00000000' -or -not $activation.pointerReturned){throw 'Native host activation failed.'}}
    $json | Set-Content -LiteralPath (Join-Path $directory 'result.json') -Encoding UTF8
    $json
}finally{
    if($registered){& (Join-Path $PSScriptRoot 'Register-DiagnosticHal.ps1') -Action Remove -Scope User | Out-Null}
}
