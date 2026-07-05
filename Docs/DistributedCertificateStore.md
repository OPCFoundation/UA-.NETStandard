# Distributed certificate store (shared trust lists)

Extension beyond OPC 10000-4 §6.6.

OPC UA replicas in a `RedundantServerSet` must present the same PKI: a certificate trusted (or rejected) on one replica should be trusted (or rejected) on every replica, and CRLs must be shared. By default each replica keeps its trusted, issuer and rejected certificate lists in a local `DirectoryCertificateStore` (or Windows `X509Store`), so trust decisions can diverge across replicas.

`SharedKeyValueCertificateStore` stores the **trusted**, **issuer** and **rejected** certificate lists and their **CRLs** in the same shared `ISharedKeyValueStore` that backs the rest of the high-availability state (in-memory for a single process, or a Raft / CRDT / future Redis backend across a replica set — see [HighAvailability.md](HighAvailability.md)). It plugs in through the existing certificate store provider model, so any `CertificateStoreIdentifier` can point at the distributed store type.

## What is shared

- Trusted certificate list, issuer (CA) certificate list, rejected certificate list.
- The CRLs used for revocation checking.

The store holds **public certificates only** — `NoPrivateKeys` is `true` and `SupportsLoadPrivateKey` is `false`. Distributing the application instance certificate (with its private key) is intentionally out of scope for this feature; it requires private-key confidentiality and is a future capability.

## Security: integrity, fail-closed

Trust-list integrity is critical: an attacker with write access to the shared store must not be able to inject a trusted CA. Every record (certificate or CRL) is therefore written through an `IRecordProtector` and verified on read. A forged or tampered record fails the authenticity check and is **skipped** (fail-closed) — it is never returned to a validator.

- An external, network-reachable shared store **must** use an authenticating protector (for example the `AesCbcHmacRecordProtector` used elsewhere in the HA stack, keyed from a Kubernetes Secret / KMS). This is the same record-protection model used for mirrored sessions and subscriptions.
- An in-memory, single-process store may use the no-op `NullRecordProtector` (the default), which applies no protection and is safe only because the store never leaves the process.

## Live propagation

The certificate validator enumerates the trusted/issuer store **on each validation** (it caches the store instance, not the certificate list). Because `SharedKeyValueCertificateStore.EnumerateAsync` reads the current shared-store state on every call, a certificate trusted or rejected on one replica is observed by the other replicas' next validation automatically — no restart and no explicit refresh are required.

> On a network-backed shared store (Raft/Redis), reading on every validation costs a round-trip. A future enhancement can cache the enumerated list locally and use `ISharedKeyValueStore.WatchAsync` to invalidate the cache (and emit `CertificateManager` `TrustListUpdated` / `CrlUpdated` change events) when another replica changes the shared state, keeping the read path fast while remaining live.

## Usage

### Direct construction

```csharp
using Opc.Ua;
using Opc.Ua.Redundancy;

// The shared store and protector come from the HA infrastructure (or an in-memory store for a single process).
ISharedKeyValueStore sharedStore = /* Raft / CRDT / Redis / InMemorySharedKeyValueStore */;
IRecordProtector protector = /* AesCbcHmacRecordProtector for an external store, or NullRecordProtector */;

var store = new SharedKeyValueCertificateStore(sharedStore, protector, telemetry);
store.Open("kv:pki/trusted");           // the store path is the key namespace for this list
```

### Through the provider and CertificateManager

Register a `SharedKeyValueCertificateStoreProvider` so a store type of `SharedKeyValue` (or a store path using the `kv:` scheme) resolves to the distributed store. The built-in `Directory` and `X509Store` providers remain available.

```csharp
using Opc.Ua;
using Opc.Ua.Security.Certificates;

var provider = new SharedKeyValueCertificateStoreProvider(sharedStore, protector);

CertificateManager manager = CertificateManagerFactory.Create(
    securityConfiguration,
    telemetry,
    options => options.AddStoreProvider(provider));
```

Point the security configuration's trusted/issuer/rejected stores at the distributed backend by setting each `CertificateStoreIdentifier` to `StoreType = CertificateStoreType.SharedKeyValue` with a `kv:` store path, for example:

- Trusted peers: `kv:pki/trusted`
- Issuers: `kv:pki/issuer`
- Rejected: `kv:pki/rejected`

Every replica in the set uses the same shared store and the same paths, so they share one trust list, one issuer list and one rejected store.

## Dependency injection

`SharedKeyValueCertificateStoreProvider` is injectable: construct it from the DI-registered `ISharedKeyValueStore` and `IRecordProtector` and pass it to the certificate manager through `CertificateManagerOptions.AddStoreProvider` (or `StoreProviders`). The direct-construction path above is the fallback when dependency injection is not used.

## Notes and limitations

- Public certificates only; the application instance certificate (with private key) is not distributed by this feature.
- The rejected-list trim (`AddRejectedAsync`) keeps the newest N entries best-effort; it is advisory and not security-critical, so no linearizable compare-and-swap is used.
- The store is additive: existing `Directory` and `X509Store` stores are unaffected.

See also: [HighAvailability.md](HighAvailability.md), [CertificateManager.md](CertificateManager.md), [Certificates.md](Certificates.md).
