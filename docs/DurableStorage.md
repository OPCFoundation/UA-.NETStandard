# Local durable shared storage

`FileSharedKeyValueStore` implements the existing `ISharedKeyValueStore`
interface with protected local persistence. It also implements
`ISharedKeyValueStoreConsistency`, `ISharedKeyValueStoreDurability` and
`ISharedKeyValueStoreCapacity`.
Linearizable access by itself does not establish durable storage; callers that
need durable commits must require the durability interface and verify readiness.

This is a single-live-owner reference backend, not a distributed coordination
store. It reports process-local visibility even though its data survives
reopening. It does not provide replication, failover, stale-backup detection,
business exactly-once effects or a clock that measures time across host restart.

## Construction and startup

The application supplies an `IRecordProtector` backed by keys managed through
its [secret-store infrastructure](CertificateManager.md). The store borrows that
protector; it does not create or persist a key or dispose a borrowed protector.
The logical store identifier is authenticated with every file and must remain
stable across restart. A different identifier or unavailable protection key is
an error, not an empty store.

```csharp
await using var store = new FileSharedKeyValueStore(
    directory,
    logicalStoreId,
    protector,
    maximumSnapshotBytes: 16 * 1024 * 1024);

await store.VerifyDurabilityAsync(cancellationToken);

bool inserted = await store.CompareAndSwapAsync(
    "application/example",
    expected: default,
    value: ByteString.From(1, 2, 3),
    ct: cancellationToken);
```

The parent directory must already exist. Use a dedicated store directory, not a
filesystem root or a directory containing unrelated application files.
Initialization requires filesystem support for the actual native directory
durability barrier.

Dependency injection is available through the normal OPC UA builder:

```csharp
services.AddSingleton<IRecordProtector>(protector);
services.AddOpcUa().AddFileSharedKeyValueStore(
    directory,
    logicalStoreId,
    maximumSnapshotBytes: 16 * 1024 * 1024);

// During asynchronous startup:
await provider.GetRequiredService<ISharedKeyValueStoreDurability>()
    .VerifyDurabilityAsync(cancellationToken);
```

Registration alone does not open the store or enable a protocol feature.
The same instance is resolved through the shared-store, consistency, durability
and bounded-write interfaces.

## Commit and recovery behavior

A snapshot contains its format version, store identity, revision and complete
key/value map. The complete file is authenticated and encrypted. Keys use exact
ordinal comparison; values use `ByteString`.

Each mutation writes and flushes a new staged snapshot before atomically
replacing the primary file and synchronizing the directory. In-memory state and
watch notifications are published only after that sequence completes.
Compare-and-swap retains the existing shared-store semantics: a null expected
value requires absence, and a null replacement deletes the matched key.
`SetAsync` can store a present null ByteString separately from an absent key.

The directory's ownership marker, protected identity and primary snapshot are
checked during recovery. A missing identity or snapshot, changed committed
content, failed authentication or unexpected directory content causes an error.
Missing both committed files while an ownership marker remains is not treated
as a new empty store. Abandoned stage files are removed only after valid
committed state has been recovered; they are never promoted as an alternative
result.

A failed publication may have reached its atomic switch. The affected instance
therefore stops serving successful reads or readiness confirmations until it is
disposed and reopened. Reopening observes the actual valid committed generation.
It does not prove that a failed caller received a result, and it never authorizes
repeating a business operation.

The contract assumes intact committed storage. Complete external destruction
of the directory, restoration of an older backup or a changed logical Server
requires application/operator reconciliation, not automatic recovery claims.

## Bounds and lifetime

`MaximumSnapshotBytes` limits one complete protected snapshot, including
metadata, keys and encryption overhead. It is not a total process-memory or
filesystem-volume limit. The owner must budget current data, transient encoding
buffers, staged publication and in-flight readers in its existing resource
accounting. Capacity failure before publication does not evict committed data.

`ISharedKeyValueStoreCapacity.TryCompareAndSwapAsync` reports `Applied`,
`ConditionNotMet` or `CapacityRejected`. The last two guarantee no mutation.
A capacity rejection leaves the store healthy and existing reads, shrinking
updates and deletions available. Other exceptions remain potentially committed
failures. The original `CompareAndSwapAsync` interface retains its existing
exception behavior.

RSE reserves a bounded future protected record slot in its admission value,
then shrinks that value to terminal history at release. This uses the store's
existing snapshot bound; it does not introduce a separate storage quota.
Budget protected record slots, store metadata and protection overhead when
selecting `MaximumSnapshotBytes`.

`ScanAsync` returns a stable prefix snapshot. Watchers observe committed changes
from the live instance and have bounded queues. A lagging watcher fails
explicitly and must obtain a new snapshot; changes are not silently dropped.
Disposal closes watchers, cancels pending local work and releases the directory
lease.

For [RSE](AsyncServices.md), this store is only one prerequisite. Durable mode
also needs consistent authenticated ownership, qualified restart-safe timing,
crash-consistent operation transitions and conservative handling of surviving
execution. File readback or a process-restart demonstration is not proof of a
physical power-loss guarantee or the complete Durable profile.
