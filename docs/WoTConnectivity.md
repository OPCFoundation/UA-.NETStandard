# OPC UA WoT Connectivity (OPC 10100-1)

This repository implements the OPC UA **WoT Connectivity** companion specification (OPC 10100-1, "WoT Connectivity for OPC UA") through the model, client, server, and protocol-binding libraries plus integration tests and runnable aggregation samples:

| Project                          | Purpose                                                       |
|----------------------------------|---------------------------------------------------------------|
| `Opc.Ua.WotCon`                  | Source-generated information model (NodeStates, NodeIds, generated ObjectType client proxies) generated once from the combined **WoT Connectivity 1.2 draft** NodeSet2 (incorporating the OPC 10100-1 v1.02 model plus additive registry nodes in one namespace) and the **xRegistry 0.7.0 draft** base NodeSet2 (see §11) |
| `Opc.Ua.WotCon.Server`           | Server-side node manager (`WotConnectivityNodeManager` → `AsyncCustomNodeManager`) and the extensible provider model |
| `Opc.Ua.WotCon.Client`           | Client wrappers + extension methods that compose the generated proxies without inheritance, covering both the OPC 10100-1 v1.02 asset-connection surface (`WotConnectivityClient`) and the registry surface (`WotRegistryClient`, see §11.8) |
| `Opc.Ua.WotCon.Bindings`         | Protocol-binding abstractions, planners, codecs, credential references, HTTP/Modbus/OPC UA executors on net8+, and the generic target-mapping channel factory |
| `Opc.Ua.WotCon.Bindings.Mqtt`    | Optional MQTT executor package |
| `Opc.Ua.WotCon.Tests`            | NUnit tests covering the TD parser, mappers, simulated provider, discovery facade |

The model namespace URI is `http://opcfoundation.org/UA/WoT-Con/`.
The combined input is draft version `1.2`, dated 2026-09-12; the incorporated
published `1.02.0` ModelDesign remains unchanged.

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
The `WoTFile` BrowseName belongs to the WoT model namespace; its NodeId remains
in the configured asset namespace.

### Lifecycle

1. Client calls `CreateAsset(name)` → server creates an `IWoTAssetType`
   instance (`HasInterface` reference) with a single `WoTFile` child.
2. Client opens `WoTFile` with mode `Write|EraseExisting` (the only
   write mode allowed per Spec §6.3.10), writes a JSON TD, and calls
   `CloseAndUpdate`.
3. Server parses the TD and prepares any native/existing-type binding through
   the shared semantic converter before provider connection or graph mutation.
   It then selects a registered
   `IWotAssetProviderFactory` whose `CanHandle` accepts it, connects
   the resulting provider, and materialises a property variable for
   each WoT property (mapped per Table 14) and a method node for each
   WoT action (mapped per §6.3.9).

Native/type-bound descriptions use the converter's NodeSet/declaration facts
instead of the reduced primitive mapper. The resolved ObjectType and declaration
QNames/types are applied to the existing asset owner; Variables retain the
legacy `HasWoTComponent` relation and action/argument metadata remains native.
See [legacy existing-type bindings](WoTLegacyTypeBindings.md) for the complete
preparation, identity, source-preservation and direct/DI contract.

Optional flow when `DiscoverAssets` / `CreateAssetForEndpoint` /
`ConnectionTest` are wired:

1. `DiscoverAssets` returns a list of asset endpoints.
2. `ConnectionTest` verifies one of them.
3. `CreateAssetForEndpoint(name, endpoint)` synthesises a TD via
   `IWotAssetDiscoveryProvider.CreateThingDescriptionAsync` and runs
   the same materialisation path before publishing the new asset owner —
   no client upload needed. Persisted native documents are also prepared before
   creating their owners during startup; invalid source files are retained.

Uploaded UTF-8 document bytes remain authoritative for file downloads, persistence,
and registry mirroring. The provider-facing `ThingDescription` is a parsed
projection, not the document used to recreate those bytes. Restored files retain
their original content, including an accepted UTF-8 byte order mark; discovery-created
files expose the generated description. File persistence stages the complete
document before replacing the committed file, and a failed write is surfaced to
the caller. `CloseAndUpdate` awaits materialisation and persistence without
blocking on an asynchronous callback.
Multiple read handles may coexist, but a writer excludes other readers and writers,
including while `CloseAndUpdate` awaits completion. Reads require a positive length;
seeking beyond the file clamps the position to its end.
Closing a Session releases its still-open read and write handles without committing
pending writes, allowing subsequent Sessions to use the file.

Materialized properties, actions and argument nodes are indexed for service
access. Replacing the description removes obsolete interaction subtrees and
both directions of their explicit asset references. Removed property/action
callbacks are detached, so old node objects cannot invoke the replacement
provider through an obsolete interaction.
Argument node identities live beneath their owning action's path, separately
from authored action names such as `Reset_in` or `Reset_out`; asset and action
identities remain unchanged.

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
enabled. Each mirrored asset retains the registry and assigned resource identity
returned by upsert, so updates and deletion do not guess an identifier from the
asset's display-name casing. Mirroring is best-effort: registry rejection or I/O
failure is logged and the asset lifecycle still succeeds. This is distinct from
the file-persistence failure behavior described above.

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

WoT action bindings use a separate, single-source invocation path. An OPC UA
form's `uav:callObjectId` identifies its source receiver independently of the
Method target. A present invalid receiver is rejected rather than replaced by
a fallback. Local `uav:componentOf` arrays describe model placement, not a source
receiver; the earlier scalar form-scoped receiver spelling remains readable
with a compatibility warning.

Standard planners retain complete input/output schemas and converter-resolved
argument layouts on the compiled payload descriptor. An ordinary native Call
rejects a declared argument-count mismatch before sending it. Occurrence-level
Condition actions retain the native EventId/Comment positions: an explicitly
optional missing Comment becomes `LocalizedText.Null`, while a required missing
Comment is rejected. This path does not retry a different form or source.

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

## 11. WoT Connectivity registry and materialization (preview)

The `Opc.Ua.WotCon` assembly is source-generated once from the combined **WoT Connectivity 1.2 draft** NodeSet2, which incorporates the published OPC 10100-1 v1.02 model (NodeIds `1..172`, superseded in capability but **not** deprecated) plus the additive registry nodes (`64000+`) in one namespace, and from the abstract **xRegistry 0.7.0 draft** base model the registry types build on:

| Model | Namespace | Emitted C# namespace |
|-------|-----------|----------------------|
| xRegistry 0.7.0 draft (abstract registry base) | `http://opcfoundation.org/UA/xRegistry/` | `Opc.Ua.XRegistry` |
| WoT Connectivity 1.2 draft (combined) | `http://opcfoundation.org/UA/WoT-Con/` | `Opc.Ua.WotCon` |

The NodeSet2 models are *pinned* from the OPC UA drafts authoring repository and added as `AdditionalFiles`. xRegistry has one authoritative copy in `src/Opc.Ua.XRegistry`; Connectivity's NodeSet and NodeId CSV are in `src/Opc.Ua.WotCon/Design`. The legacy 1.02 `WotConnection.xml` / `WotConnection.csv` sources are retained under `Design/` for reference only — they are incorporated into the combined NodeSet and are **not** source-generated a second time, so the preserved 1.02 constants and the additive registry constants coexist in one `Opc.Ua.WotCon` namespace under their exact NodeIds. The tooling that refreshes the pinned copies from the draft repository lives in that authoring repository, not here.

### 11.1 Architecture

The runtime separates a **stable registry** from **ephemeral projections**:

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

The registry's startup refresh is an [awaited readiness
phase](NodeManagerReadiness.md), not part of address-space preparation. Both
ordinary `AddWotRegistryServer` startup and runtime
`WotRegistryNodeManagerFactory` registration await persisted-document
materialization before returning. This also applies when `AutoRefresh` is
`false`; no explicit `Refresh` is needed to read successfully restored nodes.

Startup cancellation reaches the real projection host. Failed materialization
results fail readiness with `BadConfigurationError` and remain observable in the
registry, rather than being swallowed. A runtime parent is already committed at
this point: its failed Add reports the retained live registration for recovery or
removal. Removing that parent awaits cleanup of its actual dependent projections.
Initial server startup instead runs ordered server cleanup before propagating
failure. A custom host that only calls `CreateAddressSpaceAsync` must explicitly
invoke readiness after initializing the server; see the linked migration note.
Concurrent native `Refresh` calls receive `BadServerTooBusy` while startup owns
the existing refresh admission gate, and can be retried after startup completes.

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
* `FileWotRegistryStore` — durable; metadata is written with a **bounded atomic replace** (write-to-temp then `File.Replace`), one blob per version, content-addressed directories. Typed resources reject missing, ambiguous, changed or wrong-kind source authority before replacing bytes, independently of optional validation policies.

#### Exact authority and typed provisioning

The stock service implements the additive `IWotTypedRegistryService` capability and
the six generated draft provisioning Methods. A group is keyed by `(Kind, exact CatalogUri)`;
a resource by `(owning group, exact ThingId/ModelId)`. TD and TM groups can share
the same catalogue URI. Case, scheme, query, fragment, trailing slash and escaped
bytes remain significant; `Uri` is used for validation, not canonicalization.
New groups use `td.` / `tm.` readable domains and the bounded
[xRegistry allocator](XRegistry.md#source-derived-identifier-allocation). Resources
use the same allocator within their owning group. Names, slugs and hashes are
never a substitute for authoritative identity.

The existing nested immutable Group/Resource records are the durable allocation
map. Manifest schema **5** adds exact CatalogUri and explicit authority-presence
markers, and validates ownership, duplicate assignments/authorities, kind,
source identity and exact default-Version references on recovery. Schema 3/4
remain readable: missing legacy catalogue authority stays explicitly unbound,
not reconstructed from a Name. An explicit configured binding can establish an
unambiguous legacy catalogue without renaming its assigned IDs. Incomplete or
contradictory legacy authority cannot be used for typed provisioning.

`CreateDocumentGroup(Kind, CatalogUri)` and `GetOrCreateDocumentGroup` return the
assigned GroupId. Group-specific `CreateThingDescriptionResource(ThingId, VersionId,
RequestFileOpen)` / `CreateThingModelResource(ModelId, VersionId, RequestFileOpen)`
return distinct logical Resource and exact Version NodeIds plus both assigned IDs.
Their GetOrCreate forms additionally report independent CreatedResource and
CreatedVersion flags. Metadata, mapping and structure are committed before return.
Typed Create with an empty VersionId allocates the next Version; GetOrCreate with
an empty VersionId selects the current default. Creating a new Version does not
silently select it as default.

RequestFileOpen=false opens nothing and returns zero. When true, the actual
session-owned exact-Version write handle is prepared before durable commit, using
the existing file manager. Its staged bytes initially equal committed content,
its position is zero, and it does not request erase or append. A clean Close
changes neither bytes nor epochs. Dirty Close validates exact source identity
and kind before committing. Open/preparation failure cannot leave a new allocation.
Session discard invalidates a prepared handle and cancels the same creation
transaction passed to the store. A discard before the store's commit point rejects
the call with `Bad_SessionClosed`, without publishing an allocation. Once the store
has committed, normal snapshot publication completes even if the Session then
closes; cleanup discards the handle, not the already committed registry generation.

Inherited xRegistry signatures and legacy 1.02 model identities are unchanged.
Already provisioned generic identifiers resolve normally. New generic names
require `WotRegistryServerOptions.IdentityBindings`, supplied through direct
construction or `AddWotRegistryServer`:

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

These are explicit aliases, not naming conventions. Generic Methods return the
actual assigned identities; aliases cannot allocate a second entity for an
already mapped authority. Missing or contradictory authority is
`Bad_InvalidArgument`. Existing unbound programmatic snapshots remain a legacy
compatibility surface, not a claim of typed document-write conformance.
Both legacy programmatic group creation APIs check case-insensitive identifier
occupancy before entering any store. Exact assigned IDs still resolve, but a
case-colliding supplied ID cannot create a second assignment or be normalized into
a different typed assignment. The historical normalizer remains available for
unbound legacy groups. These service checks do not depend on file-store validation.
Custom services may implement `IWotTypedRegistryService` additively. The native
fileless path uses the existing versioned projection, including distinct logical
Resources, typed Versions containers and exact Versions, without requiring the
separate legacy `IWotVersionedRegistryService` mutation capability. The
atomic requested-file-open path currently requires the stock transactional
service; other providers receive `Bad_NotSupported` before mutation rather than
a post-commit or authority-dropping fallback.

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

1. Resolves `Kind`, exact Version and optional dependent selection from registry
   metadata before acquiring bodies. Empty selection selects enabled inputs;
   a nonempty unmatched selection performs no materialization work.
2. Captures the selected/required exact inputs, original context and content,
   semantic edges and owner-issued Version leases. References resolve by exact
   identity rather than arbitrary URL suffixes. Disabled dependencies may supply
   definitions without becoming executing owners; individual acquisition failures
   remain associated with their closures.
3. Partitions the graph into **dependency closures** (weakly-connected
   components), preserving legal semantic strongly connected components and
   separately checking ordering constraints. Thing Models precede the
   descriptions that extend them; inheritance cycles and missing dependencies
   produce deterministic diagnostics.
4. Validates/converts captured inputs to one or more NodeSet2 documents and projects
   the closure as one runtime NodeManager (Add, or graceful/immediate
   reload on update according to `RetirementPolicy`).

The existing native `DependencySnapshot` and `LastDependencyAttempt` Properties
distinguish committed graphs from actual attempts; dry runs update neither.
See [selected dependencies and exact-Version snapshots](WotDependencySnapshots.md)
for direct/DI usage, authoritative origins, native reads and the separate
whole-manifest store-integrity boundary. Selection-scoped acquisition does not
promise selected-only backing-store validation I/O.

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
* The NodeManager emits the concrete `WoTValidationFailureEventType`,
  `WoTLoadFailureEventType`, `WoTBindingFailureEventType`, and
  `WoTRefreshCompletedEventType`. The abstract `WoTResourceEventType` is never
  instantiated for successful activation. The coordinator's application-level
  `Resource` notification remains available to in-process subscribers; native
  xRegistry events describe resource and version changes.

### 11.4 Binder integration seam

`IWotBinderRegistry` is the runtime-neutral seam the coordinator uses during Prepare/Activate/Deactivate. `WotProtocolBinderRegistry` implements that seam and `IWotBindingChannelFactory`, compiling immutable plans from the registered binders and opening channels through independently registered executors. The base `Opc.Ua.WotCon.Bindings` package ships all eight planners and bundles HTTP, Modbus TCP, and OPC UA executors on `net8.0`, `net9.0`, and `net10.0`; MQTT remains in `Opc.Ua.WotCon.Bindings.Mqtt`. The base package retains the full `net472;net48;netstandard2.1;net8.0;net9.0;net10.0` matrix, where planner-only validation remains available even when concrete executor namespaces are not compiled.

The generic projection runtime is implemented in `Opc.Ua.WotCon.Server.Materialization`. It resolves affordance-level OPC 10101 target mappings against freshly imported runtime NodeSets, wires async read/write handlers, opens one lazy channel per compiled form per generation, lets local monitored items sample the same read handler, supports reflection-free structured field mapping, and disposes channels with their owning generation. Updates use shadow reload, so existing monitored items keep the retired generation alive until they drain while new reads and monitored items use the replacement generation.

Direct and structured-field property reads retain values and source timestamps with Uncertain quality. The handlers combine the mapped operation status and DataValue quality without lowering severity; composed reads use the oldest field timestamp and the first non-default status at the highest severity. Property writes preserve successful subcodes such as `GoodClamped`, and a structured write reports a Bad field result even when an earlier field reported Uncertain.

Property results carry their source message context when supplied by the channel. The runtime translates namespace-bearing direct values and structured fields by URI, and contextual writes translate into the source Server's namespace table without extending that remote table. Context-free channels remain compatible for namespace-independent values; a session-local identifier without an authoritative context fails instead of being reinterpreted locally. The optional `IWotPropertyBindingChannel` capability carries read ranges, data encodings, and write contexts. The OPC UA executor forwards native index ranges, while other channels use local read-range/encoding processing and reject unsupported indexed writes before writing. Structured field mapping rejects an index range on the composed write rather than applying that range to every source field.

The default `NullWotBinderRegistry` remains the no-binding baseline. With it, affordance forms either **fail a strict closure** (`StrictBindings = true`) or **materialize as degraded nodes** (`BadConfigurationError`) when non-strict. Protocol support is opt-in: `AddWotProtocolBinders()` registers all eight planners, while `AddHttpWotBinding()`, `AddMqttWotBinding()`, `AddModbusWotBinding()`, and `AddOpcUaWotBinding()` add their concrete executors. The core server registers none of these by default; see [WoT protocol bindings](WotBindings.md).

The registry exposes `SupportedBindings` as a browseable folder of
`WoTBindingType` objects, including an empty folder when no binders are registered.
Standard descriptors expose `BindingUri`, `Title`, `ProfileVersion`,
`DraftMaturity`, `Enabled`, `ContentTypes`, and the `Capabilities` structure as
individual read-only Properties. Descriptor and property NodeIds are distinct
per binding URI/version and never reuse model declaration NodeIds. `Enabled`
reports whether the registered binding has effective runtime operations: a
planner without an executor remains discoverable but is disabled. It does not
report a particular remote endpoint's connection state.

The read-only registry `SelectedBindings` array is a detached, deterministically
ordered snapshot of bindings in currently published projection plans, not a copy
of all registered capabilities. An unused registered binder is absent; retiring
the last plan using a binding removes it from the selected set but leaves its
registered descriptor available. Direct callers can capture the same selected set
with `WotMaterializationCoordinator.GetSelectedBindingCapabilitiesAsync`.
Snapshot capture waits for the coordinator's current operation; it does not add
an independent publication or transaction mechanism.

Older custom `IWotBinderRegistry` implementations may omit optional title,
profile-version, or maturity metadata. Those Properties remain absent rather than
reporting invented values, and unversioned identities remain distinct from
explicitly empty versions. A binding URI must be nonblank. Identical repeated
capabilities share one descriptor; conflicting snapshots with the same URI/version
fail registry configuration instead of silently choosing one. Clients decoding
the capability structure directly from a stock Session register the generated
type with that Session's factory:

```csharp
session.MessageContext.Factory.Builder
    .AddEncodeableType<WoTBindingCapabilityDataType>()
    .Commit();
```

### 11.5 Legacy 1.02 compatibility

The legacy `WotConnectivityNodeManager`, its generated 1.02 namespace/NodeIds/method signatures and the client APIs are unchanged. When both features are hosted, legacy-created assets are additionally registered as Thing Description resources in a configured legacy group (`WotRegistryServerOptions.LegacyGroupId`) so they participate in registry materialization, without making the flat legacy asset list canonical for the registry.

### 11.6 Protocol and projection scope

The implemented data plane covers executable HTTP, Modbus TCP, MQTT, and OPC UA binding forms according to the operation coverage documented in [WoT protocol bindings](WotBindings.md). CoAP, BACnet, PROFINET, and LoRaWAN currently ship as planner-only binders: their forms are validated and represented in plans, but a non-strict closure is degraded until an executor is registered.

OPC 10101 target mapping is authored on property affordances, not forms. `uav:mapByFieldPath` requires `uav:mapToType`, portable `nsu=` NodeIds are resolved against the runtime generation's namespace table, and the mapping is protocol-neutral as illustrated by [OPC 10101 §8.2](https://reference.opcfoundation.org/specs/OPC-10101/8.2). See [OPC 10101 §6.5.4](https://reference.opcfoundation.org/specs/OPC-10101/6.5.4) and the [binding-authoring guide](WotBindings.md#adding-your-own-binding) for the exact validation and runtime semantics.

### 11.7 Browseable registry projection and management Methods

The stable `WoTRegistryNodeManager` materializes the registry snapshot as a browseable object tree and wires the inherited xRegistry / registry Methods:

* For every service group a `ThingDescriptionGroupType` or
  `ThingModelGroupType` object is created beneath `WoTRegistry`. Each group
  organizes stable logical `ThingDescriptionFileType` / `ThingModelFileType`
  Resources. A logical Resource owns a typed `ResourceVersionsType` container
  whose children are distinct exact Version document nodes. NodeIds are stable and
  deterministic, derived from the registry Xid (for example
  `WoTRegistry/groups/{groupId}/resources/{resourceId}`). The projection is
  reconciled on every registry `Changed` event — including projection-only
  callbacks, which never re-trigger materialization — and removes group and
  resource nodes as they disappear from the snapshot.
* Each node carries its xRegistry and registry metadata (ids/Xid/epoch/name/
  description/timestamps/format/content type, desired/default/active
  version, enabled/load state, validation outcome, content digest,
  materialized-node count, the materialized `RootNodeId`, and selected
  bindings). Resource Meta owns membership/default selection, Meta labels and
  Meta epochs/timestamps; each Version owns its bytes, Version labels, epoch,
  timestamps and validation result. Logical non-Meta fields and new file Opens
  select the default, not the active or desired Version. `HasNotifier` references
  chain Server -> WoTRegistry -> group -> logical Resource -> exact Version.
  Native xRegistry Resource events use the logical node and Version events use
  the exact node. WoT validation, load, and binding failures identify the supplied
  exact Version, not the serving or default Version. Projection transitions and
  WoT failure/completion delivery share one FIFO, so an earlier Version creation
  is applied before its failure and later queued deletion follows delivery.
  Native mutation-triggered reconciliation uses that same ordered dispatcher.
  Cancellation can stop a caller's wait without canceling already accepted
  projection work or preventing address-space cleanup.
  Mutable validation and summary payloads are copied when queued. Missing or
  invalid identities and providers without an exact-Version projection produce
  explicit diagnostics instead of substituting a Resource or registry source.
  Phase and generation fields come from the coordinator. The registry object
  remains the source for refresh completion.
* The xRegistry `CreateGroup` / `GetOrCreateGroup` (on `WoTRegistry`),
  `CreateResource` / `GetOrCreateResource` / `Delete` (on a group) and the
  document `Delete`, `Validate`, `SetEnabled` and `SetDefaultVersion` (on a
  resource) Methods are wired to the registry service, enforcing
  `ExpectedEpoch` optimistic concurrency and the management access policy.
  Logical Delete uses MetaEpoch and removes the Resource; exact Version Delete
  uses that Version's Epoch even when the Version is currently default or has
  the same id as its Resource. Domain lifecycle policy still applies.
  Registry mutations require a `SignAndEncrypt` SecureChannel; deployments
  may separately permit read-only registry access over `SecurityMode.None`.
* The inherited FileType (`Open` / `Read` / `Write` / `Close` /
  `GetPosition` / `SetPosition`) transfers the document body with
  per-session handles, a single exclusive writer and bounds. A logical handle
  remains pinned to the exact Version selected at Open, including its cursor
  and writer reservation. Creating a Version and committing its bytes are
  separate operations; writing an existing exact Version does not silently
  create another one. A rejected v2 activation can leave v1 active while v2
  remains the selected default: new logical reads return v2, while an old v1
  handle still reads v1. Closing that old handle releases its v1 reader without
  replacing the logical FileType view: `Size`, `OpenCount` and the other file
  properties continue to describe the current default, including any v2 readers
  that remain open.
* Retention and restart use the existing WoT service and FileStore contracts,
  not new generic registrar options. At a retention limit of two, active,
  default, independently desired and incoming Versions must fit; a commit or
  allocation that cannot retain them is rejected. Pending allocation does not
  evict committed content, and pending Close applies retention atomically.
  Schema-5 persistence retains the applicable selections and pending state.
  The stock service's optional `IWotRegistryVersionLeaseProvider` also protects
  an otherwise unselected Version while any file lease remains. Exact/logical
  read and write handles share this owner protection; Close, cancellation and
  Session abandonment release only the corresponding leases. Typed creation
  transfers its owner-issued lease into the existing prepared file reservation.
  Older providers do not implicitly acquire this guarantee. See
  [Version leases](WotRegistryVersionLeases.md) for direct/DI use and lifetime limits.
* Every browseable registry/group/resource node also carries the inherited
  optional `Labels` (`AttributesType`) container. Each label is persisted as
  an ordinally-ordered key/value pair on the owning `WotRegistrySnapshot` /
  `WotResourceGroup` / `WotResource` model and materializes as its own
  `PropertyType` child with a deterministic NodeId (for example
  `WoTRegistry/groups/{groupId}/labels/{key}`) and a safe, collision-checked
  BrowseName. The container's `AddAttribute(Key, Value, ExpectedEpoch)` and
  `RemoveAttribute(Key, ExpectedEpoch)` Methods enforce the management access
  policy, optimistic-concurrency `ExpectedEpoch` (Group Epoch, Version Epoch,
  or logical Resource MetaEpoch according to the addressed owner; the registry Labels compare
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
  Version-level labels are stored on the immutable `WotResourceVersion` and
  materialized beneath each exact Version's `Labels` container. The logical
  `Labels` view follows the default Version; `MetaLabels` is independently
  Resource-owned. Identical bytes/labels leave owned epochs and timestamps
  unchanged. See [xRegistry roles and default views](XRegistry.md#resource-meta-and-default-version-views).

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
* `CreateDocumentGroupAsync(kind, catalogUri)` / `GetOrCreateDocumentGroupAsync` use the generated typed Methods. Group-specific typed resource entrypoints accept exact ThingId/ModelId; `CreateDocumentResourceAsync` / `GetOrCreateDocumentResourceAsync` select the receiver's typed Method. Every typed call verifies its namespace-qualified receiver, complete scalar argument layout and Executable/UserExecutable attributes. There is no generic fallback.
* The older `CreateThingDescriptionGroupAsync` / `CreateThingModelGroupAsync` conveniences and their GetOrCreate counterparts retain the inherited generic signatures and therefore require the corresponding explicit server binding for a new group. They discover the returned type and read the actual assigned GroupId instead of treating the supplied alias as an allocation.
* `WotRegistryGroupClient.CreateResourceAsync` / `GetOrCreateResourceAsync` call the group's `CreateResource` / `GetOrCreateResource` Methods and return a `WotRegistryResourceClient` plus the server-assigned version id.
* `WotRegistryResourceClient.UploadNewVersionAsync(ByteString | Stream, …)` allocates a new document Version and uploads through the inherited `FileType` `Open(Write|EraseExisting)` -> `Write` -> `Close` primitives (the same `FileTypeClientExtensions` used elsewhere in this package). `DownloadAsync` reads the addressed exact Version, or the selected default when addressed through a logical Resource, using the shared xRegistry document helper; it does not prefer the active Version. `DownloadToAsync` streams into a caller-owned `Stream`. `ValidateAsync`, `SetEnabledAsync`, `SetDefaultVersionAsync` and `DeleteAsync` call the matching document Methods.
* `WotRegistryClient.RefreshAsync` / `RefreshAllAsync` call the generated `Refresh` Method and return a typed `WotRegistryRefreshResult` (`Summary`, `Results`, `NewGeneration`, `HasFailures`, `EnsureSuccess()`).
* `WotRegistryClient.LoadDocumentsAsync` loads a caller-supplied `ArrayOf<WotRegistryDocument>` (an immutable `Kind`/`GroupId`/`ResourceId`/`Content` (`ByteString`)/`VersionId` descriptor), get-or-creating each target group/resource and uploading its content, then optionally calls `RefreshAllAsync` — one workflow. Thing Models are always processed before Thing Descriptions (preserving the caller's relative order within each kind) so referenced models are materialised before the descriptions that depend on them. A mutation failure or a group/document kind mismatch aborts immediately (`ServiceResultException`); a refresh failure is *not* thrown — it is surfaced on the returned `WotRegistryBulkLoadResult.Refresh` for the caller to inspect, since a partial refresh outcome is legitimate application data.

```csharp
WotRegistryClient registry = await WotRegistryClient.ForServerAsync(
    session, session.MessageContext.Telemetry, ct);

(WotRegistryGroupClient group, _) = await registry.GetOrCreateDocumentGroupAsync(
    WoTDocumentKindEnum.ThingDescription, "https://contoso.org/plant/things/", ct);
WotRegistryResourceAllocation allocation = await group.CreateThingDescriptionResourceAsync(
    "urn:plant:sensor01", requestFileOpen: true, ct: ct);
// The document must retain this exact id. The returned handle belongs to this exact Version.
ByteString document = ByteString.From(await File.ReadAllBytesAsync("sensor01.td.json", ct));
await allocation.Version.Proxy.WriteAsync(allocation.FileHandle, document, ct);
await allocation.Version.Proxy.CloseAsync(allocation.FileHandle, ct);

WotRegistryRefreshResult refresh = await registry.RefreshAllAsync(ct: ct);
refresh.EnsureSuccess();
```

Register the registry client with DI alongside `AddWotConClient` via `AddWotRegistryClient` (on `IOpcUaBuilder` or `IOpcUaClientBuilder`, bindable from `IConfiguration`/`IConfigurationSection`, default section `OpcUa:WotCon:RegistryClient`). It follows the same lazy `ManagedSession`-backed factory pattern: resolve `Func<CancellationToken, Task<WotRegistryClient>>` for the lazily connected form, or `Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>` to wrap an already-connected session.

## 12. Model identity and runtime conformance

This clause describes what the model requires and what this implementation
provides. It is a statement of the current state, not a history of how either
got here.

### 12.1 Model identity

The information model is generated from the reviewed draft NodeSets, adopted
verbatim rather than maintained by hand. These are unpublished successor
declarations, not a claim that every declared capability is implemented.

| Model | Draft version | PublicationDate metadata |
|---|---|---|
| WoT Connectivity | `1.2` | 2026-09-12 |
| xRegistry (`RequiredModel`) | `0.7.0` | 2026-09-12 |

xRegistry contributes 131 nodes and Connectivity 349. Both retain the Core
`1.05.04` dependency with date `2025-01-08`, matching the incorporated legacy
input. Binding vocabulary and protocol-format versions are independent of these
NodeSet model identities; the Binding vocabulary is not a `RequiredModel`.

The generated successor surface includes typed provisioning Methods, canonical
capability snapshots, origin/dependency/plan Structures, event-binding descriptors
and projection-group declarations. Generated classes and enum members are not
runtime support discovery: a client must check the applicable server capability
before using an optional contract. Stock typed provisioning is implemented as
described above; this does not certify the complete successor transaction,
dependency, event-mode or profile contracts.
In particular, `All = 2` is a selector-only value, not a document kind: snapshot,
creation, upload and executable-plan boundaries reject it.

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

A **projection document** is a `WoT-Projection/1.2` plan, not an already
resolved Thing Description or Thing Model. It names source documents and
states which of their affordances a view is assembled from. Its selectors
are references and annotations, not executable InteractionAffordances.

This completes the NodeClass binding. Seven OPC UA NodeClasses bind to a WoT
construct that defines something; `View` is the eighth and the only one whose
purpose is to select rather than define. A View owns no Node — it organizes Nodes
that already exist so a client can browse a subset shaped for one task — and a
projection document is that construct in WoT.

A projection is marked by `uav:projection` in its `@type` and declares:

| Term | Meaning |
|---|---|
| `uav:projectionKind` | required resolved result kind: `ThingDescription` or `ThingModel` |
| `uav:scenario` | absolute IRI naming the purpose the view serves |
| `uav:projects` | non-empty manifest of the documents it projects |
| `uav:sourceName` | alias for a source, unique in the manifest |
| `uav:routing` | `source` (default) or `projection` |
| `uav:sourceDigest` | `sha-256:<hex>` pinning a source revision |
| `uav:namePrefix` | prefix applied to bulk-selected names |

The current plan root carries `uav:projection`, without `Thing`,
`tm:ThingModel`, or an OPC UA NodeClass annotation. `WotProjection.ResultKind`
provides its declared output kind; the View builder uses that value rather
than classifying the unresolved root as an ordinary TD/TM. Resolution removes
`uav:projection` and `uav:projectionKind` and supplies the ordinary result's
`Thing` or `tm:ThingModel` marker.

Use `WotProjection.Format` and `WotProjection.ContentType` for its registry
metadata: `WoT-Projection/1.2` and
`application/ld+json; profile="http://opcfoundation.org/UA/WoT-Binding/v1.2/projection"`.
A source manifest accepts `application/td+json`, `application/tm+json`, or
that projection media type for a nested plan. Its media type must describe
the fetched source role; enabling compatibility does not disguise a modern
plan as an ordinary TD or TM.

Direct `WotRegistryService.UpsertResourceAsync` calls require the corresponding
Format, ContentType and stored result kind. `DetectProjectionFormat`, when
explicitly selected on an upsert request, classifies an authored projection role
before applying plan admission. The Full-registry FileType adapter uses that
mode, so the existing generated upload clients can store current plans without
claiming the bytes are ordinary TD/TM documents. It never enables legacy syntax.
The TD-only asset-upload and endpoint-generation paths reject unresolved plans,
as does ordinary NodeSet conversion. Restored plans are revalidated before any
runtime closure is published.

`WotProjection.Parse`, `WotProjectionResolver`, and `WotProjectionViewBuilder`
default to current-plan processing. Draft plans combining `uav:projection`
with an old `Thing` or `tm:ThingModel` marker require explicit compatibility:

```csharp
var options = new WotNodeSetConverterOptions
{
    ProjectionCompatibilityMode = WotProjectionCompatibilityMode.DraftProjection11
};
var resolver = new WotProjectionResolver(thingResolver, options);
```

For standalone parsing, use the `WotProjection.Parse` overload with that
compatibility mode. Exactly one old TD/TM marker must determine the result
kind. An invalid explicit `uav:projectionKind` is not a request for legacy
processing, and compatibility does not rewrite the original document bytes.

Hosted deployments select compatibility through
`WotRegistryServerOptions.ProjectionCompatibilityMode`; it reaches both registry
admission and View materialization. Direct registry construction also has an
explicit compatibility overload. Compatibility and format metadata participate
in refresh fingerprints. The compatibility mode is captured before body acquisition;
changing it invalidates the next refresh without changing an earlier capture's
fingerprint. Captured fingerprints also distinguish the stored document kind,
Format and ContentType independently of the Version identifier and content bytes.
Changing a Version's Format or ContentType invalidates
its format validation and selected runtime admission even if its bytes did not
change. A validation result cannot be attached to a replacement Version or to
different format metadata.

Source `href` values are resolved against the owning projection's effective
base for retrieval. Nested projections retain their own retrieved location and
resolve their authored base against it; source forms are not moved under the
outer projection's base. `WotProjectionManifestSource.Href` retains the authored
spelling, while generated `uav:resolvedFrom` uses the resolved source location.
Relative organizing-graph links are resolved at each owning document, including
cycle checks. Query-only references replace the previous query; fragments and
query text never become path segments. Dot-segment removal applies to the path,
including rooted relative paths, without rewriting opaque query values.

Selection has three forms. An enumerated `tm:ref` names one affordance and is the
only form that can annotate it; `uav:selectAll` takes every affordance of a
source; and `uav:select` filters on affordance kind, semantic identifier and type
tokens. The predicate set is closed — a filter carrying any other key is rejected
rather than ignored — so a filter stays decidable by inspection.

Type and semantic-ID predicates compare expanded identities in the filter's
original context and the candidate affordance's original context. Different
prefixes for the same namespace can match; equal spellings bound to different
namespaces do not. Local and term-scoped contexts, definition-time mappings,
type vocabularies and original document locations remain owner-specific.
Predicate identities must be established before source acquisition.

Constraints within one filter are conjunctive, including every listed type;
filters are disjunctive. A definite matching type or filter is not defeated by
an unrelated unresolved alternative. If the remaining evidence cannot determine
membership, resolution reports `ProjectionSelectorInvalid`, rather than
guessing a match or returning a successful partial view.
First-selection precedence is applied in the same deterministic total order
before evaluating later bulk candidates. An uncertain candidate cannot
invalidate an already selected name; uncertainty that could still determine
the winner remains an error.

Present controls are validated before source acquisition, including in nested
projections. `uav:sourceDigest`, `uav:routing`, and `uav:namePrefix` must have their
declared string shapes; a non-string value is not treated as an omitted pin,
route, or prefix. An explicit `uav:select` array and each filter must be non-empty.
A type predicate contains one non-empty string or a non-empty array containing
only non-empty strings, and a semantic predicate names an absolute IRI.
Malformed controls produce an error rather than an unconstrained selection.

Every member of `properties`, `actions` and `events` carries `tm:ref`. A member
without one is defining an affordance, which is the one thing a projection
document must not do.

An enumerated reference identifies a direct affordance in the matching source
map: a projected property selects `/properties/<name>`, an action selects
`/actions/<name>`, and an event selects `/events/<name>`. Document roots,
affordance maps, nested DataSchemas, action inputs and event payload schemas
are not affordance selections. RFC 6901 escaping retains names containing `/`
or `~`; it does not permit a selection to cross affordance kinds.

Selections retain their source location and canonical definition pointer.
The resolver carries the sibling string property named by `uav:unitProperty`
and the Condition event named by an action's `uav:actsOn`, reusing a dependency's
selected output name when it is already present. Selecting that event does not
add the source's other actions. Invalid dependencies fail the resolution rather
than leaving a pointer to an unrelated or missing member.

A supporting affordance keeps its source name when free. On a collision it uses
`q:d:<B64u(sourceName)>:<B64u(sourcePointer)>`, followed by the first unused
positive `:1`, `:2`, and so on when that spelling is already occupied.
Authored selections are not overwritten. Selected and supporting affordances
count against `MaxNodeCount`. Literal values and opaque metadata are not searched
for reference lookalikes.

Original `uav:resolvedFrom` provenance survives selection and support carriage;
relative provenance is resolved at the original containing document location,
not its runtime endpoint base. Conflicting document contents fetched under the
same source location in a plan cannot be used interchangeably.

The projection keeps its own ordered root context. Each carried affordance,
DataSchema, DataType, URI variable, security definition and form retains its
original effective context in an isolated scope, so a same-spelled prefix in
another source or the projection does not change its meaning. Local and
term-scoped overrides and explicit null resets remain effective; source-only
prefixes do not become projection-wide declarations.
Implicit `ua` and `uav` bindings are used only when no declaration or reset
blocks them. A total null context reset does not reintroduce those defaults;
an explicit later prefix declaration can restore the intended binding.

The known [TD 1.1 context](https://www.w3.org/2022/wot/td/v1.1) includes its
standard vocabulary and prefix scopes: TD terms at the root, JSON Schema terms
inside DataSchemas, hypermedia terms in forms, and security terms in security
definitions. A root-only `@vocab` override does not replace the standard
property-scoped vocabulary. A vocabulary-relative type such as `dataPoint`
therefore remains resolvable without a hierarchical document identifier; a
relative `uav:semanticId` still requires its own applicable document base.

Projection-routed forms retain projection ownership even inside source-owned
data. Host type and semantic annotations retain host meaning, while a host
title or description override carries its own term language without retagging
unchanged source text. Local alias and prefix chains and compact vocabulary
declarations are resolved before an annotation crosses owners; self-dependent
and indirect prefix cycles are rejected. Completed term, prefix and vocabulary
mappings retain the context in which they were defined. A later redefinition
or disabling of a dependency does not reinterpret an earlier completed mapping.
Property-scoped contexts are instead processed when applied to their carrying
objects, using the then-current enclosing context. Ordered redefinitions that
refer to completed mappings are not mistaken for simultaneous definition cycles.
The destination interpretation is checked separately before carriage.

If the host identity cannot be established, depends on an unacquired context
or import, or would be reinterpreted by the destination scope, resolution reports
`ProjectionContextConflict` rather than borrowing a source prefix or base.
Restoring a prefix also requires establishing its namespace dependencies;
the spelling of a URN alone does not make an unresolved prefix authoritative.
The same rule applies to text-predicate identities and inherited term languages.
An explicit null text predicate cannot acquire a default predicate.

A property-scoped context inherited from an earlier declaration is not assumed
to survive a later unacquired context. A subsequent explicit scoped declaration
can establish the facts needed by the annotations. Vocabulary fallback for a
bare type also requires known absence of an explicit term mapping: restoring
`@vocab` does not erase an alias an unacquired context may have introduced.
A later explicit term declaration can establish that token's identity.
This also applies when an example's relative context reference is supplied without its original location
or acquired context: the filename is not treated as proof of the context's
contents. Repeated semantic context members and invalid root or nested context
declaration kinds produce diagnostics before mutable cloning; an invalid
projection context is rejected before source acquisition.

Context document references resolve at the original
document location, not the device endpoint base. Ordered relative `@base`
entries use the preceding effective base. An opaque logical identifier alone
does not provide a hierarchical location for resolving a relative context URL.
Nested semantic contexts follow the same rules; context-looking keys inside
literal values or opaque metadata are not rewritten. Keys of declared JSON-LD
index maps are names rather than context declarations, including a key named
`@context`; a semantic object stored under such a key can still carry its own
local context. The fixed WoT maps, including `securityDefinitions`, retain
their map-entry interpretation even when no explicit root context is supplied.

Referenced reusable schemas are carried into `schemaDefinitions` without
overwriting the projection owner's definitions. Local references to selected
DataSchemas follow their selected output names, and recursive schemas reuse
the same output definition. Known DataSchema locations in otherwise unselected
properties, action input/output, event data/subscription/cancellation/response,
URI-variable declarations and local definition maps can supply schema-only
dependencies. Their containing schema root is carried into `schemaDefinitions`
with its original context and reference origin; this does not select or execute
the source affordance. Nested schema references reuse that root, including when
several references address different children. Literal values, opaque metadata,
forms and other non-schema locations do not become schema targets merely because
their containing unselected schema has already been copied. Declaration-map
entries named `const` remain ordinary schema names.
Reusable schemas count with affordances against
`MaxNodeCount`. Missing or malformed known references fail rather than leaving
a successful document with dangling local pointers.

Known DataType definitions referenced by a selected schema are carried once in
`uav:dataTypeDefinitions`. Inline full definitions become graph references;
local field and base-type dependencies share the same closure. Native-ID and
namespace-qualified name references also retain their known definitions.
Base-reference objects retain every supplied graph, name and native-ID form;
conflicting forms cannot disappear when a definition is carried. A local
definition pointer must address an indexed semantic definition, not a literal
lookalike. Reusing an outer definition still checks the other owner's
transitive dependencies.
Unreferenced source definitions are not copied merely because the source
contains them. DataTypes participate in the same `MaxNodeCount` budget.

Repeated complete definitions within one source are invalid. Across owners,
reuse requires the same expanded graph and native identities and agreeing
context-resolved facts. Unknown semantic terms retain captured context for a
conservative comparison; localized text uses its original declared locale and
known location references use their original document location. Opaque
metadata remains literal. Distinct graph nodes cannot claim one native
DataType identity. Malformed definition containers and reference shapes fail
explicitly rather than disappearing during carriage.

Known opaque members and literal `const`, `default`, `enum` and `examples`
values retain their received JSON representation during projection, including
duplicate literal keys, member order, whitespace, numeric spellings and string
escapes. They are not materialized as mutable unique-key dictionaries or
searched for semantic references. Comparison for definition reuse is separate
from output preservation; an incomparable literal can be reused only when its
received representation agrees exactly. Different opaque values are not merged.
Semantic objects still require unique keys, and an unknown term does not
automatically establish an opaque boundary.

Native round trips also preserve the owner context of literal members directly
on a DataType definition. Namespaced opaque keys retain their source meaning
without changing the projection root's context or the regenerated native
DataType identity.

Definition discovery distinguishes declaration-map names from annotation
predicates. Literal `const`, `default`, `enum` and `examples` values cannot
declare a DataType or provide a local definition target; fields with those
names remain valid. Repeated definition members are diagnosed before mutable
carriage. Ambiguous qualified names require a definitive graph or native
identity, but do not invalidate otherwise unambiguous references. An ambiguous
base reference must identify the base itself; the subtype's own identity does
not disambiguate it. Reuse compares known local references by their indexed
graph targets and recognizes standard
schema facets without treating irrelevant prefix aliases as different facts.
Unknown semantic terms retain their context even when prefixed with `uav:`.
When a base reference supplies both a standard DataType name and a native
identity, they must agree unless the closure supplies a matching definitive
custom type with that same BrowseName. Known definition identity takes
precedence over the built-in fallback. Both native conversion and projection
carriage use the reference's effective context and accept equivalent
namespace-zero URI forms.

Known schema references (`$ref`, a source definition's `tm:ref`, and
`uav:externalSchema`) retain their original document location. Relative external
references become origin-relative absolute references where that location is
absolute; this step does not fetch external schemas. Named
`additionalResponses[].schema` dependencies use the actual form owner's
definitions, so a host form cannot silently select a same-named source schema,
or vice versa.

Compact Binding references in `tm:ref` and `uav:externalSchema` are expanded
through their original owner's effective context before carriage. This does not
reinterpret JSON Schema `$ref` strings as JSON-LD vocabulary. Explicit event
`uav:eventSelectClauses[].tm:ref` entries participate in reference relocation;
the clause objects are not treated as DataSchemas or searched recursively.
When an EventType definition has been carried into the result, its mapped
document-local JSON Pointer is resolved against that actual held document
before any provider is consulted. The existing EventType shape, cycle and
depth checks still apply; a local pointer does not authorize external retrieval.
An executable source-routed TD selection requires non-empty source forms.
An abstract source-routed TM selection may retain an affordance without forms.
A source property with an explicit `const` is a static fact, not an executable
endpoint, and may likewise omit forms; this includes carried engineering-unit
properties. No form is fabricated for such facts.
Source-only dependencies carried to close `uav:unitProperty` or `uav:actsOn`
are supporting facts, not additional executable selections. Their carriage does
not require inventing a source endpoint. Explicit selections remain subject to
the executable-form check, and projection-routed support still requires actual
host forms.
Explicit draft-plan compatibility and context-free structural projection
fixtures retain their earlier carriage behavior. When such processing lacks
forms, it reports that executable forms were not established; that result is
not executable-TD admission proof. Selecting draft compatibility does not relax
the form checks on a current plan with its declared WoT context.

External acquisition is a separate, caller-configured stage of the public
conversion interface. `WotNodeSetConverter.ToNodeSetResultAsync` already invokes
`WotEventSelectionResolver` when a Thing resolver is supplied, and
`WotExternalSchemaResolver` when the caller supplies that module. The same
modules can be called directly against a resolved projection document:

```csharp
WotConversionResult<WotEventSelectionCatalog> selections =
    await new WotEventSelectionResolver(allowedThings, options)
        .ResolveAsync(resolvedView, resolutionContext, cancellationToken);

WotConversionResult<UANodeSet> conversion =
    await WotNodeSetConverter.ToNodeSetResultAsync(
        resolvedView, options, allowedThings, resolutionContext, allowedNodes,
        new WotExternalSchemaResolver(allowedSchemas), cancellationToken);
```

The direct module call and the conversion call are alternative entry points;
applications need not resolve the same selection twice. A required EventType
link or explicit clause must resolve before event planning. Missing definitions,
cycles and caller policy failures are not successful event closure. Retrieved
TD-link hops retain their retrieval location rather than adopting that TD's
runtime endpoint `base`; the existing Thing Model document-base/scoped-base
resolution convention is retained.

A supporting `uav:externalSchema` is not a replacement DataType authority.
No configured provider means `NotEvaluated`; no answer means `Unresolved`;
conflicting answers are `Ambiguous`; a compared disagreement is `Incompatible`.
The converter reports those dispositions under its existing rules and never
changes the canonical data to fit an external schema. An HTTP-shaped identifier
does not itself authorize retrieval. Unselected affordances, literal instance
values, opaque metadata and generic retained schema references are not acquisition
requests. These distinctions are Binding checks, not a generic JSON Schema
validation engine.

For bulk-selected and supporting projection-routed affordances, an application
can supply **`IWotProjectionFormProvider`** through
`WotNodeSetConverterOptions.ProjectionFormProvider`. Its typed
`WotProjectionFormContext` identifies the original projection location and
effective endpoint base, selected source location and pointer, final affordance
name and kind, result kind, and shared resolution context. It returns an
`ArrayOf<JsonElement>` containing the forms the host actually serves, in their
intended order. This is an endpoint-description seam, not an endpoint publisher.

Authored forms take precedence; an empty or malformed authored form declaration
is not repaired by calling the provider. Source-routed selections never call it.
An enumerated projection-routed member must declare its own serving forms:
omission is invalid, not a request for provider fallback. Enumerated members are
checked before any host-provider invocation; fallback supplies only bulk and
supporting selections. Explicit draft compatibility retains its separately
reported structural behavior, not provider-based repair of enumerations.
Provider forms must be detached JSON objects. Relative hrefs use the original
host base and become absolute; their scheme, host and port must remain in the
host's origin. A source endpoint cannot acquire host credentials by being copied
into the response. Security requirements and named response schemas resolve
against host definitions, while the selected data domain and `uav:resolvedFrom`
remain source-owned.

Generated forms are held in a separate owning document until context and
dependency carriage completes. The original plan is not mutated, and a generated
JSON element is never treated as an authored element of that plan. Host URI
variables, credential-variable conflicts, form contexts and response-schema
dependencies use the existing owner-scoped closure.

Each provider request and returned payload uses the shared document/byte budget.
Generated documents also obey the configured JSON size/depth bounds. The provider
must bound its own I/O and honor caller cancellation, including timeout tokens.
No provider, no forms, malformed forms, or expected I/O, invalid-operation,
timeout, JSON and format failures produce an unsuccessful result with diagnostics
and no value. Caller cancellation propagates without a partial view; unexpected
programming exceptions are not swallowed.

Direct construction and registry hosting use the same options:

```csharp
var options = new WotNodeSetConverterOptions
{
    ProjectionFormProvider = applicationHostForms
};
var resolver = new WotProjectionResolver(sourceResolver, options);
WotConversionResult<WotDocument> result =
    await resolver.ResolveAsync(plan, cancellationToken: cancellationToken);
// Inspect result.Success and result.Diagnostics before using and disposing result.Value.

services.AddSingleton<IWotProjectionFormProvider>(applicationHostForms);
services.AddOpcUa().AddWotRegistryServer();
// Alternatively: AddWotRegistryServer(o => o.ProjectionFormProvider = applicationHostForms).
```

A provider registered in DI takes precedence over the registry option. An
explicitly registered `WotNodeSetConverterOptions` instance retains its own
provider. Applications remain responsible for actual endpoint availability and
for keeping provider output stable within a materialization generation; this
seam does not add provider-driven registry invalidation or endpoint lifecycle
management.

**Current admission boundary:** full base TD/TM JSON Schema validation is
deliberately deferred. The Binding-specific context, ownership, local dependency
and URI-template guards described here are not a JSON Schema validator.
Actual host forms require an application provider or authored forms; no
deployment-specific endpoint provider is supplied by the resolver. Required
Binding-reference acquisition uses the explicit public modules described above;
`WotProjectionResolver.ResolveAsync` alone is the origin-preserving projection
stage, not a claim that every later consumer dependency has been acquired.
Origin-preserving external reference carriage is not proof that the referenced
definition was acquired or resolved. A successful document-resolution result
alone must not be treated as admission proof for an executable TD.

URI-template variables required by carried forms use that form owner's
declarations. Source-affordance declarations take precedence over the source
root; required root declarations are carried at the resulting affordance scope.
Variables from different sources remain distinct even when their names match.
Projection-owned forms use the projection root's declarations, never surviving
source declarations. The projection annotation whitelist does not permit an
enumerated member to restate `uriVariables`.

Supplied Thing-level forms use the projection owner's Thing-level declarations
and undergo the same URI-template syntax and dependency checks as carried
affordance forms. Percent-encoded braces remain literal characters, not variable
references.

An active API key security scheme with `in: "uri"` can also declare a URI
placeholder through its `name`. Resolution follows the form's effective security
requirement, including form overrides and combined schemes, within the actual
form owner's security domain. These placeholders are not synthesized as data
`uriVariables`; a name shared with a data-variable declaration is a conflict.
An unrelated or inactive security scheme cannot supply a missing variable.

If host routing replaces a selected source's URI-variable subtree, source data
references into that subtree retain their original variable schemas as supporting
`schemaDefinitions`. They do not follow the source affordance's ancestor mapping
into the host-owned variables. These dependencies share the existing owner-scoped
reference closure, collision-safe names, original contexts and support-node budget.

Carried variables preserve their original ordered and term-scoped contexts and
schema-reference ownership. Duplicate containers or declarations must agree;
equivalent facts coalesce and contradictory facts fail with
`ProjectionSourceUnresolved`. Source-local containers are checked before cloning
enumerated, bulk-selected or supporting affordances, including under host routing.
Variable maps are emitted in code-point order without changing the URI template.
Retained variables count with affordances and reusable schemas against `MaxNodeCount`.

Dependency discovery reads supported RFC 6570 expressions, including prefix and
explode modifiers and literal percent-encoded variable names. Missing required
declarations and malformed templates fail explicitly. Escaped braces and
template-like text in defaults or opaque metadata do not create dependencies;
this step does not expand templates or perform an interaction.

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

Copied authentication definitions keep their source ownership. With `B64u`
denoting unpadded base64url of exact UTF-8 bytes, a source scheme is named
`q:s:<B64u(sourceName)>:<B64u(schemeName)>`; a projection-owned scheme is named
`q:p:<B64u(schemeName)>`. Root, affordance and form requirements and known combo
references follow the corresponding mapping. An authored host name cannot
impersonate a generated source name, and underscores in two source/name pairs
cannot collapse their authentication requirements.

Required security definitions must be present, and combo dependencies must be
acyclic within the configured resolver depth. Contradictory duplicate
definitions fail rather than replacing another authentication scheme.
Consistent repeats may share a definition. Unrelated vendor metadata is retained,
not rewritten by matching strings. Consumers must follow the emitted names
rather than assuming the older underscore-concatenated spelling.

Source-owned affordance requirements are qualified as well as form requirements.
An incomplete Thing Model's affordance can retain a security-definition closure
without forms. Repeating the `securityDefinitions` container does not permit an
earlier policy to be replaced: repeated containers must describe equivalent
facts. Projection-owned conflicts fail before source acquisition; source-owned
conflicts fail before a resolved view is returned.

Selections are applied in the total order of Section 12.4, and the **first**
selection of a name wins: by the position of the source in `uav:projects`;
within one source, every enumerated selection before every bulk one; within each
group, by affordance kind in the fixed order `properties`, `actions`, `events`;
within one kind, by ascending Unicode code point of the name the selection takes
**in the view**; and, where two selections still compare equal, by ascending
Unicode code point of the affordance's name **in the source**. The last key is
what makes the order total: `uav:namePrefix` upper-cases the first Unicode scalar of
the source name, so `serialNumber` and `SerialNumber` in one source both become
`deviceSerialNumber` in the view and nothing before it separates them. The order
is stated over names rather than over document order because `properties`,
`actions` and `events` are JSON objects, which RFC 8259 defines as unordered — a
rule that ranked selections by member position would let two conforming
consumers resolve identical bytes into different views.

Prefix capitalization is culture-invariant, handles supplementary characters as
one scalar, and preserves the remainder of the source name unchanged.

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

The aggregation sample declares both pumps, management Methods, and alarm
EventTypes in its source NodeSet before generating the linked documents.
Each pump carries `GeneratesEvent` references and is the notifier clients
subscribe to. The generic runtime publishes selected upstream occurrences and
routes the declared `uav:conditionAction` / `uav:actsOn` Methods back to their
owning source. A local EventType is distinct from its mutable Condition instance
and occurrence EventIds.

The Supervision and Management projections organize existing signal Variables,
Methods, and EventTypes; they do not create copies or overlay missing Nodes on
an authoritative native partition. See [the sample README](../samples/WotCon/README.md)
for the exact per-pump membership and supported alarm workflow, and
[the binding runtime](WotBindings.md#projected-methods-events-and-conditions)
for channel, subscription, and occurrence-route ownership.

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
