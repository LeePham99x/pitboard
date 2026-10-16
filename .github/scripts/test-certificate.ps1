# Dot-sourced by pack-windows-share.ps1 and runner-facts.ps1. Nothing trusts the certificate it makes.

# A self-signed code-signing certificate for $Subject, valid for a day, written to $Pfx under the password it returns,
# and its public half to $Cer when given.
function Export-TestCertificate([string] $Subject, [string] $Pfx, [string] $Cer) {
    $password = [guid]::NewGuid().ToString('N')
    $key = [Security.Cryptography.RSA]::Create(3072)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            $Subject, $key, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new('DigitalSignature', $true))
        $codeSigning = [Security.Cryptography.OidCollection]::new()
        [void] $codeSigning.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($codeSigning, $false))
        $now = [DateTimeOffset]::UtcNow
        $certificate = $request.CreateSelfSigned($now.AddMinutes(-5), $now.AddDays(1))
        [IO.File]::WriteAllBytes($Pfx, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
        if ($Cer) {
            [IO.File]::WriteAllBytes($Cer, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        }
    } finally {
        $key.Dispose()
    }
    $password
}
