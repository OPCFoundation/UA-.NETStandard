# NodeManager lifecycle change sets

Use `LifecycleChange` to publish additions, reloads and removals through one
transaction entry point. A reload or removal targets a stable `NodeManagerSlot`;
preparation selects its current generation once, and commit rejects a later
conflict rather than silently selecting a different generation.

Resolve `INodeManagerLifecycle` from the existing server dependency-injection
registration, or use `StandardServer.NodeManagerLifecycle` for direct construction.
Both paths use the server's existing lifecycle owner. No separate change-set
service or registration collection is created.

## Public contract and compatibility

The change-set API follows the F1 lifecycle model in the
[adopted design report](https://claude.ai/artifact/3HUn2FGiECrBHbHgEUSX3n) and
[OPCFoundation/UA-.NETStandard#4931](https://github.com/OPCFoundation/UA-.NETStandard/issues/4931):

| API | Ownership and behavior |
| --- | --- |
| `LifecycleChange.Add(factory)` | Creates a new slot and private candidate. |
| `LifecycleChange.Reload(slot, factory, transitionMode)` | Replaces the generation selected at preparation. `Replace` names the same operation. |
| `LifecycleChange.Remove(slot)` | Unpublishes the selected generation. |
| `lifecycle.ApplyAsync(ArrayOf<LifecycleChange>, ct)` | Prepares and commits one change set with an in-memory decision. |
| `lifecycle.BeginAsync(expectedRevision, ct)` | Captures optimistic publication evidence for an `ILifecycleWriter`. |
| `ILifecycleWriter.PrepareAsync(...)` | Prepares an `IPreparedChanges` unit against the writer's evidence. |
| `IPreparedChanges.CommitAsync(decideAsync, ct)` | Performs the optional durable decision, publishes, then returns `LifecycleResult`. |
| `LifecycleResult` | Retains the committed revision, registrations, slot snapshots and issues; exposes the generation owner's physical retirement task. |

This layer is additive to the preceding lifecycle API. `ApplyAsync`, `BeginAsync`
and the slot-based `PrepareAsync` overloads are extension methods, not new required
members of `INodeManagerLifecycle`. Adding interface members would break existing
providers and binaries; default interface implementations cannot bridge that
change on .NET Framework 4.8. Consumers must import `Opc.Ua.Server`. Provider
implementations and mocks still implement or intercept the existing
`PrepareAsync(ArrayOf<NodeManagerBatchChange>, ...)` and `CapturePublication()`
members, not the extensions. A custom provider adopting slot-based calls must
also resolve the additive `NodeManagerBatchChange.TargetSlot` under its own
preparation admission; legacy exact-generation calls still use `Current`.
Replacement results that do not preserve the requested slot are rejected and
disposed before commit rather than accepted as additions.

The report's list and untyped metadata shapes use the repository's `ArrayOf<T>`
and generation-owned `INodeManagerReadImage` instead. The existing
`IPreparedNodeManagerBatch`, exact-generation `NodeManagerBatchChange` and
`NodeManagerBatchResult` surfaces remain available; the old result delegates to
its `CommittedResult`, which is the same `LifecycleResult`, rather than copying
revision, reconciliation or retirement state.

The obsolete convenience and explicit-context extensions remain for one migration
release. Their signatures and retry behavior are unchanged. Synchronous factories
can use `factory.AsAsync()` to reuse the existing compatibility factory adapter;
new change sets accept `IAsyncNodeManagerFactory`. Factory creation remains
synchronous in that adapter; it never blocks on asynchronous work. This layer does not add a synchronous publication path
or require existing providers to implement another interface.

## Single operation and mixed batch

An addition returns its private candidate's now-published registration. Retain its
slot for later operations; retain the registration only when exact-generation
identity is required.

```csharp
LifecycleResult added = await lifecycle.ApplyAsync(
    [LifecycleChange.Add(factory)], cancellationToken);
NodeManagerSlot slot = added.Registrations[0].Slot;

LifecycleResult reloaded = await lifecycle.ApplyAsync(
    [LifecycleChange.Reload(slot, replacement)], cancellationToken);

LifecycleResult removed = await lifecycle.ApplyAsync(
    [LifecycleChange.Remove(slot)], cancellationToken);
await removed.Retired;
```

A mixed batch publishes one routing revision. `Registrations` lists additions
and replacements in change order; removals have no new registration.

```csharp
LifecycleResult result = await lifecycle.ApplyAsync(
    [
        LifecycleChange.Remove(obsoleteSlot),
        LifecycleChange.Add(newFactory),
        LifecycleChange.Reload(existingSlot, replacement)
    ], cancellationToken);
```

Duplicate targets, foreign or removed slots, and protected system slots are
rejected before factories run. `Reload` defaults to `Migrate`, which preserves
compatible monitored items and slot-bound recovery. Use `Invalidate` to make
existing items terminal. Removal always invalidates; clients must create new items
if matching nodes are added again. There is no retained-generation reload mode.

## Preparation, decision and abort

Use explicit preparation when the application has a durable decision or prepared
in-memory state to publish. Dispose every uncommitted unit, including after
cancellation, decision failure or a stale commit.

```csharp
await using ILifecycleWriter writer =
    await lifecycle.BeginAsync(expectedRevision, cancellationToken);
await using IPreparedChanges prepared = await writer.PrepareAsync(
    [LifecycleChange.Reload(slot, replacement)], cancellationToken);

LifecycleResult result = await prepared.CommitAsync(
    decideAsync: token => store.DecideAsync(token),
    publishCommittedState: () => application.PublishPreparedState(),
    cancellationToken: cancellationToken);
```

`BeginAsync` checks an optional expected routing revision. The writer also retains
the existing type/factory revision evidence. It does not reserve the mutation
writer while factories run. Successful sequential units advance its evidence to
their own revision; an unrelated publication makes it stale. A stale writer
cannot prepare and does not invoke factories.

A prepared stale unit is rejected and consumed before the durable decision.
`ApplyAsync`, explicit preparation and writer operations never automatically
rebase or retry. A decision callback runs once and cannot re-enter lifecycle
mutation. A decision failure is uncommitted; if the external outcome is uncertain,
resolve that uncertainty before starting another transaction.

Only the obsolete Add/Reload/Remove convenience adapters retain automatic
conflict retries. They retry at most three attempts, only for a pre-decision,
pre-commit conflict tagged to that exact candidate. They never retry arbitrary
`InvalidOperationException`, factory, cleanup, decision or committed failures.
Each allowed retry prepares a fresh candidate.

## Typed metadata

Bind an `INodeManagerReadImage` to an addition or replacement before consuming the
prepared unit. Its `Owner` must be the exact candidate manager for that slot.
Each slot can be bound once; the adapter binds the accumulated images to the
existing prepared owner at commit.

```csharp
NodeManagerRegistration candidate = prepared.Registrations[0];
INodeManagerReadImage image = application.CreateReadImage(candidate.NodeManager);
prepared.SetMetadata(candidate.Slot, image);
```

For metadata-only publication on live managers, use
`writer.PrepareReadImagesAsync(images, ct)` and commit the returned unit. Those
images are already bound during preparation and cannot be changed through
`SetMetadata`. Neither path permits an untyped metadata bag.

## What is authoritative after publication?

The routing swap is the commit point. After it, cancellation or a callback
failure cannot abort the transaction. `LifecycleResult.Revision` identifies that
publication; `PublishedSlots` retains its exact generation snapshots even if a
nested readiness callback publishes a newer revision before the parent returns.
`Slots` contains stable handles whose current contents may subsequently change.

`Issues` describes unfinished reconciliation, including whether each issue is
retryable or superseded. `CleanupFailure` retains the original failure for
diagnostics even after recovery. Retry only unfinished work through
`result.RetryReconciliationAsync(ct)`; it does not repeat the durable decision,
factories or routing publication. Concurrent callers join the same attempt, and
canceling one caller's wait does not cancel admitted callbacks. An application
publication callback must be idempotent because a throwing attempt may already
have applied part of the state.

Newer publication supersedes generation-owned recovery that would overwrite it.
Such an issue becomes nonretryable; its result still describes the earlier
committed revision. Nested lifecycle publication is permitted from reconciliation
callbacks, not from the durable decision.

`Retired` completes independently after retired generations drain and physical
cleanup finishes. `RetirementStatus` distinguishes pending, completed,
completed-with-failure and canceled tasks. Do not await retirement from a request
or callback whose own admission must drain. Committing a removal does not wait
for that request to finish.

Compatibility committed exceptions implement `INodeManagerCommittedFailure`.
Their existing `Result` remains a `NodeManagerBatchResult`; `CommittedResult`
exposes its authoritative owner. Legacy add/reload exception constructors that
never received a result still return null. Do not interpret those exceptions as
an aborted publication or retry the operation as another addition.

## Component boundaries and remaining work

Preparation still constructs private candidates and validates the captured view.
The lifecycle's typed commit owner performs the durable decision and publishes
registration/revision state. The master owns routing and metadata reservations.
Published reference work owns addition reconciliation and observer notification;
the lifecycle owns bindings, monitored-item transitions and retirement tracking.
The former callback-heavy `NodeManagerBatchContext` is no longer the handoff.

Existing internal host interfaces and private `PreparedNodeManager` candidates
remain behind this typed plan. Removing every host/candidate adapter would
require a larger provider migration than this compatibility layer. F2 candidate
ownership is described below. This change does not implement F3 metadata redesign
or the matched notification, activation and retirement performance gaps recorded in
[OPCFoundation/UA-.NETStandard#4952](https://github.com/OPCFoundation/UA-.NETStandard/pull/4952).

## Candidate state and cleanup ownership

`PreparedNodeManager` receives its generation identity and exact predecessor at
construction. The lifecycle assigns the identity before `CreateAddressSpaceAsync`
runs, so preparation callbacks continue to resolve the candidate's stable slot
and generation. Routing preparation validates that identity; it does not assign
the candidate to a generation again.

The candidate has one private-to-published or private-to-aborted transition.
Publication requires a live generation, releases the predecessor reference and
permanently excludes private abort, including after retirement. An abort caller
claims address-space destruction once. A repeated abort does not repeat a failed
destruction callback. The batch entry separately transfers manager disposal once,
including when destruction throws; cleanup failures remain observable.

| Resource | Owner and boundary |
| --- | --- |
| Private metadata image | `PreparedBatch.Preparation` owns the shared `ChangePreparer`; individual candidates do not retain duplicate preparation pointers. |
| Routing image and reservation | `PreparedRoutes` owns the next routing epoch and reservation until publication or abandonment. Abandonment closes its unpublished epoch and prepared admission; host abort still owns private address-space destruction. |
| Generation identity | The generation registry assigns one identity per canonical manager resource; synchronous/asynchronous adapters retain their shared resource identity and owning facade. |
| Private manager | The batch entry owns disposal until publication transfers lifetime to the generation owner. Host abort claims address-space destruction independently of disposal. |
| Retirement task | The reclaimer owns each retirement record, worker and retry checkpoints. Results expose completion without acquiring cleanup ownership. |

Writer mutation admission, callback permits and ambient scopes remain distinct.
The writer serializes the durable decision; callback permits authorize only their
declared host reentry capabilities and are revoked when the callback ends.
Operation scopes retain shutdown admission while writer-free callbacks execute;
independent retirement workers suppress inherited execution context. These
mechanisms cannot be replaced by a candidate publication flag.

Physical retirement still waits for generation admission, routing epochs and
retained dependencies to quiesce before resource disposal and address-space
destruction. The transitive `NodeManagerRoutingTable.Remove` /
`RemoveAndCapturePosition` cleanup path and `UsesDirectReferenceStorage` adapter
checks remain live and are retained. F2 changes writer-path state and ownership;
it adds no request, sampling or notification-path work and does not qualify the
performance work in
[OPCFoundation/UA-.NETStandard#4869](https://github.com/OPCFoundation/UA-.NETStandard/pull/4869).

The former private `ResolveNamespaceIndexes`, `AddExternalReferencesAsync` and
`RemoveExternalReferencesAsync` helpers have no callers, including through the
compatibility adapters, and are removed. Their serving effects belong to the
existing prepared routing and reference publication paths; removing them does
not remove reference reconciliation or destruction-failure diagnostics.
