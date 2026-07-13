## CertificateManager

The `CertificateManager` provides centralized certificate lifecycle management for OPC UA applications. It replaces the scattered certificate handling across `CertificateValidator`, `CertificateIdentifier`, `CertificateTypesProvider`, and `CertificateFactory` with a cohesive set of interfaces following the Interface Segregation Principle.

> **Note:** Since the `x509` refactor, `CertificateIdentifier` is **metadata-only** (`StoreType` / `StorePath` / `SubjectName` / `Thumbprint` / `CertificateType` / `RawData` / `ValidationOptions`). It no longer caches a `Certificate`, no longer implements `IDisposable`, and the `Certificate` property, `(Certificate)` / `(Certificate, options)` / `(byte[])` constructors, `FindAsync`, `LoadPrivateKey*Async` and `OpenStore` instance methods have been removed. The `CertificateManager` (via `ICertificateRegistry`) is now the single source of truth for materialized application certificates; `CertificateIdentifierResolver` is the stateless helper used to materialize a `Certificate` from an identifier on demand. See *Migration: CertificateIdentifier is metadata-only* below.

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

> **Concurrency & caching:** `ValidateAsync` is designed for highly concurrent
> use — a single shared `CertificateManager` validates many certificates in
> parallel without serializing on an internal lock. Each trust-list is backed by
> an immutable, lock-free state snapshot, and the trusted/issuer stores and their
> CRLs are cached and reused across validations (re-read only when the backing
> store changes, e.g. via the trust-list APIs or an out-of-band directory change).
> Servers that validate a client certificate per incoming secure channel therefore
> scale across cores instead of bottlenecking on certificate validation.

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

#### Server-Side Certificate Rotation via Push (OPC UA Part 12 §7.10.9)

When a client rotates a server's application certificate through the standard `ServerConfiguration.UpdateCertificate` + `ServerConfiguration.ApplyChanges` push flow, the server must — once the `ApplyChanges` response has been delivered — force the SecureChannels that were negotiated against the old certificate to renegotiate. The session (and any subscriptions) stay alive so the client's reconnect logic can transfer them onto a fresh channel.

The stack implements this contract in two pieces:

1. **`ConfigurationNodeManager.ApplyChanges`** captures the old certificate per group at staging time, then schedules a deferred apply that (a) waits ~250 ms for the method response to flush, (b) re-syncs the registry from disk via `CertificateManager.UpdateAsync`, and (c) calls the new channel-cut hook on every transport listener.
2. **`ITransportListenerCertificateRotation`** is an optional capability interface on `ITransportListener`. `TcpTransportListener` implements it by thumbprint-matching the per-channel `ServerCertificate` and force-closing only affected channels (listener socket stays bound). `HttpsTransportListener` implements it by cycling its Kestrel host (`Stop()` + `Start()`).

Tests and hosts that need deterministic timing can await the deferred work via `IConfigurationNodeManager.DrainPendingApplyChangesAsync(CancellationToken)`.

Custom transport listeners opt into the renegotiate hook by implementing the capability interface:

```csharp
public sealed class MyTransportListener : ITransportListener, ITransportListenerCertificateRotation
{
    // ...

    public IReadOnlyList<string> CloseChannelsForCertificate(Certificate oldCertificate)
    {
        // Close every channel whose negotiated ServerCertificate.Thumbprint
        // matches oldCertificate.Thumbprint. Keep the listener socket bound.
        // Return the global channel ids that were closed (for diagnostics).
        return [];
    }
}
```

Server-base subclasses that want to observe rotation in custom ways can still subscribe to `ICertificateManager.CertificateChanges` and react to `ApplicationCertificateUpdated` events — but should not drive channel teardown from that hook (the event fires twice during a push update: once when the new cert is staged, once when `ApplyChanges` reloads, and only `ApplyChanges` has the real old-cert reference).

#### Client-Side Auto-Detection of Certificate Changes

`ManagedSession` automatically subscribes to
`CertificateManager.CertificateChanges` and surfaces the three
scenarios discussed in client-side certificate management:

1. **Own (client) certificate renewal** — `ApplicationCertificateUpdated` fires from `UpdateApplicationCertificateAsync` / `ReloadApplicationCertificatesAsync`.
2. **Trust-list add/remove** — `TrustListUpdated` fires from `WriteTrustListAsync` and from a successful `TrustListTransaction.CommitAsync` whenever certificates were added or removed.
3. **CRL add/remove** — `CrlUpdated` fires from the same paths whenever CRLs were modified.

Each event is forwarded to subscribers of
`ManagedSession.ApplicationCertificateChanged` for diagnostics and for
applications that want to implement custom rotation policies.

By default
(`ManagedSession.DisableAutoReconnectOnCertificateChange` is `false`),
the managed session ALSO automatically reacts:

- **`ApplicationCertificateUpdated`** — calls
  `Session.ReloadInstanceCertificateAsync` (so the next ActivateSession
  is signed with the rotated client cert) and triggers a reconnect via
  the state machine. The new client cert takes effect within
  milliseconds rather than at the next `SecurityTokenLifetime` rekey
  (Part 4 §5.5.2).
- **`TrustListUpdated` / `CrlUpdated`** — wakes a long-lived
  per-session revalidation loop that:
  1. Debounces a burst of events (250 ms window) on the injected
     `TimeProvider`. A batch trust-list refresh or a fleet onboarding
     that publishes dozens of events in a few milliseconds collapses
     to a single validation on the final state.
  2. Re-runs
     `ICertificateValidatorEx.ValidateAsync(serverCert)` against the
     server certificate currently cached on the configured endpoint.
     There is exactly **one** validation in flight per session — the
     loop's single-reader semantics serialise the work and signals
     that arrive during a validation re-arm the next iteration so the
     final trust state is always honoured exactly once after the burst
     settles.
  3. Calls `StateMachine.TriggerReconnect()` **only when the result
     flips from valid to invalid** — sessions whose server cert is
     still trusted under the new state stay connected. This keeps a
     new server being onboarded into a shared client/server fleet
     from forcing every session in the process to reconnect.

The revalidation loop is implemented in
`Libraries/Opc.Ua.Client/Session/ManagedSession.CertificateChanges.cs`.
It uses a bounded `Channel<int>` of capacity 1 with
`BoundedChannelFullMode.DropWrite` so the notifier thread never
allocates a `Task` or `CancellationTokenSource` per event — duplicate
signals collapse into the pending one. The loop pattern mirrors
`RunIdentityRefreshLoopAsync` in `ManagedSession.cs` (single Task per
session, started in `SubscribeCertificateChanges`, cancelled +
awaited in `StopRevalidationLoopAsync` from `DisposeAsync`).

```csharp
using var managed = await ManagedSession.CreateAsync(
    configuration, endpoint, sessionFactory);
// Auto-reconnect on cert/trust/CRL changes is on by default.
// Opt out by setting DisableAutoReconnectOnCertificateChange = true.

managed.ApplicationCertificateChanged += (sender, e) =>
{
    Console.WriteLine($"observed {e.Kind} on {e.TrustList}");
};
```

Applications that need manual control (auditing, idempotency) can opt
out of auto-reconnect by setting
`ManagedSession.DisableAutoReconnectOnCertificateChange = true` and
continue calling `Session.ReloadInstanceCertificateAsync` + their own
reconnect logic. The `ApplicationCertificateChanged` event still fires
either way.

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
| `SnapshotApplicationCertificates()` | Caller-owned snapshot of all registered application certificate entries — dispose the returned `CertificateEntryCollection` |
| `AcquireApplicationCertificateByType(NodeId)` | Caller-owned entry found by OPC UA certificate type NodeId — dispose the returned `CertificateEntry` |
| `AcquireApplicationCertificateBySecurityPolicy(string)` | Caller-owned entry found by security policy URI — dispose the returned `CertificateEntry` |

> **Certificate chain:** each `CertificateEntry` already carries its issuer chain, so there are no
> separate chain-loading methods. Use `entry.IssuerChain` for the issuers and
> `entry.GetEncodedChainBlob()` for the DER-encoded `leaf || issuers` blob.

> **Ownership:** `CertificateEntry` (and `CertificateEntryCollection`) implement `IDisposable`.
> Every accessor returns an independent, reference-counted handle that the caller **owns and
> must dispose** (a `using` is recommended). Disposing a returned entry has no effect on the
> registry's own certificates, and the registry may concurrently replace its certificates
> (e.g. a hot-update) without invalidating handles you already hold.

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
- `SharedKeyValueCertificateStoreProvider` — a certificate store distributed across a redundant server set over a shared key/value backend (`kv:` prefix, store type `SharedKeyValue`); shares the trusted, issuer and rejected lists and CRLs with fail-closed record integrity. See [High Availability § Shared certificate stores](HighAvailability.md).

Custom providers are passed to the `CertificateManager` constructor (or via `CertificateManagerOptions.AddStoreProvider`):

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

All migration is complete; the legacy `CertificateValidator` class,
`ICertificateValidator` interface, `CertificateValidatorAdapter`
bridge, `CertificateTypesProvider` class, and the legacy
`ApplicationConfiguration.CertificateValidator` /
`ServerBase.CertificateValidator` /
`ServerBase.InstanceCertificateTypesProvider` properties have been
removed. New code uses `CertificateManager` directly (or the
`ICertificateValidatorEx`, `ICertificateRegistry`,
`ICertificateLifecycle`, and `ITrustListFileAccess` interfaces it
implements).

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
| ApplicationAdmin privilege (Part 12 §7.2) | `GdsRole.ApplicationAdmin` + `GdsRoleBasedIdentity.AdministeredApplicationIds` |
| KeyCredential management (Part 12 §8) | `IKeyCredentialRequestStore` / `InMemoryKeyCredentialRequestStore` |
| Authorization services (Part 12 §9) | `AuthorizationServiceState` with `GetServiceDescription` / `RequestAccessToken` |
| CreateSelfSignedCertificate (Part 12 §7.10.6) | `ConfigurationNodeManager.CreateSelfSignedCertificateAsync` |
| Audit event redaction | `AuditEvents.RedactedPrivateKeyPassword` / `RedactedPrivateKey` — secrets never appear in audit payloads |
| Push/Pull management (Part 12 §7.7) | `ICertificateLifecycle.UpdateApplicationCertificateAsync` |
| TrustListType (Part 12 §7.5) | `ITrustListFileAccess` |
| Expiry monitoring (Part 9 §5.8.17) | `CertificateLifecycleMonitor` → `CertificateExpiring` events |
| Rejected certificates | `ICertificateLifecycle.RejectCertificateAsync` (awaitable) |
| User X.509 tokens (Part 4 §5.6.3) | `ValidateAsync(..., TrustListIdentifier.Users)` |


### Migration: `CertificateIdentifier` is metadata-only

#### What changed

`CertificateIdentifier` used to play two roles:

1. **Pure metadata** describing *where* to find a certificate (`StoreType` / `StorePath` / `SubjectName` / `Thumbprint` / `CertificateType` / `ValidationOptions`).
2. **A cert wrapper** that owned a loaded `Certificate` and implemented `IDisposable`.

The dual role caused recurring lifecycle bugs (stale caches surviving rotations, identifier disposal racing the registry, `Thumbprint` setter throwing on cache replacement, etc.). The `x509` branch removes the second role:

* `Certificate` property and the cached `m_certificate` field — **removed**.
* `IDisposable` declaration, `Dispose()`, `DisposeCertificate()` — **removed**. `CertificateIdentifier` is no longer disposable.
* Constructors taking `Certificate` / `Certificate, options` / `byte[]` — **removed**.
* Instance methods `FindAsync`, `LoadPrivateKeyAsync` (instance), `LoadPrivateKeyExAsync`, `OpenStore` — **removed**.
* `RawData` is now backed by an explicit `byte[]` field (the setter still derives `SubjectName` / `Thumbprint` / `CertificateType` from the parsed raw bytes).
* `Equals` compares metadata only.
* `ICertificateRegistry.GetIssuersAsync` returns `IList<CertificateIssuerReference>` (a public sealed record carrying `Certificate` + `CertificateValidationOptions`) instead of `IList<CertificateIdentifier>`.

#### How to materialize a `Certificate` from a `CertificateIdentifier`

Use the new `CertificateIdentifierResolver` static helper:

```csharp
using Opc.Ua;
using Opc.Ua.Security.Certificates;

var id = new CertificateIdentifier
{
    StoreType = CertificateStoreType.Directory,
    StorePath = "%LocalApplicationData%/OPC/pki/own",
    SubjectName = "CN=My Application",
    Thumbprint = "9B7B...",
    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
};

// Resolve a public-key-only certificate from the registry / inline RawData / the store.
using Certificate publicKey = await CertificateIdentifierResolver.ResolveAsync(
    id,
    registry: certificateManager,
    needPrivateKey: false,
    applicationUri: configuration.ApplicationUri,
    telemetry,
    ct);

// Resolve a certificate with private key (PFX, prompts the password provider).
using Certificate privateKey = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
    id,
    passwordProvider: configuration.SecurityConfiguration.CertificatePasswordProvider,
    applicationUri: configuration.ApplicationUri,
    telemetry,
    ct);

// Open the underlying store directly (rare — prefer the manager / resolver helpers).
using ICertificateStore store = CertificateIdentifierResolver.OpenStore(id, telemetry);
```

The resolver always returns a caller-owned, `AddRef`'d `Certificate` (or `null`). The caller is responsible for disposing it.

#### Common before / after migration patterns

| Before (legacy) | After (resolver / manager) |
|---|---|
| `var id = new CertificateIdentifier(cert);` | `var id = new CertificateIdentifier { Thumbprint = cert.Thumbprint, SubjectName = cert.Subject, CertificateType = CertificateIdentifier.GetCertificateType(cert) };` (caller owns `cert`) |
| `var id = new CertificateIdentifier(rawDataBytes);` | `var id = new CertificateIdentifier { RawData = rawDataBytes };` (RawData setter derives the other fields) |
| `id.Certificate` read | `CertificateIdentifierResolver.ResolveAsync(id, ...)` or `using CertificateEntry? e = registry.AcquireApplicationCertificateByType(id.CertificateType); var cert = e?.Certificate;` (caller owns and disposes `e`) |
| `id.Certificate = cert;` write | Drop the assignment. The cert is owned by the manager registry (use `ICertificateLifecycle.UpdateApplicationCertificateAsync`) or by a local variable in the calling method. |
| `await id.FindAsync(true, applicationUri, ...)` | `await CertificateIdentifierResolver.LoadPrivateKeyAsync(id, passwordProvider, applicationUri, telemetry, ct)` |
| `await id.LoadPrivateKeyExAsync(passwordProvider, ...)` | `await CertificateIdentifierResolver.LoadPrivateKeyAsync(id, passwordProvider, applicationUri, telemetry, ct)` |
| `id.OpenStore(telemetry)` | `CertificateIdentifierResolver.OpenStore(id, telemetry)` |
| `id.DisposeCertificate(); id.Dispose();` | Drop. The identifier owns nothing disposable. Dispose certificates returned by the resolver instead. |
| `IList<CertificateIdentifier>` from `GetIssuersAsync`; `issuers[i].Certificate` | `IList<CertificateIssuerReference>`; `issuers[i].Certificate` (record's `Certificate` field, caller-owned) |

#### When to register an in-memory certificate with the manager

If you have a `Certificate` instance that wasn't loaded from a configured store (for example, a freshly generated cert or one returned by a GDS push), persist it to a store and let the manager pick it up:

```csharp
await newCertificate.AddToStoreAsync(
    id.StoreType,
    id.StorePath,
    passwordProvider?.GetPassword(id),
    telemetry,
    ct);

// Reload + register with the manager so endpoint descriptions etc.
// observe the rotation via CertificateChanges.
await ((ICertificateLifecycle)certificateManager).UpdateApplicationCertificateAsync(
    id.CertificateType,
    newCertificate,
    issuerChain: null,
    ct);
```
