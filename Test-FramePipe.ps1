[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$directory=Join-Path $PSScriptRoot 'artifacts\frame-pipe-tests'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@('src\Core\FramePipe.cs','src\Core\RgbColor.cs','src\Adapters\Asus\HalPipeFrameSource.cs','tests\FramePipeTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
foreach($platform in @('x64','x86')){
    $exe=Join-Path $directory ("AuraLink.FramePipeTests.$platform.exe")
    & $compiler /nologo /target:exe "/platform:$platform" /optimize+ /warnaserror+ "/out:$exe" $sources
    if($LASTEXITCODE -ne 0){throw 'Frame pipe test compilation failed.'}
    $process=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $directory "$platform.json") -RedirectStandardError (Join-Path $directory "$platform.stderr.txt")
    $null=$process.Handle
    try{
        if(-not $process.WaitForExit(20000)){$process.Kill();$process.WaitForExit();throw 'Frame pipe tests timed out.'}
        $process.Refresh()
        if($process.ExitCode -ne 0){Get-Content (Join-Path $directory "$platform.stderr.txt");throw "Frame pipe tests failed ($platform)."}
        Get-Content (Join-Path $directory "$platform.json")
    }finally{if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
}
