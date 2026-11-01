[CmdletBinding()]
param([switch]$Publish,[switch]$CheckAccess)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$version=(Get-Content (Join-Path $repo 'version.json') -Raw | ConvertFrom-Json).version
$tag='v'+$version
$directory=Join-Path $repo ('artifacts\releases\'+$version)
$names=@("AuraLink-$version-Setup-x64.exe","AuraLink-$version-windows-x64.zip","AuraLink-$version-build.json",'SHA256SUMS.txt')
if(-not $Publish -and -not $CheckAccess){[pscustomobject]@{Tag=$tag;Prerelease=($version.Contains('-'));Assets=$names;Directory=$directory;Published=$false} | ConvertTo-Json;return}
# Keep credential-helper output in memory. Never put tokens in args, files or logs.
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=(Get-Command git).Source;$start.Arguments='credential fill';$start.WorkingDirectory=$repo
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$process=[Diagnostics.Process]::Start($start)
$process.StandardInput.Write("protocol=https`nhost=github.com`npath=Erhoz/auralink.git`n`n");$process.StandardInput.Close()
$credential=$process.StandardOutput.ReadToEnd();$null=$process.StandardError.ReadToEnd();$process.WaitForExit()
try{if($process.ExitCode -ne 0){throw 'Git credential helper could not authorize GitHub.'}}finally{$process.Dispose()}
$secret=$null
foreach($line in $credential.Split("`n")){if($line.StartsWith('password=')){$secret=$line.Substring(9).TrimEnd("`r")}}
$credential=$null
if(-not $secret){throw 'GitHub credential unavailable; authenticate Git before publishing.'}
$headers=@{Authorization='Bearer '+$secret;Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='AuraLink-release'}
$base='https://api.github.com/repos/Erhoz/auralink'
try{
    $repository=Invoke-RestMethod -Uri $base -Headers $headers
    if($CheckAccess){[pscustomobject]@{Repository=$repository.full_name;CanPush=$repository.permissions.push;ReleaseApiAvailable=$true} | ConvertTo-Json;return}
    $metadata=Get-Content (Join-Path $directory $names[2]) -Raw | ConvertFrom-Json
    if($metadata.dirty){throw 'Dirty-build assets cannot be published.'}
    $head=(& git -c "safe.directory=$repo" rev-parse HEAD).Trim()
    $tagCommit=(& git -c "safe.directory=$repo" rev-list -n 1 $tag).Trim()
    if($LASTEXITCODE -ne 0 -or $tagCommit -ne $head -or $metadata.sourceCommit -ne $head){throw 'Tag, working HEAD and artifact source commit must match.'}
    if(@(& git -c "safe.directory=$repo" status --porcelain).Count){throw 'Commit source changes before publishing.'}
    foreach($name in $names){if(-not(Test-Path -LiteralPath (Join-Path $directory $name))){throw "Missing asset: $name"}}
    foreach($artifact in $metadata.artifacts){if((Get-FileHash -LiteralPath (Join-Path $directory $artifact.name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $artifact.sha256){throw 'Artifact hash changed.'}}
    $sums=@(Get-Content (Join-Path $directory 'SHA256SUMS.txt'))
    if($sums.Count -ne 3){throw 'Checksum manifest incomplete.'}
    foreach($line in $sums){$parts=$line -split '  ',2;if($parts.Count -ne 2 -or $parts[1] -notin $names[0..2] -or (Get-FileHash (Join-Path $directory $parts[1]) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $parts[0]){throw 'Checksum manifest mismatch.'}}
    $existing=@(Invoke-RestMethod -Uri ($base+'/releases?per_page=100') -Headers $headers | ForEach-Object {$_} | Where-Object tag_name -eq $tag)
    if($existing.Count){throw 'A release with this tag already exists; refusing to replace it.'}
    # ReadAllText returns a plain string; PS 5.1 Get-Content adds provider
    # properties that ConvertTo-Json can serialize as an object instead of text.
    $notes=[IO.File]::ReadAllText((Join-Path $repo ('docs\releases\'+$version+'.md')),[Text.Encoding]::UTF8)
    $body=@{tag_name=$tag;target_commitish=$head;name='AuraLink '+$version;body=$notes;draft=$true;prerelease=($version.Contains('-'));make_latest='false'} | ConvertTo-Json -Depth 4
    $release=Invoke-RestMethod -Method Post -Uri ($base+'/releases') -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    $uploaded=@()
    foreach($name in $names){
        $file=Join-Path $directory $name
        $upload='https://uploads.github.com/repos/Erhoz/auralink/releases/'+$release.id+'/assets?name='+[Uri]::EscapeDataString($name)
        $asset=Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType 'application/octet-stream' -InFile $file
        if($asset.size -ne (Get-Item -LiteralPath $file).Length){throw 'GitHub asset size mismatch; release remains a draft.'}
        if($asset.digest -and $asset.digest -ne ('sha256:'+(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())){throw 'GitHub asset digest mismatch; release remains a draft.'}
        $uploaded+= [pscustomobject]@{Name=$asset.name;Bytes=$asset.size;URL=$asset.browser_download_url;Digest=$asset.digest}
    }
    $published=Invoke-RestMethod -Method Patch -Uri ($base+'/releases/'+$release.id) -Headers $headers -ContentType 'application/json' -Body '{"draft":false}'
    $published=Invoke-RestMethod -Uri ($base+'/releases/'+$release.id) -Headers $headers
    $publicAssets=@($published.assets | ForEach-Object {[pscustomobject]@{Name=$_.name;Bytes=$_.size;URL=$_.browser_download_url;Digest=$_.digest}})
    [pscustomobject]@{Version=$version;URL=$published.html_url;Prerelease=$published.prerelease;Draft=$published.draft;Assets=$publicAssets} | ConvertTo-Json -Depth 5
}finally{$secret=$null;$headers=$null}
