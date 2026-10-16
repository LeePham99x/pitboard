# Tests check-windows-app.ps1's package identity, update key and version checks against the
# Release folder CI's windows-app job built and packed: each case is a folder of its own with
# copies of the files those checks read, one thing changed. It runs none of them.
param(
    [Parameter(Mandatory)] [string] $Folder,
    [Parameter(Mandatory)] [ValidateSet('x86_64-pc-windows-msvc', 'aarch64-pc-windows-msvc')] [string] $Triple,
    [Parameter(Mandatory)] [string] $GatedLibrary,
    # The subject PitboardShare.exe was built to name, and its package was packed for.
    [Parameter(Mandatory)] [string] $Signer,
    # The update key the Release build was given.
    [Parameter(Mandatory)] [string] $UpdateKey,
    # The version the Release build was given, and its FileVersion.
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $FileVersion
)
$ErrorActionPreference = 'Stop'

$check = Join-Path $PSScriptRoot 'check-windows-app.ps1'
$pack = Join-Path $PSScriptRoot 'pack-windows-share.ps1'
$root = (Resolve-Path -LiteralPath $Folder).Path
$work = Join-Path ([IO.Path]::GetTempPath()) "test-windows-app-$([guid]::NewGuid().ToString('N'))"
$other = 'CN=Someone Else'
# Read here, at the script's level: PSScriptAnalyzer does not count a use inside a function.
$checked = @{ Triple = $Triple; GatedLibrary = $GatedLibrary; Version = $Version; FileVersion = $FileVersion }
$givenKey = $UpdateKey

function New-Case([string] $Name, [string[]] $Files) {
    $case = Join-Path $work $Name
    New-Item -ItemType Directory -Path $case | Out-Null
    foreach ($file in @('Pitboard.exe', 'Pitboard.dll', 'Pitboard.Windows.dll') + $Files) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $case
    }
    $case
}

function Assert-Refused([string] $Case, [string] $In, [string] $By, [string] $Expected, [string] $Key = $givenKey, [hashtable] $Instead = @{}) {
    $given = $checked.Clone()
    foreach ($name in $Instead.Keys) { $given[$name] = $Instead[$name] }
    try {
        & $check -Folder $In -Signer $By -UpdateKey $Key @given | Out-Null
    } catch {
        if ($_.Exception.Message -notmatch $Expected) {
            throw "$Case`: refused for another reason: $($_.Exception.Message)"
        }
        Write-Output "ok: $Case"
        return
    }
    throw "$Case`: check-windows-app.ps1 let it through"
}

New-Item -ItemType Directory -Path $work | Out-Null
try {
    $whole = @('PitboardShare.exe', 'PitboardShare.msix')

    $copied = New-Case 'copied' $whole
    Copy-Item -LiteralPath (Join-Path $copied 'PitboardShare.exe') -Destination (Join-Path $copied 'Other.exe')
    Assert-Refused 'another program with an <msix> element' $copied $Signer 'Other\.exe asks for a package identity'

    $repacked = New-Case 'repacked' @('PitboardShare.exe')
    & $pack -Folder $repacked -Publisher $other -Version '1.0.0.0' | Out-Null
    Assert-Refused 'a package another subject publishes' $repacked $Signer "Publisher is 'CN=Someone Else', not the signer's subject"
    Assert-Refused "a signer PitboardShare.exe's <msix> element does not name" $repacked $other "<msix> publisher is '$([regex]::Escape($Signer))'"

    Assert-Refused 'PitboardShare.exe without its package' (New-Case 'alone' @('PitboardShare.exe')) $Signer 'no PitboardShare\.msix beside'
    Assert-Refused 'a package without PitboardShare.exe' (New-Case 'orphan' @('PitboardShare.msix')) $Signer 'without PitboardShare\.exe'
    Assert-Refused 'PitboardShare.exe with no signer named' (New-Case 'unnamed' $whole) '' '-Signer must name'

    $anotherKey = [Convert]::ToBase64String([byte[]]::new(32))
    Assert-Refused 'an update key other than the one the build was given' (New-Case 'key' $whole) $Signer `
        "carries the update key '.+', and the build was given '$([regex]::Escape($anotherKey))'" $anotherKey
    Assert-Refused 'an update key the build was not given' (New-Case 'keyless' $whole) $Signer 'and the build was given none' ''

    Assert-Refused 'a version the build was not given' (New-Case 'version' ($whole + 'pitboard_ffi.dll')) $Signer `
        "names the product 'Pitboard' at '.+', file .+, not Pitboard at 9\.9\.9," -Instead @{ Version = '9.9.9' }
    Assert-Refused 'a FileVersion windows-versions.sh does not give the version' (New-Case 'file' ($whole + 'pitboard_ffi.dll')) $Signer `
        "names the product 'Pitboard' at '$([regex]::Escape($Version))', file .+, not Pitboard at .+, file 9\.9\.9\.1000" `
        -Instead @{ FileVersion = '9.9.9.1000' }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'check-windows-app.ps1 refused every package identity, update key and version it should.'
exit 0
