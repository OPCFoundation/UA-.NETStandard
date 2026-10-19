# WoT design and provider contracts

This page describes the document-to-NodeSet conversion contracts of the WoT
types library. See [bindings and conversion](WotBindings.md) for document
authoring and [NodeSet conversion](WoTNodeSetConversion.md) for the mapping
rules. These contracts do not establish Full-profile or full JSON Schema
conformance. The registry runtime's ownership, publication, and recovery
contracts follow the conversion contracts below; hosting and client examples
remain in [WoT Connectivity](WoTConnectivity.md).

## Contents

- [Conversion providers](#conversion-providers)
- [DataType declaration inputs](#datatype-declaration-inputs)
- [Ownership and publication](#ownership-and-publication)
- [Provider requirements](#provider-requirements)
- [Dependency capture and observations](#dependency-capture-and-observations)
- [Version leases](#version-leases)
- [Prepared registry store](#prepared-registry-store)
- [Canonical Views](#canonical-views)
- [Dry-run validation](#dry-run-validation)
- [Recovery](#recovery)
- [Bridge deletion and recovery](#bridge-deletion-and-recovery)

## Conversion providers

`WotNodeSetConverter` converts a document to a NodeSet2 model. It resolves
declarations from the captured sibling documents of the Thing before any
loaded type. Callers supply the exact original bytes and the contextual
declarations; the converter does not perform a fresh default lookup or use an
arbitrary Web resolver.
See [type binding](WotBindings.md#resolving-a-type-binding-the-local-context).

An embedded `uav:nodeSet` needs no external model fetch. Required models absent
from both the converted document and the caller's resolver remain diagnostics.

### DataType declaration inputs

Stored definitions can be referenced by graph identity, qualified name, or
portable NodeId. Lookup uses the carrying element's original context. Graph
identities need not resemble document locations; ambiguous owners fail.
`uav:dataTypeDefinition`, `uav:fieldDataTypeDefinition`, typed links, and
`uav:dataTypeSubtypeOf` contribute dependencies. Recursive fields are legal;
inheritance cycles are not. An acyclic base/derived pair in one document does
not create a cycle between documents.
Opaque metadata and literal instance values are not definition indexes.

`IWotCapturedDataTypeDefinitions` on the captured Thing resolver supplies
complete `WotDataTypeDefinitionSource` values through `DataTypeDefinitions`.
`IWotDataTypeDefinitionResolver.ResolveDataTypeDefinitionsAsync` resolves
definitions on demand. Resolvers own the documents they return; the converter
does not dispose borrowed documents and does not retain them after conversion.

Generated NodeSets declare the namespaces their Nodes own; merely referring to
a namespace does not claim it.

## Ownership and publication

`IWotRegistryService` owns an immutable `WotRegistrySnapshot`. Its Int64
`Generation` tracks store mutations; UInt32 `RefreshGeneration` tracks committed
materialization. Metadata-only observations need not replace runtime Nodes.
The stable registry NodeManager owns metadata/files; source generations own
converted Nodes and binding runtimes; canonical Views own navigation, not copies
of source affordances.

The coordinator captures routing/type/factory revisions through
`IWotInvocationProjectionHost` and reserves the registry owner through
`IWotInvocationRegistryPublicationService.BeginPublicationAsync`. Candidates,
read images, and source/View changes prepare privately before the deciding store
commits. Reservations remain through notification/completion bookkeeping.
Confirmed noncommit disposes candidates and preserves the old image; confirmed
commit switches routes/read images together. Cancellation after that decision
cannot turn commitment into rollback. Cleanup/readiness/observer failures are
committed warnings. An indeterminate decision fences mutation until recovery.
Conflicting lifecycle operations fail `BadServerTooBusy` before effects;
callbacks must not synchronously wait on a mutation excluded by their invocation.

| Requested atomicity | Initial publication unit |
| --- | --- |
| PerResource | One selected activation Resource |
| PerGroup | Selected work in one group |
| PerClosure | One dependency closure |
| PerRegistry | All selected/required activation work |

Dependencies, shared declarations, grouping cycles, and affected old/new closures
can coarsen these boundaries. Disabled definition inputs are not activation
members; unrelated registry rows are not added to activation. Semantic reference
cycles can coactivate, but inheritance/order cycles fail. Replacements and
affected retiring owners publish in the same batch.

Independent units commit independently; no-op/dry-run units allocate no generation.
Result rows describe the unit that changed them, including removal-only work.
`LastRefreshPlan` records RequestId, PreparationGeneration, requested/applied
atomicity, and UnitCount before the first switch of an actual invocation.
Read-only capture does not publish it. A stale nonzero `ExpectedGeneration`
fails `BadInvalidState`; the guard is the committed refresh generation, not the
last completion summary.

## Provider requirements

`LifecycleWotProjectionHost` and `LifecycleWotViewProjectionHost` require the same
actual `INodeManagerBatchLifecycle`. The default lifecycle and file registry with
genuine immutable-content leases support the four modes. Immediate-only custom
hosts and `InMemoryWotRegistryStore` do not provide Refresh publication;
unsupported capabilities fail `BadNotSupported` before acquisition/effects.
Storage and read-only capture remain available independently.

The default immutable lease requires `LocalFileSystem` on Windows, whose kernel
sharing protects bytes and key mappings against write/replacement/deletion.
POSIX advisory sharing, arbitrary `IFileSystem` implementations, copied buffers,
and unrelated snapshot capabilities are not equivalent guarantees. Other
deployments must inject a genuine immutable version/key lease provider.
Custom source hosts bind registry read images before the deciding commit;
topology-free observations use `PrepareReadImagesAsync` without source reload.
Custom View owners expose `IWotCanonicalViewReadImage` for captured native reads.

## Dependency capture and observations

Selection resolves Kind and exact Resource/Version from metadata before body
acquisition. Empty selectors select enabled committed content; unmatched nonempty
selectors do no work. `IncludeDependents` adds reverse dependencies. Conflicting
exact Versions fail before fetch when known; URI suffixes do not establish identity.
Acquisition failures stay with the affected Resource/closure. Contentless inputs
remain unresolved, not fabricated digest-pinned definitions.

```csharp
using WotRefreshCapture capture = await captureProvider.CaptureAsync(request, ct);
WotCapturedRefreshRequest invocation = capture.Request;
ArrayOf<WotSelectedResource> selected = capture.Inputs.Selection;
```

DI resolves `IWotRefreshCaptureProvider` to the coordinator. Capture copies the
request before yielding, retains exact bytes/Version leases, and performs no
conversion, activation, retirement, or publication. `GetContent` checks the
captured incarnation/epoch/digest without reacquiring a new default.
`GetRegistryInputDigest` covers registry inputs, not uncaptured external artifacts;
`CreateRefreshPlan` observes the owner's applied boundary rather than choosing it.

`WotDependencySnapshot` retains exact source Version, request/time/generation,
effective input digest, edges, targets, and authoritative origins.
`DependencySnapshot` commits with successful activation; `LastDependencyAttempt`
records a completed actual attempt. Failure/no-op retains the committed graph;
dry runs publish neither. Incomplete preparation has no complete effective digest.
Native reads require a verified `WotRegistryOrigin`; the hosted NodeManager
supplies ApplicationUri, registry root, and exact Version NodeIds. Unconfigured
direct/native reads report `BadNotSupported`. OriginRegistry/VersionNodeId and
DocumentUri are authorities/identities, not execution-partition selectors.

Dependency indexes live in Version metadata and are hydrated from exact old
content. Prepared initialization persists derived indexes with full validation
without changing content, epochs, selections, or committed refresh generation.
Shared graph storage is described in
[manifest schema](WoTConnectivity.md#persistence-and-manifest-schema).

## Version leases

`WotRegistryService` implements `IWotRegistryVersionLeaseProvider`, aliased by DI
to the same singleton. A lease prevents automatic removal of an exact Version
incarnation independently of active/default/desired pointers:

```csharp
IWotRegistryVersionLeaseProvider leases = registry;
using IWotRegistryVersionLease lease = await leases.AcquireVersionLeaseAsync(
    groupId, resourceId, version, ct);
ByteString document = await registry.ReadContentAsync(lease.Version, ct);
```

Acquisition shares the mutation owner with retention/publication. Stale/deleted
incarnations fail `BadInvalidState`/`BadNodeIdUnknown`; delete/recreate cannot
reconnect an old lease. Same-owner reload preserves surviving incarnations.
Native file handles retain their own Version/cursor/writer reservation through
in-flight reads and dirty Close; failed Open, cancellation, and Session abandonment
release only their reservations. Async file dispatch acquires leases without
sync-over-async; lease-aware synchronous Open reports `BadNotSupported`.

A leased unselected Version still counts toward retention limits. Releasing the
last lease makes it eligible for normal retention, not immediate deletion.
Leases change no snapshot/epoch/event, are not persisted, and do not protect
against explicit deletion, content writes, or independent distributed owners.
Immutable-byte leasing and writer exclusion are separate contracts.

## Prepared registry store

`IWotRegistryPreparedStore` retains validated generation/content evidence so
projection-metadata commits need not reread independent blobs. The service
discovers the capability on its injected store and retains it through metadata
generations. Reload through `WotRegistryService.InitializeAsync`; calling
`LoadAsync` on its live store invalidates existing captures.

| Member | Contract |
| --- | --- |
| `SupportsPreparedCommits` | Actual content provider's immutable-lease capability |
| `CaptureValidatedGenerationAsync` | Store-owned validated manifest/content capture |
| `PrepareCommitAsync` | Validate intended snapshot against that capture, without publication |
| `IWotRegistryPreparedCommit.CommitAsync` | Recheck the manifest and make one durable decision |

Only `ApplyProjectionResultsAsync` selects `ProjectionMetadata` scope; a
`ProjectionOnly` notification alone does not. That scope preserves identities,
membership, content, labels, dependency-source metadata, and epochs while allowing
observations/graphs/committed refresh state. Other mutations use full validation.
Foreign, disposed, reload-invalidated, stale, or same-generation changed-manifest
captures fail. Prepared owners are single-use; disposal before decision aborts,
and after decision does not roll back. Direct consumers acquire the next validated
generation before disposing a successful prepared owner.

`IWotRegistryContentLeaseProvider` must belong to the real resource-store owner.
Its lease protects immutable bytes and their ResourceKey association; validation
hashes those bytes, not trusted digest/length/timestamp claims. Decorators forward
only genuine underlying capabilities. `WotBlobResourceStore` fixes its root at
construction. Sharing bytes alone does not coordinate distributed publication.

| Commit outcome | Required handling |
| --- | --- |
| `WotRegistryCommitNotCommittedException` | Keep the old image |
| `WotRegistryCommitDurabilityUncertainException` | Use `CommittedSnapshot` and surface the warning |
| `WotRegistryCommitIndeterminateException` | Fence mutation and recover authoritative state |

Validated-generation handoff precedes `Changed` observers. Observer errors remain
in `PersistenceFailure`; exact-Version warnings retain `ValidatedVersionXid`.
Pending validation intents release once only when recovery proves commitment.
Post-decision completion ignores caller cancellation; read-only preparation
creates no storage root, and pristine rollback releases leases before cleanup.

## Canonical Views

`WotCommittedPublicationState` pairs registry, canonical graph, and active plans;
construction does not publish it. `IWotPreparedViewProjectionHost.PrepareAsync`
prepares the complete remaining graph, including affected ancestors/shared children.
Its publication contributes ordered lifecycle `Changes`, binds complete graph
bytes/handles/`AffectedResourceXids`, and performs non-failing `OnPublished`
bookkeeping without I/O or a second decision. Disposal discards unpublished state.
DI aliases this capability to the same View host, not a replacement provider.

Authored portable View IDs must already resolve in the captured source namespace
table; default roots use Resource-relative `/View` identities. Ownership checks
use actual source partitions/roles, not prefixes or a predecessor belonging to
another registration. Source/View `HasWoTProjection` correlations switch together.
Views/groups have `EventNotifier.None` and own no binding runtime.

`CanonicalViewGraphState` is the root immutable graph carrier, not independent
per-Resource authority. `WithPublicationState` preserves it for default/null
`ByteString`, clears it for explicit `ByteString.Empty`, and retains token/allocation
history even without live Views. `ViewVersion` hashes membership as described in
[projection Views](WoTConnectivity.md#124-projection-documents-and-the-view-nodeclass);
the full `ProjectionMembershipDigest` is 32 bytes.
Native membership/version/root/count/load/refresh fields read from the same
captured image as routing; an inactive captured image does not fall through to
newer state.

Projection inputs retain source context/location/provenance; host routing owns
forms/security/URI variables, not source DataTypes. Supporting definitions share
bounded document/node/byte/depth budgets. Security names are qualified by source
(`q:s:<B64u(sourceName)>:<B64u(schemeName)>`) or host (`q:p:<B64u(schemeName)>`);
combos must be complete/acyclic and cannot confer host credentials on source origins.
Literal instance values/opaque metadata are not searched for references.

## Dry-run validation

`IWotProjectionValidationPublication` prepares private source/View candidates,
mapping/routing/type/reference checks, and registry read images, then disposes
them without a durable decision. Earlier private prerequisites can support later
units without becoming live. Rejected retirements retain serving metadata.
No observations, LastRefreshPlan/completion caches, generations, View tokens, or
management/model-change events change. Predicted roots in unregistered namespaces
are `NodeId.Null`; counts remain predictions. Custom hosts without private
validation report failure, not an unsupported successful preview.

## Recovery

Readiness calls `RecoverAsync` after loading the deciding store. Fresh registries
use ordinary materialization; a committed publication, including an empty one,
restores before readiness even with `AutoRefresh = false`. Recovery uses retained
exact committed inputs, not newer defaults/desired Versions or edits under the
same VersionId. Missing/conflicting evidence fails rather than guessing.
Source/View candidates prepare privately; `IWotRecoverableViewProjectionHost`
restores the recorded graph in the common batch. Runtime namespace indexes may
rebase without changing durable identities, graph bytes, or generations.

`IWotRegistryRecoveryStore` retains validation through the switch;
`IWotRegistryRecoveryPublication` prepares the local image. Recovery writes no
manifest/generation and replays no events. Repeated healthy recovery is a no-op.
Direct hosts initialize then call `RecoverAsync`; ordinary Refresh is not cold
recovery merely because the coordinator is new.

For an interrupted warm publication, `IWotRegistryRecoveryResolver` uses the
same owner/store rules. Missing primary plus staging/backup is an error, not a
choice among equal candidates. Mutation remains fenced until routes/read images
and native registry projections acknowledge the authoritative image.
`Publish`/`CompleteAsync` reconcile silently before lifting the fence; failed
acknowledgment retains it for retry. Nested mutation/reload is rejected.
The coordinator retries recovery before a later Refresh after its unresolved
decision/warning. This is local recovery, not cross-replica consensus.

## Bridge deletion and recovery

The optional [1.02 bridge](WoTConnectivity.md#mirroring-assets-into-the-wot-xregistry)
retains the assigned Resource and registry owner. Backing deletion precedes local
provider/graph/file removal. Reject/noncommit retains the asset; committed warning
returns `GoodResultsMayBeIncomplete`; indeterminate outcomes retain it until that
same registry recovers. Retry does not repeat a confirmed deletion.
Post-commit cleanup ignores caller cancellation and blocks replacement uploads
until complete; an already disabled/retired `Retire` is unchanged.

With asset persistence enabled, bounded `.delete-pending` intents retain assigned
identity and the last confirmed generation, not proof of commitment. Startup
restores management/TD state without reconnecting/remirroring; initialize/recover
the same registry and retry DeleteAsset. An uninitialized empty snapshot proves
nothing. Invalid records/missing backing registry fail startup; successful cleanup
removes the TD and recovery record.
