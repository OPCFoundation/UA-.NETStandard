# OPC UA — OpenUSD binding

This guide covers the client- and server-side libraries that bridge an OPC UA address space to an
[OpenUSD](https://openusd.org/) stage. For the OPC 40010 Robotics companion SDK used to label and drive robot-cell
twins, see [Robotics](Robotics.md).

This is **Part 1** of the OpenUSD work, which keeps the USD scene *outside* OPC UA and binds process values to it. Its
counterpart, [OpenUSD scene materialization](OpenUsdScene.md) (Part 2), materializes the scene *inside* the address
space so the prim tree becomes the node hierarchy. The two interoperate — a Part 1 binding may target a Part 2
attribute Variable — but neither requires the other.

> The OPC UA — OpenUSD Bindings companion model is a **draft** (experimental) model. The type NodeIds and the
> `Server/OpenUSD/Representations` registry described here are subject to change until the companion specification is
> ratified.

## Libraries

| Package | Role |
| --- | --- |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd` | The draft OpenUSD-binding companion model (source-generated NodeStates). |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Client` | The generic, domain-agnostic `OpenUsdConnector`, the `IUsdSink` abstraction, and the file/mock sinks. |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Server` | Server-side authoring helpers (`UsdAssetDelivery`, representation authoring). |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector` | A ready-to-run console connector tool built on the client library. |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector.Viewer` | Optional viewport for the connector's `--view` option. Renders the composed stage and exposes it as an `IUsdSink`. |

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

`UsdAssetDelivery.AttachStageAssets(context, stage, openUsdNs, assets)` serves artist-authored USD layers through the
address space as read-only Part 5 files with SHA-256 digests, so a generic connector can fetch, verify, cache, and
render the twin with no external asset resolver. `assets` is an `ArrayOf<ServedAsset>` and the method returns the
created `OpenUsdAssetState` nodes as an `ArrayOf<OpenUsdAssetState>`.

## Samples

* [`MinimalRobotServer`](../samples/MinimalRobotServer) — a self-contained server exposing an OPC 40010
  MotionDeviceSystem with two independently mobile robots. OPC 10000-210 RSL
  frames drive live `double3` translation/rotation, and OPC 10000-211 GPOS
  locations drive geospatial metadata; a generic connector renders the cell
  live. See [Positioning](Positioning.md).
* [`PumpDeviceIntegrationServer`](../samples/PumpDeviceIntegrationServer) — a DI pump line bound to OpenUSD, including
  component composition, cross-server components, and served-asset delivery.

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

> The viewport requires .NET 10 on `win-x64` and the OpenUSD packages
> (`OpenUsd`, `OpenUsd.Viewer`, `OpenUsd.Runtime.Imaging.win-x64`). Until those are published to nuget.org, build
> them from the [openusd repository](https://github.com/marcschier/openusd-dotnet) with `eng/pack-packages.ps1` and
> point restore at the resulting folder feed by setting `OPENUSD_LOCAL_FEED`. For the same reason
> `tools/Opc.Ua.OpenUsd.Connector.Viewer` is not listed in `UA.slnx` and is built explicitly:
>
> ```
> $env:OPENUSD_LOCAL_FEED = "<openusd>/artifacts/localfeed"
> dotnet publish tools/Opc.Ua.OpenUsd.Connector -c Release -f net10.0 -r win-x64 --self-contained false -o out
> dotnet publish tools/Opc.Ua.OpenUsd.Connector.Viewer -c Release -r win-x64 --self-contained false -o out
> ```
>
> Publishing both into the same directory is what puts the optional assembly, its dependencies, and the native
> plugin tree where the connector looks for them.

