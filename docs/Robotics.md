# OPC UA Robotics

The Robotics libraries implement the OPC UA Robotics companion specification
([OPC 40010-1](https://reference.opcfoundation.org/Robotics/v102/docs/), version
1.02). Robotics builds on Industrial Automation
([OPC 40001-1](https://reference.opcfoundation.org/IA/v400/docs/)), which in turn
builds on Device Integration
([OPC 10000-100](https://reference.opcfoundation.org/DI/v104/docs/)); all three
models are source-generated from their released NodeSets and loaded in
dependency order.

## Packages

| Package | Purpose |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Robotics` | Source-generated Robotics and IA models plus `ArrayOf<T>`-based common contracts shared by client and server. |
| `OPCFoundation.NetStandard.Opc.Ua.Robotics.Server` | Stock node manager, model providers, hosting extensions, and validated fluent topology builders. |
| `OPCFoundation.NetStandard.Opc.Ua.Robotics.Client` | Continuation-safe, subtype-aware discovery of Robotics instances over the DI client, plus Robotics type classification. |

Generated model types stay in the specification namespaces `Opc.Ua.Robotics` and
`Opc.Ua.IA`; hand-written APIs compose the generated NodeStates, factories,
enums, and ObjectType clients instead of replacing or inheriting from them.

`RoboticsNamespaces` exposes the two model namespace URIs, and `RoboticsModel`
adds namespace-safe resolution and classification helpers over the generated
`ObjectTypeIds` / `ReferenceTypeIds` classes, which remain the source of truth.

## Minimal hosted server

`AddRobotics()` registers the stock `RoboticsNodeManager`, the built-in DI/IA/
Robotics model provider, and the Robotics configuration pipeline.
`ConfigureRobotics(...)` runs after the models are loaded and builds instances:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;

HostApplicationBuilder host = Host.CreateApplicationBuilder(args);

host.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "RobotCellServer";
        options.EndpointUrls.Add("opc.tcp://localhost:62830/RobotCellServer");
    })
    .AddRobotics(options =>
        options.InstanceNamespaceUri = "urn:example:robot-cell")
    .ConfigureRobotics(async context =>
    {
        await context.AddMotionDeviceSystemAsync("RobotCell", system =>
        {
            ISafetyStateBuilder safety = system.AddSafetyState("Safety");

            IMotionDeviceBuilder robot = system.AddMotionDevice("R1", device =>
                device.WithMotionDeviceCategory(
                    MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT));

            IDriveBuilder drive = robot.AddDrive(
                "Drive1",
                item => item.WithProductCode("DRV-1"));
            IPowerTrainBuilder train = robot.AddPowerTrain("PowerTrain1");
            IAxisBuilder axis = robot.AddAxis("A1", item => item
                .WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                .WithActualPosition(0));

            train.AddMotor("Motor1", motor => motor.IsDrivenBy(drive));
            train.Moves(axis);
            axis.Requires(train);

            IControllerBuilder controller = system.AddController("Controller1");
            controller.AddSoftware("Runtime", software =>
                software.WithIdentification(data =>
                {
                    data.Manufacturer = new LocalizedText("Vendor");
                    data.Model = new LocalizedText("Robot Runtime");
                    data.SoftwareRevision = "1.0";
                }));
            ITaskControlBuilder taskControl = controller.AddTaskControl("Main");

            controller.Controls(robot).UsesSafetyState(safety);
            taskControl.Controls(robot);
        }, context.CancellationToken);
    });

await host.Build().RunAsync();
```

`AddRobotics()` owns the DI namespace and therefore cannot be combined with
`AddOpcUaDi()`; both register the shared `DiAddressSpaceOwnership` marker and the
second call throws with the name of the conflicting extension.

## Hosting API

| Method | Builder | Purpose |
|---|---|---|
| `AddRobotics(Action<RoboticsServerOptions>?)` | `IOpcUaServerBuilder` | Registers the stock manager, built-in model provider, and configuration pipeline. |
| `AddRoboticsModel<TProvider>()` | `IOpcUaServerBuilder` | Adds an `IRoboticsModelProvider` that contributes further compiled models. |
| `ConfigureRobotics(Action<IRoboticsBuildContext>)` | `IOpcUaServerBuilder` | Synchronous configurator for the stock manager. |
| `ConfigureRobotics(Func<IRoboticsBuildContext, ValueTask>)` | `IOpcUaServerBuilder` | Asynchronous configurator. |
| `ConfigureRobotics(Func<IRoboticsBuildContext, CancellationToken, ValueTask>)` | `IOpcUaServerBuilder` | Asynchronous configurator with the hosting token. |
| `ConfigureRobotics<TConfigurator>()` | `IOpcUaServerBuilder` | Class-based, dependency-injected `IRoboticsConfigurator`. |
| `ConfigureRoboticsFor<TNodeManager>(…)` | `IOpcUaServerBuilder` | The same three lambda overloads, targeting an application-owned `DiNodeManager`. |
| `ConfigureRoboticsFor<TNodeManager, TConfigurator>()` | `IOpcUaServerBuilder` | Class-based configurator for an application-owned manager. |

Configurators registered for the same node manager run in registration order and
share one build context; the hosting pipeline calls `Seal()` once they have all
completed. Every overload is additive, so a composition root can mix lambdas and
classes.

`RoboticsServerOptions.InstanceNamespaceUri` selects the application-owned
namespace used for dynamically created instances. It defaults to
`urn:opcua-netstandard:robotics:instances` and is validated to be an absolute
URI that is not the OPC UA, DI, IA, or Robotics model namespace.

### Class-based code-behind

A class configurator is resolved from the container, so drivers, telemetry,
file-system providers, and authorization services can be injected:

```csharp
public sealed class RobotCell(IRobotBackend backend) : IRoboticsConfigurator
{
    public ValueTask ConfigureAsync(
        IRoboticsBuildContext context,
        CancellationToken cancellationToken)
    {
        return context.AddMotionDeviceSystemAsync("RobotCell", system =>
        {
            IMotionDeviceBuilder robot = system.AddMotionDevice("R1");
            robot.AddAxis("A1", axis => axis
                .WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                .BindActualPosition(backend.ReadJoint1Async));
            // …
        }, cancellationToken);
    }
}

// builder.ConfigureRobotics<RobotCell>();
```

### Model providers

`IRoboticsModelProvider` composes additional compiled models before instance
configuration, which keeps optional models (for example the draft OpenUSD
binding) out of `Opc.Ua.Robotics.Server`:

```csharp
public sealed class OpenUsdModelProvider : IRoboticsModelProvider
{
    public int Order => 0;

    public ArrayOf<string> NamespaceUris => new[] { Opc.Ua.OpenUsd.Namespaces.OpenUSD };

    public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        => nodes.AddOpcUaOpenUsd(context);
}

// builder.AddRoboticsModel<OpenUsdModelProvider>();
```

Providers run in ascending `Order`. The built-in DI/IA/Robotics provider uses
`int.MinValue`, so application providers run afterwards by default. A provider
that replaces the built-in core provider must advertise both the IA and Robotics
namespace URIs.

## Build context

`IRoboticsBuildContext` is created once per manager startup and shared by every
configurator of that manager:

| Member | Purpose |
|---|---|
| `Manager` | The active `DiNodeManager`. |
| `Context` | The active `ISystemContext`. |
| `Nodes` | The single fluent `INodeManagerBuilder` owned by the context. |
| `InstanceNamespaceIndex` | The resolved application-owned namespace index. |
| `DeviceSet` | The DI `DeviceSet` node that Robotics systems are added below. |
| `CancellationToken` | The hosting cancellation token. |
| `GetRequiredService<T>()` | Narrow service resolution for code-behind. |
| `Seal()` | Seals the fluent builder and starts configured simulations. Called by the hosting pipeline. |

`AddMotionDeviceSystemAsync(browseName, configure, cancellationToken)` is the
single entry point for topology. It configures the tree, validates it, verifies
that every instance NodeId is unique, adds the semantic references, and then
registers the completed generated state tree — rolling back the reservation if
any step fails. Reservations for NodeIds and root BrowseNames are held per node
manager, so concurrent configurators cannot collide.

## Topology builders

Every builder implements `IRoboticsNodeBuilder<TState>`, which exposes the
generated `State`, the owning `BuildContext`, a low-level
`Configure(Action<TState, ISystemContext>)` escape hatch, and `AsNode()` for the
fluent `INodeBuilder<TState>` view after registration.

| Builder | Adds |
|---|---|
| `IMotionDeviceSystemBuilder` | `AddController`, `AddMotionDevice`, `AddSafetyState`. |
| `IControllerBuilder` | `AddSoftware`, `AddTaskControl`, `AddSystemOperation`, `AddPrograms`, `WithCurrentUser`, `AddAuxiliaryComponent`, `AddDrive`, `Controls`, `UsesSafetyState`. |
| `ISystemOperationBuilder` | `WithInitialState`, `OnGetReady`, `OnStart`, `OnStop`, `OnStandDown`, `WithStopModes`, `OnTransition`. |
| `IProgramsBuilder` | `UseFileSystem(provider)` / `UseFileSystem<TProvider>()`, `WithOptions`. |
| `IRoboticsUserBuilder` | The mandatory Controller `CurrentUser` child. |
| `IRoboticsSoftwareBuilder` | Software identification (manufacturer, model, revision). |
| `IMotionDeviceBuilder` | `AddAxis`, `AddPowerTrain`, `AddDrive`, `AddAuxiliaryComponent`, `WithFlangeLoad`, `WithMotionDeviceCategory`, speed-override binding, `UsesTaskControl`. |
| `IAxisBuilder` | `WithMotionProfile`, `AsVirtual`, actual position/speed/acceleration, `WithAdditionalLoad`, `Requires`. |
| `IPowerTrainBuilder` | `AddMotor`, `AddGear`, `Moves`, `HasSlave`. |
| `IMotorBuilder` | Identification, motor temperature, brake-released, effective load rate, `IsDrivenBy`. |
| `IGearBuilder` | Identification, `WithGearRatio(int numerator, uint denominator)`, `WithPitch`. |
| `IDriveBuilder` / `IAuxiliaryComponentBuilder` | Product code, asset id, component name. |
| `ILoadBuilder` | `WithMass`, `WithCenterOfMass`, `WithInertia`. |
| `ISafetyStateBuilder` | `AddEmergencyStop`, `AddProtectiveStop`, emergency-stop / operational-mode / protective-stop values and bindings. |
| `ITaskControlBuilder` | `AddTaskModule`, `AddTaskControlOperation`, execution mode, task-program name and loaded flag, `Controls`. |
| `ITaskControlOperationBuilder` | `OnStart`, `OnStop`, `OnLoadByName`, `OnLoadByNodeId`, `OnUnloadByName`, `OnUnloadByNodeId`, `OnUnloadProgram`, `OnResetToProgramStart`, `WithMotionDevicesUnderControl`. |
| `ITaskModuleBuilder` | `WithName`, `WithVersion`, `WithIsReferenced`. |

Instances are always materialised through the generated
`ISystemContext.CreateInstanceOf<Type>` factories (for example
`CreateInstanceOfMotionDeviceSystemType`), so each instance carries the full
companion-type structure and its per-instance NodeIds, rather than only a
type-definition reference on a bare `BaseObjectState`.

### Binding live values

`WithXxx` seeds a static value; `BindXxx` attaches an asynchronous read (and,
where the variable is writable, a write) handler:

```csharp
robot.AddAxis("A1", axis => axis
    .WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
    .BindActualPosition(backend.ReadJoint1Async)
    .BindActualSpeed(backend.ReadJoint1SpeedAsync));

robot.BindSpeedOverride(
    backend.ReadSpeedOverrideAsync,
    backend.WriteSpeedOverrideAsync);
```

Read handlers are `Func<CancellationToken, ValueTask<DataValue>>`, so quality and
timestamps stay under application control. `SpeedOverride` writes are validated
to be finite and within 0–100 for every non-Bad status code.

### Semantic references

The builders add the Robotics reference types with the correct forward/inverse
semantics:

| Call | Reference |
|---|---|
| `controller.Controls(motionDevice)` | `Controls` (optional). |
| `controller.UsesSafetyState(safetyState)` | `HasSafetyStates`. |
| `taskControl.Controls(motionDevice)` | `Controls`. |
| `powerTrain.Moves(axis)` | `Moves`. |
| `axis.Requires(powerTrain)` | `Requires`. |
| `powerTrain.HasSlave(other)` | `HasSlave`, for non-1:1 kinematics. |
| `motor.IsDrivenBy(drive)` | `IsDrivenBy` (optional). |
| `builder.IsConnectedTo(other)` | `IsConnectedTo`, between any two Robotics nodes. |

`ITaskControlBuilder.Controls` and `IMotionDeviceBuilder.UsesTaskControl` add the
standard `Controls` relation. When the task control also has a
`TaskControlOperation` (via `AddTaskControlOperation`), the motion device's
`TaskControlReference` property is populated to point at that operation node.

### Standard operations and programs

`ControllerType.SystemOperation` and `TaskControlType.TaskControlOperation` are
optional facets carrying the two Part 16 state machines. The builders wire the
methods to application handlers and move the machine only when a handler
succeeds:

```csharp
IControllerBuilder controller = system.AddController("Controller1");

controller.WithCurrentUser(user => user.WithName("operator"));

controller.AddSystemOperation(operation => operation
    .WithInitialState(RoboticsOperationState.Idle)
    .WithStopModes([RoboticsStopMode.Normal, RoboticsStopMode.Emergency],
        RoboticsStopMode.Normal)
    .OnGetReady((context, ct) => backend.GetReadyAsync(ct))
    .OnStart((context, ct) => backend.StartAsync(ct))
    .OnStop((request, ct) => backend.StopAsync(request.StopMode, ct))
    .OnStandDown((context, ct) => backend.StandDownAsync(ct)));

controller.AddPrograms(programs => programs
    .UseFileSystem<IRobotProgramStore>()
    .WithOptions(o => o.AllowDelete = false));

ITaskControlBuilder task = controller.AddTaskControl("Main");
task.AddTaskControlOperation(operation => operation
    .OnLoadByName((name, ct) => backend.LoadProgramAsync(name, ct))
    .OnStart((context, ct) => backend.RunAsync(ct))
    .OnStop((request, ct) => backend.HaltAsync(request.StopMode, ct))
    .WithMotionDevicesUnderControl([robot.State.NodeId]));
```

Causes follow the spec transitions: `GetReady` Idle→Ready, `Start`
Ready→Executing, `Stop` Executing→Ready, `StandDown` and the unload verbs
Ready→Idle, the load verbs Idle→Ready. A cause that is illegal from the current
state returns `BadInvalidState` without invoking the handler.
`LastTransition`, `LastTransitionReason`, `PossibleStopModes`, and
`ConfiguredDefaultStopMode` are maintained automatically.

`AddPrograms` binds the optional `Programs` `FileDirectoryType` to the stack's
existing `IFileSystemProvider` model through the shared
`Opc.Ua.Server.FileSystem.IFileDirectoryBinder`, so any node manager — not just
the dedicated file-system manager — can serve a Part 5 directory. Binding runs
after the Robotics tree is registered, and is disposed if the build rolls back.

### Validation

Registration fails with a `ServiceResultException` (`BadConfigurationError`)
that reports every problem at once when the configured topology violates the
companion specification:

* a motion-device system without at least one controller, one motion device, and
  one safety state;
* a controller without at least one `SoftwareType` and one `TaskControlType`
  instance — both are mandatory placeholders;
* a controller without its mandatory `CurrentUser` child;
* a motion device without at least one axis and one power train;
* a non-virtual axis without a `Requires` link — mark an axis with `AsVirtual()`
  when it has no power train;
* a power train without a motor, or a gear with a zero ratio denominator;
* an emergency stop, protective stop, or task module without a `Name`.

Registration additionally rejects a descendant whose NodeId is null, outside the
instance namespace, duplicated within the tree, or already indexed by the node
manager. Duplicate BrowseNames raise `BadBrowseNameDuplicated`, both for
siblings under one parent and for a `MotionDeviceSystem` BrowseName already
reserved on the same node manager.

Because validation runs before registration, a failed build leaves no partial
subtree in the address space.

Units follow the specification: a gear ratio numerator is a signed `int` and the
denominator is a `uint`; `WithPitch` is millimetres of linear travel per
output-side revolution and is a `BaseDataVariableType` without EngineeringUnits.

## Custom node managers and non-DI hosting

The stock `RoboticsNodeManager` owns the DI address space and exactly one
application instance namespace. Use it — through `AddRobotics()` — whenever
Robotics is the only companion model the server adds. Reach for a custom node
manager instead when any of the following applies:

* the server composes **additional models into the same manager** (for example
  the OpenUSD binding, or RSL/GPOS positioning, alongside Robotics), so a single
  `LoadPredefinedNodesAsync` must return all of them in dependency order;
* the server needs its own `INodeIdFactory` scheme (deterministic string
  NodeIds, an external asset registry, a sharded allocator);
* the server already owns a `DiNodeManager` for its device model and Robotics is
  one facet of it.

An application that already owns a `DiNodeManager` keeps it and still gets the
validated fluent builders:

```csharp
builder.ConfigureRoboticsFor<MyDeviceNodeManager>(async context =>
{
    await context.AddMotionDeviceSystemAsync("RobotCell", system => { /* … */ },
        context.CancellationToken);
});
```

Outside the hosting pipeline entirely, load the models and create the context
directly:

```csharp
// Inside a DiNodeManager: load DI + IA + Robotics in dependency order.
predefinedNodes.AddRoboticsTypeSystem(context);

// After the models are loaded:
IRoboticsBuildContext buildContext =
    manager.CreateRoboticsBuildContext(new RoboticsServerOptions
    {
        InstanceNamespaceUri = "urn:example:robot-cell"
    });
```

`CreateRoboticsBuildContext` validates the `DeviceSet`, the loaded Robotics
model, and the configured instance namespace before returning. The manager's
`Context.NodeIdFactory` must implement `IRoboticsNodeIdFactory`; the allocator
must be thread-safe, must reserve unique NodeIds for unregistered nodes, and must
allocate Robotics instances in the configured instance namespace.

[`MinimalRobotServer`](../samples/MinimalRobotServer) is the worked example of
the custom-manager route: it composes Robotics, IA, DI, the draft OpenUSD
binding, and RSL/GPOS in one `DiNodeManager` subclass.

## Vendor extensions

The Robotics packages are a base for robot vendors, not a closed set. A vendor
ships their own package that references `Opc.Ua.Robotics` (and
`Opc.Ua.Robotics.Server` when it needs fluent accessors), adds its own
NodeSet2.xml declaring ObjectTypes **derived from** the companion types (for
example an `AcmeMotionDeviceType` under `MotionDeviceType`), and runs the model
source generator with default options:

```xml
<AdditionalFiles Include="Model\Acme.Robots.NodeSet2.xml">
  <ModelSourceGeneratorPrefix>Acme.Robots</ModelSourceGeneratorPrefix>
</AdditionalFiles>
```

The `ModelDependencyAttribute` emitted by `Opc.Ua.Robotics` makes the generator
resolve the base state types from that assembly instead of re-emitting them, so
`AcmeMotionDeviceState` derives from the shipped `MotionDeviceState`. The
`ModelFluentAccessorProviderAttribute` on `Opc.Ua.Robotics.Server` does the same
for the fluent accessors: the vendor assembly emits accessors only for its new
types and inherits the Robotics ones. See
[Model Dependencies](ModelDependencies.md).

On the client side every discovery and classification call is subtype aware, so
vendor specialisations are found and labelled without any client change.

## Layering

The three packages follow the same layering as the Device Integration and
Positioning trios:

| Package | References | Generation |
|---|---|---|
| `Opc.Ua.Robotics` | `Opc.Ua.Di` | Robotics + IA models with `ModelSourceGeneratorOmitFluentApi=true` |
| `Opc.Ua.Robotics.Server` | `Opc.Ua.Robotics`, `Opc.Ua.Di.Server`, `Opc.Ua.Server` | fluent accessors only, with `ModelSourceGeneratorFluentAccessorsOnly=true` |
| `Opc.Ua.Robotics.Client` | `Opc.Ua.Robotics`, `Opc.Ua.Di.Client`, `Opc.Ua.Client` | — |

The `Opc.Ua.Robotics.Operations` contracts ship in the model package so a client
and a server can share them without either taking a dependency on the other.

The model package stays free of any server dependency because the generated
fluent-accessor method bodies call into the `Opc.Ua.Server` fluent builders.
Emitting them from the model package would force `Opc.Ua.Robotics` — and
therefore `Opc.Ua.Robotics.Client` — to reference `Opc.Ua.Server`. The
accessors are therefore generated once, in the server package, against the state
types the model package already ships.

## Common contracts

`Opc.Ua.Robotics` ships immutable `ArrayOf<T>`-based records that project a robot
cell without a client or server dependency:
`RoboticsTopologySnapshot`, `MotionDeviceSystemSnapshot`, `ControllerSnapshot`,
`MotionDeviceSnapshot`, `AxisSnapshot`, `AxisStateSnapshot`, `AxisLimits`,
`AxisEngineeringOptions`, `LoadSnapshot`, `PowerTrainSnapshot`, `MotorSnapshot`,
`GearSnapshot`, `DriveSnapshot`, `SafetyStateSnapshot`,
`SafetyFunctionSnapshot`, `TaskControlSnapshot`, `TaskModuleSnapshot`,
`RoboticsComponentIdentification`, and `RoboticsEngineeringValue`.

Containment identifiers stay on the owning instance, while
`RoboticsRelationshipSnapshot` (built from `RoboticsRelationshipEntry` values) is
the authoritative projection of the semantic Robotics references listed above.

## Client

`Opc.Ua.Robotics.Client` extends the Device Integration client: Robotics types
derive from IA, which derives from OPC 10000-100, so `RoboticsClient` composes
`DiTopologyClient` rather than reimplementing device navigation. It lets a
generic client — a viewer, an OpenUSD connector, or a fleet manager — find,
label, and drive robot cells without hard-coded NodeIds.

### Registration

```csharp
services.AddOpcUa()
    .AddClient(options => { /* endpoint and application options */ })
    .AddRoboticsClient();
```

`AddRoboticsClient()` also calls `AddOpcUaDi()`, and registers a
`Func<CancellationToken, Task<RoboticsClient>>` factory bound to the managed
session. The direct constructor
`new RoboticsClient(session, telemetry)` remains available as the non-DI
fallback.

### API

| Member | Purpose |
|---|---|
| `RoboticsClient(ISession, ITelemetryContext)` | Creates the client over a connected session. |
| `session.Robotics(telemetry)` | Extension shorthand for the constructor. |
| `Session` / `Telemetry` | The session and telemetry context the client was created with. |
| `Topology` | The `DiTopologyClient` this client extends — use it to walk the DI device topology (`DeviceSetId`, `NetworkSetId`, `DeviceTopologyId`). |
| `DiscoverMotionDeviceSystemsAsync(ct)` | Discovers every MotionDeviceSystem below the DI `DeviceSet`. |
| `DiscoverMotionDeviceSystemsAsync(root, ct)` | Same, below an explicit root (for example the Objects folder). |
| `EnumerateMotionDeviceSystemsAsync(ct)` | Streams systems as they are discovered. |
| `DiscoverMotionDevicesAsync(root, ct)` | Discovers MotionDevices, typically below a system's `MotionDevices` folder. |
| `DiscoverControllersAsync(root, ct)` | Discovers Controllers, typically below a system's `Controllers` folder. |
| `DiscoverAxesAsync(root, ct)` | Discovers Axes, typically below a motion device's `Axes` folder. |
| `ReadSystemAsync(system, ct)` | Reads a complete `RoboticsTopologySnapshot`, including the semantic `RoboticsRelationshipSnapshot`. |
| `ReadControllerAsync` / `ReadMotionDeviceAsync` / `ReadAxisAsync` / `ReadSafetyStateAsync` / `ReadTaskControlAsync` | Per-node typed snapshots. |
| `SystemOperation(controller)` | The standard SystemOperation state-machine client. |
| `TaskControl(taskControl)` | The standard TaskControl state-machine client. |
| `ProgramsAsync(controller, ct)` | A `FileSystemClient` rooted at the Controller `Programs` directory. |
| `OperationsAsync(motionDevice, ct)` | The non-normative operation convention client. |
| `ObserveAxisAsync` / `ObserveSafetyAsync` | Streaming telemetry over the subscription API. |
| `GetRoboticsTypeNameAsync(typeDefinition, ct)` | Classifies a TypeDefinition against the server's type hierarchy, so vendor subtypes resolve to their closest standard Robotics type. Returns `null` when the node is not a Robotics type. |
| `RoboticsClient.DiscoverMotionDeviceSystemsAsync(session, root, ct)` (static) | Session-only discovery for callers that do not hold a client instance. |
| `RoboticsClient.TryGetRoboticsTypeName(typeDefinition, namespaceUris, out name)` (static) | Offline exact-match classification with no server round-trip. |

Every discovery method returns `ArrayOf<NodeId>` and uses `ManagedBrowseAsync`,
so a server that caps references per node cannot silently truncate the result.
When the server does not expose the Robotics namespace, discovery returns an
empty `ArrayOf<NodeId>` and classification returns `null` / `false` instead of
throwing.

### Walking a cell

```csharp
using Opc.Ua.Robotics.Client;

var robots = new RoboticsClient(session, telemetry);

foreach (NodeId system in await robots.DiscoverMotionDeviceSystemsAsync(ct))
{
    foreach (NodeId device in await robots.DiscoverMotionDevicesAsync(system, ct))
    {
        foreach (NodeId axis in await robots.DiscoverAxesAsync(device, ct))
        {
            // Read ParameterSet/ActualPosition, subscribe, drive a twin, …
        }
    }
}
```

### Classifying a discovered node

```csharp
// Exact match, no server round-trip — use when you already resolved the
// namespace table and only care about the standard types.
if (RoboticsClient.TryGetRoboticsTypeName(
        typeDefinition, session.NamespaceUris, out string? typeName))
{
    // typeName is MotionDeviceSystem, MotionDevice, Axis, or Controller.
}

// Subtype aware — an AcmeMotionDeviceType instance is reported as MotionDevice.
string? kind = await robots.GetRoboticsTypeNameAsync(typeDefinition, ct);
```

### Invoking robot operations

The Robotics methods themselves (task-control and system state-machine
operations) are exposed through the **source-generated ObjectType clients** in
`Opc.Ua.Robotics`, which give a typed result carrying a `ServiceResult` plus the
declared outputs — for example `LoadByName(string name)` returning a `Status`
Int32. The generated state-machine identifiers live in
`SystemOperationStateMachineTypeIds` and `TaskControlStateMachineTypeIds`.

> A higher-level verb façade over these generated proxies is documented under
> [Operation conventions](#operation-conventions) below. The standard state
> machines are reached through `SystemOperation(...)` and `TaskControl(...)`.

### Standard operations

`RoboticsClient` exposes the two OPC 40010 state machines directly:

```csharp
SystemOperationClient system = robots.SystemOperation(controllerNodeId);
await system.GetReadyAsync(ct);       // Idle    -> Ready
await system.StartAsync(ct);          // Ready   -> Executing
await system.StopAsync(RoboticsStopMode.Normal, ct);   // Executing -> Ready
await system.StandDownAsync(ct);      // Ready   -> Idle

RoboticsOperationState state = await system.ReadStateAsync(ct);
await foreach (RoboticsOperationState s in system.ObserveStateAsync(ct))
{
    // Idle / Ready / Executing
}
```

```csharp
TaskControlClient task = robots.TaskControl(taskControlNodeId);
await task.LoadByNameAsync("weld-seam-3", ct);
await task.StartAsync(ct);
await task.StopAsync(RoboticsStopMode.Normal, ct);
await task.ResetToProgramStartAsync(ct);
await task.UnloadProgramAsync(ct);
```

A verb that is illegal from the current state is rejected with `BadInvalidState`
before the server-side handler runs, and a handler that returns a bad
`ServiceResult` leaves the state machine where it was.

### Programs

When the Controller exposes the optional `Programs` directory, it is a standard
Part 5 `FileDirectoryType`, so it is read and written with the ordinary file
services:

```csharp
FileSystemClient programs = await robots.ProgramsAsync(controllerNodeId, ct);
await foreach (var entry in programs.EnumerateAsync("/", ct))
{
    // program files exposed by the controller
}
```

### Observing telemetry

```csharp
await foreach (AxisStateSnapshot axis in robots.ObserveAxisAsync(axisNodeId, ct))
{
    // ActualPosition / ActualSpeed / ActualAcceleration as they change
}
```

## Operation conventions

OPC 40010 defines **no motion verbs**. Its only actuation surface is the two
state machines above. Applications that need `MoveTo` / `Grasp` / `Release`
style verbs — the shape the
[URML](https://github.com/URML-MARS/URML) robot-intent language expects from an
OPC UA substrate — opt into the **non-normative** convention layer in the
`Opc.Ua.Robotics.Operations` namespace.

These are **not** part of OPC 40010 and are never created in the Robotics
namespace. `AddOperations` requires an application-owned namespace and rejects
the OPC UA, DI, IA, and Robotics namespaces with `BadConfigurationError`.

```csharp
robot.AddOperations("Operations", applicationNamespaceIndex, ops => ops
    .OnMoveTo((request, ct) => backend.MoveToAsync(request, ct))
    .OnMoveJ((request, ct) => backend.MoveJointsAsync(request, ct))
    .OnGrasp((request, ct) => gripper.GraspAsync(request, ct))
    .OnRelease((request, ct) => gripper.ReleaseAsync(request, ct))
    .WithUserExecutable(session => session.IsOperator));
```

Only the verbs whose handler was registered are materialised, each with full
`InputArguments` / `OutputArguments` metadata so a generic client can
introspect them. Anything outside the industrial subset uses the generic
extension point:

```csharp
ops.AddOperation<ScanRequest, ScanResult>("Scan", (request, ct) => …);
```

The client side resolves the methods by BrowseName, never by hard-coded NodeId:

```csharp
RoboticsOperationsClient ops = await robots.OperationsAsync(motionDeviceNodeId, ct);
await ops.MoveToAsync(new MoveToRequest { … }, ct);
await ops.InvokeAsync<ScanRequest, ScanResult>("Scan", request, ct);
```

`CallProgram` exists for completeness, but the standard Programs plus
TaskControl route is preferred and is what the SDK documents first.

## Sample

[`MinimalRobotServer`](../samples/MinimalRobotServer) exposes a
`MotionDeviceSystem` with two independently mobile 6-axis robots, a cell
emergency stop, a speed-override command, and a runtime-mounted gripper. It
combines Robotics with OPC 10000-210 RSL frames and OPC 10000-211 GPOS locations
and binds the whole cell to an OpenUSD stage, so a generic connector renders it
live with no robot-specific code.

## URML primitive mapping

The [URML](https://github.com/URML-MARS/URML) robot-intent language names OPC UA
Robotics as its canonical non-ROS substrate, and its Layer-2 v0.1.0 vocabulary
of 27 primitives is the motivating use case for
[issue #3827](https://github.com/OPCFoundation/UA-.NETStandard/issues/3827).
Every primitive has a route through this SDK:

| URML primitive | Route |
|---|---|
| `move_to`, `grasp`, `release` | convention verbs `MoveTo`, `Grasp`, `Release` |
| `pick_from`, `place_at`, `swap_tool` (industrial profile) | convention verbs `PickFrom`, `PlaceAt`, `SwapTool` |
| `set_output` | convention verb `SetOutput` |
| `call_program` | **normative**: `TaskControl(...).LoadByNameAsync` + `StartAsync`, with programs listed through `ProgramsAsync(controller)`. `CallProgram` is the fallback. |
| `measure` | `ReadAxisAsync` and the other snapshot readers |
| `wait_for` | `ObserveAxisAsync` / `ObserveSafetyAsync` / `ObserveStateAsync` |
| `report` | plain session write |
| `wait` | client-side delay; no server surface needed |
| `bimanual` | address the specific `MotionDevice`; `DiscoverMotionDevicesAsync` enumerates them |
| `dock`, `hover`, `detect`, `scan`, `capture`, `speak`, `listen`, `take_off`, `land`, `return_to_home`, `plan_path`, `follow_trajectory`, `drive`, `turn` | generic `AddOperation<TRequest, TResponse>` / `InvokeAsync<TRequest, TResponse>` |

URML's Layer-1 capability manifest is derived live from
`ReadSystemAsync`, rather than hand-declared: motion devices give arm count,
`AxisSnapshot.Limits` and `AxisEngineeringOptions` give joint limits and
velocities, `MotionDeviceCategory` gives mobility, `SafetyStateSnapshot` gives
the safety envelope, `ProgramsAsync` gives the declared programs, and
`RoboticsRelationshipSnapshot` gives the kinematic structure. Because verbs are
resolved by BrowseName under the operations object, no per-deployment NodeId
mapping file is required.

## See also

* [Device Integration (DI) developer guide](DeviceIntegration.md) — the base
  model, fluent device builders, and the companion-spec packaging pattern.
* [Relative Spatial Location and Global Positioning](Positioning.md) — the RSL
  and GPOS models used by the robot sample.
* [OpenUSD binding](OpenUsd.md) — rendering a robot cell as a live USD stage.
* [Dependency Injection](DependencyInjection.md) — the shared `AddOpcUa()`
  hosting surface.
* [Source Generated NodeManagers](SourceGeneratedNodeManagers.md) — the fluent
  `INodeManagerBuilder` and generated factories the builders compose.
