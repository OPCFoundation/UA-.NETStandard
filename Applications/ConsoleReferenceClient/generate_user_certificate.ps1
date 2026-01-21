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

    # Create certificate parameters and dynamically insert the curve
    $params = @{
        Type              = 'Custom'
        Subject           = 'CN=iama.tester@example.com'
        TextExtension     = @(
            '2.5.29.37={text}1.3.6.1.5.5.7.3.2'
            '2.5.29.17={text}upn=iama.tester@example.com'
        )
        KeyUsage           = @('DigitalSignature', 'NonRepudiation')
        KeyAlgorithm       = "ECDSA_$curve"    # <-- dynamic!
        CurveExport        = 'CurveName'
        HashAlgorithm = $signatureAlgorithm
        CertStoreLocation  = 'Cert:\CurrentUser\My'
    }

    # 1. Create cert
    $cert = New-SelfSignedCertificate @params

    # 2. Export DER
    $derPath = Join-Path $certDir "iama.tester.$curve.der"
    Export-Certificate -Cert $cert -FilePath $derPath -Type CERT

    # 3. Export PFX with password
    $secret = ConvertTo-SecureString -String "password" -Force -AsPlainText
    $pfxPath = Join-Path $privateDir "iama.tester.$curve.pfx"
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secret

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
    KeyUsage          = @('DigitalSignature','DataEncipherment','NonRepudiation','KeyEncipherment')
    KeyAlgorithm      = 'RSA'
    KeyLength         = 2048
    CertStoreLocation = 'Cert:\CurrentUser\My'
}

$rsaCert = New-SelfSignedCertificate @rsaParams

Export-Certificate -Cert $rsaCert -FilePath (Join-Path $certDir   "iama.tester.rsa.der") -Type CERT
Export-PfxCertificate -Cert $rsaCert -FilePath (Join-Path $privateDir "iama.tester.rsa.pfx") -Password $secret
