[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$output=Join-Path $PSScriptRoot 'artifacts\startup-task-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe=Join-Path $output 'StartupTaskTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /warnaserror+ /reference:Microsoft.CSharp.dll /out:$exe (Join-Path $PSScriptRoot 'src\Desktop\StartupTask.cs') (Join-Path $PSScriptRoot 'tests\StartupTaskTests.cs')
if($LASTEXITCODE -ne 0){throw 'Startup task regression compilation failed.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Startup task regression failed.'}
