# OPC UA — OpenUSD

This guide covers the client- and server-side libraries that bridge an OPC UA address space to an
[OpenUSD](https://openusd.org/) stage. For the OPC 40010 Robotics companion SDK used to label and drive robot-cell
twins, see [Robotics](Robotics.md).

The work comes in two parts, covered here in turn. **Part 1** — the binding model — keeps the USD scene *outside* OPC
UA and binds process values to it. **Part 2** — [scene materialization](#part-2--scene-materialization) — puts the
scene *inside* the address space, so the composed prim tree becomes the node hierarchy and browsing the server is
browsing the scene. The two interoperate — a Part 1 binding may target a Part 2 attribute Variable — but neither
requires the other.

|  | Part 1 — bindings | Part 2 — scene materialization |
|---|---|---|
| USD scene lives | outside OPC UA | inside OPC UA |
| OPC UA carries | *which* prim/attribute a value maps to | the prims and attributes themselves |
| Consumer | a connector that writes an external stage | a client that browses/subscribes, or exports `.usda` |

> Both companion models are **draft** (experimental) models. The type NodeIds and the
> `Server/OpenUSD/Representations` registry described here are subject to change until the companion specifications
> are ratified.

## Libraries

| Package | Role |
| --- | --- |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd` | The draft OpenUSD-binding companion model (source-generated NodeStates). |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Client` | The generic, domain-agnostic `OpenUsdConnector`, the `IUsdSink` abstraction, and the file/mock sinks. |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Server` | Server-side authoring helpers (`UsdAssetDelivery`, representation authoring). |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector` | A ready-to-run console connector tool built on the client library. |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector.Viewer` | Optional viewport for the connector's `--view` option. Renders the composed stage and exposes it as an `IUsdSink`. |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsdScene` | Part 2: the source-generated companion model, the scene document model, the `.usda` reader/writer, and the value-type map. |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsdScene.Server` | Part 2: materializer, exporter, discovery, and Part 1 interop. |

## The connector

`OpenUsdConnector` is a **client**: it discovers a server's `OpenUsdRepresentation` instances through the Part 1
`Server/OpenUSD/Representations` registry, subscribes to the bound source Variables, applies the declared conversion,
and writes the target USD attributes into an `IUsdSink`. It is domain-agnostic — it knows only the OpenUSD binding
model, never "pump" or "robot".

### Progressive API

The simplest usage needs only a connected session and a sink:

```csharp
using Opc.Ua.OpenUsd.Client;

// session is a connected ISession (for example a ManagedSession).
var sink = new UsdFileSink("live.usda");
await using var connector = new OpenUsdConnector(session, sink);

await connector.StartAsync(cancellationToken);   // discover + subscribe + compose
// ... the sink now receives live updates ...
await connector.StopAsync(cancellationToken);      // stop streaming
```

`OpenUsdConnector` implements `IAsyncDisposable`; `await using` (or an explicit `DisposeAsync`) stops streaming and
closes any connector-owned remote sessions opened for cross-server federation. The caller-provided primary session is
never closed by the connector.

Advanced behaviour is configured through `OpenUsdConnectorOptions`:

```csharp
var options = new OpenUsdConnectorOptions
{
    EnableCommands = true,                                   // opt in to UsdToUaCommand actuation (fail-closed by default)
    RemoteSessionFactory = (endpointUrl, ct) => OpenRemoteSessionAsync(endpointUrl, ct), // §5.14 cross-server federation
    MaxAssetBytes = 32 * 1024 * 1024,                        // per-asset read cap
    MaxTotalAssetBytes = 128L * 1024 * 1024,                 // per-fetch read cap
};
await using var connector = new OpenUsdConnector(session, sink, options, telemetry);
```

### Values cross the boundary as `Variant`

`IUsdSink` never exposes `object`. A scalar attribute is a `double`, a colour is a three-element `float` array
(`ArrayOf<float>`), and a token/visibility value is a `string` — all carried as `Variant`:

```csharp
public interface IUsdSink
{
    void SetAttribute(string primPath, string propertyName, Variant value);
    void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value);
    void ComposePrim(string primPath, OpenUsdCompositionArc arc, string? assetReference, bool active);
    IDisposable BeginBatch();
}
```

Two sinks ship in the box:

* `UsdFileSink` authors a text USD override layer (`live.usda`). It validates every prim-path segment and property
  name as a USD identifier, escapes token values, and rejects unsafe asset references, so a hostile or malformed name
  from the server cannot corrupt or inject into the layer.
* `MockUsdSink` is an in-memory, thread-safe sink used by tests and diagnostics.

`BeginBatch()` lets a file-backed sink defer flushes; history replay uses it to author many time samples with a single
file write.

### History replay and commands

* `ReplayHistoryAsync(startTime, endTime, ct)` replays Part 11 history for every `UaHistoryToUsd` binding, following
  continuation points and authoring the returned values as USD time samples. Sources that do not historize degrade to
  zero samples without throwing.
* `IssueCommandAsync(value, ct)` actuates the opt-in `UsdToUaCommand` binding. It is **fail-closed**: it throws unless
  the connector was constructed with `EnableCommands = true`.

### Integrity

When a stage advertises a `RootLayerDigest`, the connector verifies it (constant-time) before authoring any opinions,
and refuses to compose on a mismatch. Served asset closures (`FetchServedAssetsAsync`) are streamed through the Part 5
`FileType`, digest-verified fail-closed, and cached under sanitized relative paths (path-traversal is defended).

## Dependency injection

Both a standalone `IServiceCollection` extension and the fluent `IOpcUaClientBuilder` extension register the singleton
`OpenUsdConnectorFactory`; the direct constructors remain available as a non-DI fallback.

```csharp
// Standalone.
services.AddOpenUsdConnector(o => o.EnableCommands = true);

// Fluent, chained onto the client builder.
services.AddOpcUa()
    .AddClient(configuration)
    .AddOpenUsdConnector(o => o.MaxAssetBytes = 32 * 1024 * 1024);
```

Resolve the factory and create a connector per connected session:

```csharp
public sealed class TwinWorker(OpenUsdConnectorFactory connectors)
{
    public async Task RunAsync(ISession session, IUsdSink sink, CancellationToken ct)
    {
        await using OpenUsdConnector connector = connectors.Create(session, sink);
        await connector.StartAsync(ct);
        // ...
    }
}
```

Observability is threaded through `ITelemetryContext` (resolved from DI), which the factory passes to each connector.

## Server-side authoring

`context.CreateRepresentation(owner, stage, primPath, openUsdNs)` attaches an `OpenUsdRepresentation` to a domain
Object and points it at a stage and prim path. The representation is an **AddIn**, so it is mounted with
`HasAddIn` — not plain `HasComponent`. The distinction is easy to get wrong because `HasAddIn` is a *subtype* of
`HasComponent`: an AddIn mounted with the wrong reference type still browses, still aggregates, and still works
end to end, so only a conformance checker notices. The `IOpenUsdRepresentedType` model placeholder declares the
same `HasAddIn` contract. `CreateRepresentation` is the supported authoring path; do not hand-roll a mount with
`CreateInstanceOfOpenUsdRepresentationType` and a manually assigned reference type. The sample servers assert the
reference type in their E2E suites.

`UsdAssetDelivery.AttachStageAssets(context, stage, openUsdNs, assets)` serves artist-authored USD layers through the
address space as read-only Part 5 files with SHA-256 digests, so a generic connector can fetch, verify, cache, and
render the twin with no external asset resolver. `assets` is an `ArrayOf<ServedAsset>` and the method returns the
created `OpenUsdAssetState` nodes as an `ArrayOf<OpenUsdAssetState>`.

## Samples

* [`MinimalRobotServer`](../samples/Robotics/MinimalRobotServer) — a self-contained server exposing an OPC 40010
  MotionDeviceSystem with two independently mobile robots. OPC 10000-210 RSL
  frames drive live `double3` translation/rotation, and OPC 10000-211 GPOS
  locations drive geospatial metadata; a generic connector renders the cell
  live. See [Positioning](Positioning.md).
* The Robotics samples also include an agent-plus-viewer Robot Intent flow: the OpenUSD viewport shows the same robot
  that the MCP tools command, and a viewport prim pick can become a robot command through
  `UsdViewOptions.PrimPicked`. See [Robot Intent](Robotics.md#robot-intent) and the
  [Robotics samples](../samples/Robotics/README.md).
* [`PumpDeviceIntegrationServer`](../samples/DI/PumpDeviceIntegrationServer) — a DI pump line bound to OpenUSD, including
  component composition and served-asset delivery.
* [`GeneratorServer`](../samples/OpenUsd/GeneratorServer) — the Generators companion
  specification with a datasheet-driven simulation and one independent twin per
  configured generating set.
* [`SiteCompositionServer`](../samples/OpenUsd/SiteCompositionServer) — a supervisory server that owns no devices and
  composes the machines of the pump and generator servers into a single scene
  through cross-server components. Render it with the connector's `--federate`
  option. See [Samples](../samples/OpenUsd/SiteCompositionServer/README.md).

## The connector tool

`Opc.Ua.OpenUsd.Connector` is a console application that connects to any server implementing the draft binding,
discovers `Server/OpenUSD/Representations`, subscribes, optionally fetches the served asset closure, and authors a live
`live.usda` override layer. It is the end-to-end reference for the client library.

```
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- \
    --server opc.tcp://localhost:62830/MinimalRobotServer \
    --fetch-assets ./stage --insecure --seconds 30
```

| Option | Meaning |
| --- | --- |
| `--server <url>` | Endpoint to connect to. |
| `--out <live.usda>` | Override layer to author. Defaults to `live.usda` in the working directory. |
| `--seconds N` | Stop after `N` seconds instead of waiting for Ctrl+C. |
| `--fetch-assets <dir>` | Download the served USD layer closure (§5.15) and compose a self-contained `stage.usda`. |
| `--insecure` | Demo only: unsecured endpoint and blanket certificate acceptance. |
| `--enable-commands` | Opt in to actuating `UsdToUaCommand` bindings (fail-closed by default). |
| `--command-value <double>` | Setpoint written once at start when commands are enabled. |
| `--view` | Render the composed stage and stream the same live values into it. |
| `--renderer <Auto\|Storm\|D3D12\|Vulkan>` | Renderer preference for `--view`. |
| `--stage <stage.usda>` | Render an existing local stage instead of a fetched one. |
| `--plugins <dir>` | Directory holding the staged USD plugin tree, when it is not beside the connector. |
| `--pick-command [<prim path>]` | With `--view`, print picked target prim paths. |

### Rendering the twin live

`--view` opens a viewport on the composed stage and fans every subscribed value into **both** the override layer and
the stage being rendered, so the twin animates in one process:

```
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- \
    --server opc.tcp://localhost:62830/MinimalRobotServer \
    --insecure --view
```

Without `--fetch-assets` or `--stage`, `--view` fetches the asset closure into a temporary directory, because a
renderer needs geometry it can resolve. Closing the window stops the session; `--seconds` closes it automatically.

The renderer itself lives in a separate, optional assembly, `Opc.Ua.OpenUsd.Connector.Viewer`
(`OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector.Viewer`), which the connector loads on demand from its own
directory. That keeps the connector package free of a UI framework and a native OpenUSD payload, and lets it keep
targeting .NET Framework. When the assembly is absent, `--view` explains how to install it and every other option
keeps working.

Internally the viewport supplies a sink that authors into the scheduler-owned stage the renderer already owns — the
connector never opens the stage a second time. `CompositeUsdSink` fans values out to that sink and to `UsdFileSink`,
so the on-disk artefact and the picture never diverge.

The connector client API also exposes `UsdViewOptions.PrimPicked`, a callback that receives picked USD prim paths.
Renderer-backed pointer picking works: the OpenUSD viewer owns input handling, DPI scaling, physical-pixel conversion
and stale-revision retry, and reports hits through the host callback. `Auto` uses the renderer first and falls back to
the command-prim watcher only when renderer picking is unavailable, `Renderer` requires renderer-backed picks, and
`CommandPrim` uses the fallback directly. Misses do not submit intents. For the fallback, set a `targetPrim`
relationship, string attribute, or token attribute on `UsdViewOptions.CommandPrimPath` (default `/World/IntentCommand`)
and the callback fires when that target changes.

> The viewport requires .NET 10 and the OpenUSD packages (`OpenUsd`, `OpenUsd.Viewer`, `OpenUsd.Runtime.Imaging`),
> which are published on nuget.org, so a plain restore is enough. The RID-agnostic runtime metapackages resolve the
> correct native payload per RID; `win-x64`, `linux-x64` and `osx-arm64` are all supported. With `0.7.0-alpha`, a
> RID-less build or publish on a supported host copies that host's OpenUSD native payload. Use an explicit RID when
> publishing for another platform. Publish the connector and the viewport into the *same* directory, substituting your
> own RID:
>
> ```
> dotnet publish tools/Opc.Ua.OpenUsd.Connector -c Release -f net10.0 -r win-x64 --self-contained false -o out
> dotnet publish tools/Opc.Ua.OpenUsd.Connector.Viewer -c Release -r win-x64 --self-contained false -o out
> ```
>
> Publishing both into the same directory is what puts the optional assembly, its dependencies, and the native
> plugin tree where the connector looks for them.

#### Viewport colour, materials and cameras

Current OpenUSD packages support the live viewport features the connector samples rely on:

- `primvars:displayColor` is authored through the managed `color3f[]` API, so DisplayColor bindings animate in the
  viewport instead of only appearing in the override layer.
- Bound `UsdPreviewSurface` material networks are shaded, so material colour inputs can be used for scalar shader
  colour targets without requiring a duplicate displayColor fallback.
- Authored stage cameras are opened automatically, so samples can frame their intended operator view.

**Visibility** bindings remain a good fit for binary state such as run lamps, alarm beacons and fault halos. Use them
when an on/off condition should be unmistakable; they are no longer needed as a workaround for colour or material
limitations.


## Part 2 — scene materialization

Where the binding model above keeps the USD scene **outside** OPC UA, scene materialization puts the
composed prim tree **inside** the address space: browsing the server is browsing the scene, and an
ordinary Subscription on an attribute Variable is a live feed of that part of it.

### Materializing a stage

```csharp
UsdStage stage = UsdaReader.ReadFile("Plant.usda");

FolderState stages = context.EnsureStagesFolder(serverObject, sceneNamespaceIndex);
UsdMaterializationResult result = context.MaterializeUsdStage(stages, stage, sceneNamespaceIndex);
```

`MaterializeUsdStage` walks the composed prim tree depth first and creates, for each prim, a
`HasComponent` child typed by its USD `typeName`; for each attribute a `UsdAttributeType`
Variable; and for each relationship a `UsdRelationshipType` Object with ordered `Targets` and
`TargetPaths`.

The result carries two indexes — `PrimsByPath` and `AttributesByPath` — so a caller can bind live
data to a materialized attribute without re-browsing.

### Nothing is ever dropped

The specification is emphatic that an importer must not discard what it does not understand
(§8.4), because an exporter has to be able to reproduce it. So:

- an **unknown typed schema** stays a `UsdPrimType` carrying its `TypeName` token;
- an **unknown API schema** degrades to a generic `UsdApiSchemaType` AddIn carrying its
  `SchemaName`;
- an **unknown value type** is carried opaquely as `BaseDataType`, with the exact
  `SdfValueTypeName` preserved in `UsdTypeName`.

### Types carry USD roles

USD's `color3f`, `normal3f`, `point3f` and `vector3f` all decompose to three floats and differ
only by *role*. Rather than flatten that away, the model gives each its own DataType that
**subtypes the built-in** — the same idiom the OPC UA standard uses for `Duration : Double` or
`UtcTime : DateTime`.

Because the role type subtypes a built-in, the value bytes are unchanged: a generic client reads
a `Float[3]` exactly as before, while a renderer or material editor can tell a colour from a point
straight from the type system instead of parsing a string. `UsdValueTypeMap` implements the
mapping, and the attribute's `UsdTypeName` keeps it reversible even where several USD types share
one OPC UA DataType.

### Live attributes

A materialized attribute is an ordinary Variable, so USD's static/time-sampled duality maps onto
the OPC UA value surface in two modes a Server may mix per attribute:

- **Mode A — live.** The Value is server-maintained and time-varying; a Subscription delivers
  changes and, where retained, `HistoryRead` exposes the timeline — the OPC UA counterpart of USD
  time samples. Rotating `xformOp`s and process-driven attributes use this mode.
- **Mode B — static.** The Value is the authored default.

Driving Mode A is a **Server-side** responsibility. An external Part 1 connector authors into a
USD sink and cannot write an in-server Variable; a Part 1 binding *declares* the mapping, and the
Server (or a server-hosted connector) applies it.

### Georeferencing

Core OpenUSD has no geodetic schema, so a georeferenced stage is expressed today through vendor
extension schemas (Cesium for Omniverse, NVIDIA's geospatial schema). Those materialize through
the ordinary vendor-extension mechanism — a georeference prim type as an ObjectType subtype, an
anchor API schema as an AddIn.

To give a client something portable, the model also defines `UsdGeoreferenceApiType` (a
stage-level origin: latitude, longitude, height, EPSG code, tangent plane) and
`UsdGlobeAnchorApiType` (a per-prim geodetic position). When the materializer recognises a Cesium
schema it **dual-authors** the portable one with the same values, so a generic client reads the
anchor from one well-known type while a vendor-aware client still reads the native schema.

A partial anchor is never published: a wrong position is worse than no position.

### Conversion and the round-trip contract

`UsdaReader` and `UsdaWriter` convert between `.usda` and the scene document model, and
`UsdSceneExporter` reads a materialized address space back out, so the full round trip is

```text
.usda → UsdStage → address space → UsdStage → .usda
```

The contract (§7.4) is **composed-scene lossless**: the exported stage is prim-for-prim,
attribute-for-attribute (name, `SdfValueTypeName`, resolved value and array shape),
relationship-for-relationship (ordered targets), metadata-, variant-selection-, kind- and
specifier-equivalent to the input's *composed* result, and the recorded composition arc list is
preserved.

It is deliberately **not** authoring-layer lossless: the input's per-layer opinion stack, sublayer
structure and value clips are summarised as provenance under `Composition/` and
`RootLayerIdentifier` rather than reproduced layer by layer. `UsdSceneSignature` computes the
equivalence used to check the contract.

### Discovery and Part 1 interop

`EnsureStagesFolder` returns the folder stages are organized under: Part 1's
`Server/OpenUSD/Stages` when the bindings model is also implemented — so one connector discovers
both external-stage bindings and in-server materialized stages — or a standalone
`Server/OpenUSDScene/Stages` otherwise.

A Part 1 live binding may target a Part 2 attribute Variable. Part 1 ≥ 0.3.0 carries the target
two ways, and a Server should author both so that path-resolving and NodeId-resolving connectors
agree:

```csharp
NodeId target = result.ResolveBindingTargetNodeId(
    "/Plant/Pumps/Pump_1/Pump/Impeller", "xformOp:rotateZ");
```

### Conformance units

`UsdMaterializationOptions` maps one-to-one onto the specification's conformance units, each
independent and additive. **Scene Structure** is the baseline; the rest are opt-in:
composition provenance, typed schemas, applied schemas, georeferencing, live attributes,
conversion, and Part 1 interop.
