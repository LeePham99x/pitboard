# Packs PitboardShare.exe's sparse package, PitboardShare.msix, into the folder it was published
# to, for CI and the release alike: Pitboard.Share/Package/AppxManifest.xml with -Publisher and
# -Version written in, packed by makeappx with /nv, since the logos it names are in the external
# location. It registers nothing and runs none of the files. With -TestCertificate, which only
# CI passes, it signs the package with a self-signed certificate for -Publisher, made into a PFX
# file in the job's temporary folder, never into a certificate store, and deleted afterwards.
param(
    [Parameter(Mandatory)] [string] $Folder,
    # The subject of the certificate that signs the package, as PitboardShare.exe's manifest names it.
    [Parameter(Mandatory)] [string] $Publisher,
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')] [string] $Version,
    [switch] $TestCertificate
)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $Folder).Path
if (-not (Test-Path -LiteralPath (Join-Path $root 'PitboardShare.exe') -PathType Leaf)) { throw "no PitboardShare.exe in $root" }
$apps = Join-Path $PSScriptRoot '..\..\apps\windows'

# makeappx and signtool from the Windows SDK build tools the app restores, at the version its
# lock file pins, for this machine's architecture.
$lock = Get-Content -Raw -LiteralPath (Join-Path $apps 'Pitboard.App\packages.lock.json') | ConvertFrom-Json
$toolsVersion = $lock.dependencies.'net10.0-windows10.0.26100'.'Microsoft.Windows.SDK.BuildTools'.resolved
if (-not $toolsVersion) { throw "Pitboard.App's lock file pins no Microsoft.Windows.SDK.BuildTools" }
$packages = (& dotnet nuget locals global-packages --list | Out-String).Trim() -replace '^global-packages:\s*', ''
if ($LASTEXITCODE -ne 0) { throw 'dotnet could not say where its packages are' }
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$tools = Get-ChildItem -LiteralPath (Join-Path $packages "microsoft.windows.sdk.buildtools\$toolsVersion\bin") -Directory |
    Sort-Object Name -Descending | Select-Object -First 1 | ForEach-Object { Join-Path $_.FullName $arch }
if (-not $tools -or -not (Test-Path -LiteralPath (Join-Path $tools 'makeappx.exe'))) {
    throw "no makeappx.exe in Microsoft.Windows.SDK.BuildTools $toolsVersion for ${arch}: restore Pitboard.App first"
}

$manifest = [xml](Get-Content -Raw -LiteralPath (Join-Path $apps 'Pitboard.Share\Package\AppxManifest.xml'))
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $Version
$package = Join-Path $root 'PitboardShare.msix'
$work = Join-Path ([IO.Path]::GetTempPath()) "pitboard-share-package-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $manifest.Save((Join-Path $work 'AppxManifest.xml'))
    & (Join-Path $tools 'makeappx.exe') pack /d $work /p $package /nv /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx exited $LASTEXITCODE" }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}
Write-Output "Packed $package for $Publisher, version $Version."
if (-not $TestCertificate) { exit 0 }

$temporary = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$pfx = Join-Path $temporary "pitboard-share-test-$([guid]::NewGuid().ToString('N')).pfx"
$password = [guid]::NewGuid().ToString('N')
$key = [Security.Cryptography.RSA]::Create(3072)
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $Publisher, $key, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new('DigitalSignature', $true))
    $codeSigning = [Security.Cryptography.OidCollection]::new()
    [void] $codeSigning.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($codeSigning, $false))
    $now = [DateTimeOffset]::UtcNow
    $certificate = $request.CreateSelfSigned($now.AddMinutes(-5), $now.AddDays(1))
    [IO.File]::WriteAllBytes($pfx, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
    & (Join-Path $tools 'signtool.exe') sign /fd SHA256 /f $pfx /p $password $package
    if ($LASTEXITCODE -ne 0) { throw "signtool exited $LASTEXITCODE" }
} finally {
    $key.Dispose()
    Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue
}
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile
$zip = [IO.Compression.ZipFile]::OpenRead($package)
try { $signed = $null -ne $zip.GetEntry('AppxSignature.p7x') } finally { $zip.Dispose() }
if (-not $signed) { throw 'signtool left PitboardShare.msix without its AppxSignature.p7x' }
# Printed for the release's signature check, which reads a package this way. No machine trusts
# a certificate made a moment ago, so its status is not Valid.
$signature = Get-AuthenticodeSignature -LiteralPath $package
Write-Output "Signed $package with a throwaway certificate for $Publisher. Get-AuthenticodeSignature reads it as $($signature.Status), signed by '$($signature.SignerCertificate.Subject)'."
exit 0
