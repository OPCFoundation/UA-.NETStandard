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

#### TrustList-Change Effects on Channels, Sessions and Subscriptions (OPC UA Part 12 §7.10.9)

`ApplyChanges` also has to react to *TrustList* changes committed in the transaction, not just server-certificate rotation. Once the response has been delivered, `ConfigurationNodeManager` maps every committed TrustList to the certificate group it belongs to and applies the corresponding effect:

- **Application group TrustList (`opc.tcp`)** validates the *peer* (client application) certificate presented in `OpenSecureChannel` against the `Peers` store. If a committed change (typically a removal of a trusted certificate or issuer) means a currently-connected peer would no longer validate, that peer's `opc.tcp` SecureChannel is forced to renegotiate; peers whose certificate is still trusted — and peers on a group whose TrustList did not change — keep their channels, Sessions and Subscriptions.
- **HTTPS group TrustList** validates the certificates used by the request-scoped UA-HTTPS transport (`Https` store). Because UA-HTTPS has no long-lived, per-peer SecureChannel keyed by the client application certificate, and any client TLS certificate (mutual TLS) is re-validated at the next TLS handshake by the shared validator against the directory-backed trust stores, an HTTPS-group change forces **no** channel renegotiation rather than a meaningless connection teardown. Server-*certificate* rotation on HTTPS is a separate concern handled by `ITransportListenerCertificateRotation` (a Kestrel Stop/Start cycle).
- **User-token group TrustList** validates X.509 user identity tokens. Every active Session that authenticated with a certificate user identity is re-validated against the updated user TrustList; a Session whose user certificate is no longer trusted is closed together with its Subscriptions (§7.10.9). Anonymous / username / issued-token Sessions are never disturbed.

The effect fan-out runs after the same grace/flush boundary as certificate rotation and is awaitable through `IConfigurationNodeManager.DrainPendingApplyChangesAsync(CancellationToken)`.

Two seams make this behaviour injectable and testable:

1. **`ITransportListenerPeerCertificateRotation`** — an optional capability interface on `ITransportListener` (sibling to `ITransportListenerCertificateRotation`). Each implementing listener advertises the single TrustList scope it validates its peer certificates against through `PeerCertificateTrustListScope`, and a committed change is routed to it **only** when the change targets that scope — so an `opc.tcp` listener is never re-validated or closed against the HTTPS store, and vice versa. Both `opc.tcp` listeners — the raw-socket `TcpTransportListener` and the Kestrel-hosted `KestrelTcpTransportListener` — implement it with the `Peers` scope, re-validating each tracked channel's client certificate through a caller-supplied async predicate and force-closing only the channels that fail (listener socket stays bound). The UA-HTTPS listener deliberately does **not** implement this capability (see the HTTPS bullet above). Custom transports opt in the same way:

   ```csharp
   public sealed class MyTransportListener : ITransportListener, ITransportListenerPeerCertificateRotation
   {
       // The single scope this listener's peer certificates are validated
       // against; only a change to this scope reaches CloseChannelsForUntrustedPeersAsync.
       public TrustListIdentifier PeerCertificateTrustListScope => TrustListIdentifier.Peers;

       public async ValueTask<IReadOnlyList<string>> CloseChannelsForUntrustedPeersAsync(
           Func<Certificate, CancellationToken, ValueTask<bool>> isPeerTrustedAsync,
           CancellationToken ct = default)
       {
           // For every channel with a client certificate, await isPeerTrustedAsync(cert).
           // Close the channel when it returns false; keep the listener socket bound.
           // Return the global channel ids that were closed (for diagnostics).
           return [];
       }
   }
   ```

2. **`IPushConfigurationTrustListEffectHandler`** — the injectable provider that performs the two effects above. `ConfigurationNodeManager` builds a `PushConfigurationTrustListEffectContext` (the committed effects plus the live transport listeners, session manager, certificate validator and a session-close delegate) and dispatches it to the handler after the grace boundary. A default `PushConfigurationTrustListEffectHandler` is created automatically; hosts can register their own through DI (`services.TryAddSingleton<IPushConfigurationTrustListEffectHandler, ...>()`) or pass one to `MainNodeManagerFactory` / `ConfigurationNodeManager` directly.

#### PushManagement Transactions (OPC UA Part 12 §7.10.2-§7.10.11)

`ConfigurationNodeManager` implements the full PushManagement transaction model: every `UpdateCertificate`, `CreateSelfSignedCertificate`, `DeleteCertificate` and TrustList (`AddCertificate` / `RemoveCertificate` / `Open`+`CloseAndUpdate`) call made within a Session is **staged**, not applied. Nothing takes effect until `ApplyChanges` is called; `CancelChanges` discards the staged work instead. This matches §7.10.2's *Transaction Lifecycle*: a transaction is created automatically on the first staging call, is owned exclusively by the Session that created it (every other Session's staging calls fail with `Bad_TransactionPending`), and is torn down by `ApplyChanges`, `CancelChanges`, or the owning Session closing.

Because the stack always implements the transaction model, every staging Method always reports `ApplyChangesRequired = TRUE` (§7.10.2) — there is no "immediate apply" mode when going through `ConfigurationNodeManager`. The following standard nodes are exposed under `ServerConfiguration` to support the model:

| Node | Kind | Purpose |
| --- | --- | --- |
| `ServerConfiguration.SupportsTransactions` | Variable | Always `true`; advertises transaction-model support to Clients (§7.10.4). |
| `ServerConfiguration.DeleteCertificate` | Method | Deletes the Certificate occupying a CertificateGroup/CertificateType slot (§7.10.7). Staged like `UpdateCertificate`; the endpoint-reference safety check runs when `ApplyChanges` is called (see *Method security and validation* below). |
| `ServerConfiguration.CancelChanges` | Method | Discards the calling Session's staged changes (§7.10.11). Returns `Bad_NothingToDo` if the Session has no active transaction. |
| `ServerConfiguration.TransactionDiagnostics` | Object | Exposes the outcome of the last `ApplyChanges` (start/end time, per-operation results) for troubleshooting (§7.10.3). Its Variables follow the §7.10.17 DataValue-status rules: every Variable reads `Bad_OutOfService` before any transaction has started, `Result` reads `Bad_InvalidState` while a transaction is active (with `EndTime` at `DateTime.MinValue`), and once the transaction completes `Result` is `Good` and carries the `ApplyChanges` outcome (or `Bad_RequestCancelledByClient` after `CancelChanges`). |

**Client call flow** — a full Update-Certificate-then-apply round trip, now available through `IServerPushConfigurationClient`:

```csharp
await using var pushClient = new ServerPushConfigurationClient(configuration, telemetry);
await pushClient.ConnectAsync(endpointUrl);

// Stage the new certificate; the server always reports true here.
bool applyChangesRequired = await pushClient.UpdateCertificateAsync(
    certificateGroupId: default, // DefaultApplicationGroup
    certificateTypeId,
    newCertificate.RawData.ToByteString(),
    privateKeyFormat: null,      // reuse the existing/pending private key
    privateKey: default,
    issuerCertificates);

// Nothing above is visible to the server yet. Commit the transaction:
await pushClient.ApplyChangesAsync();

// Or, to abandon everything staged so far instead:
// await pushClient.CancelChangesAsync();

// Removing a certificate slot entirely (§7.10.7) participates in the
// same transaction and also requires ApplyChanges:
await pushClient.DeleteCertificateAsync(certificateGroupId: default, otherCertificateTypeId);
await pushClient.ApplyChangesAsync();
```

TrustList changes made with `AddCertificateAsync` / `RemoveCertificateAsync` / `UpdateTrustListAsync` share the *same* per-Session transaction as the CertificateGroup Methods above — a single `ApplyChanges` commits (or a single `CancelChanges` discards) both kinds of staged work together, and a `TrustList.Open` for writing is itself blocked with `Bad_TransactionPending` while another Session's transaction is active.

**DI and replacement points.** Several collaborators drive the transaction model. `IPendingCertificateKeyStore`, `IPushCertificateKeyGenerator` and `IPushConfigurationTrustListEffectHandler` are registered as `TryAddSingleton` defaults by `AddServer(...)`/`OpcUaServerBuilderExtensions`, so applications can override any of them by registering their own implementation *before* calling `AddServer`. The `IPushConfigurationTransactionCoordinator` is deliberately **not** registered as a shared default: it holds mutable per-server transaction state, so each server's `ConfigurationNodeManager` owns its own instance and two servers built from one container never share (and corrupt) each other's transactions. A host that needs to observe or replace the coordinator can still register a custom `IPushConfigurationTransactionCoordinator` before `AddServer`, and it is honored as-is.

- `IPushConfigurationTransactionCoordinator` (default: a private, per-server `PushConfigurationTransactionCoordinator` owned by `ConfigurationNodeManager`) owns transaction ownership, staged-operation bookkeeping, commit/rollback ordering and `TransactionDiagnostics`.
- `IPendingCertificateKeyStore` (default: `DirectoryPendingCertificateKeyStore`) persists the private key regenerated by `CreateSigningRequest(regeneratePrivateKey: true)` until a matching `UpdateCertificate` consumes it (§7.10.10), scoped to its own sub-folder per (CertificateGroup, CertificateType) and protected with the configured `ICertificatePasswordProvider`. Tests can swap in the volatile, in-memory `InMemoryPendingCertificateKeyStore` instead.
- `IPushCertificateKeyGenerator` (default: `AdditionalEntropyCertificateKeyGenerator`) generates the regenerated signing-request key pair, **genuinely mixing the caller-supplied §7.10.10 `Nonce` into the private key** (see *Method security and validation* below).

Both are also constructor parameters on `ConfigurationNodeManager`/`MainNodeManagerFactory` for applications that construct the server directly instead of through DI; omitting them lets `ConfigurationNodeManager` create its own private defaults.

**High-availability (distributed) transactions.** By default the PushManagement transaction is *per-server*: each replica of a `RedundantServerSet` would have its own independent transaction, so two administrators talking to two replicas could stage conflicting changes at once. Opting in with `UseDistributedPushConfiguration()` (from `Opc.Ua.Redundancy.Server`) replaces the two collaborators above with shared-store-backed variants so the transaction is single **across the whole replica set**:

- `DistributedPushConfigurationTransactionCoordinator` wraps the per-server coordinator and guards it with a compare-and-swap lease in the shared `ISharedKeyValueStore` (the same lease pattern as `SharedStoreLeaseElection`). A replica may only start a transaction while it holds the lease, so at most one replica has an active transaction at a time; a replica that stops renewing (for example, because it crashed) loses the lease once it expires and a standby can take over. The lease is acquired at an `await` boundary immediately before the synchronous `Stage`, so the coordinator's synchronous `Stage`/`CancelChanges` contract is preserved with no sync-over-async. It is released as soon as the transaction is applied, cancelled, reset, or its owning Session closes.
- `SharedKeyValuePendingCertificateKeyStore` persists the regenerated `CreateSigningRequest` private key in the shared store instead of a local directory, so a `CreateSigningRequest` on one replica and the matching `UpdateCertificate` on another still complete. It is scoped per (CertificateGroup, CertificateType), consumes the key atomically (only one replica wins a concurrent `TryTake`), wipes every private-key working buffer, and — like the other distributed stores — protects each record with an `IRecordProtector` and fails closed if an external store is configured without one.

This composes with the configured leader election. Setting `DistributedPushConfigurationOptions.RequireLeadership = true` additionally restricts transaction ownership to the elected leader; when the Kubernetes extension is configured (`UseKubernetesLeaderElection`), that leader is decided by the Kubernetes-native `Lease` already registered as the `ILeaderElection` service, reused without creating a second Kubernetes client. Distributed behaviour is opt-in — a server that does not call `UseDistributedPushConfiguration()` keeps the per-server coordinator and directory-backed pending-key store unchanged. See [High Availability](HighAvailability.md#distributed-pushmanagement-transactions-beyond-66-opt-in) for the full setup.

**Method security and validation.** The `ServerConfiguration` Methods enforce the per-Method SecureChannel and Role requirements from the §7.10 Method result tables. The **channel-security** requirement is checked first and reported separately from the **Role** requirement:

- `UpdateCertificate` (§7.10.5) and `CreateSigningRequest` (§7.10.10) transfer private-key material and require an **encrypted** SecureChannel (`SignAndEncrypt`); a weaker channel is rejected with `Bad_SecurityModeInsufficient`.
- `CreateSelfSignedCertificate` (§7.10.6), `DeleteCertificate` (§7.10.7), `GetCertificates`, `GetRejectedList`, `ApplyChanges` (§7.10.9) and `CancelChanges` do not transfer a private key and accept any **authenticated** SecureChannel (`Sign` or `SignAndEncrypt`); an unauthenticated (`None`) channel is rejected with `Bad_SecurityModeInsufficient`.
- Once the channel requirement is met, a caller lacking the `SecurityAdmin` Role is rejected with `Bad_UserAccessDenied` (never conflated with the channel failure).

`CreateSigningRequest(regeneratePrivateKey: true)` additionally requires the caller to supply at least **32 bytes** of additional entropy in the `Nonce` argument (§7.10.10); a shorter or missing `Nonce` is rejected with `Bad_InvalidArgument` and leaves all state unchanged. The default `AdditionalEntropyCertificateKeyGenerator` genuinely incorporates that entropy: it instantiates a NIST SP 800-90A HMAC-DRBG from a fresh server-side cryptographic seed concatenated with the caller `Nonce`, and derives the RSA primes (via managed `BigInteger` prime generation, on every target framework) or the EC private scalar from that DRBG. Because the server seed is always present, a weak or adversarial `Nonce` can never weaken the key; a strong `Nonce` genuinely adds entropy. On .NET Framework and `netstandard2.1` the platform cannot import a private-only EC scalar, so genuine additional-entropy incorporation into an ECC key is unavailable there: an ECC `regeneratePrivateKey: true` request is rejected with `Bad_NotSupported` rather than silently generating a key that ignores the mandated `Nonce` (use an RSA `CertificateType`, or run the server on .NET 8 or later, to regenerate an ECC key). RSA keys remain fully nonce-derived on all frameworks.

`DeleteCertificate`'s endpoint-reference safety check (§7.10.7: "Certificates that are referenced by EndpointDescriptions shall not be deleted. This determination happens when ApplyChanges is called.") resolves the exact certificate each active `EndpointDescription` presents **from the active certificate registry** — keyed by the endpoint's `SecurityPolicyUri`, exactly as the channel handshake resolves the presented certificate — and rejects the transaction with `Bad_InvalidState` at `ApplyChanges` if the deleted certificate is still referenced. Resolving from the live registry (rather than the `EndpointDescription.ServerCertificate` blob captured when the endpoints were created) ensures a certificate that was rotated after startup is still protected. A delete that is superseded within the same transaction by a `CreateSelfSignedCertificate`/`UpdateCertificate` for the same slot coalesces to the later operation (§7.10.2 ordered-queue semantics), so replacing a referenced certificate in one transaction remains allowed. A conservative net "last remaining certificate" check still runs at staging time for immediate feedback.

#### Certificate-Expiration and TrustList-Staleness Alarms (OPC UA Part 12 §7.8.3)

Each server `CertificateGroup` exposes the two optional standard alarm
instances defined by OPC 10000-12 §7.8.3:

- **`CertificateExpired`** (`CertificateExpirationAlarmType`) becomes **active**
  when the group's soonest-expiring application certificate is within its
  `ExpirationLimit` (default two weeks) of `NotAfter`, or has already expired.
  Severity is *Medium* while approaching expiry and *High* once expired.
- **`TrustListOutOfDate`** (`TrustListOutOfDateAlarmType`) becomes **active**
  when the group's TrustList has not been updated within its `UpdateFrequency`.
  A non-positive `UpdateFrequency` (the default) disables the staleness check.

`ConfigurationNodeManager` creates and wires these alarms during
`CreateAddressSpace` and starts periodic monitoring automatically once the
server is fully running (`StandardServer.OnServerStarted`), so transition
events are never emitted before the subscription infrastructure is ready.
Monitoring is stopped and drained on shutdown. All thresholds, timestamps and
the evaluation timer flow through the injected `TimeProvider`.

The alarms follow the standard OPC 10000-9 condition lifecycle: on activation
they set `ActiveState`, raise `Retain`, clear `AckedState` and report the
alarm event; a stable state never re-emits an event; the wired `Acknowledge`
method (and `SetAcknowledgedState`) clears `Retain` once the alarm is both
inactive and acknowledged. Alarm values are re-evaluated after every committed
`UpdateCertificate`/`TrustList` change, so replacing an expiring certificate
clears the alarm without waiting for the next tick.

#### Optional ServerConfiguration Surface (OPC UA Part 12 §7.10.3, §7.10.13, §7.10.20)

Beyond the mandatory Push Certificate Management Methods, the
`ServerConfigurationType` (§7.10.3) defines several **Optional** members.
`ConfigurationNodeManager` exposes each of them, but — per the specification's
"expose only when configured/known" rule — only materialises the optional
address-space node when it is actually configured. When a member is not
configured, its node is suppressed entirely (it does not appear in the address
space), and every other feature keeps working unchanged.

| Member | Kind | Exposed when | Backed by |
| --- | --- | --- | --- |
| `ApplicationUri`, `ProductUri`, `ApplicationType`, `ApplicationNames` | Variables | **Always** | The `ApplicationConfiguration` (identity is always known) |
| `HasSecureElement` | Variable | `ServerConfigurationOptions.HasSecureElement` is set | The configured `bool?` value |
| `InApplicationSetup` | Variable | `ServerConfigurationOptions.InApplicationSetup` is set | The configured `bool?` value |
| `ResetToServerDefaults` | Method | An `IServerConfigurationResetProvider` is configured | The provider (§7.10.13) |
| `ConfigurationFile` | Object | An `IApplicationConfigurationFileProvider` is configured | The provider (§7.10.20) |

Everything is configured through a single `ServerConfigurationOptions` object,
either fluently, via DI, or as a constructor argument on
`ConfigurationNodeManager`/`MainNodeManagerFactory` for direct construction.

```csharp
// Fluent (recommended)
builder
    .ConfigureServerConfiguration(o =>
    {
        o.HasSecureElement = true;                       // exposes HasSecureElement = true
        o.InApplicationSetup = false;                    // exposes InApplicationSetup = false
        o.ResetShutdownDelay = TimeSpan.FromSeconds(10); // SecondsTillShutdown advertised on reset
    })
    .WithServerConfigurationReset<MyResetProvider>()      // exposes ResetToServerDefaults
    .WithApplicationConfigurationFile<MyConfigFileProvider>(); // exposes ConfigurationFile
```

The reset and configuration-file providers may also be registered as plain DI
services (`services.AddSingleton<IServerConfigurationResetProvider, ...>()`);
`DependencyInjectionStandardServer` resolves them and merges them into the
options. A DI-registered provider takes precedence over one set on the options.

**`ResetToServerDefaults` (§7.10.13).** Resets the application security
configuration to its vendor-specific default state. `ConfigurationNodeManager`
owns the standard concerns and only delegates the actual reset to the injected
`IServerConfigurationResetProvider`:

- Requires an **authenticated** SecureChannel (`Sign`/`SignAndEncrypt`, else
  `Bad_SecurityModeInsufficient`) and the `SecurityAdmin` Role (else
  `Bad_UserAccessDenied`).
- Is rejected with `Bad_TransactionPending` while another Session owns an active
  PushManagement transaction.
- **Returns its response first**, then advertises the pending shutdown
  (`ServerState` = `Shutdown`, `ShutdownReason`, `SecondsTillShutdown` =
  `ResetShutdownDelay`) so the Client can receive the response and learn its
  credentials may no longer work, waits the grace period, and only then invokes
  the provider. The deferred reset honors the server shutdown token, so a server
  that stops while the reset is pending abandons it cleanly.

```csharp
public sealed class MyResetProvider : IServerConfigurationResetProvider
{
    public async ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken)
    {
        // Restore the vendor default configuration; the server then restarts.
        await RestoreDefaultConfigurationAsync(cancellationToken);
    }
}
```

**`ConfigurationFile` (§7.10.20).** An `ApplicationConfigurationFileType` (a
`ConfigurationFileType`, §7.8.5) whose read/update flow is backed by an
`IApplicationConfigurationFileProvider`. `ConfigurationNodeManager` wires the
whole `FileType` surface (`Open`, `Read`, `Write`, `Close`, `SetPosition`,
`GetPosition`, `CloseAndUpdate`, `ConfirmUpdate`) and owns:

- **Stream lifecycle & single-writer** semantics (last open wins; an abandoned
  Session's open handle is released on Session close and after the
  `ActivityTimeout` elapses).
- **Security**: read requires SecurityAdmin over an encrypted channel; the
  update Methods require SecurityAdmin over an authenticated channel.
- **Transaction exclusion**: opening for writing is rejected with
  `Bad_TransactionPending` while another Session's transaction is active, and
  while the file is open for writing new `ApplyChanges` are blocked
  (§7.10.2/§7.10.20).
- **`CloseAndUpdate`** checks `VersionToUpdate` against the provider's
  `CurrentVersion` (`Bad_InvalidState` on mismatch), then **validates before
  applying** and applies **atomically** — an invalid configuration or a failed
  apply leaves the active configuration unchanged (no partial update). When the
  provider reports `RequiresConfirmation`, a non-empty `UpdateId` is returned and
  the change is reverted if `ConfirmUpdate` does not arrive within the
  Client-supplied revert window.

```csharp
public sealed class MyConfigFileProvider : IApplicationConfigurationFileProvider
{
    public uint CurrentVersion { get; private set; } = 1;
    public DateTime LastUpdateTime { get; private set; } = DateTime.UtcNow;
    public bool RequiresConfirmation => false;

    public ValueTask<ByteString> ReadConfigurationAsync(CancellationToken ct = default)
        => new(SerializeCurrentConfiguration());

    public ValueTask ValidateConfigurationAsync(ByteString config, CancellationToken ct = default)
        => ValidateOrThrowAsync(config); // throw ServiceResultException on invalid

    public ValueTask ApplyConfigurationAsync(ByteString config, CancellationToken ct = default)
        => ApplyAtomicallyAsync(config); // all-or-nothing; bump CurrentVersion/LastUpdateTime

    public ValueTask ConfirmUpdateAsync(CancellationToken ct = default) => default;
    public ValueTask RevertUpdateAsync(CancellationToken ct = default) => default;
}
```

The file content exchanged through the provider is the §7.8.5.1 `FileType`
stream body (a serialized `UABinaryFileDataType` whose `SupportedDataType` is
`ApplicationConfigurationDataType`) and is treated opaquely by the node manager,
so the concrete encoding is entirely the provider's responsibility.

#### TrustList Size Limits (OPC UA Part 12 §8.4.5)

`ServerConfiguration.MaxTrustListSize` advertises, in bytes, the largest
TrustList a Client may write (`0` = unlimited). Enforcing a truly unbounded
write would expose the server to unbounded allocation, so the server bounds
actual enforcement by a **resource-protection safety ceiling** and — crucially —
advertises the *effective* limit it actually enforces rather than a `0` that
hides a cap.

The effective, actually-enforced limit is derived from the advertised
`MaxTrustListSize` and `ServerConfigurationOptions.MaxTrustListSizeSafetyCeiling`
(default 1&nbsp;MiB):

| Advertised `MaxTrustListSize` | Effective (enforced **and** advertised) limit |
| --- | --- |
| `0` (unlimited) | the safety ceiling |
| above the safety ceiling | the safety ceiling |
| finite, at or below the ceiling | the configured `MaxTrustListSize` |

The effective limit is enforced consistently — before allocation — on
`TrustList.Read`/`Write` (cumulatively across chunks, overflow-safe),
`CloseAndUpdate` (the staged payload is decoded under the effective bound), and
the direct `AddCertificate` path (an oversized certificate is rejected with
`Bad_EncodingLimitsExceeded`).

```csharp
builder.ConfigureServerConfiguration(o =>
{
    // MaxTrustListSize = 0 (unlimited) in the ApplicationConfiguration is
    // honestly advertised and enforced as this ceiling; raise it to accept
    // larger TrustLists (for example many large CRLs).
    o.MaxTrustListSizeSafetyCeiling = 8 * 1024 * 1024; // 8 MiB
});
```

> The legacy `TrustList` constructor overloads that take only a single
> `maxTrustListSize` (no explicit ceiling) are unchanged and fully backward
> compatible: a finite size is honored exactly (never clamped) and `0` falls
> back to the 1&nbsp;MiB default. The overload that also takes a
> `maxTrustListSizeSafetyCeiling` opts into the clamping semantics above.

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
`src/Opc.Ua.Client/Session/ManagedSession.CertificateChanges.cs`.
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

> **Note:** `ITrustListTransaction`/`BeginUpdateAsync` above is a local, in-process API on `CertificateManager` for application code that wants an atomic trust-list edit. It is unrelated to the OPC UA PushManagement transaction model (`ApplyChanges`/`CancelChanges`, see *PushManagement Transactions* above), which is driven remotely by a Client over the `ServerConfiguration` address space and governs `TrustList`/`ServerConfiguration` Methods called through `ConfigurationNodeManager`.

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

The authoritative, per-requirement Part 12 support status — every implemented ServerConfiguration / PushManagement, TrustList, certificate-alarm, KeyCredentialService and AuthorizationService requirement linked to its source **and** its automated tests, with complete / partial / optional / unsupported marks — lives in the [GDS Conformance Matrix](GDS.md#conformance-matrix). That matrix also identifies the applicable OPC UA Facets / conformance units and states explicitly that the formal UACTT/CTT (a licensed GUI tool) is not run automatically here.


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
