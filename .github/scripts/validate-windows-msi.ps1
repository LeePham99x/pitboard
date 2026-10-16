# Runs ICE03, which Pitboard.Installer suppresses for WinUI's own files, on a built MSI with the WiX that built it, and
# refuses any message but those files'. It first holds that WiX to nuget.org's hash, since no lock file covers an SDK.
param(
    [Parameter(Mandatory)] [string] $Msi
)
$ErrorActionPreference = 'Stop'

# nuget.org's SHA-512 of each WixToolset.Sdk global.json may name: raising the pin there needs its line here.
$pinned = @{
    '7.0.0' = 'E6sydem/3Fj08mhN9BTvNzw5JwdzKpKQ8JA6hs6BDYQt6ytTVr+2kYaZ/AH+x87z9iSZGyblj2y9y2zcNVtKyg=='
}
$global = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\..\apps\windows\global.json') | ConvertFrom-Json
$version = $global.'msbuild-sdks'.'WixToolset.Sdk'
if (-not $pinned.ContainsKey($version)) { throw "global.json names WixToolset.Sdk $version, and this script pins no hash for it" }
$packages = (& dotnet nuget locals global-packages --list | Out-String).Trim() -replace '^global-packages:\s*', ''
if ($LASTEXITCODE -ne 0) { throw 'dotnet could not say where its packages are' }
$sdk = Join-Path $packages "wixtoolset.sdk\$version"
$package = Join-Path $sdk "wixtoolset.sdk.$version.nupkg"
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "NuGet holds no ${package}: build Pitboard.Installer first" }
$hash = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($package)))
if ($hash -cne $pinned[$version]) { throw "the WixToolset.Sdk $version that built the MSI hashes to $hash, not nuget.org's $($pinned[$version])" }
Write-Output "WixToolset.Sdk $version is nuget.org's."

function Invoke-Com($Target, [string] $Name, [string] $Kind, [object[]] $Arguments) {
    $Target.GetType().InvokeMember($Name, $Kind, $null, $Target, $Arguments)
}

# A Filename or DefaultDir column's long name: short|long, then :source for a directory.
function Get-LongName([string] $Column) {
    (($Column -split ':')[0] -split '\|')[-1]
}

$file = (Resolve-Path -LiteralPath $Msi).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = Invoke-Com $installer 'OpenDatabase' 'InvokeMethod' @($file, 0)
function Get-Row([string] $Query) {
    $view = Invoke-Com $database 'OpenView' 'InvokeMethod' @($Query)
    try {
        Invoke-Com $view 'Execute' 'InvokeMethod' $null | Out-Null
        $record = Invoke-Com $view 'Fetch' 'InvokeMethod' $null
        if (-not $record) { return $null }
        $count = Invoke-Com $record 'FieldCount' 'GetProperty' $null
        # COM takes no PSObject, which a pipeline's $_ is.
        return , @(1..$count | ForEach-Object { Invoke-Com $record 'StringData' 'GetProperty' @([int] $_) })
    } finally {
        Invoke-Com $view 'Close' 'InvokeMethod' $null | Out-Null
    }
}

# The Windows App SDK's files ICE03 refuses (#208): the MUI files for ug-CN, mi-NZ and gd-gb, whose primary language is
# past 0x7F, and two libraries whose list of languages is longer than the File table's Language column.
$languages = 'ug-CN', 'mi-NZ', 'gd-gb'
$muiFiles = 'Microsoft.ui.xaml.dll.mui', 'Microsoft.UI.Xaml.Phone.dll.mui'
$longLists = 'Microsoft.ui.xaml.dll', 'Microsoft.UI.Xaml.Phone.dll'
function Test-WinUiRow([string] $What, [string] $Table, [string] $Column, [string] $Key) {
    if ($Table -ne 'File' -or $Column -ne 'Language') { return $false }
    $row = Get-Row "SELECT ``File``.``FileName``, ``Component``.``Directory_`` FROM ``File``, ``Component`` WHERE ``File``.``Component_`` = ``Component``.``Component`` AND ``File``.``File`` = '$($Key.Replace("'", "''"))'"
    if (-not $row) { return $false }
    $name = Get-LongName $row[0]
    $folder = Get-LongName (Get-Row "SELECT ``DefaultDir`` FROM ``Directory`` WHERE ``Directory`` = '$($row[1])'")[0]
    switch ($What) {
        'Invalid Language Id' { return $name -in $muiFiles -and $folder -in $languages }
        'String overflow (greater than length permitted in column)' { return $name -in $longLists }
    }
    return $false
}

$sdkTools = Join-Path $sdk 'tools\net8.0'
$work = Join-Path ([IO.Path]::GetTempPath()) "validate-windows-msi-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $said = @(& dotnet (Join-Path $sdkTools 'wix.dll') msi validate -acceptEula wix7 -ice ICE03 -intermediateFolder $work $file 2>&1 |
        ForEach-Object { "$_" })
    $exit = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
$allowed = 0
$refused = @()
foreach ($line in $said) {
    if ($line -notmatch '\b(error|warning) WIX\d+') { continue }
    if ($line -match 'ICE03: (?<what>[^;]+); Table: (?<table>[^,]+), Column: (?<column>[^,]+), Key\(s\): (?<key>[^\s\[]+)' -and
        (Test-WinUiRow $Matches.what $Matches.table $Matches.column $Matches.key)) {
        $allowed++
    } else {
        $refused += $line
    }
}
if ($refused) {
    $refused | Write-Output
    throw "$(Split-Path -Leaf $file) fails ICE03 in rows besides WinUI's own files"
}
if ($exit -ne 0 -and $allowed -eq 0) {
    $said | Write-Output
    throw "wix msi validate exited $exit"
}
Write-Output "ICE03 passes $(Split-Path -Leaf $file) but for $allowed File rows of WinUI's own files."
exit 0
