# Graph-wide prepared View publication contracts

These additive contracts let a View host contribute to a coordinator-owned
publication. They do not introduce another View engine, binding registry, store
decision, or independent View transaction.

## Expected and candidate images

`WotCommittedPublicationState` pairs the authoritative `RegistrySnapshot` with
the current `Views` and `ActiveBindingPlans`. Its `RefreshGeneration` comes from
the snapshot, not a separate counter. Constructing this immutable value does not
publish it. Mutable binding-capability descriptors are copied on input and
returned as detached snapshots; editing a supplied or returned descriptor does
not change the committed image or its registered provider.

`IWotPreparedViewProjectionHost` extends the existing `IWotViewProjectionHost`.
It advertises `SupportsPreparedPublication` truthfully and accepts:

```csharp
ValueTask<IWotPreparedViewPublication> PrepareAsync(
    ArrayOf<WotViewProjectionRequest> updates,
    ArrayOf<WotViewProjectionHandle> removals,
    WotCommittedPublicationState expectedPublication,
    CancellationToken cancellationToken = default);
```

Preparation computes the complete remaining request closure from the expected
image. A child update includes every affected ancestor. A shared canonical child
and role-keyed wrappers belong to one complete candidate image, not independent
parent-local blobs or competing route owners.

## Participant lifecycle

`IWotPreparedViewPublication` owns private candidate input until publication or
asynchronous disposal:

- `Changes` is the ordered `ArrayOf<NodeManagerBatchChange>` contributed to the
  coordinator's aggregate lifecycle preparation.
- `BindPreparedRegistrations` receives that participant's corresponding
  addition/replacement registrations, in order; removals produce no new
  registration. It returns `WotPreparedViewGraphState`, with complete graph
  bytes, complete resulting View handles, and all `AffectedResourceXids`.
- `OnPublished` acknowledges the coordinator's authoritative committed image.
  It is non-failing bookkeeping only: no I/O, new Node mutation, cancellation,
  generation allocation or second durable decision.
- Disposal before publication discards private state, never the old live View.
  Disposal after a committed/indeterminate store decision cannot undo that
  decision.

Canonical identity, wrapper roles, membership validation, full membership digest
and candidate ViewVersion semantics belong to the View planner. The coordinator
owns when they become committed. No-op, dry-run and confirmed noncommit retain
the previous committed token. Source references are resolved against the same
prepared source image; a candidate must not be reported absent merely because
it is not yet visible in the live address space.

The existing immediate `ApplyAsync` and `RemoveAsync` remain compatibility
operations. Calling them during preparation and attempting compensation later
does not implement this contract. A host that cannot participate in the common
lifecycle publication must not advertise prepared support.

## Captured dependency metadata

`IWotRefreshCaptureProvider` resolves to the registered
`WotMaterializationCoordinator`. Its `CaptureAsync` method captures the request,
selected exact-Version inputs and their leases. The result is preparation input,
not a prepared publication unit or a store-integrity token. Capture does not
convert, activate, retire or publish anything.

`WotResourceProjection` carries two init-only observation properties:

```csharp
public WotDependencySnapshot? DependencySnapshot { get; init; }
public WotDependencySnapshot? LastDependencyAttempt { get; init; }
```

The registry namespace owns `WotDependencySnapshot`, `WotRegistryOrigin` and
`WotDependencyTargetPin`; `WotDependency` remains in the materialization
namespace. A snapshot carries SourceVersionId, Generation, RequestId,
ResolvedAt, IsCommitted, EffectiveInputDigest, Edges and Targets. Its Generation
and projection RefreshGeneration are UInt32 values, distinct from the registry/
store's Int64 Generation.

The prepared metadata path must preserve both observation payloads.
Committed dependency state belongs to the same authoritative
publication as the Resource rows, graph, routes and plans. A failed attempt
does not replace the previous committed dependency snapshot, and dry runs do
not publish either dependency diagnostics or a new committed generation.
These payloads do not own another deciding record or generation allocator.

The capture's `GetRegistryInputDigest` fingerprints the registry inputs it holds.
It does not cover uncaptured external artifacts. `CreateRefreshPlan` reports the
publication owner's applied atomicity and unit count. It neither chooses those
units nor publishes `LastRefreshPlan`. The publication owner still performs the
final stale-input checks and commits the resulting image.

Captured registry authority is not an execution-partition selector.
OriginRegistry identifies registry authority; RegistryNodeId identifies its
registry root; VersionNodeId identifies a registry Version; DocumentUri
identifies or locates a document. The snapshot has no SourceModelUri,
SourceRootNodeId or partition field. A projection RootNodeId is an output,
not a captured partition selector. Nonempty-binding owner partitions must not
be inferred from leaf order/count, namespace membership or URI suffixes.
Passing a data-only multi-model path does not establish that binding authority.

Content-lease capability still comes from the actual wrapped
`IXRegistryResourceStore` owner. Both ordinary and leased reads remain observable;
SupportsDependencySnapshots is not an immutable-content lease substitute.
The existing capture provider and prepared store serve different purposes.
Neither the dependency snapshot nor these View contracts supplies another
selection engine, durable decision or generation allocator.
See [selected dependencies and exact-Version snapshots](WotDependencySnapshots.md)
for capture and observation behavior.

## Authoritative graph carrier

`WotRegistrySnapshot.CanonicalViewGraphState` is the immutable `ByteString`
carrier at the registry/manifest root. It is committed with every affected
Resource projection row in the same store generation. Per-Resource metadata is
not independent authority for a shared graph.

`WotRegistrySnapshot.RefreshGeneration` is the committed materialization
generation, distinct from the store's `long Generation`. Existing group/label
snapshot helpers retain both fields. `WithPublicationState` preserves graph
bytes for a null/default input, replaces them with an explicitly empty graph
for `ByteString.Empty`,
and records supplied committed generation values. No `ByteString?` is used.

Older manifests without these optional fields load as absent graph/generation
zero. An explicitly empty graph remains non-null and zero-length across
serialization and service reload; it must not become an absent legacy graph.
Retired identity/token history belongs to the canonical payload even when no
Views remain live. Parent-local retirement must not reset that history or
replace it with an empty payload.

Graph payload validation/serialization belongs to the View planner; the
store preserves its opaque bytes and rejects invalid base64 representation.
The carrier does not introduce a JSON Schema validator.

The existing `IWotRegistryPreparedStore` remains the only durable decision
contract. `ProjectionMetadata` scope includes graph bytes and committed refresh
state together with affected Resource projection metadata. NotCommitted,
DurabilityUncertain and Indeterminate retain their existing distinct meanings.

## Ordered live reconciliation

The live projection seam is an optional init-only
`XRegistryProjectionContext.ProjectionDispatcher`:

```csharp
public Func<Func<CancellationToken, ValueTask>, CancellationToken, ValueTask>?
    ProjectionDispatcher { get; init; }
```

Current-state and native `ReconcileProjectionAsync` operations use this
dispatcher when supplied. Generic behavior remains direct when it is absent;
WoT supplies its existing reconciliation FIFO. A supplied immutable transition
passed to `ReconcileAsync` remains caller-ordered and must not recursively
dispatch itself.

The FIFO orders live reconciliation and notification delivery, including native
deletion. It is not a durable decision, publication lease or generation owner.
Private preparation must not use a current-state reconciliation call to expose
candidate state. The coordinator remains responsible for its complete committed
registry/routing/reference/View/plan image and exact producer Version, Phase and
committed generation before releasing notification intent.

Once projection work has been accepted, cancelling the caller's wait does not
cancel that accepted work or poison readiness cleanup. Pre-decision cancellation
can still abort a private publication candidate; it cannot undo a committed
decision or cancel mandatory post-commit state publication. Consumer callbacks
and acknowledgment failures must not split the committed image or strand later
publications, and post-commit failures must remain explicit.

The stock WoT projection supplies this dispatcher through its reconciliation
queue. Generic xRegistry users without a dispatcher keep the direct path.
The queue does not supply the prepared batch or durable publication decision.

## Integration status

The declarations and graph-root persistence are independently composable.
`INodeManagerBatchLifecycle` is an optional capability; declarations alone do
not make a lifecycle implementation support it. A full coordinator/runtime
adapter must still stage and publish source routing, references, metadata and
View state through one owner before advertising atomicity. Do not infer
multi-resource atomicity, stock host support, or lock-free visibility merely
from these carrier types.

See [prepared registry metadata commits](WotRegistryPreparedStore.md) for the
validated content and generation lease contract.
