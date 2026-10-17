# Hands on nothing age did not encrypt to an X25519 key.
param(
    [Parameter(Mandatory)] [string] $Folder,
    [Parameter(Mandatory)] [ValidatePattern('^session-build-[a-z0-9_-]+$')] [string] $Name,
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Age,
    [Parameter(Mandatory)] [string] $Recipient
)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $Folder).Path
$sums = Join-Path $root 'SHA256SUMS'
if (Test-Path -LiteralPath $sums) { throw "$root holds a SHA256SUMS already" }
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force |
    ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } | Sort-Object -CaseSensitive)
if (-not $files) { throw "$root holds nothing to pack" }
# The session machine's check splits each line at its spaces.
$spaced = @($files | Where-Object { $_ -match '\s' })
if ($spaced) { throw "$($spaced -join ', ') has a space in its name, which SHA256SUMS cannot name" }
$lines = foreach ($file in $files) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash.ToLowerInvariant(), $file
}
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"))

New-Item -ItemType Directory -Force -Path $Out | Out-Null
# .NET reads a relative path against its own current folder, which is not PowerShell's.
$dist = (Resolve-Path -LiteralPath $Out).Path
$zip = Join-Path $dist "$Name.zip"
$sealed = "$zip.age"
foreach ($old in $zip, $sealed) {
    if (Test-Path -LiteralPath $old) { throw "$old is there already" }
}
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile
try {
    # Each entry named here, with /, as the zip format asks.
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in @($files) + 'SHA256SUMS') {
            $level = [IO.Compression.CompressionLevel]::Optimal
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $file), "$Name/$file", $level) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
    & $Age --encrypt --recipient $Recipient --output $sealed $zip
    if ($LASTEXITCODE -ne 0) { throw "age exited $LASTEXITCODE" }
    $expected = "age-encryption.org/v1`n-> X25519 "
    $head = [byte[]]::new($expected.Length)
    $stream = [IO.File]::OpenRead($sealed)
    try { $read = $stream.Read($head, 0, $head.Length) } finally { $stream.Dispose() }
    if ([Text.Encoding]::ASCII.GetString($head, 0, $read) -cne $expected) {
        throw "age wrote $sealed, which does not read as encrypted to an X25519 key, so nothing is handed on"
    }
} catch {
    Remove-Item -LiteralPath $sealed -Force -ErrorAction SilentlyContinue
    throw
} finally {
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
}
Write-Output $sealed
