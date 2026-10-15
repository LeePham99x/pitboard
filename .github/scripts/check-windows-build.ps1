# Checks a pitboard.exe for Windows, for CI and the release alike: its machine, its C runtime,
# its version, and the release gate as the version's major decides it.
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [ValidateSet('x86_64-pc-windows-msvc', 'aarch64-pc-windows-msvc')] [string] $Triple,
    # What --version names. Cargo.toml's when left out.
    [string] $Version,
    # Built as release.yml builds it, in this environment.
    [switch] $Release,
    # Built for the tests, with --cfg pitboard_unreleased_windows. Run as a 1.x build is, and
    # failed only when it fails to start or crashes, or with -Strict on anything a 1.x build fails on.
    [switch] $Opened,
    [switch] $Strict,
    # Lists Credential Manager after the runs as a standard user, for the leak check.
    [string] $Probe
)
$ErrorActionPreference = 'Stop'

# A GitHub-hosted runner is thrown away after the job.
if (-not ($env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted')) {
    throw 'check-windows-build.ps1 runs only on a GitHub-hosted runner: it runs the pitboard.exe it is given, from 1.0.0 as a Windows account it makes'
}
if ($Strict -and -not $Opened) { throw '-Strict goes with -Opened: a 1.x build is always held to it' }

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Version) {
    $section = ''
    foreach ($line in Get-Content -LiteralPath (Join-Path $root 'Cargo.toml')) {
        if ($line -match '^\[(.+)\]\s*$') {
            $section = $Matches[1]
        } elseif ($section -eq 'workspace.package' -and $line -match '^version = "(.+)"') {
            $Version = $Matches[1]
            break
        }
    }
    if (-not $Version) { throw 'Cargo.toml names no version under [workspace.package]' }
}
$machine = @{ 'x86_64-pc-windows-msvc' = 'AMD64'; 'aarch64-pc-windows-msvc' = 'ARM64' }[$Triple]
$exePath = (Resolve-Path -LiteralPath $Exe).Path

$readobj = Get-Command llvm-readobj -ErrorAction SilentlyContinue
$readobj = if ($readobj) { $readobj.Source } else { "$env:ProgramFiles\LLVM\bin\llvm-readobj.exe" }
$headers = & $readobj --file-headers $exePath | Out-String
if ($LASTEXITCODE -ne 0) { throw "llvm-readobj could not read $exePath" }
if ($headers -notmatch "Machine: IMAGE_FILE_MACHINE_$machine\b") {
    Write-Output $headers
    throw "pitboard.exe is not a program for $machine"
}
$imports = & $readobj --coff-imports $exePath | Out-String
if ($LASTEXITCODE -ne 0) { throw "llvm-readobj could not read $exePath's imports" }
if ($imports -match '(?i)vcruntime|api-ms-win-crt') {
    Write-Output $imports
    throw 'pitboard.exe takes its C runtime from a DLL: it was built without +crt-static'
}

$said = (& $exePath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "pitboard --version exited $LASTEXITCODE" }
if ($said -ne "pitboard $Version") { throw "pitboard.exe --version says '$said', not 'pitboard $Version'" }
Write-Output "$said, for $machine, with the C runtime inside it"

# A build for the tests has test-support, which reads this variable.
if (-not $Opened) {
    $bytes = [IO.File]::ReadAllBytes($exePath)
    if ([Text.Encoding]::Latin1.GetString($bytes).Contains('PITBOARD_TEST_CLAUDE_DESKTOP')) {
        throw 'pitboard.exe holds a variable only a build for tests reads'
    }
}

# RUSTFLAGS gives way only to CARGO_ENCODED_RUSTFLAGS. A dev-dependency's features are in
# cargo tree's features edges unless the edge kinds are named.
if ($Release) {
    foreach ($name in 'RUSTFLAGS', 'CARGO_ENCODED_RUSTFLAGS') {
        $flags = [Environment]::GetEnvironmentVariable($name)
        if ($flags -and $flags.Contains('pitboard_unreleased_windows')) {
            throw "$name opens the build to Windows before its release: $flags"
        }
    }
    $manifest = Join-Path $root 'Cargo.toml'
    $tree = & cargo tree --locked --manifest-path $manifest -e 'normal,build,features' --target $Triple -p pitboard | Out-String
    if ($LASTEXITCODE -ne 0) { throw "cargo tree exited $LASTEXITCODE" }
    if ($tree -match 'feature "(test-support|fixture)"') {
        throw "the release resolves the $($Matches[1]) feature, which only a build for tests has"
    }
    Write-Output 'Built with neither the cfg nor a feature for tests.'
}

function Invoke-Exe([string] $Program, [string[]] $Arguments, [hashtable] $Environment) {
    $start = [Diagnostics.ProcessStartInfo]::new($Program)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($name in $Environment.Keys) { $start.Environment[$name] = $Environment[$name] }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($start)
    $err = $process.StandardError.ReadToEndAsync()
    $out = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ Exit = $process.ExitCode; Out = $out; Err = $err.Result }
}

$major = [int](($Version -split '\.')[0])
if ($major -eq 0 -and -not $Opened) {
    # Each run gets these, never this script: Cargo and rustup find their homes through USERPROFILE.
    $scratch = Join-Path $env:RUNNER_TEMP "release-gate-$([guid]::NewGuid().ToString('N'))"
    $homes = @{}
    foreach ($folder in 'USERPROFILE', 'HOME', 'APPDATA', 'LOCALAPPDATA', 'CODEX_HOME', 'CLAUDE_CONFIG_DIR', 'PITBOARD_HOME') {
        $homes[$folder] = Join-Path $scratch $folder
        New-Item -ItemType Directory -Force -Path $homes[$folder] | Out-Null
    }
    $commands = @(
        @('status', '--json'),
        @('use', 'x', '--json'),
        @('renew', '--json'),
        @('schedule', 'install', '--json')
    )
    foreach ($command in $commands) {
        $run = Invoke-Exe $exePath $command $homes
        $code = try { ($run.Out | ConvertFrom-Json).error.code } catch { $null }
        if ($run.Exit -ne 1 -or $code -ne 'windows_not_released') {
            throw "pitboard $($command -join ' ') exited $($run.Exit) with $code`: $($run.Out)$($run.Err)"
        }
    }
    foreach ($folder in $homes.Keys) {
        $left = @(Get-ChildItem -Force -LiteralPath $homes[$folder])
        if ($left.Count -ne 0) { throw "$folder holds $($left.Name -join ', ')" }
    }
    Remove-Item -Recurse -Force -LiteralPath $scratch
    Write-Output 'A 0.x build refuses every change with windows_not_released, and leaves every folder empty.'
    exit 0
}

# winget's validation runs a portable package's program with no argument, and wants 0.
$asUser = Join-Path $PSScriptRoot 'test-as-standard-user.ps1'
$given = @{ Exe = $exePath; Commands = @('', 'doctor --json') }
if ($Probe) { $given.Probe = $Probe }
$runs = [Collections.Generic.List[object]]::new()
& $asUser @given | ForEach-Object { if ($_ -is [string]) { $_ } else { $runs.Add($_) } }
if ($runs.Count -ne 2) { throw "test-as-standard-user.ps1 gave $($runs.Count) runs, not 2" }
$bare, $doctor = $runs
foreach ($run in $runs) {
    # Pitboard exits 0 to 255. Anything else is a Windows status, such as an unhandled exception's.
    if ($run.Exit -lt 0 -or $run.Exit -gt 255) {
        throw "pitboard $($run.Command) ended with Windows status $('0x{0:X8}' -f $run.Exit): it failed to start or crashed"
    }
}
$envelope = try { $doctor.Out | ConvertFrom-Json } catch { $null }
Write-Output "As a fresh standard user, pitboard with no argument exited $($bare.Exit), and pitboard doctor --json $($doctor.Exit)."
if ($Opened -and -not $Strict) { exit 0 }
if ($bare.Exit -ne 0) {
    throw "pitboard with no argument exited $($bare.Exit): winget's validation runs it so, and wants 0"
}
if (($doctor.Exit -notin @(0, 3)) -or ($envelope.command -ne 'doctor')) {
    throw "pitboard doctor --json did not answer: it exited $($doctor.Exit)"
}
exit 0
