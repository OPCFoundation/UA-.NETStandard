## CertificateManager

The `CertificateManager` provides centralized certificate lifecycle management for OPC UA applications. It replaces the scattered certificate handling across `CertificateValidator`, `CertificateIdentifier`, `CertificateTypesProvider`, and `CertificateFactory` with a cohesive set of interfaces following the Interface Segregation Principle.

### Architecture

The `CertificateManager` is composed of focused interfaces. Consumers depend only on the slice they need:

```
ICertificateManager (composite)
├── ICertificateRegistry         — "What certificates do I have?"
├── ICertificateTrustListManager — "Which stores exist?"
├── ICertificateValidatorEx      — "Is this certificate trusted?"
├── ICertificateLifecycle        — "What changed?"
└── ITrustListFileAccess         — "Read/write trust-list blobs"

Standalone (no CertificateManager dependency):
├── ICertificateFactory          — "Create from raw material"
└── ICertificateIssuer           — "Sign as a CA"
```

### Quick Start

#### Creating a CertificateManager

From an existing `SecurityConfiguration` (most common):

```csharp
using Opc.Ua;
using Opc.Ua.Security.Certificates;

// Create from SecurityConfiguration (auto-registers Peers, Users, Https, Rejected trust-lists)
var manager = CertificateManagerFactory.Create(
    securityConfiguration,
    telemetry,
    options =>
    {
        options.MaxRejectedCertificates = 10;
        options.ExpiryWarningThreshold = TimeSpan.FromDays(30);

        // Register custom trust-lists
        options.AddTrustList("MqttBrokers",
            trustedStorePath: "%LocalApplicationData%/OPC/pki/mqtt/trusted",
            issuerStorePath: "%LocalApplicationData%/OPC/pki/mqtt/issuers");
    });

// Load application certificates
await manager.LoadApplicationCertificatesAsync(securityConfiguration);
```

The `CertificateManager` is also automatically initialized by `ServerBase` and `ApplicationInstance` during startup.

#### Validating Certificates

```csharp
// Validate against the Peers trust-list (default)
CertificateValidationResult result = await manager.ValidateAsync(peerCertificate);
if (!result.IsValid)
{
    Console.WriteLine($"Validation failed: {result.StatusCode}");
}

// Validate a user X.509 identity token against the Users trust-list
CertificateValidationResult userResult = await manager.ValidateAsync(
    userCertificate,
    TrustListIdentifier.Users);

// Validate against a custom trust-list
CertificateValidationResult mqttResult = await manager.ValidateAsync(
    brokerCertChain,
    new TrustListIdentifier("MqttBrokers"));

// Validate with per-call options, including a per-error accept callback
// (the new-design replacement for the legacy CertificateValidation event).
var options = new CertificateValidationOptions
{
    AutoAcceptUntrustedCertificates = true,
    AcceptError = (certificate, error) =>
        // Suppress chain-incomplete errors only for self-test scenarios.
        error.StatusCode == StatusCodes.BadCertificateChainIncomplete
};
CertificateValidationResult devResult = await manager.ValidateAsync(
    serverCertificate,
    TrustListIdentifier.Peers,
    options);
```

#### Subscribing to Certificate Changes

```csharp
// Subscribe to all changes
manager.CertificateChanges.Subscribe(new MyObserver());

// Or filter by trust-list using System.Reactive (optional dependency)
// manager.CertificateChanges
//     .Where(e => e.TrustList == TrustListIdentifier.Peers)
//     .Subscribe(e => HandlePeerChange(e));

private class MyObserver : IObserver<CertificateChangeEvent>
{
    public void OnNext(CertificateChangeEvent e)
    {
        switch (e.Kind)
        {
            case CertificateChangeKind.ApplicationCertificateUpdated:
                Console.WriteLine($"Certificate updated: {e.CertificateType}");
                break;
            case CertificateChangeKind.CertificateExpiring:
                Console.WriteLine($"Certificate expiring: {e.OldCertificate?.Thumbprint}");
                break;
        }
    }
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}
```

#### Updating Application Certificates

```csharp
// Replace an application certificate (notifies all subscribers synchronously)
await manager.UpdateApplicationCertificateAsync(
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    newCertificate,
    issuerChain);
```

#### Working with Trust-Lists

```csharp
// Register a custom trust-list at runtime
manager.RegisterTrustList(
    new TrustListIdentifier("CustomDevices"),
    trustedStorePath: "/opt/opcua/pki/devices/trusted",
    issuerStorePath: "/opt/opcua/pki/devices/issuers");

// Open a store directly
using ICertificateStore trustedStore = manager.OpenTrustedStore(TrustListIdentifier.Peers);
using CertificateCollection certs = await trustedStore.EnumerateAsync();

// Transactional trust-list update (atomic commit/rollback)
await using ITrustListTransaction tx = await manager.BeginUpdateAsync(TrustListIdentifier.Peers);
await tx.AddTrustedCertificateAsync(newTrustedCert);
await tx.RemoveTrustedCertificateAsync(oldThumbprint);
await tx.CommitAsync();  // Atomic apply; disposing without commit rolls back

// Read/write trust-list as a blob (GDS Push Management)
TrustListData data = await manager.ReadTrustListAsync(TrustListIdentifier.Peers);
await manager.WriteTrustListAsync(TrustListIdentifier.Peers, data);
```

### Interfaces Reference

#### ICertificateRegistry

Read-only access to the application's own certificates.

| Member | Description |
|--------|-------------|
| `ApplicationCertificates` | All registered application certificate entries |
| `GetApplicationCertificate(NodeId)` | Find by OPC UA certificate type NodeId |
| `GetInstanceCertificate(string)` | Find by security policy URI |
| `GetEncodedChainBlob(string)` | DER-encoded cert+chain for wire transmission |

#### ICertificateTrustListManager

Manages an extensible set of named trust-lists.

| Member | Description |
|--------|-------------|
| `TrustLists` | All registered trust-list identifiers |
| `RegisterTrustList(...)` | Register a named trust-list (trusted + optional issuer store) |
| `OpenTrustedStore(TrustListIdentifier)` | Open the trusted-certificate store |
| `OpenIssuerStore(TrustListIdentifier)` | Open the issuer-certificate store (null if none) |
| `BeginUpdateAsync(TrustListIdentifier)` | Begin a transactional trust-list update |

Well-known trust-lists: `TrustListIdentifier.Peers`, `.Users`, `.Https`, `.Rejected`.

#### ICertificateValidatorEx

Validates certificates against any trust-list. Works with both stored and ephemeral (wire-parsed) certificates.

| Member | Description |
|--------|-------------|
| `ValidateAsync(CertificateCollection, TrustListIdentifier?, ...)` | Validate a chain against a trust-list |
| `ValidateAsync(Certificate, TrustListIdentifier?, ...)` | Validate a single certificate |
| `AcceptError` (property) | `Func<Certificate, ServiceResult, bool>?` — global per-error accept callback that fires for **every** validation done through this validator. Modern replacement for the legacy `CertificateValidator.CertificateValidation` event. Per-call `CertificateValidationOptions.AcceptError` (when set) takes precedence over this global hook. |

Returns `CertificateValidationResult` with `IsValid`, `StatusCode`, `Errors`, and `IsSuppressible`.

##### Migrating from the legacy `CertificateValidation` event

Set the global `AcceptError` property once on `ApplicationConfiguration.CertificateValidator` (or any `ICertificateValidatorEx` instance) and a single delegate handles every validation:

```csharp
// Before (legacy event):
config.CertificateValidator.CertificateValidation += (s, e) =>
{
    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted) { e.Accept = true; }
};

// After (modern global hook on ICertificateValidatorEx):
config.CertificateValidator.AcceptError = (cert, error) =>
    error.StatusCode == StatusCodes.BadCertificateUntrusted;

// Or per-call (overrides the global hook for that call only):
var options = new CertificateValidationOptions
{
    AcceptError = (cert, error) =>
        error.StatusCode == StatusCodes.BadCertificateUntrusted
};
CertificateValidationResult result = await manager.ValidateAsync(chain, options: options);
```

Per-call validation behaviour can be overridden via
`CertificateValidationOptions`:

| Option | Description |
|--------|-------------|
| `RejectSHA1SignedCertificates` | Override the global SHA-1 rejection policy. |
| `RejectUnknownRevocationStatus` | Override the global revocation-unknown rejection policy. |
| `MinimumCertificateKeySize` | Override the minimum acceptable RSA key size. |
| `AutoAcceptUntrustedCertificates` | Override the global auto-accept policy. |
| `AcceptError` | `Func<Certificate, ServiceResult, bool>` — invoked for each suppressible error encountered. Returning `true` accepts the specific error and validation continues. Structured replacement for the legacy `CertificateValidator.CertificateValidation += handler` + mutable `e.Accept = true` pattern. |

#### ICertificateLifecycle

Monitors certificate changes and expiry.

| Member | Description |
|--------|-------------|
| `CertificateChanges` | `IObservable<CertificateChangeEvent>` — subscribe for notifications |
| `UpdateApplicationCertificateAsync(...)` | Replace an app cert, notify subscribers |
| `RejectCertificateAsync(...)` | Save to rejected store (awaitable, no fire-and-forget) |

Change event kinds: `ApplicationCertificateUpdated`, `TrustListUpdated`, `CrlUpdated`, `CertificateRejected`, `CertificateExpiring`.

#### ITrustListFileAccess

Read/write trust-lists as serialized blobs for GDS Push Management (Part 12 §7.5).

| Member | Description |
|--------|-------------|
| `ReadTrustListAsync(TrustListIdentifier, TrustListMasks)` | Read trust-list contents |
| `WriteTrustListAsync(TrustListIdentifier, TrustListData, TrustListMasks)` | Write trust-list contents |

#### ICertificateFactory

Stateless certificate creation and parsing. Located in `Opc.Ua.Security.Certificates`.

| Member | Description |
|--------|-------------|
| `CreateFromRawData(ReadOnlyMemory<byte>)` | Parse a DER-encoded certificate |
| `ParseChainBlob(ReadOnlyMemory<byte>)` | Parse a DER-encoded chain blob |
| `CreateCertificate(string)` | Return a certificate builder |
| `CreateApplicationCertificate(...)` | Builder with OPC UA SAN extension |
| `CreateSigningRequest(...)` | Create a CSR |
| `CreateWithPEMPrivateKey(...)` | Combine cert with PEM private key |
| `CreateWithPrivateKey(...)` | Combine cert with private key from another cert |

#### ICertificateIssuer

CA signing and CRL revocation. Located in `Opc.Ua.Security.Certificates`.

| Member | Description |
|--------|-------------|
| `IssueCertificate(ICertificateBuilder, Certificate)` | Sign a certificate with a CA key |
| `RevokeCertificates(...)` | Produce an updated CRL |

### Pluggable Store Backends

Store providers are registered via `ICertificateStoreProvider`:

```csharp
public interface ICertificateStoreProvider
{
    string StoreTypeName { get; }
    bool SupportsStorePath(string storePath);
    ICertificateStore CreateStore(ITelemetryContext telemetry);
}
```

Built-in providers:
- `DirectoryStoreProvider` — file-system certificate store (default)
- `X509StoreProvider` — Windows certificate store (`X509Store:` prefix)
- `InMemoryStoreProvider` — in-memory store for testing (`InMemory:` prefix)

Custom providers are passed to the `CertificateManager` constructor:

```csharp
var manager = new CertificateManager(
    telemetry,
    storeProviders: [new DirectoryStoreProvider(), new MyAzureKeyVaultProvider()]);
```

### Certificate Wrapper and Reference Counting

The `Certificate` class wraps `X509Certificate2` with reference counting:

```csharp
// Certificate starts with refcount=1
using var cert = CertificateBuilder.Create("CN=Test").SetRSAKeySize(2048).CreateForRSA();

// AddRef before sharing (e.g., when store returns cached certs)
var shared = cert.AddRef();  // refcount=2

// Each Dispose decrements; inner X509Certificate2 disposed at refcount=0
shared.Dispose();  // refcount=1
cert.Dispose();    // refcount=0 → X509Certificate2.Dispose() called
```

`CertificateCollection.Dispose()` calls `Dispose()` on each member, decrementing their reference counts. Store methods like `EnumerateAsync()` call `AddRef()` on cached certificates before returning them, so disposing the returned collection does not invalidate the store's cache.

### Backward Compatibility

The legacy `CertificateValidator` class and `ICertificateValidator`
interface, the `CertificateTypesProvider` class, and the
`ApplicationConfiguration.CertificateValidator` /
`ServerBase.CertificateValidator` /
`ServerBase.InstanceCertificateTypesProvider` properties are now
marked `[Obsolete]`. They remain functional bridges into the new
design so that existing applications continue to compile and run
without changes:

- The legacy `CertificateValidator` class implements **both**
  `ICertificateValidator` (legacy) and `ICertificateValidatorEx`
  (new). This is the single most important compatibility bridge:
  every existing `new CertificateValidator(telemetry)` instance
  satisfies the new property type swap on
  `ApplicationConfiguration.CertificateValidator` (now
  `ICertificateValidatorEx`).
- The legacy `CertificateValidator.CertificateValidation` event
  with mutable `e.Accept = true` continues to fire. New code
  should use `CertificateValidationOptions.AcceptError` instead
  (see Quick Start above).
- `ApplicationConfiguration.CertificateManager` is a parallel
  property that exposes the new manager. It is populated by
  `ApplicationInstance.CheckApplicationInstanceCertificatesAsync`
  alongside the legacy `CertificateValidator` property so that
  both surfaces can coexist during migration.

`CertificateValidatorAdapter` bridges the new `ICertificateValidatorEx`
to the old `ICertificateValidator` interface and is the canonical
adapter used internally:

```csharp
// Wrap a manager (or any ICertificateValidatorEx) as the legacy interface.
ICertificateValidator oldApi = new CertificateValidatorAdapter(manager);

// Scope the bridge to a specific trust list (e.g. for X.509 user identity):
ICertificateValidator userApi = new CertificateValidatorAdapter(
    manager,
    TrustListIdentifier.Users);
```

The static methods on `CertificateFactory`
(`Create(ReadOnlyMemory<byte>)`, `CreateCertificate(...)`,
`CreateSigningRequest(...)`, `RevokeCertificate(...)`,
`CreateCertificateWith{,PEM}PrivateKey(...)`) are marked `[Obsolete]`
and forward to `Certificate.FromRawData(...)`,
`DefaultCertificateFactory.Instance.*`, and
`DefaultCertificateIssuer.Instance.*` respectively. The
`CertificateFactory.DefaultKeySize` / `DefaultLifeTime` /
`DefaultHashSize` constants are intentionally kept un-obsoleted because
they remain the canonical default values used across configuration
sites.

### OPC UA Specification Alignment

| Spec Area | Interface |
|-----------|-----------|
| Trust determination (Part 4 §6.1.3) | `ICertificateValidatorEx` with `TrustListIdentifier` |
| Certificate groups (Part 12) | `ICertificateTrustListManager` named trust-lists |
| Push/Pull management (Part 12 §7.7) | `ICertificateLifecycle.UpdateApplicationCertificateAsync` |
| TrustListType (Part 12 §7.5) | `ITrustListFileAccess` |
| Expiry monitoring (Part 9 §5.8.17) | `CertificateLifecycleMonitor` → `CertificateExpiring` events |
| Rejected certificates | `ICertificateLifecycle.RejectCertificateAsync` (awaitable) |
| User X.509 tokens (Part 4 §5.6.3) | `ValidateAsync(..., TrustListIdentifier.Users)` |
