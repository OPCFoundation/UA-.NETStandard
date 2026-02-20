# 1. Ensure directories exist
$certDir   = "./bin/pki/trustedUser/certs"
$privateDir = "./bin/pki/trustedUser/private"

$curves = @(
    'nistP256',
    'nistP384',
    'brainpoolP256r1',
    'brainpoolP384r1'
)

foreach ($d in @($certDir, $privateDir)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}

function New-CertificateWithAKI {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2] $SourceCert,
        [string] $HashAlgorithmName = 'SHA256'
    )

    $hashAlg = [System.Security.Cryptography.HashAlgorithmName]::new($HashAlgorithmName)

    # Determine key type and create CertificateRequest
    $ecdsa = [System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($SourceCert)
    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($SourceCert)

    if ($ecdsa) {
        $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            $SourceCert.SubjectName, $ecdsa, $hashAlg)
    }
    elseif ($rsa) {
        $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            $SourceCert.SubjectName, $rsa, $hashAlg,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    }
    else {
        throw "Unsupported key type"
    }

    # Copy all extensions from source cert
    foreach ($ext in $SourceCert.Extensions) {
        $req.CertificateExtensions.Add($ext)
    }

    # Build Authority Key Identifier from Subject Key Identifier
    $skiExt = $SourceCert.Extensions | Where-Object {
        $_ -is [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]
    }

    if ($skiExt) {
        $hex = $skiExt.SubjectKeyIdentifier
        $keyIdBytes = [byte[]]::new($hex.Length / 2)
        for ($i = 0; $i -lt $keyIdBytes.Length; $i++) {
            $keyIdBytes[$i] = [System.Convert]::ToByte($hex.Substring($i * 2, 2), 16)
        }
        # AKI ASN.1: SEQUENCE { [0] IMPLICIT keyIdentifier }
        # 30 <seqLen> 80 <keyIdLen> <keyIdBytes>
        [byte[]] $akiInner = @(0x80, $keyIdBytes.Length) + $keyIdBytes
        [byte[]] $akiValue = @(0x30, $akiInner.Length) + $akiInner
        $akiOid = [System.Security.Cryptography.Oid]::new('2.5.29.35', 'Authority Key Identifier')
        $akiExt = [System.Security.Cryptography.X509Certificates.X509Extension]::new(
            $akiOid, $akiValue, $false)
        $req.CertificateExtensions.Add($akiExt)
    }

    return $req.CreateSelfSigned(
        [DateTimeOffset]::new($SourceCert.NotBefore),
        [DateTimeOffset]::new($SourceCert.NotAfter))
}

# 2. Create a self-signed ECC certificate (NIST P-256)
# $cert = New-SelfSignedCertificate `
#     -Subject "CN=iama.tester@example.com" `
#     -CertStoreLocation "Cert:\CurrentUser\My" `
#     -KeyExportPolicy Exportable `
#     -KeySpec Signature `
#     -KeyAlgorithm ECDSA_nistP256 `
#     -Curve 'CurveName' `
#     -HashAlgorithm SHA256 `
#     -NotAfter (Get-Date).AddYears(1)

foreach ($curve in $curves) {

   Write-Host "Generating certificate for curve: $curve"

$signatureAlgorithm = if ($curve -match 'P384') { 'SHA384' } else { 'SHA256' }

# OIDs for the extensions
# 2.5.29.14 = Subject Key Identifier
# 2.5.29.35 = Authority Key Identifier
# 2.5.29.19 = Basic Constraints (Subject Type=End Entity or CA)
$name = 'CN=iama.tester@example.com,O=' + $curve;

$params = @{
    Type              = 'Custom'
    Subject           = $name 
    TextExtension     = @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.2',           # Enhanced Key Usage (Client Auth)
        '2.5.29.17={text}upn=iama.tester@example.com', # SAN
        '2.5.29.19={text}ca=0'                         # Basic Constraints: Not a CA
    )
    KeyUsage          = @('DigitalSignature', 'NonRepudiation') # Added KeyCertSign for self-signed logic
    KeyAlgorithm      = "ECDSA_$curve"
    CurveExport       = 'CurveName'
    HashAlgorithm     = $signatureAlgorithm
    CertStoreLocation = 'Cert:\CurrentUser\My'
    NotAfter          = (Get-Date).AddYears(2)    # Best practice to set an explicit expiry
}

# 1. Create cert and rebuild with Authority Key Identifier
$tempCert = New-SelfSignedCertificate @params
$cert = New-CertificateWithAKI -SourceCert $tempCert -HashAlgorithmName $signatureAlgorithm
Remove-Item "Cert:\CurrentUser\My\$($tempCert.Thumbprint)" -Force

Write-Host "Certificate generated with Thumbprint: $($cert.Thumbprint)"

    # 2. Export DER
    $derPath = Join-Path $certDir "iama.tester.$curve.der"
    [System.IO.File]::WriteAllBytes($derPath, $cert.RawData)

    # 3. Export PFX with password
    $pfxPath = Join-Path $privateDir "iama.tester.$curve.pfx"
    $pfxBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, 'password')
    [System.IO.File]::WriteAllBytes($pfxPath, $pfxBytes)

    Write-Host "Finished: $curve`n"
}

Write-Host "`n=== Generating RSA-2048 ==="

$rsaParams = @{
    Type              = 'Custom'
    Subject           = 'CN=iama.tester@example.com'
    TextExtension     = @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.2'
        '2.5.29.17={text}upn=iama.tester@example.com'
    )
    KeyUsage          = @('DigitalSignature','DataEncipherment','NonRepudiation','KeyEncipherment', 'CertSign')
    KeyAlgorithm      = 'RSA'
    KeyLength         = 2048
    CertStoreLocation = 'Cert:\CurrentUser\My'
}

$tempRsaCert = New-SelfSignedCertificate @rsaParams
$rsaCert = New-CertificateWithAKI -SourceCert $tempRsaCert -HashAlgorithmName 'SHA256'
Remove-Item "Cert:\CurrentUser\My\$($tempRsaCert.Thumbprint)" -Force

$derPath = Join-Path $certDir "iama.tester.rsa.der"
[System.IO.File]::WriteAllBytes($derPath, $rsaCert.RawData)
$pfxPath = Join-Path $privateDir "iama.tester.rsa.pfx"
$pfxBytes = $rsaCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, 'password')
[System.IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
