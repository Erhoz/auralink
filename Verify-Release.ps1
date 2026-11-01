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
$result=[ordered]@{Version=$version;SourceCommit=$manifest.sourceCommit;Files=$manifest.files.PSObject.Properties.Name.Count;ExtractedHashesMatch=$true;HalAnyCpu=$true;BinaryVersionsMatch=$true;PowerShellSyntaxPassed=$true;Utf8TextPassed=$true;Installed=$false;HardwareAccess=$false;Directory=$work}
$result | ConvertTo-Json | Set-Content (Join-Path $directory 'package-verification.json') -Encoding UTF8
$result | ConvertTo-Json
