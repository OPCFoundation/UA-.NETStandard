# PumpDeviceIntegrationServer

A self-contained, NativeAOT-friendly OPC UA server that demonstrates
the [OPC 40223 Pumps companion specification](https://reference.opcfoundation.org/specs/OPC-40223)
with a full live simulation **and** the
[OPC 10000-100 (Device Integration) software-update facet](https://reference.opcfoundation.org/specs/OPC-10000-100),
wired entirely through the fluent `INodeManagerBuilder` API and the
DI hosting integration.

The pump sample is the integration test for every fluent API extension
shipped under `src/Opc.Ua.Server/Fluent/`. Each extension is
documented in
[Source-generated NodeManagers — Building richer node managers](../../docs/SourceGeneratedNodeManagers.md#building-richer-node-managers--the-fluent-extension-surface).

## Running the sample

```pwsh
cd samples/PumpDeviceIntegrationServer
dotnet run -c Release
```

The server listens on `opc.tcp://localhost:62542/PumpDeviceIntegrationServer`
by default. Override with `--port 62550`.

Sample console output:

```
info: Opc.Ua.Server.MasterNodeManager
      MasterNodeManager.Startup - NodeManagers=3
info: Pumps.PumpNodeManager
      Configuring PumpNodeManager fluent wiring...
info: Pumps.PumpNodeManager
      PumpNodeManager: address space ready (10330 predefined nodes).
info: Opc.Ua.Server.StandardServer
      OPC UA server listening at opc.tcp://localhost:62542/PumpDeviceIntegrationServer.
```

Browse to `Objects > DeviceSet > Pump #1` in any OPC UA client (e.g.
UaExpert) to explore the simulated pump. A second declarative pump,
`Pump #2`, sits alongside under the same `DeviceSet` parent — it
demonstrates the DI hosting `ConfigureDevicesFor` flow without the
hand-wired fluent simulation, and additionally exposes the OPC
10000-100 software-update facet (`Pump #2 > SoftwareUpdate`) backed
by an in-memory `ISoftwarePackageStore` seeded with two demo
firmware payloads.

## Running in Docker

A [`Dockerfile`](./Dockerfile) is provided that builds the Release
publish output on the .NET **AzureLinux 3** base images and runs it as a
non-root user.

> **Build from the repository root**, not from this folder. The image
> needs the full source tree (`src/`, `src/`, `tools/`), so the
> Docker build context must be the repo root and the Dockerfile is
> selected with `-f`. Running `docker build .` from inside this folder
> fails fast with a message telling you the correct command.

```pwsh
# from the repository root:
docker build -f samples/PumpDeviceIntegrationServer/Dockerfile `
             -t pumpdeviceintegrationserver:local .
```

Run it, publishing the OPC UA port:

```pwsh
docker run --rm -p 62542:62542 pumpdeviceintegrationserver:local
```

Inside the container the endpoint binds to `0.0.0.0` so it is reachable
from the host. Override the bind host and port via environment variables:

```pwsh
docker run --rm -p 62550:62550 `
           -e host=0.0.0.0 -e port=62550 `
           pumpdeviceintegrationserver:local
```

The server creates its certificate/PKI store under `/app` at runtime.
To persist certificates across container restarts, mount a volume:

```pwsh
docker run --rm -p 62542:62542 `
           -v pump-pki:/app/pki `
           pumpdeviceintegrationserver:local
```

The image is built and published to the GitHub Container Registry by the
[`pump-device-integration-server-docker.yml`](../../.github/workflows/pump-device-integration-server-docker.yml)
workflow on every push to `master` and on manual dispatch.

## What the sample demonstrates

| Feature | Where |
|---------|-------|
| `AddOpcUa().AddServer(...).AddNodeManager<T>()` hosting | `Program.cs` |
| Multi-model composition (DI library + locally source-generated Machinery + Pumps) | `PumpNodeManager.cs` `LoadPredefinedNodesAsync` |
| Identification properties via `WithProperty(name, value)` | `PumpNodeManager.Configure.cs` `WithIdentification` |
| Optional-child materialisation via generator-emitted `AddXxx(context)` helpers (Operational / Measurements / Events / SupervisionProcessFluid / SupervisionPumpOperation / Maintenance) | `PumpNodeManager.cs` `MaterialisePumpOptionalChildren` |
| Engineering units / EURange via `WithEngineeringUnits` / `WithEURange` | `WithMeasurements` |
| Discrete `NumberOfStarts` counter wired via `Variable<uint>(...).OnRead(...)` | `WithMeasurements` |
| 250 ms simulation tick via `Simulation(...).OnTick(...)` | `Configure` → `AdvanceSimulation` |
| Limit alarm with thresholds and acknowledge handler via `CreateLimitAlarm(...).WithLimits(...)` | `WithSupervision` |
| Boolean supervision (TwoStateDiscreteState) → alarm activation via `.ActivatesAlarm(...)` | `WithSupervision` |
| Cross-namespace path resolution (Pump #1 in Pumps NS → Operational in Machinery NS → Measurements in Pumps NS, all in one unqualified browse path) | `src/Opc.Ua.Server/Fluent/BrowsePathResolver.cs` |
| Cross-pump device-health simulation via `RegisterSupervisedDeviceHealth` + `WithDeviceHealth` | `PumpNodeManager.cs` + `Program.cs` (`Pump #2`) |
| DI declarative device + `WithIdentification` | `Program.cs` (`Pump #2`) |
| Software-update facet (`ISoftwarePackageStore` + `WithSoftwareUpdate`) | `Program.cs` (`Pump #2`), `SoftwarePackageSeeder.cs` |

## Architecture

```
PumpDeviceIntegrationServer/
├── Program.cs                          # AddOpcUa().AddServer(...).AddNodeManager<T>()
│                                       # + ConfigureDevicesFor declarative Pump #2 + SU
├── PumpNodeManager.cs                  # Hand-written FluentNodeManagerBase
│                                       # + LoadPredefinedNodesAsync (multi-model)
│                                       # + CreateAddressSpaceAsync (builder setup)
├── PumpNodeManager.Configure.cs        # partial — fluent wiring + simulation tick
├── SoftwarePackageSeeder.cs            # seeds demo firmware payloads
├── PumpDeviceIntegrationServer.csproj  # ProjectReference to Opc.Ua.Di model lib
│                                       # AdditionalFiles for Machinery + Pumps
│                                       # NodeSet2 (consumed by source generator)
├── Model/
│   ├── Opc.Ua.Machinery.NodeSet2.xml   # AdditionalFiles — build-time only
│   └── Opc.Ua.Pumps.NodeSet2.xml       # AdditionalFiles — build-time only
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
[ModelDependencies.md](../../docs/ModelDependencies.md) for the wire
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
- **Swap the software-update store**: replace the singleton
  `ISoftwarePackageStore` registration in `Program.cs` with a
  `FileSystemPackageStore` over an `IFileSystemProvider` for
  on-disk persistence:

  ```csharp
  builder.Services.AddSingleton<ISoftwarePackageStore>(_ =>
  {
      var provider = new PhysicalFileSystemProvider(
          rootDirectory: "/var/lib/sw-update",
          mountName: "Packages",
          isWritable: true);
      return new FileSystemPackageStore(provider, rootPath: "/SoftwarePackages");
  });
  ```

## NativeAOT publishing

```pwsh
cd samples/PumpDeviceIntegrationServer
dotnet publish -c Release -r win-x64
```

The pump server publishes cleanly under NativeAOT — no trim or AOT
warnings — because every fluent extension is reflection-free and the
generated model factories are statically rooted.

## See also

- [`docs/DeviceIntegration.md`](../../docs/DeviceIntegration.md) —
  full developer guide for the DI library trio (device builder,
  hosting integration, lock service, software-update package store,
  client helpers).
- [`docs/SoftwareUpdate.md`](../../docs/SoftwareUpdate.md) —
  in-depth coverage of the software-update facet wiring, file-transfer
  pipeline, and client `UploadPackageAsync`.

