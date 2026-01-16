## Certificates

All required application certificates for OPC UA are created at the first start of each application in a directory or OS-level certificate store and remain in use until deleted from the store.

The UA stack allows also for using CA issued application certificates and remote certificate store and trust list management with a *Global Discovery Server* using *Server Push*.

### Certificate stores

The layout of the certificate stores for sample applications which store the certificates in the file system follow the recommended layout in the [specification](https://reference.opcfoundation.org/v104/GDS/docs/F.1/), where certificates are stored in a `certs` folder, private keys under a `private` folder and revocation lists under a `crl` folder with a `<root>` folder called `pki`.

The UA .NET Standard stack supports the following certificate stores:

- The **Application** store  `<root>/own`which contains private keys used by the application.

- The **Issuer** store  `<root>/issuer`which contains certificates which are needed for validation, for example to complete the validation of a certificate chain. A certificate in the *Issuer* store is *not* trusted!

- The **Trusted** store  `<root>/trusted`which contains certificates which are trusted by the application. The certificates in this store can either be self signed, leaf, root CA or sub CA certificates.
  The most common use case is to add a self signed application certificate to the *Trusted* store to establish trust with that application.
  If the application certificate is the leaf of a chain, the trust can be established by adding the root CA, a sub CA or the leaf certificate itself to the *Trusted* store. Each of the options enables a different set of trusted certificates. A trusted Root CA or Sub CA certificate is used as the trust anchor for the certificate chain, which means any leaf certificate with a chain which contains the Root CA and Sub CA certificate is trusted, but the specification still mandates the validation of the whole chain. For the chain validation any certificate in the chain except the leaf certificate must be available from the *Issuer* store.

  If only the leaf certificate is in the *Trusted* store and the rest of the chain is stored in the *Issuer* store, then only the leaf certificate is trusted.
  As an example, to trust an application certificate that is issued by a Root CA, only the Root CA certificate is required in the *Trusted* store to establish trust to all application certificates issued by the CA. This option can greatly simplify the management of OPC UA Clients and Servers because only one certificate needs to be distributed across all systems.

- The **Rejected** store  `<root>/rejected` which contains certificates which have been rejected. This store is provided as a convenience for the administrator of an application to allow to copy an untrusted certificate from the *Rejected* to the *Trusted* store to establish trust with that application.

- The **Issuer User** store  `<root>/issuerUser` which contains user certificates which are used to validate user certificates.

- The **Trusted User** store  `<root>/trustedUser` which contains user certificates which are trusted by an application. To establish trust, the same rules apply as explained for the *Trusted* and the *Issuer* store.

- The **Issuer Https** store  `<root>/issuerHttps` which contains https certificates which are used to validate https connection certificates.

- The **Trusted Https** store  `<root>/trustedHttps` which contains https certificates which are trusted by an application. To establish trust, the same rules apply as explained for the *Trusted* and the *Issuer* store.

### X509Store on Windows
Starting with Version 1.5.xx of the UA .NET Standard Stack the X509Store supports the storage and retrieval of CRLS, if used on the **Windows OS**.
This enables the usage of the X509Store instead of the Directory Store for stores requiring the use of crls, e.g. the issuer or the directory Store.

### Windows .NET applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc). The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\pki** with a root folder which is specified by the `SpecialFolder` variable **%CommonApplicationData%**. On Windows 7/8/8.1/10 this is usually the invisible folder **C:\ProgramData**.

### Windows UWP applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc).

The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\pki** in the **LocalState** folder of the installed universal windows package. Deleting the application state also deletes the certificate stores.

### .NET Core applications on Windows, Linux, iOS etc.
The self signed certificates are stored in a folder called **OPC Foundation/pki/own** with a root folder which is specified by the `SpecialFolder` variable **%LocalApplicationData%** or in a **X509Store** called **CurrentUser\\My**, depending on the configuration. For best cross platform support the personal store **CurrentUser\\My** was chosen to support all platforms with the same configuration. Some platforms, like macOS, do not support arbitrary certificate stores.

The *trusted*, *issuer* and *rejected* stores remain in a shared folder called **OPC Foundation\pki** with a root folder specified by the `SpecialFolder` variable **%LocalApplicationData%**. Depending on the target platform, this folder maps to a hidden locations under the user home directory.

## Certificate Validation

The OPC UA .NET Standard Stack uses the `CertificateValidator` class to validate certificates according to the OPC UA specification. This section describes the certificate validation workflow, configuration settings, and how to customize the validation process.

### Validation Workflow

The certificate validation process follows these steps:

1. **Pre-validation Check**: If the certificate was previously validated and `UseValidatedCertificates` is enabled, the validation is skipped.

2. **Trust Check**: The validator checks if the certificate is explicitly trusted by searching in:
   - The trusted certificate list (`TrustedPeerCertificates`)
   - The trusted certificate store
   - The application's own certificate collection

3. **Issuer Chain Validation**: For certificates issued by a CA, the validator builds and validates the complete certificate chain. See [Chain Building Process](#chain-building-process) for detailed technical documentation.

4. **Certificate Properties Validation**: The validator checks:
   - Certificate expiration dates (NotBefore/NotAfter)
   - Key usage flags (DigitalSignature for ECDSA, DataEncipherment for RSA)
   - Minimum key size requirements
   - Signature algorithm strength (e.g., rejecting SHA-1 if configured)
   - Certificate signature validity

5. **Domain Validation**: If an endpoint is provided, the validator checks that the certificate contains the endpoint's domain name in its Subject Alternative Names.

6. **Application URI Validation**: Verifies that the certificate contains the expected Application URI in the Subject Alternative Name extension.

7. **Error Handling**: If validation errors occur, they are classified as either:
   - **Suppressible errors**: Can be accepted via the `CertificateValidation` event callback
   - **Non-suppressible errors**: Always cause validation to fail

8. **Rejected Certificate Storage**: Failed certificates are saved to the rejected certificate store for administrator review.

### Chain Building Process

This section provides detailed technical documentation of the certificate chain building and validation algorithm implemented in `CertificateValidator.GetIssuersNoExceptionsOnGetIssuerAsync()`.

#### Overview

The chain building process constructs a complete certificate chain from a leaf certificate up to a self-signed root CA certificate. The algorithm searches through multiple certificate stores in a specific priority order and performs revocation checking at each step.

#### Chain Building Algorithm Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     Start Chain Building                        │
│                   Input: Certificate Chain                      │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
                ┌────────────────────────────┐
                │  Current = certificates[0] │
                │  (Leaf Certificate)        │
                └────────────┬───────────────┘
                             │
                             ▼
                ┌────────────────────────────┐
                │  Create untrusted list     │
                │  from certificates[1..n]   │
                └────────────┬───────────────┘
                             │
                ┌────────────▼───────────────┐
                │ Loop: Find Issuer Chain    │
                └────────────┬───────────────┘
                             │
                ┌────────────▼───────────────────────────┐
                │ Is Current certificate self-signed?    │
                └─┬─────────────────────────────────┬────┘
                  │ YES                             │ NO
                  │                                 │
                  ▼                                 ▼
         ┌────────────────┐          ┌─────────────────────────────┐
         │ Chain Complete │          │ Search for Issuer (Step 1)  │
         │ Return Result  │          │ Location: Trusted Store     │
         └────────────────┘          └──────────┬──────────────────┘
                                                 │
                                     ┌───────────▼──────────────┐
                                     │ Issuer Found in Trusted? │
                                     └─┬─────────────────────┬──┘
                                       │ YES                 │ NO
                                       │                     │
                                       ▼                     ▼
                          ┌────────────────────┐  ┌──────────────────────────┐
                          │ Mark as Trusted    │  │ Search for Issuer (Step 2)│
                          │ Check Revocation   │  │ Location: Issuer Store    │
                          └──────┬─────────────┘  └────────┬─────────────────┘
                                 │                         │
                                 │             ┌───────────▼──────────────┐
                                 │             │ Issuer Found in Issuer?  │
                                 │             └─┬─────────────────────┬──┘
                                 │               │ YES                 │ NO
                                 │               │                     │
                                 │               ▼                     ▼
                                 │    ┌──────────────────┐  ┌──────────────────────────┐
                                 │    │ Check Revocation │  │ Search for Issuer (Step 3)│
                                 │    └────────┬─────────┘  │ Location: Untrusted Certs │
                                 │             │            └────────┬─────────────────┘
                                 │             │                     │
                                 │             │         ┌───────────▼──────────────┐
                                 │             │         │ Issuer Found in Chain?   │
                                 │             │         └─┬─────────────────────┬──┘
                                 │             │           │ YES                 │ NO
                                 │             │           │                     │
                                 │             │           ▼                     ▼
                                 │             │  ┌──────────────────┐  ┌──────────────┐
                                 │             │  │ Check Revocation │  │ Chain Broken │
                                 │             │  └────────┬─────────┘  │ Return Result│
                                 │             │           │            └──────────────┘
                                 │             │           │
                                 └─────────────┴───────────┘
                                               │
                                 ┌─────────────▼──────────────┐
                                 │ Check for Circular Chain   │
                                 │ (Duplicate Thumbprint)     │
                                 └─┬────────────────────────┬─┘
                                   │ DUPLICATE              │ UNIQUE
                                   │                        │
                                   ▼                        ▼
                          ┌────────────────┐    ┌──────────────────────┐
                          │ Chain Complete │    │ Add Issuer to List   │
                          │ Return Result  │    │ Current = Issuer Cert│
                          └────────────────┘    └──────────┬───────────┘
                                                            │
                                                            │
                                        ┌───────────────────┘
                                        │ Loop Back to Top
                                        └──────────────────────┐
                                                               │
                                                               ▼
                                                    (Continue Building Chain)
```

#### Detailed Algorithm Steps

##### Step 1: Initialization

```
INPUT:
  - certificates: X509Certificate2Collection (certificate chain from peer)
  - issuers: List<CertificateIdentifier> (output list, initially empty)
  - validationErrors: Dictionary<X509Certificate2, ServiceResultException> (output)

INITIALIZE:
  - isTrusted ← false
  - current ← certificates[0]  // Leaf certificate
  - untrustedCollection ← certificates[1..n]  // Additional certs from peer
```

##### Step 2: Iterative Chain Building Loop

```
WHILE (issuer is found) DO:

  // Exit condition: Self-signed certificate reached
  IF (IsSelfSigned(current)) THEN
    BREAK  // Chain is complete
  END IF

  // Step 2.1: Search in Trusted Certificate Store
  issuer ← FindIssuer(
    certificate: current,
    location: TrustedCertificateList + TrustedCertificateStore,
    checkRevocation: true
  )

  IF (issuer != null) THEN
    isTrusted ← true  // Chain ends in trusted store
    revocationStatus ← CheckCRL(issuer, current)
    validationErrors[current] ← revocationStatus
    GOTO Step_2.4
  END IF

  // Step 2.2: Search in Issuer Certificate Store
  issuer ← FindIssuer(
    certificate: current,
    location: IssuerCertificateList + IssuerCertificateStore,
    checkRevocation: true
  )

  IF (issuer != null) THEN
    revocationStatus ← CheckCRL(issuer, current)
    validationErrors[current] ← revocationStatus
    GOTO Step_2.4
  END IF

  // Step 2.3: Search in Untrusted Certificates (from peer)
  issuer ← FindIssuer(
    certificate: current,
    location: untrustedCollection,
    checkRevocation: true
  )

  IF (issuer == null) THEN
    BREAK  // Chain building failed - no issuer found
  END IF

  // Step 2.4: Circular Chain Detection
  FOR EACH existingIssuer IN issuers DO
    IF (existingIssuer.Thumbprint == issuer.Thumbprint) THEN
      BREAK LOOP  // Circular chain detected
    END IF
  END FOR

  // Step 2.5: Add Issuer to Chain
  issuers.Add(issuer)
  current ← LoadCertificate(issuer)

END WHILE

RETURN isTrusted
```

##### Step 3: Issuer Matching Algorithm

The `FindIssuer()` function uses the following matching criteria:

```
FUNCTION FindIssuer(certificate, location, checkRevocation):

  // Extract issuer information from certificate
  subjectName ← certificate.IssuerName
  authorityKeyId ← ExtractAuthorityKeyIdentifier(certificate)
  serialNumber ← ExtractSerialNumber(certificate)

  // Search all certificates in location
  FOR EACH candidate IN location DO

    // Basic matching criteria
    IF (candidate.SubjectName != subjectName) THEN
      CONTINUE  // Subject name must match issuer name
    END IF

    // Check if issuer is allowed (not end-entity cert)
    IF (NOT IsIssuerAllowed(candidate)) THEN
      CONTINUE  // Must have CA capabilities
    END IF

    // Optional: Match by serial number
    IF (serialNumber != null AND candidate.SerialNumber != serialNumber) THEN
      CONTINUE
    END IF

    // Optional: Match by Authority Key Identifier
    IF (authorityKeyId != null) THEN
      subjectKeyId ← candidate.SubjectKeyIdentifier
      IF (subjectKeyId != authorityKeyId) THEN
        CONTINUE
      END IF
    END IF

    // Candidate matches - perform revocation check
    IF (checkRevocation) THEN
      revocationStatus ← CheckCRL(candidate, certificate)
      IF (revocationStatus == Revoked) THEN
        RETURN (candidate, RevocationError)
      END IF
    END IF

    RETURN (candidate, revocationStatus)
  END FOR

  RETURN (null, null)  // No matching issuer found
END FUNCTION
```

##### Step 4: Certificate Revocation List (CRL) Checking

```
FUNCTION CheckCRL(issuer, certificate):

  // Only check if store supports CRL operations
  IF (store.SupportsCRL()) THEN

    crlStatus ← store.IsRevoked(issuer, certificate)

    CASE crlStatus OF
      StatusCodes.Good:
        RETURN null  // Not revoked

      StatusCodes.BadCertificateRevoked:
        IF (IsCertificateAuthority(certificate)) THEN
          RETURN BadCertificateIssuerRevoked
        ELSE
          RETURN BadCertificateRevoked
        END IF

      StatusCodes.BadCertificateRevocationUnknown:
        IF (IsCertificateAuthority(certificate)) THEN
          statusCode ← BadCertificateIssuerRevocationUnknown
        ELSE
          statusCode ← BadCertificateRevocationUnknown
        END IF

        // Check if error should be suppressed
        IF (RejectUnknownRevocationStatus AND
            NOT HasValidationOption(SuppressRevocationStatusUnknown)) THEN
          RETURN statusCode
        END IF
        RETURN null  // Suppressed

      StatusCodes.BadNotSupported:
        RETURN null  // CRL not supported by store
    END CASE
  END IF

  RETURN null
END FUNCTION
```

#### X509Chain Validation

After building the issuer chain, the validator uses .NET's `X509Chain` to verify cryptographic signatures and certificate properties:

```
// Configure chain policy
policy ← new X509ChainPolicy()
policy.RevocationMode ← NoCheck  // Already checked via CRL
policy.RevocationFlag ← EntireChain
policy.VerificationFlags ← ConfigureFromValidationOptions(issuers)

// Add all found issuers to extra store
FOR EACH issuer IN issuers DO
  policy.ExtraStore.Add(issuer.Certificate)
END FOR

// Build and validate chain
chain ← new X509Chain()
chain.ChainPolicy ← policy
chain.Build(certificate)

// Process chain status
FOR EACH chainStatus IN chain.ChainStatus DO
  ProcessChainStatus(chainStatus)
END FOR

// Verify chain matches issuers
IF (chain.ChainElements.Count != issuers.Count + 1) THEN
  chainIncomplete ← true
END IF

// Validate each chain element
FOR EACH element IN chain.ChainElements DO
  ValidateChainElement(element)
END FOR
```

#### Chain Validation Results

The chain building process returns:

1. **isTrusted**: `true` if any issuer was found in the trusted store, `false` otherwise
2. **issuers**: List of all issuer certificates in the chain (leaf to root, excluding the leaf itself)
3. **validationErrors**: Dictionary mapping certificates to their revocation status errors

These results are then used by the main validation logic to determine if the certificate should be accepted or rejected.

#### Key Behaviors

1. **Search Priority**: Trusted store → Issuer store → Untrusted collection (from peer)
2. **Trust Anchoring**: If any issuer is found in the trusted store, the entire chain is considered to have a trusted anchor
3. **CRL Checking**: Performed during chain building if the store supports it
4. **Circular Chain Detection**: Prevents infinite loops by checking for duplicate thumbprints
5. **Partial Chains**: If no issuer is found, the chain is marked incomplete but processing continues
6. **Self-Signed Detection**: Chain building stops when a self-signed certificate is encountered

### Configuration Settings

The certificate validation behavior is controlled by several configuration settings in the `SecurityConfiguration` class:

#### AutoAcceptUntrustedCertificates
- **Type**: `bool`
- **Default**: `false`
- **Description**: When `true`, automatically accepts certificates that have the `BadCertificateUntrusted` status. This is useful for development environments but should not be used in production.
- **Example**:
```csharp
configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
```

#### RejectSHA1SignedCertificates
- **Type**: `bool`
- **Default**: `true` (when default hash size >= 256)
- **Description**: When `true`, rejects certificates signed with SHA-1 algorithms as they are considered cryptographically weak.
- **Example**:
```csharp
configuration.SecurityConfiguration.RejectSHA1SignedCertificates = true;
```

#### RejectUnknownRevocationStatus
- **Type**: `bool`
- **Default**: `false`
- **Description**: When `true`, rejects certificates when the revocation status cannot be determined (e.g., CRL is not available).
- **Example**:
```csharp
configuration.SecurityConfiguration.RejectUnknownRevocationStatus = true;
```

#### MinimumCertificateKeySize
- **Type**: `ushort`
- **Default**: `2048` (CertificateFactory.DefaultKeySize)
- **Description**: The minimum RSA key size in bits that will be accepted. Common values are 2048, 3072, or 4096.
- **Example**:
```csharp
configuration.SecurityConfiguration.MinimumCertificateKeySize = 2048;
```

#### UseValidatedCertificates
- **Type**: `bool`
- **Default**: `false`
- **Description**: When `true`, skips validation for certificates that have already been successfully validated in the current session. This improves performance by caching validation results.
- **Example**:
```csharp
configuration.SecurityConfiguration.UseValidatedCertificates = true;
```

#### MaxRejectedCertificates
- **Type**: `int`
- **Default**: `5`
- **Description**: Limits the number of rejected certificates kept in history. A value of 0 means all rejected certificates are kept. A negative value means no history is kept.
- **Example**:
```csharp
configuration.SecurityConfiguration.MaxRejectedCertificates = 10;
```

### Suppressible Validation Errors

The following validation errors can be suppressed by handling the `CertificateValidation` event and setting `e.Accept = true`:

- **BadCertificateUntrusted**: The certificate is not trusted (not in the trusted store or chain).
- **BadCertificateHostNameInvalid**: The domain name in the endpoint URL does not match any domain in the certificate.
- **BadCertificateIssuerRevocationUnknown**: The revocation status of the issuer cannot be determined.
- **BadCertificateChainIncomplete**: The certificate chain is incomplete (missing issuer certificates).
- **BadCertificateIssuerTimeInvalid**: The issuer certificate has expired or is not yet valid.
- **BadCertificateIssuerUseNotAllowed**: The issuer certificate is not valid for the intended use.
- **BadCertificateRevocationUnknown**: The revocation status of the certificate cannot be determined.
- **BadCertificateTimeInvalid**: The certificate has expired or is not yet valid.
- **BadCertificatePolicyCheckFailed**: The certificate does not meet policy requirements (e.g., key size, signature algorithm).
- **BadCertificateUseNotAllowed**: The certificate is not valid for the intended use (missing key usage flags).

All other validation errors are **non-suppressible** and will always cause the validation to fail.

### Registering a Certificate Validation Callback

To handle certificate validation errors and decide whether to accept or reject certificates, register a callback handler:

```csharp
// Register the callback
configuration.CertificateValidator.CertificateValidation += CertificateValidationCallback;

// Implement the callback
private void CertificateValidationCallback(
    CertificateValidator sender,
    CertificateValidationEventArgs e)
{
    // Log the validation error
    Console.WriteLine($"Certificate validation error: {e.Error}");
    Console.WriteLine($"Certificate Subject: {e.Certificate.Subject}");

    // Decide whether to accept the certificate
    // For example, auto-accept BadCertificateUntrusted in development
    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
    {
        Console.WriteLine("Auto-accepting untrusted certificate in development mode.");
        e.Accept = true; // Accept this specific error
    }

    // To accept all errors for this certificate (use with caution):
    // e.AcceptAll = true;

    // To provide a custom error message:
    // e.ApplicationErrorMsg = "Custom error message";
}

// Don't forget to unregister when disposing
configuration.CertificateValidator.CertificateValidation -= CertificateValidationCallback;
```

### Configuring a Custom Certificate Validator

To use a custom certificate validator instead of the default `CertificateValidator`, implement the `ICertificateValidator` interface:

```csharp
public class CustomCertificateValidator : ICertificateValidator
{
    public Task ValidateAsync(X509Certificate2 certificate, CancellationToken ct)
    {
        return ValidateAsync(new X509Certificate2Collection { certificate }, ct);
    }

    public Task ValidateAsync(X509Certificate2Collection certificateChain, CancellationToken ct)
    {
        // Implement your custom validation logic
        X509Certificate2 certificate = certificateChain[0];

        // Example: Check custom requirements
        if (!MeetsCustomRequirements(certificate))
        {
            throw new ServiceResultException(
                StatusCodes.BadCertificateInvalid,
                "Certificate does not meet custom requirements.");
        }

        return Task.CompletedTask;
    }

    private bool MeetsCustomRequirements(X509Certificate2 certificate)
    {
        // Implement your custom validation logic
        return true;
    }
}

// To use the custom validator:
var customValidator = new CustomCertificateValidator();
configuration.CertificateValidator = customValidator;
```

Alternatively, you can extend the default `CertificateValidator` class to customize specific aspects:

```csharp
public class ExtendedCertificateValidator : CertificateValidator
{
    public ExtendedCertificateValidator(ITelemetryContext telemetry)
        : base(telemetry)
    {
    }

    protected override async Task InternalValidateAsync(
        X509Certificate2Collection certificates,
        ConfiguredEndpoint endpoint,
        CancellationToken ct = default)
    {
        // Call base validation first
        await base.InternalValidateAsync(certificates, endpoint, ct);

        // Add your custom validation logic
        X509Certificate2 certificate = certificates[0];

        if (!CustomValidationCheck(certificate))
        {
            throw new ServiceResultException(
                StatusCodes.BadCertificateInvalid,
                "Custom validation failed.");
        }
    }

    private bool CustomValidationCheck(X509Certificate2 certificate)
    {
        // Implement additional validation logic
        return true;
    }
}
```

### Best Practices

1. **Production vs Development**: Never use `AutoAcceptUntrustedCertificates = true` in production environments.

2. **Certificate Store Management**: Regularly review rejected certificates in the rejected store and move trusted certificates to the appropriate trust store.

3. **Revocation Checking**: Enable `RejectUnknownRevocationStatus` for high-security environments where CRL checking is critical.

4. **Minimum Key Size**: Use at least 2048 bits for RSA keys. Consider 3072 or 4096 bits for long-term security.

5. **SHA-1 Deprecation**: Keep `RejectSHA1SignedCertificates = true` to ensure only certificates with strong signature algorithms are accepted.

6. **Validation Callback**: Always log certificate validation events for security auditing purposes.

7. **Custom Validators**: When implementing a custom validator, ensure it complies with OPC UA security requirements and thoroughly test edge cases.