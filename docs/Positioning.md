# OPC UA Relative Spatial Location and Global Positioning

The Positioning libraries implement two OPC UA companion specifications:

- Relative Spatial Location (RSL), defined by
  [OPC 10000-210](https://reference.opcfoundation.org/specs/OPC-10000-210).
- Global Positioning (GPOS), defined by
  [OPC 10000-211](https://reference.opcfoundation.org/specs/OPC-10000-211).

The libraries use the released RSL 1.00.1 and GPOS 1.0.0 NodeSets. GPOS
depends on RSL.

## Contents

- [Packages](#packages)
- [Minimal standalone server](#minimal-standalone-server)
- [Server authoring](#server-authoring)
- [Client](#client)
- [MCP tools](#mcp-tools)
- [Transform conventions](#transform-conventions)
- [Robot and OpenUSD sample](#robot-and-openusd-sample)

## Packages

| Package | Purpose |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Positioning` | Source-generated RSL and GPOS models plus frame, WGS84, ENU, and ground-control-point transformations. |
| `OPCFoundation.NetStandard.Opc.Ua.Positioning.Server` | Standalone/composed node-manager hosting, address-space builders, providers, lifecycle, validation, and logging. |
| `OPCFoundation.NetStandard.Opc.Ua.Positioning.Client` | Continuation-safe discovery, typed reads and streams, frame-chain resolution, and Zone transforms. |

Generated model types remain in the specification namespaces:

- `Opc.Ua.Rsl`
- `Opc.Ua.Gpos`

The base package exposes generated NodeIds, NodeStates, encodeables, model
loaders, and generated ObjectType clients. The hand-written APIs compose those
generated basics instead of replacing or inheriting from them.

## Minimal standalone server

`AddPositioningServer` owns an RSL/GPOS node manager. Configure the address
space after the generated models are loaded:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Positioning.Server;
using Opc.Ua.Positioning.Server.Hosting;

HostApplicationBuilder host = Host.CreateApplicationBuilder(args);

IPositioningServerBuilder positioning = host.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "PositioningServer";
        options.EndpointUrls.Add(
            "opc.tcp://localhost:4840/PositioningServer");
    })
    .AddPositioningServer();

positioning
    .AddGeoLocationProvider<MyGpsProvider>()
    .AddRelativeSpatialLocationProvider<MyRelativeLocationProvider>()
    .ConfigurePositioningFor<PositioningNodeManager>(async context =>
    {
        PositioningAddressSpaceBuilder model = context.AddressSpace;

        // Create a SpatialObjectsList, SpatialObject AddIns and concrete
        // Cartesian frames, then register each completed subtree.
        // Create Zones and attach GlobalPosition or GlobalLocation Variables
        // in the same callback.

        await ValueTask.CompletedTask;
    });

await host.Build().RunAsync();
```

For a server that already owns the companion address space, keep that manager
as the single namespace owner:

```csharp
IPositioningServerBuilder positioning = serverBuilder
    .AddNodeManager<MyNodeManagerFactory>()
    .AddPositioningFor<MyNodeManager>();

positioning.ConfigurePositioningFor<MyNodeManager>(
    context => ((MyNodeManager)context.Manager)
        .ConfigurePositioningAsync(context));
```

The owning composite manager must load the RSL model before GPOS and advertise
both namespace URIs.

## Server authoring

`PositioningAddressSpaceBuilder` creates instances through the generated
factories and supports:

- `SpatialObjectsListType` AddIns below the standardized
  `RelativeSpatialLocations` entry point.
- `SpatialObjectType` AddIns attached to existing objects through `HasAddIn`.
- Cartesian world, position, attach-point, internal, and alternative frames.
- GPOS `ZoneType` instances defined by ground control points or a
  position/radius.
- `GlobalPositionType` and `GlobalLocationType` Variables attached to tracked
  objects.
- Coherent aggregate/component updates, optional-field status, `NodeVersion`,
  and model-change notifications.

Provider bindings publish an initial value and then consume a cancellation-aware
async stream:

```csharp
PositioningProviderSubscription subscription =
    await model.BindGlobalLocationAsync(
        globalLocation,
        provider,
        sourceId,
        cancellationToken: ct);
```

Dispose the subscription asynchronously during server shutdown. Provider
failures retain the last value, set its status to `BadCommunicationError`, log
the failure through `ITelemetryContext`, and fault `Completion`.

`IGeoLocationProvider` is intentionally technology-neutral, and lives in
`Opc.Ua.Server` (namespace `Opc.Ua`) rather than in a companion-model assembly.
Global Positioning System (GPS) and World Geodetic System 1984 (WGS84)
coordinates are built-in use cases. Real-time location systems (RTLS),
ultra-wideband (UWB), radio-frequency identification (RFID), local
floor-plan coordinates, and other tracking systems can use the same contract.
An RSL provider
is also useful when a robot controller, metrology system, or kinematic service
is authoritative for a relative frame rather than a global coordinate.

Because the contract is shared, **one provider implementation serves every
model that publishes location**. The same instance can back GPOS
`GlobalLocation` Variables here and OPC 10030 (ISA-95) `GeoSpatialLocationType`
Variables — see [ISA-95](ISA95.md#geospatiallocationtype-provider-seam).
A sample can include:

- `GeoPosition` with latitude, longitude, optional height, accuracy, floor,
  and EPSG code
- `GeoOrientation`
- Text labels

```csharp
public sealed class MyGpsProvider : IGeoLocationProvider
{
    public bool SupportsPush => true;

    public ValueTask<GeoLocationSample> ReadAsync(
        string sourceId,
        CancellationToken ct = default)
    {
        return new ValueTask<GeoLocationSample>(
            GeoLocationSample.Good(
                new GeoPosition(47.3769, 8.5417, 408.0, EpsgCode: 4326)));
    }

    public async IAsyncEnumerable<GeoLocationSample> WatchAsync(
        string sourceId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Yield a sample whenever the receiver reports a new fix.
    }
}
```

Set `SupportsPush` to `false` when the source can only be polled; the binding
layer then never calls `WatchAsync` and reads through `ReadAsync` at the
Variable's sampling interval instead.

A GPOS Variable requires coordinates. If a sample has `Position` set to
`null`, the server rejects it with `BadNoDataAvailable`. If the position's
`EpsgCode` differs from the Variable's configured
`CoordinateReferenceSystem`, the binding fails rather than mis-georeference
the value silently. Set `EpsgCode` to `null` to accept the Variable's
configured coordinate reference system.
`InMemoryGeoLocationProvider` ships as a reference implementation for tests and
for servers whose positions are pushed in from elsewhere. Watching an unknown
source does not create a sample: reads return `BadNotFound` until `Update`
supplies one. `Fault` makes reads surface the supplied exception even for an
unknown source; a subsequent `Update` clears the fault and publishes its sample.

## Client

Register client factories over the managed-session registration:

```csharp
services.AddOpcUa()
    .AddClient(options => { /* endpoint and application options */ })
    .AddPositioningClient();
```

Direct constructors are also available:

```csharp
var rsl = new RelativeSpatialLocationClient(session, telemetry);
var gpos = new GlobalPositioningClient(session, telemetry);

await foreach (PositioningObjectEntry list in
    rsl.EnumerateSpatialObjectListsAsync(ct))
{
    await foreach (PositioningObjectEntry spatialObject in
        rsl.EnumerateSpatialObjectsAsync(list.NodeId, ct))
    {
        RelativeSpatialFrameValue frame =
            await rsl.ReadPositionFrameAsync(spatialObject.NodeId, ct);
    }
}

await foreach (PositioningObjectEntry zone in gpos.EnumerateZonesAsync(ct))
{
    GroundControlPointFitResult transform =
        await gpos.ReadZoneTransformAsync(zone.NodeId, cancellationToken: ct);
}
```

`ResolveFrameToWorldAsync` follows the RSL `Base` chain, composes each
`ThreeDFrame`, and rejects missing nodes, bad status, cycles, and excessive
depth. `ObserveFrameAsync`, `ObservePositionFrameAsync`,
`ObserveNodeVersionAsync`, `ObserveGlobalPositionAsync`, and
`ObserveGlobalLocationAsync` use `IStreamingSubscription`.

GPOS structured types are registered with both the session and message-context
encodeable factories before reads, so binary `ExtensionObject` values decode to
the generated types.

## MCP tools

The `OPCFoundation.NetStandard.Opc.Ua.Mcp.Positioning` package exposes the
existing typed positioning clients as seven read-only MCP tools:

| Tool | Discriminator and targets |
|---|---|
| `positioning_rsl_list` | `scope`: `SpatialObjectLists`, `SpatialObjects` (list `parentNodeId`), `Frames` (folder `parentNodeId`) |
| `positioning_rsl_read` | `facet`: `PositionFrame` (spatial object), `Frame` or `WorldFrame` (frame variable) |
| `positioning_rsl_observe` | `facet`: `PositionFrame`, `Frame`, `NodeVersion` (list) |
| `positioning_gpos_list` | Enumerate Zones under GlobalLocations |
| `positioning_gpos_read` | `facet`: `GlobalPosition`, `GlobalLocation` (variable), `ZoneTransform` (Zone) |
| `positioning_gpos_transform` | `direction`: `GlobalToLocal` or `LocalToGlobal` using the Zone's control points |
| `positioning_gpos_observe` | `facet`: `GlobalPosition` or `GlobalLocation` (variable) |

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

services.AddOpcUaMcpCore();
services.AddOpcUaMcpPositioning();
services.AddMcpServer()
    .WithOpcUaMcpFilters()
    .WithOpcUaPositioningTools(McpToolProfile.Positioning);
```

The registration also supports `McpToolProfileSet`. Connect first with the
contributed connection tools; `sessionName` selects a named session and may
be omitted when exactly one session is active. Accessors resolve the current
session for every call. No inventory, proxy, frame chain or fitted transform
is cached.

For example, resolve a discovered frame with `positioning_rsl_read`:

```json
{
  "nodeId": "ns=2;s=RobotFrame",
  "facet": "WorldFrame",
  "angleUnit": "Radians"
}
```

Replace example NodeIds with the actual discovered targets. WorldFrame
requires the actual RSL orientation units (`Radians` or `Degrees`); raw
Frame/PositionFrame reads retain the server's units without conversion.
The result includes the base-frame chain and the existing transform's
row-major rotation matrix, translation and frame representation.

To convert geographic coordinates with `positioning_gpos_transform`:

```json
{
  "zoneNodeId": "ns=2;s=FactoryZone",
  "direction": "GlobalToLocal",
  "x": 8.55,
  "y": 47.37,
  "angleUnit": "Degrees",
  "fitOptions": {
    "mode": "Rigid",
    "controlPointAngleUnit": "Degrees",
    "allowReflection": false,
    "coordinateReferenceSystem": "EPSG:4326"
  }
}
```

Global `x`/`y` are longitude/latitude; optional `z` is elevation in metres.
Local `x`/`y`/`z` use the Zone's coordinate units, with omitted local Z
defaulting to zero. Input/output geographic `angleUnit` is independent of
the actual server `controlPointAngleUnit`. Fit modes are `Rigid`,
`Similarity` and `Affine`. The existing fitter determines 2D versus 3D
from the control points; missing elevation is not fabricated.
`ZoneTransform` returns dimension, rank, residuals, determinant and
invertibility together with the effective options.

The default transformer is WGS84/EPSG:4326. A host can inject
`ICoordinateReferenceSystemTransformer` and callers must explicitly request
its CRS identifier in `fitOptions`. An unsupported or mismatched identifier
is rejected. Raw GlobalPosition/GlobalLocation numeric CRS codes remain
unchanged and are distinct from the transformer identifier.

Discovery supports `offset` and `maxResults` (default 100, maximum 500).
Observe calls support `durationMs` (default 1,000, maximum 30,000) and
`maxItems` (default 100, maximum 500). They borrow the selected
`ManagedSession.DefaultStreaming`, release only their own enumerators, and
propagate external cancellation; they never dispose the host subscription.
Missing optional nodes remain service errors, distinguishable from empty
observation windows. Encoded values preserve optional fields, while
`metadata` carries UA-encoded status and source timestamp; the raw
status code, source node, type and CRS are also preserved where applicable.
Every tool is annotated read-only/non-destructive and none invents a write
or method-call surface.

## Transform conventions

RSL uses a right-handed coordinate system:

- `A`: roll about X
- `B`: pitch about Y
- `C`: yaw about Z
- column-vector matrix: `Rz(C) * Ry(B) * Rx(A)`

`RslFrameTransform` supports conversion, composition, inversion, point
transformation, and deterministic gimbal-lock handling.

The built-in coordinate-reference-system transformer supports WGS84 /
EPSG:4326, ECEF, and local East-North-Up coordinates. Inject
`ICoordinateReferenceSystemTransformer` for another CRS.

`GroundControlPointFitter` supports rigid, similarity, and affine fits. When
elevation or rank is insufficient, it selects a horizontal 2D fit. It forces
a proper rotation for rigid and similarity modes, and rejects affine
reflections unless you enable them. The fitter reports residual, determinant,
rank, dimension, and invertibility diagnostics.

## Robot and OpenUSD sample

[`MinimalRobotServer`](../samples/Robotics/MinimalRobotServer) composes RSL and GPOS into
its Robotics node manager. Both robots publish `GlobalLocation` values and
derived local RSL frames. Each robot independently selects `Fixed`,
`FigureEight`, `Circle`, or `Shuttle` motion. RSL position/orientation drive
OpenUSD `double3` transform operators, while GPOS longitude, latitude, and
elevation drive live custom attributes.

The generic OpenUSD connector has no Positioning dependency; it handles core
`ThreeDCartesianCoordinates`, `ThreeDOrientation`, and `ThreeDFrame` values.
