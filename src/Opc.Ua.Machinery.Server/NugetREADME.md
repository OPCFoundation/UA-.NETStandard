# OPCFoundation.NetStandard.Opc.Ua.Machinery.Server

Server hosting for the OPC 40001 *OPC UA for Machinery* specification series.

The package turns the source-generated model assemblies
(`Opc.Ua.Machinery`, `…​.ProcessValues`, `…​.Jobs`, `…​.Energy`,
`…​.Result`) into a running address space: a node manager that loads the
type system, the `Machines` folder every Machinery server publishes, and a
fluent builder surface for the building blocks of OPC 40001-1 and the four
specialised parts.

## What it gives you

| Concern | API |
| --- | --- |
| Hosting | `AddMachinery()` / `ConfigureMachinery(...)` on `IOpcUaServerBuilder` |
| Node manager | `MachineryNodeManager : DiNodeManager` plus `MachineryNodeManagerFactory` |
| Coexistence | `nodes.AddMachineryTypeSystem(context, parts)` + `ConfigureMachineryFor<TNodeManager>()` for a server that already owns the DI address space |
| Machines | `context.AddMachine(name)` — identification, monitoring, components, equipment, notifications, counters |
| State | `IMachineryItemStateController` / `IMachineryOperationModeController`, server-driven as OPC 40001-1 requires |
| Process values | `WithProcessValue(...)` — OPC 40001-2 over PADIM `AnalogSignalType` |
| Jobs | `WithJobManagement(...)` — OPC 40001-3 over ISA-95 Job Control V2 |
| Energy | `WithEnergy(...)` — OPC 40001-4 over OPC 34100 ECM |
| Results | `WithResultManagement(...)` and the stand-alone `MachineryResultNodeManager` — OPC 40001-101 including the `GenerateFileForRead` download path |

## Quick start

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(options => { /* … */ })
    .AddMachinery()
    .ConfigureMachinery(async machinery =>
    {
        await machinery.AddMachine("Press-1")
            .WithIdentification(id =>
            {
                id.Manufacturer = "Acme";
                id.Model = "P-500";
                id.SerialNumber = "SN-0001";
            })
            .WithMonitoring(monitoring => monitoring
                .WithMachineryItemState()
                .WithOperationMode())
            .BuildAsync();
    });
```

Only what you configure is advertised: the node manager reports a
Machinery conformance unit exactly when the structure behind it was
materialised and wired.

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
