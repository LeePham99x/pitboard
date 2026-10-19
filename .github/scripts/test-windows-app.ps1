# Tests check-windows-app.ps1 against copies of CI's Release folder, one thing changed in each, and check-windows-msi.ps1,
# validate-windows-msi.ps1, check-msi-components.ps1 and verify-windows-signatures.ps1 against its MSI and package. It
# runs none of them.
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
    # The MSI the job built from that folder, unsigned, the folder of its administrative image that
    # holds Pitboard.exe, the ProductVersion windows-versions.sh gives it, and the pitboard.exe and
    # completions\pitboard.ps1 in it.
    [Parameter(Mandatory)] [string] $Msi,
    [Parameter(Mandatory)] [string] $MsiImage,
    [Parameter(Mandatory)] [string] $MsiVersion,
    [Parameter(Mandatory)] [string] $CommandLine,
    [Parameter(Mandatory)] [string] $Completions
)
$ErrorActionPreference = 'Stop'

$check = Join-Path $PSScriptRoot 'check-windows-app.ps1'
$pack = Join-Path $PSScriptRoot 'pack-windows-share.ps1'
$verify = Join-Path $PSScriptRoot 'verify-windows-signatures.ps1'
$checkMsi = Join-Path $PSScriptRoot 'check-windows-msi.ps1'
$validate = Join-Path $PSScriptRoot 'validate-windows-msi.ps1'
$components = Join-Path $PSScriptRoot 'check-msi-components.ps1'
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

function Assert-Thrown([string] $Case, [string] $Script, [hashtable] $Given, [string] $Expected, [hashtable] $Instead = @{}) {
    $Given = $Given.Clone()
    foreach ($name in $Instead.Keys) { $Given[$name] = $Instead[$name] }
    try {
        & $Script @Given | Out-Null
    } catch {
        if ($_.Exception.Message -notmatch $Expected) {
            throw "$Case`: refused for another reason: $($_.Exception.Message)"
        }
        Write-Output "ok: $Case"
        return
    }
    throw "$Case`: $(Split-Path -Leaf $Script) let it through"
}

# A copy of the MSI under its own name, in a folder of its own, with one change made through Windows Installer.
function Edit-Msi([string] $Case, [string] $Query) {
    $folder = Join-Path $work $Case
    New-Item -ItemType Directory -Path $folder | Out-Null
    $copy = Join-Path $folder (Split-Path -Leaf $msiPath)
    Copy-Item -LiteralPath $msiPath -Destination $copy
    $installer = New-Object -ComObject WindowsInstaller.Installer
    # msiOpenDatabaseModeTransact
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @([string] $copy, 1))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Query))
    [void] $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    [void] $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    [void] $database.GetType().InvokeMember('Commit', 'InvokeMethod', $null, $database, $null)
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    $copy
}

# The File table's key of the one file the MSI lays out under a name, as WiX harvested it.
function Get-FileKey([string] $Name) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @([string] $msiPath, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @('SELECT `File`, `FileName` FROM `File`'))
    [void] $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    $keys = @(while (($record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null))) {
        $long = ($record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(2)) -split '\|')[-1]
        if ($long -ceq $Name) { $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1)) }
    })
    [void] $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    if ($keys.Count -ne 1) { throw "the MSI lays out $($keys.Count) files named $Name" }
    $keys[0]
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
        "names the product 'Pitboard' by 'Pitboard' at '.+', file .+, not Pitboard by Pitboard at 9\.9\.9," -Instead @{ Version = '9.9.9' }
    Assert-Refused 'a FileVersion windows-versions.sh does not give the version' (New-Case 'file' ($whole + 'pitboard_ffi.dll')) $Signer `
        "names the product 'Pitboard' by 'Pitboard' at '$([regex]::Escape($Version))', file .+, not Pitboard by Pitboard at .+, file 9\.9\.9\.1000" `
        -Instead @{ FileVersion = '9.9.9.1000' }
    $foreign = New-Case 'foreign' ($whole + 'pitboard_ffi.dll')
    Copy-Item -LiteralPath (Join-Path $root 'WinSparkle.dll') -Destination (Join-Path $foreign 'pitboard_other.dll')
    Assert-Refused 'a library named for Pitboard whose version resource names another product' $foreign $Signer `
        "pitboard_other\.dll names the product '(?!Pitboard')"

    $built = Join-Path $work 'built'
    New-Item -ItemType Directory -Path $built | Out-Null
    Copy-Item -LiteralPath $msiPath -Destination $built
    & $verify -Path $msiPath -Mode unsigned -Built $built
    Write-Output 'ok: the MSI and each program of Pitboard''s in it are NotSigned, the others as their makers sign them, and it holds the files and tables it was built with'
    # A harvested file renamed in the File table, whose name msiexec /a lays it out under.
    $stranger = Edit-Msi 'stranger' "UPDATE ``File`` SET ``FileName`` = 'Stranger.dll' WHERE ``File`` = '$(Get-FileKey 'WinSparkle.dll')'"
    Assert-Thrown 'an unsigned program that is neither Pitboard''s nor WinSparkle.dll' $verify @{ Path = $stranger; Mode = 'unsigned' } `
        'Pitboard\.msi/PFiles64/Pitboard/Stranger\.dll is NotSigned, not Valid as Microsoft Corporation signs it'
    $impostor = Edit-Msi 'impostor' "UPDATE ``File`` SET ``FileName`` = 'Microsoft.Impostor.dll' WHERE ``File`` = '$(Get-FileKey 'CommunityToolkit.Common.dll')'"
    Assert-Thrown 'a program the .NET Foundation signed where Microsoft signs' $verify @{ Path = $impostor; Mode = 'unsigned' } `
        "Pitboard\.msi/PFiles64/Pitboard/Microsoft\.Impostor\.dll is signed by '.*\(\.NET Foundation\).*', not by Microsoft Corporation"
    $renamed = Edit-Msi 'renamed' "UPDATE ``File`` SET ``FileName`` = 'other.exe' WHERE ``File`` = 'CommandLineExe'"
    Assert-Thrown 'an MSI that lays out a file under another name than it was built with' $verify `
        @{ Path = $renamed; Mode = 'unsigned'; Built = $built } 'holds other files than it was built with'
    $retitled = Edit-Msi 'retitled' "UPDATE ``Property`` SET ``Value`` = 'Someone Else' WHERE ``Property`` = 'Manufacturer'"
    Assert-Thrown 'an MSI with a row its build did not write' $verify `
        @{ Path = $retitled; Mode = 'unsigned'; Built = $built } 'holds other tables than it was built with: Property$'

    & $components -Msi $msiPath -Previous $msiPath
    $dropped = Edit-Msi 'dropped' "DELETE FROM ``Component`` WHERE ``KeyPath`` = 'CommandLineExe'"
    & $components -Msi $dropped -Previous $msiPath | Out-Null
    & $components -Msi $msiPath -Previous $dropped, $msiPath | Out-Null
    Write-Output 'ok: a component that goes or comes leaves every other GUID where it was'
    $regenerated = Edit-Msi 'regenerated' "UPDATE ``Component`` SET ``ComponentId`` = '{00000000-0000-0000-0000-000000000001}' WHERE ``KeyPath`` = 'CommandLineExe'"
    Assert-Thrown 'a key path with another GUID than the last release gave it' $components @{ Msi = $regenerated; Previous = $msiPath } `
        'file ProgramFiles64Folder\\Pitboard\\bin\\pitboard\.exe has the component \{00000000-0000-0000-0000-000000000001\}, and had \{'
    Assert-Thrown 'a GUID with another key path than the last release gave it' $components @{ Msi = $renamed; Previous = $msiPath } `
        'has the key path file ProgramFiles64Folder\\Pitboard\\bin\\other\.exe, and had file ProgramFiles64Folder\\Pitboard\\bin\\pitboard\.exe in '

    & $validate -Msi $msiPath
    $broken = Edit-Msi 'ice03' "UPDATE ``Registry`` SET ``Root`` = 7 WHERE ``Name`` = 'CommandLineOnPath'"
    Assert-Thrown 'a row of Pitboard''s own that ICE03 refuses' $validate @{ Msi = $broken } 'fails ICE03 in rows besides WinUI''s own files'
    Assert-Thrown 'an unsigned MSI in a signed release' $verify @{ Path = $msiPath; Mode = 'signed'; Subject = $signPath } `
        'Pitboard\.msi is NotSigned, and every program of a signed release is Valid'

    $msiGiven = @{ Msi = $msiPath; Folder = $MsiImage; MsiVersion = $MsiVersion; CommandLine = $CommandLine; Completions = $Completions; Share = $true }
    Assert-Thrown 'an MSI of another version' $checkMsi $msiGiven "ProductVersion is '.+', not 9\.9\.9" @{ MsiVersion = '9.9.9' }
    Assert-Thrown 'an MSI with another command line than the zip''s' $checkMsi $msiGiven 'bin\\pitboard\.exe is not the zip''s' `
        @{ CommandLine = (Join-Path $root 'Pitboard.exe') }
    Assert-Thrown 'an MSI with other completions than the zip''s' $checkMsi $msiGiven 'completions\\pitboard\.ps1 is not the zip''s' `
        @{ Completions = (Join-Path $root 'Pitboard.dll') }
    Assert-Thrown 'a share target in an MSI without a signed package' $checkMsi $msiGiven 'this release has no signed package' @{ Share = $false }
    $otherPackage = Join-Path $work 'other.msix'
    Copy-Item -LiteralPath (Join-Path $root 'PitboardShare.msix') -Destination $otherPackage
    [IO.File]::AppendAllText($otherPackage, 'another')
    Assert-Thrown 'an MSI whose package is not the signed one' $checkMsi $msiGiven 'PitboardShare\.msix is not the signed one' @{ Package = $otherPackage }

    $package = Join-Path $root 'PitboardShare.msix'
    & $verify -Path $package -Mode test -Subject $Signer
    Assert-Thrown 'a package signed for a test, checked against SignPath''s subject' $verify @{ Path = $package; Mode = 'test'; Subject = $signPath } `
        "PitboardShare\.msix is signed by '$([regex]::Escape($Signer))', not 'CN=SignPath Foundation"
    Assert-Thrown 'a package signed for a test, in a signed release' $verify @{ Path = $package; Mode = 'signed'; Subject = $Signer } `
        'PitboardShare\.msix is \w+, and every program of a signed release is Valid'
    Assert-Thrown 'a package signed for a test, in an unsigned release' $verify @{ Path = $package; Mode = 'unsigned' } `
        'PitboardShare\.msix is \w+, and every program of an unsigned release is NotSigned'
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'check-windows-app.ps1, check-windows-msi.ps1, validate-windows-msi.ps1, check-msi-components.ps1 and verify-windows-signatures.ps1 refused what they should.'
exit 0
