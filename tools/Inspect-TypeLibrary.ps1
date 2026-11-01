[CmdletBinding()]
param([Parameter(Mandatory=$true)][string[]]$Path)
$ErrorActionPreference = 'Stop'
if (-not ('TypeLibraryProbe' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'TypeLibraryProbe.cs')
}
foreach ($file in $Path) {
    Write-Output "FILE $file"
    try { [TypeLibraryProbe]::Inspect((Resolve-Path -LiteralPath $file).Path) }
    catch { Write-Output "TYPELIB_ERROR $($_.Exception.Message)" }
}
