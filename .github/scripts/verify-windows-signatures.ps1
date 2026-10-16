# Checks the Authenticode signature of every program a Windows release hands on, and runs none of them. Of an MSI it
# checks the programs named for Pitboard, and with -Built, that signing changed none of its files or tables.
param(
    [Parameter(Mandatory)] [string[]] $Path,
    # signed: each signature is Valid, by -Subject, with a timestamp. unsigned: none is signed.
    # test: signed by -Subject with a certificate made for a test, which nothing trusts or timestamps.
    [Parameter(Mandatory)] [ValidateSet('signed', 'unsigned', 'test')] [string] $Mode,
    [string] $Subject,
    # The folder holding each zip and MSI as it was built, before signing: one given must hold the same files.
    [string] $Built
)
$ErrorActionPreference = 'Stop'

if ($Mode -ne 'unsigned' -and -not $Subject) { throw 'a signed release names the subject its signer signs as' }
$extract = Join-Path $PSScriptRoot 'extract-msi.ps1'

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile
$work = Join-Path ([IO.Path]::GetTempPath()) "verify-windows-signatures-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

# A program by its bytes, whatever its name: the MZ header, then the PE signature it points to.
function Test-Program([string] $File) {
    $stream = [IO.File]::OpenRead($File)
    try {
        if ($stream.Length -lt 64) { return $false }
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { return $false }
        $stream.Position = 0x3C
        $header = $reader.ReadUInt32()
        if ($header + 4 -gt $stream.Length) { return $false }
        $stream.Position = $header
        return $reader.ReadUInt32() -eq 0x00004550
    } finally {
        $stream.Dispose()
    }
}

function Assert-Signature([string] $File, [string] $Name) {
    $signature = Get-AuthenticodeSignature -LiteralPath $File
    $status = $signature.Status
    if ($Mode -eq 'unsigned') {
        if ($status -ne 'NotSigned') { throw "$Name is $status, and every program of an unsigned release is NotSigned" }
        Write-Output "ok: $Name is NotSigned"
        return
    }
    # A certificate made a moment ago chains to no root the machine trusts, which PowerShell calls UnknownError.
    $accepted = if ($Mode -eq 'test') { 'Valid', 'UnknownError', 'NotTrusted' } else { , 'Valid' }
    if ($status -notin $accepted) { throw "$Name is $status, and every program of a $Mode release is $($accepted -join ' or ')" }
    $signer = $signature.SignerCertificate.Subject
    if ($signer -ne $Subject) { throw "$Name is signed by '$signer', not '$Subject'" }
    if ($Mode -eq 'test') {
        Write-Output "ok: $Name is signed by '$signer' and $status here"
        return
    }
    if (-not $signature.TimeStamperCertificate) { throw "$Name's signature has no timestamp" }
    Write-Output "ok: $Name is Valid, signed by '$signer', timestamped by '$($signature.TimeStamperCertificate.Subject)'"
}

# Each file of an administrative image, by its path in the image.
function Get-ImageFiles([string] $Image) {
    $prefix = (Get-Item -LiteralPath $Image).FullName.TrimEnd('\').Length + 1
    return @(Get-ChildItem -LiteralPath $Image -Recurse -File | ForEach-Object { $_.FullName.Substring($prefix) } | Sort-Object)
}

function Invoke-Com($Target, [string] $Name, [string] $Kind, [object[]] $Arguments) {
    $Target.GetType().InvokeMember($Name, $Kind, $null, $Target, $Arguments)
}

# Each table of an MSI as Windows Installer exports it, the .idt files and the streams of its binary columns.
function Export-Tables([string] $Msi, [string] $To) {
    New-Item -ItemType Directory -Path $To | Out-Null
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = Invoke-Com $installer 'OpenDatabase' 'InvokeMethod' @($Msi, 0)
    $view = Invoke-Com $database 'OpenView' 'InvokeMethod' @('SELECT `Name` FROM `_Tables`')
    Invoke-Com $view 'Execute' 'InvokeMethod' $null | Out-Null
    $tables = @(while (($record = Invoke-Com $view 'Fetch' 'InvokeMethod' $null)) { Invoke-Com $record 'StringData' 'GetProperty' @(1) })
    Invoke-Com $view 'Close' 'InvokeMethod' $null | Out-Null
    foreach ($table in $tables) {
        Invoke-Com $database 'Export' 'InvokeMethod' @([string] $table, $To, "$table.idt") | Out-Null
    }
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($database)
}

# The tables of two MSIs that differ, by name.
function Compare-Tables([string] $Msi, [string] $Built) {
    $mine = Join-Path $work ([guid]::NewGuid().ToString('N'))
    $theirs = Join-Path $work ([guid]::NewGuid().ToString('N'))
    Export-Tables $Msi $mine
    Export-Tables $Built $theirs
    $names = @(Get-ImageFiles $mine) + @(Get-ImageFiles $theirs) | Sort-Object -Unique
    foreach ($name in $names) {
        $a = Join-Path $mine $name
        $b = Join-Path $theirs $name
        if (-not (Test-Path -LiteralPath $a) -or -not (Test-Path -LiteralPath $b) -or
            (Get-FileHash -LiteralPath $a).Hash -ne (Get-FileHash -LiteralPath $b).Hash) {
            ($name -split '[\\.]')[0]
        }
    }
}

function Get-EntryNames([string] $Zip) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    } finally {
        $archive.Dispose()
    }
}

try {
    foreach ($given in $Path) {
        $file = (Resolve-Path -LiteralPath $given).Path
        $leaf = Split-Path -Leaf $file
        if ($leaf -like '*.zip') {
            if ($Built) {
                $before = Join-Path $Built $leaf
                if (-not (Test-Path -LiteralPath $before -PathType Leaf)) { throw "$Built holds no $leaf as it was built" }
                $names = (Get-EntryNames $file) -join ', '
                $namesBuilt = (Get-EntryNames $before) -join ', '
                if ($names -ne $namesBuilt) { throw "$leaf holds other entries than it was built with: $names, not $namesBuilt" }
            }
            $programs = 0
            $archive = [IO.Compression.ZipFile]::OpenRead($file)
            try {
                foreach ($entry in $archive.Entries) {
                    if ($entry.FullName.EndsWith('/')) { continue }
                    # A folder each, since entries in two folders of the zip may share a name.
                    $folder = Join-Path $work ([guid]::NewGuid().ToString('N'))
                    New-Item -ItemType Directory -Path $folder | Out-Null
                    $out = Join-Path $folder $entry.Name
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $out)
                    if (Test-Program $out) {
                        Assert-Signature $out "$leaf/$($entry.FullName)"
                        $programs++
                    }
                }
            } finally {
                $archive.Dispose()
            }
            if ($programs -eq 0) { throw "$leaf holds no program" }
        } elseif ($leaf -like '*.msi') {
            Assert-Signature $file $leaf
            $image = Join-Path $work ([guid]::NewGuid().ToString('N'))
            & $extract -Msi $file -Destination $image | Out-Null
            $files = Get-ImageFiles $image
            if ($Built) {
                $before = Join-Path $Built $leaf
                if (-not (Test-Path -LiteralPath $before -PathType Leaf)) { throw "$Built holds no $leaf as it was built" }
                $imageBuilt = Join-Path $work ([guid]::NewGuid().ToString('N'))
                & $extract -Msi $before -Destination $imageBuilt | Out-Null
                if (($files -join ', ') -ne ((Get-ImageFiles $imageBuilt) -join ', ')) { throw "$leaf holds other files than it was built with" }
                # Signing an MSI adds its signature and changes none of its files or tables.
                $changed = @($files | Where-Object {
                    (Get-FileHash -LiteralPath (Join-Path $image $_)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $imageBuilt $_)).Hash })
                if ($changed) { throw "$leaf holds another $($changed -join ', ') than it was built with" }
                $tables = @(Compare-Tables $file $before | Sort-Object -Unique)
                if ($tables) { throw "$leaf holds other tables than it was built with: $($tables -join ', ')" }
            }
            $programs = 0
            foreach ($inside in $files) {
                $full = Join-Path $image $inside
                # Pitboard's own programs are the ones named for it, and only those are its signer's.
                if ((Split-Path -Leaf $inside) -like 'pitboard*' -and (Test-Program $full)) {
                    Assert-Signature $full "$leaf/$($inside.Replace('\', '/'))"
                    $programs++
                }
            }
            if ($programs -eq 0) { throw "$leaf holds no program of Pitboard's" }
        } elseif ($leaf -like '*.msix') {
            Assert-Signature $file $leaf
        } elseif (Test-Program $file) {
            Assert-Signature $file $leaf
        } else {
            throw "$leaf is neither a zip, an MSI, a sparse package nor a program"
        }
    }
} finally {
    Remove-Item -Recurse -Force -LiteralPath $work
}
