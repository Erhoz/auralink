[CmdletBinding()]
param(
    [ValidateSet('Plan','Install','Remove')][string]$Action='Plan',
    [ValidateSet('User','Machine')][string]$Scope='User',
    [switch]$Discovery,
    [string]$AssemblyPath,
    [ValidateSet('Headset','HeadphoneStand')][string]$DeviceType='HeadphoneStand'
)
$ErrorActionPreference='Stop'
$classId='{9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6}'
$owner='AuraLink.DiagnosticHal/0.1'
$classPath="Software\Classes\CLSID\$classId"
$discoveryPath="Software\Classes\CLSID\{109DC3E4-B9FF-4AF3-9008-AB13705D4E5F}\Instance\{E9BBD754-6CF4-492E-BA89-782177A2771B}\Instance\$classId"
$directory=Join-Path $PSScriptRoot 'artifacts\diagnostic-hal'
$dll=Join-Path $directory 'AuraLink.DiagnosticHal.dll'
if($AssemblyPath){$dll=[IO.Path]::GetFullPath($AssemblyPath)}
$manifestPath=Join-Path $directory "registration-$Scope.json"
$hive=if($Scope -eq 'User'){[Microsoft.Win32.RegistryHive]::CurrentUser}else{[Microsoft.Win32.RegistryHive]::LocalMachine}
$views=@([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)
$paths=@($classPath)
if($Discovery){$paths += $discoveryPath}
if($Action -eq 'Plan'){
    [pscustomobject]@{Action='Plan';Scope=$Scope;Views=@('Registry64','Registry32');ClassId=$classId;Assembly=$dll;RegistryPaths=$paths;Discovery=[bool]$Discovery;RestartsServices=$false} | ConvertTo-Json -Depth 5
    return
}
if($Scope -eq 'Machine'){
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Machine scope requires an administrator shell.'}
    if($Action -eq 'Install'){
        $protectedRoot=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'AuraLink\DiagnosticHal'))+[IO.Path]::DirectorySeparatorChar
        if(-not $dll.StartsWith($protectedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Machine registration requires a protected AuraLink installation. Use Start-ArmouryDiagnostic.ps1.'}
    }
}
if($Action -eq 'Remove'){
    if(-not(Test-Path -LiteralPath $manifestPath)){throw 'Registration manifest not found. Refusing untracked removal.'}
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($manifest.Owner -ne $owner -or $manifest.ClassId -ne $classId -or $manifest.Scope -ne $Scope){throw 'Manifest identity mismatch.'}
    $paths=@($classPath)
    if($manifest.Discovery){$paths += $discoveryPath}
    # Check every target before the first mutation. Never remove shared parents.
    foreach($view in $views){
        $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
        try{
            foreach($path in $paths){
                $key=$root.OpenSubKey($path)
                if($null -eq $key){continue}
                try{if($key.GetValue('AuraLinkOwner') -ne $owner){throw "Foreign registration at $view/$path"}}finally{$key.Dispose()}
            }
            $server=$root.OpenSubKey("$classPath\InprocServer32")
            if($null -ne $server){try{if($server.GetValue('CodeBase') -ne $manifest.CodeBase){throw 'CodeBase changed; refusing removal.'}}finally{$server.Dispose()}}
        }finally{$root.Dispose()}
    }
    foreach($view in $views){
        $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
        try{foreach($path in $paths){$root.DeleteSubKeyTree($path,$false)}}finally{$root.Dispose()}
    }
    Remove-Item -LiteralPath $manifestPath
    "Removed AuraLink registration ($Scope); no service restart."
    return
}
if(-not(Test-Path -LiteralPath $dll)){throw 'Build the diagnostic HAL first.'}
if(Test-Path -LiteralPath $manifestPath){throw 'Tracked registration already exists. Remove it before reinstalling.'}
$assembly=[Reflection.AssemblyName]::GetAssemblyName($dll).FullName
# Match .NET Framework RegAsm's CodeBase format. Percent-encoded spaces can
# pass tests in a CLR host (assembly already resolvable from its app directory)
# while native COM activation fails with HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND).
$codeBase=[Uri]::UnescapeDataString(([Uri][IO.Path]::GetFullPath($dll)).AbsoluteUri)
# Refuse collisions in either view before creating any key.
foreach($view in $views){
    $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
    try{foreach($path in $paths){$key=$root.OpenSubKey($path);if($null -ne $key){$key.Dispose();throw "Existing registration at $view/$path"}}}finally{$root.Dispose()}
}
$created=@()
try{
    foreach($view in $views){
        $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
        try{
            foreach($path in $paths){
                $key=$root.CreateSubKey($path)
                $created += [pscustomobject]@{View=$view;Path=$path}
                try{
                    $key.SetValue('AuraLinkOwner',$owner)
                    if($path -eq $classPath){
                        $key.SetValue('','AuraLink Diagnostic HAL (no hardware)')
                        $server=$key.CreateSubKey('InprocServer32')
                        try{
                            $server.SetValue('','mscoree.dll');$server.SetValue('ThreadingModel','Both')
                            $server.SetValue('Class','AuraLink.DiagnosticHal.DiagnosticHal');$server.SetValue('Assembly',$assembly)
                            $server.SetValue('RuntimeVersion','v4.0.30319');$server.SetValue('CodeBase',$codeBase)
                        }finally{$server.Dispose()}
                    }else{
                        foreach($pair in @(@('Name','AuraLink Diagnostic'),@('Description','Unofficial diagnostic sink; no hardware'),@('Manufacturer','AuraLink'),@('SpecVersion','1.0.0'),@('Version','0.1.0'),@('DeviceType',$DeviceType))){$key.SetValue($pair[0],$pair[1])}
                        $key.SetValue('Pluging',0,[Microsoft.Win32.RegistryValueKind]::DWord)
                    }
                }finally{$key.Dispose()}
            }
        }finally{$root.Dispose()}
    }
    [ordered]@{Owner=$owner;ClassId=$classId;Scope=$Scope;CodeBase=$codeBase;Discovery=[bool]$Discovery;CreatedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}catch{
    foreach($entry in $created){
        $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$entry.View)
        try{$root.DeleteSubKeyTree($entry.Path,$false)}finally{$root.Dispose()}
    }
    throw
}
"Installed AuraLink registration ($Scope), discovery=$([bool]$Discovery); no service restart."
