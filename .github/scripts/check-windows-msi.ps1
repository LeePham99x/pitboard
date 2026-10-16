# Checks an MSI and its administrative image as a release ships them, for CI and the release
# alike: the ProductVersion windows-versions.sh gives the version, Pitboard laid out as an install
# lays it out with the command line in bin, the very pitboard.exe the release's zip holds, and
# PitboardShare.exe with its package only where the release has a signed one. It reads the files
# and runs none of them.
param(
    [Parameter(Mandatory)] [string] $Msi,
    # The folder of its administrative image that holds Pitboard.exe, as extract-msi.ps1 prints it.
    [Parameter(Mandatory)] [string] $Folder,
    # The ProductVersion windows-versions.sh gives Cargo.toml's version.
    [Parameter(Mandatory)] [string] $MsiVersion,
    # The pitboard.exe the release's zip for this machine holds.
    [Parameter(Mandatory)] [string] $CommandLine,
    # The MSI carries PitboardShare.exe and its package, as it does where the package is signed.
    [switch] $Share,
    # The signed PitboardShare.msix it must carry byte for byte.
    [string] $Package
)
$ErrorActionPreference = 'Stop'

if ($Package -and -not $Share) { throw '-Package names the package an MSI with -Share carries' }

function Get-Hash([string] $File) {
    (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash
}

function Invoke-Com($Target, [string] $Name, [string] $Kind, [object[]] $Arguments) {
    $Target.GetType().InvokeMember($Name, $Kind, $null, $Target, $Arguments)
}

# Windows Installer's own reader of the Property table.
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = Invoke-Com $installer 'OpenDatabase' 'InvokeMethod' @((Resolve-Path -LiteralPath $Msi).Path, 0)
$view = Invoke-Com $database 'OpenView' 'InvokeMethod' @("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'ProductVersion'")
Invoke-Com $view 'Execute' 'InvokeMethod' $null | Out-Null
$record = Invoke-Com $view 'Fetch' 'InvokeMethod' $null
$version = if ($record) { Invoke-Com $record 'StringData' 'GetProperty' @(1) } else { $null }
Invoke-Com $view 'Close' 'InvokeMethod' $null | Out-Null
if ($version -cne $MsiVersion) { throw "the MSI's ProductVersion is '$version', not $MsiVersion" }
Write-Output "The MSI is version $version."

$root = (Resolve-Path -LiteralPath $Folder).Path
foreach ($name in 'Pitboard.exe', 'Pitboard.dll', 'pitboard_ffi.dll', 'WinSparkle.dll', 'bin\pitboard.exe') {
    if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) { throw "the MSI lays out no $name beside Pitboard.exe" }
}
if ((Get-Hash (Join-Path $root 'bin\pitboard.exe')) -ne (Get-Hash $CommandLine)) {
    throw "the MSI's bin\pitboard.exe is not the zip's, $CommandLine"
}
Write-Output "It lays out Pitboard.exe with the zip's pitboard.exe in bin."

$carried = @('PitboardShare.exe', 'PitboardShare.msix', 'pitboard_share_ffi.dll') | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) }
if (-not $Share) {
    # Only a signed package registers, so an MSI without one carries no share target at all.
    if ($carried) { throw "the MSI carries $($carried -join ', '), and this release has no signed package" }
    Write-Output 'It carries no share target, as a release without a signed package does.'
    exit 0
}
if ($carried.Count -ne 3) { throw "the MSI carries $($carried -join ', '), not PitboardShare.exe, its package and pitboard_share_ffi.dll" }
if ($Package -and (Get-Hash (Join-Path $root 'PitboardShare.msix')) -ne (Get-Hash $Package)) {
    throw "the MSI's PitboardShare.msix is not the signed one, $Package"
}
Write-Output 'It carries PitboardShare.exe and its package.'
exit 0
