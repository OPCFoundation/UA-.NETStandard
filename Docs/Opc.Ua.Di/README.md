# OPC UA Device Integration (DI) — Developer Guide

This guide documents the `Opc.Ua.Di*` library trio that ships in this
repository and how applications consume it through the unified
`AddOpcUa()` dependency-injection (DI hosting) pattern.

| Library | Role |
|---------|------|
| `Opc.Ua.Di` | Model assembly: source-generated NodeId tables, DataTypes, ObjectType client proxies, `AddOpcUaDi(NodeStateCollection)` extension. |
| `Opc.Ua.Di.Server` | Server: `DiNodeManager`, fluent `IDeviceBuilder`, locking service, software-update package store, hosting integration. |
| `Opc.Ua.Di.Client` | Client: `DiDeviceClient`, `DiDiscoveryClient`, `DiTopologyClient`, `DiLockClient`, `SoftwareUpdateClient`, hosting integration. |

The running example is `Applications/MinimalPumpServer`, which combines
all three layers with the Machinery + Pumps companion specs.

## Reference index

- [DeviceBuilder](DeviceBuilder.md) — fluent surface for creating
  and configuring DI devices.
- [Hosting](Hosting.md) — `AddOpcUa().AddServer().AddOpcUaDi()` +
  `ConfigureDevicesFor<TNodeManager>`.
- [LockService](LockService.md) — `ILockService` / `DefaultLockService`
  and method binding.
- [SoftwareUpdate](SoftwareUpdate.md) — `ISoftwarePackageStore`,
  in-memory + file-system implementations.
- [ClientHelpers](ClientHelpers.md) — `DiLockClient`,
  `DiTopologyClient`, `SoftwareUpdateClient`.
- [ComplianceMatrix](ComplianceMatrix.md) — current OPC 10000-100
  coverage status.

## Quick start

### Plain DI server

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MyDiServer";
        o.EndpointUrls.Add("opc.tcp://localhost:48010/MyDiServer");
    })
    .AddOpcUaDi()
    .ConfigureDevicesFor<DiNodeManager>(async ctx =>
    {
        var device = await ctx.CreateDeviceAsync(
            new QualifiedName("Sensor #1", ctx.Manager.DiNamespaceIndex));
        device.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme");
            id.SerialNumber = "SN-001";
            id.DeviceClass = "Sensor";
        });
    });

await builder.Build().RunAsync();
```

### Companion-spec server (Pumps + DI)

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(o => { ... })
    .AddNodeManager<Pumps.PumpNodeManagerFactory>()
    // Pump factory already loads DI via its own ModelLoaderBuilder; do NOT
    // call AddOpcUaDi() — that would double-register the DI namespace.
    .ConfigureDevicesFor<Pumps.PumpNodeManager>(async ctx =>
    {
        var pump = await ctx.CreateDeviceAsync(
            new QualifiedName("Pump #2", ctx.Manager.DiNamespaceIndex));
        pump.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme Pumps Inc.");
            id.SerialNumber = "SN-DI-2";
            id.DeviceClass = "Pump";
        });
    });
```

### Client

```csharp
builder.Services
    .AddOpcUa()
    .AddClient(o => { o.Configuration = ...; o.Session.Endpoint = ...; })
    .AddOpcUaDi();

// In your service:
public sealed class MyAppService(
    IDiDiscoveryService discovery,
    Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>> deviceFactory,
    Func<CancellationToken, ValueTask<DiTopologyClient>> topologyFactory)
{
    public async Task PrintAsync(CancellationToken ct)
    {
        DiTopologyClient topology = await topologyFactory(ct);
        await foreach (TopologyEntry entry in topology.EnumerateDevicesAsync(ct))
        {
            DiDeviceClient device = await deviceFactory(entry.NodeId, ct);
            DeviceIdentification id = await device.ReadIdentificationAsync(ct);
            Console.WriteLine($"{id.Manufacturer} {id.Model} ({id.SerialNumber})");
        }
    }
}
```

## See also

- [CompanionSpecLibraries](../CompanionSpecLibraries.md) — packaging
  conventions for companion-spec libraries.
- [FileSystemClient](../FileSystemClient.md) — file-transfer client
  used by `SoftwareUpdateClient` and `FileSystemPackageStore`.
- [SourceGeneratedNodeManagers](../SourceGeneratedNodeManagers.md) —
  the underlying source generator + fluent API.
