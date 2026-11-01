[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$output=Join-Path $PSScriptRoot 'artifacts\diagnostic-hal'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){ throw '.NET Framework C# compiler was not found.' }
$sources=@('src\Core\DiagnosticSink.cs','src\DiagnosticHal\Contracts.cs','src\DiagnosticHal\DiagnosticHal.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
$resource=Join-Path $PSScriptRoot 'src\DiagnosticHal\Capability.xml'
$dll=Join-Path $output 'AuraLink.DiagnosticHal.dll'
& $compiler /nologo /target:library /platform:anycpu /optimize+ /warnaserror+ /reference:System.Web.Extensions.dll "/out:$dll" "/resource:$resource,AuraLink.Capability.xml" $sources
if($LASTEXITCODE -ne 0){ throw 'Diagnostic HAL compilation failed.' }
foreach($platform in @('x64','x86')){
    $client=Join-Path $output "AuraLink.HalClient.$platform.exe"
    $clientSource=Join-Path $PSScriptRoot 'tests\HalNativeClient.cs'
    & $compiler /nologo /target:exe "/platform:$platform" /optimize+ /warnaserror+ /reference:System.Web.Extensions.dll /reference:System.Xml.Linq.dll "/out:$client" $clientSource
    if($LASTEXITCODE -ne 0){throw "Native COM client compilation failed: $platform"}
}
Get-FileHash -LiteralPath $dll -Algorithm SHA256
