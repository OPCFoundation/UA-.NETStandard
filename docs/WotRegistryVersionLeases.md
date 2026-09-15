# WoT registry Version leases

The stock `WotRegistryService` implements the optional
`IWotRegistryVersionLeaseProvider` capability. A lease protects an exact Version
incarnation from automatic retention eviction, independently of active, default
and desired selection. It does not create a generic registrar activation,
retention or metadata-reload contract.

## Retention and lifetime

At a committed-Version limit of two, suppose v1 is open and active/default/desired
all point to v2. The owner must retain v1 because of its lease, not because of a
selection flag. It rejects an allocation whose eventual commit could not retain
v1, v2 and the incoming Version. A previously allocated pending Version also
cannot commit by evicting v1. After the last relevant lease releases, v1 becomes
eligible for the existing retention policy.

Acquisition is serialized with allocation, retention and publication by the same
registry owner operation. A request cannot acquire from an old published snapshot
while that owner is already committing its eviction. Leases are reference-counted
by the existing internal incarnation, not by ResourceId, VersionId or content
digest alone. Benign metadata copies preserve the incarnation; delete/recreate
does not. The incarnation remains internal.

The same owner's `InitializeAsync` also preserves surviving incarnations when it
rehydrates canonical state, including required recovery after an indeterminate
commit. A validated committed snapshot reported with uncertain durability is
reconciled before publication. Leases acquired before either operation remain
effective, and an older owner-issued snapshot can still acquire the same surviving
incarnation. This applies even when every selection pointer has moved elsewhere.

Reconciliation uses the owner's known Resource/Version lifecycle, including the
intended generation when recovering an indeterminate commit. It preserves loaded
metadata and content rather than restoring an older document. Actual deletion
removes the old incarnation from that lineage; recreating the same ResourceId,
VersionId and bytes does not reconnect an outstanding old lease. A stale lease's
release cannot release a replacement incarnation's leases.

Lease acquisition/release changes no canonical snapshot, epoch, timestamp or event.
Rejections retain the previous metadata and bytes. Persistence continues to use
the existing FileStore schema; leases themselves are not persisted. Reload and
in-process identity reconciliation neither write a generation nor emit a mutation
event. A committed-outcome exception still exposes the exact published snapshot.

## Native FileType access

The stock projection acquires a lease for read and write handles through both
exact-Version and logical Resource aliases. Logical Opens resolve the default
Version and keep that exact identity, cursor, writer reservation and lease after
selection changes. Multiple aliases or Sessions contribute separate leases.

- Close releases its handle's lease; dirty writer Close retains it through commit.
- Session abandonment discards only that Session's handles and staged writes.
- Canceled or failed Open releases any acquired lease and unpublished reservation.
- In-flight reads retain their lease until the operation has drained.
- Same-owner snapshot rehydration does not invalidate an open writer's incarnation
  guard. Its normal content-conflict check still applies; actual deletion and
  recreation cannot let a stale handle overwrite the replacement.
- Typed creation transfers a lease issued by the owner into the existing prepared
  file reservation before durable commit. It does not reacquire the owner operation
  from inside its preparation callback.

The same existing file manager, handle table, store, Session checks and cursor
operations are used. No separate file, connection, cache or retention engine is
introduced.

## Direct and DI access

The capability is available on a directly constructed stock service:

```csharp
using var registry = new WotRegistryService(store, bounds);
await registry.InitializeAsync(ct).ConfigureAwait(false);
IWotRegistryVersionLeaseProvider leases = registry;

WotResource resource = registry.Current.FindResource(groupId, resourceId)
    ?? throw new InvalidOperationException("The Resource is absent.");
WotResourceVersion version = resource.FindVersion(versionId)
    ?? throw new InvalidOperationException("The Version is absent.");

using IWotRegistryVersionLease lease = await leases
    .AcquireVersionLeaseAsync(groupId, resourceId, version, ct)
    .ConfigureAwait(false);
ByteString document = await registry.ReadContentAsync(lease.Version, ct).ConfigureAwait(false);
```

`lease.Version` is the canonical snapshot captured by the owner. A stale snapshot
for a deleted Version fails with `BadNodeIdUnknown`; a snapshot from a replaced
incarnation fails with `BadInvalidState`. Obtain a new snapshot deliberately rather
than silently retrying by VersionId.

`AddWotRegistryServer` exposes `IWotRegistryVersionLeaseProvider` as the same
singleton as its registered stock `IWotRegistryService`. A custom service can
implement the capability and coordinate it with its own atomic mutation owner.
If an older service does not implement it, explicit DI capability resolution fails;
its pre-existing APIs remain available, but lease-protected retention is not claimed.

## File-provider compatibility

The shared xRegistry projection has additive async Open, async contentless-write
and owner-prepared preserving-write capabilities. Existing synchronous providers
keep their old paths. Lease-aware providers use TAP; invoking their low-level sync
Open callback does not block on async acquisition and returns `BadNotSupported`.
Normal generated native clients use the asynchronous dispatch.

`IXRegistryAsyncProjectedResourceFileHandleForwarder` keeps later operations on the
same file provider. `IXRegistryAsyncProjectedContentlessResourceFile` rechecks the
contentless claim after owner acquisition. `IXRegistryPreparedResourceFile` accepts
provider-owned candidate state during preserving-write preparation. None exposes
a lock or changes a wire Method/type ID or signature.

## Boundaries

This is an in-process owner lease, not a durable distributed lease or a reservation
against explicit lifecycle deletion. Explicit deletion still follows the existing
policy; an old lease cannot protect a replacement incarnation. A direct lease is
also not a content-write reservation: native writer exclusion belongs to the
existing file manager.

Applications with multiple independent mutation owners over shared storage need
provider-specific cross-owner coordination; this capability does not claim to
solve that distributed contract.

See [WoT registry and materialization](WoTConnectivity.md#11-wot-connectivity-registry-and-materialization-preview)
and [xRegistry roles](XRegistry.md#resource-meta-and-default-version-views).
