# Tests check-windows-app.ps1's package identity, update key, feed and version checks against the
# Release folder CI's windows-app job built and packed: each case is a folder of its own with
# copies of the files those checks read, one thing changed. Then verify-windows-signatures.ps1
# against the job's unsigned MSI and the package it signed with a throwaway certificate. It runs
# none of them.
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
    [Parameter(Mandatory)] [string] $FileVersion,
    # The MSI the job built from that folder, unsigned.
    [Parameter(Mandatory)] [string] $Msi
)
$ErrorActionPreference = 'Stop'

$check = Join-Path $PSScriptRoot 'check-windows-app.ps1'
$pack = Join-Path $PSScriptRoot 'pack-windows-share.ps1'
$verify = Join-Path $PSScriptRoot 'verify-windows-signatures.ps1'
$signPath = 'CN=SignPath Foundation, O=SignPath Foundation, L=Lewes, S=Delaware, C=US'
$msiPath = (Resolve-Path -LiteralPath $Msi).Path
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

function Assert-Unverified([string] $Case, [hashtable] $Given, [string] $Expected) {
    try {
        & $verify @Given | Out-Null
    } catch {
        if ($_.Exception.Message -notmatch $Expected) {
            throw "$Case`: refused for another reason: $($_.Exception.Message)"
        }
        Write-Output "ok: $Case"
        return
    }
    throw "$Case`: verify-windows-signatures.ps1 let it through"
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
    Assert-Refused 'no update key' (New-Case 'keyless' $whole) $Signer "the update key '' is not the base64 of 32 bytes" ''
    $short = [Convert]::ToBase64String([byte[]]::new(31))
    Assert-Refused 'an update key WinSparkle keeps none of' (New-Case 'short' $whole) $Signer `
        "the update key '$([regex]::Escape($short))' is not the base64 of 32 bytes" $short

    # The same length, so the assembly's metadata still reads: a feed pinned to one release.
    $pinned = New-Case 'feed' $whole
    $windows = Join-Path $pinned 'Pitboard.Windows.dll'
    $latin = [Text.Encoding]::Latin1
    $from = $latin.GetString([Text.Encoding]::Unicode.GetBytes('releases/latest/download/'))
    $to = $latin.GetString([Text.Encoding]::Unicode.GetBytes('releases/download/v1.0.0/'))
    $text = $latin.GetString([IO.File]::ReadAllBytes($windows))
    if (-not $text.Contains($from)) { throw "Pitboard.Windows.dll holds no 'releases/latest/download/'" }
    [IO.File]::WriteAllBytes($windows, $latin.GetBytes($text.Replace($from, $to)))
    Assert-Refused 'a feed other than the latest full release''s' $pinned $Signer `
        "reads its updates from 'https://github\.com/datlechin/pitboard/releases/download/v1\.0\.0/appcast-windows\.xml'"

    Assert-Refused 'a version the build was not given' (New-Case 'version' ($whole + 'pitboard_ffi.dll')) $Signer `
        "names the product 'Pitboard' at '.+', file .+, not Pitboard at 9\.9\.9," -Instead @{ Version = '9.9.9' }
    Assert-Refused 'a FileVersion windows-versions.sh does not give the version' (New-Case 'file' ($whole + 'pitboard_ffi.dll')) $Signer `
        "names the product 'Pitboard' at '$([regex]::Escape($Version))', file .+, not Pitboard at .+, file 9\.9\.9\.1000" `
        -Instead @{ FileVersion = '9.9.9.1000' }

    & $verify -Path $msiPath -Mode unsigned
    $built = Join-Path $work 'built'
    New-Item -ItemType Directory -Path $built | Out-Null
    Copy-Item -LiteralPath $msiPath -Destination $built
    & $verify -Path $msiPath -Mode unsigned -Built $built | Out-Null
    Write-Output 'ok: the MSI and each program of Pitboard''s in it are NotSigned, and it holds the files it was built with'
    Assert-Unverified 'an unsigned MSI in a signed release' @{ Path = $msiPath; Mode = 'signed'; Subject = $signPath } `
        'Pitboard\.msi is NotSigned, and every program of a signed release is Valid'

    $package = Join-Path $root 'PitboardShare.msix'
    & $verify -Path $package -Mode test -Subject $Signer
    Assert-Unverified 'a package signed for a test, checked against SignPath''s subject' @{ Path = $package; Mode = 'test'; Subject = $signPath } `
        "PitboardShare\.msix is signed by '$([regex]::Escape($Signer))', not 'CN=SignPath Foundation"
    Assert-Unverified 'a package signed for a test, in a signed release' @{ Path = $package; Mode = 'signed'; Subject = $Signer } `
        'PitboardShare\.msix is \w+, and every program of a signed release is Valid'
    Assert-Unverified 'a package signed for a test, in an unsigned release' @{ Path = $package; Mode = 'unsigned' } `
        'PitboardShare\.msix is \w+, and every program of an unsigned release is NotSigned'
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'check-windows-app.ps1 refused every package identity, update key and version it should, and verify-windows-signatures.ps1 read the MSI and the package.'
exit 0
