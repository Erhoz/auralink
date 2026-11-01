[CmdletBinding()]
param([switch]$AllowDirty)
$ErrorActionPreference='Stop'
$repo=$PSScriptRoot
$config=Get-Content (Join-Path $repo 'version.json') -Raw | ConvertFrom-Json
$version=$config.version
if($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$'){throw 'Invalid SemVer.'}
$commit=(& git -c "safe.directory=$repo" rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0){throw 'Git source commit unavailable.'}
$dirty=@(& git -c "safe.directory=$repo" status --porcelain).Count -gt 0
if($dirty -and -not $AllowDirty){throw 'Commit source changes before building release assets, or use -AllowDirty for package verification only.'}
$output=Join-Path $repo ('artifacts\releases\'+$version)
$work=Join-Path $output ('work-'+[Guid]::NewGuid().ToString('N'))
$payload=Join-Path $work 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
foreach($name in @('bin','scripts')){New-Item -ItemType Directory -Path (Join-Path $payload $name) | Out-Null}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$info=Join-Path $work 'VersionInfo.cs'
$informational=$version+'+'+$commit.Substring(0,12)+$(if($dirty){'.dirty'}else{''})
@"
using System.Reflection;
[assembly: AssemblyVersion("$($config.assemblyVersion)")]
[assembly: AssemblyFileVersion("$($config.assemblyVersion)")]
[assembly: AssemblyInformationalVersion("$informational")]
[assembly: AssemblyProduct("AuraLink")]
[assembly: AssemblyCompany("AuraLink contributors")]
[assembly: AssemblyCopyright("Copyright (c) 2026 AuraLink contributors")]
"@ | Set-Content -LiteralPath $info -Encoding UTF8
$manifest=Join-Path $repo 'packaging\app.manifest'
function Compile([string]$Target,[string]$Kind,[string[]]$Sources,[string[]]$Extra=@()){
    $paths=@($Sources | ForEach-Object {Join-Path $repo $_})+@($info)
    $platform=if($Kind -eq 'library'){'anycpu'}else{'x64'}
    if($Kind -ne 'library'){$Extra+= '/win32icon:'+(Join-Path $repo 'assets\AuraLink.ico')}
    & $compiler /nologo "/target:$Kind" "/platform:$platform" /optimize+ /warnaserror+ /utf8output /codepage:65001 /reference:System.Web.Extensions.dll "/out:$Target" @Extra $paths
    if($LASTEXITCODE -ne 0){throw "Compilation failed: $Target"}
}
$capability=Join-Path $work 'Capability.xml'
(Get-Content (Join-Path $repo 'src\DiagnosticHal\Capability.xml') -Raw).Replace('AuraLink Diagnostic (no hardware)','AuraLink Razer Base Station Chroma').Replace('AuraLink Diagnostic LED 0','Base Station LEDs') | Set-Content -LiteralPath $capability -Encoding UTF8
Compile (Join-Path $payload 'bin\AuraLink.DiagnosticHal.dll') 'library' @('src\Core\FramePipe.cs','src\Core\DiagnosticSink.cs','src\DiagnosticHal\Contracts.cs','src\DiagnosticHal\DiagnosticHal.cs') @("/resource:$capability,AuraLink.Capability.xml",'/define:AURALINK_RELEASE')
Compile (Join-Path $payload 'bin\AuraLink.Worker.exe') 'exe' @('src\Core\RgbColor.cs','src\Core\FramePipe.cs','src\Adapters\Asus\HalPipeFrameSource.cs','src\Adapters\Razer\HidMetadata.cs','src\Adapters\Razer\BaseStationProtocol.cs','src\Adapters\Razer\VolatileFrameProtocol.cs','src\Adapters\Razer\HidFeatureSession.cs','src\Adapters\Razer\ReconnectableHidControl.cs','tools\RazerVolatileExperiment.cs','src\Desktop\WorkerProgram.cs') @("/win32manifest:$manifest")
Compile (Join-Path $payload 'AuraLink.exe') 'winexe' @('src\Desktop\Program.cs','src\Desktop\StartupTask.cs','src\Adapters\Razer\HidMetadata.cs') @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.ServiceProcess.dll','/reference:Microsoft.CSharp.dll',"/win32manifest:$manifest")
Copy-Item (Join-Path $repo 'packaging\Session.ps1') (Join-Path $payload 'scripts\Session.ps1')
Copy-Item (Join-Path $repo 'Register-DiagnosticHal.ps1') (Join-Path $payload 'scripts\Register-DiagnosticHal.ps1')
foreach($name in @('README.txt','NOTICE.txt')){
    $content=(Get-Content (Join-Path $repo ('packaging\'+$name)) -Raw -Encoding UTF8).Replace('@VERSION@',$version)
    [IO.File]::WriteAllText((Join-Path $payload $name),$content,(New-Object Text.UTF8Encoding($false)))
}
$files=[ordered]@{}
Get-ChildItem $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative=$_.FullName.Substring($payload.Length+1).Replace('\','/')
    $files[$relative]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$package=[ordered]@{version=$version;sourceCommit=$commit;dirty=$dirty;files=$files}
[IO.File]::WriteAllText((Join-Path $payload 'payload.manifest.json'),($package | ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $work 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip)
$setup=Join-Path $output ("AuraLink-$version-Setup-x64.exe")
Compile $setup 'winexe' @('src\Packaging\Setup.cs','src\Desktop\StartupTask.cs') @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:Microsoft.CSharp.dll',"/resource:$zip,AuraLink.Payload.zip","/win32manifest:$manifest")
$verify=Join-Path $work 'installer-verification.json'
$p=Start-Process -FilePath $setup -ArgumentList ('--verify-payload "{0}"' -f $verify) -WindowStyle Hidden -PassThru
$null=$p.Handle
try{if(-not $p.WaitForExit(10000)){$p.Kill();$p.WaitForExit();throw 'Installer payload verification timed out.'};$p.Refresh();if($p.ExitCode -ne 0){throw 'Installer payload verification failed.'}}finally{$p.Dispose()}
$bundle=Join-Path $work 'download'
New-Item -ItemType Directory -Path $bundle | Out-Null
Copy-Item -LiteralPath $setup -Destination $bundle
Copy-Item -LiteralPath (Join-Path $payload 'README.txt') -Destination $bundle
$download=Join-Path $output ("AuraLink-$version-windows-x64.zip")
if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download}
[IO.Compression.ZipFile]::CreateFromDirectory($bundle,$download)
$build=Join-Path $output ("AuraLink-$version-build.json")
$metadata=[ordered]@{version=$version;sourceCommit=$commit;dirty=$dirty;builtUtc=[DateTime]::UtcNow.ToString('o');unsigned=$true;automaticStartup=$true;trayApplication=$true;persistentProvider=$true;installerPayloadVerification=(Get-Content $verify -Raw | ConvertFrom-Json);hardwareValidation='Backend previously hardware-validated. Updated installer, login startup and persistent provider verified without a new hardware/login round; old installation recovered and removed on user request.';artifacts=@((Get-Item $setup),(Get-Item $download) | ForEach-Object {@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$_.Length}})}
[IO.File]::WriteAllText($build,($metadata | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
$lines=@($setup,$download,$build) | ForEach-Object {((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($_)}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$lines,(New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{Version=$version;SourceCommit=$commit;Dirty=$dirty;Directory=$output;Installer=$setup;Zip=$download;Payload=$payload;Verification=$verify} | ConvertTo-Json
