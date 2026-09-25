# WoT selected dependencies and exact-Version snapshots

The registry materialization coordinator resolves selection from registry metadata
before acquiring document bodies. It captures exact input Versions, original
bytes, dependency edges and owner-issued retention leases for the operation.
These contracts are separate from store transaction/integrity validation and do
not by themselves advertise selected-only backing-store I/O or multi-resource
atomic publication. The stock prepared-store capability supplies metadata-commit
I/O isolation only when its actual content provider supports immutable leases.

## Selection and capture

An omitted or empty selection selects enabled resources with committed content.
A nonempty selection that matches nothing performs no materialization work.
Selectors apply `Kind` and exact Resource/Version identity before acquisition;
`IncludeDependents` adds the indexed reverse dependency closure. Conflicting
Versions of one Resource are rejected rather than resolved by a default guess.
An absolute document identity is not matched by an arbitrary resource-name suffix.
Known transitive dependency metadata is checked for incompatible exact-Version
requirements before body acquisition. Contentless targets remain unresolved;
their raw edges and acquisition failures are retained without inventing a content
digest pin or aborting independent selected work.

Portable NodeIds remain native identities rather than relative document
locations. A DataType name or NodeId that identifies a stored declaration adds
that declaration's owner to the required inputs. Otherwise it remains a
loaded-AddressSpace or built-in type lookup, not a missing document dependency.
Indexing does not resolve native identities against a document's base; ordinary
relative document references still use their original active base context.

The captured graph separates ordinary semantic strongly connected components
from ordering constraints such as inheritance. Reciprocal ordinary references
may co-activate; inheritance cycles remain invalid. Referenced disabled inputs
can supply definitions without acquiring executing ownership. Acquisition failures
remain associated with their intended resources/closures rather than aborting
unrelated selected work before it can be processed.

## DataType declaration inputs

DataSchemas and StructureFields can refer to a definition by its graph identity,
qualified name or portable NodeId. The index retains graph identities separately
from qualified names and native identities. A graph identity need not resemble
its document's URI. Each lookup uses the carrying element's context; its raw
authored target remains in the dependency edge. Ambiguous stored owners are not
selected arbitrarily.
Conversion uses the declaration owner's context for declaration keys and the
referring element's context for reference keys. A scoped `@context` does not
turn an otherwise reference-only `@id` object into another full definition.
When a definition omits its native identity, indexing and conversion share
`TryDeriveDataTypeNodeId`; an explicit identity does not gain a derived alias.

`uav:dataTypeDefinition` and `uav:fieldDataTypeDefinition` add resolution edges.
Recursive fields are legal. `uav:dataTypeSubtypeOf` preserves its ordering
constraint for graph-identity, name and NodeId forms, including object forms.
True inheritance cycles remain failures. Context-qualified typed links also
contribute their document targets, with or without an optional `uav:refId`.
An acyclic base/derived relationship inside one document does not create a
Resource-level self-cycle; the converter still validates the type hierarchy.
Opaque configuration, JSON literals and native preservation payloads are not
readable declaration indexes.

The stock converter receives complete captured definitions through the optional
`IWotCapturedDataTypeDefinitions` capability on its Thing resolver. Each
`WotDataTypeDefinitionSource` retains its owning document and context throughout
conversion. `WotNodeSetConverter.ReadDataTypeDefinitions` returns borrowed full
definitions, omitting reference-only occurrences. `TrySplitCompactName` provides
the same context-aware name parsing used by conversion and registry indexing.
Captured inputs remain subject to the converter's document, node and byte limits;
supplying them does not authorize another fetch.

The content dictionary passed to `IWotDocumentConverter.ConvertAsync` can also
implement `IWotDocumentConversionContext`. Its declaration inputs carry the
per-conversion emission decision through injected converters and decorators.
Forward the content argument unchanged when delegating conversion. The captured
input image parses each required declaration document once for a given JSON
depth limit, reuses those borrowed documents across publication units, and
disposes them when the capture ends. Converters must not dispose borrowed
documents or retain them beyond that capture.
Publication-unit input views borrow that root cache; completing or aborting one
unit does not dispose documents still available to the remaining units.

An active definition owner emits its own declarations. Resolution-only
definitions are emitted once by an active source in the prepared closure.
Only a readable source can take that emission assignment. Native projection
and envelope restoration preserve their native content and do not consume it.
Admission uses the converter's supported-profile-aware classification: an
unsupported native profile does not suppress usable readable declarations.
Consumers sharing such declarations stay in one publication unit rather than
registering the same type independently. Generated NodeSets declare the
namespaces of their emitted Nodes; merely referencing a namespace does not
claim ownership of it. A definition's `ProjectedSeparately` flag states emission
ownership, not eligibility to execute a disabled Resource.

Dependency indexes are versioned inside the existing Version manifest entries.
An older index is rehydrated from its exact content; an unsupported or incomplete
current index is rejected. Rehydration covers ordinary Versions, the committed
Version and retained resolution-only inputs. It preserves active/runtime
generation and pending default selections, and persists the upgraded metadata
once. Cold recovery uses the retained definition Version even when the current
default supplies a different type identity.

Deletion policy uses the same contextual reference lookup as closure capture,
including the lookup after proposed removals. A compact reference cannot bypass
`Reject` merely because its raw spelling differs from the expanded identity.

## Direct capture

Direct callers can capture the same public input image:

```csharp
var selector = new WoTResourceSelectorDataType
{
    Kind = WoTDocumentKindEnum.ThingDescription,
    GroupId = groupId,
    ResourceId = resourceId,
    VersionId = "v1"
};

using WotMaterializationSnapshot inputs = await WotDependencyGraph.CaptureAsync(
    registry,
    [selector],
    includeDependents: false,
    maxJsonDepth: registry.Bounds.MaxJsonDepth,
    cancellationToken: ct).ConfigureAwait(false);

foreach (WotSelectedResource selected in inputs.Selection)
{
    if (selected.Resource.Enabled && selected.Version is { HasContent: true } version)
    {
        ByteString originalBytes = inputs.GetContent(selected.Resource, version);
        // Consume the captured bytes while this operation owns its Version leases.
    }
}
```

`GetContent` checks the exact captured incarnation, epoch and digest. It never
falls back to a new default Version or a fresh store read. Disposing the snapshot
releases its leases without modifying its captured metadata or bytes.

The existing [Version lease contract](WotRegistryVersionLeases.md) applies across
same-owner reload. Releasing a lease makes an otherwise unselected Version
eligible for normal retention; it does not immediately delete that Version.
Allocating a contentless Version is also not a content commit. Committed-Version
retention runs when the incoming content is committed, while allocation checks
that such a commit could fit without evicting protected inputs.

## Coordinator capture provider

`IWotRefreshCaptureProvider` resolves to the registered
`WotMaterializationCoordinator`, not a second selection owner. Its `CaptureAsync`
copies the request before yielding, captures the same selected input image used
by `RefreshAsync`, and owns the corresponding Version leases:

```csharp
using WotRefreshCapture capture = await captureProvider
    .CaptureAsync(request, ct).ConfigureAwait(false);
WotCapturedRefreshRequest invocation = capture.Request;
ArrayOf<WotSelectedResource> selected = capture.Inputs.Selection;
```

Capture performs no conversion, activation, retirement, or metadata publication.
A stale nonzero `ExpectedGeneration` fails before body acquisition. Returned
selectors and `CreateRefreshPlan` values are independent copies; caller mutation
cannot change the captured invocation. `GetRegistryInputDigest` fingerprints
captured registry inputs, not uncaptured external artifacts. `CreateRefreshPlan`
uses the publication owner's actual applied atomicity and unit count; it neither
chooses units nor publishes `LastRefreshPlan`. Dry runs cannot produce that
publication observation. Final stale-input checks and atomic publication remain
the publication owner's responsibility.

## Owner capability and authoritative origin

The stock `WotRegistryService` implements
`IWotRegistryDependencySnapshotProvider`. Its `SupportsDependencySnapshots`
property promises retention of both observation records in the same owner's
immutable registry state. DI resolves this capability to that registry instance;
an older provider that does not implement it receives an explicit unsupported
capability error, not a replacement owner.

Add the existing registry/client services to the configured OPC UA builder:

```csharp
builder
    .AddWotRegistryServer(options => options.AutoRefresh = false)
    .AddWotRegistryClient();
```

The stock NodeManager supplies the running server's authoritative ApplicationUri
and actual registry-root NodeId. Exact target Version NodeIds use the existing
registry projection mapping. No endpoint label, arbitrary namespace, resource
suffix, or invented origin substitutes for these identities.

A direct coordinator without a NodeManager must receive a verified/configured
`WotRegistryOrigin` explicitly before publishing origin-scoped observations.
`WotMaterializationCoordinator.SupportsDependencySnapshots` also requires the
owner's observation and Version-lease capabilities. Unconfigured/unsupported
native reads report `BadNotSupported`; they do not fabricate an initial graph.

## Native Properties and generated Refresh

The existing generated `WoTDocumentType` Properties are used without changing
their NodeIds or the `Refresh` Method signature:

| Property | Meaning |
| --- | --- |
| `DependencySnapshot` | The graph associated with successful activation of the exact source Version. Failure or no-op does not replace it. |
| `LastDependencyAttempt` | The latest completed actual resolution/preparation attempt, independently of the committed graph. An incomplete failed preparation has no complete effective-input digest. |

Both initially return `BadWaitingForInitialData`. A dry run changes neither.
Logical Resource reads delegate to the current default Version; an exact-Version
read does not follow a later default change. The existing management/read
authorization boundary also protects dependency-target disclosure.

Snapshots retain the raw authored target URI while publishing the exact target
Version Xid, authoritative origin, optional portable Version NodeId and content
digest. The optional `OriginRegistry` field is emitted with its generated presence
mask; non-registry targets do not receive a fabricated registry origin.

The stock client registers the generated xRegistry/WoT encodeable activators in
its session context. Generated Method output and Property Structures therefore
use the existing typed decoding path without reflection-based model registration:

```csharp
WotRegistryClient client = await WotRegistryClient.ForServerAsync(
    session, telemetry, ct).ConfigureAwait(false);

var (summary, results, generation) = await client.Proxy.RefreshAsync(
    [selector],
    new WoTRefreshOptionsDataType
    {
        Atomicity = WoTAtomicityEnum.PerClosure,
        IncludeDependents = false
    },
    0,
    "selected-refresh",
    ct).ConfigureAwait(false);
```

Read the existing `DependencySnapshot` Property NodeId browsed from the exact
Version, then decode its value using the same session message context:

```csharp
DataValue value = await client.Session.ReadValueAsync(
    snapshotPropertyId, ct).ConfigureAwait(false);
if (StatusCode.IsBad(value.StatusCode))
{
    throw new ServiceResultException(value.StatusCode);
}

WoTDependencySnapshotDataType? snapshot;
if (!value.WrappedValue.TryGetStructure<WoTDependencySnapshotDataType>(
    client.Session.MessageContext, out snapshot) || snapshot is null)
{
    throw new ServiceResultException(
        StatusCodes.BadDecodingError, "Unexpected dependency snapshot value.");
}
```

## Boundaries

Selection bounds materialization acquisition, planning, retirement and activation.
`IWotRegistryService.ApplyProjectionResultsAsync` notification scoping is **not**
a guarantee that `IWotRegistryStore.CommitAsync` reads only selected blobs.
The [prepared store](WotRegistryPreparedStore.md) retains previously validated
immutable content evidence and checks manifest identity and expected generation
before projection-only publication. Its allow-list includes committed dependency
snapshots and last-attempt observations, not changes to authoritative content,
dependency-source metadata, Version membership, labels, or entity epochs.
Providers without genuine immutable content leases retain full validation; a
snapshot capability or read-counting decorator cannot fabricate that guarantee.
When a prepared-capable store loads a pre-index manifest, initialization commits
the derived dependency metadata through the full validated mutation path before
using it as a projection-only baseline. Content bytes and entity epochs are not
rewritten. Legacy providers retain their existing in-memory hydration behavior.

The stock binding runtime skips data-only plans that need no fluent callback or
default namespace. Custom factories still receive every generation's configuration
callback, including failure/cancellation hooks. Plans with binding/declaration
work retain the configuration path. This does not infer an authoritative publication partition
for arbitrary multi-model binding work from leaf count or namespace membership.

JSON Schema engine work remains separate. Capturing a graph is not format,
compatibility, security-profile, or whole companion-specification certification.
