# WoT selected dependencies and exact-Version snapshots

The registry materialization coordinator resolves selection from registry metadata
before acquiring document bodies. It captures exact input Versions, original
bytes, dependency edges and owner-issued retention leases for the operation.
These contracts are separate from store transaction/integrity validation and do
not advertise selected-only backing-store I/O or multi-resource atomic publication.

## Selection and capture

An omitted or empty selection selects enabled resources with committed content.
A nonempty selection that matches nothing performs no materialization work.
Selectors apply `Kind` and exact Resource/Version identity before acquisition;
`IncludeDependents` adds the indexed reverse dependency closure. Conflicting
Versions of one Resource are rejected rather than resolved by a default guess.
An absolute document identity is not matched by an arbitrary resource-name suffix.

The captured graph separates ordinary semantic strongly connected components
from ordering constraints such as inheritance. Reciprocal ordinary references
may co-activate; inheritance cycles remain invalid. Referenced disabled inputs
can supply definitions without acquiring executing ownership. Acquisition failures
remain associated with their intended resources/closures rather than aborting
unrelated selected work before it can be processed.

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
The file store still validates referenced content in its complete intended/current
manifest. A future selective metadata-commit capability must preserve integrity
and reject stale validation context; no such store contract is implied here.

Data-only plans do not request a binding-runtime fluent callback or an arbitrary
default namespace. Plans with binding/declaration work retain the existing
configuration path. This does not infer an authoritative publication partition
for arbitrary multi-model binding work from leaf count or namespace membership.

JSON Schema engine work remains separate. Capturing a graph is not format,
compatibility, security-profile, or whole companion-specification certification.
