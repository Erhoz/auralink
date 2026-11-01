[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$ExpectedOldCommit)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$version=(Get-Content (Join-Path $repo 'version.json') -Raw | ConvertFrom-Json).version
$tag='v'+$version
$head=(& git -c "safe.directory=$repo" rev-parse HEAD).Trim()
$old=(& git -c "safe.directory=$repo" rev-list -n 1 $tag).Trim()
$oldObject=(& git -c "safe.directory=$repo" rev-parse "refs/tags/$tag").Trim()
if($old -ne $ExpectedOldCommit){throw 'Old tag commit changed; replacement refused.'}
if(@(& git -c "safe.directory=$repo" status --porcelain).Count){throw 'Commit replacement source first.'}
$directory=Join-Path $repo ('artifacts\releases\'+$version)
$build=Get-Content (Join-Path $directory ("AuraLink-$version-build.json")) -Raw | ConvertFrom-Json
if($build.dirty -or $build.sourceCommit -ne $head){throw 'Replacement requires clean assets from current HEAD.'}
$inspection=Get-Content (Join-Path $directory 'package-verification.json') -Raw | ConvertFrom-Json
if($inspection.SourceCommit -ne $head -or -not $inspection.StartupTaskDefinitionPassed -or -not $inspection.IconsExtractedFromAllExecutables){throw 'Replacement package verification missing.'}
$remote=(& git -c "safe.directory=$repo" ls-remote origin "refs/tags/$tag").Split("`t")[0].Trim()
if($LASTEXITCODE -ne 0 -or $remote -ne $oldObject){throw 'Remote tag changed; replacement refused.'}
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=(Get-Command git).Source;$start.Arguments='credential fill';$start.WorkingDirectory=$repo
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$p=[Diagnostics.Process]::Start($start)
$p.StandardInput.Write("protocol=https`nhost=github.com`npath=Erhoz/auralink.git`n`n");$p.StandardInput.Close()
$credential=$p.StandardOutput.ReadToEnd();$null=$p.StandardError.ReadToEnd();$p.WaitForExit()
try{if($p.ExitCode -ne 0){throw 'GitHub authentication unavailable.'}}finally{$p.Dispose()}
$secret=$null;foreach($line in $credential.Split("`n")){if($line.StartsWith('password=')){$secret=$line.Substring(9).TrimEnd("`r")}};$credential=$null
if(-not $secret){throw 'GitHub credential missing.'}
$headers=@{Authorization='Bearer '+$secret;Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='AuraLink-release'}
$base='https://api.github.com/repos/Erhoz/auralink'
try{
    $release=Invoke-RestMethod -Uri ($base+'/releases/tags/'+$tag) -Headers $headers
    if($release.tag_name -ne $tag -or -not $release.prerelease -or $release.immutable){throw 'Unexpected or immutable release; replacement refused.'}
    [IO.File]::WriteAllText((Join-Path $directory 'withdrawn-release.json'),(@{OldCommit=$old;OldTagObject=$oldObject;Release=$release} | ConvertTo-Json -Depth 16),(New-Object Text.UTF8Encoding($false)))
    Invoke-RestMethod -Method Delete -Uri ($base+'/releases/'+$release.id) -Headers $headers | Out-Null
    & git -c "safe.directory=$repo" push ("--force-with-lease=refs/tags/"+$tag+':'+$oldObject) origin (':refs/tags/'+$tag)
    if($LASTEXITCODE -ne 0){throw 'Old release removed, but tag deletion failed; inspect before continuing.'}
    & git -c "safe.directory=$repo" tag -d $tag
    if($LASTEXITCODE -ne 0){throw 'Local tag deletion failed.'}
    & git -c "safe.directory=$repo" tag -a $tag -m ("AuraLink $version replacement: automatic startup and tray icon") $head
    if($LASTEXITCODE -ne 0){throw 'Replacement tag creation failed.'}
    & git -c "safe.directory=$repo" push origin main
    if($LASTEXITCODE -ne 0){throw 'Source push failed.'}
    & git -c "safe.directory=$repo" push origin $tag
    if($LASTEXITCODE -ne 0){throw 'Replacement tag push failed.'}
}finally{$secret=$null;$headers=$null}
& (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -File (Join-Path $PSScriptRoot 'Publish-Release.ps1') -Publish
if($LASTEXITCODE -ne 0){throw 'Replacement tag pushed; release publication needs completion.'}
