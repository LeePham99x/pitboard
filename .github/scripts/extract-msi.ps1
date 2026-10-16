# Extracts an MSI into a new folder as an administrative image, which copies its files and
# registers nothing, and prints the folder of the image that holds Pitboard.exe. It installs
# nothing and runs none of the files.
param(
    [Parameter(Mandatory)] [string] $Msi,
    [Parameter(Mandatory)] [string] $Destination
)
$ErrorActionPreference = 'Stop'

$file = (Resolve-Path -LiteralPath $Msi).Path
$name = Split-Path -Leaf $file
if (Test-Path -LiteralPath $Destination) { throw "$Destination is there already: an image goes into a folder of its own" }
$image = (New-Item -ItemType Directory -Path $Destination).FullName
$log = Join-Path ([IO.Path]::GetTempPath()) "extract-msi-$([guid]::NewGuid().ToString('N')).log"
try {
    $extract = Start-Process msiexec.exe -Wait -PassThru -ArgumentList '/a', "`"$file`"", '/qn', "TARGETDIR=`"$image`"", '/l*v', "`"$log`""
    if ($extract.ExitCode -ne 0) {
        $tail = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -Tail 40) -join "`n" } else { 'no log' }
        throw "msiexec /a $name exited $($extract.ExitCode):`n$tail"
    }
} finally {
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
}
$found = @(Get-ChildItem -LiteralPath $image -Recurse -File | Where-Object Name -CEQ 'Pitboard.exe')
if ($found.Count -ne 1) { throw "the administrative image of $name holds $($found.Count) Pitboard.exe" }
$found[0].DirectoryName
