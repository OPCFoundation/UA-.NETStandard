# OPC UA WoT Connectivity (OPC 10100-1)

This repository implements the OPC UA **WoT Connectivity** companion
specification (OPC 10100-1, "WoT Connectivity for OPC UA") through three
class libraries plus an integration test project:

| Project                          | Purpose                                                       |
|----------------------------------|---------------------------------------------------------------|
| `Opc.Ua.WotCon`                  | Source-generated information model (NodeStates, NodeIds, generated ObjectType client proxies) compiled from the official `WotConnection.xml` design + `WotConnection.csv` |
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

---

<<<<<<< HEAD
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

## 6. Limitations and known issues

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

## 7. References

* OPC 10100-1, *WoT Connectivity for OPC UA*: https://reference.opcfoundation.org/specs/OPC-10100-1/full
* W3C Web of Things Thing Description 1.1: https://www.w3.org/TR/wot-thing-description11/
* W3C WoT Binding Templates: https://w3c.github.io/wot-binding-templates/
