# Bounded synchronization over `IXRegistryEndpoint`

This bridge-local policy layer uses existing endpoints. It adds no transport,
event feed, distributed transaction, or exactly-once guarantee. One writer owns
each state directory. The host schedules `RunOnceAsync` at its configured polling
interval; native events may prompt another pass but are not required.

```csharp
using Opc.Ua.XRegistry.Bridge.Sync;

var options = new XRegistrySyncOptions(
    "production-registry-pair",
    configuredOpcUaEndpointAndRegistryRoot,
    configuredHttpRegistryRoot)
{
    OpcUaContext = opcUaOperatorContext,
    HttpContext = httpOperatorContext,
    ConflictPolicy = XRegistrySyncConflictPolicy.Manual,
    PropagateDeletes = true
};

var store = new FileXRegistrySyncStateStore(LocalFileSystem.Instance, privateStateDirectory);
await using var storeLifetime = store.ConfigureAwait(false);
var synchronizer = new XRegistrySynchronizer(opcUaEndpoint, httpEndpoint, store, options, telemetry, timeProvider);
XRegistrySyncReport report = await synchronizer.RunOnceAsync(dryRun: false, cancellationToken);
```

`AddXRegistrySynchronization(options, opcUaEndpoint, httpEndpoint, store)` registers
the engine and offline `XRegistrySyncStateManager` with `IServiceCollection`.
Register `ITelemetryContext` and optionally `TimeProvider` first. Supplied endpoint
and store instances remain caller-owned. Direct construction remains available.

## Supported operations and guards

Full inventories traverse every model-defined group/resource collection, Resource
Meta, and exact Version, including metadata-only resources and opaque/empty binary
documents. Collection membership is checked again after enumeration. Root identity,
effective model, and mutation guarantees are rechecked. Per-call time, pages,
entities, document bytes, inventory bytes, operations, JSON depth, and state bytes
are bounded. An incomplete scan never authorizes an absence-based creation or delete.

Fingerprints include exact collection-aware paths, meaningful metadata, and document
bytes. Object property ordering and equivalent JSON number spellings are canonical.
Top-level generated epochs, times, links, counts, default projections, and correlation
are excluded; identically named fields in user-defined nested metadata are retained.
Each side keeps its own exact unsigned epoch, including zero and values beyond UInt32.
Epochs are guards at their originating endpoint, never cross-registry ordering.

Supported writes require measured atomic and conditional mutation guarantees:

| Entity | Behavior |
| --- | --- |
| Registry/group metadata | Conditional replacement; deployment identity/model/capabilities are not copied. |
| New group | Atomic nested merge guarded by the destination root epoch and confirmed absence. |
| New resource | Atomic nested merge guarded by the destination group epoch; explicit Version map and default metadata. |
| Exact Version | Conditional metadata/document replacement. New explicit Versions use Resource POST with a Resource Meta guard, preserving the destination's observed default policy. |
| Resource Meta/default | Conditional replacement using Meta epoch, after Version operations. The endpoint validates the selected Version atomically. |
| Empty group deletion | Enabled by default; requires baseline, complete inventories, current absence, confirmed empty child collections, and the destination's original epoch. |
| Resource, nonempty group, or exact Version deletion | Requires `IXRegistryPreparedEndpoint` and `SupportsPreparedMutations`. The bridge stages the deletion, rechecks the affected subtree (including Resource Meta/default and sibling Versions), then commits with global-generation invalidation. Changed or incomplete observations abort without mutation. |

This deliberately narrow profile **does not** synchronize model/configuration changes,
external resource/document references, read-only/immutable domain attributes,
server-assigned Version identities, automatic retention/ordering, or ancestry changes
to existing Versions. These produce explicit records/conflicts, not approximations.
Resource/nonempty subtree deletion and exact-Version deletion are held when the
destination cannot prepare an operation with global mutation invalidation. An
ordinary HTTP backend does not offer that guarantee; a prepared native endpoint
can offer it through the experimental extension. Local validation aborts are not
recorded as if the server had returned a mutation result. `PropagateDeletes = false` retains live baselines
without propagating absence; it is not an unguarded-delete mode.

Pagination accepts bounded same-collection registry-relative `next` links with
`cursor`, `page`, `limit`, `pagesize`, or `offset`. Cycles, duplicate entries,
cross-scope links, filtered pages, and unqualified pagination flags make a scan
incomplete. There is no synthetic watch endpoint or event replay cursor.

## Recovery and conflict administration

Equal initial live states establish baselines without writes. One-sided initial
data is copied conditionally. Divergent initial states or changes on both sides
use `Manual` (default), `PreferOpcUa`, or `PreferHttp`. Other independent entries
continue. Preference decisions retain both observations in durable conflict history.

Every outgoing mutation has a persisted intent before dispatch. Returned outcomes
are persisted before readback and baseline publication. No pending mutation is
resent, including PUT. An optional operation journal is consulted only for a
previously advertised replay identity. Ordinary HTTP requests receive no invented
operation ID or correlation flag.

For an unknown update/delete outcome, guarded readback may prove present convergence
or absence, without inventing a server response or claiming attribution. Unknown
creation/assigned-Version outcomes remain pending even when an entity currently
looks equal. Newer edits after a known commit become conflicts without replacing the
old baseline. Pending intents, outcomes, conflicts, and tombstones have no TTL.
Quota exhaustion stops the job instead of discarding evidence.

```csharp
var manager = new XRegistrySyncStateManager(store, options.JobId, timeProvider);
ArrayOf<XRegistrySyncConflict> conflicts = await manager.ListConflictsAsync(cancellationToken);
await manager.ResolveConflictAsync(conflictId, XRegistrySyncConflictPolicy.PreferHttp, cancellationToken);
```

These commands have no endpoint dependency. Resolution records a request against the
saved fingerprints and **each side's own epoch**. The next pass revalidates those
exact observations; a newer edit invalidates the decision rather than being forced.
A preference cannot force an ambiguous pending creation to be repeated.
An unknown conflict is rejected through a read-only preflight before acquiring
writer ownership, so a failed resolution does not initialize pristine storage.

## State and status

File snapshots use the existing `IFileSystem`, qualified writer ownership,
file flush-to-disk and directory barriers, staged atomic `Replace`, a persistent
initialization marker, and an interrupted-commit marker. Recovery artifacts,
missing initialized state, corrupt/unsupported schema, checksum failure, storage
quota, or uncertain publication fail closed. They are not automatically deleted,
repaired, or interpreted as a fresh job. Provision private filesystem permissions;
the provider does not change ACLs or qualify network/distributed filesystems.
Custom filesystems must explicitly supply `IXRegistrySyncFileDurability`.
Default UNC/device paths also require explicit qualification.

`MemoryXRegistrySyncStateStore` is process-local and makes no restart durability claim.
State binds the job, both configured endpoint/root identities, caller subject,
authority/authentication/roles, both registry IDs, and effective model. Reusing state
for another scope is rejected. Session reconnect IDs are intentionally not scope.

Read-only sessions, conflict listing, and `RunOnceAsync(dryRun: true)` never create or
modify persistent artifacts or endpoints. Dry run returns planned work and may read
endpoints. It does not recover or resolve durable intents.

`XRegistrySyncReport` contains ordered records, observed/converged/applied/deleted/
planned counts, active conflicts, pending operations, failures, and scan completeness.
Exit codes are `0` succeeded, `1` conflicts/pending, `2` incomplete or bounded work
remaining, and `3` failed state. `ReadStatusAsync` reports durable generation,
baseline/intent/outcome/verified/pending/conflict/tombstone counts without upstream
contact. Readback-only recovery increments verified state, not the server-outcome
count.
