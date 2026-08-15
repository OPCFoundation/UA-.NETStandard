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
- [Substituting the symmetric primitives](#substituting-the-symmetric-primitives)
- [Using a key served over a network](#using-a-key-served-over-a-network)
- [PubSub](#pubsub)
- [Contributing a security policy](#contributing-a-security-policy)
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
| The symmetric, derivation and random operations themselves | `ISymmetricCryptoProvider`, `IKeyDerivationProvider`, `ISecureRandomSource` |
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

### Using a PKCS#11 token

The optional `OPCFoundation.NetStandard.Opc.Ua.Security.Pkcs11` package supplies a certificate store
backed by a hardware token, smart card or HSM. It is never referenced by `Opc.Ua.Core`, so applications
that do not use one are unaffected.

```csharp
services.AddOpcUa()
    .AddPkcs11CertificateStore(new Pkcs11TokenOptions
    {
        ModulePath = "/usr/lib/softhsm/libsofthsm2.so",
        TokenLabel = "opcua",
        PinProvider = () => secretStore.Read("token-pin")
    })
    .AddPkcs11CryptoProvider(CryptoPurpose.ApplicationInstanceKey);
```

Stores are then addressed with an RFC 7512 URI, so an existing configuration moves to a token by changing
only the store path:

```
pkcs11:token=opcua;object=server?module-path=/usr/lib/softhsm/libsofthsm2.so
```

The store binds the token key with `CopyWithDetachedPrivateKey` for the reason below. Signing supports
PKCS#1 v1.5 and PSS, decryption supports OAEP and PKCS#1 v1.5, and ECDSA is supported on the curves the
token implements. Revocation lists are not held on a token, and objects are provisioned with the vendor's
tools rather than through the store.

On Linux the module is loaded through `DllImport("libdl")`, and glibc 2.34 folded `libdl` into `libc`.
Distributions that ship only the `libdl.so.2` ABI stub need `libc6-dev` (or an equivalent `libdl.so`
symlink) or the first call fails with `DllNotFoundException`. That is a property of the interop library
rather than of this package.

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
| `KeyDerivation` | Deriving the channel and session key material from a shared secret |
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

### What `FipsOnly` actually enforces at startup

`CryptoProviderAuditor.ThrowIfNotCompliant()` refuses to start when a provider bound to an operation
would not perform it. Carrying the facet is necessary but not sufficient: `Supports(algorithm)` is
consulted again at the point of use, and a provider that answers `false` there is bypassed in favour of
the platform *for that algorithm alone*. So the audit checks the algorithms of every policy the
application actually offers, and names what would fall through:

```
The compliance policy requires validated cryptography to perform every operation, but the
provider resolved for these cannot perform them and the platform would be used instead:
ChannelSymmetric (ChaCha20Poly1305) for ...#ECC_nistP256_ChaChaPoly, KeyDerivation (HkdfSha256) for ...
```

A module that covers AES-CBC but not ChaCha20-Poly1305 is therefore caught before it can run — where a
facet-only check would have reported it fully compliant while the platform performed every message on
any policy negotiating the algorithm it lacks. Nothing is reported when the platform provider is the one
resolved: the platform performing the platform's work is not a shortfall.

Narrow the offered policy set, or extend the module, rather than lowering the compliance policy.

### Output a provider did not produce is refused

`IKeyDerivationProvider.DeriveKey` and `ISecureRandomSource.GetBytes` return `void`, so a module that
no-ops, fills part of the buffer, or swallows an internal failure is indistinguishable from one that
succeeded — and the buffer it was handed becomes channel signing keys, encryption keys, initialization
vectors and nonces. The platform paths these replace cannot fail this way: `Utils.PSHA` returns the array
it built and `RandomNumberGenerator` throws.

That matters most for exactly the deployments this seam exists for. A network- or hardware-served module
can be transiently offline, and because both ends of a channel usually run the same image, both would
derive the same dead key material and the handshake would complete — traffic flowing with no
confidentiality and forgeable integrity, invisible to the operator and to the peer.

So the buffer is stamped before the call and checked after it. Output left untouched or zeroed is
rejected with `BadSecurityChecksFailed` rather than used. This cannot prove the output is good — no
caller-side check can — it fails closed on output that is provably unusable. **Implementations must fill
the whole span or throw.**

## Substituting the symmetric primitives

The asymmetric operations are pluggable because `RSA` and `ECDsa` are already the right abstraction. The
symmetric ones are not: the platform offers nothing that covers the block cipher, the authenticated
cipher and the message authentication code together, so three interfaces declare those operations
directly.

| Facet | Covers |
|---|---|
| `ISymmetricCryptoProvider` | AES-CBC, AES-GCM, ChaCha20-Poly1305 and the HMAC signatures |
| `IKeyDerivationProvider` | P_SHA1, P_SHA256, HKDF-SHA256, HKDF-SHA384 |
| `ISecureRandomSource` | Nonces and other random material |

A provider opts in by implementing one alongside `ICryptoProvider` and declaring the matching purpose.
They are separate interfaces rather than members of `ICryptoProvider`, so a provider written before they
existed still compiles, and a provider that can serve only some of them says so.

```csharp
public sealed class ValidatedModule : ICryptoProvider, ISymmetricCryptoProvider
{
    public string Name => "AcmeFIPS";

    public CryptoValidationStatus Validation => new(
        CryptoValidationLevel.FipsValidated, "Acme Cryptographic Module", "CMVP #1234");

    public ArrayOf<CryptoCapability> Capabilities { get; } =
        new(new[] { new CryptoCapability(CryptoPurpose.ChannelSymmetric) });

    // ... the operations
}

services.AddOpcUa()
    .AddCryptoProvider(crypto => crypto
        .For(CryptoPurpose.ChannelSymmetric).Use(module)
        .For(CryptoPurpose.KeyDerivation).Use(module)
        .For(CryptoPurpose.RandomNumberGeneration).Use(module));
```

**The consumer this exists for is a validated software module that must perform every operation, not
hardware offload.** A device round trip per message would destroy throughput, and nothing here makes that
viable.

`RandomNumberGeneration` is the one purpose with process-wide reach: nonces are created from many places
that have no container in scope, so an **unscoped** binding also becomes the process's nonce source. A
binding made for a single security policy — `.For(purpose, policyUri).Use(module)` — deliberately does
not, because it would otherwise redirect nonce generation for every other policy as well.

### What it costs when you do not use it

Nothing, by construction. `CryptoProviderFacets` returns `null` both when no registry is configured and
when resolution lands on the platform provider, because the platform facets perform exactly the code the
channel would otherwise run inline. A `null` facet tells the channel to take its existing path, so the
per-message code has no interface dispatch at all unless a provider was registered.

Resolution happens once, in `CalculateSymmetricKeySizes`, and the result is held for the life of the
channel. `SymmetricChannelCryptoBenchmarks` measures both paths — `EncryptSignThenDecryptVerify` is the
baseline and `EncryptSignThenDecryptVerifyThroughProvider` is the same work through the seam — so the
cost of the indirection is measured rather than assumed.

### A provider that cannot do what it was bound to

Binding a provider to `ChannelSymmetric` without implementing `ISymmetricCryptoProvider` would otherwise
be silent: resolution falls through to the platform and the channel keeps working, while a deployment
believes its validated module performed the per-message cryptography.

`CryptoCompliance.GetUnservedOperationPurposes` reports exactly that case, and under `FipsOnly`
`CryptoProviderAuditor.ThrowIfNotCompliant()` refuses to start rather than run on cryptography the
operator did not ask for.

## Using a key served over a network

`RSA` and `ECDsa` are synchronous contracts, and they are .NET's rather than this stack's, so a key
backed by a cloud key service occupies a thread for the whole of every call. Rather than replace those
contracts — which would make every ready-made hardware and cloud implementation unusable — an
implementation may **also** declare an asynchronous path:

```csharp
public sealed class KmsRsa : RSA, IAsyncRsaKey
{
    // The synchronous members are still implemented, and are what the paths
    // that are not yet asynchronous will use.

    public async ValueTask<byte[]> SignHashAsync(
        ReadOnlyMemory<byte> hash,
        HashAlgorithmName hashAlgorithm,
        RSASignaturePadding padding,
        CancellationToken ct = default)
        => await m_kms.SignAsync(hash, hashAlgorithm, padding, ct);

    public async ValueTask<byte[]> DecryptAsync(
        ReadOnlyMemory<byte> data,
        RSAEncryptionPadding padding,
        CancellationToken ct = default)
        => await m_kms.DecryptAsync(data, padding, ct);
}
```

The stack finds the facet by type test, so a key that does not implement it is unaffected and the
asynchronous paths complete synchronously for it — ordering, and everything that depends on it, is
unchanged.

| Path | Asynchronous? |
|---|---|
| Secure channel open and renew | ✅ |
| User identity token signing and decryption, session activation | ✅ |
| Service faults, and the synchronous reconnect handoff | ❌ both are reached from synchronous call sites |
| Certificate, certificate request and revocation list signing | ❌ by construction — `X509SignatureGenerator.SignData` is called by .NET internals |

## PubSub

The per-message cryptography a publisher and subscriber apply — AES-CTR and
HMAC-SHA-256, per Part 14 §7.2.4.4.3.1 — routes through
`ISymmetricCryptoProvider` when one is registered, so a validated module performs
it:

```csharp
services.AddOpcUa()
    .AddCryptoProvider(crypto => crypto
        .For(CryptoPurpose.ChannelSymmetric).Use(module))
    .AddPubSub(...);
```

The policies resolve the provider once, when they are constructed, and hold it;
nothing consults a registry per message. A provider that does not declare the
algorithms a policy needs is ignored rather than used, so a configuration mistake
does not stop publishing. `IPubSubSecurityPolicy` is unchanged — a provider is
supplied through the constructor, so implementations of that interface outside
this stack are unaffected.

The wrapper resolver selects the bundle it wraps with from the registered
policies rather than from a static default, which is what makes the registration
above take effect. Outside the container, `PubSubApplicationBuilder` does the
same with the policies it holds, and `WithSecurityPolicySelector` overrides the
choice per connection:

```csharp
var module = new ValidatedModule();

new PubSubApplicationBuilder(telemetry)
    .WithSecurityPolicySelector(
        (_, _) => new PubSubAes256CtrPolicy(module))
    // ...
    .Build();
```

### What device custody can and cannot mean here

**With a standard Security Key Service the key necessarily exists in process
memory.** `GetSecurityKeys` (Part 14 §8.3.2) returns raw key bytes over the wire,
so the property the client and server side achieve — the key never leaves the
device — **cannot** be achieved for PubSub through the SKS pull profile. That is
a property of the specification, not of this stack.

Introducing a wrapped-key envelope would change what is on the wire and break
interoperability with third-party key services and publishers, so it is
deliberately not done.

What is achievable, and is supported:

- **The operations** can be performed by a validated module, as above.
- **The key material can come from somewhere other than the SKS.**
  `IPubSubSecurityKeyProvider` is the seam; an implementation may derive per-token
  keys from a long-lived secret that stays in a device, so only the derived
  material is in memory.
- **Its lifetime is bounded.** `PubSubSecurityKey` zeroizes on disposal, the key
  ring disposes keys as it retires them, and the intermediate copies made while
  unpacking an SKS response are cleared rather than left in the heap.

## Contributing a security policy

The policy set is no longer fixed at compile time. A provider that implements a profile this stack does
not ship — a national or vendor profile, or one added by a later specification — can make it visible at
runtime by constructing a `SecurityPolicyInfo` and registering it:

```csharp
using IDisposable registration = SecurityPolicies.Register(
    new SecurityPolicyInfo("urn:vendor:SecurityPolicy#CustomProfile", "CustomProfile")
    {
        PlatformSupport = () => true,
        SupportedCertificateTypes = [ObjectTypeIds.RsaSha256ApplicationCertificateType],
        IsDefault = true
    });
```

or through the builder, alongside the provider that performs its cryptography:

```csharp
services.AddOpcUa()
    .AddCryptoProvider(crypto => crypto
        .For(CryptoPurpose.ChannelSymmetric, "urn:vendor:SecurityPolicy#CustomProfile").Use(module))
    .AddSecurityPolicy(customPolicy);
```

A registered policy is discoverable through the same API as a built-in one — `GetInfo`, `GetUri`,
`GetDisplayName`, `GetDisplayNames` and the default-URI helpers — because those are now driven from one
table rather than from reflection over the constants. Registering a URI or name that already exists
throws unless `replaceExisting: true` is passed, which makes shadowing a built-in policy deliberate and
reversible: disposing the returned registration restores what was there before.

### Resolving the policy set

Those lookups are members of `ISecurityPolicyRegistry`, which is the object that owns the policy set.
Resolve it to work against the policies *this* application registered:

```csharp
public sealed class MyEndpointPicker(ISecurityPolicyRegistry policies)
{
    public string[] Offered => policies.GetDisplayNames();
}
```

`AddSecurityPolicy` registers the registry for you; call `AddSecurityPolicyRegistry()` when you want to
resolve one without contributing a policy of your own.

The identity token handlers take one too, so a token's security policy URI resolves against the policies
that application offers rather than the process-wide fallback:

```csharp
var handler = new UserNameIdentityTokenHandler(username, password, policies);
```

Passing nothing keeps the previous behaviour and uses `SecurityPolicies.Default`.

The registry a container builds is **its own**. A policy registered by one application is not visible to
another hosted in the same process, and it is not visible to `SecurityPolicies.Default`. That
fallback carries exactly the built-in set and is what the paths that run before any container exists —
configuration loading, most obviously — resolve their policies through:

```csharp
string? uri = SecurityPolicies.Default.GetUri("Basic256Sha256");
```

Construct a registry directly when you want an isolated set, which is also what makes the policy set
testable without touching process-wide state:

```csharp
using var policies = new SecurityPolicies(telemetry);
using IDisposable registration = policies.Register(customPolicy);
```

The registry creates its logger from the `ITelemetryContext` it is given, which is why `Encrypt`,
`Decrypt` and the signature helpers take no `ILogger` argument.

`PlatformSupport` is what decides whether the policy is offered on the machine it is running on, so a
policy whose algorithms are unavailable is filtered out the same way the built-in ones are. The
`ECC_curve25519` and `ECC_curve448` profiles are the worked example: they ship in the tree but are not
advertised, and a consumer can light them up from outside without rebuilding the stack — which is what
`RegisterLightsUpCurvePoliciesFromOutsideCore` asserts.

Registering a policy makes it **advertised and resolvable**; it does not by itself supply the
cryptography behind it. For the two curve profiles above, the in-tree key agreement is still behind a
compile-time symbol that no project defines and a BouncyCastle dependency, so a deployment that lights
them up supplies the operations through a provider, exactly as [the sections above](#substituting-the-symmetric-primitives)
describe. That is the intended division: the policy set says *what* is offered, the provider says *who*
performs it.

Removing the reflection that used to build these tables also removed the last reflection in
`Opc.Ua.Core.Security.Constants`, which is why this is also a trimming and Native AOT improvement.

## Limitations

- **HTTPS with a device-held key does not work on Windows or macOS.** SChannel and the macOS Security
  framework require keys registered with a platform key storage provider. It works on Linux, where the
  TLS layer dispatches through the managed key. UA-TCP is unaffected on every platform.
- **The per-message symmetric path is not offloaded to hardware.** Session keys are symmetric and derived
  per channel token; a device round trip per message would destroy throughput. Hardware is used only for
  the operations that happen when a channel opens or a session is activated. Substituting a *software*
  implementation is supported — see above.
- **Certificate issuance cannot be made asynchronous at all.** `X509SignatureGenerator.SignData` is
  called by .NET's own `CertificateRequest` and CRL builders, so signing a certificate, a certificate
  request or a revocation list with a remote key occupies a thread by construction. Service faults and
  the synchronous reconnect handoff are in the same position, because both are reached from call sites
  that cannot become asynchronous without a contract break.

## Related

- [Certificates](Certificates.md) — how certificates are used in the certificate stores
- [CertificateManager](CertificateManager.md) — certificate lifecycle and Part 12 push management
- [ECC Certificates](EccProfiles.md) — the elliptic curve security policies
- [Dependency Injection](DependencyInjection.md) — how features are registered
- [Diagnostics](Diagnostics.md) — telemetry, audit events and metrics
- [Native AOT](NativeAoT.md) — why plugin discovery is registration-based
