# Tests package-windows.ps1 and check-windows-build.ps1 against a pitboard.exe built without the
# cfg and without test-support, as CI's windows job builds one in target/gated.
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [ValidateSet('x86_64-pc-windows-msvc', 'aarch64-pc-windows-msvc')] [string] $Triple
)
$ErrorActionPreference = 'Stop'

$package = Join-Path $PSScriptRoot 'package-windows.ps1'
$check = Join-Path $PSScriptRoot 'check-windows-build.ps1'
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$work = Join-Path $env:RUNNER_TEMP "test-windows-release-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

function Assert-Refused([string] $Case, [hashtable] $Given, [string] $Expected) {
    try {
        & $check @Given | Out-Null
    } catch {
        if ($_.Exception.Message -notmatch $Expected) {
            throw "$Case`: refused for another reason: $($_.Exception.Message)"
        }
        Write-Output "ok: $Case"
        return
    }
    throw "$Case`: check-windows-build.ps1 let it through"
}

$top = "pitboard-v1.2.3-beta.4-$Triple"
$zip = & $package -Exe $exePath -Version '1.2.3-beta.4' -Triple $Triple -Out $work
if ($zip -ne (Join-Path $work "$top.zip")) { throw "package-windows.ps1 wrote $zip" }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })
    $expected = @('pitboard.exe', 'completions/pitboard.ps1', 'completions/pitboard.bash', 'LICENSE', 'NOTICE', 'README.md') |
        ForEach-Object { "$top/$_" }
    if (($names -join ', ') -ne ($expected -join ', ')) { throw "the zip holds $($names -join ', ')" }
    foreach ($entry in $archive.Entries) {
        if ($entry.Length -eq 0) { throw "$($entry.FullName) is empty" }
    }
    $completions = @{ 'completions/pitboard.ps1' = 'Register-ArgumentCompleter'; 'completions/pitboard.bash' = 'complete -F' }
    foreach ($name in $completions.Keys) {
        $reader = [IO.StreamReader]::new($archive.GetEntry("$top/$name").Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if (-not $text.Contains($completions[$name])) { throw "$name is not the shell's completions" }
    }
    $packed = Join-Path $work 'packed.exe'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry("$top/pitboard.exe"), $packed)
    if ((Get-FileHash -LiteralPath $packed).Hash -ne (Get-FileHash -LiteralPath $exePath).Hash) {
        throw 'the zip holds another pitboard.exe'
    }
} finally {
    $archive.Dispose()
}
Write-Output "ok: the zip holds $($names.Count) files in $top, named with /"

$tainted = Join-Path $work 'tainted\pitboard.exe'
New-Item -ItemType Directory -Path (Split-Path -Parent $tainted) | Out-Null
Copy-Item -LiteralPath $exePath -Destination $tainted
[IO.File]::AppendAllText($tainted, 'PITBOARD_TEST_CLAUDE_DESKTOP')
Assert-Refused 'a build that holds a variable only a build for tests reads' @{ Exe = $tainted; Triple = $Triple } 'only a build for tests reads'

Assert-Refused 'a build that names another version' @{ Exe = $exePath; Triple = $Triple; Version = '0.0.1' } "not 'pitboard 0\.0\.1'"

$other = if ($Triple -eq 'x86_64-pc-windows-msvc') { 'aarch64-pc-windows-msvc' } else { 'x86_64-pc-windows-msvc' }
Assert-Refused 'a build for the other machine' @{ Exe = $exePath; Triple = $other } 'is not a program for'

$flags = @{ RUSTFLAGS = $env:RUSTFLAGS; CARGO_ENCODED_RUSTFLAGS = $env:CARGO_ENCODED_RUSTFLAGS }
try {
    $env:CARGO_ENCODED_RUSTFLAGS = $null
    $env:RUSTFLAGS = '-C target-feature=+crt-static --cfg pitboard_unreleased_windows'
    Assert-Refused 'a release whose RUSTFLAGS open it' @{ Exe = $exePath; Triple = $Triple; Release = $true } '^RUSTFLAGS opens'
    $env:RUSTFLAGS = '-C target-feature=+crt-static'
    $env:CARGO_ENCODED_RUSTFLAGS = "-C`u{1f}target-feature=+crt-static`u{1f}--cfg`u{1f}pitboard_unreleased_windows"
    Assert-Refused 'a release whose CARGO_ENCODED_RUSTFLAGS open it' @{ Exe = $exePath; Triple = $Triple; Release = $true } 'CARGO_ENCODED_RUSTFLAGS opens'
    $env:CARGO_ENCODED_RUSTFLAGS = $null
    & $check -Exe $exePath -Triple $Triple -Release | Out-Null
    Write-Output 'ok: a build with neither passes as a release'
} finally {
    $env:RUSTFLAGS = $flags.RUSTFLAGS
    $env:CARGO_ENCODED_RUSTFLAGS = $flags.CARGO_ENCODED_RUSTFLAGS
}

Remove-Item -Recurse -Force -LiteralPath $work
Write-Output 'Every case passed.'
exit 0
