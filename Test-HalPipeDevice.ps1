[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Build-DiagnosticHal.ps1') | Out-Null
$directory=Join-Path $PSScriptRoot 'artifacts\frame-device-tests'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$dll=Join-Path $directory 'AuraLink.DiagnosticHal.dll'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'artifacts\diagnostic-hal\AuraLink.DiagnosticHal.dll') -Destination $dll
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($platform in @('x64','x86')){
    $exe=Join-Path $directory ("AuraLink.FrameDeviceTests.$platform.exe")
    & $compiler /nologo /target:exe "/platform:$platform" /optimize+ /warnaserror+ "/reference:$dll" "/out:$exe" (Join-Path $PSScriptRoot 'tests\HalPipeDeviceTests.cs')
    if($LASTEXITCODE -ne 0){throw 'HAL pipe device test compilation failed.'}
    $process=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $directory "$platform.json") -RedirectStandardError (Join-Path $directory "$platform.stderr.txt")
    $null=$process.Handle
    try{
        if(-not $process.WaitForExit(10000)){$process.Kill();$process.WaitForExit();throw 'HAL pipe device tests timed out.'}
        $process.Refresh()
        if($process.ExitCode -ne 0){Get-Content (Join-Path $directory "$platform.stderr.txt");throw 'HAL pipe device tests failed.'}
        Get-Content (Join-Path $directory "$platform.json")
    }finally{if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
}
