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

Canonical graph preparation requires an absolute scenario URI and a TD or TM
document kind before resolving candidate identities. Invalid updates are rejected
without changing the expected graph image.

The existing immediate `ApplyAsync` and `RemoveAsync` remain compatibility
operations. Calling them during preparation and attempting compensation later
does not implement this contract. A host that cannot participate in the common
lifecycle publication must not advertise prepared support.

The canonical native factory accepts a previous registration only from a
canonical View manager with the same exact logical-server and allocation
authority. Its candidate must retain the prior Resource allocations, Node roles
and token history. Creation also checks the actual server instance before
allocating a replacement manager. An unrelated registry registration is not
authority to replace its Nodes.

The stock canonical factory is also bound to that exact lifecycle operation.
A factory naming a predecessor cannot be used with `Add`, and a replacement
cannot supply a different registration. Ordinary and aggregate lifecycle
admission check this before invoking the factory; possession of a valid
predecessor is not permission to create a second owner of its Nodes.

Canonical managers participate in the existing reload lifecycle. Their complete
replacement supplies graph-owned References; retained inbound navigation
References transfer only to surviving owned Nodes. The Core lifecycle, not the
View planner, retains or invalidates captured Browse continuations according to
the selected graceful or immediate retirement policy. Retiring a graph does not
retire the source Nodes it organized.

## Stock prepared participant

`LifecycleWotViewProjectionHost` implements `IWotPreparedViewProjectionHost`.
DI resolves the typed interface to the same registered View host; a custom
immediate-only registration is not replaced or advertised as prepared.
The direct path pairs a `LifecycleWotViewProjectionHost` and
`LifecycleWotProjectionHost` over the same lifecycle.

The host copies request membership, links and namespace input before awaiting
admission. It checks the expected graph bytes, exact current registration and
issued View handles. A foreign expected image or removal handle cannot authorize
replacement. Existing immediate images cannot silently become canonical owners.

Source and View factories run in the same Core batch. A private source lookup
uses the actual preceding candidate managers, excludes replaced/removed source
owners, and otherwise resolves retained live sources. Binding checks those exact
source registrations and the exact View candidate. It does not substitute the
old serving source when its replacement is still private. The View itself has no
binding runtime or executable affordance copies.

The coordinator maps captured source identities in a private namespace table,
then rebases them against the prepared native source image. Authored View
namespaces must already be known; View preparation does not register them.
Authored affordance identities are checked against the named source's captured
converted partition, not a string-prefix relationship with its root. Numeric,
GUID, opaque and independently named string identities retain their NodeIds.
The shared native ownership index resolves partition aliases and namespace URIs;
another Resource or a dependency model alone does not supply source membership.
The bound source roots, complete View handles, affected ancestor metadata and
graph-root payload enter the existing registry decision together. On publication
the View host installs its already-built bookkeeping; it performs no second
decision, Node mutation or I/O.

Ordinary TD/TM roots used by the stock participant carry their inverse
`HasWoTProjection` in the private converted source image. Core's prepared
reference publication installs the matching logical-Resource edge together
with the canonical View correlations. A rejected candidate cannot add or
remove those serving edges. This does not implement arbitrary programmatic
cross-owner correlation repair.

The generated logical-Resource `ProjectionMembershipDigest` Property reads the
full 32-byte digest from the authoritative committed graph carrier, rather than
maintaining a second per-Resource graph. Before an active canonical publication
it reports `BadWaitingForInitialData`. Clients locate the Property by its
namespace-qualified BrowseName; its NodeId is assigned by the existing registry
projection infrastructure.
Native operations obtain the digest from the same captured NodeManager image
that supplies the ViewVersion, including while a newer generation is active.
Prepared View NodeManagers implement `IWotCanonicalViewReadImage` and retain
their immutable Resource-to-digest lookup until captured operations drain.
Providers are discovered across the captured visible NodeManagers; a custom
View owner need not register the WoT Connectivity namespace.
The stock source host rejects a custom View candidate without that contract
before the durable decision. A captured image with no active projection reports
`BadWaitingForInitialData`; it does not fall through to newer registry state.

Context-free reads share an immutable digest index for their registry snapshot;
polling does not reconstruct the graph. This index does not retain retired
snapshots, and a malformed or unsupported carrier remains an error rather than
a previously cached digest.

The direct constructor accepts a `WotProjectionRetirementPolicy`; its default
is graceful. DI takes that policy from `WotRegistryServerOptions`, like the
coordinator. The complete remaining request closure governs canonical retirement,
and an empty live graph retains serialized allocation/token history.

Cold startup uses `IWotRecoverableViewProjectionHost.PrepareRecoveryAsync` to
restore the exact persisted graph through the same prepared source batch.
An ordinary preparation still rejects a persisted, unbound image; callers do
not bypass ownership checks by treating it as a fresh publication.

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
If startup needs to persist a missing dependency index, that migration advances
the store generation but preserves the graph carrier and committed refresh
generation. It does not create a new View publication.
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

## Cold recovery of a committed publication

Registry readiness calls `WotMaterializationCoordinator.RecoverAsync` after
loading the deciding store. A fresh registry without a committed refresh
generation keeps its ordinary startup materialization behavior. A committed
publication is restored before readiness returns, including an explicitly
empty publication. This is independent of `AutoRefresh`.

Recovery selects the retained committed input of each active Resource, not its
newer desired/default Version. The deciding manifest retains that input's exact
Version metadata and content digest when the publication commits. Subsequent
uploads, including edits to the same VersionId, do not replace that input;
its immutable content remains owned with the committed image. Older active
manifests without this evidence fail recovery explicitly rather than guessing
which bytes were originally published.

Source and View candidates are built privately under the existing invocation
owners. The View planner validates the recorded logical server, roles, Node facts,
membership and token history against the recovered source image. Resource root
NodeIds are rebased to the current namespace table only in the runtime snapshot;
durable identities, store/refresh generations and graph bytes do not change.
An active projection Resource requires its recorded canonical graph even on a
fresh host with no runtime View handles. Null or empty graph bytes are not
evidence of an ordinary source-only or empty publication when committed
projection inputs remain.
Before publication, the complete recovered View handle set must match the
active graph entries and their committed logical Resources. Missing/duplicate
owners, different root identities or inconsistent materialized-node counts
fail recovery; the runtime does not preserve or repair a contradictory count.

`IWotRegistryRecoveryStore` validates an owner-issued generation and retains its
authority through the runtime switch. The registry's
`IWotRegistryRecoveryPublication` prepares only this local representation.
Recovery writes no manifest, allocates no new generation and repeats no registry
or materialization notification intent. Concurrent store changes are rejected
at final validation. Disposal before the switch releases private state and
validation ownership. Repeating successful recovery is a no-op, and ordinary
refresh can subsequently activate pending desired inputs.

Direct-constructor hosts call `RecoverAsync` after initializing the registry;
ordinary `RefreshAsync` does not reinterpret every newly constructed coordinator
as a cold server. Warm indeterminate-decision resolution and recovery of retained
resolution-only dependency inputs remain separate acceptance work; these cold
startup contracts do not establish Full-profile or HA conformance.

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

## Publication unit planning

When the source host and registry support invocation-isolated prepared publication, all four
atomicity requests use the same prepared source/View/store owner. PerResource
starts with one activation Resource, PerGroup with selected work in one group,
PerClosure with a dependency closure, and PerRegistry with all selected and
required activation work in one deciding transaction. Unrelated registry rows
remain in the authoritative full snapshot but are not added to the work.

Resolution inputs and activation members are distinct. Legal reference SCCs
coactivate; only the ordering graph can reject a cycle. Grouping cycles coarsen
before publication, and the summary reports the applied mode. Disabled inputs
remain available to conversion without becoming source owners or group members.
A dependent unit requires a successful or exact unchanged prerequisite
publication, not merely fetched or converted bytes.
Native model requirements are expanded from the registry metadata index and
retain their model-URI target pins when a closure is partitioned.

An update retains the complete affected old/new closure, including merges and
splits. Old source registrations are replaced/retired in the same prepared
batch as their complete replacement. A failed member remains in its intended
unit; its prepared peers do not publish. Independent units advance the committed
refresh generation separately and retain earlier successes after later
validation failures. No-op and dry-run units do not advance it.

Views join their source unit and still require
`IWotPreparedViewProjectionHost`; the immediate View API is not an atomic
substitute. Unsupported View preparation fails before source publication.
The unit planner does not change the canonical graph algorithm or renderer.
A prepared View participant's affected-Resource footprint must stay within its
planned activation/retirement unit. An unexpected outside Resource is rejected
before the durable decision, not used as permission to mutate unrelated rows.

## Invocation isolation and final plan

`IWotInvocationProjectionHost` captures the source owner's routing, type and
factory revisions before input acquisition. Its `SupportedAtomicities` is the
actual supported subset. `IWotInvocationRegistryPublicationService` reserves
publication on the existing registry service; each unit still uses
`IWotPreparedRegistryPublication` and the same deciding store.

The coordinator retains both admissions through its entire commit phase,
including intervening notifications and completion bookkeeping. Before any
switch it checks the caller generation, preparation generation, authoritative
full registry snapshot, source revisions and captured policies. A stale
nonzero caller generation fails `BadInvalidState`. Other stale preparation is
reacquired and rebuilt, including when ExpectedGeneration is zero. Successful
own units update the checked snapshot/generation without self-conflict.

Registry mutations await the invocation's completion. Conflicting lifecycle
operations fail `BadServerTooBusy` before effects and can retry afterward.
Callbacks must not synchronously wait for mutations excluded by their own
invocation. A lifecycle mutation attempted from a committed callback remains
an explicit callback/cleanup failure, not a successful nested publication.
Ordinary lifecycle operations outside an invocation retain their existing
callback and drain behavior.

`LastRefreshPlan` uses the generated `WoTRefreshPlanDataType` on the well-known
registry. It initially reads `BadWaitingForInitialData`; an empty server startup
does not fabricate a refresh plan. An actual Refresh publishes the final
RequestId, PreparationGeneration, RequestedAtomicity, AppliedAtomicity and
UnitCount under admission before its first switch. Explicit empty selections
that match no work can report a zero-unit plan. Dry runs leave the Property
and committed state unchanged. Returned coordinator plan objects are detached.

Failed and checked-unchanged dependency attempts can persist their diagnostic
metadata without replacing the committed dependency snapshot or advancing
RefreshGeneration. Same-content publication metadata repair is a changed unit,
not Unchanged. Store generation remains a separate Int64 counter.

Confirmed store noncommit before any accepted unit retains its dedicated
exception contract. After an earlier independent success it produces failed
unit rows and a partial-success summary with the actual committed generation.
Committed warnings do not prevent later independent units. Cancellation or an
indeterminate decision does not fabricate a successful completion; the existing
store recovery barrier remains authoritative. Automatic replay of an unresolved
deciding record is not supplied by these admission interfaces.

Preparation is serial, which respects every nonzero MaxParallelism upper bound;
zero leaves that choice to the server. Timeout is an invocation-wide budget in
milliseconds, including waits and capture. Zero disables that budget; negative,
non-finite or values above Int32.MaxValue are rejected. Cancellation after a
durable decision cannot undo its accepted unit.

### Provider requirements

Refresh no longer falls back to visible immediate Resource commits when the
configured owners cannot provide the requested isolation. Unsupported owners
or modes fail `BadNotSupported` before acquisition/publication. Read-only
`CaptureAsync` remains available independently.

The stock lifecycle/host and a file registry with genuine immutable-content
leases support all four modes. The current process-local registry store and
immediate-only custom projection/View hosts do not provide that capability;
they must not advertise it. The file provider's existing platform constraints
still apply. No copied byte buffer, alternate deciding owner or simulated
successful publication substitutes for a missing provider guarantee.

## Integration status

The declarations and graph-root persistence are independently composable.
`INodeManagerBatchLifecycle` is an optional capability; declarations alone do
not make a lifecycle implementation support it. The stock NodeManager lifecycle
implements this interface. The hosted lifecycle forwards it to the attached
server, or reports that a custom lifecycle does not support it.

The lifecycle privately prepares additions, replacements and removals. Commit
rechecks the exact registrations and atomically validates/reserves the serving
routing revision before calling the supplied durable decision. Conflicting routing
writes fail before effects rather than being accepted and overwritten. The reservation
protects the single routing switch and internal host bookkeeping; it releases on
noncommit/cancellation or before committed-state and binding-reconciliation callbacks.
Readers retain coherent captured images, and successful later writes are preserved.
A prepared batch can be
consumed only once; disposal aborts an uncommitted candidate. Post-decision
cancellation cannot undo the committed image.

The optional committed-state callback runs after the routing switch and before
readiness and retirement. If that callback throws, the lifecycle retains its
failure in `NodeManagerBatchResult.CleanupFailure` and still runs reconciliation.
Readiness and retirement failures are also reported as committed outcomes, not
as permission to discard the active registrations.

### Committed coordinator handoff

The prepared-unit coordinator installs the exact prepared source handles, closure
bookkeeping, namespace ownership, committed View/plan image and refresh generation
before releasing registry `Changed` observers. The View participant is acknowledged
before those notifications; registry publication still runs if that acknowledgment
fails. This uses the existing prepared registry decision and Core committed-state
callback, not a second transaction or a replay of the decision.

A confirmed noncommit or cancellation before the decision leaves the previous
registry and live owners intact and disposes the private candidates. A confirmed
commit, including a store durability warning, completes publication despite caller
cancellation. Store, observer and Core reconciliation warnings remain explicit in
the committed refresh result. Registry and committed materialization-event
observers are each invoked once; one observer's exception does not skip the remaining
observers or later committed event intents. A notification failure does not poison
admission of the next publication.

An indeterminate decision is not converted into a committed result. The registry
continues to block conflicting mutation until its existing recovery path establishes
the deciding store state. Failure to reacquire validated store evidence likewise
retains the existing reload requirement; a committed image is not rolled back.
This handoff does not implement automatic store recovery.
The captured selected input image remains distinct from the complete authoritative
registry snapshot, including unselected Resources. Null, empty and retained-history
graph carriers keep their existing meanings.

The stock participant now joins the existing four-mode publication owner rather
than falling back to immediate View mutation. Full-profile conformance does not
follow from these carrier types or this participant. Warm indeterminate recovery,
retained resolution-only input recovery, coordinated programmatic mutations,
the independent R35 correlation residual and JSON Schema validation remain
separate work.

See [prepared registry metadata commits](WotRegistryPreparedStore.md) for the
validated content and generation lease contract.
