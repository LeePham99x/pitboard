# The job's user is elevated in every program it starts, so the tests run as a fresh standard
# user: nextest's, from the test programs and metadata the job's user built, and test programs
# that need nothing from the job user's profile, each run as it is. A WinUI test program, whose
# windows fail for that user in the job user's session, runs as the job's user inside the same
# leak check. With -Exe, the user runs one program in scratch homes, and each run comes back as
# an object.
# Bound strictly, so a misspelt option fails rather than lands in $args.
[CmdletBinding()]
param(
    # Cargo's options for the build, as `cargo nextest list` takes them.
    [string] $Cargo,
    # Self-contained test programs, such as a published Microsoft.Testing.Platform one, inside
    # the workspace.
    [string[]] $Dotnet,
    # Such programs to run as the job's user.
    [string[]] $JobDotnet,
    [string] $Exe,
    # Its argument lines, run in turn; an empty one runs it with none.
    [string[]] $Commands = @(''),
    # Built already, inside the workspace the user is granted read and run on. Without it, -Exe
    # leaves Credential Manager out of the leak check.
    [string] $Probe,
    # A folder on a volume the job mounted, which the tests find in PITBOARD_TEST_VOLUME.
    [string] $Volume
)
$ErrorActionPreference = 'Stop'

if (-not $Cargo -and -not $Dotnet -and -not $JobDotnet -and -not $Exe) {
    throw 'nothing to run: give -Cargo, -Dotnet, -JobDotnet, -Exe or a mix'
}
if (-not $Probe -and ($Cargo -or $Dotnet -or $JobDotnet)) {
    throw 'the tests need -Probe for the leak check'
}

# A GitHub-hosted runner is thrown away after the job.
if (-not ($env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted')) {
    throw 'test-as-standard-user.ps1 runs only on a GitHub-hosted runner: it makes a Windows account, grants it rights on folders and may change who holds the symbolic-link right'
}

function Split-Words([string] $Text) {
    @($Text -split '\s+' | Where-Object { $_ })
}

$probePath = $null
if ($Probe) {
    if (-not (Test-Path -LiteralPath $Probe -PathType Leaf)) {
        throw "no measurement probe at $Probe for the leak check: build it first, with cargo build --locked -p pitboard-probe"
    }
    $probePath = (Resolve-Path -LiteralPath $Probe).Path
}
if ($Exe) {
    if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "no program at $Exe" }
    $exePath = (Resolve-Path -LiteralPath $Exe).Path
}
$volumePath = $null
if ($Volume) {
    if (-not (Test-Path -LiteralPath $Volume -PathType Container)) { throw "no folder at $Volume for the tests' volume" }
    $volumePath = (Resolve-Path -LiteralPath $Volume).Path
}

# Any of these present would give a test the runner's settings rather than its own.
function Get-MachineLayers {
    @(
        (Join-Path $env:ProgramData 'OpenAI\Codex'),
        'C:\Program Files\ClaudeCode',
        'HKLM:\SOFTWARE\Policies\ClaudeCode',
        'HKCU:\SOFTWARE\Policies\ClaudeCode'
    ) | Where-Object { Test-Path -LiteralPath $_ }
}

# The app's data folders hold its account windows' sign-ins.
function Get-LoginPlaces([string] $ProfileDir, [string] $LocalData) {
    @(
        (Join-Path $ProfileDir '.claude'),
        (Join-Path $ProfileDir '.claude.json'),
        (Join-Path $ProfileDir '.codex'),
        (Join-Path $LocalData 'Pitboard'),
        (Join-Path $LocalData 'Pitboard App'),
        (Join-Path $LocalData 'Pitboard App Debug')
    )
}

# The AppUserModelIDs AppNotificationManager.Register keeps a key for: the app's two builds and
# Pitboard.Probe's.
$notifiers = 'Pitboard.Pitboard', 'Pitboard.Pitboard.Debug', 'Pitboard.Probe'

# What only an installed Release registers, under the job user's HKCU: its link schemes, its
# notifications, the start at sign-in and the command line on the user PATH.
function Get-JobRegistrations {
    foreach ($scheme in 'pitboard', 'pitboard-debug') {
        if (Test-Path -LiteralPath "HKCU:\Software\Classes\$scheme") { "HKCU\Software\Classes\$scheme" }
    }
    foreach ($aumid in $notifiers) {
        if (Test-Path -LiteralPath "HKCU:\Software\Classes\AppUserModelId\$aumid") { "HKCU\Software\Classes\AppUserModelId\$aumid" }
    }
    $run = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Pitboard' -ErrorAction SilentlyContinue
    if ($null -ne $run) { 'the Run value Pitboard' }
    if ([Environment]::GetEnvironmentVariable('Path', 'User') -match 'Programs\\Pitboard') { 'a user PATH naming Programs\Pitboard' }
}

# Every user's, which the job user may list. Through Windows PowerShell, whose Appx module
# PowerShell 7 does not load on every image.
function Get-PitboardPackages {
    $names = & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -Command `
        "Get-AppxPackage -AllUsers -Name 'Pitboard*' | ForEach-Object { `$_.PackageFullName }"
    if ($LASTEXITCODE -ne 0) { throw 'Get-AppxPackage could not list the packages' }
    @($names | Where-Object { $_ })
}

$layers = @(Get-MachineLayers)
if ($layers.Count -gt 0) {
    throw "this runner is not hermetic for the tests, since it holds $($layers -join ', '), from which Codex or Claude Code may take settings on every account"
}
Write-Output 'Codex''s %ProgramData%\OpenAI\Codex, Claude Code''s C:\Program Files\ClaudeCode and its policy keys are absent from this runner.'
# What the job user has already is the image's; only what appears during the run is a leak.
$jobPlaces = @(Get-LoginPlaces $env:USERPROFILE $env:LOCALAPPDATA)
$jobPlacesBefore = @($jobPlaces | Where-Object { Test-Path -LiteralPath $_ })
Write-Output "Of the job user's own login places, $($jobPlacesBefore.Count) of $($jobPlaces.Count) are there before the tests run."
$jobRegistrationsBefore = @(Get-JobRegistrations)
$packagesBefore = @(Get-PitboardPackages)

function Test-Inside([string] $Path, [string] $Folder) {
    $Path.StartsWith($Folder.TrimEnd('\') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

$workspace = (Resolve-Path -LiteralPath $env:GITHUB_WORKSPACE).Path
$readable = @($workspace)
if ($Cargo) {
    # Built as the job's user: Cargo and rustup are in its profile, which a standard user cannot
    # open. Given both metadata files, nextest runs what was built without them.
    $binaries = & cargo nextest list --list-type binaries-only --message-format json @(Split-Words $Cargo)
    if ($LASTEXITCODE -ne 0) { throw "cargo nextest list $Cargo failed" }
    $cargoMetadata = & cargo metadata --format-version 1 --locked
    if ($LASTEXITCODE -ne 0) { throw 'cargo metadata failed' }
    $metadata = ($cargoMetadata | Out-String) | ConvertFrom-Json
    $nextest = (Get-Command -Name cargo-nextest -CommandType Application).Source
    # Where the ci profile writes its report.
    $store = Join-Path $metadata.target_directory 'nextest'
    if (-not (Test-Inside $metadata.target_directory $workspace)) {
        $readable += $metadata.target_directory
    }
}
function Resolve-TestProgram([string] $Program) {
    if (-not (Test-Path -LiteralPath $Program -PathType Leaf)) { throw "no test program at $Program to run: build it first" }
    $path = (Resolve-Path -LiteralPath $Program).Path
    if (-not (Test-Inside $path $workspace)) { throw "$path is not inside the workspace, $workspace" }
    $path
}
$programs = @(foreach ($program in $Dotnet) { Resolve-TestProgram $program })
$jobPrograms = @(foreach ($program in $JobDotnet) { Resolve-TestProgram $program })

$name = 'pbtest' + (Get-Random -Minimum 10000 -Maximum 99999)
$secure = ConvertTo-SecureString -String ([guid]::NewGuid().ToString('N') + 'Aa1!') -AsPlainText -Force
$cred = [pscredential]::new($name, $secure)
$logs = Join-Path $env:RUNNER_TEMP "test-as-$name"
New-Item -ItemType Directory -Force -Path $logs | Out-Null
# At the drive root, as runner-facts.ps1 ran it: a folder it cannot open fails with ERROR_DIRECTORY.
$runDir = Join-Path "$env:SystemDrive\" "pitboard-test-as-$name"

function Start-AsUser([string] $Program, [string[]] $Arguments, [string] $In, [hashtable] $Environment, [string] $Label, [switch] $JobUser) {
    $out = Join-Path $logs "$Label.out.txt"
    $err = Join-Path $logs "$Label.err.txt"
    $start = @{
        FilePath = $Program
        WorkingDirectory = $In
        Wait = $true
        PassThru = $true
        RedirectStandardOutput = $out
        RedirectStandardError = $err
    }
    if ($JobUser) {
        $start.NoNewWindow = $true
    } else {
        $start.Credential = $cred
        $start.LoadUserProfile = $true
    }
    if ($Arguments) { $start.ArgumentList = $Arguments }
    if ($Environment) { $start.Environment = $Environment }
    $process = Start-Process @start
    [pscustomobject]@{ Exit = $process.ExitCode; Out = $out; Err = $err }
}

# Workflow commands off, so no line a test prints is read as one.
function Show-Run($Run) {
    $token = [guid]::NewGuid().ToString('N')
    Write-Output "::stop-commands::$token"
    foreach ($file in $Run.Out, $Run.Err) {
        $text = Get-Content -Raw -Path $file -ErrorAction SilentlyContinue
        if ($text) { Write-Output $text.TrimEnd() }
    }
    Write-Output "::$token::"
}

# A program that fails fast, as WinUI does on an exception nothing handled, prints nothing of it. Windows
# Error Reporting's events name the module and the code.
function Show-Crashes([datetime] $Since) {
    $filter = @{ LogName = 'Application'; ProviderName = 'Application Error', '.NET Runtime'; StartTime = $Since }
    $crashes = @(Get-WinEvent -FilterHashtable $filter -ErrorAction SilentlyContinue | Sort-Object TimeCreated)
    if ($crashes.Count -eq 0) { return }
    Write-Output "::group::The Application log's crashes since the tests started: $($crashes.Count)"
    $token = [guid]::NewGuid().ToString('N')
    Write-Output "::stop-commands::$token"
    foreach ($crash in $crashes) {
        Write-Output "$($crash.TimeCreated.ToString('HH:mm:ss')) $($crash.ProviderName) $($crash.Id): $($crash.Message)"
    }
    Write-Output "::$token::"
    Write-Output '::endgroup::'
}

# SIDs are compared and written back, never printed.
function Get-SymlinkHolders {
    $export = Join-Path $logs 'rights.inf'
    secedit /export /cfg $export /areas USER_RIGHTS *> $null
    $line = Get-Content -Path $export | Where-Object { $_ -match '^SeCreateSymbolicLinkPrivilege\s*=' } | Select-Object -First 1
    Remove-Item -Force -Path $export -ErrorAction SilentlyContinue
    if (-not $line) { return '' }
    ((($line -split '=', 2)[1] -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object) -join ','
}

function Set-SymlinkHolders([string] $Holders) {
    $inf = Join-Path $logs 'rights-set.inf'
    $db = Join-Path $logs 'rights-set.sdb'
    @(
        '[Unicode]', 'Unicode=yes',
        '[Version]', 'signature="$CHICAGO$"', 'Revision=1',
        '[Privilege Rights]', "SeCreateSymbolicLinkPrivilege = $Holders"
    ) | Set-Content -Path $inf -Encoding Unicode
    secedit /configure /db $db /cfg $inf /areas USER_RIGHTS *> $null
    $exit = $LASTEXITCODE
    Remove-Item -Force -Path $inf, $db -ErrorAction SilentlyContinue
    if ($exit -ne 0) { throw "secedit could not set the symbolic-link right (exit $exit)" }
}

# icacls names the files it could not change, never a secret.
function Set-UserRights([string] $Folder, [string] $Rights) {
    $said = if ($Rights) {
        icacls $Folder /grant "*${sid}:(OI)(CI)$Rights" /Q 2>&1 | Out-String
    } else {
        icacls $Folder /remove:g "*$sid" /Q 2>&1 | Out-String
    }
    if ($LASTEXITCODE -ne 0) { throw "icacls could not set the standard user's rights on $Folder (exit $LASTEXITCODE): $said" }
}

$user = New-LocalUser -Name $name -Password $secure -AccountNeverExpires -PasswordNeverExpires
$sid = $user.SID.Value
$holders = $null
$granted = [System.Collections.Generic.List[string]]::new()
$failed = [System.Collections.Generic.List[string]]::new()
try {
    Add-LocalGroupMember -Group 'Users' -Member $name -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    Set-UserRights $runDir 'M'
    foreach ($folder in $readable) {
        $granted.Add($folder)
        Set-UserRights $folder 'RX'
    }
    # exFAT and FAT32 keep no access list, and every account may write there.
    if ($volumePath -and ([System.IO.DriveInfo]::new($volumePath).DriveFormat -notin 'exFAT', 'FAT32', 'FAT')) {
        $granted.Add($volumePath)
        Set-UserRights $volumePath 'M'
    }
    if ($Cargo) {
        New-Item -ItemType Directory -Force -Path $store | Out-Null
        $granted.Add($store)
        Set-UserRights $store 'M'
        $userNextest = Join-Path $runDir (Split-Path -Leaf $nextest)
        Copy-Item -LiteralPath $nextest -Destination $userNextest
        $binariesFile = Join-Path $runDir 'binaries-metadata.json'
        $binaries | Set-Content -Path $binariesFile
        $cargoMetadataFile = Join-Path $runDir 'cargo-metadata.json'
        $cargoMetadata | Set-Content -Path $cargoMetadataFile
    }
    if ($Exe) {
        # A copy in the run's folder, which the user may run wherever the program was built.
        $userExe = Join-Path $runDir (Split-Path -Leaf $exePath)
        Copy-Item -LiteralPath $exePath -Destination $userExe
    }

    $devKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    $developerMode = (Get-ItemProperty -Path $devKey -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense -eq 1
    if ($developerMode) {
        Write-Output 'Developer Mode is on, so the standard user can make symbolic links as it is.'
    } else {
        $holders = Get-SymlinkHolders
        Set-SymlinkHolders $(if ($holders) { "$holders,*$sid" } else { "*$sid" })
        Write-Output 'Developer Mode is off, so the standard user is given the symbolic-link right for this run.'
    }

    # The first logon makes the profile.
    $made = Start-AsUser "$env:SystemRoot\System32\cmd.exe" @('/d', '/c', 'exit', '0') $runDir $null 'profile'
    if ($made.Exit -ne 0) { Show-Run $made; throw "the standard user's first logon exited $($made.Exit)" }
    $profileDir = (Get-CimInstance -ClassName Win32_UserProfile | Where-Object { $_.SID -eq $sid }).LocalPath
    if (-not $profileDir) { throw 'the standard user has no profile after its first logon' }
    $userEnv = @{
        USERPROFILE = $profileDir
        HOMEDRIVE = Split-Path -Path $profileDir -Qualifier
        HOMEPATH = Split-Path -Path $profileDir -NoQualifier
        APPDATA = Join-Path $profileDir 'AppData\Roaming'
        LOCALAPPDATA = Join-Path $profileDir 'AppData\Local'
        TEMP = Join-Path $profileDir 'AppData\Local\Temp'
        TMP = Join-Path $profileDir 'AppData\Local\Temp'
        USERNAME = $name
        # Without it insta 1.48 runs `cargo metadata`, which is in the job user's profile.
        INSTA_WORKSPACE_ROOT = $workspace
        INSTA_UPDATE = 'no'
    }
    if ($volumePath) { $userEnv.PITBOARD_TEST_VOLUME = $volumePath }
    # Made by the user itself, so the folder is its own.
    if (-not (Test-Path -LiteralPath $userEnv.TEMP)) {
        $temp = Start-AsUser "$env:SystemRoot\System32\cmd.exe" @('/d', '/c', 'mkdir', $userEnv.TEMP) $runDir $userEnv 'temp'
        if ($temp.Exit -ne 0) { Show-Run $temp; throw "the standard user could not make its temporary folder" }
    }

    $testsStarted = Get-Date
    # Start-Process joins its arguments with spaces and quotes nothing, and none of these holds one.
    if ($Cargo) {
        $arguments = @('nextest', 'run', '--profile', 'ci', '--binaries-metadata', $binariesFile, '--cargo-metadata', $cargoMetadataFile)
        $run = Start-AsUser $userNextest $arguments $runDir $userEnv 'nextest'
        Show-Run $run
        if ($run.Exit -ne 0) { $failed.Add("nextest exited $($run.Exit)") }
    }
    for ($i = 0; $i -lt $programs.Count; $i++) {
        # Otherwise Microsoft.Testing.Platform's results go beside the program, where the user may only read.
        $arguments = @('--results-directory', (Join-Path $runDir "results-$i"))
        $run = Start-AsUser $programs[$i] $arguments $runDir $userEnv "dotnet-$i"
        Write-Output "::group::$($programs[$i]), as the standard user: $(if ($run.Exit -eq 0) { 'passed' } else { "exited $($run.Exit)" })"
        Show-Run $run
        Write-Output '::endgroup::'
        if ($run.Exit -ne 0) { $failed.Add("$($programs[$i]) exited $($run.Exit)") }
    }
    for ($i = 0; $i -lt $jobPrograms.Count; $i++) {
        $arguments = @('--results-directory', (Join-Path $runDir "job-results-$i"))
        $run = Start-AsUser $jobPrograms[$i] $arguments $runDir $null "job-dotnet-$i" -JobUser
        Write-Output "::group::$($jobPrograms[$i]), as the job's user: $(if ($run.Exit -eq 0) { 'passed' } else { "exited $($run.Exit)" })"
        Show-Run $run
        Write-Output '::endgroup::'
        if ($run.Exit -ne 0) { $failed.Add("$($jobPrograms[$i]) exited $($run.Exit) as the job's user") }
    }
    if ($failed.Count -gt 0) { Show-Crashes $testsStarted }
    if ($Exe) {
        $exeEnv = $userEnv.Clone()
        foreach ($folder in 'USERPROFILE', 'HOME', 'APPDATA', 'LOCALAPPDATA', 'CODEX_HOME', 'CLAUDE_CONFIG_DIR', 'PITBOARD_HOME') {
            $exeEnv[$folder] = Join-Path $runDir "homes\$folder"
            New-Item -ItemType Directory -Force -Path $exeEnv[$folder] | Out-Null
        }
        for ($i = 0; $i -lt $Commands.Count; $i++) {
            $run = Start-AsUser $userExe (Split-Words $Commands[$i]) $runDir $exeEnv "exe-$i"
            $label = "$(Split-Path -Leaf $exePath) $($Commands[$i])".TrimEnd()
            Write-Output "::group::$label, as the standard user: exited $($run.Exit)"
            Show-Run $run
            Write-Output '::endgroup::'
            [pscustomobject]@{ Command = $Commands[$i]; Exit = $run.Exit; Out = (Get-Content -Raw -Path $run.Out) }
        }
    }

    $leaked = [System.Collections.Generic.List[string]]::new()
    if ($probePath) {
        $leaks = Start-AsUser $probePath @('credman-names', '--leak-check') $runDir $userEnv 'leak-check'
        Write-Output "::group::Credential Manager's live login families and test items, as the standard user: $(if ($leaks.Exit -eq 0) { 'none' } else { 'FOUND OR UNREADABLE' })"
        Show-Run $leaks
        Write-Output '::endgroup::'
        if ($leaks.Exit -ne 0) {
            $leaked.Add("the leak check found a live login family's or a test's item in the standard user's Credential Manager, or could not list a family (exit $($leaks.Exit))")
        }
    } else {
        Write-Output 'No probe was given, so the leak check leaves Credential Manager out.'
    }
    # Every task, as the job's user, so a list that cannot be read fails rather than reads empty.
    $tasks = @(Get-ScheduledTask | Where-Object { $_.TaskPath -like '\Pitboard\*' })
    if ($tasks.Count -gt 0) { $leaked.Add("Task Scheduler's \Pitboard\ folder holds $($tasks.Count) task(s)") }
    foreach ($place in (Get-LoginPlaces $profileDir $userEnv.LOCALAPPDATA)) {
        if (Test-Path -LiteralPath $place) { $leaked.Add("the standard user's real $(Split-Path -Leaf $place) exists") }
    }
    $layers = @(Get-MachineLayers)
    if ($layers.Count -gt 0) { $leaked.Add("the machine's own layers appeared: $($layers -join ', ')") }
    foreach ($place in $jobPlaces) {
        if ((Test-Path -LiteralPath $place) -and ($jobPlacesBefore -notcontains $place)) {
            $leaked.Add("the job user's real $(Split-Path -Leaf $place) appeared")
        }
    }
    # reg.exe exits 0 where the key or value is there, as the standard user reads its own HKCU.
    $userKeys = [ordered]@{
        'HKCU\Software\Classes\pitboard' = @('query', 'HKCU\Software\Classes\pitboard')
        'HKCU\Software\Classes\pitboard-debug' = @('query', 'HKCU\Software\Classes\pitboard-debug')
        'the Run value Pitboard' = @('query', 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run', '/v', 'Pitboard')
    }
    foreach ($aumid in $notifiers) {
        $userKeys["HKCU\Software\Classes\AppUserModelId\$aumid"] = @('query', "HKCU\Software\Classes\AppUserModelId\$aumid")
    }
    $i = 0
    foreach ($key in $userKeys.Keys) {
        $query = Start-AsUser "$env:SystemRoot\System32\reg.exe" $userKeys[$key] $runDir $userEnv "registry-$i"
        $i++
        if ($query.Exit -eq 0) { $leaked.Add("the standard user's $key exists") }
    }
    $path = Start-AsUser "$env:SystemRoot\System32\reg.exe" @('query', 'HKCU\Environment', '/v', 'Path') $runDir $userEnv 'registry-path'
    if ($path.Exit -eq 0 -and (Get-Content -Raw -Path $path.Out) -match 'Programs\\Pitboard') {
        $leaked.Add("the standard user's PATH names Programs\Pitboard")
    }
    foreach ($registration in (Get-JobRegistrations)) {
        if ($jobRegistrationsBefore -notcontains $registration) { $leaked.Add("the job user's $registration appeared") }
    }
    $packages = @(Get-PitboardPackages | Where-Object { $packagesBefore -notcontains $_ })
    if ($packages.Count -gt 0) { $leaked.Add("a package named Pitboard* appeared: $($packages -join ', ')") }
    if ($leaked.Count -eq 0) {
        Write-Output "No $(if ($probePath) { 'login, test item, ' })task, login folder, registration or package was left behind."
    }
    $failed.AddRange($leaked)
} finally {
    if ($null -ne $holders) {
        Set-SymlinkHolders $holders
        if ((Get-SymlinkHolders) -ne $holders) { $failed.Add('the symbolic-link right was not set back as it was') }
    }
    foreach ($folder in $granted) {
        try { Set-UserRights $folder '' } catch { $failed.Add($_.Exception.Message) }
    }
    Remove-LocalUser -Name $name -ErrorAction SilentlyContinue
    Get-CimInstance -ClassName Win32_UserProfile | Where-Object { $_.SID -eq $sid } |
        Remove-CimInstance -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -Path $runDir -ErrorAction SilentlyContinue
}
if ($failed.Count -gt 0) {
    throw "as a standard user: $($failed -join '; ')"
}
if ($Cargo -or $Dotnet) { Write-Output 'The tests passed as a standard user.' }
if ($JobDotnet) { Write-Output "The tests passed as the job's user." }
exit 0
