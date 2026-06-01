# MinimalPumpServer

A self-contained, NativeAOT-friendly OPC UA server that demonstrates
the [OPC 40223 Pumps companion specification](https://reference.opcfoundation.org/specs/OPC-40223)
with a full live simulation, wired entirely through the fluent
`INodeManagerBuilder` API.

The pump sample is the integration test for every fluent API extension
shipped under `Libraries/Opc.Ua.Server/Fluent/`. Each extension is
documented in
[Source-generated NodeManagers — Building richer node managers](../../Docs/SourceGeneratedNodeManagers.md#building-richer-node-managers--the-fluent-extension-surface).

## Running the sample

```pwsh
cd Applications/MinimalPumpServer
dotnet run -c Release
```

The server listens on `opc.tcp://localhost:62542/MinimalPumpServer` by
default. Override with `--port 62550`.

Sample console output:

```
info: Opc.Ua.Server.MasterNodeManager
      MasterNodeManager.Startup - NodeManagers=3
info: Pumps.PumpNodeManager
      Configuring PumpNodeManager fluent wiring...
info: Pumps.PumpNodeManager
      PumpNodeManager: address space ready (10330 predefined nodes).
info: Opc.Ua.Server.StandardServer
      OPC UA server listening at opc.tcp://localhost:62542/MinimalPumpServer.
```

Browse to `Objects > DeviceSet > Pump #1` in any OPC UA client (e.g.
UaExpert) to explore the simulated pump. A second declarative pump,
`Pump #2`, sits alongside under the same `DeviceSet` parent — it
demonstrates the DI hosting `ConfigureDevicesFor` flow without the
hand-wired fluent simulation.

## What the sample demonstrates

| Feature | Where |
|---------|-------|
| `AddOpcUa().AddServer(...).AddNodeManager<T>()` hosting | `Program.cs` |
| Multi-model composition (DI library + locally source-generated Machinery + Pumps) | `PumpNodeManager.cs` `LoadPredefinedNodesAsync` |
| Identification properties via `WithProperty(name, value)` | `PumpNodeManager.Configure.cs` `WithIdentification` |
| Engineering units / EURange via `WithEngineeringUnits` / `WithEURange` | `WithMeasurements` |
| 250 ms simulation tick via `Simulation(...).OnTick(...)` | `Configure` → `AdvanceSimulation` |
| Read-write actuation variables | `WithActuation` |
| Limit alarm with thresholds and acknowledge handler via `CreateLimitAlarm(...).WithLimits(...)` | `WithSupervision` |
| Boolean supervision → alarm activation via `.ActivatesAlarm(...)` | `WithSupervision` |
| Per-read simulated boolean / discrete signals | `WithSignals`, `WithSupervision` |
| Maintenance counters (operating time, number of starts) | `WithMaintenance`, `AdvanceSimulation` |

## Architecture

```
MinimalPumpServer/
├── Program.cs                       # AddOpcUa().AddServer(...).AddNodeManager<T>()
├── PumpNodeManager.cs               # Hand-written FluentNodeManagerBase
│                                    # + LoadPredefinedNodesAsync (multi-model)
│                                    # + CreateAddressSpaceAsync (builder setup)
├── PumpNodeManager.Configure.cs     # partial — fluent wiring + simulation tick
├── MinimalPumpServer.csproj         # ProjectReference to Opc.Ua.Di model lib
│                                    # AdditionalFiles for Machinery + Pumps
│                                    # NodeSet2 (consumed by source generator)
├── Model/
│   ├── Opc.Ua.Machinery.NodeSet2.xml  # AdditionalFiles — build-time only
│   └── Opc.Ua.Pumps.NodeSet2.xml      # AdditionalFiles — build-time only
└── Properties/AssemblyInfo.cs
```

The `Opc.Ua.Di` model library is consumed as a project reference (its
types live under the `Opc.Ua.Di` namespace and are source-generated
from the ModelDesign XML). Cross-namespace references from Machinery
and Pumps to DI types resolve through the
`[assembly: ModelDependencyAttribute]` carried in the `Opc.Ua.Di`
assembly — no DI NodeSet2 XML needed in this project. The unified
attribute carries the compact type-table payload that the consumer's
source generator imports at compile time; see
[ModelDependencies.md](../../Docs/ModelDependencies.md) for the wire
format and consumer-side flow.

The Machinery and Pumps NodeSet2 XMLs are **source-generated locally
inside this assembly** via the `<AdditionalFiles>` plumbing in the
`.csproj`. The generator emits typed `*State` classes, NodeId tables,
and the `AddOpcUaMachinery` / `AddOpcUaPumps` extension methods that
`LoadPredefinedNodesAsync` calls. No runtime XML loading happens — the
`Model/` folder is a build-time input only. Consumer assemblies that
want to reference Machinery or Pumps the same way they reference
`Opc.Ua.Di` should source-generate against the model XML inside their
own assembly using the same `<AdditionalFiles>` pattern.

## Extending the sample

- **Add a measurement**: open `PumpNodeManager.Configure.cs`, add a
  call to `AddMeasurement(builder, browsePath, getter, units, min, max)`
  inside `WithMeasurements`, then add a field + line to
  `AdvanceSimulation` that updates the value each tick.
- **Add an alarm**: inside `WithSupervision`, chain another
  `builder.Node("Pump #1/Events").CreateLimitAlarm(...).WithLimits(...)`
  and wire the triggering boolean variable via `.ActivatesAlarm(...)`.
- **Add a second pump**: two patterns are demonstrated in the sample.
  - **Hand-rolled** (used for `Pump #1`): in `PumpNodeManager.CreatePumpInstanceAsync`, call `context.CreateInstanceOfPumpType(deviceSet, browseName)`, attach it to the DI `DeviceSet`, and `AddPredefinedNodeAsync(pump)`. The fluent `Configure.cs` then wires its measurements, alarms, and simulation by browse path.
  - **DI declarative** (used for `Pump #2`): in `Program.cs`, call `ctx.CreateDeviceAsync(new QualifiedName("Pump #N", ctx.Manager.DiNamespaceIndex))` from a `ConfigureDevicesFor<PumpNodeManager>` block, then call `pump.WithIdentification(...)` for the nameplate. This route exercises the `Opc.Ua.Di.Server` builder surface.

## NativeAOT publishing

```pwsh
cd Applications/MinimalPumpServer
dotnet publish -c Release -r win-x64
```

The pump server publishes cleanly under NativeAOT — no trim or AOT
warnings — because every fluent extension is reflection-free and the
generated model factories are statically rooted.
