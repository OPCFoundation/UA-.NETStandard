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

Browse to `Objects > Pumps > Pump #1` in any OPC UA client (e.g.
UaExpert) to explore the simulated pump.

## What the sample demonstrates

| Feature | Where |
|---------|-------|
| `AddOpcUa().AddServer(...).AddNodeManager<T>()` hosting | `Program.cs` |
| Multi-model composition (DI library + 2 NodeSet2 XMLs) | `PumpNodeManager.cs` `LoadPredefinedNodesAsync` |
| Identification properties via `WithProperty(name, value)` | `PumpNodeManager.Configure.cs` `WireIdentification` |
| Engineering units / EURange via `WithEngineeringUnits` / `WithEURange` | `WireMeasurements` |
| 250 ms simulation tick via `Simulation(...).OnTick(...)` | `Configure` → `AdvanceSimulation` |
| Read-write actuation variables | `WireActuation` |
| Limit alarm with thresholds and acknowledge handler via `CreateLimitAlarm(...).WithLimits(...)` | `WireSupervision` |
| Boolean supervision → alarm activation via `.ActivatesAlarm(...)` | `WireSupervision` |
| Per-read simulated boolean / discrete signals | `WireSignals`, `WireSupervision` |
| Maintenance counters (operating time, number of starts) | `WireMaintenance`, `AdvanceSimulation` |

## Architecture

```
MinimalPumpServer/
├── Program.cs                       # AddOpcUa().AddServer(...).AddNodeManager<T>()
├── PumpNodeManager.cs               # Hand-written FluentNodeManagerBase
│                                    # + LoadPredefinedNodesAsync (multi-model)
│                                    # + CreateAddressSpaceAsync (builder setup)
├── PumpNodeManager.Configure.cs     # partial — fluent wiring + simulation tick
├── MinimalPumpServer.csproj         # ProjectReference to Opc.Ua.Di model lib
│                                    # Embedded Machinery + Pumps NodeSet2 XMLs
├── Model/
│   ├── Opc.Ua.Di.NodeSet2.xml       # DI (loaded for cross-ref by Machinery/Pumps)
│   ├── Opc.Ua.Machinery.NodeSet2.xml
│   └── Opc.Ua.Pumps.NodeSet2.xml    # 6.4 MB — the OPC 40223 spec deliverable
└── Properties/AssemblyInfo.cs
```

The `Opc.Ua.Di` model library is consumed as a NuGet/project reference
(its types live under the `Opc.Ua.Di` namespace and are
source-generated from the ModelDesign XML, not the NodeSet2). The
Machinery and Pumps NodeSet2 XMLs are embedded resources loaded at
runtime via `ModelLoaderBuilder.ImportEmbeddedNodeSet(...)` because
their source-gen output would reference DI under the wrong namespace
prefix (see [Companion specification libraries — Common pitfalls](../../Docs/CompanionSpecLibraries.md#6-common-pitfalls)).

## Inspecting / extending the sample

- **Add a measurement**: open `PumpNodeManager.Configure.cs`, add a
  call to `TryWireMeasurement(builder, browsePath, getter, units, min, max)`
  inside `WireMeasurements`, then add a field + line to
  `AdvanceSimulation` that updates the value each tick.
- **Add an alarm**: inside `WireSupervision`, chain another
  `events.CreateLimitAlarm(...).WithLimits(...)` and wire the
  triggering boolean variable via `.ActivatesAlarm(...)`.
- **Add a second pump**: use the G1 instance creation extension —
  `builder.Node("Pumps").CreateInstance(name, typeDefId, factory)`.
  The MinimalPumpServer doesn't currently exercise instance creation
  beyond what the Pumps NodeSet2 already declares, but the underlying
  fluent API supports it; see
  [Creating instances of model types](../../Docs/SourceGeneratedNodeManagers.md#creating-instances-of-model-types).

## NativeAOT publishing

```pwsh
cd Applications/MinimalPumpServer
dotnet publish -c Release -r win-x64
```

The pump server publishes cleanly under NativeAOT — no trim or AOT
warnings — because every fluent extension is reflection-free and the
generated model factories are statically rooted.
