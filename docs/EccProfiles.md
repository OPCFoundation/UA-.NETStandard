# Support for Elliptic Curve Cryptography (ECC) Certificates in Server and Client Applications

Server and client applications support encrypted communication with RSA
and elliptic curve cryptography (ECC) certificates. This guide explains
how to configure both certificate types.

## Contents

- [Previous client and server configuration (RSA only)](#previous-client-and-server-application-configuration-supports-only-rsa-certificates)
- [Previous server configuration (RSA only)](#previous-server-application-configuration-supports-only-rsa-certificates)
- [New client and server configuration (RSA and ECC)](#new-client-and-server-application-configuration-support-both-rsa-and-ecc-certificates)
- [New server configuration for ECC](#new-server-application-configuration-related-to-ecc-certificates)
- [Old and new configuration compatibility](#old-client-and-server-application-configuration-format-vs-new-client-and-server-application-configuration-format)
- [Configure GDS for ECC](#configure-gds-for-use-with-ecc-certificates)
- [Known limitations](#known-limitations)

The [previous configuration](#previous-client-and-server-application-configuration-supports-only-rsa-certificates)
supports RSA certificates. The
[new configuration](#new-client-and-server-application-configuration-support-both-rsa-and-ecc-certificates)
supports both RSA and ECC certificates. The guide also describes
compatibility and ECC limitations.

## Previous Client and Server application configuration supports only RSA certificates

Up to now, the configuration supported encrypted communication using only the RSA encryption algorithm. That means that the server and client certificates were RSA certificates and there was just one RSA certificate per application needed to be configured.
The XML tag which contained the RSA certificate to be configured was `<ApplicationCertificate>`. This tag is still used for the RSA certificate configuration in backward compatibility mode as further described below:

```xml
<!-- The security configuration for the server. -->
<SecurityConfiguration>
 <!-- Where the application instance certificate is stored (MachineDefault) -->
 <ApplicationCertificate>
   <StoreType>Directory</StoreType>
   <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
   <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
 </ApplicationCertificate>
 ....
</SecurityConfiguration>
```

## Previous Server application configuration supports only RSA certificates

For Server applications the configuration of the RSA certificate involves also specifying the SecurityPolicies to be supported by the server:

```xml
    <!-- The security configuration for the server. -->
<SecurityConfiguration>
    <!-- The security policy to use. -->
    <SecurityPolicies>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</SecurityPolicyUri>
        </ServerSecurityPolicy>   
        <ServerSecurityPolicy>
            <SecurityMode>None_1</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
    ....
</SecurityConfiguration>
```

## New Client and Server application configuration support both RSA and ECC certificates

With the newly introduced support for ECC certificates, the configuration of the server application has been extended to support both RSA and ECC certificates. The XML tag under which both the RSA and ECC certificates are configured is `<ApplicationCertificates>`.

The `<ApplicationCertificate>` tag is still used for the RSA certificate configuration in backward compatibility mode, meaning that old server configurations are still supported, but they cannot simultaneously coexist.

The new configuration of the server application is described below:

```xml
<!-- The security configuration for the server. -->
<SecurityConfiguration>
    <!-- Where the application instance certificate is stored (MachineDefault) -->
    <ApplicationCertificates>
        <CertificateIdentifier>
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>RsaSha256</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>NistP256</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>NistP256</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>NistP384</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>NistP384</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>BrainpoolP256r1</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>BrainpoolP256r1</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>BrainpoolP384r1</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>BrainpoolP384r1</CertificateTypeString>
        </CertificateIdentifier>
    </ApplicationCertificates>
....
</SecurityConfiguration>
```

This layout of the configuration file allows the server to support both RSA and ECC certificates and allows the server to generate certificates of different types. The `<CertificateTypeString>` tag is used to specify the type of the certificate.

The supported types are:

- `RsaSha256`              for RSA certificates
- `NistP256`               for ECC certificates with NIST P256 curve
- `NistP384`               for ECC certificates with NIST P384 curve
- `BrainpoolP256r1`        for ECC certificates with Brainpool P256r1 curve
- `BrainpoolP384r1`        for ECC certificates with Brainpool P384r1 curve

Additionally, this layout enables the user to select a specific `<SubjectName>` for each certificate. This is useful when the user wants to generate certificates with different `<SubjectName>`s.

## New Server application configuration related to ECC certificates

The Server applications can configure the supported SecurityPolicies in the same way as before, but now the SecurityPolicies can be configured for each ECC specific SecurityPolicyUri which is to be supported by the server:

```xml
<!-- The security configuration for the server. -->
<SecurityConfiguration>
    <!-- The security policy to use. -->
    <SecurityPolicies>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1<SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1</SecurityPolicyUri>
        </ServerSecurityPolicy>
    </SecurityPolicies>
    ....
</SecurityConfiguration>
```

With the introduction of ECC certificates, the `<SecurityPolicies>` section of the configuration file has been extended to support ECC specific SecurityPolicies. The ECC specific SecurityPolicies are the following:

- `http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256`
- `http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384`
- `http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1`
- `http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1`

For every ECC-specific policy in `<SecurityPolicies>`, the server must have
a corresponding ECC certificate in `<ApplicationCertificates>`. Its
`CertificateTypeString` must match the policy's curve. If a corresponding
certificate is missing, the server cannot start.

The server encrypts each `UserIdentityToken` with the certificate selected
by the active security policy or by the `<SecurityPolicyUri>` in its
`<UserTokenPolicy>`. To explicitly list the policies supported for user
identity tokens, configure them in `<UserTokenPolicies>`. They must also
appear in `<SecurityPolicies>`.

The following example shows how the server can specify the supported `<SecurityPolicy>`s for the UserIdentityTokens:

```xml
<!-- The SDK expects the server to support the same set of user tokens for every endpoint. -->
<UserTokenPolicies>
    <!-- Allows anonymous users -->
    <ua:UserTokenPolicy>
        <ua:TokenType>Anonymous_0</ua:TokenType>
        <!-- <ua:SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</ua:SecurityPolicyUri> -->
    </ua:UserTokenPolicy>
        <!-- Allows username/password with password encrypted using the active security policy-->
    <ua:UserTokenPolicy>
        <ua:TokenType>UserName_1</ua:TokenType>
        <!-- passwords must be encrypted - this specifies what algorithm to use -->
        <!-- if no algorithm is specified, the active security policy is used -->
        <!-- <ua:SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</ua:SecurityPolicyUri> -->
    </ua:UserTokenPolicy>
        <!-- Allows username/password with password encrypted using ECC security-->
    <ua:UserTokenPolicy>
        <ua:TokenType>UserName_1</ua:TokenType>
        <!-- passwords must be encrypted - this specifies what algorithm to use -->
        <!-- if no algorithm is specified, the active security policy is used -->
        <ua:SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</ua:SecurityPolicyUri>
    </ua:UserTokenPolicy>
</UserTokenPolicies>
```

You can omit `<SecurityPolicyUri>` from `<UserTokenPolicy>`. In that case,
the server uses the active security policy to encrypt the
`UserIdentityToken`. Therefore, each policy listed in `<UserTokenPolicies>`
must also appear in `<SecurityPolicies>`.

If `<UserTokenPolicies>` names a policy that `<SecurityPolicies>` does not
include, the configuration is invalid.

## "Old" Client and Server application configuration format VS "New" Client and Server application configuration format

Applications using the old configuration continue to use RSA certificates
configured with `<ApplicationCertificate>`. The server must not list ECC
policies in `<SecurityPolicies>`. These applications cannot communicate with
peers that require ECC certificates.

Applications using the new configuration can support both RSA and ECC
certificates. Configure both certificate types with
`<ApplicationCertificates>`, then list the supported policies in
`<SecurityPolicies>`, including any ECC policies.

Use `<UserTokenPolicies>` to configure the policies used to encrypt user
identity tokens. These policies must also appear in `<SecurityPolicies>`.

Do not combine the old and new configuration formats. A configuration file
cannot contain both `<ApplicationCertificate>` and
`<ApplicationCertificates>`.

## Configure GDS for use with ECC Certificates

To use ECC certificates with the Global Discovery Server (GDS), update its
configuration as shown below.

```xml
  <Extensions>
    <ua:XmlElement>
      <GlobalDiscoveryServerConfiguration xmlns="http://opcfoundation.org/UA/GDS/Configuration.xsd">
        <CertificateGroups>
          <CertificateGroupConfiguration>
            <Id>Default</Id>
            <CertificateType>RsaSha256ApplicationCertificateType</CertificateType>
```

Replace the `<CertificateType>` node of the Default CertificateGroupConfiguration with the `<CertificateTypes>` node.
This allows the Certificate Group to have multiple CA Certificates for the different Certificate types.

```xml
<Extensions>
    <ua:XmlElement>
      <GlobalDiscoveryServerConfiguration xmlns="http://opcfoundation.org/UA/GDS/Configuration.xsd">
        <CertificateGroups>
          <CertificateGroupConfiguration>
            <Id>Default</Id>
            <CertificateTypes>
              <ua:String>RsaSha256ApplicationCertificateType</ua:String>
              <ua:String>EccNistP256ApplicationCertificateType</ua:String>
              <ua:String>EccNistP384ApplicationCertificateType</ua:String>
            </CertificateTypes>
```

The old Configuration format is still supported but only supports either RSA or ECC Certificates for a single CertificateGroup.
The GDS checks on startup if a valid configuration was supplied.

## Known Limitations

Not all curves are supported by all OS platforms and not all .NET implementations offer cryptographic API support for all curve types.
**On .NET Framework 4.8 the ECDH agreement and the AEAD ciphers run in BouncyCastle.**
OPC UA Part 6 feeds the raw ECDH shared secret into HKDF. .NET Framework
only offers `ECDiffieHellman.DeriveKeyMaterial`, which hashes the secret
first, so the .NET Framework 4.8 build computes the raw agreement with the
managed `BouncyCastle.Cryptography` implementation (`ECDHBasicAgreement`),
which `Opc.Ua.Security.Certificates` already references on that target.
The ephemeral keys, ECDSA signatures and HKDF stay on the platform (CNG)
providers; only the agreement step is managed code. The AES-GCM and
ChaCha20-Poly1305 variants of the ECC policies (`ECC_*_AesGcm`,
`ECC_*_ChaChaPoly`) and `RSA_DH_AesGcm` / `RSA_DH_ChaChaPoly` need
authenticated ciphers that .NET Framework does not have, so on that target
the stack runs AES-GCM (`GcmBlockCipher` over `AesEngine`) and
ChaCha20-Poly1305 in BouncyCastle as well, also for the encrypted ECC user
tokens. Deployments bound to FIPS-validated or other certified cryptographic
modules should note that these steps are not performed by a validated module
on .NET Framework, and that the managed AES is neither hardware accelerated
nor hardened against cache-timing side channels like the CNG implementation;
use the .NET 8+ build, or an RSA policy, where that matters. The .NET 8+
builds use `ECDiffieHellman.DeriveRawSecretAgreement`, `AesGcm` and
`ChaCha20Poly1305` from the BCL and do not load BouncyCastle for them.

All ECC policies (`ECC_nistP256`, `ECC_nistP384`, `ECC_brainpoolP256r1`,
`ECC_brainpoolP384r1` and their `_AesGcm` / `_ChaChaPoly` variants) are
available on every target, subject to the OS supporting the curve.
ECC certificate parsing and signing remain subject to OS curve support.
The supported ECC curve types are the following:

- `NistP256`               for ECC certificates with NIST P256 curve
- `NistP384`               for ECC certificates with NIST P384 curve
- `BrainpoolP256r1`        for ECC certificates with Brainpool P256r1 curve
- `BrainpoolP384r1`        for ECC certificates with Brainpool P384r1 curve
