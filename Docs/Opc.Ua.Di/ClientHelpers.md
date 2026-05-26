# DI Client Helpers

Beyond `DiDeviceClient` and `DiDiscoveryClient` (which ship in
the base Phase 6 client library), Phase 8D/8F add three typed client
wrappers that compose the standard OPC UA `Session` surface.

| Helper | Purpose | Phase |
|--------|---------|-------|
| `DiLockClient` | Wraps the four `LockingServicesType` methods. | 8D |
| `DiTopologyClient` | Browses `DeviceSet`, `NetworkSet`, `DeviceTopology`. | 8D |
| `SoftwareUpdateClient` | Reads the software-version property of a `SoftwareUpdateType` instance. | 8F |

All three live in `Opc.Ua.Di.Client` and are registered automatically
when applications call `services.AddOpcUa().AddClient(...).AddOpcUaDi()`
on the client-side builder.

## DiLockClient

```csharp
public sealed class DiLockClient
{
    public DiLockClient(ISession session, NodeId lockNodeId, ITelemetryContext telemetry);
    public ValueTask<int> InitLockAsync(string context, CancellationToken ct = default);
    public ValueTask<int> RenewLockAsync(CancellationToken ct = default);
    public ValueTask<int> ExitLockAsync(CancellationToken ct = default);
    public ValueTask<int> BreakLockAsync(CancellationToken ct = default);
}
```

Each method invokes the corresponding `LockingServicesType.{Method}`
generated NodeId on the server and returns the integer status code
(see `LockStatus` in [LockService.md](LockService.md) for the OPC
spec values).

The constructor takes the NodeId of the `Lock` instance — typically
the `device.Lock` child, not the device itself. Resolve it via
`Session.TranslateBrowsePathsToNodeIdsAsync` or by browsing the
device's `HasComponent` references for a child named `"Lock"`.

```csharp
DiLockClient lockClient = new(session, lockNodeId, telemetry);
int status = await lockClient.InitLockAsync("client-tag");
if (status == LockStatus.Ok)
{
    try
    {
        // Do work that requires exclusive access.
    }
    finally
    {
        await lockClient.ExitLockAsync();
    }
}
```

## DiTopologyClient

```csharp
public sealed class DiTopologyClient
{
    public DiTopologyClient(ISession session, ITelemetryContext telemetry);
    public NodeId DeviceSetId { get; }
    public NodeId NetworkSetId { get; }
    public NodeId DeviceTopologyId { get; }

    public ValueTask<IReadOnlyList<TopologyEntry>> EnumerateDevicesAsync(CancellationToken ct = default);
    public ValueTask<IReadOnlyList<TopologyEntry>> EnumerateNetworksAsync(CancellationToken ct = default);
    public ValueTask<IReadOnlyList<TopologyEntry>> EnumerateChildrenAsync(NodeId parentNodeId, CancellationToken ct = default);
}

public sealed record TopologyEntry(
    NodeId NodeId,
    string DisplayName,
    NodeId TypeDefinitionId,
    QualifiedName BrowseName);
```

`EnumerateDevicesAsync` and `EnumerateNetworksAsync` browse the
hierarchical references of `DeviceSet` and `NetworkSet` and return
the direct children that are Objects. `EnumerateChildrenAsync`
provides the same surface for arbitrary nodes — useful when walking
the `DeviceTopology` tree.

```csharp
DiTopologyClient topology = new(session, telemetry);
foreach (TopologyEntry device in await topology.EnumerateDevicesAsync())
{
    Console.WriteLine($"{device.BrowseName}: {device.DisplayName}");
}
```

## SoftwareUpdateClient

```csharp
public sealed class SoftwareUpdateClient
{
    public SoftwareUpdateClient(ISession session, NodeId softwareUpdateNodeId, ITelemetryContext telemetry);
    public ValueTask<string> ReadSoftwareVersionAsync(CancellationToken ct = default);
}
```

The constructor takes the NodeId of the `SoftwareUpdateType`
instance — typically the `device.SoftwareUpdate` child.
`ReadSoftwareVersionAsync` does a browse-and-read to fetch the
`SoftwareVersion` property; returns an empty string when the
property is absent. Method-level operations (load / install /
power-cycle / confirm) are handled by the source-generated
`*StateMachineClient` proxies in the model assembly.

## Hosting registration

When `services.AddOpcUa().AddClient(o => { ... }).AddOpcUaDi()` is
called, the following factories are registered as singletons:

```csharp
Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>     // existing
Func<NodeId, CancellationToken, ValueTask<DiLockClient>>       // 8D
Func<CancellationToken, ValueTask<DiTopologyClient>>           // 8D
Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>> // 8F
IDiDiscoveryService                                            // existing
```

Inject the factory you need into your application services; the
factory opens (or reuses) the lazy `ManagedSession` on first call.

```csharp
public sealed class MyMonitor(
    Func<CancellationToken, ValueTask<DiTopologyClient>> topologyFactory,
    Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>> deviceFactory,
    Func<NodeId, CancellationToken, ValueTask<DiLockClient>> lockFactory)
{
    public async Task ScanAsync(CancellationToken ct)
    {
        DiTopologyClient topo = await topologyFactory(ct);
        foreach (TopologyEntry e in await topo.EnumerateDevicesAsync(ct))
        {
            DiDeviceClient dev = await deviceFactory(e.NodeId, ct);
            DeviceIdentification id = await dev.ReadIdentificationAsync(ct);
            // ...
        }
    }
}
```

## See also

- [Hosting](Hosting.md) — DI registration details.
- [LockService](LockService.md) — server-side counterpart.
- [SoftwareUpdate](SoftwareUpdate.md) — package store + state machines.
