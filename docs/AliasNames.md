# OPC UA Part 17 — Alias Names

OPC UA Part 17 defines a hierarchy of human-readable **alias names**.
Each alias uses a non-hierarchical reference to point to one or more
nodes. Clients can search the hierarchy with wildcard patterns and
resolve aliases without knowing target NodeIds in advance. This pattern
suits tag-naming schemes (PI / SCADA / DCS), pub/sub topic registries,
MES integration, and other scenarios where people choose names for
machine-addressed nodes.

This stack ships full Part 17 support in **`Opc.Ua.Server`** (server
side) and **`Opc.Ua.Client`** (client side). The implementation covers:

| Spec section | Type / Method                            | Status |
| ------------ | ---------------------------------------- | ------ |
| §6.2         | `AliasNameType` (browsable instance nodes) | ✔ opt-in — see below |
| §6.3.1       | `AliasNameCategoryType` (incl. nesting)  | ✔ (nested instances opt-in — see below) |
| §6.3.1       | `LastChange` (`VersionTime`)             | ✔      |
| §6.3.2       | `FindAlias`                              | ✔      |
| §6.3.3       | `FindAliasVerbose`                       | ✔      |
| §6.3.4       | `AddAliasesToCategory`                   | ✔      |
| §6.3.5       | `DeleteAliasesFromCategory`              | ✔      |
| §7.2         | `AliasNameDataType`                      | ✔      |
| §7.3         | `AliasNameVerboseDataType`               | ✔      |
| §8.2         | `AliasFor` reference type                | ✔      |
| §9.2         | Well-known `Aliases (i=23470)`           | ✔ wired; optional methods opt-in — see below |
| §9.3         | Well-known `TagVariables (i=23479)`      | ✔ wired; optional methods opt-in — see below |
| §9.4         | Well-known `Topics (i=23488)`            | ✔ wired; optional methods opt-in — see below |
| Annex D      | PubSub replication (LastChange notifications) | ✔ transport-agnostic — see below |

## Contents

- [Server side](#server-side--opcuaserveraliasnames)
  - [Quick start — serving standard categories](#quick-start--serving-standard-categories)
  - [Quick start — application-defined categories](#quick-start--application-defined-categories)
  - [Browsable alias nodes](#browsable-alias-nodes)
  - [Custom backend](#custom-backend)
- [Client side](#client-side--opcuaclientaliasnames)
  - [`AliasNameResolver`](#aliasnameresolver--cached-aliasnodeid)
- [Spec deviations / wrinkles](#spec-deviations--wrinkles)
- [Annex D — PubSub LastChange notifications](#annex-d--pubsub-lastchange-notifications)
  - [Server-side PubSub](#server-side--opcuaserveraliasnamespubsub)
  - [Client-side PubSub](#client-side--opcuaclientaliasnamespubsub)
- [See also](#see-also)

## Server side — `Opc.Ua.Server.AliasNames`

Search patterns use the shared `LikePattern` parser and matcher, with case-sensitive whole-string
matching. Backslashes escape literal characters both inside and outside
character sets. `[^...]` negates a set; `[!...]` remains accepted for
compatibility. Wildcards match line breaks. Trailing escapes, malformed sets,
descending ranges, and unescaped `^` outside the start of a set return
`BadInvalidArgument` from both FindAlias variants even with an empty
store. All alias matches in one search share a 100 ms deadline; exceeding it
returns `BadTimeout`. The server also checks for request cancellation before
matching the next name.

The server library exposes a pluggable backend (`IAliasNameStore`) plus
a default in-memory implementation. Apps assemble their alias inventory
inside a store, then either:

1. Register the store through `AddAliasNameStore(...)` on the DI server
   builder, or directly with the server-wide `IAliasNameStoreRegistry`,
   so the standard well-known
   `Aliases`/`TagVariables`/`Topics` nodes start dispatching through it,
   **or**
2. Wrap the store in an `AliasNameNodeManager` to expose application-
   defined categories under a custom namespace (with full
   `AddAliasesToCategory` / `DeleteAliasesFromCategory` support).

Both approaches can be combined.

### Quick start — serving standard categories

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Server.AliasNames;

var tagVariables = new AliasNameCategoryDescriptor(
    ObjectIds.TagVariables,
    QualifiedName.From(BrowseNames.TagVariables),
    AliasNameCapabilities.FindAliasVerbose);

var store = new InMemoryAliasNameStore([tagVariables]);
store.Seed(ObjectIds.TagVariables, "ServerCurrentTime",
    new ExpandedNodeId(VariableIds.Server_ServerStatus_CurrentTime),
    serverUri: null,
    referenceTypeId: ReferenceTypeIds.AliasFor);

services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "AliasServer";
        options.EndpointUrls.Add("opc.tcp://localhost:4840/AliasServer");
    })
    .AddAliasNameStore(store)
    .ConfigureAliasNames(options => options.MaterializeAliasNodes = true);
```

`AddAliasNameStore(...)` and `AddAliasNameStoreRegistry(...)` register
their stores before address-space startup, after application
`IServerPreStartupTask` registrations have finished initializing source
registries. The opt-in
`ConfigureAliasNames(Action<AliasNameServerOptions>)` extension is on
`IOpcUaServerBuilder` in `Microsoft.Extensions.DependencyInjection`.
`AliasNameServerOptions` is in `Opc.Ua.Server.AliasNames`, and its
`MaterializeAliasNodes` property defaults to `false`.
`RefreshAliasNodesOnChange` separately defaults to `true`, keeping the
browse view current whenever materialization is enabled.

When enabled, the normal `ConfigurationNodeManager` materializes
registered standard-category aliases and their declared optional
capabilities after the standard nodes have loaded. No server or
diagnostics-node-manager subclass is needed. The same setting applies
to stores for `Topics`; their targets must be `PublishedDataSetType`
instances. Omit `ConfigureAliasNames(...)` when only method-based
lookup is needed.

Initial node materialization retries one timed-out snapshot read before creating
nodes for that root. The retry is logged; each attempt retains the 100 ms search limit.
Repeated timeouts, other store errors, and cancellation still abort startup.
Only initial materialization uses this immediate retry.

When a client calls `Aliases.FindAlias` (`i=23476`),
`TagVariables.FindAlias` (`i=23485`) or `Topics.FindAlias` (`i=23494`),
`DiagnosticsNodeManager`'s late binder routes the call through the
registry to the matching store.

Direct `IAliasNameStoreRegistry` registration remains available for
custom hosting. Register stores before the materialization pass when
their aliases must also be browsable.

### Quick start — application-defined categories

`AliasNameNodeManager` is a `CustomNodeManager2` that owns a namespace
and creates its own category tree from the store's
`RootCategories`. Add it to your server's node-manager list:

```csharp
var myRoot = new AliasNameCategoryDescriptor(
    new NodeId("My/Category", myNamespaceIndex),
    new QualifiedName("My/Category", myNamespaceIndex),
    AliasNameCapabilities.All);            // expose every optional method
var store = new InMemoryAliasNameStore([myRoot]);
nodeManagers.Add(new AliasNameNodeManager(server, configuration, store));
```

Options:

* `NamespaceUri` — controls the namespace under which the manager
  registers its category instances. Defaults to
  `http://opcfoundation.org/UA/AliasName/`. Every category descriptor's
  NodeId must lie in this namespace; descriptors pointing anywhere else
  are skipped with a warning rather than claiming another manager's
  ids.
* `MaterializeAliasNodes` (default `true`, unchanged for
  `AliasNameNodeManagerOptions`) — creates one browsable
  `AliasNameType` node per alias, with `AliasFor` references to its
  targets, exactly as the standard-node materialization below does.
  Disable to expose only the category tree.
* `LinkToStandardAliasesObject` (default `true`) — adds `Organizes`
  external references from the well-known `Aliases (i=23470)` object to
  the manager's root categories so they show up in the standard browse
  tree.
* `RequireSecurityAdminForMutations` (default `true`) — rejects
  `AddAliasesToCategory` / `DeleteAliasesFromCategory` calls from
  unauthenticated users or sessions without the
  `WellKnownRole_SecurityAdmin` role on a `SignAndEncrypt` channel.
* `RegisterWithServerRegistry` (default `true`) — also registers the
  store with `IAliasNameStoreRegistry` so the well-known standard nodes
  see it.

### Browsable alias nodes

Registering a store makes `FindAlias` answer from it, but the aliases
themselves stay inside the store — nothing in the address space shows
them. Part 17 §6.2 clients (the OPC Foundation CTT among them) also
*browse* for aliases, so a server under conformance test needs the
alias hierarchy materialized as real nodes.

Both materialization paths — `AliasNameNodeManager` for
application-defined namespaces and
`DiagnosticsNodeManager.MaterializeRegisteredAliasNameNodesAsync` for
the standard well-known nodes — run the same shared walker
(`AliasNameNodeMaterializer`), so they produce structurally identical
Part 17 trees and every fix lands in both at once.

For the standard DI server, `ConfigureAliasNames(options =>
options.MaterializeAliasNodes = true)` makes the normal
`ConfigurationNodeManager` invoke that helper. Its default is `false`,
independent of the custom `AliasNameNodeManagerOptions` default of
`true`. Metadata, resource, and alias configuration use the
`DependencyInjectionStandardServer` hooks; a non-DI custom
`AddServer<TServer>()` rejects these DI-only settings rather than
silently ignoring them.

`DiagnosticsNodeManager.MaterializeRegisteredAliasNameNodesAsync`
materializes every registered store. For each alias, it creates an
`AliasNameType` instance whose BrowseName is the alias name. It adds
`AliasFor` references to the alias targets and inverse `HasAlias`
references to local targets.

The method also creates an `AliasNameCategoryType` instance for every store
category absent from the standard NodeSet, including nested categories. It
organizes a root category under the standard `Aliases` object so clients can
browse to it. For each remote target, the manager registers its server URI in
the server's `ServerUris` table and uses the matching `ServerIndex`.

The manager moves server-defined BrowseNames in namespace 0, reserved for
OPC Foundation names, to the diagnostics namespace. Part 17 clients compare
alias names without considering the namespace, so this change is transparent.
The pass is idempotent and creates category nodes only when their NodeId lies
in the diagnostics namespace. If a descriptor points elsewhere, the manager
skips that category with a warning rather than claiming IDs owned by another
manager or the standard NodeSet.
Custom diagnostics-node-manager implementations can still call the
helper from an overridden `CreateAddressSpaceAsync`, after `base` has
loaded the standard categories:

```csharp
public override async ValueTask CreateAddressSpaceAsync(
    IDictionary<NodeId, IList<IReference>> externalReferences,
    CancellationToken cancellationToken = default)
{
    await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
        .ConfigureAwait(false);

    await MaterializeRegisteredAliasNameNodesAsync(
        externalReferences, cancellationToken).ConfigureAwait(false);
}
```

The same pass also creates the **optional Part 17 members**
`FindAliasVerbose`, `AddAliasesToCategory`, `DeleteAliasesFromCategory`,
and `LastChange` for each category whose descriptor declares them through
`AliasNameCapabilities`. This includes well-known categories for which the
standard NodeSet does not declare these members:

```csharp
var tagVariables = new AliasNameCategoryDescriptor(
    ObjectIds.TagVariables,
    QualifiedName.From(BrowseNames.TagVariables),
    AliasNameCapabilities.All);       // verbose + add + delete + LastChange
```

Mutation calls are gated on a `SecurityAdmin` caller over a
`SignAndEncrypt` channel and return `BadUserAccessDenied` otherwise.

Standard-category materialization is opt-in: servers that only need
`FindAlias` to answer from their store do not create alias instance
nodes. Once materialized, both standard and application-defined alias
nodes follow store changes by default. Aliases added or removed through
`AddAliasesToCategory` / `DeleteAliasesFromCategory` update queries,
advance `LastChange`, and schedule reconciliation of `AliasNameType`
nodes and their target references.

`RefreshAliasNodesOnChange` defaults to `true` on both `AliasNameServerOptions`
and `AliasNameNodeManagerOptions`. Set it to `false` explicitly to retain a
startup-only browse snapshot. It never overrides `MaterializeAliasNodes = false`:

```csharp
builder.ConfigureAliasNames(options =>
{
    options.MaterializeAliasNodes = true;
});
```

Each live-materialization host owns one background worker and at most one
pending refresh signal. Category and ancestor notifications coalesce into a
single pass over store roots.

The worker applies a completed query only if its generation is current. If a
query fails, it leaves existing nodes and references intact and logs the
failure. The worker retains unchanged node instances, updates only
changed associations, and applies inverse `HasAlias` references to their
actual local targets. It reads `LastChange` from the current store version,
not a possibly delayed event value, including across counter rollover.

During asynchronous host disposal, the host cancels and drains the worker.
Disposal waits up to five seconds and logs a warning if a provider does not
stop.

The browse view materializes only `AliasFor` associations. If a store holds an
entry under an unrelated reference type, the entry is not a Part 17 §6.2 alias
association. `FindAlias` can return it when the filter matches, but the
materializer does not create an alias node for it. The materializer also
creates entries stored under an `AliasFor` subtype, but uses the base
`AliasFor` reference because the verbose record does not carry the concrete
reference type.

Two things the caller controls:

* Give each category descriptor a BrowseName in a namespace owned by the
  server. The store assigns that namespace to every alias `QualifiedName` it
  reports. The materializer also moves `ns=0` BrowseNames out of the reserved
  OPC Foundation namespace. Using a server-owned namespace keeps query results
  and browsable nodes aligned, so `TranslateBrowsePathsToNodeIds` resolves
  returned alias names. The reference server uses the diagnostics namespace
  for all its descriptors.
* Part 17 §9 requires `Topics` aliases to target `PublishedDataSetType`. The
  store and `AddAliasesToCategory` do not enforce this constraint. If a server
  exposes mutation methods on `Topics`, grant access only to operators who
  preserve it.

`Quickstarts.ReferenceServer` applies these rules in
`ReferenceServerConfigurationNodeManager`. The manager creates
`PublishedDataSet` instances and seeds the `Topics` aliases in the same
component. An alias therefore cannot target a dataset that the manager did
not create. `ReferenceServer.ConfigureAliasNameStore` seeds the nested
`Devices` sub-category under `TagVariables`.

### Custom backend

Implement `IAliasNameStore` to back the alias inventory with your own
storage (DB, file, MES, …). The interface is small:

```csharp
public interface IAliasNameStore
{
    IReadOnlyList<AliasNameCategoryDescriptor> RootCategories { get; }
    event EventHandler<AliasStoreChangedEventArgs>? Changed;

    uint? GetLastChange(NodeId categoryId);
    bool OwnsCategory(NodeId categoryId);

    ValueTask<IReadOnlyList<AliasNameDataType>> FindAliasAsync(...);
    ValueTask<IReadOnlyList<AliasNameVerboseDataType>> FindAliasVerboseAsync(...);
    ValueTask<StatusCode[]> AddAliasesAsync(...);
    ValueTask<StatusCode[]> DeleteAliasesAsync(...);
}
```

The reference `InMemoryAliasNameStore` is thread-safe (SemaphoreSlim),
supports nested categories and emits `Changed` events that bubble up to
the address-space `LastChange` property. A mutation on a nested
category bumps — and notifies for — every ancestor category as well,
per Part 17 §6.3.1/§9.2: a category's `LastChange` reflects the most
recent change anywhere in its subtree, and the root `Aliases` value
reflects any change at all.

## Client side — `Opc.Ua.Client.AliasNames`

The client library provides a high-level `AliasNameClient` plus a
caching `AliasNameResolver`.

```csharp
using Opc.Ua.Client.AliasNames;

// Standard categories have hardcoded method NodeIds so the first call
// is one round-trip — no extra TranslateBrowsePaths probe needed.
AliasNameClient client = AliasNameClient.OpenStandardTagVariables(session);

IReadOnlyList<AliasNameDataType> result =
    await client.FindAliasAsync("TIC%", referenceTypeFilter: null, ct);
```

`AliasNameClient` exposes the full Part 17 method surface:

* `FindAliasAsync(pattern, referenceTypeFilter, ct)`
* `FindAliasVerboseAsync(...)` — throws `NotSupportedException` when the
  category does not expose the optional method.
* `AddAliasesToCategoryAsync(IEnumerable<AliasNameAddRequest>, ct)`
* `DeleteAliasesFromCategoryAsync(IEnumerable<AliasNameDeleteRequest>, ct)`
* `EnumerateSubCategoriesAsync(ct)` — `IAsyncEnumerable` of child
  `AliasNameSubCategoryInfo`, including children whose type is a
  subtype of `AliasNameCategoryType`.
* `ReadLastChangeAsync(ct)` — returns the `VersionTime` (or `null` when
  the category does not expose `LastChange`).

Per-call errors map to typed exceptions:

| Status code                | Exception                           |
| -------------------------- | ----------------------------------- |
| `BadUserAccessDenied`      | `UnauthorizedAccessException`       |
| `BadNotSupported`          | `NotSupportedException`             |
| `BadNotImplemented`        | `NotSupportedException`             |
| other `BadXxx`             | `ServiceResultException`            |

### `AliasNameResolver` — cached alias→NodeId

```csharp
await using var resolver = new AliasNameResolver(
    AliasNameClient.OpenStandardTagVariables(session));

IReadOnlyList<ExpandedNodeId> targets =
    await resolver.ResolveAsync("TIC101_Setpoint", ct);

string aliasName = await resolver.ResolveAliasNameAsync(targets[0], ct);
```

Default refresh mode is `Manual` — callers invoke `RefreshAsync`
(or rely on lazy-load via `ResolveAsync`). Opt in to automatic cache
invalidation via one of:

| `AliasNameResolverRefreshMode`     | Strategy                                  | When to use |
| ---------------------------------- | ----------------------------------------- | ----------- |
| `Manual` (default)                 | `ManualAliasNameRefreshStrategy`          | Caller drives refresh explicitly. Safe everywhere. |
| `AutoOnLastChangePolling`          | `PollingAliasNameRefreshStrategy`         | Server lacks Subscriptions or you want a fixed `Read` cadence. |
| `AutoOnLastChangeMonitoredItem`    | `MonitoredItemAliasNameRefreshStrategy`   | Server supports Subscriptions. Push-based — no `Read` per interval. |

Custom strategies (e.g. the Annex D PubSub bridge in
`Opc.Ua.Client.AliasNames.PubSub` — see Annex D below) plug in via:

```csharp
new AliasNameResolverOptions
{
    RefreshStrategy = new MyCustomStrategy()  // takes precedence over RefreshMode
}
```

The `IAliasNameRefreshStrategy` contract is tiny:

```csharp
public interface IAliasNameRefreshStrategy : IAsyncDisposable
{
    ValueTask StartAsync(AliasNameClient client, Action onInvalidate, CancellationToken ct);
}
```

Implementations watch for stale-cache triggers and invoke `onInvalidate`
when they detect a change. `MonitoredItemAliasNameRefreshStrategyOptions`
controls subscription ownership. By default, the strategy creates and
deletes the underlying `Subscription`. Set `SharedSubscription` to use
an externally managed subscription for the monitored item.

Disposing the resolver (`await using` / `DisposeAsync`) tears down the
strategy: timer for polling, `MonitoredItem` + `Subscription` for the
monitored-item variant. Disposal is idempotent and never throws.

## Spec deviations / wrinkles

* **`AliasNameDataType.ReferencedNodes`** — the wire format defines this
  as `ExpandedNodeId[]` (NodeSet `i=18`), not `NodeId[]`. The
  source-generated `AliasNameDataType` is correct; the historical
  Quickstart sample used `NodeId[]` and has been removed.
* **Standard well-known nodes** — the OPC UA NodeSet instantiates only
  `FindAlias` on `Aliases`/`TagVariables`/`Topics` (plus `LastChange` on
  `Aliases`), so the always-on binder wires just those. The optional
  methods are added by `MaterializeRegisteredAliasNameNodesAsync`
  through the generated `AddFindAliasVerbose` /
  `AddAddAliasesToCategory` / `AddDeleteAliasesFromCategory` optional-
  child helpers, for each category whose descriptor declares the
  matching `AliasNameCapabilities`. Each child is created at the NodeId
  the OPC Foundation reserves for it (`Aliases.FindAliasVerbose` =
  `i=24054`, `TagVariables.LastChange` = `i=32854`, and so on).

  The standard identifier registry (`StandardTypes.csv`) allocates these
  identifiers, but the ModelDesign does not declare the optional children
  and the published NodeSet does not include them. The standard address
  space therefore does not instantiate them.

  A reserved ID is available if a server chooses to expose a node; it does
  not require the server to create that node. The source generator therefore
  emits no parent-to-instance mapping or `MethodIds` constant for these
  children. `DiagnosticsNodeManager.ReservedChildIds` supplies nine method
  IDs and the two missing `LastChange` IDs explicitly, each traceable to its
  registry row. The argument properties have generated `VariableIds`
  constants, which the implementation uses to reference them.

  Do not add these children to `StandardTypes.xml`. That file copies the
  OPC Foundation ModelDesign verbatim and is synchronized from upstream.
  A local edit would be lost on the next sync, and the NodeIds would revert
  to factory-minted values.
* **`AliasNameCapabilities.AddAliasesToCategory` /
  `DeleteAliasesFromCategory`** — on an `AliasNameNodeManager` this
  defaults to `SecurityAdmin`-only via
  `AliasNameNodeManagerOptions.RequireSecurityAdminForMutations`; on the
  standard well-known nodes the same check is always applied and cannot
  be opted out of. It requires both the role grant AND a
  `SignAndEncrypt` channel.
* **`ReferenceTypeFilter` semantics** — null/empty and
  `ReferenceTypeIds.References` match every alias regardless of
  reference type. Otherwise matches are limited to aliases whose
  reference type is, or is a subtype of, the filter (using
  `Server.TypeTree.IsTypeOf`).

## Annex D — PubSub LastChange notifications

Part 17 Annex D defines a lightweight PubSub schema for alias-change
notifications between servers. The schema carries only each category's
current `LastChange` value (a `VersionTime`/`uint`) — subscribers learn
that a publisher's category changed, then refetch alias contents via
`FindAlias`/`FindAliasVerbose` if needed.

The data types (already emitted by the source generator):

| NodeId   | Type                          | Fields |
| -------- | ----------------------------- | ------ |
| `i=24052` | `AliasCategoryUpdateDataType` | `Category : PortableNodeId`, `LastChange : VersionTime` |
| `i=24053` | `AliasUpdateDataType`         | `ApplicationUri : string`, `Categories : AliasCategoryUpdateDataType[]` |

### Server side — `Opc.Ua.Server.AliasNames.PubSub`

The server library exposes a transport-agnostic publisher that emits
fully-built `AliasUpdateDataType` messages whenever an
`IAliasNameStoreRegistry`-tracked store changes:

```csharp
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.AliasNames.PubSub;

// inside server startup (after the alias store is registered):
var resolver = new ServerPortableNodeIdResolver(server);
var publisher = new AliasNamePublisher(
    registry: ((IAliasNameStoreRegistryProvider)server).AliasNameStoreRegistry,
    portableResolver: resolver,
    applicationUri: configuration.ApplicationUri);

publisher.AliasUpdateProduced += (_, e) =>
{
    // Hand `e.Update` to your transport — e.g. publish a DataSetMessage
    // through Opc.Ua.PubSub.UaPubSubApplication with the DataSet
    // built by AliasUpdateDataSetFactory.Create(...).
};
```

Helpers shipped:

* `IPortableNodeIdResolver` + `ServerPortableNodeIdResolver` — converts
  a local `NodeId` into the spec-required `PortableNodeId`
  (NamespaceUri + Identifier with namespace index stripped).
* `AliasUpdateDataSetFactory.Create(dataSetClassId)` — builds the
  fixed-by-spec `DataSetMetaDataType` describing the `AliasUpdate`
  DataSet (`ApplicationUri : string`,
  `Categories : AliasCategoryUpdateDataType[]`).
* `AliasNamePublisher` — subscribes to the registry, builds and emits
  `AliasUpdateDataType` messages via the `AliasUpdateProduced` event.

The library deliberately stays transport-agnostic: it raises the
fully-built `AliasUpdateDataType` and lets the application wire it
into `Opc.Ua.PubSub.UaPubSubApplication` (UDP / JSON / MQTT) or any
other transport that can carry the `AliasUpdateDataType` payload.

### Client side — `Opc.Ua.Client.AliasNames.PubSub`

The client library mirrors the publisher:

```csharp
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.AliasNames.PubSub;

var reader = new AliasNamePubSubReader(
    new AliasNamePubSubReaderOptions
    {
        ExpectedApplicationUri = "urn:opcfoundation:publisher",
    });

// Hand incoming AliasUpdateDataType messages off to the reader from
// your transport (e.g. UaPubSubApplication.DataReceived, an MQTT
// subscriber callback, ...):
reader.Submit(receivedAliasUpdate);

// Wire reader into the resolver so the cache invalidates on every
// LastChange bump observed via PubSub.
await using var resolver = new AliasNameResolver(
    AliasNameClient.OpenStandardAliases(session),
    new AliasNameResolverOptions
    {
        RefreshStrategy = new AliasNamePubSubRefreshStrategy(reader),
    });
```

Helpers shipped:

* `AliasNamePubSubReader` — surfaces incoming
  `AliasUpdateDataType` messages as the `AliasUpdateReceived` event.
  Optional `ExpectedApplicationUri` filter drops messages from other
  publishers.
* `AliasNamePubSubRefreshStrategy : IAliasNameRefreshStrategy` —
  bridges the reader into the resolver. Matches incoming entries by
  the resolver's category `NamespaceUri` + identifier; fires
  `Invalidate` on any value-difference (wrap-safe — the comparison is
  inequality, not strict greater-than).

The PubSub bridge plugs into the same `IAliasNameRefreshStrategy`
extension point as the polling and monitored-item strategies — apps
can mix-and-match, e.g. fall back to polling on a particular category
while letting PubSub drive the rest.

## See also

* OPC UA Part 17 specification:
  https://reference.opcfoundation.org/v105/Core/docs/Part17/
* [Asset Management Basics](AssetManagementBasics.md#discovery) — the
  OPC 10000-110 categories `Assets`, `AssetsByProductInstanceUri` and
  `AssetsByAssetId`, which the AMB node manager serves with a registry of its
  own; `FindAlias` on `0:Aliases` does not search them.
* [Dependency Injection](DependencyInjection.md#alias-name-stores-and-standard-browse-nodes)
  — hosted store registration and standard-category materialization.
* `tools/Opc.Ua.SourceGeneration.Core/Design/StandardTypes.xml` —
  Part 17 type definitions consumed by the source generator.
* `tests/Opc.Ua.Server.Tests/AliasNames/` — server-side unit tests.
* `tests/Opc.Ua.Client.Tests/AliasNames/` — mocked-session and live
  integration tests.
* `samples/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs`
  — `ConfigureAliasNameStore` shows how to seed and register a store.
