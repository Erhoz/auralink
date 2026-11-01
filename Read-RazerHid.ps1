[CmdletBinding()]
param([switch]$QueryState)
$ErrorActionPreference='Stop'
$directory=Join-Path $PSScriptRoot 'artifacts\usb-investigation'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $directory 'AuraLink.RazerHidProbe.exe'
$sources=@('src\Core\RgbColor.cs','src\Core\FramePipe.cs','src\Adapters\Asus\HalPipeFrameSource.cs','src\Adapters\Asus\HalLogFrameSource.cs','src\Adapters\Razer\HidMetadata.cs','src\Adapters\Razer\BaseStationProtocol.cs','src\Adapters\Razer\HidStateReader.cs','src\Adapters\Razer\HidFeatureSession.cs','src\Adapters\Razer\VolatileFrameProtocol.cs','tools\RazerVolatileExperiment.cs','tools\RazerHidProbe.cs','tests\RazerVolatileLifecycleTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /target:exe /platform:anycpu /optimize+ /warnaserror+ /reference:System.Web.Extensions.dll "/out:$exe" $sources
if($LASTEXITCODE -ne 0){throw 'HID metadata probe compilation failed.'}
& $exe --self-test
if($LASTEXITCODE -ne 0){throw 'Protocol self-tests failed.'}
if($QueryState){
    $helper=Join-Path $PSScriptRoot 'tools\Invoke-RazerUsbRead.ps1'
    $engine=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments='-NoProfile -NonInteractive -File "{0}" -ExpectedProbeHash {1}' -f $helper,(Get-FileHash -LiteralPath $exe).Hash
    $child=Start-Process -FilePath $engine -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $null=$child.Handle
    try{
        while(-not $child.WaitForExit(1000)){Write-Verbose 'USB read experiment is running.'}
        $child.Refresh()
        $report=Get-Content -LiteralPath (Join-Path $directory 'state-operation.json') -Raw | ConvertFrom-Json
        $report | ConvertTo-Json -Depth 12
        if($child.ExitCode -ne 0 -or -not $report.Completed){exit 2}
    }finally{$child.Dispose()}
    return
}
$stdout=Join-Path $directory 'hid-metadata.json'
$stderr=Join-Path $directory 'hid-metadata.stderr.txt'
$process=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$null=$process.Handle
try{
    if(-not $process.WaitForExit(15000)){$process.Kill();$process.WaitForExit();throw 'HID metadata probe timed out.'}
    $process.Refresh()
    Get-Content -LiteralPath $stdout -Raw
    if($process.ExitCode -ne 0){exit 2}
}finally{if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
