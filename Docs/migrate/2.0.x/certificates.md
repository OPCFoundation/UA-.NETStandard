# Certificates and ICertificateProvider

> **When to read this:** Read this for the new ref-counted `Certificate` wrapper, the segregated-interface `CertificateManager` design, the `ICertificateProvider` cache, and the obsoleted `X509Certificate2` direct-exposure APIs.

## Centralised certificate cache via `ICertificateProvider`

A new public `ICertificateProvider` interface exposes the existing
`CertificateCache` for resolving private-key certs on demand:

```csharp
public interface ICertificateProvider
{
    Certificate? TryGetPrivateKeyCertificate(string thumbprint);          // sync
    ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
        CertificateIdentifier identifier,
        ICertificatePasswordProvider? passwordProvider = null,
        string? applicationUri = null,
        CancellationToken ct = default);
}
```

`CertificateManager` exposes one via the new `CertificateProvider`
property; `ICertificateManager` likewise. The provider follows the
**TryGet → async ValueTask** pattern: cache hits complete
synchronously without allocations; misses fall through to
`CertificateIdentifierResolver.LoadPrivateKeyAsync` and write the
loaded cert back into the cache.

Wire it through to the new `X509IdentityTokenHandler` /
`UserIdentity.CreateAsync` overloads:

```csharp
UserIdentity userIdentity = await UserIdentity.CreateAsync(
    certificateIdentifier,
    passwordProvider,
    configuration.CertificateManager.CertificateProvider,
    ct);
```

## Certificate Management

### Certificate and CertificateCollection wrapper types

`X509Certificate2` and `X509Certificate2Collection` are no longer used directly in the public API. They are replaced by `Certificate` and `CertificateCollection` (in `Opc.Ua.Security.Certificates`).

**Migration steps:**

```csharp
// Before:
X509Certificate2 cert = new X509Certificate2(rawData);
X509Certificate2Collection certs = await store.Enumerate();

// After:
Certificate cert = new Certificate(rawData);
CertificateCollection certs = await store.EnumerateAsync();
```

`Certificate` implements reference counting. Call `AddRef()` before sharing a certificate across ownership boundaries, and `Dispose()` to release. The inner `X509Certificate2` is disposed when the last reference is released.

For .NET interop, use `certificate.AsX509Certificate2()` which returns a copy the caller must dispose. The internal `X509Certificate2` is accessible via the `internal X509` property for `InternalsVisibleTo` friends.

`CertificateBuilder.CreateForRSA()` and `CreateForECDsa()` now return `Certificate` instead of `X509Certificate2`.

### CertificateManager and segregated interfaces

A new centralized `CertificateManager` replaces the scattered certificate handling across `CertificateValidator`, `CertificateIdentifier`, `CertificateTypesProvider`, and `CertificateFactory`. It is composed of focused interfaces:

| Interface | Purpose | Location |
|-----------|---------|----------|
| `ICertificateRegistry` | Read-only access to app certificates | `Opc.Ua` |
| `ICertificateTrustListManager` | Named trust-list management | `Opc.Ua` |
| `ICertificateValidatorEx` | Trust-list-scoped validation | `Opc.Ua` |
| `ICertificateLifecycle` | Change notifications + cert updates | `Opc.Ua` |
| `ICertificateFactory` | Stateless cert creation/parsing | `Opc.Ua.Security.Certificates` |
| `ICertificateIssuer` | CA signing + CRL revocation | `Opc.Ua.Security.Certificates` |
| `ICertificateStoreProvider` | Pluggable store backends | `Opc.Ua` |

The `CertificateManager` is automatically initialized by `ServerBase` and `ApplicationInstance` during startup. Access it via `ServerBase.CertificateManager` or `ApplicationInstance.CertificateManager`.

**Trust-lists are now named and extensible:**

```csharp
// Well-known: TrustListIdentifier.Peers, .Users, .Https, .Rejected
// Custom:
manager.RegisterTrustList(new TrustListIdentifier("MqttBrokers"),
    trustedStorePath: "...", issuerStorePath: "...");

// Validate against any trust-list
var result = await manager.ValidateAsync(cert, TrustListIdentifier.Users);
```

**Subscribe to certificate changes:**

```csharp
manager.CertificateChanges.Subscribe(observer);
```

See [CertificateManager.md](../../CertificateManager.md) for the full API reference and usage guide.

### CertificateIdentifier is metadata-only

`CertificateIdentifier` no longer caches a `Certificate`, no longer implements `IDisposable`, and the cert-bearing constructors / instance methods have been removed. Use `CertificateIdentifierResolver` to materialize a `Certificate` from an identifier.

**Removed members:**

* `Certificate` get/set property and the cached `m_certificate` field.
* `IDisposable` declaration, `Dispose()`, `DisposeCertificate()`.
* Constructors `CertificateIdentifier(Certificate)`, `CertificateIdentifier(Certificate, CertificateValidationOptions)`, `CertificateIdentifier(byte[])`.
* Instance methods `FindAsync(...)`, `LoadPrivateKeyAsync(char[], ...)`, `LoadPrivateKeyExAsync(...)`, `OpenStore(...)`.
* `IOpenStore` interface declaration on `CertificateIdentifier`.

**`RawData`** is now backed by an explicit `byte[]` field. The setter still derives `SubjectName` / `Thumbprint` / `CertificateType` from the parsed raw bytes.

**`ICertificateRegistry.GetIssuersAsync`** now returns `IList<CertificateIssuerReference>` (a public sealed record with `Certificate Certificate, CertificateValidationOptions Options`) instead of `IList<CertificateIdentifier>`. Existing callers must update the list type and switch from `CertificateIdentifier.Certificate` to `CertificateIssuerReference.Certificate`.

**Migration patterns:**

| Before (legacy) | After |
|---|---|
| `var id = new CertificateIdentifier(cert);` | `var id = new CertificateIdentifier { Thumbprint = cert.Thumbprint, SubjectName = cert.Subject, CertificateType = CertificateIdentifier.GetCertificateType(cert) };` |
| `var id = new CertificateIdentifier(rawData);` | `var id = new CertificateIdentifier { RawData = rawData };` |
| `id.Certificate` (read) | `await CertificateIdentifierResolver.ResolveAsync(id, registry, needPrivateKey: false, applicationUri, telemetry, ct)` |
| `id.Certificate = cert;` | Drop the assignment. Cert lifecycle is owned by `CertificateManager` (use `ICertificateLifecycle.UpdateApplicationCertificateAsync`) or by a local variable. |
| `await id.FindAsync(true, applicationUri, telemetry, ct)` | `await CertificateIdentifierResolver.LoadPrivateKeyAsync(id, passwordProvider, applicationUri, telemetry, ct)` |
| `await id.LoadPrivateKeyExAsync(passwordProvider, applicationUri, telemetry, ct)` | `await CertificateIdentifierResolver.LoadPrivateKeyAsync(id, passwordProvider, applicationUri, telemetry, ct)` |
| `id.OpenStore(telemetry)` | `CertificateIdentifierResolver.OpenStore(id, telemetry)` |
| `using var id = new CertificateIdentifier(...);` | `var id = new CertificateIdentifier(...);` (no `using`) |
| `IList<CertificateIdentifier> issuers = ...; var cert = issuers[i].Certificate;` | `IList<CertificateIssuerReference> issuers = ...; var cert = issuers[i].Certificate;` |

See [CertificateManager.md](../../CertificateManager.md#migration-certificateidentifier-is-metadata-only) for the full migration walkthrough.

### PushManagement transactions: TrustList/Certificate updates now require `ApplyChanges`

`ConfigurationNodeManager` now implements the full OPC UA Part 12 §7.10.2
PushManagement transaction model. This affects any code (Client or
custom `ServerConfiguration`-adjacent NodeManager) that calls the
`TrustList` `AddCertificate` / `RemoveCertificate` / `CloseAndUpdate`
Methods directly and previously observed the change take effect
immediately:

| 1.5.378 behavior | 2.0 behavior |
|---|---|
| `TrustList.AddCertificate` / `RemoveCertificate` / `CloseAndUpdate` applied to the store immediately; a subsequent `ReadTrustList` reflected the change right away. | The same calls are **staged**. The store is unchanged — and `ReadTrustList` still returns the *old* contents — until the Session that made the call also calls `ServerConfiguration.ApplyChanges`. `CloseAndUpdate` now always reports `applyChangesRequired = true` (previously reported `false` when no restart was needed). |
| No concept of a per-Session transaction; concurrent Sessions could interleave TrustList/Certificate updates freely. | Exactly one transaction is active at a time, owned by the Session that started it; every other Session's staging call fails with `Bad_TransactionPending` until that Session calls `ApplyChanges`, calls the new `CancelChanges` Method, or closes. |
| `UpdateCertificate`/`CreateSelfSignedCertificate` already required `ApplyChanges` (§7.7.5) — unaffected by this change. | Unchanged; now share the same transaction as TrustList updates, plus the new `DeleteCertificate` Method. |

No application code changes are required if you already call
`ApplyChanges` after every push-management write (the previously
recommended/spec-compliant pattern for Certificate updates) — this
change only affects code that assumed `TrustList` writes took effect
without it. See
[CertificateManager.md § PushManagement Transactions](../../CertificateManager.md#pushmanagement-transactions-opc-ua-part-12-7102-71011)
for the full model, the new standard nodes (`SupportsTransactions`,
`DeleteCertificate`, `CancelChanges`, `TransactionDiagnostics`), and the
`IPushConfigurationTransactionCoordinator` / `IPendingCertificateKeyStore`
DI replacement points.

### Obsoleted certificate APIs

The following APIs are marked `[Obsolete]` and will be removed in the next minor version. They remain
functional forwarders to the new design for binary-compatibility, but emit `CS0618` warnings when used.

| Obsolete API | Replacement |
|-------------|-------------|
| `CertificateFactory.Create(ReadOnlyMemory<byte>)` | `Certificate.FromRawData(ReadOnlyMemory<byte>)` or `DefaultCertificateFactory.Instance.CreateFromRawData(...)` |
| `CertificateFactory.CreateCertificate(string)` | `DefaultCertificateFactory.Instance.CreateCertificate(string)` |
| `CertificateFactory.CreateCertificate(string, string, string, ArrayOf<string>)` | `DefaultCertificateFactory.Instance.CreateApplicationCertificate(...)` |
| `CertificateFactory.CreateSigningRequest(...)` | `DefaultCertificateFactory.Instance.CreateSigningRequest(...)` |
| `CertificateFactory.RevokeCertificate(...)` | `DefaultCertificateIssuer.Instance.RevokeCertificates(...)` |
| `CertificateFactory.CreateCertificateWithPEMPrivateKey(...)` | `DefaultCertificateFactory.Instance.CreateWithPEMPrivateKey(...)` |
| `CertificateFactory.CreateCertificateWithPrivateKey(...)` | `DefaultCertificateFactory.Instance.CreateWithPrivateKey(...)` |
| `CertificateStoreIdentifier.RegisterCertificateStoreType(...)` | Register `ICertificateStoreProvider` via dependency injection or pass to the `CertificateManager` constructor |
| `CertificateValidator` (class) | `ICertificateManager` (composed of `ICertificateValidatorEx` for validation, `ICertificateRegistry` for app certs, `ICertificateTrustListManager` for trust lists, `ICertificateLifecycle` for change events). Construct via `CertificateManagerFactory.Create(securityConfiguration, telemetry, ...)` |
| `ICertificateValidator` (interface) | `ICertificateValidatorEx` from `ICertificateManager`. The new interface returns a structured `CertificateValidationResult` (`IsValid`, `StatusCode`, `Errors`, `IsBeingTrustedTransiently`) instead of throwing. Per-error accept logic moves from the `CertificateValidation` event to the new `CertificateValidationOptions.AcceptError` callback. |
| `CertificateTypesProvider` (class) | `ICertificateRegistry` (composed in `ICertificateManager`). Use `using CertificateEntry? e = manager.AcquireApplicationCertificateBySecurityPolicy(securityPolicyUri);` (caller-owned — dispose the entry). The entry already carries the chain: use `e.IssuerChain` / `e.GetEncodedChainBlob()`. |
| `ApplicationConfiguration.CertificateValidator` (property) | `ApplicationConfiguration.CertificateManager` (parallel property — set in `ApplicationInstance.CheckApplicationInstanceCertificatesAsync`) |
| `ServerBase.CertificateValidator` (property) | `ServerBase.CertificateManager` |
| `ServerBase.InstanceCertificateTypesProvider` (property) | `ServerBase.CertificateManager` (use `ICertificateRegistry` surface) |

> **Lifecycle ordering.** `configuration.CertificateManager` is populated *inside* `await applicationInstance.CheckApplicationInstanceCertificatesAsync(...)`. Code that reads it before that call gets `null`. The required ordering is:
>
> 1. Construct `new ApplicationInstance(telemetry)`.
> 2. Load `ApplicationConfiguration` (e.g. via `LoadApplicationConfigurationAsync`).
> 3. `await applicationInstance.CheckApplicationInstanceCertificatesAsync(silent: false, ..., ct);`.
> 4. Read `configuration.CertificateManager` / pass `configuration.CertificateManager.CertificateProvider` to `UserIdentity.CreateAsync(...)`.

#### Migrating the `CertificateValidator.CertificateValidation` event

The legacy event with mutable `e.Accept = true` mutability has been replaced by
the structured `CertificateValidationOptions.AcceptError` callback:

```csharp
// Before:
configuration.CertificateValidator.CertificateValidation += (s, e) =>
{
    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
    {
        e.Accept = true;
    }
};
await configuration.CertificateValidator.ValidateAsync(cert);

// After:
var options = new CertificateValidationOptions
{
    AcceptError = (cert, error) =>
        error.StatusCode == StatusCodes.BadCertificateUntrusted
};
CertificateValidationResult result =
    await applicationInstance.CertificateManager.ValidateAsync(cert, options: options);
if (!result.IsValid)
{
    throw new ServiceResultException(result.StatusCode);
}
```

#### Endpoint-aware validation helpers

`CertificateValidator.ValidateApplicationUri(...)` and
`CertificateValidator.ValidateDomains(...)` are now exposed as extension
methods on `ICertificateValidatorEx` in the
`Opc.Ua.CertificateValidationExtensions` static class. Existing call sites
that previously used the legacy class continue to work transparently.

> The `CertificateFactory.DefaultKeySize` / `DefaultLifeTime` / `DefaultHashSize` constants are
> intentionally **not** marked obsolete; they remain the canonical default values used across
> configuration sites.

To suppress `CS0618` warnings while migrating, add at the top of affected files:
```csharp
#pragma warning disable CS0618 // Obsolete API usage during migration
```

---

**See also**

- Related: [identity.md](identity.md), [configuration.md](configuration.md), [sessions-subscriptions.md](sessions-subscriptions.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

