# OPC UA WoT Connectivity (OPC 10100-1)

WoT Connectivity exposes devices described by W3C Thing Descriptions (TDs) in an
OPC UA AddressSpace. Thing Models (TMs) describe reusable types. You can host the
published [WoT-Con 1.02 asset interface](#wot-con-102-compatibility), the preview
registry, or both.

| Guide | Audience |
| --- | --- |
| This page | Developers hosting a server, uploading documents, or using the clients |
| [WoT bindings and conversion](WotBindings.md) | Developers configuring transports, authoring mappings, or adding a binding |
| [WoT design and provider contracts](WoT.md) | Developers integrating custom registry, conversion, storage, or publication providers |
| [Aggregation sample](../samples/WotCon/README.md) | Developers running the two-source example |

## Contents

- [Packages and model versions](#packages-and-model-versions)
- [Hosting WoT-Con 1.02 assets](#hosting-wot-con-102-assets)
- [WoT-Con 1.02 compatibility](#wot-con-102-compatibility)
- [Writing an asset provider](#writing-an-asset-provider)
- [Using the asset client](#using-the-asset-client)
- [Hosting the registry](#hosting-the-registry)
- [Provisioning documents and using the registry client](#provisioning-documents-and-using-the-registry-client)
- [Persistence and manifest schema](#persistence-and-manifest-schema)
- [Refreshing and inspecting dependencies](#refreshing-and-inspecting-dependencies)
- [Authoring projection Views](#authoring-projection-views)
- [Security and input limits](#security-and-input-limits)
- [Support boundaries](#support-boundaries)

## Packages and model versions

| Project | Purpose |
| --- | --- |
| `Opc.Ua.WotCon` | Generated NodeStates, NodeIds, encodeables, and ObjectType client proxies |
| `Opc.Ua.WotCon.Server` | Asset and registry NodeManagers, materialization, and injectable providers |
| `Opc.Ua.WotCon.Client` | `WotConnectivityClient`, `WotAssetClient`, and `WotRegistryClient` |
| `Opc.Ua.WotCon.Bindings` | Protocol planners, codecs, credentials, target mapping, and HTTP/Modbus/OPC UA executors on .NET 8+ |
| `Opc.Ua.WotCon.Bindings.Mqtt` | Optional MQTT executor |

The combined WoT Connectivity model uses namespace
`http://opcfoundation.org/UA/WoT-Con/`, version `1.1`, and publication date
2026-09-21. It corresponds to specification document `1.1-draft8` and incorporates
the unchanged published [WoT-Con 1.02](#wot-con-102-compatibility) (`1.02.0`) model.
The required xRegistry draft model is
`0.7.0`, dated 2026-09-12, in `http://opcfoundation.org/UA/xRegistry/`.
The pinned models contain 349 Connectivity nodes and 131 xRegistry nodes.
Their Core dependency is `1.05.04`, dated 2025-01-08.

Connectivity is generated once from the combined NodeSet2. The
`WotConnection.xml` and `WotConnection.csv`
[WoT-Con 1.02 inputs](#wot-con-102-compatibility) under `Design/`
are reference inputs, not a second generation source. WoT Binding vocabulary
revision `1.1` and protocol-format versions are separate from these model versions.
Generated declarations do not establish runtime support or complete conformance.

## Hosting WoT-Con 1.02 assets

The [WoT-Con 1.02 compatibility](#wot-con-102-compatibility) interface manages
individual assets through `WoTAssetConnectionManagement` and each asset's
`WoTFile`. Register its factory with a `StandardServer`:

```csharp
var options = new WotConnectivityServerOptions
{
    ThingDescriptionStorageFolder = Path.Combine(AppContext.BaseDirectory, "wot-assets")
};
options.Bindings.Add(assetProviderFactory);
options.Discovery = discoveryProvider;
server.NodeManagerFactories.Add(new WotConnectivityNodeManagerFactory(options));
```

For an already running server, add the same factory through
`await server.NodeManagerLifecycle.AddAsync(factory, callerContext: null, ct)`.
The DI hosting equivalent is `AddWotConServer`.

The factory loads the static model through `AddOpcUaWotCon`. Instances use
`http://opcfoundation.org/UA/WoT-Con/Assets/` by default; set
`AssetNamespaceUri` to change it. `WoTFile` has a WoT-model BrowseName and an
asset-namespace NodeId. `WoTAssetConnectionManagement` is organized below Objects.

The upload workflow is:

1. Call `CreateAsset(name)` to allocate the asset and its file.
2. Open `WoTFile` with `Write | EraseExisting`, write UTF-8 TD bytes, and call
   `CloseAndUpdate`.
3. The server validates and prepares the document, connects an accepted
   `IWotAssetProviderFactory`, and publishes property Variables and action Methods.

`CloseAndUpdate` awaits materialization and persistence. Multiple readers may
coexist, but a writer excludes readers and other writers through completion.
Reads require a positive length; seeking beyond the file clamps to its end.
Session closure discards open handles and uncommitted writes.

The original uploaded bytes are authoritative for downloads, persistence, and
registry mirroring, including an accepted UTF-8 BOM and unknown members.
The parsed provider model is not serialized over them. Persistence stages a
complete file before replacement; a write failure is returned to the caller.
Updating an asset removes obsolete interaction subtrees and detaches their
callbacks. Argument nodes belong beneath their action's path.

If a discovery provider is configured, `DiscoverAssets`, `ConnectionTest`, and
`CreateAssetForEndpoint` use it. Each optional operation may return
`BadNotSupported`. Discovery-generated TDs and persisted native documents are
prepared before publishing an owner or connecting a provider. Invalid persisted
documents keep their source files.

### Mirroring assets into the registry

Mirroring is off when `WotConnectivityServerOptions.RegistryBridge` is null.
Assign an `IWotRegistryService` directly, or resolve the registered service with:

```csharp
builder.AddServer(serverOptions)
    .AddWotConServer(options => options.ThingDescriptionStorageFolder = "wot-assets")
    .AddWotRegistryBridge();
```

`AddWotRegistryBridge()` uses the `thingdescriptions` group by default and accepts
a custom group id. Mirroring is independent of asset file persistence, including
`RebuildAsync(..., persistOnSuccess: false, ...)`. The asset retains the registry
and assigned Resource identity returned by upsert.

Initial mirroring and update mirroring are best-effort: a registry failure is
logged without invalidating an otherwise usable asset. **Deletion is different**:
the backing registry decision must succeed before the asset is removed.
A rejected deletion retains the asset; a committed warning reports
`GoodResultsMayBeIncomplete`. See
[bridge deletion and recovery](WoT.md#bridge-deletion-and-recovery) for interrupted
deletions and incomplete local cleanup.

## WoT-Con 1.02 compatibility

The published OPC 10100-1 v1.02 interface (NodeIds `1..172`) is supported and is
not deprecated. Its generated namespace, NodeIds, Method signatures, and client
APIs are preserved. The registry adds capabilities without making the flat asset
list its canonical document store.

`LegacyGroupId` is the existing `WotRegistryServerOptions` property name for the
configured WoT-Con 1.02 group. Use `RegistryBridge` or `AddWotRegistryBridge` to
enable mirroring; hosting both NodeManagers alone does not enable it.

### Existing-type bindings

`WoTAssetFileType.CloseAndUpdate` uses the shared semantic converter for
native/type-bound TDs before connecting a replacement provider or changing the
asset graph. It applies the converted instance to the existing asset owner,
not a second executing root.

A namespace-qualified ObjectType in `@type` and a `ua:HasTypeDefinition` link
use the same loaded type context. Missing, ambiguous, conflicting, or wrong-class
types fail instead of falling back. A name in a held namespace is a binding even
when lookup fails; ordinary annotations in unloaded namespaces stay annotations.
Property, action, and event scopes use their own effective contexts.

Bound declarations retain their QName, ReferenceType, VariableType, DataType,
ValueRank, and ArrayDimensions. An unqualified member may populate a uniquely
named declaration; an explicit qualified name is never replaced by a name guess.
Mandatory Variables come from native conversion. Converted argument Properties
retain native types and ranks. Variables also carry the WoT-Con 1.02
`HasWoTComponent` relation.

The existing asset identity and placement stay fixed. Authored root identities
must agree with that owner, and instance Nodes must belong to its owned asset
namespace. Conflicting parents or unsupported cross-owner publication fail before
provider effects. Failed admission leaves the previous provider, graph, and document
available. Reference cleanup removes only the edges acquired by that generation;
this is not whole-asset graph/file atomicity.

The default `WotNodeSetDocumentConverter` receives the running AddressSpace
context automatically. To supply a converter directly:

```csharp
options.DocumentConverter = documentConverter;
```

For DI:

```csharp
services.AddSingleton<IWotDocumentConverter>(documentConverter);
services.AddOpcUa().AddWotConServer(options =>
{
    options.ThingDescriptionStorageFolder = storageFolder;
    options.Bindings.Add(assetProviderFactory);
});
```

An options-supplied converter takes precedence over the DI converter on this
asset path. A custom converter returns the complete authoritative graph,
selected root, and resolved affordances, or an explicit failure.
Native events and declarations outside the owned namespace require the applicable
provider/ownership capabilities; the primitive asset mapper does not approximate them.

An unbound generated `IWoTAssetState` initializes its own interface type:
do not assume its TypeDefinition is `BaseObjectType`. A bound update installs the
resolved ObjectType; an unbound replacement restores the initialized type.
Unbound mapping supports primitive properties and flat action argument maps.
Unmappable object/null properties read with `BadConfigurationError`; deeper
action schemas use a single `BaseDataType` argument with the schema retained.

## Writing an asset provider

Implement `IWotAssetProvider` for one asset. Its async operations cover property
read/write, subscribe/unsubscribe, action invocation, event subscribe/unsubscribe,
and disposal. `WotPropertyTag.Form` retains the binding-specific form as a
`JsonElement`. A factory advertises `SupportedBindings`, identifies acceptable
`ThingDescription` values through `CanHandle`, and connects through `ConnectAsync`.

Event `data` fields become a non-abstract `BaseEventType` subtype. The asset
becomes a root notifier. `GeneratesEvent` belongs on ObjectTypes, VariableTypes,
or Methods, not an Object instance. Report ordered field values through
`OnWotEvent`, for example:

```csharp
cb(tag, [new Variant(temperature)],
    new LocalizedText("Pump is overheating"), 700, at);
```

The server subscribes once per affordance/generation and controls delivery to
clients; the provider does not maintain per-client subscriptions.
Missing messages use the event name; missing/out-of-range severity uses the
medium fallback. Severity belongs to an occurrence, not default TD metadata.
Skipped invalid or duplicate affordances produce `GoodResultsMayBeIncomplete`
and diagnostics, not an indistinguishable `Good`.

`SimulatedWotAssetProvider` in the test project is a working read/write/observe/
action example. For registry protocol executors rather than this
[WoT-Con 1.02 provider interface](#wot-con-102-compatibility), see
[adding a binding](WotBindings.md#adding-your-own-binding).

## Using the asset client

```csharp
WotConnectivityClient client = await WotConnectivityClient.ForServerAsync(
    session, session.MessageContext.Telemetry, ct);
WotAssetClient asset = await client.CreateAssetAsync("PressureSensor01", ct);
await using FileStream document = File.OpenRead("sensor.td.jsonld");
await asset.UploadThingDescriptionAsync(document, ct);
await foreach (WotAssetVariableEntry property in asset.EnumeratePropertiesAsync(ct))
{
    DataValue value = (await session.ReadValueAsync(property.NodeId, ct))!;
    Console.WriteLine($"{property.BrowseName} = {value.WrappedValue}");
}
await client.DeleteAssetAsync(asset.AssetId, ct);
```

`WotAssetClient` supplies upload/download byte and Stream conveniences.
`FileTypeClient.UploadAsync`, `DownloadAllAsync`, and `DownloadToAsync` reuse the
file primitives; `WoTAssetFileTypeClient.UploadAndUpdateAsync` additionally calls
`CloseAndUpdate`. Non-seekable input streams are supported. Callers own their streams.
These helpers work on FileType instances outside `Server.FileSystem`.

Generated proxies use type-declaration MethodIds, permitted by OPC UA Part 4
§5.12.2.2. On `BadMethodInvalid`, the shared helper resolves the instance Method,
caches it, and retries once for servers that require that spelling.
This interoperability retry is distinct from protocol action execution, which
does not retry another form or source.

<a id="111-architecture"></a>

## Hosting the registry

The stable `WotRegistryNodeManager` exposes `WoTRegistry`, groups, logical
Resources, exact Versions, settings, and management Methods. Converted document
closures have separate runtime NodeManagers.

```csharp
builder
    .AddServer(server => { /* application server configuration */ })
    .AddWotRegistryServer(options =>
    {
        options.StorageFolder = Path.Combine(AppContext.BaseDirectory, "wot-registry");
        options.AutoRefresh = true;
        options.StrictBindings = false;
        options.RetirementPolicy = WotProjectionRetirementPolicy.Graceful;
    });
```

Protocol support is opt-in; the server uses `NullWotBinderRegistry` until you
[register planners/executors](WotBindings.md#registering-binders-and-executors).
Strict bindings reject unsupported required forms; non-strict bindings publish
degraded nodes with `BadConfigurationError`.

Startup uses [awaited readiness](NodeManagers.md#awaited-readiness).
Both ordinary hosting and runtime factory registration await persisted
materialization/recovery, including when `AutoRefresh` is false.
Committed publications restore their retained active inputs, not newer desired
Versions, without advancing the committed generation. Readiness failures are
observable and fail startup. A custom host must invoke readiness after
AddressSpace creation and server initialization.
Concurrent native Refresh during startup reports `BadServerTooBusy`.

Runtime source retirement is graceful by default: existing monitored items keep
the outgoing generation until drain. Immediate retirement invalidates affected
items with `BadNodeIdUnknown`; it is rejected for a generation owning durable
monitored items.

Refresh publication requires prepared source and registry providers.
The default lifecycle and a file store backed by genuine immutable-content
leases support all four atomicity modes. The current `InMemoryWotRegistryStore`
supports storage/capture but not Refresh publication. Unsupported owners fail
`BadNotSupported` before effects. See [provider requirements](WoT.md#provider-requirements)
for platform and custom-provider constraints.

## Provisioning documents and using the registry client

Groups are keyed by document Kind and **exact** CatalogUri; Resources by owning
group and exact ThingId/ModelId. URI spelling, case, query, fragment, trailing
slash, and escaped bytes matter. Assigned identifiers are not guessed from names.
TD and TM groups may share a catalogue URI.

`WotRegistryClient` extends the shared `XRegistryClient` and composes generated
proxies. `ForServerAsync` browse-resolves the actual `WoTRegistry` root.
`OpenGroupAsync`/`OpenResourceAsync` discover metadata without provisioning or
activating it. Register the client through `AddWotRegistryClient`.

```csharp
WotRegistryClient registry = await WotRegistryClient.ForServerAsync(
    session, session.MessageContext.Telemetry, ct);
(WotRegistryGroupClient group, _) = await registry.GetOrCreateDocumentGroupAsync(
    WoTDocumentKindEnum.ThingDescription, "https://contoso.org/plant/things/", ct);
WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
    "urn:plant:sensor01", requestFileOpen: true, ct: ct);
ByteString document = ByteString.From(await File.ReadAllBytesAsync("sensor01.td.json", ct));
await allocation.Version.Proxy.WriteAsync(allocation.FileHandle, document, ct);
await allocation.Version.Proxy.CloseAsync(allocation.FileHandle, ct);
WotRegistryRefreshResult refresh = await registry.RefreshAllAsync(ct: ct);
refresh.EnsureSuccess();
```

The uploaded `id` must retain the exact supplied ThingId. Typed provisioning
returns distinct logical Resource and exact Version NodeIds and assigned IDs.
GetOrCreate also reports independent CreatedResource/CreatedVersion flags.
An empty VersionId allocates the next Version for Create and selects the default
for GetOrCreate. Allocating a Version does not select it as default.

`requestFileOpen: true` prepares a session-owned preserving write handle before
durable allocation. Initial bytes equal committed bytes and position is zero;
it requests neither erase nor append. A clean Close changes nothing; a dirty
Close validates source identity/kind. Preparation failure creates no allocation.
Session closure before the commit point rejects the transaction; after commit it
discards the handle without undoing the allocation. Atomic requested-file-open
requires the default transactional service; other providers fail before mutation.

Generic provisioning conveniences require explicit aliases for new authorities:

```csharp
options.IdentityBindings.Groups =
[
    new("models", WoTDocumentKindEnum.ThingModel, "https://contoso.org/models/")
];
options.IdentityBindings.Resources =
[
    new("models", "pump", "https://contoso.org/models/Pump")
];
```

Aliases do not allocate a second entity for an existing authority. Missing or
contradictory authorities and case-colliding assignments are rejected.
Already provisioned generic IDs resolve normally. For custom services,
`IWotTypedRegistryService` is an optional capability, not a replacement owner.

Logical FileType Opens select the current default Version, not the active or
desired Version. Each handle pins its Version, cursor, writer reservation, and
retention lease. A failed v2 activation may leave v1 serving while new logical
downloads return default v2. An existing v1 handle continues reading v1.
`UploadNewVersionAsync`, `DownloadAsync`/`DownloadToAsync`, `ValidateAsync`,
`SetEnabledAsync`, `SetDefaultVersionAsync`, and `DeleteAsync` use the generated
file/document Methods.

`LoadDocumentsAsync` accepts an `ArrayOf<WotRegistryDocument>` and processes TMs
before TDs, preserving order within each kind. Mutation/kind failures throw;
partial refresh results are returned in `WotRegistryBulkLoadResult.Refresh`.

### Labels, deletion, and federation

Registry, group, Version, and Resource Meta labels are ordinal key/value pairs.
`Labels.AddAttribute`/`RemoveAttribute` enforce the addressed owner's epoch and
management policy. Logical `Labels` follows the default Version; `MetaLabels`
belongs to the Resource. `MaxLabelsPerEntity`, `MaxLabelKeyLength`, and
`MaxLabelValueLength` bound labels. Unsafe or fixed-member-colliding keys fail.
Label-only changes reconcile native nodes without scheduling materialization.

`DeletePolicy` governs dependent handling:

| Policy | Result |
| --- | --- |
| `Reject` | Refuses proven dependents or unreadable documents that prevent proving safety |
| `Retire` | Retires projections but keeps documents resolvable |
| `Cascade` | Unloads proven dependents; reports and leaves unreadable unrelated documents alone |
| `Force` | Deletes the target and marks dependents/unreadable documents Failed with diagnostics |

The target's own unreadable blob does not block its deletion.
Hosted deletion and disabling use coordinated publication even with automatic
refresh disabled. Logical deletion checks MetaEpoch; exact-Version deletion
checks that Version's Epoch; group operations check the group epoch.

For federation, use an independently authorized remote Session and
`IXRegistryFederationProvider`/`FollowExternalReferenceAsync` with a verified
`XRegistryFederationTarget`. `ResourceUrl` or claimed `OriginRegistry` is not
trust. See [xRegistry federation](XRegistry.md#federation). This does not enable
automatic federated dependency fetching or establish Full-profile conformance.

<a id="112-registry-service-and-persistence"></a>

Bounded consumers can use the stream overload of
`UploadNewVersionAndGetResultAsync` with `maxBytes`; pending structural versions
are claimed before consuming a non-seekable source, without buffering it for
replay. The returned version id and NodeId identify the bytes actually written.
`WotRegistryRefreshResult.MethodStatus` retains the actual method completion
status alongside the summary and per-resource outcomes.

For the legacy file workflow, `UploadAndUpdateWithResultAsync` returns the
consumed byte count and actual `CloseAndUpdate` status, including
`GoodResultsMayBeIncomplete`. `ValidateWithResultAsync` on a generated document
proxy validates that exact node and returns the method status plus the typed
outcome. These result-bearing helpers do not replace completed results with
late cancellation.

## Persistence and manifest schema

`FileWotRegistryStore` persists registry metadata in `manifest.json` and document
bytes in content-addressed blobs. `InMemoryWotRegistryStore` is volatile.
`WotRegistryPersistenceBounds` limits document size, Versions per Resource,
Resources per group, groups, and labels.

**Manifest schema 6 is the format version of the registry's local metadata file.**
It is not the WoT vocabulary version, TD JSON Schema version, or NodeSet model
version. It stores group/resource assignments, exact authorities, Version
metadata and selections, validation observations, and committed publication state.
Developers normally use the registry service rather than edit this file.

`DependencyGraphs` stores each distinct ordered dependency graph once.
Version observations retain separate source Version, request, time, generation,
and fingerprint fields, and reference a graph through `GraphIndex`. Large shared
closures therefore do not duplicate their complete edges for every member.
Invalid, contradictory, or out-of-range references fail loading.

Schema 5 represents the authority/selection metadata with inline dependency
graphs; schemas 3–5 remain readable. Older records without catalogue authority
are explicitly unbound, not inferred from a Name. Configured identity bindings
can establish an unambiguous authority. The next durable commit writes schema 6.
Before downgrading to a binary that cannot read it, preserve a compatible
manifest **and its referenced blobs**. This describes storage compatibility,
not a 1.5-to-2.0 API migration.

<a id="keeping-the-document-bytes-in-a-shared-store"></a>

### Shared document storage

Supply `IXRegistryResourceStore` through DI or options:

```csharp
options.StorageFolder = registryManifestFolder;
options.ResourceStore = new WotBlobResourceStore(sharedDocumentFolder);
```

A DI resource store takes precedence over the options store.
`WotBlobResourceStore` uses digest-named `.bin` content. Writes go to `staging/`;
commit promotes referenced entries to `blobs/` before switching the manifest.
Unpromoted staging entries are not evidence of committed state. Content is
hashed incrementally during validation; a changed/missing blob rejects commit.
A supplied provider owns blob durability. Decorators must forward
`IWotRegistryResourceStoreProvider` so service writes and store validation use
the same content owner.

Sharing document storage alone does not provide distributed metadata/publication
coordination. [Prepared store contracts](WoT.md#prepared-registry-store) explain
immutable leases, commit outcomes, and local platform support.

## Refreshing and inspecting dependencies

Empty selection means enabled Resources with committed content. A nonempty
selection matching nothing does no materialization work. Exact Version selectors
do not silently follow a new default. `IncludeDependents` adds the reverse
dependency closure. Conflicting exact Versions are rejected.

Refresh captures original bytes, contextual references, required definitions,
and Version leases before conversion. Disabled dependencies can supply definitions
without executing. Independent valid units may commit despite a failed unit.
Unchanged inputs/options/binder versions return `Unchanged` without a model change.

```csharp
var selector = new WoTResourceSelectorDataType
{
    Kind = WoTDocumentKindEnum.ThingDescription,
    GroupId = groupId,
    ResourceId = resourceId,
    VersionId = "v1"
};
var (summary, results, generation) = await registry.Proxy.RefreshAsync(
    [selector],
    new WoTRefreshOptionsDataType
    {
        Atomicity = WoTAtomicityEnum.PerClosure,
        IncludeDependents = false
    },
    0,
    "selected-refresh",
    ct);
```

PerResource, PerGroup, PerClosure, and PerRegistry apply to selected/required work,
not unrelated Resources. Dependency safety may coarsen the requested boundary.
Inspect `Summary.Atomicity` and `LastRefreshPlan` for the applied boundary/unit count.
Use `RefreshGeneration` for `ExpectedGeneration`, not the last completion summary.
A stale nonzero generation fails `BadInvalidState`.

`MaxParallelism` is an upper bound; current preparation is serial.
Timeout is invocation-wide in milliseconds, including admission/capture.
Zero disables that budget; negative/non-finite/above-Int32 values are invalid.
Dry runs prepare private candidates but change no diagnostics, plans, generation,
View tokens, or events. Unsupported private validation is a failure, not a preview.

| Native Property | Meaning |
| --- | --- |
| `DependencySnapshot` | Graph committed with successful activation of the exact source Version |
| `LastDependencyAttempt` | Last completed actual resolution/preparation attempt |
| `RefreshGeneration` | Committed materialization generation |
| `LastRefreshSummary` / `LastRefreshTime` | Last completed actual invocation and its end time |
| `LastRefreshPlan` | Final plan of an actual Refresh, published before its first switch |

Observation Properties initially report `BadWaitingForInitialData` where no
observation exists. Logical dependency reads follow the default; exact-Version
reads do not. Authorization also protects disclosure of dependency targets.
Use the session factory/message context to decode generated Structures:

```csharp
DataValue value = await registry.Session.ReadValueAsync(snapshotPropertyId, ct);
if (StatusCode.IsBad(value.StatusCode))
{
    throw new ServiceResultException(value.StatusCode);
}
if (!value.WrappedValue.TryGetStructure<WoTDependencySnapshotDataType>(
    registry.Session.MessageContext, out var snapshot) || snapshot is null)
{
    throw new ServiceResultException(StatusCodes.BadDecodingError);
}
```

Concrete validation/load/binding failure events retain exact Version, phase, and
serving generation. `Validate` records syntax/bounds/admission results on the
addressed Version; unperformed format/compatibility policies report `Skipped`,
not passed. A failed default's validation does not replace the active source.
For direct capture, authoritative origin, and lifetime rules see
[dependency capture](WoT.md#dependency-capture-and-observations).

## Authoring projection Views

A projection plan selects existing affordances rather than defines new ones.
Its format is `WotProjection.Format` (`WoT-Projection/1.1`); use
`WotProjection.ContentType` for the profile media type.
The plan root carries `uav:projection` and a required `uav:projectionKind` of
`ThingDescription` or `ThingModel`, without a `Thing`/`tm:ThingModel` marker.
`uav:scenario` is an absolute IRI; `uav:projects` is a nonempty source manifest.

| Control | Meaning |
| --- | --- |
| `uav:sourceName` | Unique source alias |
| `uav:routing` | `source` by default, or `projection` for host-served forms |
| `uav:sourceDigest` | `sha-256:<hex>` pin of received source bytes |
| `uav:namePrefix` | Prefix for bulk-selected output names |
| `tm:ref` | Enumerated direct property/action/event reference |
| `uav:selectAll` | All source affordances |
| `uav:select` | Closed filters over kind, semantic identifier, and type tokens |

Use the [checked-in sample documents](../samples/WotCon/AggregationClient/Documents)
for complete plans. The authoritative sample manifest has **535 document entries**;
the pinned Pumps NodeSet contains **9,624 Nodes**. These are different inventories,
not a conversion count or a conformance claim.

Enumerated members must carry `tm:ref`. They may annotate presentation/semantics
(`title`, `titles`, `description`, `descriptions`, `@type`, `uav:semanticId`,
`uav:metadata`), not replace source schemas, units, ranges, or enums.
Only projection routing permits host `forms`/`security`.
Source routing preserves source forms and credentials. Selectors reject unknown
keys and ambiguous membership rather than returning a guessed partial result.
Selection order is deterministic: source order, enumerated before bulk, then
property/action/event kind, then output/source names in Unicode code-point order.

Relative source references retain their owning document locations and contexts.
Required unit, Condition, schema, DataType, URI-variable, and security dependencies
are carried without selecting unrelated executable affordances.
No URI in a document grants permission for network retrieval.
Context resets and source-local prefix definitions remain effective.

For bulk/supporting projection-routed forms, supply the host's real forms:

```csharp
services.AddSingleton<IWotProjectionFormProvider>(applicationHostForms);
services.AddOpcUa().AddWotRegistryServer();
```

Direct conversion uses `WotNodeSetConverterOptions.ProjectionFormProvider`.
The DI provider wins over the registry option; explicitly registered converter
options retain their own provider. Enumerated host-routed selections must author
their forms, not rely on this fallback. Provider forms stay in the host origin;
host security definitions do not confer credentials on copied source endpoints.
The interface describes endpoints; it does not publish them or invalidate
registry generations when provider output changes.

Earlier draft plans mixing `uav:projection` and one TD/TM marker require explicit
`ProjectionCompatibilityMode = WotProjectionCompatibilityMode.DraftProjection11`
on converter or registry options. Invalid current controls are not repaired by
that mode. Direct upserts must supply plan Format, ContentType, and result Kind;
`DetectProjectionFormat` can classify the role. The registry FileType adapter
uses that classification, not draft compatibility. Asset uploads and ordinary
NodeSet conversion reject unresolved plans.

A materialized View organizes source Nodes without copying/executing affordances.
Its root/count describe the View and organizational Objects, not the source Nodes.
Views/groups have `EventNotifier.None`; subscribe to the source notifier for events.
Missing members are reported as omissions and `Outcome = Warning`.
Nested projections follow provenance to ultimate owned source Nodes.
Source membership requires the actual converted partition, not a root prefix.
See [canonical Views and publication](WoT.md#canonical-views) for identities,
membership digests, ViewVersion, atomic replacement, and recovery.

For TD parent placement, `links` with `rel: "uav:componentOf"` can target a
document's projection root or a loaded portable NodeId. Unresolved placement
fails Projection; browse-path placement and TM-root fallback are not implemented.
This is distinct from [source browse-path addressing](WotBindings.md#portable-browse-path-targets).

## Security and input limits

### Management authorization

Management mutations, outbound discovery, and file writes require role-based
access and a `SignAndEncrypt` channel whether or not the registry is present.
The default `WotManagementAccessPolicy` requires
`WellKnownRole_SecurityAdmin` and rejects anonymous callers.

```csharp
services.AddOpcUa().AddWotConServer(options =>
{
    options.ManagementAccess = new WotManagementAccessPolicy
    {
        RequiredRoleId = ObjectIds.WellKnownRole_ConfigureAdmin,
        MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
        AllowAnonymous = false
    };
});
```

A denied call returns `BadUserAccessDenied`. Context-free internal bootstrap
operations bypass the remote-session check; that is not authorization for a
remote client. Isolated lab overrides do not certify production conformance.
Read-only registry access may separately be allowed over `SecurityMode.None`.

### Asset endpoints and names

`AssetEndpointPolicy` defaults to `http`, `https`, and `opc.tcp`, blocks loopback,
RFC1918/link-local/IPv6 ULA addresses, and bounds operations to 30 seconds.
`AllowedSchemes`, `AllowLoopback`, `AllowPrivateAddresses`, and
`MaxOperationTimeout` configure those gates and timeout. `AllowedHosts` and
`BlockedHosts` configure explicit exceptions/rejections:

```csharp
options.AssetEndpointPolicy.AllowedHosts.Add("10.20.30.40");
```

The validator does not resolve DNS. IP-range enforcement covers literal IPs;
use pinned literals or connector-level policy to avoid DNS-rebinding exposure.
Discovered endpoints receive the same filter. Generated TDs remain untrusted and
must have a nonempty name/title before materialization.
Registry executors use their separate [binding endpoint policy](WotBindings.md#transport-security).

Asset names cannot escape storage or use reserved device names/unsafe prefixes.
Child names reject empty/over-128-character strings, surrounding whitespace,
control/BIDI characters, and `/`, `\`, `.`, `#`, `:`, `!`. Duplicate names are
case-sensitive. Invalid entries are sanitized in logs and skipped with an
incomplete-result status.

### Persisted asset bounds and errors

| `WotConnectivityServerOptions` property | Default |
| --- | --- |
| `MaxThingDescriptionSize` | 1 MiB |
| `MaxPersistedThingDescriptionFiles` | 10,000; nonpositive disables startup loading |
| `MaxThingDescriptionJsonDepth` | 64 |

These bound each uploaded/persisted TD and startup enumeration. Oversized,
malformed, or unreadable files are logged and skipped; cancellation propagates.
Only increase the bounds for controlled inputs.

Remote errors contain mapped statuses and generic operation text, not exception
messages, internal paths, or stack traces. Detailed failures go to telemetry:
`NotSupportedException` maps to `BadNotSupported`, `ArgumentException` to
`BadInvalidArgument`, `IOException` to `BadResourceUnavailable`; other failures
map to `BadInternalError` (management) or `BadCommunicationError` (data plane).
Caller cancellation is not rewritten as a status.

## Support boundaries

The specification's Minimal profile covers WoT-Con 1.02; Registry Server and
Minimal are separate subsets of Full. These names are not blanket implementation
claims. Full TD/TM JSON Schema and compatibility-policy validation are not
supplied by syntax, identity, Binding, or publication checks.

The registry supports EventDriven automatic refresh. Periodic/Scheduled refresh,
cross-replica HA publication, optional SemanticChange conformance, and deployment
disclosure policies require separate implementation/evidence. Generated
`All = 2` is selector-only, not a document kind.
Prepared publication, conversion fidelity, and native event admission do not
close unrelated conformance gaps.

References:

- [OPC 10100-1](https://reference.opcfoundation.org/specs/OPC-10100-1/full)
- [W3C Thing Description 1.1](https://www.w3.org/TR/wot-thing-description11/)
- [WoT Binding Templates](https://w3c.github.io/wot-binding-templates/)
- [WoT design and provider contracts](WoT.md)
