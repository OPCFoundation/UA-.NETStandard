# PumpDeviceIntegrationServer

A self-contained, NativeAOT-friendly OPC UA server that demonstrates
the [OPC 40223 Pumps companion specification](https://reference.opcfoundation.org/specs/OPC-40223)
with a full live simulation, wired through the fluent
`INodeManagerBuilder` API and the additive OPC 10000-100 topology-element
builder integration.

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
by default. Override with `--host localhost`, `--port 62550`, and
`--pumps N` (or the matching `host`, `port`, and `pumps` environment
variables). `--pumps` defaults to `2` and accepts values from 1 to 100.

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

Browse to `Objects > DeviceSet > Pump_1` in any OPC UA client (e.g.
UaExpert) to explore the first simulated pump. BrowseNames use
identifier-safe names (`Pump_1`, `Pump_2`, ...); DisplayNames keep the
operator-friendly labels (`Pump #1`, `Pump #2`, ...). Pass `--pumps N`
or set `pumps=N` to materialise more instances. Every pump is wired
through the same simulation, alarm, history, identification, maintenance,
and declarative DI `ConfigureDevicesFor` flow, so pumps added after
startup automatically join the same live simulation. All pumps publish
monitored data changes every 250 ms, with deterministic phase offsets so
their values do not move in lockstep.

Subscribe to the `EventNotifier` attribute on any pump to receive alarm
condition events when its simulated `MotorOverheat` state activates or
clears. Each pump is also registered as a root notifier, so the same events
are available from a subscription on the Server object.

## Validating the address space

The
[`Pump Address Space Validation`](../../.github/workflows/pump-address-space-validation.yml)
workflow runs nightly and can also be started manually. It builds this sample,
installs the latest stable
[`OpcUaAddressSpaceChecker`](https://www.nuget.org/packages/OpcUaAddressSpaceChecker)
global tool, and validates all `PumpType` instances.

To reproduce the validation locally, start the server and run the following
commands in another terminal:

```pwsh
dotnet tool install --global OpcUaAddressSpaceChecker
opcua-check-address-space `
    --endpoint opc.tcp://localhost:62542/PumpDeviceIntegrationServer `
    --type "nsu=http://opcfoundation.org/UA/Pumps/;i=1052" `
    --severity-threshold warning `
    --view-completeness complete `
    --require-complete-view
```

The nightly check keeps the tool's default `auto` validation-view policy and
fails on confirmed errors or any checker execution failure.

## Running in Docker

A [`Dockerfile`](./Dockerfile) is provided that builds the Release
publish output on the .NET **AzureLinux 3** base images and runs it as a
non-root user.

> **Build from the repository root**, not from this folder. The image
> needs the full source tree (`src/`, `samples/`, `tools/`), so the
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
           -e host=0.0.0.0 -e port=62550 -e pumps=4 `
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
| Push-style monitored value updates via `Bind(out IValueUpdater<T>)` | `CreatePumpSimulation` |
| Discrete `NumberOfStarts` counter published on change | `CreatePumpSimulation` |
| One 250 ms simulation tick for all phase-shifted pumps | `Configure` → `AdvanceSimulation` |
| Browsable/subscribable limit alarm with thresholds and acknowledge handler via `CreateLimitAlarm(...).WithLimits(...)` | `CreatePumpSimulation` |
| Boolean supervision (TwoStateDiscreteState) → alarm activation, condition raise/clear, and reported events via `.ActivatesAlarm(...)` | `CreatePumpSimulation` |
| `EventNotifier`, `HasNotifier`, and `HasEventSource` instance wiring | `PumpNodeManager.cs` + fluent alarm builders |
| Cross-namespace path resolution (Pump #1 in Pumps NS → Operational in Machinery NS → Measurements in Pumps NS, all in one unqualified browse path) | `src/Opc.Ua.Server/Fluent/BrowsePathResolver.cs` |
| Declarative `ConfigureDevicesFor` topology-element configuration adding an application-namespace Diagnostics functional group to every generated `PumpType` instance | `Program.cs` |
| In-memory historian wiring so NodeSet-declared historical access is genuinely serviceable for all analog measurements and historized supervision booleans | `PumpNodeManager.Configure.cs` `UseHistorian()` / `Historize()` |

## Architecture

```
PumpDeviceIntegrationServer/
├── Program.cs                          # AddOpcUa().AddServer(...).AddNodeManager<T>()
│                                       # + ConfigureDevicesFor diagnostics for every pump
├── PumpNodeManager.cs                  # Hand-written FluentNodeManagerBase
│                                       # + LoadPredefinedNodesAsync (multi-model)
│                                       # + CreateAddressSpaceAsync (builder setup)
├── PumpNodeManager.Configure.cs        # partial — fluent wiring + simulation tick
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

The sample intentionally does not add `GeneratesEvent` to pump instances.
OPC 10000-3 restricts that reference to ObjectType, VariableType, and Method
declarations; runtime delivery is provided by the notifier/event-source
hierarchy and `ReportEvent`.

## Extending the sample

- **Add a measurement**: open `PumpNodeManager.Configure.cs`, add a
  bound updater in `CreatePumpSimulation`, store it in
  `PumpSimulationState`, and publish its value from `Publish`.
- **Add an alarm**: create it from the typed `Events` builder in
  `CreatePumpSimulation` and wire the triggering boolean variable via
  `.ActivatesAlarm(...)`.
- **Add pumps**: pass `--pumps N` (or set `pumps=N`) to materialise N
  identical simulated `PumpType` instances. `PumpNodeManager` demonstrates
  the hand-written fluent style by creating every pump, wiring
  Identification, Measurements, Supervision, Maintenance, engineering
  units, alarms, history, and the simulation callbacks. `Program.cs` keeps
  the declarative DI style by wrapping each generated pump with
  `ctx.TopologyElement<PumpState>(...)` and adding the ad-hoc Diagnostics
  functional group through `ConfigureDevicesFor<PumpNodeManager>`.
  `CreatePumpAsync` registers each new instance with the shared simulation,
  so pumps created after startup join the same live tick.

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
