# OPC UA Part 17 — Alias Names

OPC UA Part 17 defines a small but valuable address-space pattern: a
hierarchy of human-readable **alias names** that point at one or more
nodes via a non-hierarchical reference type. Clients can search the
hierarchy by wildcard pattern and resolve a name to its targets without
needing to know the target NodeId in advance — useful for tag-naming
schemes (PI / SCADA / DCS), pub/sub topic registries, MES integration,
and any scenario where humans pick names but machines need ids.

This stack ships full Part 17 support in **`Opc.Ua.Server`** (server
side) and **`Opc.Ua.Client`** (client side). The implementation covers:

| Spec section | Type / Method                            | Status |
| ------------ | ---------------------------------------- | ------ |
| §6.2         | `AliasNameType`                          | ✔      |
| §6.3.1       | `AliasNameCategoryType`                  | ✔      |
| §6.3.1       | `LastChange` (`VersionTime`)             | ✔      |
| §6.3.2       | `FindAlias`                              | ✔      |
| §6.3.3       | `FindAliasVerbose`                       | ✔      |
| §6.3.4       | `AddAliasesToCategory`                   | ✔      |
| §6.3.5       | `DeleteAliasesFromCategory`              | ✔      |
| §7.2         | `AliasNameDataType`                      | ✔      |
| §7.3         | `AliasNameVerboseDataType`               | ✔      |
| §8.2         | `AliasFor` reference type                | ✔      |
| §9.2         | Well-known `Aliases (i=23470)`           | ✔ wired |
| §9.3         | Well-known `TagVariables (i=23479)`      | ✔ wired |
| §9.4         | Well-known `Topics (i=23488)`            | ✔ wired |
| Annex D      | PubSub replication                       | not implemented |

## Server side — `Opc.Ua.Server.AliasNames`

The server library exposes a pluggable backend (`IAliasNameStore`) plus
a default in-memory implementation. Apps assemble their alias inventory
inside a store, then either:

1. Register the store directly with the server-wide
   `IAliasNameStoreRegistry` so the standard well-known
   `Aliases`/`TagVariables`/`Topics` nodes start dispatching through it,
   **or**
2. Wrap the store in an `AliasNameNodeManager` to expose application-
   defined categories under a custom namespace (with full
   `AddAliasesToCategory` / `DeleteAliasesFromCategory` support).

Both approaches can be combined.

### Quick start — serving standard categories

```csharp
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.AliasNames;

// inside CreateMasterNodeManager(IServerInternal server, ...):
var tagVariables = new AliasNameCategoryDescriptor(
    ObjectIds.TagVariables,
    QualifiedName.From(BrowseNames.TagVariables),
    AliasNameCapabilities.FindAliasVerbose);

var store = new InMemoryAliasNameStore([tagVariables]);
store.Seed(ObjectIds.TagVariables, "TIC101_Setpoint",
    new ExpandedNodeId("Scalar_Static_Double", refServerNs),
    serverUri: null,
    referenceTypeId: ReferenceTypeIds.AliasFor);
// ... seed more entries ...

((IAliasNameStoreRegistryProvider)server)
    .AliasNameStoreRegistry.Register(store);
```

When a client calls `Aliases.FindAlias` (`i=23476`),
`TagVariables.FindAlias` (`i=23485`) or `Topics.FindAlias` (`i=23494`),
`DiagnosticsNodeManager`'s late binder routes the call through the
registry to the matching store.

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
  `http://opcfoundation.org/UA/AliasName/`.
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
the address-space `LastChange` property.

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
  `AliasNameSubCategoryInfo`.
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
invalidation via `AliasNameResolverRefreshMode.AutoOnLastChange`, which
polls the category's `LastChange` property at the configured publishing
interval and invalidates the cache on any value-difference (covers
`VersionTime` wraparound). Disposing the resolver tears down the
polling timer.

## Spec deviations / wrinkles

* **`AliasNameDataType.ReferencedNodes`** — the wire format defines this
  as `ExpandedNodeId[]` (NodeSet `i=18`), not `NodeId[]`. The
  source-generated `AliasNameDataType` is correct; the historical
  Quickstart sample used `NodeId[]` and has been removed.
* **Standard well-known nodes** — the OPC UA NodeSet instantiates only
  `FindAlias` on `Aliases`/`TagVariables`/`Topics` (plus `LastChange` on
  `Aliases`). Optional methods (`FindAliasVerbose`/`Add`/`Delete`) on
  the standard nodes would require NodeSet extension and are not wired
  by the binder. To expose those, use a standalone
  `AliasNameNodeManager` with your own category nodes.
* **`AliasNameCapabilities.AddAliasesToCategory` /
  `DeleteAliasesFromCategory`** — defaults to `SecurityAdmin`-only
  via `AliasNameNodeManagerOptions.RequireSecurityAdminForMutations`.
  The check requires both the role grant AND a `SignAndEncrypt`
  channel; opt out via the option for development scenarios only.
* **`ReferenceTypeFilter` semantics** — null/empty and
  `ReferenceTypeIds.References` match every alias regardless of
  reference type. Otherwise matches are limited to aliases whose
  reference type is, or is a subtype of, the filter (using
  `Server.TypeTree.IsTypeOf`).

## See also

* OPC UA Part 17 specification:
  https://reference.opcfoundation.org/v105/Core/docs/Part17/
* `Tools/Opc.Ua.SourceGeneration.Core/Design/StandardTypes.xml` —
  Part 17 type definitions consumed by the source generator.
* `Tests/Opc.Ua.Server.Tests/AliasNames/` — server-side unit tests.
* `Tests/Opc.Ua.Client.Tests/AliasNames/` — mocked-session and live
  integration tests.
* `Applications/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs`
  — `ConfigureAliasNameStore` shows how to seed and register a store.
