function Get-AsusEvidence {
    $base = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID'
    $branch = "$base\{109DC3E4-B9FF-4AF3-9008-AB13705D4E5F}\Instance\{E9BBD754-6CF4-492E-BA89-782177A2771B}\Instance"
    $providers = @(Get-ChildItem -LiteralPath $branch -ErrorAction Stop | ForEach-Object {
        $entry = Get-ItemProperty -LiteralPath $_.PSPath
        [pscustomobject]@{ ClassId=$_.PSChildName; Name=$entry.Name; Manufacturer=$entry.Manufacturer; Version=$entry.Version }
    })
    New-AuraLinkEvidence -Vendor ASUS -Hypothesis 'Installed HAL discovery registrations exist (not proof of custom HAL acceptance)' -Status Observed -Data $providers
}
Export-ModuleMember -Function Get-AsusEvidence
