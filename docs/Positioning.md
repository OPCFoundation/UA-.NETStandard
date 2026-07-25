# OPC UA Relative Spatial Location and Global Positioning

The Positioning libraries implement OPC UA Relative Spatial Location (RSL,
[OPC 10000-210](https://reference.opcfoundation.org/specs/OPC-10000-210)) and
Global Positioning (GPOS,
[OPC 10000-211](https://reference.opcfoundation.org/specs/OPC-10000-211)).
They use the released RSL 1.00.1 and GPOS 1.0.0 NodeSets. GPOS depends on RSL.

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
    .AddGlobalPositionProvider<MyGlobalPositionProvider>()
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

`IGlobalPositionProvider` is intentionally technology-neutral. GPS/WGS84 is a
built-in use case, but RTLS, UWB, RFID, local floor-plan coordinates, and other
tracking systems use the same contract. An RSL provider is also useful when a
robot controller, metrology system, or kinematic service is authoritative for a
relative frame rather than a global coordinate.

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

`GroundControlPointFitter` supports rigid, similarity, and affine fits. It
selects a horizontal 2D fit when elevation or rank is insufficient, forces a
proper rotation for rigid/similarity modes, rejects affine reflections unless
enabled, and reports residual, determinant, rank, dimension, and invertibility
diagnostics.

## Robot and OpenUSD sample

[`MinimalRobotServer`](../samples/MinimalRobotServer) composes RSL and GPOS into
its Robotics node manager. Both robots publish `GlobalLocation` values and
derived local RSL frames. Each robot independently selects `Fixed`,
`FigureEight`, `Circle`, or `Shuttle` motion. RSL position/orientation drive
OpenUSD `double3` transform operators, while GPOS longitude, latitude, and
elevation drive live custom attributes.

The generic OpenUSD connector has no Positioning dependency; it handles core
`ThreeDCartesianCoordinates`, `ThreeDOrientation`, and `ThreeDFrame` values.
