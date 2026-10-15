# The runner facts, gathered on a disposable GitHub-hosted Windows runner by windows-facts.yml,
# as the job's user and as throwaway users this makes. Each change it makes is undone in a
# finally: the probe's marker, VHDs, a ReFS or Dev Drive volume, an SMB share, Bun and
# proper-lockfile in C:\pitboard-probe-tools, three throwaway users and their passwords, tasks,
# the job user's HKCU\Environment values, the state of OpenSSH Server where the image has one,
# Developer Mode and the symbolic-link right.
#
# Everything goes into one JSON report at -Out, saved after each row, so a run cut short still
# leaves what it gathered. A fact that cannot be gathered is recorded as an error and does not
# fail the step. The step fails when the probe's own report is missing, or when any report the
# probe or a helper printed cannot be parsed: an unparsed one is kept with its parse error and
# text, which hold nothing secret, since the probe and the helpers redact what they print. No
# SID and no user name is written into the report.
param(
    [Parameter(Mandatory)] [string] $Probe,
    [Parameter(Mandatory)] [string] $Triple,
    [Parameter(Mandatory)] [string] $Out,
    # The probe built for x64, on windows-11-arm only, for the emulated process of RF-H1.
    [string] $ProbeX64 = '',
    # crates/pitboard-probe/helpers, copied to the tools folder for the Bun rows.
    [Parameter(Mandatory)] [string] $Helpers,
    [Parameter(Mandatory)] [string] $BunVersion,
    [Parameter(Mandatory)] [string] $BunZip,
    [Parameter(Mandatory)] [string] $BunSha256
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# A GitHub-hosted runner is thrown away after the job.
if (-not ($env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted')) {
    throw 'runner-facts.ps1 runs only on a GitHub-hosted runner: it makes Windows accounts, changes their passwords, mounts disks, shares a folder, starts OpenSSH Server and installs Bun for a run'
}

$Probe = (Resolve-Path -Path $Probe).Path
$Helpers = (Resolve-Path -Path $Helpers).Path
$scratch = Join-Path $env:RUNNER_TEMP 'pitboard-probe-scratch'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
# The probe looks for its marker in the folder FOLDERID_Profile names, which is what this
# returns.
$profileDir = [Environment]::GetFolderPath('UserProfile')
$marker = Join-Path $profileDir 'pitboard-probe-throwaway.marker'
$tools = 'C:\pitboard-probe-tools'
$toolHelpers = Join-Path $tools 'helpers'
$pwsh = (Get-Process -Id $PID).Path
$jobSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$usersSid = 'S-1-5-32-545'
$administratorsSid = 'S-1-5-32-544'
# The files of the reports that ConvertFrom-Json could not read. The step fails on any.
$script:unreadable = [System.Collections.Generic.List[string]]::new()
$report = [ordered]@{ triple = $Triple }

function Save-Report {
    $report | ConvertTo-Json -Depth 64 | Set-Content -Path $Out -Encoding utf8
}

# A report the probe or a helper wrote, parsed; $null when there is none. One that does not
# parse is kept as its parse error and its text, and named in $script:unreadable.
function Read-Json([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $text = Get-Content -Raw -LiteralPath $Path
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    try {
        return ($text | ConvertFrom-Json)
    } catch {
        $script:unreadable.Add((Split-Path -Leaf $Path))
        return [ordered]@{ unreadable = $true; parse_error = $_.Exception.Message; text = $text }
    }
}

function Invoke-Fact([scriptblock] $Block) {
    try { return (& $Block) } catch { return [ordered]@{ error = $_.Exception.Message } }
}

# One row: its result goes into $Result under $Key, and the report is saved.
function Invoke-Row($Result, [string] $Key, [scriptblock] $Block) {
    $Result[$Key] = Invoke-Fact $Block
    Save-Report
}

# Run the probe as the job's user with its report written to a file, and hand the report
# back. The probe writes only a file whose name begins pitboard-probe-. Its output goes to
# files rather than pipes: a browser it starts can hold an inherited pipe open long after it
# exits, and run 38685119129 waited on one until the job's time ran out.
function Invoke-Probe([string] $Label, [string[]] $ProbeArgs, [int] $TimeoutSeconds = 900) {
    $file = Join-Path $scratch "pitboard-probe-report-$Label.json"
    Remove-Item -Force -Path $file -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $Probe -ArgumentList (Format-Arguments (@('--out', $file) + $ProbeArgs)) -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $scratch "job-$Label-out.txt") -RedirectStandardError (Join-Path $scratch "job-$Label-err.txt")
    $null = $process.Handle
    $exited = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) {
        taskkill /PID $process.Id /T /F *> $null
        $null = $process.WaitForExit(15000)
    }
    $result = [ordered]@{ exit = $(if ($process.HasExited) { $process.ExitCode } else { $null }); report = (Read-Json $file) }
    if (-not $exited) { $result.timed_out = $true }
    $result
}

# Start-Process joins its arguments with spaces, so one holding a space is quoted here. None
# holds a quote or ends in a backslash.
function Format-Arguments([string[]] $Arguments) {
    ($Arguments | ForEach-Object { if ($_ -eq '' -or $_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
}

function New-Password {
    ConvertTo-SecureString -String ([guid]::NewGuid().ToString('N') + 'Aa1!') -AsPlainText -Force
}

function Get-PlainText([securestring] $Secure) {
    [System.Net.NetworkCredential]::new('', $Secure).Password
}

# The last lines a run wrote to standard error, for a run that failed, with the throwaway
# user's and the job user's names and profiles taken out.
function Get-ErrorTail([string] $Path, $User) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $text = (Get-Content -Path $Path -Tail 6 -ErrorAction SilentlyContinue) -join "`n"
    if (-not $text) { return $null }
    foreach ($pair in @(
            @($User.ProfileDir, '<profile>'), @($env:USERPROFILE, '<job-profile>'),
            @($User.Name, '<user>'), @($env:USERNAME, '<job-user>'))) {
        if ($pair[0]) { $text = $text -replace [regex]::Escape($pair[0]), $pair[1] }
    }
    if ($text.Length -gt 800) { $text = $text.Substring($text.Length - 800) }
    $text
}

# Start a program as $User, with its own profile's variables and any $Extra ones (a $null
# value removes one), its standard output and error sent to files the job reads.
#
# CI run 37660991344 started these with -LoadUserProfile and the job user's variables, which
# Start-Process hands a child unless told otherwise, -Credential or not: the user's Local
# AppData then named the job user's folder, which it cannot open, and SHGetKnownFolderPath
# failed. So once the first run has made the profile, every run is given the user's own
# profile variables with -Environment (PowerShell 7.4 on).
function Start-AsUser($User, [string] $Label, [string[]] $ArgumentList, [string] $FilePath = $Probe, [hashtable] $Extra = @{}) {
    $stdout = Join-Path $scratch "runas-$($User.Label)-$Label-out.txt"
    $stderr = Join-Path $scratch "runas-$($User.Label)-$Label-err.txt"
    $environment = @{}
    if ($User.Env) { foreach ($k in $User.Env.Keys) { $environment[$k] = $User.Env[$k] } }
    foreach ($k in $Extra.Keys) { $environment[$k] = $Extra[$k] }
    $start = @{
        FilePath = $FilePath
        Credential = $User.Cred
        WorkingDirectory = $User.RunDir
        LoadUserProfile = $true
        PassThru = $true
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
    }
    if ($ArgumentList) { $start.ArgumentList = Format-Arguments $ArgumentList }
    if ($environment.Count -gt 0) { $start.Environment = $environment }
    try {
        $process = Start-Process @start
        # Read now, so the exit code can still be read once the process has gone.
        $null = $process.Handle
        [pscustomobject]@{ Process = $process; Out = $stdout; Err = $stderr; User = $User; Failure = $null }
    } catch {
        [pscustomobject]@{ Process = $null; Out = $stdout; Err = $stderr; User = $User; Failure = $_.Exception.Message }
    }
}

# Wait for a run, ending it and its children after $TimeoutSeconds, and read what it printed:
# a report, or with -Text the last lines of plain text.
function Wait-Run($Run, [int] $TimeoutSeconds = 900, [switch] $Text) {
    if (-not $Run.Process) { return [ordered]@{ started = $false; error = $Run.Failure } }
    $exited = $Run.Process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) {
        taskkill /PID $Run.Process.Id /T /F *> $null
        $null = $Run.Process.WaitForExit(15000)
    }
    $exit = if ($Run.Process.HasExited) { $Run.Process.ExitCode } else { $null }
    $result = [ordered]@{
        started = $true
        environment = if ($Run.User.Env) { 'the user''s own profile variables' } else { 'the job user''s' }
        timed_out = -not $exited
        exit = $exit
        exit_hex = if ($null -ne $exit) { '0x{0:X8}' -f $exit } else { $null }
    }
    if ($Text) {
        $result.output = (Get-Content -Path $Run.Out -Tail 20 -ErrorAction SilentlyContinue) -join "`n"
    } else {
        $result.report = Read-Json $Run.Out
    }
    if ($exit -ne 0 -or ($null -eq $result.report -and -not $Text)) { $result.stderr_tail = Get-ErrorTail $Run.Err $Run.User }
    $result
}

function Invoke-AsUser($User, [string] $Label, [string[]] $ArgumentList, [string] $FilePath = $Probe, [hashtable] $Extra = @{}, [int] $TimeoutSeconds = 900, [switch] $Text) {
    Wait-Run (Start-AsUser $User $Label $ArgumentList $FilePath $Extra) $TimeoutSeconds -Text:$Text
}

# Wait until the file appears, for at most $Seconds.
function Wait-File([string] $Path, [int] $Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (-not (Test-Path -LiteralPath $Path) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    Test-Path -LiteralPath $Path
}

function Get-FreeLetter {
    [char[]](68..90) | Where-Object { -not (Test-Path -Path "$($_):\") } | Select-Object -Last 1
}

function Get-Policies {
    $ci = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction SilentlyContinue
    $nt = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $saferKey = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers'
    $devKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    [ordered]@{
        os = [ordered]@{
            product = $nt.ProductName
            edition = $nt.EditionID
            installation_type = $nt.InstallationType
            display_version = $nt.DisplayVersion
            current_build = $nt.CurrentBuild
            ubr = $nt.UBR
        }
        powershell = $PSVersionTable.PSVersion.ToString()
        smart_app_control_state = if ($ci) { $ci.VerifiedAndReputablePolicyState } else { $null }
        safer_default_level = if (Test-Path $saferKey) { (Get-ItemProperty -Path $saferKey).DefaultLevel } else { $null }
        safer_policy_present = Test-Path $saferKey
        applocker_effective_rules = Invoke-Fact {
            $p = Get-AppLockerPolicy -Effective
            ($p.RuleCollections | ForEach-Object { @($_).Count } | Measure-Object -Sum).Sum
        }
        wdac = Invoke-Fact {
            $g = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName 'Win32_DeviceGuard'
            [ordered]@{
                kernel_code_integrity = $g.CodeIntegrityPolicyEnforcementStatus
                user_mode_code_integrity = $g.UsermodeCodeIntegrityPolicyEnforcementStatus
            }
        }
        defender = Invoke-Fact {
            $m = Get-MpComputerStatus
            [ordered]@{
                service = $m.AMServiceEnabled
                antivirus = $m.AntivirusEnabled
                real_time = $m.RealTimeProtectionEnabled
                behavior_monitor = $m.BehaviorMonitorEnabled
                tamper_protected = $m.IsTamperProtected
            }
        }
        # RF-A1: how UAC treats an administrator here.
        uac = Invoke-Fact {
            $p = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
            [ordered]@{
                enable_lua = $p.EnableLUA
                consent_prompt_behavior_admin = $p.ConsentPromptBehaviorAdmin
                filter_administrator_token = $p.FilterAdministratorToken
                prompt_on_secure_desktop = $p.PromptOnSecureDesktop
            }
        }
        developer_mode = if (Test-Path $devKey) {
            (Get-ItemProperty -Path $devKey -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
        } else { $null }
        winget_present = $null -ne (Get-Command -Name 'winget' -ErrorAction SilentlyContinue)
        scoop_present = $null -ne (Get-Command -Name 'scoop' -ErrorAction SilentlyContinue)
        server_service = Invoke-Fact { [string] (Get-Service -Name 'LanmanServer').Status }
    }
}

# W16's volume test: can the job make, format and mount an exFAT or FAT32 VHD with diskpart,
# and what do the rename routes do there. Each volume's entry goes into $Results as soon as
# it is made, so what was gathered survives a later failure.
function Get-Vhds($Results) {
    $dir = 'C:\pitboard-probe-vhd'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    try {
        foreach ($fs in 'exfat', 'fat32') {
            $entry = [ordered]@{ fs = $fs }
            $Results.Add($entry)
            $file = Join-Path $dir "pitboard-probe-$fs.vhdx"
            $letter = Get-FreeLetter
            $create = Join-Path $dir "create-$fs.txt"
            @(
                "create vdisk file=`"$file`" maximum=256 type=expandable",
                "select vdisk file=`"$file`"",
                'attach vdisk',
                'create partition primary',
                "format fs=$fs quick label=PBPROBE",
                "assign letter=$letter"
            ) | Set-Content -Path $create -Encoding ascii
            try {
                $made = diskpart /s $create | Out-String
                $entry.diskpart_exit = $LASTEXITCODE
                $entry.mounted = Test-Path -Path "$($letter):\"
                if ($entry.mounted) {
                    $volumeScratch = "$($letter):\pitboard-probe"
                    $entry.volume = Invoke-Probe "volume-$fs" @('volume', '--scratch', $volumeScratch)
                    $entry.flush_dir = Invoke-Probe "flush-$fs" @('flush-dir', '--scratch', $volumeScratch)
                    $entry.remove_home_while_locked = Invoke-Probe "lfx-$fs" @('lockfileex', '--mode', 'remove-home', '--seconds', '5', '--scratch', $volumeScratch)
                } else {
                    $entry.diskpart_tail = ($made -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 4) -join ' | '
                }
            } catch {
                $entry.error = $_.Exception.Message
            } finally {
                $detach = Join-Path $dir "detach-$fs.txt"
                @("select vdisk file=`"$file`"", 'detach vdisk') | Set-Content -Path $detach -Encoding ascii
                diskpart /s $detach *> $null
                $entry.detach_exit = $LASTEXITCODE
                Remove-Item -Force -Path $file -ErrorAction SilentlyContinue
            }
        }
    } finally {
        Remove-Item -Recurse -Force -Path $dir -ErrorAction SilentlyContinue
    }
}

# RF-Bun and RF-Lock: Bun from its release zip, only when its SHA-256 is the one the workflow
# pinned or was given, and proper-lockfile from the committed bun.lock with install scripts
# off, both under $tools, which every local user may read and run. Hands back bun.exe's path,
# or $null.
function Install-ProbeTools($Result, [string] $Version, [string] $Zip, [string] $Sha256) {
    $Result.bun_version = $Version
    $Result.bun_zip = $Zip
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    $download = Join-Path $tools $Zip
    Invoke-WebRequest -Uri "https://github.com/oven-sh/bun/releases/download/bun-v$Version/$Zip" -OutFile $download -UseBasicParsing
    $Result.sha256_matches = (Get-FileHash -Algorithm SHA256 -Path $download).Hash -eq $Sha256
    if (-not $Result.sha256_matches) { throw "$Zip is not the build whose SHA-256 the workflow pinned; nothing of it runs" }
    $unpacked = Join-Path $tools 'unpacked'
    Expand-Archive -Path $download -DestinationPath $unpacked -Force
    $bunDir = Join-Path $tools 'bun'
    New-Item -ItemType Directory -Force -Path $bunDir | Out-Null
    $bun = Join-Path $bunDir 'bun.exe'
    Move-Item -Path (Join-Path $unpacked ([IO.Path]::GetFileNameWithoutExtension($Zip) + '\bun.exe')) -Destination $bun
    Remove-Item -Recurse -Force -Path $unpacked, $download
    $Result.bun_reports = (& $bun --version | Out-String).Trim()

    Copy-Item -Recurse -Path $Helpers -Destination $toolHelpers
    $install = Join-Path $scratch 'pitboard-probe-bun-install.txt'
    $env:BUN_INSTALL_CACHE_DIR = Join-Path $tools 'cache'
    try {
        & $bun install --cwd $toolHelpers --frozen-lockfile --ignore-scripts *> $install
        $Result.install_exit = $LASTEXITCODE
    } finally {
        Remove-Item -Path Env:BUN_INSTALL_CACHE_DIR -ErrorAction SilentlyContinue
    }
    $Result.resolved = [ordered]@{}
    foreach ($package in 'proper-lockfile', 'graceful-fs', 'retry', 'signal-exit') {
        $manifest = Join-Path $toolHelpers "node_modules\$package\package.json"
        $Result.resolved[$package] = if (Test-Path -LiteralPath $manifest) { (Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json).version } else { $null }
    }
    icacls $tools /grant "*${usersSid}:(OI)(CI)RX" /T /Q *> $null
    $Result.granted_exit = $LASTEXITCODE
    if ($Result.install_exit -ne 0) { $Result.install_tail = (Get-Content -Path $install -Tail 6) -join "`n" }
    $bun
}

# A throwaway local user in $GroupSids, its run folder at the drive root (one it cannot open
# fails with ERROR_DIRECTORY), its profile made by a first run, and the probe's marker in it.
# Handed back once the account exists, whatever fails after, so the caller removes it.
function New-ThrowawayUser([string] $Label, [string] $Name, [string[]] $GroupSids, $Result) {
    $secure = New-Password
    $made = New-LocalUser -Name $Name -Password $secure -AccountNeverExpires -PasswordNeverExpires
    $user = [pscustomobject]@{
        Label = $Label
        Name = $Name
        Sid = $made.SID.Value
        Secure = $secure
        Cred = [pscredential]::new($Name, $secure)
        RunDir = "C:\pitboard-probe-runas-$Label"
        ProfileDir = $null
        Env = $null
        Scratch = $null
        PasswordReset = $false
    }
    try {
        foreach ($group in $GroupSids) { Add-LocalGroupMember -SID $group -Member $made -ErrorAction SilentlyContinue }
        New-Item -ItemType Directory -Force -Path $user.RunDir | Out-Null
        icacls $user.RunDir /grant "*$($user.Sid):(OI)(CI)M" *> $null
        $Result.start_process_takes_environment = (Get-Command -Name 'Start-Process').Parameters.ContainsKey('Environment')
        $Result.logon = Invoke-AsUser $user 'logon' @('logon')
        $Result.unsigned_probe_ran = ($Result.logon.started -and $Result.logon.exit -eq 0)
        $user.ProfileDir = (Get-CimInstance -ClassName Win32_UserProfile | Where-Object { $_.SID -eq $user.Sid }).LocalPath
        $Result.profile_made = [bool] $user.ProfileDir
        if ($user.ProfileDir) {
            Set-Content -Path (Join-Path $user.ProfileDir 'pitboard-probe-throwaway.marker') -Value 'ci runner, disposable'
            if ($Result.start_process_takes_environment) {
                $user.Env = @{
                    USERPROFILE = $user.ProfileDir
                    HOMEDRIVE = Split-Path -Path $user.ProfileDir -Qualifier
                    HOMEPATH = Split-Path -Path $user.ProfileDir -NoQualifier
                    APPDATA = Join-Path $user.ProfileDir 'AppData\Roaming'
                    LOCALAPPDATA = Join-Path $user.ProfileDir 'AppData\Local'
                    TEMP = Join-Path $user.ProfileDir 'AppData\Local\Temp'
                    TMP = Join-Path $user.ProfileDir 'AppData\Local\Temp'
                    USERNAME = $Name
                }
            }
            $user.Scratch = Join-Path $user.ProfileDir 'AppData\Local\Temp\pitboard-probe-s'
        }
    } catch {
        $Result.setup_error = $_.Exception.Message
        $user.ProfileDir = $null
    }
    $user
}

# The user's leak check first, unless an administrator's reset has left its vault unreadable,
# then the user, its processes, its profile and its run folder go.
function Remove-ThrowawayUser($User, $Result) {
    if ($User.ProfileDir -and -not $User.PasswordReset) {
        $Result.leak_check = Invoke-AsUser $User 'leak-check' @('credman-names', '--leak-check')
    }
    taskkill /F /FI "USERNAME eq $($User.Name)" /IM * *> $null
    Start-Sleep -Seconds 3
    Remove-LocalUser -SID $User.Sid -ErrorAction SilentlyContinue
    Get-CimInstance -ClassName Win32_UserProfile | Where-Object { $_.SID -eq $User.Sid } |
        Remove-CimInstance -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -Path $User.RunDir -ErrorAction SilentlyContinue
    $Result.removed = $null -eq (Get-LocalUser -SID $User.Sid -ErrorAction SilentlyContinue)
    $Result.profile_removed = $null -eq (Get-CimInstance -ClassName Win32_UserProfile | Where-Object { $_.SID -eq $User.Sid })
}

# Developer Mode's registry value as it stands: whether the key and the value exist, and the
# value and its kind.
function Get-DevMode {
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    $name = 'AllowDevelopmentWithoutDevLicense'
    $state = [ordered]@{ key = (Test-Path -Path $key); has_value = $false; value = $null; kind = $null }
    if ($state.key) {
        $k = Get-Item -Path $key
        if ($k.GetValueNames() -contains $name) {
            $state.has_value = $true
            $state.value = $k.GetValue($name, $null, 'DoNotExpandEnvironmentNames')
            $state.kind = $k.GetValueKind($name)
        }
    }
    $state
}

# The accounts holding the symbolic-link right, as secedit exports them (SIDs, sorted, one
# string): compared only, never written into the report.
function Get-SymlinkHolders([string] $Export) {
    secedit /export /cfg $Export /areas USER_RIGHTS *> $null
    $line = Get-Content -Path $Export | Where-Object { $_ -match '^SeCreateSymbolicLinkPrivilege\s*=' } | Select-Object -First 1
    Remove-Item -Force -Path $Export -ErrorAction SilentlyContinue
    if (-not $line) { return '' }
    ((($line -split '=', 2)[1] -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object) -join ','
}

# A template that sets the symbolic-link right to exactly $Holders, and nobody else when it
# is empty, and applies it with a database of its own.
function Set-SymlinkHolders([string] $Holders, [string] $Label) {
    $inf = Join-Path $scratch "pitboard-probe-rights-$Label.inf"
    $db = Join-Path $scratch "pitboard-probe-rights-$Label.sdb"
    @(
        '[Unicode]', 'Unicode=yes',
        '[Version]', 'signature="$CHICAGO$"', 'Revision=1',
        '[Privilege Rights]', "SeCreateSymbolicLinkPrivilege = $Holders"
    ) | Set-Content -Path $inf -Encoding Unicode
    secedit /configure /db $db /cfg $inf /areas USER_RIGHTS *> $null
    $exit = $LASTEXITCODE
    Remove-Item -Force -Path $inf, $db -ErrorAction SilentlyContinue
    $exit
}

# W12 and W13's symbolic links: as the user is, with Developer Mode on for one run, and with
# the right granted for one run, each put back exactly.
function Measure-Symlinks($User, $Result) {
    $Result.symlink_as_is = Invoke-AsUser $User 'sym-as-is' @('symlink', '--scratch', (Join-Path $User.RunDir 'as-is'))
    # The key is made only when it is missing (New-Item -Force on an existing key would replace
    # it and drop its other values), and the value and its kind are put back exactly as they
    # were, or removed with the key when the run made them.
    try {
        $devKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
        $devBefore = Get-DevMode
        try {
            if (-not $devBefore.key) { New-Item -Path $devKey | Out-Null }
            Set-ItemProperty -Path $devKey -Name 'AllowDevelopmentWithoutDevLicense' -Value 1 -Type DWord
            $Result.symlink_developer_mode = Invoke-AsUser $User 'sym-devmode' @('symlink', '--scratch', (Join-Path $User.RunDir 'devmode'))
        } finally {
            if (-not $devBefore.key) {
                Remove-Item -Path $devKey -ErrorAction SilentlyContinue
            } elseif (-not $devBefore.has_value) {
                Remove-ItemProperty -Path $devKey -Name 'AllowDevelopmentWithoutDevLicense' -ErrorAction SilentlyContinue
            } else {
                Set-ItemProperty -Path $devKey -Name 'AllowDevelopmentWithoutDevLicense' -Value $devBefore.value -Type $devBefore.kind
            }
            $Result.developer_mode_restored_exactly = (
                (Get-DevMode | ConvertTo-Json -Compress) -eq ($devBefore | ConvertTo-Json -Compress))
        }
    } catch {
        $Result.developer_mode_error = $_.Exception.Message
    }
    # Added for this user beside the accounts that already held it, then set back to exactly
    # those accounts, which is nobody when the export had no line for it; read back and
    # compared.
    try {
        $holders = Get-SymlinkHolders (Join-Path $scratch 'pitboard-probe-rights-before.inf')
        $Result.right_held_by_anyone_before = [bool] $holders
        $grant = if ($holders) { "$holders,*$($User.Sid)" } else { "*$($User.Sid)" }
        $Result.right_granted_exit = Set-SymlinkHolders $grant 'grant'
        try {
            $Result.symlink_right_granted = Invoke-AsUser $User 'sym-right' @('symlink', '--scratch', (Join-Path $User.RunDir 'right'))
        } finally {
            $Result.right_restore_exit = Set-SymlinkHolders $holders 'restore'
            $Result.right_restored_exactly = (
                (Get-SymlinkHolders (Join-Path $scratch 'pitboard-probe-rights-after.inf')) -eq $holders)
        }
    } catch {
        $Result.right_error = $_.Exception.Message
    }
}

# RF-C4: the replace loop beside a probe reader and Bun's, first by MoveFileExW with readers
# that do not share delete, then by the POSIX rename with readers that do, Bun's holding each
# open 50 ms as a tool keeps its login open.
function Measure-ReplaceLoop($User, [string] $Folder, [string] $Label, [int] $Rounds, [int] $Seconds, [string] $Bun) {
    $readerLoop = Join-Path $toolHelpers 'reader-loop.mjs'
    $passes = [ordered]@{}
    foreach ($pass in @(
            @{ name = 'movefile_beside_readers_not_sharing_delete'; route = 'movefile'; share = 'no-delete'; hold = '0' },
            @{ name = 'posix_beside_readers_sharing_delete'; route = 'posix'; share = 'delete'; hold = '50' })) {
        $tag = "$Label-$($pass.route)"
        $reader = Start-AsUser $User "$tag-reader" @('replace-reader', '--scratch', $Folder, '--share', $pass.share, '--seconds', '300')
        $bunReader = if ($Bun) { Start-AsUser $User "$tag-bun" @($readerLoop, $Folder, '300', $pass.hold) -FilePath $Bun }
        $loop = Invoke-AsUser $User "$tag-loop" @('replace-loop', '--scratch', $Folder, '--rounds', [string] $Rounds,
            '--lead-seconds', '10', '--route', $pass.route, '--seconds', [string] $Seconds) -TimeoutSeconds ($Seconds + 180)
        $passes[$pass.name] = [ordered]@{
            loop = $loop
            probe_reader = Wait-Run $reader 120
            bun_reader = if ($bunReader) { Wait-Run $bunReader 120 } else { $null }
        }
        Save-Report
    }
    $passes
}

# RF-C5 and RF-C5r: the probe holds proper-lockfile's lock while Bun's proper-lockfile checks
# it, then the other way round, with one of Claude Code's two timings.
function Measure-Lock($User, [string] $Folder, [string] $Timing, [string] $Bun) {
    $lockLoop = Join-Path $toolHelpers 'proper-lockfile-loop.mjs'
    $hold = Start-AsUser $User "c5-$Timing-hold" @('lock', '--scratch', $Folder, '--mode', 'hold', '--seconds', '30', '--timing', $Timing)
    Start-Sleep -Seconds 2
    $bunCheck = Invoke-AsUser $User "c5-$Timing-bun-check" @($lockLoop, $Folder, 'check', '25', $Timing) -FilePath $Bun -TimeoutSeconds 120
    $probeHold = Wait-Run $hold 120
    $bunHold = Start-AsUser $User "c5-$Timing-bun-hold" @($lockLoop, $Folder, 'hold', '30', $Timing) -FilePath $Bun
    Start-Sleep -Seconds 3
    $probeCheck = Invoke-AsUser $User "c5-$Timing-check" @('lock', '--scratch', $Folder, '--mode', 'check', '--seconds', '25', '--timing', $Timing) -TimeoutSeconds 120
    [ordered]@{
        probe_holds = [ordered]@{ probe = $probeHold; bun_checks = $bunCheck }
        bun_holds = [ordered]@{ bun = (Wait-Run $bunHold 120); probe_checks = $probeCheck }
    }
}

# RF-C6: LockFileEx on a home's state.lock held by one process and read by another, then the
# home removed while a holder runs.
function Measure-LockFileEx($User, [string] $Folder, [string] $Label) {
    $hold = Start-AsUser $User "$Label-hold" @('lockfileex', '--scratch', $Folder, '--mode', 'hold', '--seconds', '15')
    $ready = Wait-File (Join-Path $Folder 'pitboard-probe-home\pitboard-probe-ready') 20
    $read = Invoke-AsUser $User "$Label-read" @('lockfileex', '--scratch', $Folder, '--mode', 'read')
    [ordered]@{
        holder_ready = $ready
        read = $read
        hold = Wait-Run $hold 60
        remove_home = Invoke-AsUser $User "$Label-remove-home" @('lockfileex', '--scratch', $Folder, '--mode', 'remove-home', '--seconds', '5')
    }
}

# RF-E1, RF-E3 and RF-D2's tasks: a probe task registered in $TaskFolder, run, read and
# deleted, by the job's user or by $User, with $In as the probe's scratch folder. With
# $Expect, the report the task's action writes there is waited for and read.
function Measure-Task($User, [string] $In, [string] $Label, [string] $TaskFolder, [string] $Action, [switch] $SidSuffix, [int] $WaitSeconds = 5, [string] $Expect = '') {
    $base = @('task', '--scratch', $In, '--label', $Label, '--folder', $TaskFolder)
    if ($SidSuffix) { $base += '--sid-suffix' }
    $call = {
        param([string] $Step, [string[]] $More)
        $all = $base + @('--action', $Step) + $More
        if ($User) { Invoke-AsUser $User "task-$Label-$TaskFolder-$Step" $all } else { Invoke-Probe "task-$Label-$TaskFolder-$Step" $all }
    }
    $result = [ordered]@{ register = (& $call 'register' @('--args', $Action)) }
    $result.run = & $call 'run' @()
    if ($Expect) { $result.output_appeared = Wait-File $Expect $WaitSeconds } else { Start-Sleep -Seconds $WaitSeconds }
    $result.read = & $call 'read' @()
    if ($Expect) { $result.output = Read-Json $Expect }
    $result.delete = & $call 'delete' @()
    $result
}

# RF-C3r: a 51 GB expandable VHDX made ReFS, as a Dev Drive where the system is Windows 11
# (format fs=refs is a server's) and plain where it is Windows Server; the rename routes, the
# replace loop and LockFileEx there, as the job's user and as $User.
function Measure-ReFs($User, $Result, [string] $Bun) {
    $dir = 'C:\pitboard-probe-refs'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $file = Join-Path $dir 'pitboard-probe-refs.vhdx'
    $letter = Get-FreeLetter
    $devDrive = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').InstallationType -ne 'Server'
    $Result.kind = if ($devDrive) { 'dev_drive' } else { 'refs' }
    $create = Join-Path $dir 'create.txt'
    $lines = @(
        "create vdisk file=`"$file`" maximum=52224 type=expandable",
        "select vdisk file=`"$file`"",
        'attach vdisk',
        'create partition primary'
    )
    if (-not $devDrive) { $lines += 'format fs=refs quick label=PBREFS' }
    $lines += "assign letter=$letter"
    $lines | Set-Content -Path $create -Encoding ascii
    try {
        $made = diskpart /s $create | Out-String
        $Result.diskpart_exit = $LASTEXITCODE
        if ($devDrive) {
            $Result.format = Invoke-Fact {
                $formatted = Format-Volume -DriveLetter $letter -DevDrive -NewFileSystemLabel 'PBDEV' -Confirm:$false -Force
                [ordered]@{ file_system = $formatted.FileSystem }
            }
            $Result.devdrv_query = (fsutil devdrv query "$($letter):" 2>&1 | Out-String).Trim()
        }
        $Result.mounted = Test-Path -Path "$($letter):\"
        if (-not $Result.mounted) {
            $Result.diskpart_tail = ($made -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 4) -join ' | '
            return
        }
        $root = "$($letter):\pitboard-probe"
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        icacls $root /grant "*$($User.Sid):(OI)(CI)M" *> $null
        $Result.job = [ordered]@{
            volume = Invoke-Probe 'refs-volume' @('volume', '--scratch', (Join-Path $root 'job'))
            flush_dir = Invoke-Probe 'refs-flush' @('flush-dir', '--scratch', (Join-Path $root 'job'))
        }
        Save-Report
        $userFolder = Join-Path $root 'user'
        $Result.user = [ordered]@{
            volume = Invoke-AsUser $User 'refs-volume' @('volume', '--scratch', $userFolder)
            flush_dir = Invoke-AsUser $User 'refs-flush' @('flush-dir', '--scratch', $userFolder)
        }
        Save-Report
        $Result.user.replace_loop = Measure-ReplaceLoop $User $userFolder 'refs' 2000 90 $Bun
        $Result.user.lockfileex = Measure-LockFileEx $User (Join-Path $root 'user-lfx') 'refs-lfx'
    } finally {
        $detach = Join-Path $dir 'detach.txt'
        @("select vdisk file=`"$file`"", 'detach vdisk') | Set-Content -Path $detach -Encoding ascii
        diskpart /s $detach *> $null
        $Result.detach_exit = $LASTEXITCODE
        Remove-Item -Recurse -Force -Path $dir -ErrorAction SilentlyContinue
        $Result.removed = -not (Test-Path -Path $dir)
    }
}

# RF-C3s: a folder shared over SMB to $User alone, mapped as P: inside a process of $User's
# own, where the probe reads the volume and flushes a folder.
function Measure-SmbShare($User, $Result) {
    $dir = 'C:\pitboard-probe-share'
    $share = 'pitboard-probe-share'
    $Result.server_service = [string] (Get-Service -Name 'LanmanServer' -ErrorAction SilentlyContinue).Status
    try {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        icacls $dir /grant "*$($User.Sid):(OI)(CI)M" *> $null
        New-SmbShare -Name $share -Path $dir -FullAccess "$env:COMPUTERNAME\$($User.Name)" | Out-Null
        $Result.shared = $true
        $mapper = Join-Path $User.RunDir 'pitboard-probe-c3s.ps1'
        @(
            'param([string] $Probe, [string] $Out)',
            "net use P: \\localhost\$share /persistent:no *> `$null",
            '"mapped $LASTEXITCODE"',
            '& $Probe --out (Join-Path $Out ''pitboard-probe-c3s-volume.json'') volume --scratch P:\pitboard-probe *> $null',
            '& $Probe --out (Join-Path $Out ''pitboard-probe-c3s-flush.json'') flush-dir --scratch P:\pitboard-probe *> $null',
            'net use P: /delete /y *> $null',
            '"unmapped $LASTEXITCODE"'
        ) | Set-Content -Path $mapper -Encoding ascii
        $Result.user_run = Invoke-AsUser $User 'c3s' @('-NoProfile', '-NonInteractive', '-File', $mapper, '-Probe', $Probe, '-Out', $User.RunDir) -FilePath $pwsh -Text
        $Result.volume = Read-Json (Join-Path $User.RunDir 'pitboard-probe-c3s-volume.json')
        $Result.flush_dir = Read-Json (Join-Path $User.RunDir 'pitboard-probe-c3s-flush.json')
    } finally {
        Remove-SmbShare -Name $share -Force -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force -Path $dir -ErrorAction SilentlyContinue
        $Result.unshared = $null -eq (Get-SmbShare -Name $share -ErrorAction SilentlyContinue)
    }
}

# Run a command over ssh as $User@localhost with the key, and read the report it prints.
function Invoke-Ssh($User, [string] $Label, [string[]] $SshArgs, [string] $Command) {
    $stdout = Join-Path $scratch "ssh-$Label-out.txt"
    $stderr = Join-Path $scratch "ssh-$Label-err.txt"
    & ssh.exe @SshArgs $Command > $stdout 2> $stderr
    $exit = $LASTEXITCODE
    $result = [ordered]@{ exit = $exit; report = (Read-Json $stdout) }
    if ($exit -ne 0) { $result.stderr_tail = Get-ErrorTail $stderr $User }
    $result
}

# Why sshd turns a session down, when it does: its effective settings for $User, who holds
# the network logon rights it needs, and its last events, with the user's name taken out.
function Get-SshdDiagnosis($User) {
    $sshd = Join-Path $env:SystemRoot 'System32\OpenSSH\sshd.exe'
    $wanted = '^(allowusers|allowgroups|denyusers|denygroups|authorizedkeysfile|pubkeyauthentication|passwordauthentication|authenticationmethods|strictmodes) '
    $export = Join-Path $scratch 'pitboard-probe-rights-ssh.inf'
    secedit /export /cfg $export /areas USER_RIGHTS *> $null
    $rights = Get-Content -Path $export -ErrorAction SilentlyContinue
    Remove-Item -Force -Path $export -ErrorAction SilentlyContinue
    $holders = {
        param([string] $Right)
        $line = $rights | Where-Object { $_ -match "^$Right\s*=" } | Select-Object -First 1
        $sids = if ($line) { @((($line -split '=', 2)[1] -split ',') | ForEach-Object { $_.Trim().TrimStart('*') }) } else { @() }
        [ordered]@{
            everyone = $sids -contains 'S-1-1-0'
            users = $sids -contains $usersSid
            authenticated_users = $sids -contains 'S-1-5-11'
            this_user = $sids -contains $User.Sid
            administrators = $sids -contains $administratorsSid
        }
    }
    [ordered]@{
        effective = Invoke-Fact {
            @(& $sshd -T -C "user=$($User.Name),host=localhost,addr=127.0.0.1" 2>&1 | ForEach-Object { "$_" } |
                Where-Object { $_ -match $wanted } | ForEach-Object { $_ -replace [regex]::Escape($User.Name), '<user>' })
        }
        network_logon_right = & $holders 'SeNetworkLogonRight'
        deny_network_logon_right = & $holders 'SeDenyNetworkLogonRight'
        events = Invoke-Fact {
            @(Get-WinEvent -LogName 'OpenSSH/Operational' -MaxEvents 15 -ErrorAction Stop | ForEach-Object {
                    (($_.Message -replace [regex]::Escape($User.Name), '<user>') -split "`r?`n")[0]
                })
        }
    }
}

# RF-M: a loopback ssh session as $User with a key, where sshd ships with the image, in which
# the probe reads the logon, DPAPI, Credential Manager and the processes of $User's other
# logon. The service is put back as it was. Run 38685119129 found no sshd on windows-11-arm,
# and Add-WindowsCapability did not install it within 10 minutes, so an image without one is
# recorded and left alone.
function Measure-OpenSsh($User, $Result) {
    $service = Get-Service -Name 'sshd' -ErrorAction SilentlyContinue
    $Result.sshd_present = [bool] $service
    if (-not $service) { return }
    $before = [ordered]@{ status = [string] $service.Status; start_type = [string] $service.StartType }
    $Result.before = $before
    $keyDir = Join-Path $scratch 'pitboard-probe-ssh'
    $sshDir = Join-Path $User.ProfileDir '.ssh'
    $holder = $null
    try {
        if ($before.start_type -eq 'Disabled') { Set-Service -Name 'sshd' -StartupType Manual }
        New-Item -ItemType Directory -Force -Path $keyDir | Out-Null
        $key = Join-Path $keyDir 'pitboard-probe-m-key'
        $keygen = Start-Process -FilePath 'ssh-keygen.exe' -ArgumentList (Format-Arguments @('-q', '-t', 'ed25519', '-N', '', '-C', 'pitboard-probe', '-f', $key)) `
            -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $scratch 'ssh-keygen-out.txt') -RedirectStandardError (Join-Path $scratch 'ssh-keygen-err.txt')
        $Result.key_made = ($keygen.ExitCode -eq 0) -and (Test-Path -LiteralPath "$key.pub")
        icacls $key /inheritance:r /grant:r "*${jobSid}:F" *> $null
        New-Item -ItemType Directory -Force -Path $sshDir | Out-Null
        $authorized = Join-Path $sshDir 'authorized_keys'
        Copy-Item -Path "$key.pub" -Destination $authorized
        foreach ($path in $sshDir, $authorized) {
            icacls $path /inheritance:r /grant:r "*$($User.Sid):F" "*S-1-5-18:F" "*${administratorsSid}:F" *> $null
        }
        if ($before.status -ne 'Running') { Start-Service -Name 'sshd' }
        $Result.sshd_status = [string] (Get-Service -Name 'sshd').Status

        # A codex.exe of $User's in its other logon, for processes to find from the session.
        $holderExe = Join-Path $User.RunDir 'm\codex.exe'
        New-Item -ItemType Directory -Force -Path (Split-Path $holderExe) | Out-Null
        Copy-Item -Path $Probe -Destination $holderExe
        $holder = Start-AsUser $User 'm-holder' @('lock', '--scratch', (Join-Path $User.Scratch 'm-holder'), '--mode', 'hold', '--seconds', '120') -FilePath $holderExe

        $byKey = @('-i', $key, '-o', 'BatchMode=yes', '-o', 'PreferredAuthentications=publickey', '-o', 'StrictHostKeyChecking=no',
            '-o', "UserKnownHostsFile=$keyDir\known_hosts", '-o', 'ConnectTimeout=20', "$($User.Name)@localhost")
        $rows = [ordered]@{
            logon = 'logon'
            dpapi = "dpapi --scratch $($User.Scratch)\m"
            credman_write = 'credman-item --action write --name pitboard-probe-m'
            credman_read = 'credman-item --action read --name pitboard-probe-m'
            credman_delete = 'credman-item --action delete --name pitboard-probe-m'
            credman_names = 'credman-names'
            processes = 'processes --find codex.exe'
        }
        $session = [ordered]@{}
        foreach ($row in $rows.Keys) {
            $session[$row] = Invoke-Ssh $User "key-$row" $byKey "$Probe $($rows[$row])"
            Save-Report
        }
        $Result.key = $session
        if ($session.logon.exit -ne 0) { $Result.diagnosis = Get-SshdDiagnosis $User }
    } finally {
        if ($holder -and $holder.Process -and -not $holder.Process.HasExited) { taskkill /PID $holder.Process.Id /T /F *> $null }
        Invoke-Probe 'm-cleanup' @('credman-item', '--action', 'delete', '--name', 'pitboard-probe-m') | Out-Null
        if ($before.status -ne 'Running') { Stop-Service -Name 'sshd' -Force -ErrorAction SilentlyContinue }
        Set-Service -Name 'sshd' -StartupType $before.start_type -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force -Path $keyDir, $sshDir -ErrorAction SilentlyContinue
        $after = Get-Service -Name 'sshd' -ErrorAction SilentlyContinue
        $Result.restored_exactly = ([string] $after.Status -eq $before.status) -and ([string] $after.StartType -eq $before.start_type)
    }
}

# The fresh standard user's rows, in the order the roadmap gives them, each saved as it lands.
function Measure-FreshUser($User, $Result, [string] $Bun, [string] $X64) {
    $s = $User.Scratch
    $Result.tokens = Invoke-AsUser $User 'tokens' @('tokens')
    $Result.homes = Invoke-AsUser $User 'homes' @('homes')
    Save-Report
    Measure-Symlinks $User $Result
    Save-Report

    Invoke-Row $Result 'rf_b1' {
        $fakeProfile = Join-Path $User.RunDir 'b1-profile'
        $fakeHome = Join-Path $User.RunDir 'b1-home'
        New-Item -ItemType Directory -Force -Path $fakeProfile, $fakeHome | Out-Null
        $bash = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
        $r = [ordered]@{
            userprofile_overridden = Invoke-AsUser $User 'b1-userprofile' @('homes') -Extra @{ USERPROFILE = $fakeProfile }
            home_set = Invoke-AsUser $User 'b1-home' @('homes') -Extra @{ HOME = $fakeHome }
            git_bash_present = Test-Path -LiteralPath $bash
        }
        if ($r.git_bash_present) {
            $r.from_git_bash = Invoke-AsUser $User 'b1-bash' @('-c', "'$($Probe -replace '\\', '/')' homes") -FilePath $bash
        }
        $r
    }
    Invoke-Row $Result 'rf_b1e' {
        $script = Join-Path $toolHelpers 'os-homedir.mjs'
        $fakeProfile = Join-Path $User.RunDir 'b1-profile'
        $node = (Get-Command -Name 'node' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
        $r = [ordered]@{ node_present = [bool] $node }
        if ($node) {
            $r.node = Invoke-AsUser $User 'b1e-node' @($script) -FilePath $node
            $r.node_userprofile_overridden = Invoke-AsUser $User 'b1e-node-up' @($script) -FilePath $node -Extra @{ USERPROFILE = $fakeProfile }
        }
        if ($Bun) {
            $r.bun = Invoke-AsUser $User 'b1e-bun' @($script) -FilePath $Bun
            $r.bun_userprofile_overridden = Invoke-AsUser $User 'b1e-bun-up' @($script) -FilePath $Bun -Extra @{ USERPROFILE = $fakeProfile }
        }
        $r
    }
    # The scratch folder is made by the user's own probe here, so later rows read its ACL.
    Invoke-Row $Result 'rf_c3' { Invoke-AsUser $User 'c3-volume' @('volume', '--scratch', $s) }
    Invoke-Row $Result 'rf_c1' { Invoke-AsUser $User 'c1-acl' @('acl', '--scratch', $s) }
    Invoke-Row $Result 'rf_c1b' {
        $dir = Join-Path $s 'pitboard-probe-bunmode'
        $r = [ordered]@{}
        if ($Bun) { $r.bun = Invoke-AsUser $User 'c1b-bun' @((Join-Path $toolHelpers 'bun-mode.mjs'), $s) -FilePath $Bun }
        $r.file = Invoke-AsUser $User 'c1b-file' @('acl', '--scratch', $s, '--describe', (Join-Path $dir 'pitboard-probe-bunmode.json'))
        $r.folder = Invoke-AsUser $User 'c1b-folder' @('acl', '--scratch', $s, '--describe', $dir)
        $r
    }
    Invoke-Row $Result 'rf_c2' {
        $protected = Join-Path $s 'pitboard-probe-protected'
        [ordered]@{
            create = Invoke-AsUser $User 'c2-create' @('acl', '--scratch', $s, '--create', '--keep')
            open_as_the_user = Invoke-AsUser $User 'c2-open' @('acl', '--scratch', $s, '--open', $protected)
            open_as_the_job = Invoke-Probe 'c2-open-job' @('acl', '--scratch', $s, '--open', $protected)
            remove = Invoke-AsUser $User 'c2-remove' @('acl', '--scratch', $s, '--remove', $protected)
        }
    }
    Invoke-Row $Result 'rf_c4' { Measure-ReplaceLoop $User (Join-Path $s 'c4') 'c4' 10000 180 $Bun }
    Invoke-Row $Result 'rf_c5' {
        $r = [ordered]@{ flush_dir = Invoke-AsUser $User 'c5-flush' @('flush-dir', '--scratch', $s) }
        if ($Bun) { $r.lock = Measure-Lock $User (Join-Path $s 'c5') 'write' $Bun }
        $r
    }
    if ($Bun) { Invoke-Row $Result 'rf_c5r' { Measure-Lock $User (Join-Path $s 'c5r') 'refresh' $Bun } }
    Invoke-Row $Result 'rf_c6' { Measure-LockFileEx $User (Join-Path $s 'c6') 'c6' }
    Invoke-Row $Result 'rf_d1' { Invoke-AsUser $User 'd1' @('dpapi', '--scratch', $s) }
    Invoke-Row $Result 'rf_e1' {
        [ordered]@{
            pitboard_folder = Measure-Task $User $s 'e1' 'pitboard' 'logon' -SidSuffix
            root_folder = Measure-Task $User $s 'e1' 'root' 'logon' -SidSuffix
        }
    }
    Invoke-Row $Result 'rf_d2' {
        $out = Join-Path $s 'pitboard-probe-d2-unseal.json'
        [ordered]@{
            seal = Invoke-AsUser $User 'd2-seal' @('dpapi', '--scratch', $s, '--action', 'seal', '--file', 'pitboard-probe-d2.bin')
            task = Measure-Task $User $s 'd2' 'probe' "--out $out dpapi --scratch $s --action unseal --file pitboard-probe-d2.bin" -WaitSeconds 30 -Expect $out
        }
    }
    Invoke-Row $Result 'rf_e5' {
        $e5 = Join-Path $User.RunDir 'e5'
        $app = Join-Path $e5 'app'
        New-Item -ItemType Directory -Force -Path $app | Out-Null
        $exe = Join-Path $app 'pitboard-probe.exe'
        Copy-Item -Path $Probe -Destination $exe
        $symlink = Join-Path $e5 'pitboard-probe-symlink.exe'
        $hardLink = Join-Path $e5 'pitboard-probe-hardlink.exe'
        $junction = Join-Path $e5 'current'
        New-Item -ItemType SymbolicLink -Path $symlink -Target $exe | Out-Null
        New-Item -ItemType HardLink -Path $hardLink -Target $exe | Out-Null
        New-Item -ItemType Junction -Path $junction -Target $app | Out-Null
        [ordered]@{
            direct = Invoke-AsUser $User 'e5-direct' @('exe-path') -FilePath $exe
            file_symlink = Invoke-AsUser $User 'e5-symlink' @('exe-path') -FilePath $symlink
            junction = Invoke-AsUser $User 'e5-junction' @('exe-path') -FilePath (Join-Path $junction 'pitboard-probe.exe')
            hard_link = Invoke-AsUser $User 'e5-hardlink' @('exe-path') -FilePath $hardLink
        }
    }
    Invoke-Row $Result 'rf_f1' {
        $r = [ordered]@{}
        foreach ($start in 'running', 'suspended') {
            foreach ($chain in @(@('exe', 1000), @('cmd-node-exe', 1000), @('exe-breakaway', 200))) {
                $r["$($chain[0])_$start"] = Invoke-AsUser $User "f1-$($chain[0])-$start" @('job', '--scratch', (Join-Path $s 'f1'),
                    '--chain', $chain[0], '--rounds', [string] $chain[1], '--start', $start) -TimeoutSeconds 600
                Save-Report
            }
        }
        $r
    }
    Invoke-Row $Result 'rf_h1' {
        $h1 = Join-Path $User.RunDir 'h1'
        $copies = [ordered]@{ lower = (Join-Path $h1 'a\codex.exe'); mixed = (Join-Path $h1 'b\Codex.EXE') }
        if ($X64) { $copies.x64 = (Join-Path $h1 'x\codex.exe') }
        foreach ($k in $copies.Keys) {
            New-Item -ItemType Directory -Force -Path (Split-Path $copies[$k]) | Out-Null
            Copy-Item -Path $(if ($k -eq 'x64') { $X64 } else { $Probe }) -Destination $copies[$k]
        }
        $runs = [ordered]@{}
        foreach ($k in $copies.Keys) {
            $runs[$k] = Start-AsUser $User "h1-$k" @('lock', '--scratch', (Join-Path $s "h1-$k"), '--mode', 'hold', '--seconds', '40') -FilePath $copies[$k]
        }
        $jobCopy = Join-Path $scratch 'h1\codex.exe'
        New-Item -ItemType Directory -Force -Path (Split-Path $jobCopy) | Out-Null
        Copy-Item -Path $Probe -Destination $jobCopy
        $jobRun = Start-Process -FilePath $jobCopy -ArgumentList (Format-Arguments @('lock', '--scratch', (Join-Path $scratch 'h1-job'), '--mode', 'hold', '--seconds', '40')) `
            -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $scratch 'h1-job-out.txt') -RedirectStandardError (Join-Path $scratch 'h1-job-err.txt')
        Start-Sleep -Seconds 8
        $r = [ordered]@{
            x64_copy = [bool] $X64
            as_the_user = Invoke-AsUser $User 'h1-find' @('processes', '--find', 'codex.exe')
            as_the_job = Invoke-Probe 'h1-find-job' @('processes', '--find', 'codex.exe')
        }
        foreach ($k in $runs.Keys) { $r["holder_$k"] = Wait-Run $runs[$k] 90 }
        $null = $jobRun.WaitForExit(90000)
        $r
    }
    if ($Bun) {
        Invoke-Row $Result 'rf_g3' {
            $secrets = Join-Path $toolHelpers 'bun-secrets.mjs'
            $r = [ordered]@{}
            try {
                $r.set = Invoke-AsUser $User 'g3-set' @($secrets, 'set') -FilePath $Bun
                $i = 0
                foreach ($name in 'claude-code-user', 'claude-code-user#0', 'claude-code-user#m') {
                    $target = "pitboard-citest-bun/$name"
                    $r[$name] = [ordered]@{
                        read = Invoke-AsUser $User "g3-read-$i" @('credman-item', '--action', 'read', '--name', $target)
                        delete = Invoke-AsUser $User "g3-delete-$i" @('credman-item', '--action', 'delete', '--name', $target)
                    }
                    $i++
                }
            } finally {
                $r.cleanup = Invoke-AsUser $User 'g3-cleanup' @($secrets, 'delete') -FilePath $Bun
                $r.leak_check = Invoke-AsUser $User 'g3-leak-check' @('credman-names', '--leak-check')
            }
            $r
        }
    }
}

# RF-CM2, RF-D1b and RF-D1d, last, since they change the user's password: the leak check,
# then a seal, the user's own change through ADSI in a process of its own (both passwords in
# its environment, never on its command line) and an unseal with the new password, then
# another seal, an administrator's reset and an unseal of both.
function Measure-PasswordChanges($User, $Result) {
    $s = $User.Scratch
    Invoke-Row $Result 'rf_cm2' { Invoke-AsUser $User 'cm2' @('credman-names', '--leak-check') }
    Invoke-Row $Result 'rf_d1b' {
        $r = [ordered]@{ seal = Invoke-AsUser $User 'd1b-seal' @('dpapi', '--scratch', $s, '--action', 'seal', '--file', 'pitboard-probe-d1b.bin') }
        $changer = Join-Path $User.RunDir 'pitboard-probe-d1b.ps1'
        @(
            'try {',
            '    $u = [adsi]("WinNT://" + $env:COMPUTERNAME + "/" + $env:PITBOARD_PROBE_USER + ",user")',
            '    $u.ChangePassword($env:PITBOARD_PROBE_OLD, $env:PITBOARD_PROBE_NEW)',
            '    "changed"',
            '} catch {',
            '    "failed 0x{0:X8}" -f $_.Exception.HResult',
            '}'
        ) | Set-Content -Path $changer -Encoding ascii
        $new = New-Password
        $r.change = Invoke-AsUser $User 'd1b-change' @('-NoProfile', '-NonInteractive', '-File', $changer) -FilePath $pwsh -Text -Extra @{
            PITBOARD_PROBE_USER = $User.Name
            PITBOARD_PROBE_OLD = (Get-PlainText $User.Secure)
            PITBOARD_PROBE_NEW = (Get-PlainText $new)
        }
        if ($r.change.output -match 'changed') {
            $User.Secure = $new
            $User.Cred = [pscredential]::new($User.Name, $new)
        }
        $r.unseal_with_the_new_password = Invoke-AsUser $User 'd1b-unseal' @('dpapi', '--scratch', $s, '--action', 'unseal', '--file', 'pitboard-probe-d1b.bin')
        $r
    }
    Invoke-Row $Result 'rf_d1d' {
        $r = [ordered]@{ seal = Invoke-AsUser $User 'd1d-seal' @('dpapi', '--scratch', $s, '--action', 'seal', '--file', 'pitboard-probe-d1d.bin') }
        $reset = New-Password
        Set-LocalUser -SID $User.Sid -Password $reset
        $User.Secure = $reset
        $User.Cred = [pscredential]::new($User.Name, $reset)
        $User.PasswordReset = $true
        $r.reset = $true
        $r.unseal_d1b_after_reset = Invoke-AsUser $User 'd1d-unseal-d1b' @('dpapi', '--scratch', $s, '--action', 'unseal', '--file', 'pitboard-probe-d1b.bin')
        $r.unseal_d1d_after_reset = Invoke-AsUser $User 'd1d-unseal-d1d' @('dpapi', '--scratch', $s, '--action', 'unseal', '--file', 'pitboard-probe-d1d.bin')
        $r
    }
}

# RF-B2: a second user whose name is not ASCII: pb, D with stroke, a with dot below, t.
function Measure-NonAsciiUser($Result, [string] $Bun) {
    $user = $null
    try {
        $name = 'pb' + [char] 0x0110 + [char] 0x1EA1 + 't' + (Get-Random -Minimum 10000 -Maximum 99999)
        $user = New-ThrowawayUser 'nonascii' $name @($usersSid) $Result
        if ($user.ProfileDir) {
            $s = $user.Scratch
            $Result.homes = Invoke-AsUser $user 'b2-homes' @('homes', '--codex-home', (Join-Path $s 'codex'), '--claude-config-dir', (Join-Path $s 'cc'))
            if ($Bun) { $Result.bun_os_homedir = Invoke-AsUser $user 'b2-bun' @((Join-Path $toolHelpers 'os-homedir.mjs')) -FilePath $Bun }
        }
    } catch {
        $Result.error = $_.Exception.Message
    } finally {
        if ($user) { Remove-ThrowawayUser $user $Result }
        Save-Report
    }
}

# RF-A1's other half: a fresh member of Administrators, started as the standard user is.
function Measure-FreshAdmin($Result) {
    $user = $null
    try {
        $user = New-ThrowawayUser 'admin' ('pbadmin' + (Get-Random -Minimum 10000 -Maximum 99999)) @($usersSid, $administratorsSid) $Result
        if ($user.ProfileDir) {
            $s = $user.Scratch
            $Result.tokens = Invoke-AsUser $user 'a1-tokens' @('tokens', '--scratch', $s)
            $Result.acl = Invoke-AsUser $user 'a1-acl' @('acl', '--scratch', $s)
            $Result.acl_create = Invoke-AsUser $user 'a1-create' @('acl', '--scratch', $s, '--create', '--keep')
            $Result.acl_remove = Invoke-AsUser $user 'a1-remove' @('acl', '--scratch', $s, '--remove', (Join-Path $s 'pitboard-probe-protected'))
        }
    } catch {
        $Result.error = $_.Exception.Message
    } finally {
        if ($user) { Remove-ThrowawayUser $user $Result }
        Save-Report
    }
}

# The job user's rows: RF-E1's, RF-E3, RF-D2's and RF-F2.
function Measure-JobUser($Result) {
    $s = Join-Path $scratch 'job'
    Invoke-Row $Result 'rf_e1' {
        [ordered]@{
            pitboard_folder = Measure-Task $null $s 'e1j' 'pitboard' 'logon' -SidSuffix
            root_folder = Measure-Task $null $s 'e1j' 'root' 'logon' -SidSuffix
        }
    }
    # The three values are put back exactly as they were, value and kind, or removed where
    # they were absent.
    Invoke-Row $Result 'rf_e3' {
        $names = 'PITBOARD_HOME', 'CLAUDE_CONFIG_DIR', 'CODEX_HOME'
        $read = {
            $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
            $state = [ordered]@{}
            foreach ($n in $names) {
                $state[$n] = if ($k -and $k.GetValueNames() -contains $n) {
                    [ordered]@{ value = $k.GetValue($n, $null, 'DoNotExpandEnvironmentNames'); kind = [string] $k.GetValueKind($n) }
                } else { $null }
            }
            if ($k) { $k.Close() }
            $state
        }
        $before = & $read
        $r = [ordered]@{ present_before = @($names | Where-Object { $null -ne $before[$_] }) }
        try {
            $r.setx_exit = [ordered]@{}
            foreach ($n in $names) {
                setx $n (Join-Path $s ('e3-' + $n.ToLowerInvariant())) *> $null
                $r.setx_exit[$n] = $LASTEXITCODE
            }
            $out = Join-Path $s 'pitboard-probe-e3-homes.json'
            $r.task = Measure-Task $null $s 'e3' 'probe' "--out $out homes" -WaitSeconds 30 -Expect $out
        } finally {
            $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
            foreach ($n in $names) {
                if ($null -eq $before[$n]) {
                    $k.DeleteValue($n, $false)
                } else {
                    $k.SetValue($n, $before[$n].value, [Microsoft.Win32.RegistryValueKind] $before[$n].kind)
                }
            }
            $k.Close()
            $r.restored_exactly = ((& $read) | ConvertTo-Json -Compress) -eq ($before | ConvertTo-Json -Compress)
        }
        $r
    }
    Invoke-Row $Result 'rf_d2' {
        $out = Join-Path $s 'pitboard-probe-d2j-unseal.json'
        [ordered]@{
            seal = Invoke-Probe 'd2j-seal' @('dpapi', '--scratch', $s, '--action', 'seal', '--file', 'pitboard-probe-d2j.bin')
            task = Measure-Task $null $s 'd2j' 'probe' "--out $out dpapi --scratch $s --action unseal --file pitboard-probe-d2j.bin" -WaitSeconds 30 -Expect $out
        }
    }
    Invoke-Row $Result 'rf_f2' {
        $r = [ordered]@{}
        foreach ($open in 'shell', 'assoc') {
            foreach ($end in 'terminate-job', 'allowlist', 'parent-exit') {
                $r["$($open)_$end"] = [ordered]@{
                    run = Invoke-Probe "f2-$open-$end" @('job-browser', '--scratch', (Join-Path $s 'f2'), '--open', $open, '--end', $end) -TimeoutSeconds 120
                    edge_left = @(Get-Process -Name 'msedge' -ErrorAction SilentlyContinue).Count
                }
                Stop-Process -Name 'msedge' -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 2
                Save-Report
            }
        }
        $r
    }
}

# Anything a row left behind: the probe's tasks, a \Pitboard\ folder the run made, the tools.
function Remove-Leftovers([bool] $PitboardFolderBefore) {
    Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'pitboard-probe-*' } |
        Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $root = $service.GetFolder('\')
    foreach ($folder in @('pitboard-probe') + $(if ($PitboardFolderBefore) { @() } else { @('Pitboard') })) {
        try { $root.DeleteFolder($folder, 0) } catch { $null = $_ }
    }
    Remove-Item -Recurse -Force -Path $tools -ErrorAction SilentlyContinue
}

function Test-PitboardTaskFolder {
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    try { $null = $service.GetFolder('\Pitboard'); $true } catch { $false }
}

$pitboardFolderBefore = Test-PitboardTaskFolder
$report.pitboard_task_folder_before = $pitboardFolderBefore
Set-Content -Path $marker -Value 'ci runner, disposable'
try {
    $report.job_user = Invoke-Probe 'runner-facts' @('runner-facts', '--scratch', $scratch)
    $report.policies = Invoke-Fact { Get-Policies }
    Save-Report
    $report.vhd = [System.Collections.Generic.List[object]]::new()
    try { Get-Vhds $report.vhd } catch { $report.vhd_error = $_.Exception.Message }
    Save-Report

    $report.tools = [ordered]@{}
    $bun = $null
    try { $bun = Install-ProbeTools $report.tools $BunVersion $BunZip $BunSha256 } catch { $report.tools.error = $_.Exception.Message }
    Save-Report

    $report.fresh_standard_user = [ordered]@{}
    $report.refs = [ordered]@{}
    $report.smb_share = [ordered]@{}
    $report.openssh = [ordered]@{}
    $fresh = $null
    try {
        $fresh = New-ThrowawayUser 'fresh' ('pbprobe' + (Get-Random -Minimum 10000 -Maximum 99999)) @($usersSid) $report.fresh_standard_user
        Save-Report
        if ($fresh.ProfileDir) {
            Measure-FreshUser $fresh $report.fresh_standard_user $bun $ProbeX64
            try { Measure-ReFs $fresh $report.refs $bun } catch { $report.refs.error = $_.Exception.Message }
            Save-Report
            try { Measure-SmbShare $fresh $report.smb_share } catch { $report.smb_share.error = $_.Exception.Message }
            Save-Report
            try { Measure-OpenSsh $fresh $report.openssh } catch { $report.openssh.error = $_.Exception.Message }
            Save-Report
            Measure-PasswordChanges $fresh $report.fresh_standard_user
        }
    } catch {
        $report.fresh_standard_user.error = $_.Exception.Message
    } finally {
        if ($fresh) { Remove-ThrowawayUser $fresh $report.fresh_standard_user }
        Save-Report
    }

    $report.non_ascii_user = [ordered]@{}
    Measure-NonAsciiUser $report.non_ascii_user $bun
    $report.fresh_admin = [ordered]@{}
    Measure-FreshAdmin $report.fresh_admin

    $report.job_user_rows = [ordered]@{}
    try { Measure-JobUser $report.job_user_rows } catch { $report.job_user_rows.error = $_.Exception.Message }
} finally {
    try { Remove-Leftovers $pitboardFolderBefore } catch { $report.leftovers_error = $_.Exception.Message }
    $report.tools_removed = -not (Test-Path -Path $tools)
    Remove-Item -Force -Path $marker -ErrorAction SilentlyContinue
    $report.unreadable_reports = @($script:unreadable)
    Save-Report
}
Get-Content -Raw -Path $Out
if ($null -eq $report.job_user.report) {
    throw 'the probe wrote no runner-facts report'
}
if ($report.job_user.report.ok -eq $false) {
    throw "the probe refused its runner-facts report: $($report.job_user.report.error)"
}
if ($script:unreadable.Count -gt 0) {
    throw "ConvertFrom-Json could not read $($script:unreadable -join ', '); the report keeps each one's parse error and text"
}
exit 0
