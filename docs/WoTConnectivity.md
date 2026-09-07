# OPC UA WoT Connectivity (OPC 10100-1)

This repository implements the OPC UA **WoT Connectivity** companion specification (OPC 10100-1, "WoT Connectivity for OPC UA") through the model, client, server, and protocol-binding libraries plus integration tests and runnable aggregation samples:

| Project                          | Purpose                                                       |
|----------------------------------|---------------------------------------------------------------|
| `Opc.Ua.WotCon`                  | Source-generated information model (NodeStates, NodeIds, generated ObjectType client proxies) generated once from the combined **WoT Connectivity 1.1** NodeSet2 (incorporating the OPC 10100-1 v1.02 model plus additive registry nodes in one namespace) and the draft **xRegistry** base NodeSet2 (see §11) |
| `Opc.Ua.WotCon.Server`           | Server-side node manager (`WotConnectivityNodeManager` → `AsyncCustomNodeManager`) and the extensible provider model |
| `Opc.Ua.WotCon.Client`           | Client wrappers + extension methods that compose the generated proxies without inheritance, covering both the OPC 10100-1 v1.02 asset-connection surface (`WotConnectivityClient`) and the WoT Connectivity 1.1 registry surface (`WotRegistryClient`, see §11.8) |
| `Opc.Ua.WotCon.Bindings`         | Protocol-binding abstractions, planners, codecs, credential references, HTTP/Modbus/OPC UA executors on net8+, and the generic target-mapping channel factory |
| `Opc.Ua.WotCon.Bindings.Mqtt`    | Optional MQTT executor package |
| `Opc.Ua.WotCon.Tests`            | NUnit tests covering the TD parser, mappers, simulated provider, discovery facade |

The model namespace URI is `http://opcfoundation.org/UA/WoT-Con/`,
target version `1.02.0`, publication 2025-12-05.

For current protocol-runtime architecture and the contributor guide for adding a protocol see [WoT protocol bindings](WotBindings.md), and the runnable end-to-end topology is documented in the [WoT aggregation sample](../samples/WotCon/README.md).

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

### Mirroring assets into the WoT xRegistry

WoT Connectivity can mirror each successfully materialised asset Thing
Description into the WoT xRegistry. The bridge is default-off: when
`WotConnectivityServerOptions.RegistryBridge` is `null`, asset create,
update, and delete do not call the registry. This keeps existing
deployments unchanged.

Enable the bridge by assigning an `IWotRegistryService` directly, or by
using the DI builder helper when the registry service is registered in the
same service collection:

```csharp
builder.AddServer(serverOptions)
    .AddWotConServer(wotOptions =>
    {
        wotOptions.ThingDescriptionStorageFolder = "wot-assets";
    })
    .AddWotRegistryBridge();
```

`AddWotRegistryBridge()` resolves `IWotRegistryService` from DI and mirrors
TDs into the `thingdescriptions` group by default. Pass a custom group id
to override it. Mirroring is independent of legacy TD file persistence:
the registry is itself a durable store, so `RebuildAsync(...,
persistOnSuccess: false, ...)` still mirrors the live TD when the bridge is
enabled. Mirroring is best-effort: registry rejection or I/O failure is
logged and the asset lifecycle still succeeds, matching the existing
secondary persistence policy for TD files.

---

## 2. Writing a custom `IWotAssetProvider`

A provider drives a single asset's data plane. The interface is
deliberately small so a binding driver only owns the parts that change
between protocols:

```csharp
public sealed class MyHttpWotAssetProvider : IWotAssetProvider
{
    public ValueTask<(ServiceResult, Variant)> ReadAsync(WotPropertyTag tag, CancellationToken ct);
    public ValueTask<ServiceResult> WriteAsync(WotPropertyTag tag, Variant value, CancellationToken ct);
    public ValueTask SubscribeAsync(WotPropertyTag tag, uint id, OnWotValueChange cb, CancellationToken ct);
    public ValueTask UnsubscribeAsync(WotPropertyTag tag, uint id, CancellationToken ct);
    public ValueTask<ServiceResult> InvokeActionAsync(WotActionTag action, IReadOnlyList<Variant> inputs, IList<Variant> outputs, CancellationToken ct);
    public ValueTask SubscribeEventAsync(WotEventTag tag, uint id, OnWotEvent cb, CancellationToken ct);
    public ValueTask UnsubscribeEventAsync(WotEventTag tag, uint id, CancellationToken ct);
    public ValueTask DisposeAsync();
}
```

### Event affordances

A TD `events` entry (OPC 10100-1 §6.3.10) materializes as a
non-abstract `BaseEventType` subtype whose event fields come from the
event's `data` schema. The asset object becomes an event notifier and
gains a `GeneratesEvent` reference to the materialized type, so a client
subscribing to the asset — or to the Server object — receives every
occurrence.

```jsonc
"events": {
  "Overheating": {
    "title": "Overheating",
    "data": {
      "type": "object",
      "properties": { "Temperature": { "type": "number" } }
    }
  }
}
```

The registry subscribes the provider once per affordance when the TD is
applied and keeps that subscription for the lifetime of the generation;
the server's subscription machinery decides which clients receive each
occurrence, so a provider never tracks per-client state. The provider
reports an occurrence by invoking the `OnWotEvent` callback with one
value per `WotEventTag.Fields` entry, in order:

```csharp
public ValueTask SubscribeEventAsync(
    WotEventTag tag, uint id, OnWotEvent cb, CancellationToken ct)
{
    m_client.Overheated += (temperature, at) =>
        cb(tag, [new Variant(temperature)], new LocalizedText("Pump is overheating"), 700, at);
    return default;
}
```

`message` and `severity` are optional: a null `message` publishes the
event name and a null or out-of-range `severity` uses the server's medium
fallback. Severity is occurrence data supplied by the provider; the Thing
Description carries no default-severity metadata.

Skipping is not the same as succeeding. Whenever an affordance is skipped — for
an out-of-range severity, an invalid child name, or a duplicate name — applying
the Thing Description returns `GoodResultsMayBeIncomplete` rather than `Good`,
with the number of skipped affordances and one log entry per skip explaining
why. The code stays in the Good class, so a caller testing `ServiceResult.IsGood`
is unaffected and the asset remains usable; but a caller that inspects the code
learns the Thing Description was not applied in full. Reporting a plain `Good`
would leave an operator believing an alarm they authored is configured when it
silently does not exist.

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

The same policy applies to the endpoints `DiscoverAssets` returns:
they are filtered through `AssetEndpointValidator` before they reach
the caller, so a provider cannot use discovery to hand a client an
address the policy would have refused on `ConnectionTest`.

### The generated Thing Description is untrusted too

§11 of the specification requires a Thing Description auto-generated
from a caller-chosen endpoint to be treated as untrusted input,
subject to the same `Wot-Con 1.02` format validation an uploaded
document gets. `CreateAssetForEndpoint` therefore validates what
`IWotAssetDiscoveryProvider.CreateThingDescriptionAsync` returns before
materialising anything from it: the document must identify itself by
carrying a non-empty `name` or `title`. A document that fails
materialises nothing — the asset created to hold it is removed again —
and the call returns `Bad_DecodingError`.

Deserializing into `ThingDescription` is not that check. Every member
of that type is optional, so an empty object deserializes happily;
neither the endpoint (chosen by the caller) nor the provider
(pluggable) is a trusted source. The rule lives in one place,
`ThingDescriptionFormatValidator`, which both the upload path and this
path call.

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

To loosen identity policy in a closed lab, set `AllowAnonymous = true`
and grant the anonymous identity the chosen role via your role-mapping
layer. A conformant registry mutation surface still requires
`MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt`; lowering it
is suitable only for isolated test harnesses. Read-only registry access
may be exposed over `MessageSecurityMode.None` by deployment policy.

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

The `Opc.Ua.WotCon` assembly is source-generated once from the combined **WoT Connectivity 1.1** NodeSet2, which incorporates the published OPC 10100-1 v1.02 model (NodeIds `1..172`, superseded in capability but **not** deprecated) plus the additive registry nodes (`64000+`) in one namespace, and from the abstract **xRegistry** base model the registry types build on:

| Model | Namespace | Emitted C# namespace |
|-------|-----------|----------------------|
| xRegistry (abstract registry base) | `http://opcfoundation.org/UA/xRegistry/` | `Opc.Ua.XRegistry` |
| WoT Connectivity 1.1 (combined) | `http://opcfoundation.org/UA/WoT-Con/` | `Opc.Ua.WotCon` |

Both NodeSet2 models are *pinned* from the OPC UA drafts authoring repository into `src/Opc.Ua.WotCon/Design` (as `*.NodeSet2.xml` + `*.NodeSet2.csv`) and added as `AdditionalFiles`. The legacy 1.02 `WotConnection.xml` / `WotConnection.csv` sources are retained under `Design/` for reference only — they are incorporated into the combined NodeSet and are **not** source-generated a second time, so the preserved 1.02 constants and the additive registry constants coexist in one `Opc.Ua.WotCon` namespace under their exact NodeIds. The tooling that refreshes the pinned copies from the draft repository lives in that authoring repository, not here.

### 11.1 Architecture

The 1.1 runtime separates a **stable registry** from **ephemeral projections**:

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
  `ShadowReloadRuntimeNodeSetAsync` or
  `ImmediateReloadRuntimeNodeSetAsync` for updates). Graceful retirement
  keeps the previous generation serving existing monitored items until
  they drain. Immediate retirement reports `BadNodeIdUnknown` for affected
  monitored items and disposes the previous generation without waiting
  for drain.

Register it on an OPC UA server host:

```csharp
builder
    .AddServer(server => { /* ... */ })
    .AddWotRegistryServer(options =>
    {
        options.StorageFolder = Path.Combine(AppContext.BaseDirectory, "wot-registry");
        options.AutoRefresh = true;      // re-project after every content mutation
        options.StrictBindings = false;  // materialize degraded nodes for unsupported forms
        options.RetirementPolicy = WotProjectionRetirementPolicy.Graceful;
    });
```

### 11.2 Registry service and persistence

#### Materialization extension points

Two optional seams let a protocol driver supply what a Thing Description alone cannot express. Both are resolved from DI; registering neither leaves materialization exactly as it was.

* **`IWotNodeSetContributor`** — runs once per resource *after* the Thing Description has been converted to a NodeSet and *before* any variable is created, and may add nodes to that NodeSet. This is the hook for custom `StructureType` DataTypes that have no NodeSet to import because they belong to one controller program — Rockwell/Studio 5000 UDTs, TIA Portal PLC data types, TwinCAT structured types — generated from the controller's own symbol table at onboarding time. It runs at that point precisely because a `uav:mapByFieldPath` mapping can only resolve once its structured DataType is registered. A document that *can* express its types declaratively does not need this seam: the native projection (`uav:NodeModel`) already carries `DataType` nodes with their `DataTypeDefinition`.
* **`IWotDocumentConverter`** — replaces the Thing Description → NodeSet conversion wholesale, and is now resolvable from DI as well as by direct construction.

#### Resolving referenced companion-specification NodeSets

Thing Descriptions are uploaded at run time through the standard `WoTAssetFileType` upload, so the namespaces a server will be asked to serve are not known at start-up and static pre-loading is not sufficient. `IWotNodeSetResolver` closes that gap: for every namespace a converted document requires but neither declares itself nor finds on the server, the resolver is asked for a NodeSet2. Resolution is recursive over the resolved model's own dependencies, resolved models are projected *before* the document that requires them, and a namespace that stays unresolved is reported rather than silently dropped, so an operator can see exactly what is missing.

```csharp
public interface IWotNodeSetResolver
{
    ValueTask<Stream?> TryResolveAsync(string namespaceUri, CancellationToken ct = default);
}
```

Returning `null` is the contract's way of declining and is not an error. **No implementation ships with the library** — resolving a namespace means reaching out to a catalogue (a UA Cloud Library instance, a corporate model repository, a folder on disk), which is a deployment decision, so the library takes no dependency on any of them. A document that carries its own model needs no resolver at all: the `uav:nodeSet` envelope embeds the NodeSet2 in the Thing Description itself.

`IWotRegistryService` owns an immutable `WotRegistrySnapshot`. Every mutation produces a new snapshot with a strictly greater `Generation` (epoch); readers hold a snapshot and never observe a partial change. A resource carries its versions (raw source bytes + SHA-256 content digest), desired/active version pointers, `WoTLoadStateEnum`, `WoTValidationOutcomeDataType` and diagnostics.

Two persistence back-ends are provided:

* `InMemoryWotRegistryStore` — volatile; the registry starts empty.
* `FileWotRegistryStore` — durable; metadata is written with a **bounded atomic replace** (write-to-temp then `File.Replace`), one blob per version, content-addressed directories. Invalid documents are stored with their failure state so a restart restores exactly the last observed contents.

#### Keeping the document bytes in a shared store

`WotRegistryServerOptions.ResourceStore` moves the document bytes behind the shared, injectable [`IXRegistryResourceStore`](XRegistry.md#resource-storage) — which is what lets a registry run in a high-availability or distributed deployment, because the documents then live somewhere every node can reach rather than in one server's registry folder. A store registered in DI wins over one set on the options, matching the xRegistry server's precedence:

```csharp
options.StorageFolder = "/var/lib/myapp/wot-registry";   // manifest
options.ResourceStore = new WotBlobResourceStore("/mnt/shared/wot-documents");
```

`WotBlobResourceStore` is the default WoT implementation. It keeps one file per document named after the document's SHA-256 digest — deliberately the `{root}/{digest}.bin` layout `FileWotRegistryStore` has always written, so adopting the interface needs **no on-disk migration** and existing registry folders keep working. It is validated against the shared `XRegistryResourceStoreContractTests`, so any other implementation (an object store, a database) can be substituted.

The registry still writes and switches its own manifest atomically; only the bytes move. That split is required because `IXRegistryResourceStore` has no staging, flush or bulk-delete concept, whereas the file store fsyncs its blob directory before the manifest switch and deletes it wholesale when rolling back a pristine commit. It is safe because documents are content-addressed and therefore immutable: a document is always written *before* the manifest that references it, so an interrupted commit can leave an orphaned document but never a dangling reference. A supplied store owns the durability of the bytes it holds.

##### Staging and promotion

Writing bytes before the manifest that names them collides with the way the file store recognises trouble: a `blobs/` directory with no manifest means a lost generation or a crashed commit, and the store fails closed rather than report an empty registry and discard data. If the writer put bytes straight into `blobs/`, the very first write on a fresh deployment would look exactly like that.

So writes land in `staging/` and the commit **promotes** the entries its snapshot references into `blobs/` as artifacts it owns, before switching the manifest:

```text
{root}/staging/…            writer's durable scratch — never evidence of prior state
{root}/blobs/{digest}.bin   promoted by the commit, named by the manifest
{root}/manifest.json        switched last
```

Reads prefer `blobs/` and fall back to `staging/`, so content written but not yet committed is still readable by the transaction that wrote it. A staged entry that never gets promoted is inert and safe to delete: nothing can reference a document until a manifest names it, and only promoted entries are ever named. A commit that is refused therefore leaves a staged orphan and changes nothing else.

Promotion also restores the integrity check that content addressing is worth having. Each referenced document is streamed and hashed as the snapshot is validated, so a blob whose bytes were altered without changing its length, or that cannot be read at all, fails the commit closed. The hash is computed incrementally over chunks, so verifying a document never requires holding it in memory — which is the point of keeping bytes out of the snapshot. Structural validation runs first, so a malformed snapshot is reported for what is wrong with it rather than for content it was never entitled to reference.

The practical win is that a commit no longer rewrites the whole corpus. A blob is written once per digest, so editing one document leaves every other document's file untouched — asserted by `MutatingOneResourceDoesNotRewriteAnotherResourceBytes`.

A decorator around `IWotRegistryStore` **must** forward `IWotRegistryResourceStoreProvider`. A decorator that drops it leaves the registry service writing bytes into a private in-memory store while the wrapped store validates against its own, and every commit then reports the documents missing.

Resource bounds (`WotRegistryPersistenceBounds`) cap document size, versions per resource, resources per group, and group count.

#### Deleting a document, and the documents that could not be read

`DeleteResourceAsync` first walks the dependency graph, which means reading every stored document to find its outgoing references. A registry is a set of blobs, so some of those reads can fail: the blob is gone, or its bytes no longer match the digest the manifest recorded. An unreadable document is **recorded, not propagated** — `WotDependencyGraph.FindDependentsWithFaultsAsync` returns it in `WotDependentSet.Unreadable` and it contributes no edges. Letting the read failure out of the walk would make one corrupt blob anywhere in the registry wedge every policy, including `Force`, whose entire purpose is to remove a target when the tidy answer is unavailable.

Each policy then states what it did about it, and `WotDeleteResult.Unreadable` names them:

| Policy | Proven dependents | Documents that could not be read |
| --- | --- | --- |
| `Reject` | Refuses while any exists. | **Refuses.** The safety it asserts — that nothing is still using the document — was never established. |
| `Retire` | Keeps the document stored and resolvable; only the projection comes down. | Nothing loses a reference, so they are not this policy's problem. |
| `Cascade` | Unloads only the ones that lost a reference. | **Left alone** and reported. Unloading one would take a projection down on a guess. |
| `Force` | Deletes the target and marks every dependent `Failed`. | Marked `Failed` too, with a diagnostic saying why: `Force` cannot claim they were unaffected, and its contract is to say what it broke. |

The target's own blob is the one exception: it is being removed anyway, so its readability never blocks the delete under any policy.

### 11.3 Materialization coordinator

`WotMaterializationCoordinator.RefreshAsync` drives projection:

1. Parses/validates each registry document with `Opc.Ua.Wot`.
2. Builds the TD/TM dependency graph from `links` (`rel = tm:extends /
   type / tm:submodel`), a top-level `tm:extends`, and `tm:ref` pointers,
   resolving references against the registry by Thing id / xid / resource
   id. It never follows an arbitrary external URL; an unresolved absolute
   URL remains a missing dependency unless a configured xRegistry
   federation layer has registered it.
3. Partitions the graph into **dependency closures** (weakly-connected
   components) with Thing Models topologically ordered before the Thing
   Descriptions that extend them; a shared model lands in a single
   closure. Cycles and missing dependencies produce deterministic
   diagnostics.
4. Converts each closure to one or more NodeSet2 documents and projects
   the closure as one runtime NodeManager (Add, or graceful/immediate
   reload on update according to `RetirementPolicy`).

Behaviours:

* The NodeSet2 a closure converts to is **loadable by construction**. Step 4 is
  literally `ConvertAsync` → serialize → `UANodeSet.Read` → `Import`, and the
  importer rejects any name used where a NodeId is expected that the document
  does not declare in `<Aliases>`. The converter therefore completes the
  `<Aliases>` table of everything it returns, on all three restoration paths
  (readable synthesis, `uav:nodeSet` envelope restore and `uav:nodes` native
  restore), without rewriting any name a document brought. See
  [Importable output](WoTNodeSetConversion.md#importable-output). A vendor alias
  a source document uses but never declares still fails the load, and is
  reported as a closure diagnostic naming it.
* Independent closures commit independently; a failed or invalid closure
  **retains its previous active generation**.
* An **unchanged** closure (same content digest, options and binder
  version) returns `WoTOutcomeEnum.Unchanged` and emits no model change.
* `WotProjectionRetirementPolicy.Graceful` preserves existing monitored
  items on the previous generation until drain.
  `WotProjectionRetirementPolicy.Immediate` invalidates affected items
  with `BadNodeIdUnknown` and disposes the previous generation. The proof
  rejects immediate retirement when the old generation owns a durable
  monitored item; configure `Graceful` for that closure.
* `Refresh` returns a detailed `WoTRefreshSummaryDataType` plus a
  per-resource `WoTResourceLoadResultDataType[]` and the new generation,
  matching the generated Method signature.
* The coordinator's events are re-emitted by the NodeManager as the
  generated `WoTResourceEventType` / `WoTValidationFailureEventType` /
  `WoTLoadFailureEventType` / `WoTBindingFailureEventType` /
  `WoTRefreshCompletedEventType`.

### 11.4 Binder integration seam

`IWotBinderRegistry` is the runtime-neutral seam the coordinator uses during Prepare/Activate/Deactivate. `WotProtocolBinderRegistry` implements that seam and `IWotBindingChannelFactory`, compiling immutable plans from the registered binders and opening channels through independently registered executors. The base `Opc.Ua.WotCon.Bindings` package ships all eight planners and bundles HTTP, Modbus TCP, and OPC UA executors on `net8.0`, `net9.0`, and `net10.0`; MQTT remains in `Opc.Ua.WotCon.Bindings.Mqtt`. The base package retains the full `net472;net48;netstandard2.1;net8.0;net9.0;net10.0` matrix, where planner-only validation remains available even when concrete executor namespaces are not compiled.

The generic projection runtime is implemented in `Opc.Ua.WotCon.Server.Materialization`. It resolves affordance-level OPC 10101 target mappings against freshly imported runtime NodeSets, wires async read/write handlers, opens one lazy channel per compiled form per generation, lets local monitored items sample the same read handler, supports reflection-free structured field mapping, and disposes channels with their owning generation. Updates use shadow reload, so existing monitored items keep the retired generation alive until they drain while new reads and monitored items use the replacement generation.

The default `NullWotBinderRegistry` remains the no-binding baseline. With it, affordance forms either **fail a strict closure** (`StrictBindings = true`) or **materialize as degraded nodes** (`BadConfigurationError`) when non-strict. Protocol support is opt-in: `AddWotProtocolBinders()` registers all eight planners, while `AddHttpWotBinding()`, `AddMqttWotBinding()`, `AddModbusWotBinding()`, and `AddOpcUaWotBinding()` add their concrete executors. The core server registers none of these by default; see [WoT protocol bindings](WotBindings.md).

### 11.5 Legacy 1.02 compatibility

The legacy `WotConnectivityNodeManager`, its generated 1.02 namespace/NodeIds/method signatures and the client APIs are unchanged. When both features are hosted, legacy-created assets are additionally registered as Thing Description resources in a configured legacy group (`WotRegistryServerOptions.LegacyGroupId`) so they participate in registry materialization, without making the flat legacy asset list canonical for the registry.

### 11.6 Protocol and projection scope

The implemented data plane covers executable HTTP, Modbus TCP, MQTT, and OPC UA binding forms according to the operation coverage documented in [WoT protocol bindings](WotBindings.md). CoAP, BACnet, PROFINET, and LoRaWAN currently ship as planner-only binders: their forms are validated and represented in plans, but a non-strict closure is degraded until an executor is registered.

OPC 10101 target mapping is authored on property affordances, not forms. `uav:mapByFieldPath` requires `uav:mapToType`, portable `nsu=` NodeIds are resolved against the runtime generation's namespace table, and the mapping is protocol-neutral as illustrated by [OPC 10101 §8.2](https://reference.opcfoundation.org/specs/OPC-10101/8.2). See [OPC 10101 §6.5.4](https://reference.opcfoundation.org/specs/OPC-10101/6.5.4) and the [binding-authoring guide](WotBindings.md#adding-your-own-binding) for the exact validation and runtime semantics.

### 11.7 Browseable registry projection and management Methods

The stable `WoTRegistryNodeManager` materializes the registry snapshot as a browseable object tree and wires the inherited xRegistry / registry Methods:

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
  Registry mutations require a `SignAndEncrypt` SecureChannel; deployments
  may separately permit read-only registry access over `SecurityMode.None`.
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
`uav:nodes` projection covers the complete UANodeSet schema and is emitted
only when the semantic/readable mapping cannot reproduce all source facts;
`uav:nodeSet` is emitted only for explicit byte archival or a demonstrated
final fallback. Unmapped WoT JSON members are stored
individually by RFC 6901 pointer in a `WoTJsonResidue` NodeSet Extension,
not by copying the source document. The readable surface tracks the current
[OPC UA WoT Binding](https://reference.opcfoundation.org/) revision:

* **Semantic conversion is the default.** `WotNodeSetPreservationMode`
  selects `WhenRequired` (default), `Always` (explicit byte archive), or
  `Never` (conformance/completeness tests). The converter first reconstructs
  the readable document and omits `uav:nodes` when it is equivalent; it then
  validates the structured projection when fallback is required. Tests that
  prove completeness use `Never` and assert that no opaque envelope exists.
  `WotNodeSetRoundtripReport.NativeProjectionPreserved` and
  `UsedPreservationEnvelope` distinguish the two paths
  (`Opc.Ua.Wot.WotNodeSetRoundtrip.Run`).

* **Unknown members survive as residue, not an envelope.** During
  TD/TM-to-NodeSet synthesis, only unrecognized or unmapped JSON values are
  stored in the root `Extensions` collection as digest-protected
  `WoTJsonResidue/Member` entries. Reverse conversion regenerates mapped
  facts from OPC UA and applies the pointer-addressed values. A collision
  with a regenerated model fact is reported as
  `WotDiagnosticCode.ResidueConflict`.

* **Event affordances carry `uav:eventType`.** An OPC UA EventType (a
  `BaseEventType` subtype) projects to an event affordance annotated
  `@type: uav:eventType`; a NodeSet whose root is an EventType is
  annotated the same way. That annotation is the whole statement of
  event identity — WoT Binding 1.1 defines no parallel boolean flag —
  and reverse conversion recreates a `BaseEventType` subtype from it. A
  legacy document that still carries `uav:isEvent` is consumed
  permissively: the member survives as ordinary unknown residue and
  changes nothing, while strict authoring reports it as an unknown term.

* **Identity terms are portable ExpandedNodeIds.** Every persisted
  identity term — `uav:id`, each `uav:hasComponent` / `uav:componentOf`
  entry, `uav:mapToNodeId` / `uav:mapToType`, a NodeId-valued
  `uav:refId`, and a generated `?id=` href — is emitted as an
  OPC 10000-6 `nsu=<NamespaceUri>;...` ExpandedNodeId, resolved through
  the source NodeSet's `NamespaceUris` table so the value survives a
  namespace-table reordering; namespace 0 keeps its canonical `i=` form
  and the session-local `ns=<index>` form is never emitted. On input the
  converter diagnoses an `ns=<index>` in any of these terms
  (`WotDiagnosticCode.NonPortableIdentity`). The `uav:nodeSet` envelope
  and NodeSet-local fields inside `uav:nodes` keep their own namespace
  tables and are excluded from this readable-identity rule.

* **BrowseNames are portable QualifiedNames.** Generated readable
  `uav:browseName` values use OPC 10000-6 `nsu=<NamespaceUri>;<Name>` for
  non-base namespaces and the bare Name for namespace 0. Numeric
  `namespaceIndex:name` is retained only inside `uav:nodes`, which carries
  its own `namespaceUris` table.

* **Model concepts carry NamespaceUri-qualified names.** Generated
  contexts bind `ua` to the base OPC UA namespace and deterministic
  `ns1`, `ns2`, … prefixes to companion NamespaceUris. A typed link emits
  the ReferenceType model name directly in `rel` (for example
  `ua:HasOrderedComponent`) beside its definitive `uav:refId`
  ExpandedNodeId. Authored
  `uav:mapToTypeName` hints are validated and
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
  `ua:HasOrderedComponent`, whose `uav:refId` is `i=49`, and
  whose `uav:refName` names the reference.
  Reverse conversion resolves the name, verifies the identifier when both
  are present, recreates the exact subtype, and otherwise falls back to
  plain `HasComponent`.

* **The readable surface carries more than it used to.** Documents this
  server generates now also carry event severity, `Method` argument
  schemas, event `data` and the Section 13 Condition terms, engineering
  units and ranges, `titles` / `descriptions`, `uav:valueRank` /
  `uav:arrayDimensions`, and typed links for arbitrary companion
  ReferenceTypes in both directions. Each is stated once, in
  [WoT / NodeSet conversion](WoTNodeSetConversion.md); the relation and
  type-binding resolution rules are stated once in
  [WoT protocol bindings](WotBindings.md#resolving-a-type-binding-the-local-context).
  Converted NodeSets are also alias-complete, which is what makes the
  materialization path in §11.3 loadable rather than merely convertible.

* **Conformance strictness is opt-in.** `WotNodeSetConverterOptions.ConformanceMode`
  defaults to `Permissive`, which is what Sections 4.1, 6.6, 9.4 and 10.2
  require of a consumer: an unknown `uav:` term is preserved as residue
  rather than reported. Registry materialization keeps that default, so a
  document authored against a later revision still loads. `Strict` is for
  authoring and conformance testing; see
  [Conformance claims and strict mode](WoTNodeSetConversion.md#conformance-claims-and-strict-mode-sections-41-61-66-and-11).

### 11.9 Registry client

`Opc.Ua.WotCon.Client` ships a registry client surface alongside the existing `WotConnectivityClient`. `WotRegistryClient` **derives from the shared xRegistry `XRegistryClient`** — the WoT registry model subtypes the xRegistry base model, so it is a *domain client* in the sense of [xRegistry — Extending for a domain registry](XRegistry.md#extending-for-a-domain-registry) and inherits the base group/resource lifecycle, `Session` and `RegistryNodeId`. The registry root is not the provisional well-known `65000`: the browse-resolved `WoTRegistry` NodeId is passed to the base constructor. The generated `WoTRegistryTypeClient` / xRegistry `GroupTypeClient` / `ResourceTypeClient` proxies are still *composed* rather than inherited, so a typed proxy is reused directly instead of being re-resolved per call:

* `WotRegistryClient.ForServerAsync(session, telemetry, ct)` resolves the well-known `WoTRegistry` object (a `HasComponent` child of the `Server` object) via `TranslateBrowsePaths`, exactly like `WotConnectivityClient.ForServerAsync` resolves `WoTAssetConnectionManagement`. Both now share the same internal `TranslateBrowsePaths` helper. The resolved NodeId is surfaced as the inherited `RegistryNodeId`.
* `CreateThingDescriptionGroupAsync` / `CreateThingModelGroupAsync` and their `GetOrCreate…` counterparts call the inherited xRegistry `CreateGroup`/`GetOrCreateGroup` Methods. The wire protocol has no "kind" argument, so the returned `WotRegistryGroupClient` discovers whether the server materialised a `ThingDescriptionGroupType` or a `ThingModelGroupType` from the created group's reported `TypeDefinition` — this works against any conformant server regardless of its own group-naming convention. `ThingModelsGroupId`/`ThingDescriptionsGroupId` expose the two well-known reserved group ids.
* `WotRegistryGroupClient.CreateResourceAsync` / `GetOrCreateResourceAsync` call the group's `CreateResource` / `GetOrCreateResource` Methods and return a `WotRegistryResourceClient` plus the server-assigned version id.
* `WotRegistryResourceClient.UploadNewVersionAsync(ByteString | Stream, …)` uploads a new document version through the inherited `FileType` `Open(Write|EraseExisting)` → `Write` → `Close` primitives (the same `FileTypeClientExtensions` used elsewhere in this package); closing the write handle commits the buffer as a new resource version. `DownloadAsync` reads the active/default version back through the shared xRegistry `ResourceTypeClientExtensions.ReadDocumentAsync` helper — a WoT document resource *is* an xRegistry `ResourceType`, so the shared helper applies directly to the generated proxy — and `DownloadToAsync` streams it into a caller-owned `Stream`. `ValidateAsync`, `SetEnabledAsync`, `SetDefaultVersionAsync` and `DeleteAsync` call the matching document Methods.
* `WotRegistryClient.RefreshAsync` / `RefreshAllAsync` call the generated `Refresh` Method and return a typed `WotRegistryRefreshResult` (`Summary`, `Results`, `NewGeneration`, `HasFailures`, `EnsureSuccess()`).
* `WotRegistryClient.LoadDocumentsAsync` loads a caller-supplied `ArrayOf<WotRegistryDocument>` (an immutable `Kind`/`GroupId`/`ResourceId`/`Content` (`ByteString`)/`VersionId` descriptor), get-or-creating each target group/resource and uploading its content, then optionally calls `RefreshAllAsync` — one workflow. Thing Models are always processed before Thing Descriptions (preserving the caller's relative order within each kind) so referenced models are materialised before the descriptions that depend on them. A mutation failure or a group/document kind mismatch aborts immediately (`ServiceResultException`); a refresh failure is *not* thrown — it is surfaced on the returned `WotRegistryBulkLoadResult.Refresh` for the caller to inspect, since a partial refresh outcome is legitimate application data.

```csharp
WotRegistryClient registry = await WotRegistryClient.ForServerAsync(
    session, session.MessageContext.Telemetry, ct);

WotRegistryGroupClient group = await registry.CreateThingDescriptionGroupAsync(ct);
(WotRegistryResourceClient resource, string versionId, bool created) =
    await group.GetOrCreateResourceAsync("sensor01", ct: ct);

await resource.UploadNewVersionAsync(
    ByteString.From(File.ReadAllBytes("sensor01.td.json")), ct: ct);

WotRegistryRefreshResult refresh = await registry.RefreshAllAsync(ct: ct);
refresh.EnsureSuccess();
```

Register the registry client with DI alongside `AddWotConClient` via `AddWotRegistryClient` (on `IOpcUaBuilder` or `IOpcUaClientBuilder`, bindable from `IConfiguration`/`IConfigurationSection`, default section `OpcUa:WotCon:RegistryClient`). It follows the same lazy `ManagedSession`-backed factory pattern: resolve `Func<CancellationToken, Task<WotRegistryClient>>` for the lazily connected form, or `Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>` to wrap an already-connected session.

## 12. Conformance to WoT Connectivity 1.1

This clause describes what the model requires and what this implementation
provides. It is a statement of the current state, not a history of how either
got here.

### 12.1 Model identity

The information model is generated from the NodeSets the specifications publish,
adopted verbatim rather than maintained by hand.

| Model | Version | PublicationDate |
|---|---|---|
| WoT Connectivity | `1.1` | 2026-09-02 |
| WoT Binding | `1.1` | 2026-07-29 |
| xRegistry (`RequiredModel`) | `0.4.0` | 2026-08-31 |

xRegistry contributes 71 nodes and two behavioural rules the registry honours: a
reverse-authority construction algorithm for `GroupId` and `ResourceId`
(§ 11.4), and `SignAndEncrypt` on every mutating operation.

Draft iterations are identified by the specification release label, for example
`1.1-draft5`; they do not increment the information model version.

### 12.2 Conformance units and profiles

Three profiles form a lattice rather than a ladder:

| Profile | Covers |
|---|---|
| *WoT-Con Minimal* | `Wot-Con 1.02` alone — the published OPC 10100-1 v1.02 shape and nothing else |
| *WoT-Con Registry Server* | the registry surface without federation, change events, projections or the atomicity modes |
| *WoT-Con Full* | every unit, `Wot-Con 1.02` included |

Minimal and Registry Server are each a subset of Full, and neither is a subset of
the other: they share no conformance unit. A server may implement either surface
or both.

`Wot-Con 1.02` is implementable on its own, so it covers serving the data points of
an uploaded Thing Description — and with it, format-validating that document
before any Node is materialized from it. Client-supplied input never reaches the
AddressSpace unchecked; a document that fails validation materializes nothing and
returns `Bad_DecodingError`.

`WOTC-ProjectionMaterialization` is carried by `ThingDescriptionFileType`,
`ThingModelFileType` and `HasWoTProjection`.

### 12.3 Grouping

A grouping is an ordinary document whose members are reached by `ua:Organizes`
links; across documents it is a projection document (§ 12.4). The binding has no
separate grouping vocabulary, because a grouping is an Object and `Organizes` is
a ReferenceType — the two constructs the model already has.

### 12.4 Projection documents and the View NodeClass

A **projection document** is a Thing Description or Thing Model that declares,
rather than defines, its affordances. It names source documents and states which
of their affordances a view is assembled from, so it carries references and
annotations only and has nothing that can drift from its sources.

This completes the NodeClass binding. Seven OPC UA NodeClasses bind to a WoT
construct that defines something; `View` is the eighth and the only one whose
purpose is to select rather than define. A View owns no Node — it organizes Nodes
that already exist so a client can browse a subset shaped for one task — and a
projection document is that construct in WoT.

A projection is marked by `uav:projection` in its `@type` and declares:

| Term | Meaning |
|---|---|
| `uav:scenario` | absolute IRI naming the purpose the view serves |
| `uav:projects` | non-empty manifest of the documents it projects |
| `uav:sourceName` | alias for a source, unique in the manifest |
| `uav:routing` | `source` (default) or `projection` |
| `uav:sourceDigest` | `sha-256:<hex>` pinning a source revision |
| `uav:namePrefix` | prefix applied to bulk-selected names |

Selection has three forms. An enumerated `tm:ref` names one affordance and is the
only form that can annotate it; `uav:selectAll` takes every affordance of a
source; and `uav:select` filters on affordance kind, semantic identifier and type
tokens. The predicate set is closed — a filter carrying any other key is rejected
rather than ignored — so a filter stays decidable by inspection.

Every member of `properties`, `actions` and `events` carries `tm:ref`. A member
without one is defining an affordance, which is the one thing a projection
document must not do.

An enumerated selection may annotate the affordance it names, but Section 12.5
closes the set of members it may annotate with. Permitted beside `tm:ref` are
`title`, `titles`, `description`, `descriptions`, additional `@type` values,
`uav:semanticId` and `uav:metadata` — presentation and semantics — plus `forms`
and `security` where `uav:routing` is `projection`. Every other member is
rejected with `ProjectionAnnotationNotPermitted`, including a restated `type`,
`unit`, `minimum`, `maximum` or `enum`. Merging one of those would silently
override what the source says about the Node, which is exactly what a document
that declares rather than defines must not be able to do. The rule mirrors the
closed predicate set of `uav:select`: both are decidable by inspection.

A member selected from a **`source`-routed** source carries the source's own
form, so one that states `forms` or `security` of its own makes the document
**invalid** — the same `ProjectionAnnotationNotPermitted`, reported at
resolution time where the routing is known, and the view does not resolve. It is
not dropped: a dropped form is one the author wrote and the consumer silently
did not use, which reads at run time as the source endpoint answering a request
the document appeared to address elsewhere.

Selections are applied in the total order of Section 12.4, and the **first**
selection of a name wins: by the position of the source in `uav:projects`;
within one source, every enumerated selection before every bulk one; within each
group, by affordance kind in the fixed order `properties`, `actions`, `events`;
within one kind, by ascending Unicode code point of the name the selection takes
**in the view**; and, where two selections still compare equal, by ascending
Unicode code point of the affordance's name **in the source**. The last key is
what makes the order total: `uav:namePrefix` upper-cases the first character of
the source name, so `serialNumber` and `SerialNumber` in one source both become
`deviceSerialNumber` in the view and nothing before it separates them. The order
is stated over names rather than over document order because `properties`,
`actions` and `events` are JSON objects, which RFC 8259 defines as unordered — a
rule that ranked selections by member position would let two conforming
consumers resolve identical bytes into different views.

Materialization produces a `View` Node that `Organizes` the Nodes already
materialized from the sources. The View creates **no** affordance Node, so
`MaterializedNodeCount` counts only the View and any organizational Objects, not
the Nodes it organizes. `RootNodeId` is the View, and the document resource points
at it through `HasWoTProjection`, navigable back through `WoTProjectionOf`.

`ViewVersion` is a deterministic function of the resolved membership alone, computed
exactly as *WoT Binding* §12.6 specifies: each resolved member's ExpandedNodeId in the
portable `nsu=` form, **deduplicated**, sorted ascending by Unicode code point, each
written as its length in UTF-8 octets, a colon, the string and U+000A, UTF-8 encoded,
and the first four octets of the SHA-256 digest read as a big-endian `UInt32`, with `0`
reported as `1` because OPC 10000-3 §5.4 requires a value greater than zero.

The membership is a **set**. A Node the view reaches through more than one organized
group (§12.7) is one member of the View and contributes once, because a View
`Organizes` a Node or it does not, and the same `Organizes` Reference is not created
twice. A server that counted a shared Node twice would compute a different value from
one that organized it under a single group, for the same View.

The sort is by Unicode **code point**, which on this platform is not the same as
`StringComparer.Ordinal`: an ordinal comparison orders UTF-16 code *units*, so
every supplementary character sorts below U+E000..U+FFFF instead of above them.
A Server that sorted ordinally would compute a different `ViewVersion` from a
conforming one for the same membership whenever a NodeId string identifier
carries a character outside the Basic Multilingual Plane. `Opc.Ua.Wot.WotCodePointComparer`
is the one implementation of that order, shared by this computation, the
projection selection order of §12.4 and the endpoint tie-break of §5.7.1, so a
second one cannot drift from the first.

The length prefix is what makes the encoding injective. A NodeId string identifier may
itself contain U+000A, so joining on the separator alone would let a single member that
embeds a newline serialize byte-for-byte as the two members it imitates — a structural
collision an author can construct deliberately, distinct from the statistical one below.

Naming the function is what makes the property testable: two servers that resolved the
same membership compute the same value, which a per-server counter could not promise
across a redundant pair. It needs no persisted state, so it survives a restart or a
rebuild from the registry, and it records *what* a View contains rather than how it is
arranged — reordering the same members does not change it. It is not monotonic and
carries no ordering. A `UInt32` cannot separate every possible membership, so a client
treats inequality as proof that the membership changed and equality as evidence rather
than proof that it did not.

A projection over Thing Models materializes to a `View` in the same way,
organizing the ObjectType and VariableType Nodes its source Thing Models
materialized.

A source that is not in the address space is omitted from the View and reported in
`WoTResourceLoadResultDataType.Message`; the resource still reaches
`LoadState = Active`, because an omission is a reported detail rather than a
failure. A View that omitted anything reports `Outcome = Warning`, so a client
can tell a complete View from a partial one without diffing its membership.

Two omission causes are distinguished, because they have different remedies.

**A source that is itself a projection.** `uav:resolvedFrom` names, per *WoT
Binding* §12, "the reference the selection was made by" — the immediate
selection, not the ultimate origin. A projection that selects from another
projection therefore names an intermediate document, and an intermediate
materializes a View rather than Nodes. Resolution follows the chain depth-first
to the Nodes the ultimate sources materialized, as WoT Connectivity §7.13
requires; only when no document in the chain materialized the affordance is the
member omitted, and the omission names the deepest source the walk reached.

**A member whose Node does not exist.** An affordance resolves to a NodeId by
its authored `uav:id` when it has one and by a deterministic scheme anchored at
the source's root otherwise. Neither is proof that the Node was materialized: a
document whose affordances never synthesized anything resolves to plausible
identifiers that address nothing. Every planned member is therefore tested
against the address space before the View is built, and one that no NodeManager
owns is dropped and reported instead of organized. Organizing it would leave the
View advertising a membership no client can browse — a `Browse` drops a reference
whose target does not resolve, so the View would report a count it does not have.

Authoring `uav:id` on an affordance is what makes the first mechanism exact. A
document that materializes its Nodes from a `uav:nodes` native projection should
carry `uav:id` per affordance, because the deterministic scheme describes the
shape *synthesis* produces and does not hold for restored Nodes.

### 12.4.1 Parent placement through `uav:componentOf`

WoT Connectivity §7.3 lets a Thing Description place the Object it projects
under an existing parent. The materializer supports a `links` entry whose
`rel` is `uav:componentOf` in two target forms:

| `href` target | Materialized result |
|---|---|
| another document in the same registry snapshot | the parent is that document's projection root |
| an OPC UA NodeId / ExpandedNodeId already present in the AddressSpace | the parent is that existing Node |

When either form resolves, the projected root receives the inverse
`HasComponent` reference to that parent, so normal hierarchical browsing from
the parent reaches the new Object. This is a placement operation, not a binding
fallback: if the parent cannot be resolved, the resource fails projection with
`LoadState = Failed`, raises `WoTLoadFailureEventType`, and reports
`Phase = Projection`. The server does not silently drop the parent reference.

Two §7.3 forms are not implemented yet and therefore also fail loudly rather
than being ignored: a target expressed as `uav:browsePath`, and the Thing Model
projection-root fallback described by the specification. Authors should use one
of the two supported forms above until those gaps are closed.

### 12.4.2 Event notifier behaviour in projection Views

Projection Views and the organizational group Objects they contain organize
existing Nodes; they do not become event sources. Their `EventNotifier` is
`None`, and materialization does not synthesize `GeneratesEvent` from a View or
group to the event affordances it organizes. This matters to consumers because a
subscription on a View or group is not enough to receive events. Event delivery
still depends on the Object that actually carries `GeneratesEvent` and on an
event-producing runtime path behind that Object.

Current sample limitation: the upstream cavitation signal is proven to raise the
upstream alarm and leave it unacknowledged, but Pump1 carries no
`GeneratesEvent` reference for its cavitation alarm and acknowledgement does not
round-trip because the projected pump actions are Start, Stop and Reset rather
than Condition Methods carrying `uav:conditionAction` / `uav:actsOn`. The Pump1
`Supervision` and `Management` views therefore report organizing 0 of their
selected members, naming each one, rather than reporting a success they did not
achieve. The cause is that `SamplePump.td.json` carries a `uav:nodes` native
projection: the converter restores the pump from it and returns before affordance
synthesis, so its action and event affordances materialize nothing. See
[the sample README](../samples/WotCon/README.md) for the measured breakdown.

### 12.5 Portable identifiers

Two identifier forms are errors, because a document carrying either binds to the
wrong namespace as soon as the namespace table is reordered:

* the session-local `ns=<index>` form in any NodeId-valued term — `uav:id`,
  `uav:hasComponent`, `uav:componentOf`, `uav:mapToNodeId`, `uav:mapToType`,
  `uav:refId` and form `href`s;
* a numeric namespace prefix in `uav:browseName` or `uav:browsePath`, such as
  `3:PaintingRobot_1`.

Authors write `nsu=<NamespaceUri>;<idtype>=<id>` and either a context-bound
non-numeric prefix or `nsu=<NamespaceUri>;<Name>` instead.
`WotNodeSetConverterOptions.AllowNonPortableIdentifiers` keeps a document written
against OPC 10101 v1.00 readable while it is rewritten; see
[WoT protocol bindings](WotBindings.md#compatibility-switch-for-non-portable-identifiers)
for the forms and worked examples.

### 12.6 The 1.02 asset surface

The incorporated OPC 10100-1 v1.02 management and upload surface (NodeIds
`1..172`) is superseded in capability by the registry but is **not** deprecated:
serving a WoT asset that way is legitimate, and *WoT-Con Minimal* is built on it.

It is also a **separate code path**. `AssetRegistry` reads an asset document
into the POCO shape supported by this surface, while
`Opc.Ua.Wot.WotNodeSetConverter` implements the complete WoT Binding draft.
Unknown asset-document members are ignored and cannot affect the emitted
AddressSpace.

It carries its security obligation directly rather than by reference to the
optional registry backing, so a server implementing only this surface still
inherits it. `CreateAsset`, `DeleteAsset`, `CreateAssetForEndpoint`,
`ConnectionTest` and the `WoTFile` `Open` (write mode), `Write` and
`CloseAndUpdate` operations require role-based access control and a
`SignAndEncrypt` channel for every mutation, whether or not the registry backs
them. The rule against dereferencing a URI found in a document extends to the
`WoTFile` upload path, which reaches the same materializer.
