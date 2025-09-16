# 1. Ensure directories exist
$certDir   = "./pki/user/certs"
$privateDir = "./pki/user/private"

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

$params = @{
    Type = 'Custom'
    Subject = 'CN=iama.tester@example.com'
    TextExtension = @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.2',
        '2.5.29.17={text}upn=iama.tester@example.com' )
    KeyUsage = 'DigitalSignature'
    KeyAlgorithm = 'ECDSA_nistP256'
    CurveExport = 'CurveName'
    CertStoreLocation = 'Cert:\CurrentUser\My'
}
$cert = New-SelfSignedCertificate @params

# 3. Export as DER (.cer)
$derPath = Join-Path $certDir "iama.tester.der"
Export-Certificate -Cert $cert -FilePath $derPath -Type CERT

# 4. Export as PFX (with password)
$secret = ConvertTo-SecureString -String "password" -Force -AsPlainText
$pfxPath = Join-Path $privateDir "iama.tester.pfx"
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secret
