Set-StrictMode -Version Latest
function New-AuraLinkEvidence {
    param([string]$Vendor, [string]$Hypothesis, [ValidateSet('Observed','Inconclusive','MissingDependency')][string]$Status, $Data)
    [pscustomobject][ordered]@{ Vendor=$Vendor; Hypothesis=$Hypothesis; Status=$Status; Data=$Data }
}
Export-ModuleMember -Function New-AuraLinkEvidence
