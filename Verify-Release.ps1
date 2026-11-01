[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$version=(Get-Content (Join-Path $PSScriptRoot 'version.json') -Raw | ConvertFrom-Json).version
$directory=Join-Path $PSScriptRoot ('artifacts\releases\'+$version)
$work=Join-Path $directory ('inspection-'+[Guid]::NewGuid().ToString('N'))
$setup=Join-Path $directory ("AuraLink-$version-Setup-x64.exe")
$p=Start-Process -FilePath $setup -ArgumentList ('--extract "{0}"' -f $work) -WindowStyle Hidden -PassThru -RedirectStandardOutput ($work+'.stdout.txt') -RedirectStandardError ($work+'.stderr.txt')
$null=$p.Handle
try{
    if(-not $p.WaitForExit(10000)){$p.Kill();$p.WaitForExit();throw 'Extraction inspection timed out.'}
    $p.Refresh();if($p.ExitCode -ne 0){Get-Content ($work+'.stderr.txt');throw 'Extraction inspection failed.'}
}finally{$p.Dispose()}
$manifest=Get-Content (Join-Path $work 'payload.manifest.json') -Raw | ConvertFrom-Json
foreach($item in $manifest.files.PSObject.Properties){if((Get-FileHash -LiteralPath (Join-Path $work $item.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.Value){throw 'Extracted file hash mismatch.'}}
if([Reflection.AssemblyName]::GetAssemblyName((Join-Path $work 'bin\AuraLink.DiagnosticHal.dll')).ProcessorArchitecture -ne 'MSIL'){throw 'The HAL must be AnyCPU for ASUS x86/x64 hosts.'}
foreach($relative in @('AuraLink.exe','bin\AuraLink.Worker.exe','bin\AuraLink.DiagnosticHal.dll')){
    if(-not (Get-Item (Join-Path $work $relative)).VersionInfo.ProductVersion.StartsWith($version+'+')){throw 'Binary version metadata mismatch.'}
}
foreach($file in @(Get-ChildItem (Join-Path $work 'scripts') -Filter '*.ps1')){
    $tokens=$null;$errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if($errors){$errors;throw 'Packaged PowerShell syntax failed.'}
}
$readme=Get-Content (Join-Path $work 'README.txt') -Raw -Encoding UTF8
$expectedText='Integra'+[char]0xE7+[char]0xE3+'o n'+[char]0xE3+'o oficial'
if(-not $readme.Contains($expectedText)){throw 'UTF-8 package text corrupted.'}
$taskXml=Join-Path $work 'startup-task-preview.xml'
$p=Start-Process -FilePath (Join-Path $work 'AuraLink.exe') -ArgumentList ('--export-startup-task "{0}"' -f $taskXml) -WindowStyle Hidden -PassThru
$null=$p.Handle
try{if(-not $p.WaitForExit(10000)){$p.Kill();$p.WaitForExit();throw 'Task preview timed out.'};$p.Refresh();if($p.ExitCode -ne 0){throw 'Task preview failed.'}}finally{$p.Dispose()}
[xml]$task=Get-Content -LiteralPath $taskXml -Raw
if($task.Task.Principals.Principal.RunLevel -ne 'HighestAvailable' -or $task.Task.Principals.Principal.LogonType -ne 'InteractiveToken' -or $task.Task.Actions.Exec.Arguments -ne '--autostart' -or -not $task.Task.Triggers.LogonTrigger){throw 'Automatic startup definition mismatch.'}
$scheduler=New-Object -ComObject Schedule.Service
try{$scheduler.Connect();$definition=$scheduler.NewTask(0);$definition.XmlText=[IO.File]::ReadAllText($taskXml);if($definition.Triggers.Count -ne 1 -or $definition.Principal.RunLevel -ne 1){throw 'Windows scheduler rejected startup definition.'}}finally{if($definition){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($definition)};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($scheduler)}
Add-Type -AssemblyName System.Drawing
$iconHashes=@()
foreach($exe in @($setup,(Join-Path $work 'AuraLink.exe'),(Join-Path $work 'bin\AuraLink.Worker.exe'))){
    $icon=[Drawing.Icon]::ExtractAssociatedIcon($exe)
    try{if(-not $icon){throw 'Executable icon missing.'};$bitmap=$icon.ToBitmap();try{$iconPath=Join-Path $work (([IO.Path]::GetFileName($exe))+'.icon.png');$bitmap.Save($iconPath,[Drawing.Imaging.ImageFormat]::Png);$iconHashes+=(Get-FileHash -LiteralPath $iconPath -Algorithm SHA256).Hash}finally{$bitmap.Dispose()}}finally{if($icon){$icon.Dispose()}}
}
if(@($iconHashes | Select-Object -Unique).Count -ne 1){throw 'Installer/application icons differ.'}
$result=[ordered]@{Version=$version;SourceCommit=$manifest.sourceCommit;Files=$manifest.files.PSObject.Properties.Name.Count;ExtractedHashesMatch=$true;HalAnyCpu=$true;BinaryVersionsMatch=$true;PowerShellSyntaxPassed=$true;Utf8TextPassed=$true;Installed=$false;HardwareAccess=$false;Directory=$work}
$result.StartupTaskDefinitionPassed=$true;$result.IconsExtractedFromAllExecutables=$true
$result.SchedulerDefinitionAcceptedWithoutRegistration=$true
$result.SameBrandIconInAllExecutables=$true
$result | ConvertTo-Json | Set-Content (Join-Path $directory 'package-verification.json') -Encoding UTF8
$result | ConvertTo-Json
