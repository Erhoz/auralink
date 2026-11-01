[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference='Stop'
if(-not $OutputPath){$OutputPath=Join-Path $PSScriptRoot '..\artifacts\armoury-stage3\desktop.png'}
# Read-only full virtual desktop capture. No UI Automation, input injection,
# application activation, security prompt interaction or window manipulation.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$bounds=[Windows.Forms.SystemInformation]::VirtualScreen
$bitmap=New-Object Drawing.Bitmap($bounds.Width,$bounds.Height)
$graphics=[Drawing.Graphics]::FromImage($bitmap)
try{
    $graphics.CopyFromScreen($bounds.X,$bounds.Y,0,0,$bounds.Size)
    $bitmap.Save([IO.Path]::GetFullPath($OutputPath),[Drawing.Imaging.ImageFormat]::Png)
}finally{$graphics.Dispose();$bitmap.Dispose()}
[pscustomobject]@{Path=[IO.Path]::GetFullPath($OutputPath);Width=$bounds.Width;Height=$bounds.Height;X=$bounds.X;Y=$bounds.Y} | ConvertTo-Json
