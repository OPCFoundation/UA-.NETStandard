# OPC UA — OpenUSD binding and Robotics SDK

This guide covers the client- and server-side libraries that bridge an OPC UA address space to an
[OpenUSD](https://openusd.org/) stage, plus the OPC 40010 Robotics companion SDK used to label and drive robot-cell twins.

> The OPC UA — OpenUSD Bindings companion model is a **draft** (experimental) model. The type NodeIds and the
> `Server/OpenUSD/Representations` registry described here are subject to change until the companion specification is
> ratified. The Robotics (`Opc.Ua.Robotics*`) libraries implement the ratified OPC 40010 companion model.

## Libraries

| Package | Role |
| --- | --- |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd` | The draft OpenUSD-binding companion model (source-generated NodeStates). |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Client` | The generic, domain-agnostic `OpenUsdConnector`, the `IUsdSink` abstraction, and the file/mock sinks. |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Server` | Server-side authoring helpers (`UsdAssetDelivery`, representation authoring). |
| `OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector` | A ready-to-run console connector tool built on the client library. |
| `OPCFoundation.NetStandard.Opc.Ua.Robotics{,.Client,.Server}` | The OPC 40010 Robotics companion model plus client/server helpers. |

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

## Robotics companion SDK

`Opc.Ua.Robotics.Client` lets a generic connector or viewer label and drive a robot cell without hard-coding NodeIds:

```csharp
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Client;

// Discover every MotionDeviceSystem under the DI DeviceSet or the Objects folder.
ArrayOf<NodeId> systems = await RoboticsClient.DiscoverMotionDeviceSystemsAsync(session, root, ct);

// Classify a discovered node's TypeDefinition (returns false — never throws — when the
// Robotics namespace is not present on the server).
if (RoboticsClient.TryGetRoboticsTypeName(typeDefinition, session.NamespaceUris, out string? name))
{
    // name is "MotionDeviceSystem" | "MotionDevice" | "Axis" | "Controller"
}
```

`Opc.Ua.Robotics.Server` loads the type system into a node manager and instantiates Robotics-typed objects:

```csharp
using Opc.Ua.Robotics.Server;

int added = nodes.AddRoboticsTypeSystem(context); // OPC UA DI + IA + Robotics, in dependency order
BaseObjectState cell = context.CreateTypedObject(
    parent, "Cell1", ns,
    RoboticsModel.TypeNodeId(RoboticsModel.MotionDeviceSystemType, context.NamespaceUris),
    ReferenceTypeIds.Organizes);
```

## Server-side authoring

`UsdAssetDelivery.AttachStageAssets(context, stage, openUsdNs, assets)` serves artist-authored USD layers through the
address space as read-only Part 5 files with SHA-256 digests, so a generic connector can fetch, verify, cache, and
render the twin with no external asset resolver. `assets` is an `ArrayOf<ServedAsset>` and the method returns the
created `OpenUsdAssetState` nodes as an `ArrayOf<OpenUsdAssetState>`.

## Samples

* [`MinimalRobotServer`](../samples/MinimalRobotServer) — a self-contained server exposing an OPC 40010
  MotionDeviceSystem (two 6-axis robots) bound to OpenUSD; a generic connector renders the cell live.
* [`PumpDeviceIntegrationServer`](../samples/PumpDeviceIntegrationServer) — a DI pump line bound to OpenUSD, including
  component composition, cross-server components, and served-asset delivery.

## The connector tool

`Opc.Ua.OpenUsd.Connector` is a console application that connects to any server implementing the draft binding,
discovers `Server/OpenUSD/Representations`, subscribes, optionally fetches the served asset closure, and authors a live
`live.usda` override layer. It is the end-to-end reference for the client library.
