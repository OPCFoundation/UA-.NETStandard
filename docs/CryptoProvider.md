# Crypto provider

The stack performs its cryptography with the .NET platform by default. This document describes how to
replace that: with another library, a remote service, or hardware such as a TPM, an HSM, a PKCS#11 token
or a cloud key service — and how to keep private keys inside those devices so they never exist in
process memory.

Nothing here changes behaviour until you configure it. A deployment that configures nothing behaves
exactly as it did before the provider model existed.

## Contents

- [The short version](#the-short-version)
- [Holding a private key in a device](#holding-a-private-key-in-a-device)
- [Selecting a provider per purpose and per policy](#selecting-a-provider-per-purpose-and-per-policy)
- [Generating a key inside a device](#generating-a-key-inside-a-device)
- [Certificate renewal with a device held key](#certificate-renewal-with-a-device-held-key)
- [Validation status, compliance and audit](#validation-status-compliance-and-audit)
- [What can and cannot be claimed about FIPS](#what-can-and-cannot-be-claimed-about-fips)
- [Limitations](#limitations)

## The short version

`System.Security.Cryptography.RSA` and `ECDsa` are abstract classes, and the stack routes every
private-key operation through them. Implementations backed by hardware and cloud services already exist —
`RSACng` over a TPM key storage provider, `Pkcs11Interop`, Azure's `RSAKeyVault`. The stack therefore does
**not** define its own signing interface, because doing so would make all of those unusable.

What the provider model adds is the part the platform does not express:

| Concern | Type |
|---|---|
| Which operations a provider can serve | `ICryptoProvider`, `CryptoCapability` |
| Which provider serves which operation | `ICryptoProviderRegistry`, `CryptoPurpose` |
| What may be said about the module behind it | `CryptoValidationStatus` |
| How strictly that is enforced | `CryptoCompliancePolicy` |
| Where a new key comes from | `IKeyPairGenerator` |
| Where a certificate and its key live | `ICertificateStoreProvider`, `ICertificateStore` |

## Holding a private key in a device

A device-held key is represented by a **detached** private key: the `Certificate` holds the key alongside
the `X509Certificate2` rather than inside it.

```csharp
// The device generated the key and will not give it up. Only a handle is available.
RSA deviceKey = OpenKeyInDevice("app-instance");

using Certificate publicOnly = Certificate.FromRawData(certificateDer);
Certificate certificate = publicOnly.CopyWithDetachedPrivateKey(deviceKey);

// HasPrivateKey is true, GetRSAPrivateKey() returns the device key, and the
// stack signs and decrypts with it exactly as it would with a software key.
```

### Why not `CopyWithPrivateKey`?

`X509Certificate2.CopyWithPrivateKey` cannot be used for this. On Windows the certificate layer has fast
paths only for `RSACng` and `RSACryptoServiceProvider`; for any other implementation it falls back to
exporting the private parameters, which a non-extractable key refuses by definition:

| Approach | Result on Windows |
|---|---|
| `CopyWithPrivateKey(custom RSA)` | `CryptographicException` |
| `CopyWithPrivateKey(RSACng, ExportPolicy=None)` | works |
| `CopyWithDetachedPrivateKey(...)` | works, on every platform |

`CertificateRequest.CreateSelfSigned` is affected for the same reason, since it calls
`CopyWithPrivateKey` internally.

A CNG-backed key may use either route. Everything else — PKCS#11, cloud key services, bespoke
implementations — must use `CopyWithDetachedPrivateKey`.

### Lifetime

The detached key is shared by every handle created with `AddRef()` and is disposed once, with the last
handle, unless you pass `ownsPrivateKey: false`. Each call to `GetRSAPrivateKey()` returns an independent
non-owning view, so the usual `using` around the returned object is safe and does not destroy the device
key.

Exporting such a certificate as PKCS#12 throws rather than silently producing a file without a key.

## Selecting a provider per purpose and per policy

Real deployments mix sources. Selection is therefore a resolution over three discriminators — purpose,
security policy and certificate type — with the most specific registration winning:

```
(purpose, policy) → purpose → registered default → platform
```

```csharp
services.AddOpcUa()
    .AddCryptoProvider(crypto => crypto
        .For(CryptoPurpose.ApplicationInstanceKey).Use(tpmProvider)
        .For(CryptoPurpose.KeyAgreement).Use(tpmProvider)
        .For(CryptoPurpose.UserIdentityKey).Use(keyVaultProvider)
        .For(CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384).Use(hsmProvider));
```

Purposes:

| Purpose | Used for |
|---|---|
| `ApplicationInstanceKey` | Signing and decryption while a secure channel is opened |
| `UserIdentityKey` | Proving possession of a user certificate when a session is activated |
| `KeyAgreement` | Ephemeral key agreement for the elliptic curve policies |
| `CertificateIssuance` | Signing certificates, certificate requests and revocation lists |
| `ChannelSymmetric` | Per-message symmetric encryption and signing |
| `RandomNumberGeneration` | Nonces and other random material |

`CryptoPurpose` is a value type with well-known instances, not an enum, so you can define your own.

A provider bound to a purpose it does not declare is **skipped**, and resolution falls through to the next
candidate. A configuration mistake therefore fails close to its cause instead of deep inside a handshake.

Registration is explicit. There is no assembly scanning: it would make the effective security
configuration depend on what happens to be loaded, and it is incompatible with trimming and ahead-of-time
compilation.

### Where resolution happens

Resolve when something is **bound** — a channel opening, a certificate loading, a key being generated —
and hold the result. The registry is not meant to be consulted per message. `ChannelQuotas.CryptoProviders`
carries the registry to a channel, which resolves once and keeps the result for its lifetime, in the same
way it caches the security policy on its token.

## Generating a key inside a device

`IKeyPairGenerator` decides where the key behind a new application instance certificate comes from:

```csharp
var application = new ApplicationInstance { KeyPairGenerator = new TpmKeyPairGenerator() };
```

The builder arrives with the subject, subject alternative names and lifetime already set. An
implementation only chooses where the key comes from and how the certificate is signed.

An implementation backed by hardware **cannot** call the parameterless `CreateForRSA()` or
`CreateForECDsa()`, because those generate a key in software. It must supply the public key generated in
the device with `SetRSAPublicKey` / `SetECDsaPublicKey`, and sign with an `X509SignatureGenerator` that
calls back into the device.

For the Part 12 push flow the equivalent seam is `IPushCertificateKeyGenerator`, which is already
registered through dependency injection and can be replaced the same way.

## Certificate renewal with a device held key

Only one of the two Part 12 flows works with a key that cannot leave its device:

| Flow | Compatible? |
|---|---|
| `CreateSigningRequest(RegeneratePrivateKey = true)` then `UpdateCertificate` with no private key | ✅ the key is generated in the device and only a certificate request leaves it |
| `UpdateCertificate` with a private key supplied | ❌ raw key material is pushed into the server |

Between the two calls the regenerated key has to be staged. `HardwarePendingCertificateKeyStore` does this
for device-held keys: there is nothing to export, because the device already is the durable store, so only
the association between the pending certificate and its scope is kept. A software key is declined, so the
caller falls back to a store that knows how to protect exportable material.

## Validation status, compliance and audit

Every provider declares what may be said about the module behind it:

| Level | Meaning |
|---|---|
| `FipsValidated` | The provider names a validation certificate for its module |
| `FipsCapablePlatform` | It defers to platform cryptography, which is validated when the OS is configured for it |
| `Uncertified` | It carries no validation — a third-party library or a bespoke implementation |
| `Unknown` | It declined to say. Treated as uncertified, and always reported |

`CryptoCompliancePolicy` decides how strictly this is enforced:

| Policy | Effect |
|---|---|
| `Permissive` (default) | Existing behaviour. Nothing is filtered and nothing is warned about |
| `WarnOnUncertified` | Every provider carrying no validation is reported |
| `FipsOnly` | Providers carrying no validation are refused and the server does not start |

```csharp
using var auditor = new CryptoProviderAuditor(registry, telemetry, CryptoCompliancePolicy.FipsOnly);
auditor.Report();              // writes the effective configuration to the log
auditor.ThrowIfNotCompliant(); // refuses to continue if anything is uncertified
```

Metrics are published regardless of policy, because they are pull-based and cost nothing when nobody reads
them:

| Instrument | Meaning |
|---|---|
| `opc.ua.crypto.providers` | One measurement per provider, tagged with name and validation level |
| `opc.ua.crypto.providers.uncertified` | How many providers in use carry no validation |

## What can and cannot be claimed about FIPS

**.NET holds no cryptographic module validation certificate of its own.** It calls through to the module
the operating system supplies — CNG on Windows, OpenSSL on Linux, CoreCrypto on macOS. Whether that module
is running in a validated mode is a property of how the machine is configured, not of this stack.

What can honestly be said: *with `FipsOnly`, the stack performs no cryptography outside the platform
modules, and those modules are validated when the operating system is configured for it.*

What cannot be said: that the stack itself is validated.

These algorithms are **not** FIPS-approved and are enabled by default:

| Algorithm | Note |
|---|---|
| ChaCha20-Poly1305 | Not a NIST-approved algorithm |
| Brainpool P-256r1 / P-384r1 | Not in SP 800-186 |
| Curve25519 / X25519 | Not an approved curve |
| SHA-1 and P-SHA1 (Basic128Rsa15, Basic256) | Deprecated for new signatures by SP 800-131A |

`net472` and `net48` additionally use the `BouncyCastle.Cryptography` package for elliptic curve
certificate building. That package is **not** validated — the validated Bouncy Castle product is a
separate, commercially licensed distribution — so **those target frameworks cannot make a FIPS claim at
all**.

## Limitations

- **HTTPS with a device-held key does not work on Windows or macOS.** SChannel and the macOS Security
  framework require keys registered with a platform key storage provider. It works on Linux, where the
  TLS layer dispatches through the managed key. UA-TCP is unaffected on every platform.
- **The per-message symmetric path is not offloaded.** Session keys are symmetric and derived per channel
  token; a device round-trip per message would destroy throughput. Hardware is used only for the
  operations that happen when a channel opens or a session is activated.
- **A provider cannot yet contribute a new security policy.** The policy set is fixed at compile time.
  Adding one still requires changing the stack.
- **Network-backed providers block a thread** for the duration of the call, because the `RSA` and `ECDsa`
  contracts are synchronous. This is acceptable for a local device, where an operation takes single-digit
  milliseconds, and noticeable for a remote key service.

## Related

- [Certificates](Certificates.md) — how certificates are used in the certificate stores
- [CertificateManager](CertificateManager.md) — certificate lifecycle and Part 12 push management
- [ECC Certificates](EccProfiles.md) — the elliptic curve security policies
- [Dependency Injection](DependencyInjection.md) — how features are registered
- [Diagnostics](Diagnostics.md) — telemetry, audit events and metrics
- [Native AOT](NativeAoT.md) — why plugin discovery is registration-based
