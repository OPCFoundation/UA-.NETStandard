# OPC UA WoT Connectivity (OPC 10100-1)

This repository implements the OPC UA **WoT Connectivity** companion
specification (OPC 10100-1, "WoT Connectivity for OPC UA") through three
class libraries plus an integration test project:

| Project                          | Purpose                                                       |
|----------------------------------|---------------------------------------------------------------|
| `Opc.Ua.WotCon`                  | Source-generated information model (NodeStates, NodeIds, generated ObjectType client proxies) generated once from the combined **WoT Connectivity 1.1** NodeSet2 (incorporating the OPC 10100-1 v1.02 model plus additive registry nodes in one namespace) and the draft **xRegistry** base NodeSet2 (see §11) |
| `Opc.Ua.WotCon.Server`           | Server-side node manager (`WotConnectivityNodeManager` → `AsyncCustomNodeManager`) and the extensible provider model |
| `Opc.Ua.WotCon.Client`           | Client wrappers + extension methods that compose the generated proxies without inheritance |
| `Opc.Ua.WotCon.Tests`            | NUnit tests covering the TD parser, mappers, simulated provider, discovery facade |

The model namespace URI is `http://opcfoundation.org/UA/WoT-Con/`,
target version `1.02.0`, publication 2025-12-05.

---

## 1. Hosting a WoT Connectivity server

The node manager is exposed through `WotConnectivityNodeManagerFactory`,
which plugs into a `StandardServer` via the standard
`AdditionalNodeManagers` mechanism. A typical setup:

```csharp
var options = new WotConnectivityServerOptions
{
    ThingDescriptionStorageFolder = Path.Combine(AppContext.BaseDirectory, "wot-assets")
};
options.Bindings.Add(new MyHttpWotAssetProviderFactory());
options.Bindings.Add(new MyModbusWotAssetProviderFactory());
options.Discovery = new MyDiscoveryProvider();   // optional

server.NodeManagerFactories.Add(new WotConnectivityNodeManagerFactory(options));
```

The factory advertises two namespaces:

* `http://opcfoundation.org/UA/WoT-Con/` — the static model (loaded
  through the source-generator's `AddOpcUaWotCon` extension).
* `http://opcfoundation.org/UA/WoT-Con/Assets/` (default) — the dynamic
  namespace where assets, property variables, and action methods land.
  Override with `WotConnectivityServerOptions.AssetNamespaceUri`.

`WoTAssetConnectionManagement` is automatically organized below
`Objects`. On first call to `LoadPredefinedNodes`, the server wires the
spec's six methods (CreateAsset, DeleteAsset, optionally DiscoverAssets,
CreateAssetForEndpoint, ConnectionTest, plus the configuration object).
Any persisted TDs in the storage folder are re-materialised on startup.

### Lifecycle

1. Client calls `CreateAsset(name)` → server creates an `IWoTAssetType`
   instance (`HasInterface` reference) with a single `WoTFile` child.
2. Client opens `WoTFile` with mode `Write|EraseExisting` (the only
   write mode allowed per Spec §6.3.10), writes a JSON TD, and calls
   `CloseAndUpdate`.
3. Server parses the TD, selects a registered
   `IWotAssetProviderFactory` whose `CanHandle` accepts it, connects
   the resulting provider, and materialises a property variable for
   each WoT property (mapped per Table 14) and a method node for each
   WoT action (mapped per §6.3.9).

Optional flow when `DiscoverAssets` / `CreateAssetForEndpoint` /
`ConnectionTest` are wired:

1. `DiscoverAssets` returns a list of asset endpoints.
2. `ConnectionTest` verifies one of them.
3. `CreateAssetForEndpoint(name, endpoint)` synthesises a TD via
   `IWotAssetDiscoveryProvider.CreateThingDescriptionAsync` and runs
   the same materialisation path — no client upload needed.

---

## 2. Writing a custom `IWotAssetProvider`

A provider drives a single asset's data plane. The interface is
deliberately small so a binding driver only owns the parts that change
between protocols:

```csharp
public sealed class MyHttpWotAssetProvider : IWotAssetProvider
{
    public ValueTask<(ServiceResult, object?)> ReadAsync(WotPropertyTag tag, CancellationToken ct);
    public ValueTask<ServiceResult> WriteAsync(WotPropertyTag tag, object? value, CancellationToken ct);
    public ValueTask SubscribeAsync(WotPropertyTag tag, uint id, OnWotValueChange cb, CancellationToken ct);
    public ValueTask UnsubscribeAsync(WotPropertyTag tag, uint id, CancellationToken ct);
    public ValueTask<ServiceResult> InvokeActionAsync(WotActionTag action, IReadOnlyList<object?> inputs, IList<object?> outputs, CancellationToken ct);
    public ValueTask DisposeAsync();
}
```

Pair it with an `IWotAssetProviderFactory` that advertises the WoT
binding URIs it understands (surfaced through
`SupportedWoTBindings` per Spec §6.3.1.1):

```csharp
public sealed class MyHttpWotAssetProviderFactory : IWotAssetProviderFactory
{
    public IReadOnlyCollection<string> SupportedBindings { get; }
        = new[] { "https://www.w3.org/2019/wot/http" };

    public bool CanHandle(ThingDescription td) =>
        td?.Base?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ||
        td?.Base?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;

    public ValueTask<IWotAssetProvider> ConnectAsync(ThingDescription td, CancellationToken ct)
        => new(new MyHttpWotAssetProvider(td));
}
```

Each WoT property's binding-specific `forms` element is passed through
on the `WotPropertyTag.Form` (raw `JsonElement`); providers parse it
into whatever protocol metadata they need.

For Discover / CreateForEndpoint / ConnectionTest, register an
`IWotAssetDiscoveryProvider` on `WotConnectivityServerOptions.Discovery`.
Any individual method may throw `NotSupportedException` — the node
manager translates that into `BadNotSupported`.

The repository ships with a canonical `SimulatedWotAssetProvider` in
the test project. It is a complete, working example of the contract
(read / write / observe / action echo) and serves as the default
provider for the test suite.

---

## 3. Using the client

`WotConnectivityClient` composes the generated
`WoTAssetConnectionManagementTypeClient` and adds asset enumeration,
NodeId resolution, and `WotAssetClient` construction:

```csharp
WotConnectivityClient client = await WotConnectivityClient.ForServerAsync(
    session, session.MessageContext.Telemetry, ct);

WotAssetClient asset = await client.CreateAssetAsync("PressureSensor01", ct);
await asset.UploadThingDescriptionAsync(File.ReadAllBytes("sensor.td.jsonld"), ct);

await foreach (WotAssetVariableEntry property in asset.EnumeratePropertiesAsync(ct))
{
    DataValue value = (await session.ReadValueAsync(property.NodeId, ct))!;
    Console.WriteLine($"{property.BrowseName} = {value.WrappedValue}");
}

await client.DeleteAssetAsync(asset.AssetId, ct);
```

### FileSystem extensions

The client does **not** subclass any of the existing
`Opc.Ua.Client.FileSystem` types. Instead it ships extension methods on
the generated `FileTypeClient` / `WoTAssetFileTypeClient` proxies that
add what the spec needs but the base FileSystem client cannot offer
(`CloseAndUpdate` exists only on `WoTAssetFileType`):

* `FileTypeClient.UploadAsync(bytes, …)` — chunked write with
  automatic `Open(Write|EraseExisting)` → `Write*` → `Close`.
* `FileTypeClient.UploadAsync(Stream, …)` — same flow but reads the
  content from a `System.IO.Stream` so callers don't have to buffer the
  entire payload in memory. Non-seekable streams (`NetworkStream`,
  `GZipStream`, …) are supported.
* `FileTypeClient.DownloadAllAsync(…)` — chunked read until end-of-file.
* `FileTypeClient.DownloadToAsync(Stream, …)` — chunked read that
  writes each chunk directly to the supplied `System.IO.Stream`.
* `WoTAssetFileTypeClient.UploadAndUpdateAsync(td, …)` — uploads the TD
  (as `ReadOnlyMemory<byte>` or `System.IO.Stream`) and then calls
  `CloseAndUpdate` (Spec §6.3.10).

`WotAssetClient` exposes the same upload / download convenience pair —
`UploadThingDescriptionAsync` and `DownloadThingDescriptionAsync` —
both with a `ReadOnlyMemory<byte>` / `byte[]` overload and a
`System.IO.Stream` overload, e.g.:

```csharp
await using FileStream tdFile = File.OpenRead("device.td.json");
await asset.UploadThingDescriptionAsync(tdFile, ct);
```

Stream-based callers retain ownership of the stream — the WoT
Connectivity client never disposes the caller's stream.

These work on any `FileType` instance, including ones that are not
anchored under `Server.FileSystem` (e.g. the WoT asset file living
under `WoTAssetConnectionManagement/<asset>`).

### Method invocation and server interoperability

The generated `…TypeClient` proxies invoke methods through the shared `ObjectTypeClient.CallMethodAsync` helper using the **type-declaration** `MethodId` (the Method node on the `ObjectType`). This is fully spec-conformant: OPC UA Part 4 §5.12.2.2 (v1.04 §5.11.2.2) states that, for a `Call` on an `Object` instance, the `methodId` may be **either** the instance Method's NodeId **or** the NodeId of the Method on the `ObjectType` that defines it. This stack's own server accepts both forms.

A few non-conformant servers only bind the method handler on the instance and reject the type-declaration `MethodId` with `Bad_MethodInvalid`. To interoperate with those servers, `CallMethodAsync` transparently falls back: on `Bad_MethodInvalid` it resolves the instance `MethodId` via a `HasComponent` browse path (`TranslateBrowsePathsToNodeIds`), caches it on the proxy, and retries the call once. Conformant servers never trigger the fallback and therefore pay no extra round-trip; subsequent calls against a non-conformant server reuse the cached instance `MethodId`.

---

## 4. Persistence limits

The persisted-TD loader (`AssetRegistry.EnumeratePersistedAsync`) walks
the configured `ThingDescriptionStorageFolder` and re-materialises every
`*.jsonld` file at startup. The following options bound the work and
the per-file resources so a corrupted or adversarial persistence
directory cannot wedge startup through CPU/memory/stack exhaustion:

| Option | Default | Effect |
|---|---|---|
| `MaxThingDescriptionSize` | `1 MiB` | Per-file size cap. Files larger than this are skipped at load time with a warning that names the file and reports the size. Also enforced on the write path via the OPC UA file primitives. |
| `MaxPersistedThingDescriptionFiles` | `10 000` | Hard cap on the number of `*.jsonld` files processed per startup. When reached, the loader emits a single warning and stops; the server still comes up with the assets that *were* loaded. Set to `0` (or negative) to disable persistence loading entirely without removing the directory. |
| `MaxThingDescriptionJsonDepth` | `64` | Maximum JSON nesting depth honoured by the `JsonSerializer.MaxDepth` bound. Comfortably accommodates standard W3C Thing Descriptions while staying well below the default .NET recursion budget. Files that exceed the depth are skipped with a warning (the loader does **not** throw). |

Bumping the defaults is appropriate for controlled environments that
have audited the source of the persisted files; for example:

```csharp
var options = new WotConnectivityServerOptions
{
    ThingDescriptionStorageFolder = "/var/lib/myapp/wot",
    MaxThingDescriptionSize = 4 * 1024 * 1024,        // 4 MiB
    MaxPersistedThingDescriptionFiles = 50_000,       // ~50k assets
    MaxThingDescriptionJsonDepth = 128                // headroom for deeper TDs
};
```

`OperationCanceledException` is propagated unmodified — cancelling the
startup token cancels the enumeration without losing the cancellation
type. `JsonException` and `IOException` are caught and surfaced as
per-file warnings; no other exception type is silently swallowed.

---

## 5. Name validation

Two validators harden the path from third-party input to address-space
nodes:

* **`WotAssetNameValidator`** (asset names from
  `CreateAsset` / `CreateAssetForEndpoint`) — rejects names that would
  escape the persistence folder, contain NUL bytes, hit a Windows
  reserved device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`,
  `LPT1..9`), start with `.`, ` `, or `~`, or end with `.` or ` `.
* **`WotChildNameValidator`** (TD `properties` / `actions` keys) —
  rejects names that would corrupt the OPC UA address space or enable
  visual-spoofing in a browse viewer:
  * empty / whitespace-only / `> 128` chars,
  * leading or trailing whitespace,
  * any `char.IsControl` or BIDI / format character (LRM, RLM, LRE,
    RLE, PDF, LRO, RLO, LRI, RLI, FSI, PDI — see [Unicode TR9 §2.1](
    https://www.unicode.org/reports/tr9/#Bidirectional_Character_Types)),
  * any of `/`, `\`, `.`, `#`, `:`, `!` — characters that have
    syntactic meaning in `NodeId` / browse-path expressions or that
    re-interpret to a path separator at the file-system layer.

Invalid names produce a single `LogWarning` (with the offending name
passed through `WotChildNameValidator.SanitiseForLog` so a hostile
name cannot reshape the rendered log line) and are skipped — the
remaining valid children still materialise so one bad TD entry does
not poison the whole asset.

Duplicate child names (case-sensitive) are also rejected after
validation: only the first occurrence wins, the rest are logged as
duplicates.

---

## 6. Endpoint policy

`CreateAssetForEndpoint` and `ConnectionTest` accept an endpoint URI
from a remote OPC UA client. Before that string flows into the
discovery provider, it passes through `AssetEndpointValidator` against
the configured `WotConnectivityServerOptions.AssetEndpointPolicy`.

Safe defaults:

* `AllowedSchemes` = `{ http, https, opc.tcp }` — anything else
  (`file:`, `gopher:`, `javascript:`, custom OS-vendor schemes, …)
  returns `Bad_SecurityChecksFailed`.
* `AllowLoopback = false` — blocks `127.0.0.0/8`, `::1`, and the
  literal host names `localhost`, `ip6-localhost`, `ip6-loopback`.
* `AllowPrivateAddresses = false` — blocks RFC1918 (10/8,
  172.16/12, 192.168/16), IPv4 link-local (169.254/16 — including the
  AWS / Azure IMDS address `169.254.169.254`), IPv6 ULA (`fc00::/7`),
  and IPv6 link-local (`fe80::/10`).
* `AllowedHosts` (empty) and `BlockedHosts` (empty) — optional
  exclusive allow-list and always-deny list of host names.
* `MaxOperationTimeout = 30 s` — wraps every provider call with a
  linked `CancellationTokenSource.CancelAfter`; on expiry the call
  returns `Bad_Timeout` even when the upstream provider hangs.

Opening up a single internal device while keeping the global block-list:

```csharp
var options = new WotConnectivityServerOptions
{
    AssetEndpointPolicy = new AssetEndpointPolicy
    {
        // Default safe scheme list; add a private-network device
        // explicitly via AllowedHosts.
        AllowPrivateAddresses = false
    }
};
options.AssetEndpointPolicy.AllowedHosts.Add("10.20.30.40");
```

**Security note.** The validator does NOT resolve DNS. Resolving a
host name to an IP at validation time and then re-resolving it at
connect time is itself a TOCTOU SSRF vector — a hostile DNS could
return a public IP to the validator and a private IP to the
connector. Operators who need IP-range enforcement must either pin
`AllowedHosts` to IP literals or accept that the IP-range gates only
fire when the host portion of the URI itself is an IP literal.

---

## 7. Error reporting

`AssetRegistry` never propagates the raw `Exception.Message` /
`StackTrace` / `GetType().Name` from a discovery or provider call to
the remote OPC UA client. The returned `ServiceResult` carries only a
mapped `StatusCode` and a generic operation name (e.g. `"DiscoverAssets
failed."`, `"ConnectionTest failed."`, `"Asset property read failed."`).
The full exception detail — including the inner `ex.Message`, the
stack trace, and the asset / endpoint context — is logged via
`ITelemetryContext`-derived `m_logger` at `LogError` (for control-plane
operations) or `LogWarning` (for per-property / per-action data-plane
operations).

Exception → `StatusCode` mapping:

| Exception type | Status |
|---|---|
| `NotSupportedException` | `Bad_NotSupported` |
| `ArgumentException` | `Bad_InvalidArgument` |
| `IOException` | `Bad_ResourceUnavailable` |
| any other | `Bad_InternalError` (control plane) / `Bad_CommunicationError` (data plane) |
| `OperationCanceledException` | **rethrown unchanged** — never mapped to a status code |

Internal endpoint URIs, file-system paths, provider implementation
details, and stack-trace fragments therefore never leak across the
OPC UA wire. Operators retain the full diagnostic detail through
the server log.

---

## 8. Security: management access policy

The five management methods on the standard
`WoTAssetConnectionManagement` object — `CreateAsset`, `DeleteAsset`,
`DiscoverAssets`, `CreateAssetForEndpoint`, `ConnectionTest` — mutate
the asset registry and trigger outbound network activity. Anonymous,
unauthenticated callers must not be able to reach them.

The node manager therefore enforces a
`WotManagementAccessPolicy` as the very first action of every method
handler. Defaults:

| Knob | Default | Rationale |
|---|---|---|
| `MinimumSecurityMode` | `SignAndEncrypt` | Confidentiality + integrity required. |
| `AllowAnonymous` | `false` | Anonymous identity rejected even on encrypted channels. |
| `RequiredRoleId` | `WellKnownRole_SecurityAdmin` | Mirrors `Opc.Ua.Server.ConfigurationNodeManager` for the equivalent `ServerConfiguration` methods. |

On denial the handler logs a warning (with operation, token type and
granted-role list) and throws
`ServiceResultException(BadUserAccessDenied)`. Internal callers that
invoke the underlying `AssetRegistry` APIs directly — startup
restoration, persisted-asset replay, in-process tests — flow an
`OperationContext`-less `SystemContext`; the policy check is skipped
in that path so server bootstrap continues to work.

Override the policy via DI:

```csharp
services.AddOpcUa()
    .AddServer(...)
    .AddWotConServer(opts =>
    {
        opts.ManagementAccess = new WotManagementAccessPolicy
        {
            RequiredRoleId = ObjectIds.WellKnownRole_ConfigureAdmin,
            MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
            AllowAnonymous = false
        };
    });
```

To loosen the policy (for example a closed lab deployment where the
client cannot present a non-anonymous identity), set
`AllowAnonymous = true` and grant the anonymous identity the chosen
role via your role-mapping layer; do not weaken `MinimumSecurityMode`
in production.

---

## 9. Limitations and known issues

* WoT action input/output mapping handles the flat `type:object` shape
  illustrated by Spec §6.3.9 (a `properties` bag with scalar / array
  members). Deeper schemas — nested objects, oneOf, items-of-object —
  are collapsed to a single `BaseDataType` argument with the JSON
  schema preserved in the description.
* Property mapping follows Spec Table 14: `number → Double`,
  `integer → Int64`, `boolean → Boolean`, `string → String`. Properties
  with `type: object` or `type: null` (or no `type` at all) are
  materialised with status `BadConfigurationError` on read (per Spec
  §6.3.8 last paragraph).
* `WoTAssetFileType.Open` rejects modes other than `Read (1)` and
  `Write | EraseExisting (6)` with `BadNotSupported`, matching the
  spec text.

---

## 10. References

* OPC 10100-1, *WoT Connectivity for OPC UA*: https://reference.opcfoundation.org/specs/OPC-10100-1/full
* W3C Web of Things Thing Description 1.1: https://www.w3.org/TR/wot-thing-description11/
* W3C WoT Binding Templates: https://w3c.github.io/wot-binding-templates/

---

## 11. WoT Connectivity 1.1 registry and materialization (preview)

The `Opc.Ua.WotCon` assembly is source-generated once from the combined
**WoT Connectivity 1.1** NodeSet2, which incorporates the published OPC
10100-1 v1.02 model (NodeIds `1..172`, marked deprecated) plus the additive
registry nodes (`64000+`) in one namespace, and from the abstract
**xRegistry** base model the registry types build on:

| Model | Namespace | Emitted C# namespace |
|-------|-----------|----------------------|
| xRegistry (abstract registry base) | `http://opcfoundation.org/UA/xRegistry/` | `Opc.Ua.XRegistry` |
| WoT Connectivity 1.1 (combined) | `http://opcfoundation.org/UA/WoT-Con/` | `Opc.Ua.WotCon` |

Both NodeSet2 models are *pinned* from the OPC UA drafts authoring
repository into `src/Opc.Ua.WotCon/Design` (as `*.NodeSet2.xml` +
`*.NodeSet2.csv`) and added as `AdditionalFiles`. The legacy 1.02
`WotConnection.xml` / `WotConnection.csv` sources are retained under
`Design/` for reference only — they are incorporated into the combined
NodeSet and are **not** source-generated a second time, so the preserved
1.02 constants and the additive registry constants coexist in one
`Opc.Ua.WotCon` namespace under their exact NodeIds. Run
`pwsh src/Opc.Ua.WotCon/Design/Sync-WotConModels.ps1 -Check` to verify
the pinned copies still match the draft repository (use `-Update` to
refresh them).

### 11.1 Architecture

The 1.1 runtime separates a **stable registry** from **ephemeral
projections**:

* `WotRegistryNodeManager` (stable) exposes the well-known `WoTRegistry`
  object, its Thing Description / Thing Model groups, the `Refresh`
  Method, registry settings and the registry event types. It never re-creates
  itself. Every service group and document resource is additionally
  materialized as a browseable `ThingDescriptionGroupType` /
  `ThingModelGroupType` and `ThingDescriptionFileType` /
  `ThingModelFileType` node beneath `WoTRegistry`, kept in sync with the
  registry snapshot (see §11.7). It never re-creates itself.
* Registry documents are projected into the AddressSpace as **separate
  runtime NodeManagers** through the public `INodeManagerLifecycle`
  (`AddRuntimeNodeSetAsync` for first activation,
  `ShadowReloadRuntimeNodeSetAsync` for updates). The previous generation
  keeps serving its existing monitored items until they drain — clients
  are never disconnected.

Register it on an OPC UA server host:

```csharp
builder
    .AddServer(server => { /* ... */ })
    .AddWotRegistryServer(options =>
    {
        options.StorageFolder = Path.Combine(AppContext.BaseDirectory, "wot-registry");
        options.AutoRefresh = true;      // re-project after every content mutation
        options.StrictBindings = false;  // materialize degraded nodes for unsupported forms
    });
```

### 11.2 Registry service and persistence

`IWotRegistryService` owns an immutable `WotRegistrySnapshot`. Every
mutation produces a new snapshot with a strictly greater `Generation`
(epoch); readers hold a snapshot and never observe a partial change. A
resource carries its versions (raw source bytes + SHA-256 content
digest), desired/active version pointers, `WoTLoadStateEnum`,
`WoTValidationOutcomeDataType` and diagnostics.

Two persistence back-ends are provided:

* `InMemoryWotRegistryStore` — volatile; the registry starts empty.
* `FileWotRegistryStore` — durable; metadata is written with a **bounded
  atomic replace** (write-to-temp then `File.Replace`), one blob per
  version, content-addressed directories. Invalid documents are stored
  with their failure state so a restart restores exactly the last
  observed contents.

Resource bounds (`WotRegistryPersistenceBounds`) cap document size,
versions per resource, resources per group, and group count.

### 11.3 Materialization coordinator

`WotMaterializationCoordinator.RefreshAsync` drives projection:

1. Parses/validates each registry document with `Opc.Ua.Wot`.
2. Builds the TD/TM dependency graph from `links` (`rel = tm:extends /
   type / tm:submodel`), a top-level `tm:extends`, and `tm:ref` pointers,
   resolving references against the registry by Thing id / xid / resource
   id.
3. Partitions the graph into **dependency closures** (weakly-connected
   components) with Thing Models topologically ordered before the Thing
   Descriptions that extend them; a shared model lands in a single
   closure. Cycles and missing dependencies produce deterministic
   diagnostics.
4. Converts each closure to one or more NodeSet2 documents and projects
   the closure as one runtime NodeManager (Add, or ShadowReload on
   update).

Behaviours:

* Independent closures commit independently; a failed or invalid closure
  **retains its previous active generation**.
* An **unchanged** closure (same content digest, options and binder
  version) returns `WoTOutcomeEnum.Unchanged` and emits no model change.
* `Refresh` returns a detailed `WoTRefreshSummaryDataType` plus a
  per-resource `WoTResourceLoadResultDataType[]` and the new generation,
  matching the generated Method signature.
* The coordinator's events are re-emitted by the NodeManager as the
  generated `WoTResourceEventType` / `WoTValidationFailureEventType` /
  `WoTLoadFailureEventType` / `WoTBindingFailureEventType` /
  `WoTRefreshCompletedEventType`.

### 11.4 Binder integration seam

`IWotBinderRegistry` is the runtime-neutral seam the coordinator uses
during Prepare/Activate/Deactivate. Binding plans and capabilities are
immutable. The default `NullWotBinderRegistry` registers no binders, so
affordance forms either **fail a strict closure**
(`StrictBindings = true`) or **materialize as degraded nodes**
(`BadConfigurationError`) when non-strict. Concrete protocol planners and
executors are added by registering an `IWotBinderRegistry`
implementation; no network protocol is implemented in this phase.

### 11.5 Legacy 1.02 compatibility

The legacy `WotConnectivityNodeManager`, its generated 1.02
namespace/NodeIds/method signatures and the client APIs are unchanged.
When both features are hosted, legacy-created assets are additionally
registered as Thing Description resources in a configured legacy group
(`WotRegistryServerOptions.LegacyGroupId`) so they participate in registry
materialization, without making the flat legacy asset list canonical for the registry.

### 11.6 Known limitations (preview)

* No concrete protocol binder ships in this phase (see §11.4). Affordance
  forms therefore either fail a strict closure or materialize as degraded
  nodes; no live protocol read/write/subscribe is performed yet.

### 11.7 Browseable registry projection and management Methods

The stable `WoTRegistryNodeManager` materializes the registry snapshot as a
browseable object tree and wires the inherited xRegistry / registry Methods:

* For every service group a `ThingDescriptionGroupType` or
  `ThingModelGroupType` object is created beneath `WoTRegistry`, and for
  every resource its `ThingDescriptionFileType` / `ThingModelFileType`
  document node is created beneath the group. NodeIds are stable and
  deterministic, derived from the registry Xid (for example
  `WoTRegistry/groups/{groupId}/resources/{resourceId}`). The projection is
  reconciled on every registry `Changed` event — including projection-only
  callbacks, which never re-trigger materialization — and removes group and
  resource nodes as they disappear from the snapshot.
* Each node carries its xRegistry and registry metadata (ids/Xid/epoch/name/
  description/timestamps/format/content type, desired/default/active
  version, enabled/load state, validation outcome, content digest,
  materialized-node count, the materialized `RootNodeId`, and selected
  bindings). `HasNotifier` references chain `WoTRegistry` → group → resource
  → `Server`, and resource lifecycle failure events are sourced at the
  specific resource node (the registry object remains the source for the
  refresh-completed summary event).
* The xRegistry `CreateGroup` / `GetOrCreateGroup` (on `WoTRegistry`),
  `CreateResource` / `GetOrCreateResource` / `Delete` (on a group) and the
  document `Delete`, `Validate`, `SetEnabled` and `SetDefaultVersion` (on a
  resource) Methods are wired to the registry service, enforcing
  `ExpectedEpoch` optimistic concurrency and the management access policy.
* The inherited FileType (`Open` / `Read` / `Write` / `Close` /
  `GetPosition` / `SetPosition`) transfers the document body with
  per-session handles, a single exclusive writer and bounds. Closing a
  write handle commits the buffer as a new version; a document that fails
  validation is still stored as an invalid version so the bytes are never
  lost and the previous active projection is retained.
* Every browseable registry/group/resource node also carries the inherited
  optional `Labels` (`AttributesType`) container. Each label is persisted as
  an ordinally-ordered key/value pair on the owning `WotRegistrySnapshot` /
  `WotResourceGroup` / `WotResource` model and materializes as its own
  `PropertyType` child with a deterministic NodeId (for example
  `WoTRegistry/groups/{groupId}/labels/{key}`) and a safe, collision-checked
  BrowseName. The container's `AddAttribute(Key, Value, ExpectedEpoch)` and
  `RemoveAttribute(Key, ExpectedEpoch)` Methods enforce the management access
  policy, optimistic-concurrency `ExpectedEpoch` (the group/resource's own
  epoch; the registry singleton has no separate epoch so its Labels compare
  against the snapshot `Generation`), the configured
  `WotRegistryPersistenceBounds` (`MaxLabelsPerEntity`,
  `MaxLabelKeyLength`, `MaxLabelValueLength`) and reject invalid/control/BIDI/
  path characters or a key colliding with the container's own fixed
  `AddAttribute`/`RemoveAttribute` member names, using the shared
  `WotChildNameValidator`. `IWotRegistryService` exposes matching
  `Add`/`RemoveRegistryLabelAsync`, `Add`/`RemoveGroupLabelAsync` and
  `Add`/`RemoveResourceLabelAsync` service APIs; label mutations raise a
  projection-only registry change so they update the browseable Labels
  container without re-triggering materialization. Labels survive a registry
  restart and file-store reload (persisted alongside their owning
  group/resource, and — for the registry-level set — in a small
  `registry.json`) and remain visible after every projection reconciliation.
  Version-level labels are stored on the immutable `WotResourceVersion`
  model for API completeness but are not materialized as a separate
  AddressSpace node, since the xRegistry model does not define a
  `VersionType.Labels` container (only Registry/Group/Resource expose one).

### 11.8 Binding-vocabulary alignment (NodeSet2 ↔ WoT)

`Opc.Ua.Wot.WotNodeSetConverter` maps a NodeSet2 model to a WoT Thing
Model / Thing Description and back. The deterministic, versioned
`uav:nodes` projection covers the complete UANodeSet schema and is the
default lossless path; `uav:nodeSet` is emitted only for explicit byte
archival or a demonstrated fallback. Unmapped WoT JSON members are stored
individually by RFC 6901 pointer in a `WoTJsonResidue` NodeSet Extension,
not by copying the source document. The readable surface tracks the current
[OPC UA WoT Binding](https://reference.opcfoundation.org/) revision:

* **Native conversion is the default.** `WotNodeSetPreservationMode`
  selects `WhenRequired` (default), `Always` (explicit byte archive), or
  `Never` (conformance/completeness tests). The converter reconstructs and
  compares `uav:nodes` before reporting native completeness. Tests that
  prove the Binding mapping use `Never` and assert that no envelope exists.
  `NodeSetRoundtripReport.NativeProjectionPreserved` and
  `UsedPreservationEnvelope` distinguish the two paths.

* **Unknown members survive as residue, not an envelope.** During
  TD/TM-to-NodeSet synthesis, only unrecognized or unmapped JSON values are
  stored in the root `Extensions` collection as digest-protected
  `WoTJsonResidue/Member` entries. Reverse conversion regenerates mapped
  facts from OPC UA and applies the pointer-addressed values. A collision
  with a regenerated model fact is reported as
  `WotDiagnosticCode.ResidueConflict`.

* **Event affordances carry `uav:eventType`.** An OPC UA EventType (a
  `BaseEventType` subtype) projects to an event affordance annotated
  `@type: uav:eventType` alongside `uav:isEvent: true`; a NodeSet whose
  root is an EventType is annotated the same way. The two forms are the
  `@type` annotation and the boolean anchor of the same fact, so a
  document that pairs `@type: uav:eventType` with `uav:isEvent: false`
  is rejected (`WotDiagnosticCode.EventAnnotationConflict`). Reverse
  conversion recreates a `BaseEventType` subtype from either form.

* **Identity terms are portable ExpandedNodeIds.** Every persisted
  identity term — `uav:id`, each `uav:hasComponent` / `uav:componentOf`
  entry, `uav:mapToNodeId` / `uav:mapToType`, a NodeId-valued
  `uav:refType`, and a generated `?id=` href — is emitted as an
  OPC 10000-6 `nsu=<NamespaceUri>;...` ExpandedNodeId, resolved through
  the source NodeSet's `NamespaceUris` table so the value survives a
  namespace-table reordering; namespace 0 keeps its canonical `i=` form
  and the session-local `ns=<index>` form is never emitted. On input the
  converter diagnoses an `ns=<index>` in any of these terms
  (`WotDiagnosticCode.NonPortableIdentity`). The `uav:nodeSet` envelope
  and NodeSet-local fields inside `uav:nodes` keep their own namespace
  tables and are excluded from this readable-identity rule.

* **Model concepts carry NamespaceUri-qualified names.** Generated
  contexts bind `ua` to the base OPC UA namespace and deterministic
  `ns1`, `ns2`, … prefixes to companion NamespaceUris. A typed link emits
  the ReferenceType model name directly in `rel` (for example
  `ua:HasOrderedComponent`) beside its definitive `uav:refType`
  ExpandedNodeId. Authored
  `uav:mapToTypeName` / `uav:congruentTypeName` hints are validated and
  preserved beside their definitive identifiers. Compact model names are
  never used for arbitrary instance targets.

* **`observable` advertises binding support.** A generated
  `observable: true` / `observeproperty` form states that the TD exposes
  observation through this binding. It is not a claim that other OPC UA
  Variables are technically unmonitorable; any Variable can be a
  MonitoredItem when the Server grants access.

* **HasComponent subtypes are pinned by a typed link.**
  `uav:hasComponent` / `uav:componentOf` expose parent-child ownership
  for discovery across `HasComponent` and its subtypes. When the source
  ReferenceType is a subtype (for example `HasOrderedComponent`, `i=49`),
  the converter additionally emits a link whose `rel` is
  `ua:HasOrderedComponent`, whose `uav:refType` fallback is `i=49`, and
  whose `uav:refName` names the reference.
  Reverse conversion resolves the name, verifies the fallback when both
  are present, recreates the exact subtype, and otherwise falls back to
  plain `HasComponent`.
