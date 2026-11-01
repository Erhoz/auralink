function Get-CorsairEvidence {
    $clsid = '{099B9EEB-5BBA-435C-A89E-212FEEEFA394}'
    $key = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$clsid\InprocServer32"
    $path = (Get-Item -LiteralPath $key -ErrorAction Stop).GetValue('')
    $file = Get-Item -LiteralPath $path -ErrorAction Stop
    New-AuraLinkEvidence -Vendor Corsair -Hypothesis 'Official plugin is registered as an in-process COM server' -Status Observed -Data ([pscustomobject]@{
        ClassId=$clsid; Path=$path; FileVersion=$file.VersionInfo.FileVersion; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    })
}
Export-ModuleMember -Function Get-CorsairEvidence
