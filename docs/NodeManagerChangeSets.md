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
ownership is described below. The type-table staging behavior and remaining F3
qualification are described in the next section. The matched notification,
activation and retirement performance gaps recorded in
[OPCFoundation/UA-.NETStandard#4952](https://github.com/OPCFoundation/UA-.NETStandard/pull/4952)
remain separate.

## Type registration snapshots

Type-table captures share a registration graph until a handle first mutates it.
That writer detaches by copying the type, encoding and reference-name indexes
and their connected hierarchy. A candidate therefore keeps its type changes
private during preparation, while readers of a captured graph continue to see
the graph they captured.

Publication installs the validated graph under the canonical type-table lock.
Staging handles bound to that canonical table then observe later registrations;
retired handles detach before cleanup can remove registrations still used by a
live generation. Encodeable-factory captures already share immutable frozen
maps, while additive merges still build merged frozen maps.

The `TypeTable` is server-wide, not generation-specific. Candidate preparation
uses a private snapshot, and publication merges registrations additively. A
candidate removal does not delete a serving registration; publication rejects
conflicting type hierarchies, reference-type names and encoding owners. Published
handles share the canonical table. Live node deletion removes a type only when
no other active NodeManager owns its NodeId. A retiring handle detaches before
cleanup, so its deletions affect only its private table. The canonical table
retains additions after generation retirement; this layer does not track
generation owner counts or reclaim those registrations.

A three-round sequential net10 Release comparison against baseline
`fcfc84eab5c105780d7268474a47d344d100aca1` measured these median capture costs
for five snapshots:

| Registered types | Baseline time / allocation | Copy-on-write time / allocation |
| ---: | ---: | ---: |
| 128 | 0.172 ms / 43,696 bytes | 0.024 ms / 5,216 bytes |
| 2,048 | 7.827 ms / 828,296 bytes | 0.018 ms / 5,216 bytes |
| 16,384 | 26.784 ms / 4,596,984 bytes | 0.017 ms / 5,208 bytes |

The first mutation still copies the graph. At 16,384 types, that copy measured
29.054 ms and 4,597,048 bytes per five mutations on the candidate. The copy
moves from capture under the canonical serving-table lock to the first write
under the private staging-table lock; total copy work is not eliminated.

Three alternating net10.0 Release rounds compared the S6 parent
`0a48fb5cf2de4c9bec3a2beafe41d8caedb482ea` with S7
`1cf9db60d24feb1b916f2e4741d92a1334f4f5c7`. Each round measured five
`LifecycleWriter.CaptureRevision` calls, then four concurrent candidate
preparations through `ChangePreparer.CreateNodeManagerAsync`. Each test factory
added one distinct root subtype to its private staging type table. The table
reports median elapsed time and allocation for the capture calls, and concurrent
wall time plus summed worker allocations for candidate preparation.

| Registered types | S6 capture, 5 calls | S7 capture, 5 calls | S6 concurrent preparation, 4 candidates | S7 concurrent preparation, 4 candidates |
| ---: | ---: | ---: | ---: | ---: |
| 128 | 0.29 ms / 225,568 bytes | 0.14 ms / 32,888 bytes | 2.97 ms / 394,168 bytes | 3.34 ms / 420,056 bytes |
| 2,048 | 9.81 ms / 4,148,568 bytes | 0.08 ms / 32,888 bytes | 3.01 ms / 3,470,232 bytes | 10.55 ms / 3,491,872 bytes |
| 16,384 | 22.85 ms / 22,992,048 bytes | 0.09 ms / 32,888 bytes | 23.87 ms / 18,547,144 bytes | 6.33 ms / 18,572,808 bytes |

`CaptureRevision` time and allocation stayed nearly constant in S7 as the model
grew. Concurrent preparation with the first type write was faster for 16,384
types, similar at 128, and slower at 2,048. Worker allocations were close
between versions at every size; S7 moves the graph copy to the first staging
write rather than removing it. This fixture measures metadata capture, staging
server creation and one type registration. It does not measure address-space
construction, routing publication or full lifecycle duration, and its models
use root registrations rather than a representative hierarchy.

The focused TypeTable and encodeable-factory selection passed 177/177 on each
supported TFM: net48, net8.0, net9.0 and net10.0. This includes the regression
that retiring a detached handle cannot remove another published contribution.
The encodeable matrix round-trip fixture passed 6/6 on each of those TFMs across
Binary, XML and JSON. The selected win-x64 NativeAOT lifecycle change-set test
passed 1/1 with a runtime NodeSet ObjectType registration retained across
replacement; the NativeAOT binary and JSON encoding fixture passed 5/5. The
new server-level concurrent capture and preparation tests passed 2/2 on net10.0
and 2/2 on net48. These results do not include a full-project test run or the
separate #4869 benchmark.

## Readiness and discovery registration

`StandardServer` now reports itself online to the Local Discovery Server (LDS)
only after initial publication reconciliation and all initial NodeManager
readiness callbacks complete. Startup prepares registration data offline and
starts the registration timer after readiness succeeds and the server state is
updated. If readiness fails, startup stops before scheduling discovery
registration. Shutdown still stops the timer and sends an offline update only
when the server had successfully registered online. This server-wide status
covers initial startup; readiness retries for later dynamic registrations remain
per-generation and do not change the LDS status of the whole server.

`DeferConfigurationUntilReady` and `DeferPublishedAuthoringUntilReady` now
reject a manager during configuration if it does not participate in readiness.
The error names `INodeManagerReadinessNotification.OnServerReadyAsync` and
`CompletePublishedConfigurationAsync`, so a missing forwarding callback fails
before the host publishes an unsealed address space.

Lifecycle wait warnings retain elapsed time and now report the total pending
readiness participants, up to eight slot/generation/manager identities, active
lifecycle operations, blocked generations, active admissions and pinned view
epoch counts. Canceling a caller's wait does not cancel the underlying shared
operation. The LDS ordering regression passed 1/1 on net10.0, the deferred
configuration readiness tests passed 13/13, and the lifecycle wait diagnostics
and discovery-registration selections passed 11/11. The focused run is not a
full server or DI test-suite qualification.

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
[OPCFoundation/UA-.NETStandard#4869](https://github.com/OPCFoundation/UA-.NETStandard/issues/4869).

The former private `ResolveNamespaceIndexes`, `AddExternalReferencesAsync` and
`RemoveExternalReferencesAsync` helpers have no callers, including through the
compatibility adapters, and are removed. Their serving effects belong to the
existing prepared routing and reference publication paths; removing them does
not remove reference reconciliation or destruction-failure diagnostics.

## Historian registration ownership

Historian registration during candidate preparation must not change the serving
registry. `StagingServer.HistorianRegistry` now records NodeId, namespace URI and
default bindings in a candidate-owned overlay. Its resolver combines candidate
and serving bindings by scope, so exact NodeId lookup still precedes namespace
lookup, and namespace lookup precedes the default provider.

After the candidate's readiness callback succeeds and its registration is
confirmed current, the overlay replaces that candidate's complete binding set
in the serving registry. A failed callback leaves the serving registry
unchanged; retry publishes once. Same-scope registrations within a candidate
are last-write-wins. A manager that stages historian state must implement
`INodeManagerReadinessNotification`; otherwise preparation fails and candidate
resources are disposed without publication.

Routing publication withdraws the retiring generation's bindings before the
new routing snapshot becomes visible. The retired manager's local overlay
remains available to already-admitted work until generation cleanup completes.
Owner removal leaves direct server registrations and other generations'
bindings intact. Published providers remain owned by the serving registry
through server shutdown. Candidate builders transfer to server tracking after
readiness and are removed from that tracking after generation cleanup; abort
and retirement dispose their capture handlers.

The staged-registry and lifecycle readiness regression selection passed 77/77
tests on net10.0 and 77/77 on net48. It includes pre-readiness invisibility,
readiness retry, preparation abort, overlapping replacement retirement,
cross-scope precedence, duplicate registration, and shared-provider disposal.
These are focused selections; no full-solution run or #4869 performance result
is claimed here.
