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
`Pump #2`, is organized alongside it by the same `DeviceSet` — it
demonstrates the DI hosting `ConfigureDevicesFor` flow and automatically
joins the same live simulation. Both pumps publish monitored data changes
every 250 ms, with deterministic phase offsets so their values do not move
in lockstep.

Subscribe to the `EventNotifier` attribute on either pump to receive alarm
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

## The OpenUSD twin

The server also publishes an OpenUSD representation of the pump line
(the draft OPC UA — OpenUSD Bindings companion model, see
[`docs/OpenUsd.md`](../../docs/OpenUsd.md)) and serves its USD layers as
embedded assets, so a connector can render the twin with no external
asset resolver.

`Assets/Plant.usda` models **P101** as a real machine: a horizontal
long-coupled end-suction centrifugal pump built to **EN 733** (formerly
DIN 24255), size **65-200**, following the published dimensions of the
Grundfos NK 65-200 / KSB Etanorm 65-200 family and driven by an
**IEC 160M** motor on a fabricated baseplate.

| Item | Value |
| --- | --- |
| Baseplate | 1.80 × 0.46 × 0.12 m |
| Shaft centreline above baseplate | 0.160 m (the IEC 160M frame number *is* the shaft height) |
| Volute casing | 0.355 m outer diameter × 0.110 m wide |
| Suction flange | DN80 axial, OD 0.200 m (EN 1092-2 PN16) |
| Discharge flange | DN65 vertical, OD 0.185 m (EN 1092-2 PN16) |
| Impeller | 0.198 m, six backward-curved vanes |
| Motor frame | 0.254 m outer diameter × 0.615 m long |

Livery is KSB signal blue (RAL 5005) for the wetted castings and
RAL 7035 light grey for the motor. `pump.usda` is the same machine at a
lower level of detail, referenced once per aggregated line pump;
`remote-pump.usda` wears an OEM green livery so the pump federated from
the *remote* server is obvious at a glance.

Two departures from a real pump are deliberate, so the twin can be
*seen*: the suction pipe is drawn as a stub leaving the casing eye open
(a cutaway, as trade-show display pumps are presented) and the coupling
guard is a cage rather than a solid barrel. Both let you watch the shaft
turn.

### Live bindings

| Source | USD target | Effect |
| --- | --- | --- |
| `ShaftAngle` | `…/P101/Impeller.xformOp:rotateZ` | turns the shaft, impeller and coupling |
| `BearingTemperature` | `…/P101/Body/Mat/Surface.inputs:diffuseColor` | casing colour, blue (cool) → red (hot) |
| `DifferentialPressure` | `…/StatusLight/Mat/Surface.inputs:emissiveColor` | lamp glow tracks discharge pressure |
| supervision alarm | `…/P101/StatusLight.visibility` | shows the alarm halo |

`MassFlow` is a *rate*, so binding it straight to a rotation op pins the
shaft at a fraction of a degree and the pump looks dead. The simulation
integrates the running speed into a `ShaftAngle` instead, and the binding
scales it down to a legible ~45°/s — a real 2900 rpm shaft would alias
into a stroboscopic blur at any practical sampling rate. Speed follows
flow, so the impeller visibly slows and picks up with the duty point.

The beacon mast, housing and lamp are permanently mounted; only the alarm
halo is gated by `visibility`, so a cleared alarm still leaves a lamp
whose glow tracks discharge pressure.

A real pump shaft is horizontal, but the binding contract fixes the
driven operation as `xformOp:rotateZ`. `Impeller` therefore carries a
static `xformOp:rotateY = 90` *ahead of* `xformOp:rotateZ` in
`xformOpOrder`, which lays its local Z along the world shaft axis. The
impeller and the coupling both hang off that one rotating prim, so they
turn together — as they do on the real machine.

Because the render targets expect degrees Celsius and bar while OPC
40223 publishes Kelvin and Pascal, the two colour bindings declare the
conversion themselves (`offset: -273.15` and `scale: 1e-5`); §5.8
applies `Scale` then `Offset`.

### Viewing it

Run the server, then point the connector at it with `--view`:

```pwsh
Opc.Ua.OpenUsd.Connector --server opc.tcp://localhost:62542/PumpDeviceIntegrationServer `
                         --insecure --view --fetch-assets .\stage-cache
```

The connector fetches the server-delivered layers, composes a
self-contained stage and streams the live OPC UA values into
`live.usda` and the viewport. See
[`tools/Opc.Ua.OpenUsd.Connector`](../../tools/Opc.Ua.OpenUsd.Connector).

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
| Push-style monitored value updates via `Bind(out IValueUpdater<T>)` | `CreatePumpSimulation` |
| One 250 ms simulation tick for all phase-shifted pumps | `Configure` → `AdvanceSimulation` |
| Limit alarm with thresholds and acknowledge handler via `CreateLimitAlarm(...).WithLimits(...)` | `CreatePumpSimulation` |
| Boolean supervision → reported alarm condition events via `.ActivatesAlarm(...)` | `CreatePumpSimulation` |
| `EventNotifier`, `HasNotifier`, and `HasEventSource` instance wiring | `PumpNodeManager.cs` + fluent alarm builders |
| Cross-namespace path resolution (Pump #1 in Pumps NS → Operational in Machinery NS → Measurements in Pumps NS, all in one unqualified browse path) | `src/Opc.Ua.Server/Fluent/BrowsePathResolver.cs` |
| Generated `PumpType` instance + typed Identification group configuration | `Program.cs` (`Pump #2`) |

## Architecture

```
PumpDeviceIntegrationServer/
├── Program.cs                          # AddOpcUa().AddServer(...).AddNodeManager<T>()
│                                       # + ConfigureDevicesFor declarative Pump #2
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
- **Add a second pump**: two patterns are demonstrated in the sample.
  - **Hand-rolled** (used for `Pump #1`): in `PumpNodeManager.CreatePumpAsync`, create the generated `PumpState`, attach it to the DI `DeviceSet` with `Organizes`, and register it. The fluent `Configure.cs` then wires its measurements, alarms, and simulation by browse path.
  - **DI declarative** (used for `Pump #2`): in `Program.cs`, call `PumpNodeManager.CreatePumpAsync(...)` from a `ConfigureDevicesFor<PumpNodeManager>` block, wrap the generated `PumpState` with `ctx.TopologyElement<PumpState>(...)`, then configure the mandatory `Identification` group. `CreatePumpAsync` also registers the new instance with the shared simulation.

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
