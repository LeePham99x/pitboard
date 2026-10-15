# Checks the Authenticode signature of every program a Windows release hands on: each file given,
# and each program inside each zip given. It reads the files and runs none of them.
param(
    [Parameter(Mandatory)] [string[]] $Path,
    # signed: each program's signature is Valid, by -Subject, with a timestamp. unsigned: none is signed.
    [Parameter(Mandatory)] [ValidateSet('signed', 'unsigned')] [string] $Mode,
    [string] $Subject,
    # The folder holding each zip as it was built, before signing: a zip given must hold the same entries.
    [string] $Built
)
$ErrorActionPreference = 'Stop'

if ($Mode -eq 'signed' -and -not $Subject) { throw 'a signed release names the subject its signer signs as' }

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
    if ($status -ne 'Valid') { throw "$Name is $status, and every program of a signed release is Valid" }
    $signer = $signature.SignerCertificate.Subject
    if ($signer -ne $Subject) { throw "$Name is signed by '$signer', not '$Subject'" }
    if (-not $signature.TimeStamperCertificate) { throw "$Name's signature has no timestamp" }
    Write-Output "ok: $Name is Valid, signed by '$signer', timestamped by '$($signature.TimeStamperCertificate.Subject)'"
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
        } elseif (Test-Program $file) {
            Assert-Signature $file $leaf
        } else {
            throw "$leaf is neither a zip nor a program"
        }
    }
} finally {
    Remove-Item -Recurse -Force -LiteralPath $work
}
