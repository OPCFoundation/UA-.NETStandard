# OPC UA Robotics

The Robotics libraries implement the OPC UA Robotics companion specification
([OPC 40010-1](https://reference.opcfoundation.org/Robotics/v102/docs/), version
1.02). Robotics builds on Industrial Automation
([OPC 10000-200](https://reference.opcfoundation.org/IA/v400/docs/)), which in turn
builds on Device Integration
([OPC 10000-100](https://reference.opcfoundation.org/DI/v104/docs/)); all three
models are source-generated from their released NodeSets and loaded in
dependency order.

> **Status: draft companion model.** The namespace `http://opcfoundation.org/UA/RobotIntent/` and every
> NodeId in it are **provisional**. This implements the working-group draft
> [*OPC UA — Robot Intent*](https://github.com/marcschier/opcua-drafts/blob/main/metaverse-specs/robot-intent/OPC-UA-Robot-Intent.md);
> nothing here is official or endorsed by the OPC Foundation. Do not deploy it on a production robot
> and expect the identifiers to survive.

OPC 40010 describes a robot in detail — its motion device system, its axes, its power trains, its
controller, its safety states — and defines **no motion verbs at all**. Its whole actuation surface is
`Start`, `Stop` and loading a named program. A conformant client can discover everything about a
robot's construction and cannot ask it to move anywhere.

Robot Intent supplies the verbs, and only the verbs, so the two compose rather than compete:

* [`Opc.Ua.Robotics`](../src/Opc.Ua.Robotics) carries the source-generated model, the executor
  contracts, and the Annex C pose maths.
* [`Opc.Ua.Robotics.Server`](../src/Opc.Ua.Robotics.Server) carries the execution engine, the address
  space builders and the hosting integration.
* [`Opc.Ua.Robotics.Client`](../src/Opc.Ua.Robotics.Client) carries discovery, the awaitable operation
  handle, command authority, missions and the fluent intent builders.

The NodeSet declares exactly one `RequiredModel` — the base OPC UA namespace — so a server can adopt
Robot Intent without pulling in OPC 40010, OPC 10000-100 DI, or anything else.

## Packages

| Package | Purpose |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Robotics` | Source-generated OPC 40010/IA and draft Robot Intent models, generated NodeIds/DataTypes/ObjectType clients, `ArrayOf<T>`-based common contracts shared by client and server, the `IIntentExecutor` contract, `IntentExecution`, `IIntentProgress`, `IntentOutcome`, `PoseMath` and `FrameTree`. |
| `OPCFoundation.NetStandard.Opc.Ua.Robotics.Server` | Stock Robotics node manager, Robot Intent node manager, model providers, hosting extensions (`AddRobotics`, `AddRobotIntent`, `ConfigureRobotics`, `ConfigureRobotIntent`), validated fluent topology builders, `IntentControllerHost`, safety binding, real-time channel declarations and facet calculation. |
| `OPCFoundation.NetStandard.Opc.Ua.Robotics.Client` | Continuation-safe, subtype-aware discovery of Robotics instances over the DI client, Robotics type classification, Robot Intent discovery, the awaitable operation handle, command authority, real-time-channel leases, missions and `RobotIntentBuilder`. |

Generated OPC 40010 model types stay in the specification namespaces `Opc.Ua.Robotics` and
`Opc.Ua.IA`; hand-written APIs compose the generated NodeStates, factories,
enums, and ObjectType clients instead of replacing or inheriting from them.

The generated `Opc.Ua.Robotics.Namespaces` and `Opc.Ua.IA.Namespaces` classes
expose the model namespace URIs, and `RoboticsModel` adds namespace-safe
resolution and classification helpers over the generated `ObjectTypeIds` /
`ReferenceTypeIds` classes, which remain the source of truth.

The generated Robot Intent types live in `Opc.Ua.RobotIntent`. The hand-written server APIs live in
`Opc.Ua.Robotics.Server` and `Opc.Ua.Robotics.Server.Builders`; the hand-written client APIs live in
`Opc.Ua.Robotics.Client.Intent`.

The Robot Intent client is also exposed as an MCP tool package for agent hosts:
`OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics` registers discovery, monitoring, direct-control and
mission tools over an already connected OPC UA Session. The tools are an adapter over
`RobotIntentClient` and `RobotIntentControllerClient`; they do not implement an independent robot
state machine.

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

[`MinimalRobotServer`](../samples/Robotics/MinimalRobotServer) is the worked example of
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

OPC 40010 defines **no motion verbs**. Its actuation surface is the two state
machines above, plus the standard `Programs` directory when a controller
exposes one. The former non-normative `AddOperations` / `AddOperation` builder
API has been removed; use the Robotics state machines for device control and
the Robot Intent model for task-level verbs such as move, grasp, release,
process, mission, retry and real-time-channel flows.

For application-specific verbs that do not fit the standardized Robot Intent
types, expose an application-owned information model and call it through normal
OPC UA methods, or encode the behaviour as a program selected through
TaskControl. Do not create custom Robotics children in the OPC UA, DI, IA, or
Robotics namespaces.

## Sample

[`MinimalRobotServer`](../samples/Robotics/MinimalRobotServer) exposes a
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
| `dock`, `hover`, `detect`, `scan`, `capture`, `speak`, `listen`, `take_off`, `land`, `return_to_home`, `plan_path`, `follow_trajectory`, `drive`, `turn` | application-owned information model methods, or programs selected through TaskControl |

URML's Layer-1 capability manifest is derived live from
`ReadSystemAsync`, rather than hand-declared: motion devices give arm count,
`AxisSnapshot.Limits` and `AxisEngineeringOptions` give joint limits and
velocities, `MotionDeviceCategory` gives mobility, `SafetyStateSnapshot` gives
the safety envelope, `ProgramsAsync` gives the declared programs, and
`RoboticsRelationshipSnapshot` gives the kinematic structure. Robot Intent
types and application-owned methods are resolved from the live address space, so
no per-deployment NodeId mapping file is required.

## Robot Intent

### Hosting a controller

The hosted path is the normal path for an application server. `AddRobotIntent` registers the standalone
node manager, the draft model provider and a rejecting executor. `AddRobotIntentExecutor<T>` replaces
that rejecting executor with the application implementation, and `ConfigureRobotIntent` runs after the
model and the `Server/RobotIntent/Controllers` root exist:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;

HostApplicationBuilder host = Host.CreateApplicationBuilder(args);

host.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "IntentRobot";
        options.EndpointUrls.Add("opc.tcp://localhost:4840/IntentRobot");
    })
    .AddRobotIntent(options =>
    {
        options.InstanceNamespaceUri = "urn:example:intent-robot";
    })
    .AddRobotIntentExecutor<MyIntentExecutor>()
    .ConfigureRobotIntent(async (context, ct) =>
    {
        await context.AddIntentControllerAsync(
            "Arm1",
            ConfigureArmController,
            ct);
    });

await host.Build().RunAsync();
```

The direct-construction fallback is the same address space and the same builder, just without the
Generic Host pipeline. A server that constructs node managers itself can register
`RobotIntentNodeManagerFactory` directly, or construct `RobotIntentNodeManager` with explicit
`IRobotIntentModelProvider` and `RobotIntentServerOptions` services. Once the manager has created its
address space, call `CreateRobotIntentBuildContext` and use the same `AddIntentControllerAsync`
extension:

```csharp
using Opc.Ua;
using Opc.Ua.Robotics.Server;

var factory = new RobotIntentNodeManagerFactory(
    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() }.ToArrayOf(),
    new RobotIntentServerOptions
    {
        InstanceNamespaceUri = "urn:example:intent-robot"
    });

// Register the factory with the server's node-manager registration mechanism.
// If you already have a RobotIntentNodeManager instance after address-space creation:
IRobotIntentBuildContext context = manager.CreateRobotIntentBuildContext(ct);
await context.AddIntentControllerAsync("Arm1", ConfigureArmController, ct);
```

That standalone path is intentionally useful: because the NodeSet requires only the base OPC UA
namespace, a machine can expose Robot Intent without OPC 40010, DI or the Robotics topology model. A
server that already owns an OPC 40010 node manager can instead use `ConfigureRobotIntentFor<TNodeManager>`
and then link a `MotionDeviceSystem` to the intent controller with `HasIntentController`. That inverse
reference is the structural evidence used to derive **RI-Interop-40010**.

### Declaring a robot

The controller builder makes the model declaration and the host declaration one thing. What the server
publishes under `Capabilities` is the contract `IntentControllerHost` enforces at submission time.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Server.Builders;

static void ConfigureArmController(IIntentControllerBuilder controller)
{
    controller
        .WithOperationalMode(OperationalModeEnum.AutomaticExternal)
        .WithReady(true)
        .WithMaxQueueDepth(8)
        .Accepts<JointMoveIntentDataType>()
        .Accepts<LinearMoveIntentDataType>()
        .Accepts<CircularMoveIntentDataType>()
        .Accepts<GraspIntentDataType>(cancelSupported: false)
        .Accepts<ReleaseIntentDataType>()
        .Accepts<PickIntentDataType>(cancelSupported: false)
        .Accepts<PlaceIntentDataType>()
        .Accepts<ToolChangeIntentDataType>(cancelSupported: false)
        .Accepts<SetOutputIntentDataType>()
        .Accepts<CallProgramIntentDataType>()
        .Accepts<WaitIntentDataType>()
        .WithSafetyState(new MySafetySource());

    IIntentFrameBuilder world = controller.AddFrame(
        "World",
        "world",
        FrameRoleEnum.World,
        Pose("world", 0.0, 0.0, 0.0));
    IIntentFrameBuilder @base = controller.AddFrame(
        "Base",
        "robot-base",
        FrameRoleEnum.Base,
        Pose("world", 0.0, 0.0, 0.82),
        frame => frame.WithParent(world));
    IIntentFrameBuilder flange = controller.AddFrame(
        "Flange",
        "robot-flange",
        FrameRoleEnum.MechanicalInterface,
        Pose("robot-base", 0.0, 0.0, 0.18),
        frame => frame.WithParent(@base));
    IIntentFrameBuilder tcp = controller.AddFrame(
        "GripperTcp",
        "gripper-tcp",
        FrameRoleEnum.Tool,
        Pose("robot-flange", 0.0, 0.0, 0.12),
        frame => frame.WithParent(flange));

    controller.AddTool("ParallelGripper", tcp, fitted: true);

    for (uint index = 0; index < 6; index++)
    {
        controller.AddAxis($"J{index + 1}", index, AxisKindEnum.Revolute);
    }

    controller.AddLocation(
        "Bin",
        Pose("world", 0.45, -0.30, 0.82),
        location => location.WithOccupancy(false, capacity: 1));
    controller.AddLocation("Fixture", Pose("world", 0.50, 0.25, 0.82));

    controller.AddOutput("GripperOpen", DataTypeIds.Boolean, new Variant(true));
    controller.AddOutput("BenchLight", DataTypeIds.Boolean, new Variant(false));
    controller.AddProgram("Home", "home");
    controller.AddProgram("PickAndPlace", "pick-and-place");
    controller.AddRealTimeChannel(
        "JointTelemetry",
        "joint-telemetry",
        RealTimeTransportEnum.OpcUaFx,
        "opc.udp://239.0.0.40:4840");

    controller.WithDescription(description => description
        .WithKinematicChain(CreateKinematicChain())
        .WithLimits(
            reachRadius: 0.85,
            payloadLimit: 5.0,
            maxCartesianSpeed: 0.25,
            maxCartesianAcceleration: 0.7));
}

static Pose3DDataType Pose(string frameId, double x, double y, double z)
{
    return new Pose3DDataType
    {
        FrameId = frameId,
        Position = new[] { x, y, z }.ToArrayOf(),
        Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }.ToArrayOf()
    };
}

static ArrayOf<KinematicJointDataType> CreateKinematicChain()
{
    var joints = new KinematicJointDataType[6];
    for (int ii = 0; ii < joints.Length; ii++)
    {
        joints[ii] = new KinematicJointDataType
        {
            AxisId = $"J{ii + 1}",
            Kind = AxisKindEnum.Revolute,
            OriginTransform = Pose(ii == 0 ? "robot-base" : $"J{ii}", 0.0, 0.0, 0.12),
            AxisVector = new[] { 0.0, 0.0, 1.0 }.ToArrayOf()
        };
    }
    return joints.ToArrayOf();
}

public sealed class MySafetySource : IRobotIntentSafetySource
{
    public ValueTask<RobotIntentSafetySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<RobotIntentSafetySnapshot>(new RobotIntentSafetySnapshot(
            SafeMotionFunctionEnum.None,
            EmergencyStopActive: false,
            ProtectiveStopActive: false,
            SafeSpeedLimitActive: false,
            SafeSpeedLimit: 0.0,
            SafetyControllerOk: true,
            LocalizedText.Null));
    }
}
```

The builder enforces the invariants that make the address space dependable. A tool can only point at a
TCP frame whose role is `Tool`, and at most one tool below a controller can be `Fitted=true`, because a
motion intent otherwise has no unambiguous active tool centre point. Axis indices must be unique and
contiguous from zero, because `JointMoveIntentDataType.JointTargets` is an array and the index is the
array coordinate. `Capabilities.AxisCount` is written from the number of axes, so a client can validate
a joint target vector before submitting. Every capability must include `BufferModeEnum.Aborting`, which
is the fail-safe "replace what is running" mode every controller must understand.

Declare only what the executor can really do. `Accepts<TIntent>()` is not documentation; it is the
admission rule. If a robot cannot abandon a tool change safely, declare
`Accepts<ToolChangeIntentDataType>(cancelSupported: false)` and implement `CanCancel` accordingly.

### Writing an executor

`IntentControllerHost` owns admission, queueing, the Part 10 state machine, cancellation acceptance and
the final result node. `IIntentExecutor` owns the doing: turning a typed intent into controller-specific
motion, reporting progress and returning an outcome.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.RobotIntent;

public sealed class MyIntentExecutor : IIntentExecutor
{
    public async ValueTask<IntentOutcome> ExecuteAsync(
        IntentExecution execution,
        CancellationToken cancellationToken)
    {
        switch (execution.Intent)
        {
            case LinearMoveIntentDataType linear:
                await MoveLinearAsync(linear.Target, execution.Progress, cancellationToken);
                return IntentOutcome.SucceededAt(linear.Target);

            case JointMoveIntentDataType joint when joint.HasJointTargets:
                await MoveJointsAsync(joint.JointTargets, execution.Progress, cancellationToken);
                return IntentOutcome.Success;

            case WaitIntentDataType wait:
                await Task.Delay(TimeSpan.FromMilliseconds(wait.Duration), cancellationToken);
                return IntentOutcome.Success;

            default:
                return IntentOutcome.Fail(
                    IntentFailureEnum.CapabilityNotSupported,
                    "The executor does not implement this intent.");
        }
    }

    public bool CanCancel(IntentExecution execution)
    {
        return execution.Intent is not ToolChangeIntentDataType;
    }

    private static async Task MoveLinearAsync(
        Pose3DDataType target,
        IIntentProgress progress,
        CancellationToken cancellationToken)
    {
        for (int step = 1; step <= 20; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.ReportProgress(step / 20.0);
            progress.ReportPose(target);
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private static async Task MoveJointsAsync(
        ArrayOf<double> joints,
        IIntentProgress progress,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        progress.ReportProgress(1.0);
    }
}
```

The cancellation token is signalled after a cancel has been accepted and the operation has entered
`Cancelling`. The executor then brings motion to a controlled end and returns; it does not need to
manufacture a `Cancelled` result, because the host records that terminal state. `CanCancel` is the
per-operation hook for refusing a cancel that would leave the cell in a worse state, such as a tool
change mid-exchange.

### Client: submit and await

The high-level client discovers controllers under `Server/RobotIntent/Controllers`, reads their
capabilities, takes command authority, builds a typed intent and returns an awaitable operation handle.

```csharp
using System;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Client.Intent;

RobotIntentClient discovery = session.RobotIntent(telemetry);
ArrayOf<RobotIntentNodeLookupEntry> controllers =
    await discovery.DiscoverControllersAsync(ct);

RobotIntentControllerClient controller = discovery.Controller(controllers[0].NodeId);
RobotIntentControllerInfo info = await controller.ReadAsync(ct);

await using CommandAuthorityLease authority =
    await controller.RequestAuthorityAsync(ct);
if (!authority.Granted)
{
    throw new InvalidOperationException($"Command authority is held by {authority.CurrentOwner}.");
}

Pose3DDataType target = RobotIntentBuilder.Pose(
    x: 0.45,
    y: -0.30,
    z: 0.82,
    qx: 0.0,
    qy: 0.0,
    qz: 0.0,
    qw: 1.0,
    frameId: "world");

LinearMoveIntentDataType intent = RobotIntentBuilder
    .LinearMove(target, speed: 0.2)
    .WithIntentId("move-to-bin")
    .WithBufferMode(BufferModeEnum.Aborting)
    .Build();

await using IntentOperationHandle handle =
    await controller.SubmitIntentAsync(intent, ct);
IntentResultDataType result = await handle.Completion;
Console.WriteLine($"{handle.IntentId} ended with {handle.Current.ExecutionState}: {result.Failure}");
```

Use `TrySubmitIntentAsync` when refusal is part of the normal control flow and you want the fixed
`IntentFailureEnum` rather than an exception:

```csharp
IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(intent, ct);
if (!submission.Accepted)
{
    Console.WriteLine($"Refused: {submission.Failure} {submission.Message.Text}");
    return;
}

await using IntentOperationHandle handle =
    await controller.TrackOperationAsync(submission.IntentId, submission.Operation, ct);
```

`IntentOperationHandle.StartAsync` subscribes to `ExecutionState`, `Progress`, `CurrentPose` and
`Result`. The `Changed` event is the convenient way to update a UI:

```csharp
handle.Changed += snapshot =>
{
    Console.WriteLine($"{snapshot.ExecutionState} {snapshot.Progress:P0}");
    Pose3DDataType current = snapshot.CurrentPose;
    _ = current.FrameId;
};
```

`CurrentPose` is a status report at the Subscription's sampling and publishing rate. It is deliberately
not a servo channel; using it to close a motion-control loop is outside the Robot Intent model and
outside OPC UA client/server timing guarantees.

### Client: missions

A mission builder emits the same `IntentDataType` structures used for single submissions. Released
steps form the base; unreleased steps form the horizon. A horizon update replaces the unreleased
suffix while preserving the released prefix.

```csharp
MissionDataType mission = RobotIntentBuilder.Mission("tray-42")
    .WithMissionUpdateId(1)
    .ReleasedStep("approach", RobotIntentBuilder
        .LinearMove(RobotIntentBuilder.Pose(0.40, -0.20, 0.90, 0, 0, 0, 1, "world"), 0.2)
        .Build())
    .ReleasedStep("pick", RobotIntentBuilder.Pick(
        info.Lookups.Locations[0].NodeId,
        info.Lookups.Tools[0].NodeId).Build())
    .HorizonStep("place", RobotIntentBuilder.Place(
        info.Lookups.Locations[1].NodeId,
        info.Lookups.Tools[0].NodeId).Build())
    .Build();

MissionSubmissionResult submitted = await controller.SubmitMissionAsync(mission, ct);
if (!submitted.Accepted)
{
    Console.WriteLine($"Mission refused: {submitted.Failure} {submitted.Message.Text}");
    return;
}

ArrayOf<MissionStepDataType> revisedHorizon = new[]
{
    new MissionStepDataType
    {
        StepId = "place",
        SequenceId = 3,
        Released = false,
        Intent = RobotIntentBuilder.Place(
            info.Lookups.Locations[1].NodeId,
            info.Lookups.Tools[0].NodeId).Build()
    }
}.ToArrayOf();

MissionUpdateOutcome update =
    await controller.UpdateMissionAsync("tray-42", 2, revisedHorizon, ct);
switch (update.Result)
{
    case MissionUpdateResultEnum.Accepted:
        break;
    case MissionUpdateResultEnum.Outdated:
        await controller.ReadAsync(ct);
        break;
    case MissionUpdateResultEnum.BaseConflict:
        Console.WriteLine("The update changed a released step; rebuild from the current base.");
        break;
    case MissionUpdateResultEnum.UnknownMission:
        Console.WriteLine("The mission has already ended or was never admitted.");
        break;
    case MissionUpdateResultEnum.Rejected:
        Console.WriteLine(update.Message.Text);
        break;
}
```

Transitions turn the flat list into a step graph. `MissionCondition.Always()` creates the empty
`ContentFilter` that OPC UA defines as true; an empty `Transitions` array leaves the mission as a flat
sequence.

```csharp
MissionDataType branched = RobotIntentBuilder.Mission("inspect-or-rework")
    .WithMissionUpdateId(1)
    .ReleasedStep("inspect", RobotIntentBuilder.CallProgram(info.Lookups.Programs[0].NodeId).Build())
    .HorizonStep("accept", RobotIntentBuilder.Wait(100).Build())
    .HorizonStep("rework", RobotIntentBuilder.CallProgram(info.Lookups.Programs[1].NodeId).Build())
    .Transition("inspect", "accept", DivergenceKindEnum.Alternative, MissionCondition.Always())
    .Transition("inspect", "rework", DivergenceKindEnum.Alternative)
    .ErrorPolicy("rework", ErrorPolicyEnum.Retry)
    .Build();
```

### Client: cancellation, pause and retry

Cancellation is refusal-aware. A server may legitimately refuse because the executor's `CanCancel`
returned false or because the session does not hold command authority. `Cancelling` is not terminal;
wait for the operation handle to reach `Cancelled`, `Succeeded`, `Failed` or `Retriable`.

```csharp
IntentCommandOutcome cancel =
    await handle.CancelAsync(StopModeEnum.QuickStop, ct);
if (!cancel.Accepted)
{
    Console.WriteLine("The server refused this cancel request.");
}

IntentCommandOutcome pause = await handle.PauseAsync(ct);
if (pause.Accepted)
{
    await handle.ResumeAsync(ct);
}

IntentResultDataType final = await handle.Completion;
if (handle.Current.ExecutionState == ExecutionStateEnum.Retriable)
{
    IntentSubmissionResult retry = await handle.RetryAsync(ct);
    if (retry.Accepted)
    {
        await using IntentOperationHandle retryHandle =
            await controller.TrackOperationAsync(retry.IntentId, retry.Operation, ct);
        final = await retryHandle.Completion;
    }
}
```

`Pause` stops queue dispatch only. An intent that is already executing keeps reporting `Executing`;
`Resume` lets queued work start again.

### Pose maths

`PoseMath` implements Annex C conversion between Robot Intent's `(x, y, z, w)` quaternion and the core
OPC UA `ThreeDFrame` A/B/C orientation:

```csharp
Pose3DDataType pose = RobotIntentBuilder.Pose(
    0.4, 0.2, 0.8,
    0.0, 0.0, 0.3826834323650898, 0.9238795325112867,
    "world");

ThreeDFrame frame = PoseMath.ToThreeDFrame(pose);
Pose3DDataType roundTripped = PoseMath.FromThreeDFrame(frame, "world");

if (!PoseMath.TryValidate(roundTripped, 1e-6, out string? error))
{
    throw new InvalidOperationException(error);
}
```

`FrameTree` is the corresponding helper for re-expressing poses through a declared frame graph:

```csharp
var frames = new FrameTree();
frames.TryAdd(
    "world",
    "",
    RobotIntentBuilder.Pose(0, 0, 0, 0, 0, 0, 1, "world"),
    FrameRoleEnum.World,
    out _);
frames.TryAdd(
    "robot-base",
    "world",
    RobotIntentBuilder.Pose(0.5, 0.0, 0.0, 0, 0, 0, 1, "world"),
    FrameRoleEnum.Base,
    out _);

Pose3DDataType inBase = RobotIntentBuilder.Pose(
    0.1, 0.0, 0.2,
    0.0, 0.0, 0.0, 1.0,
    "robot-base");

if (frames.TryExpress(inBase, "world", out Pose3DDataType inWorld, out string? frameError))
{
    Console.WriteLine(inWorld.Position[0]);
}
```

### Handling refusal

A refusal is an ordinary method outcome: the Method call returns `Good`, `Accepted` is false and the
failure is in the output arguments. A Bad `StatusCode` still means the transport, Session or Service
layer failed. The point of the small failure set is that the client can choose a policy without parsing
human text. `NoTransition` is `IntentFailureEnum` value 20:

```csharp
IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(intent, ct);
if (!submission.Accepted)
{
    switch (submission.Failure)
    {
        case IntentFailureEnum.QueueFull:
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            break;
        case IntentFailureEnum.ParameterInvalid:
        case IntentFailureEnum.NoTransition:
        case IntentFailureEnum.JointLimit:
        case IntentFailureEnum.WorkspaceLimit:
            Console.WriteLine("Re-plan with a reachable target.");
            break;
        case IntentFailureEnum.ControlNotOwned:
        case IntentFailureEnum.NotPermittedInMode:
        case IntentFailureEnum.SafetyLimitExceeded:
            Console.WriteLine($"Operator action required: {submission.Message.Text}");
            break;
        default:
            Console.WriteLine(submission.Message.Text);
            break;
    }
}
```

### Monitoring and direct control for agents

An agent needs the same client API a conventional supervisory application needs, but it uses it in a
tighter loop: discover, read the declaration, read live state, request authority, submit one bounded
piece of work, and observe the operation before deciding what to do next. The client exposes those
steps directly:

```csharp
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Client.Intent;

RobotIntentClient robots = new(session, telemetry, streamingSubscription);
ArrayOf<RobotIntentNodeLookupEntry> controllers =
    await robots.DiscoverControllersAsync(ct);

RobotIntentControllerClient controller = robots.Controller(controllers[0].NodeId);
RobotIntentControllerInfo info = await controller.ReadAsync(ct);
RobotIntentControllerState state = await controller.ReadStateAsync(ct);

if (!info.SupportedFacets.Contains("RI-Motion-Linear") ||
    !(state.Ready.Available && state.Ready.Value))
{
    return;
}

await using CommandAuthorityLease authority = await controller.RequestAuthorityAsync(ct);
if (!authority.Granted)
{
    Console.WriteLine($"Command authority is held by {authority.CurrentOwner}.");
    return;
}

IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(
    RobotIntentBuilder.LinearMove(RobotIntentBuilder.Pose(
        0.40,
        0.10,
        0.20,
        0.0,
        0.0,
        0.0,
        1.0,
        "world")).Build(),
    ct);

if (!submission.Accepted)
{
    Console.WriteLine($"{submission.Failure}: {submission.Message.Text}");
    return;
}

await using IntentOperationHandle operation = await controller.TrackOperationAsync(
    submission.IntentId,
    submission.Operation,
    ct);
IntentOperationWaitResult waited = await operation.WaitForCompletionAsync(
    TimeSpan.FromSeconds(2),
    ct);

if (!waited.Completed)
{
    ArrayOf<IntentOperationSnapshot> operations = await controller.ListOperationsAsync(ct);
    Console.WriteLine($"Still running; {operations.Count} operations are published.");
}
```

`ReadStateAsync` reads the values an agent must not infer from the declaration: `OperationalMode`,
`Ready`, `ControlOwner`, `ActiveIntent`, `ActiveMission` when missions are supported, the safety state
and the published operation and mission lists. `ListOperationsAsync` and `ListMissionsAsync` return the
server's outstanding work, not a client-side cache. `WaitForCompletionAsync` is deliberately bounded:
timeout returns `Completed=false` with a refreshed operation snapshot, so an agent can report progress
or re-read state instead of blocking forever.

The same client carries the explicit control methods: `CancelIntentAsync`, `CancelAllAsync`,
`PauseAsync`, `ResumeAsync`, `RetryAsync` and `ReleaseControlAsync`. Each returns the server's outcome.
No helper retries, converts a refusal into a different command, or obtains command authority as a side
effect.

### MCP tools for Robot Intent

`OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics` packages the client surface above as Model Context
Protocol tools for an LLM host. Register it beside the core OPC UA MCP package and select the Robotics
profile when the host should expose robot tools:

```csharp
using ModelContextProtocol.Server;
using Opc.Ua.Mcp;

builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpRobotics();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Robotics)
    .WithOpcUaRoboticsTools(McpToolProfile.Robotics)
    .WithTools<MyApplicationTools>();
```

`McpToolProfile.Robotics` gates registration. Passing the same profile to every OPC UA MCP package is
safe: packages whose tools are not selected contribute nothing instead of failing. The Robotics package
adds four tool groups:

* discovery: list controllers and read a controller's declared `SupportedIntents`, `SupportedFacets`
  and lookup tables;
* monitoring: read live state, list operations and missions, and wait for an operation with a bounded
  timeout;
* direct control: request and release authority, cancel, pause, resume, retry, and submit one tool per
  intent kind;
* missions: submit, update the horizon of, and cancel missions.

The sample `IntentViewerClient --mcp` is one host for these tools. In headless mode it defaults to MCP
stdio. With `--view`, it automatically uses Streamable HTTP because MCP stdio carries protocol frames
on stdout and the in-process OpenUSD viewport shares that stream. An explicit `--transport stdio
--view` is honoured for diagnostics but warned about.

The safe agent pattern is not a longer tool catalogue; it is a contract:

1. Read `SupportedIntents` and `SupportedFacets` before commanding. The declaration is how the server
   tells the truth about itself. Asking for an undeclared intent, buffer mode, facet or mission feature
   is refused rather than probed into existence.
2. Request command authority explicitly. Observation is open, but commanding is arbitrated between
   sessions. Authority is never acquired as a side effect of submitting an intent or mission.
3. Treat a refusal as information, not a transient failure. `NotPermittedInMode`,
   `SafetyLimitExceeded`, `ControlNotOwned` and `CapabilityNotSupported` are decisions by the server:
   mode selection, safety state, command ownership and capability are not things an agent should
   brute-force. The MCP tools deliberately never retry on the agent's behalf.

Those rules are load-bearing because clauses 9 and 10 of the Robot Intent draft make the server's
honesty about capabilities and safety awareness part of the safety case. A server that declares only
what it can perform and refuses what it cannot lets a planner re-plan, escalate to an operator, or stop.
An agent that hides refusals in a retry loop turns that honesty back into an unsafe black box.

The boundary remains the same whether the caller is a person, a program, or an LLM. Robot Intent
commands a robot at the level of task intent. It does not change operational mode, clear stops, command
safe motion functions, satisfy a safety-rated single-point-of-control requirement, stream cyclic servo
samples, or bypass the robot controller's path planning and safety system.

### Facets in code

A controller publishes the facets it claims in the read-only `Capabilities.SupportedFacets`, so a
client reads the claim rather than reconstructing it. The server binds that variable to the facet
calculator, so the list is recomputed on every read and tracks the address space instead of being a
registration-time snapshot. On the server-side builder:

```csharp
ArrayOf<string> facets = builder.ComputeFacets();
ArrayOf<string> published = RobotIntentFacetCalculator.Compute(builder.State);
```

On the client, `ReadAsync` returns what the server published:

```csharp
RobotIntentControllerInfo info = await controller.ReadAsync(ct);
if (info.SupportedFacets.Contains("RI-Mission-Horizon"))
{
    Console.WriteLine("The controller accepts missions with horizon updates.");
}
if (!info.Facets.EveryCapabilitySupportsAborting)
{
    throw new InvalidOperationException("The server published an invalid capability set.");
}
```

`info.Facets` remains available as a convenience projection of the individual capability variables,
and against a server that predates `SupportedFacets` it is all there is. Prefer `SupportedFacets`
where the server publishes it: the projection can only see the flags, so it necessarily disagrees with
the server about any facet whose requirements go beyond a single flag.

That disagreement is the reason `SupportedFacets` exists. A facet is not a restatement of the
declaration a client has already read. Some of what the table below requires — that blending modes are
honoured, that the refusal rules are followed, that a mission base is immutable — cannot be settled by
reading the address space at all, so a client deriving facets locally is guessing at precisely the
rows that matter most. A published claim that could drift from the model would reintroduce the same
defect with the server's authority behind the wrong answer; the live read binding makes that drift
impossible by construction.

### Limitations

The current stack implements the draft information model, admission rules, Part 10 operation lifecycle,
missions, command authority, cancellation, safety observation, capability/facet reporting, real-time
channel leasing and the client handles shown above. It does not provide a safety-rated interface, a
servo-level real-time channel, or a vendor robot driver. The facet calculator checks every structural
requirement in clause 12.2, but behavioural requirements remain the server's attestation and require
interop or acceptance testing to verify. The sample executor is a simulator, transition conditions are
only as powerful as the server-supplied `ConditionEvaluator`, and real-time channels are brokered as
leases rather than implemented as a cyclic transport in this package. The namespace
`http://opcfoundation.org/UA/RobotIntent/` and all NodeIds remain provisional until the companion
specification is ratified.

### Why a submission is not a method call

An OPC UA `Call` cannot stay open for the length of a real motion. Session timeouts, SecureChannel
re-keying and transport timeouts all bound it, and OPC 10000-4 §5.12.2 is explicit that when the
Session ends the method result is discarded *"independent of the task actually performed at the
Server"*. A synchronous method that commands a robot is therefore not merely inelegant: it loses the
outcome of work that has **already physically happened**. The robot keeps moving after the answer has
been thrown away.

OPC 10000-10 gives the OPC Foundation's own resolution — a Method performs a calculation, a
**Program** runs a batch process or a machine-tool part program. So `SubmitIntent` returns as soon as
the intent is **admitted**, and what it returns is a NodeId: an `IntentOperationType` instance, a
Part 10 program instance created for that submission, which the client subscribes to for progress and
reads for the result.

Building on `ProgramStateMachineType` buys four things this model then does not have to invent:
transition events, a terminal result object that survives the operation, invocation diagnostics
recording which Session commanded what, and a lifetime model for the instance itself. Two of those —
`FinalResultData` and `ProgramDiagnostic` — are Optional in Part 10, and Robot Intent promotes both to
**Mandatory**, because a `shall` that rests on a member a conformant server may omit is not a
requirement.

### The intent hierarchy

Intents are a **DataType hierarchy**, not one Method per verb.

```
IntentDataType (abstract)
├── MotionIntentDataType (abstract)
│   ├── JointMoveIntentDataType        movej / MoveJ / PTP / J / MOVJ
│   ├── LinearMoveIntentDataType       movel / MoveL / LIN / L / MOVL
│   ├── CircularMoveIntentDataType     movec / MoveC / CIRC / C / MOVC
│   ├── TrajectoryIntentDataType       a time-parameterised path, handed over whole
│   ├── CartesianPathIntentDataType    a taught path with per-waypoint blending
│   ├── ForceIntentDataType            move until contact
│   └── ProcessIntentDataType (abstract)
│       ├── ArcWeldIntentDataType          SpotWeldIntentDataType
│       ├── DispenseIntentDataType         FastenIntentDataType
│       └── PalletiseIntentDataType        SurfaceFinishIntentDataType
├── GraspIntentDataType / ReleaseIntentDataType
├── PickIntentDataType / PlaceIntentDataType
├── ToolChangeIntentDataType
├── SetOutputIntentDataType
├── CallProgramIntentDataType
└── WaitIntentDataType
```

Three consequences follow, and each is why the shape was chosen:

* **A single intent and a mission step are the same thing.** `MissionStepDataType.Intent` is an
  `IntentDataType`, so nothing has to be expressed twice.
* **Extension is subtyping.** A vendor adds an intent by deriving from `IntentDataType`. It is then
  carried, queued, cancelled and reported by the existing machinery without a new Method.
* **Discovery is a read, not a probe.** `IntentCapabilitiesType.SupportedIntents` names each accepted
  DataType, so a client learns what a robot accepts by reading one Variable rather than by browsing
  for BrowseNames and inferring support from their presence.

### Poses, frames and units

`Pose3DDataType` carries a `FrameId`, a `Position` of three doubles in **metres**, and an
`Orientation` of four doubles forming a **unit quaternion ordered (x, y, z, w)**.

Four rules make that unambiguous, and the server enforces all of them:

1. Every frame is **right-handed**.
2. Units are fixed by the specification and are **not** negotiable per instance: position in metres,
   joint targets in radians for a `Revolute` axis and metres for a `Prismatic` one, force in newtons,
   durations in milliseconds. `Pose3DDataType` appears as a Method argument, where no `EUInformation`
   property can reach it, so a per-instance unit would be undeliverable.
3. `Orientation` must be normalised. A quaternion whose norm differs from 1 by more than `1e-6` is
   rejected with `ParameterInvalid`.
4. `FrameId` names a `CoordinateFrameType` instance under the controller's `Frames` folder. An empty
   `FrameId` means the server's default work frame.

Quaternions are used because OPC UA defines no quaternion DataType anywhere, and because the `A`, `B`
and `C` fields of the core `ThreeDOrientation` carry no convention of their own. `PoseMath` implements
the specification's Annex C conversion in both directions, including the two properties that are
normative and easy to get wrong:

* the `asin` argument is **clamped** to `[-1, +1]`, because floating-point error at a pole otherwise
  turns a legal orientation into a domain error;
* `q` and `-q` denote the same orientation, and the conversion emits the representative whose `w` is
  non-negative, so two servers describing one orientation produce the same four numbers.

`FrameTree` composes the transforms along the path between two frames, so a pose given in one frame can
be re-expressed in another.

### The lifecycle

The Part 10 state machine carries the coarse state and generates the events. `ExecutionState` refines
it, because `Queued`, `Cancelling` and the three distinct terminal outcomes cannot be told apart from
`CurrentState` alone. The pairing is exhaustive — a combination not in this table is not legal:

| `ExecutionState` | Part 10 state | Meaning |
|---|---|---|
| `Accepted` | `Ready` | Admitted and validated; not yet queued or executing. |
| `Queued` | `Ready` | Waiting behind another intent. `QueuePosition` is non-zero. |
| `Executing` | `Running` | Commanding the robot now. |
| `Suspended` | `Suspended` | Reserved for an executor-visible suspension; the stock `Pause` command does not publish it. |
| `Cancelling` | `Running` | A cancel was accepted; motion is being brought to a controlled end. |
| `Succeeded` | `Halted` | Terminal. Completed as requested. |
| `Failed` | `Halted` | Terminal. `Result.Failure` carries the reason. |
| `Cancelled` | `Halted` | Terminal. Ended early because a cancel was accepted. |
| `Retriable` | `Halted` | Terminal for now; `Retry` may re-attempt it. |

`Cancelling` is **not** terminal. A client that treats acceptance of a cancel as the end of motion acts
too early.

`ActiveIntent` and, on servers with `MissionsSupported` true, `ActiveMission` summarize the currently executing work.
`ActiveMission` is null when no executing intent belongs to a mission; servers without mission support omit it.
Because `ActiveMission` is required whenever `MissionsSupported` is true, a client can treat a mission-capable server
that omits it the same way it treats any other contradiction between declaration and address space: the capability
claim is not usable as stated.

#### Refusal is an ordinary outcome

`SubmitIntent` refuses in a fixed order, and the order matters — a caller that lacks authority must be
told *that*, not that its parameters are wrong:

1. `ControlNotOwned` — the calling Session does not hold command authority.
2. `NotPermittedInMode` — `OperationalMode` is not `Automatic` or `AutomaticExternal`.
3. `CapabilityNotSupported` — the intent's DataType is not among `SupportedIntents`, or its
   `BufferMode`/`BlockingMode` is not among those the capability entry permits.
4. `ParameterInvalid` — a parameter is missing, malformed or out of range.
5. `QueueFull` — admitting it would exceed `MaxQueueDepth`.

A refusal creates no operation instance and moves nothing. It is reported in the **output arguments** —
`Accepted` false with a `Failure` and a `Message` — and the call returns `Good`. A Bad `StatusCode`
still means what it always meant: the transport, the Session or the Service layer failed. The
distinction is normative, and it is what makes the failure set diagnosable: a client decides whether to
retry, re-plan or escalate from the `IntentFailureEnum` value alone.

#### Queueing and blending

`BufferMode` decides how a new submission relates to what is already executing. The values are
PLCopen's `MC_BufferMode`, adopted unchanged:

| Value | Meaning |
|---|---|
| `Aborting` | Abort what is executing and start immediately. The default, and always accepted. |
| `Buffered` | Queue; start when the predecessor succeeds. |
| `BlendingLow` / `BlendingPrevious` / `BlendingNext` / `BlendingHigh` | Queue, and do not decelerate to a stop at the boundary. |

Where blending occurs, the predecessor reaches `Succeeded` **when blending begins**, not when its
target is exactly attained, and its `Result.AchievedPose` records where the tool centre point was at
that moment. That is what PLCopen defines, and reporting it any other way would tell a client the robot
stopped somewhere it never was. A server that accepts a blending mode but executes it as `Buffered`
reports `BlendingSupported` false, so a client can tell a robot that blends from one that merely
tolerates being asked to.

`BlockingMode` is orthogonal and constrains concurrency rather than ordering — it is the VDA 5050
`blockingType` matrix. A server does not begin an intent whose `BlockingMode` is `Single` or `Hard`
while any other intent is executing.

#### Cancellation is not the `Cancel` Service

The OPC UA `Cancel` Service in OPC 10000-4 §5.7.5 cancels an **outstanding service request**. It does
not stop the robot: it returns `Bad_RequestCancelledByClient` for that request and leaves the motion
running. Stopping a robot is `CancelIntent`, `CancelMission` or `CancelAll`.

A server **may refuse** a cancel and says so in the `Accepted` output. Some motions cannot be abandoned
part-way without leaving the cell worse than completing them would — a tool change mid-exchange, a
placement mid-release.

### Missions

A mission is an ordered sequence of intents submitted and tracked as a unit, so a supervisor can commit
work in advance and still change what has not yet been committed.

Every step carries `Released`. The released steps form a prefix called the **base**; the rest form the
**horizon**.

```
Step 0        Step 1        Step 2   │   Step 3        Step 4
released      released      released │   horizon       horizon
└────────── base: committed ─────────┘   └── revisable ──┘
```

The base is committed and immutable: the server assumes every released step is executing or already
executed and refuses any update that would alter, remove or reorder one. `UpdateMission` replaces the
horizon wholly and may release some of it, extending the base. `MissionUpdateId` must be strictly
greater than the mission's current value, which is what makes two updates that crossed in flight safe —
the later one wins and the earlier is rejected with `Outdated` rather than applied out of order. An
update is applied **atomically**.

Where a mission carries `Transitions`, it becomes the step-and-transition form of an IEC 61131-3
sequential function chart. Conditions are OPC UA `ContentFilter`s — the base specification's own filter
grammar, reused rather than invented — and `DivergenceKind` says whether exactly one transition is taken
(`Alternative`, evaluated in array order so two clients predict the same branch) or all of them are
(`Parallel`). Per-step `ErrorPolicy` covers `Abort`, `Retry`, `Skip`, `Fallback` and `Compensate`. An
empty `Transitions` array leaves the mission the flat sequence it was, which is what makes the graph an
addition rather than a replacement.

Where a step has outgoing transitions and none of their conditions holds, the mission terminates `Failed` with
`NoTransition` (`IntentFailureEnum` value 20). The same outcome is reported if the selected transition target no
longer resolves to a step of the mission, rather than reporting success while leaving requested work unexecuted.

### Command authority

At most one Session at a time holds command authority over a controller, and only that Session may
submit. Authority is released automatically when the holding Session closes, so a crashed client does
not lock a robot permanently. Reading, browsing and subscribing require no authority: observation is
always permitted.

> Command authority arbitrates between OPC UA clients. It is **not** the single point of control that
> ISO 10218-2 requires — that concerns mutual exclusion of remote command and local manual control and
> is enforced by safety-rated means outside this interface. It is also **not authorisation**: a Session
> that holds authority but lacks the necessary Role is still refused.

### Safety, and the boundary that is never crossed

**This is a non-safety-rated application interface.** The Methods here are application-level requests.
They do not constitute, and must not be used as, safety functions as defined in IEC 61508, nor safety
communication as defined in IEC 61784-3 or IEC 62541-15.

This is a property of the technology, not a scoping preference. OPC 10000-15 carries cyclic safety data
from a SafetyProvider to a SafetyConsumer, and the consumer's request carries an identifier, a
monitoring number and one octet of explicitly **non-safety** flags — so a caller has no channel through
which to supply safety-rated arguments. Every safety fieldbus expresses a safety command as a
**continuously asserted cyclic signal**, because the integrity argument rests on the fail-safe state
that follows when assertion stops. A Method call has no defined behaviour when it stops being called,
and therefore cannot be a safety function however it is labelled.

What the model *can* do is observe and refuse. `SafetyStateType` reports what the safety system is
enforcing, and the server refuses a submission:

* with `SafetyLimitExceeded`, when `SafeSpeedLimitActive` is true and the intent's
  `Constraints.CartesianSpeed` exceeds `SafeSpeedLimit`;
* with `NotPermittedInMode`, when `EmergencyStopActive` or `ProtectiveStopActive` is true, when
  `SafetyControllerOk` is false, or when `OperationalMode` is not `Automatic` or `AutomaticExternal`.

Each of those is observable against a running server: assert a protective stop and a conformant server
refuses; lower `SafeSpeedLimit` below a submitted speed and it refuses.

**What none of that makes true.** These refusals are an application-layer courtesy performed by
non-safety-rated software. They reduce the number of requests the safety system has to reject; they are
not a protective measure.

* A client must not treat acceptance of an intent as evidence that the motion is safe.
* A client must not treat `SafeSpeedLimit` as a limit *this interface* enforces — the safety system
  enforces it, and would enforce it identically if this model did not exist.
* `StopMode` expresses urgency and selects **no** IEC 60204-1 stop category. A client that requires a
  category-rated stop obtains it from the safety system; it cannot be obtained here.
* The model may **observe** the safety system and **refuse** on what it sees. It may never
  **instruct** it: no Method commands a safe motion function, changes an operational mode, or clears a
  stop. `OperationalMode` is read-only, because mode selection is a safety function performed by a key
  switch or an interlock, and an interface that could change it from the network would defeat the
  arrangement it is reporting.

### What this interface carries, and what it brokers

OPC UA method invocation is not deterministic and completes in tens of milliseconds. Vendor real-time
channels run two to four orders of magnitude faster on dedicated transports. The model divides the work
rather than pretending the gap is not there.

**Carried here.** A trajectory, a Cartesian path or a force-controlled move is handed over *whole* and
run by the robot's own motion kernel. The round trip happens once, at submission, so transport latency
bounds how quickly work can be *started* and never how accurately it is *executed*. This is the shape of
`FollowJointTrajectory` in ROS and of the PLCopen buffered path function blocks, and it is why
trajectory execution belongs here while trajectory streaming does not.

**Brokered.** Where a client genuinely needs a high-rate channel — visual servoing, force tracking,
conveyor following — `RealTimeChannelType` describes one and `OpenRealTimeChannel` leases it. The
samples travel on that channel and never through this interface. Of the transports named (`Rtde`,
`Egm`, `Fri`, `Rsi`, `MotoRos2`, `OpcUaFx`, `Other`) only `OpcUaFx` is an OPC Foundation specification;
the rest are vendor channels the model describes without defining.

A lease lapses at `LeaseExpiry` unless renewed, and is released when the holding Session closes — the
same reasoning as command authority: a client that dies must not hold a resource for good. While a lease
is held, the server refuses motion intents with `CapabilityNotSupported` unless it can genuinely
arbitrate between the two sources, because two things commanding one robot with no arbitration is
exactly the failure that rule exists to prevent.

`IntentOperationType.CurrentPose` exists so a client can *watch* a motion. It is a status report
delivered at whatever rate the client's Subscription asks for, and using it to close a control loop is
outside this model.

### NodeIds in intents are untrusted input

Every NodeId-valued member of an intent is chosen by the client, so the server validates that each
resolves to a node of the expected type **under the controller being commanded**, and refuses with
`ParameterInvalid` otherwise. A NodeId that resolves to a node belonging to a different controller, or
to no node at all, is never acted on.

| Member | Resolves to |
|---|---|
| `PickIntentDataType.Source`, `PlaceIntentDataType.Destination`, `PalletiseIntentDataType.Pattern` | a `LocationType` under the controller |
| `MotionIntentDataType.ToolFrame`, `ForceIntentDataType.FrameId` | a `CoordinateFrameType` under the controller; `ToolFrame` additionally of `Role` `Tool` |
| `ToolChangeIntentDataType.Tool` | a `ToolType` under the controller, or null to release the fitted tool |
| `SetOutputIntentDataType.Output` | an `OutputSignalType` under the controller; `Value` must match that signal's own DataType |
| `CallProgramIntentDataType.Program`, `ProcessIntentDataType.ProcessProgram` | a `ProgramType` under the controller |
| `WaitIntentDataType.Signal` | an `OutputSignalType` under the controller, or a Boolean Variable under it |
| `FastenIntentDataType.Joint` | a joint in an OPC 40450/40451 model under the controller where one is implemented; otherwise the member is null and the intent's own parameters stand alone |

`CallProgramIntentDataType` deserves particular care because it runs code the server holds: it is
restricted to programs published as `ProgramType` instances, and a program identifier naming anything
else is refused.

For `FastenIntentDataType.Joint`, absence of an OPC 40450/40451 joining or tightening model under the controller is
the structural statement that non-null `Joint` values are not supported. The stock host refuses such values with
`CapabilityNotSupported`. Where the controller does expose an OPC 40450/40451 model, a `Joint` that does not resolve
to a joint in that model is malformed input and is refused with `ParameterInvalid`.

Commanding is a privileged operation. Every Method here moves a machine that can injure people and
destroy property, so the server requires an authenticated Session and restricts the Methods of
`IntentControllerType` by Role, distinctly from read access to the same address space. Observing a robot
and commanding one are different privileges and are not conflated. `UserExecutable` is applied so a
client discovers what it may invoke before invoking it.

### Interoperating with OPC 40010

OPC 40010 describes the robot; Robot Intent commands it. The two are joined by one reference and are
otherwise independent — this model takes no dependency on the Robotics NodeSet, and a server
implementing only Robot Intent is fully conformant.

A server claiming the interop profile exposes a `HasIntentController` reference from the
`MotionDeviceSystemType` instance describing the robot to the `IntentControllerType` instance that
commands it, reports the same operational mode as the OPC 40010 model, publishes as `ProgramType`
instances exactly those programs the OPC 40010 task control can load, and expresses its poses in frames
consistent with the mounting and geometry OPC 40010 describes. The published **RI-Interop-40010** facet
is derived from the inverse of that reference on the intent controller, and because `SupportedFacets`
is a live read binding, the claim tracks the address space whether the reference is attached before or
after the controller is registered.

It does **not** duplicate OPC 40010's topology. `AxisType` exists here only to fix the order, kind and
limits a joint target needs; where OPC 40010 is also implemented its axis description is the fuller one
and **OPC 40010 decides**. `RobotDescriptionType.KinematicChain` is additive, because OPC 40010 defines
no kinematic chain an inverse-kinematics solver could use — and no tool centre point at all, which is
why `ToolType.TcpFrame` supplies the concept and has nothing in OPC 40010 to contradict.

### Facets

Only **RI-Base** is mandatory. A server implements the facets it can honour and declares the rest false;
a facet other than RI-Base is claimed only where every intent type it names appears in
`SupportedIntents`. Each controller lists what it claims in `Capabilities.SupportedFacets`, which
RI-Base requires — a conformance claim that cannot be read is not a claim.

Requirements below are of two kinds, and the difference decides what a tool can check. **Structural**
requirements are settled by reading the address space and the capability declaration; the facet
calculator checks every one of them, and a server shall not list a facet whose structural requirements
are unmet. **Attested** requirements — accepting, honouring, maintaining or observing a rule — cannot
be settled by reading, only by exercising the server, and are the server's own statement under the
honesty rules. Listing **RI-Blending** while treating the blending modes as `Buffered` is a false
statement in exactly the sense the honesty rules forbid, whatever `BlendingSupported` says.

| Facet | Requires |
|---|---|
| **RI-Base** (mandatory) | `RobotIntentRootType`; at least one `IntentControllerType` with `Capabilities`, `Frames`, `Tools`, `Locations`, `Axes` and `Intents`; `SupportedFacets`; `SubmitIntent`, `CancelIntent`, `CancelAll`, `RequestControl`, `ReleaseControl`; `IntentOperationType` instances with the state model above; the refusal rules *(attested)* |
| **RI-Motion-Joint** / **-Linear** / **-Circular** | the corresponding move intent; joint additionally needs `AxisType` instances covering `0`..`AxisCount − 1` |
| **RI-Trajectory** | `TrajectoryIntentDataType`, `TrajectorySupported` true, and the tolerance rules *(attested)* |
| **RI-Path** | `CartesianPathIntentDataType` and `TrajectorySupported` true |
| **RI-Force** | `ForceIntentDataType` and `ForceControlSupported` true — the robot genuinely regulates force *(attested)* |
| **RI-RealTimeChannel** | `RealTimeChannelsSupported` true, the `RealTimeChannels` folder, and the lease rules *(attested)* |
| **RI-Safety** | `SafetyState` present with a bound safety source; populated from the safety system, and the safety refusals *(attested)* |
| **RI-Description** | `Description` with a `KinematicChain` covering every axis, `ReachRadius`, `PayloadLimit`, `MaxCartesianSpeed` |
| **RI-Process-ArcWeld / -SpotWeld / -Dispense / -Fasten / -Palletise / -SurfaceFinish** | the corresponding process intent; palletise also needs a `LocationType` pattern, surface finish also needs **RI-Force** |
| **RI-Grasp** | `GraspIntentDataType`, `ReleaseIntentDataType`, and a `ToolType` with a `TcpFrame` |
| **RI-PickPlace** | `PickIntentDataType`, `PlaceIntentDataType`, and a `LocationType` |
| **RI-ToolChange** | `ToolChangeIntentDataType` and more than one `ToolType` |
| **RI-Output** / **RI-Program** / **RI-Wait** | `SetOutputIntentDataType` + `Outputs`; `CallProgramIntentDataType` + `Programs`; `WaitIntentDataType` |
| **RI-Queue** | `MaxQueueDepth` greater than zero and `Buffered` accepted; `QueuePosition` maintained *(attested)* |
| **RI-Blending** | `BlendingSupported` true and the four blending modes accepted; the modes honoured and `Result.AchievedPose` at the blend point *(attested)* |
| **RI-Pause** / **RI-Retry** | `Pause` and `Resume`; `Retry` with `Retriable` reachable |
| **RI-Mission** | `MissionsSupported` true, `ActiveMission`, `SubmitMission`, `CancelMission`, `MissionType` instances |
| **RI-Mission-Horizon** | RI-Mission plus `MissionHorizonSupported` and `UpdateMission`; base immutability *(attested)* |
| **RI-Mission-Branching** | RI-Mission plus `MissionBranchingSupported`; transitions evaluated and error policies honoured *(attested)* |
| **RI-Interop-40010** | inverse `HasIntentController` from the `MotionDeviceSystemType` instance to the `IntentControllerType` instance; operational-mode agreement with OPC 40010, `ProgramType` instances exactly matching the programs the OPC 40010 task control can load, pose/frame consistency and safety consistency *(attested)* |

### Profile and facet URI publication

`SupportedFacets` is per controller, but `ServerProfileArray` is per server. A Robot Intent server
therefore publishes both levels: every controller lists its facet names in
`Capabilities.SupportedFacets`, and the server publishes the URI of every claimed profile and every
backing facet in `Server/ServerCapabilities/ServerProfileArray`. The two paths shall agree. A profile
URI is backed by at least one controller whose `SupportedFacets` contains every facet in that profile,
and a facet URI is backed by at least one controller that lists that facet.

Profiles use the base `http://opcfoundation.org/UA-Profile/RobotIntent/Server/`:

| Profile | URI |
|---|---|
| Robot Motion Server | `http://opcfoundation.org/UA-Profile/RobotIntent/Server/Motion` |
| Robot Handling Server | `http://opcfoundation.org/UA-Profile/RobotIntent/Server/Handling` |
| Robot Path Server | `http://opcfoundation.org/UA-Profile/RobotIntent/Server/Path` |
| Robot Mission Server | `http://opcfoundation.org/UA-Profile/RobotIntent/Server/Mission` |

Facets use the base `http://opcfoundation.org/UA-Profile/RobotIntent/Facet/`, with the suffix being
the facet name after the `RI-` prefix: **RI-Base** is
`http://opcfoundation.org/UA-Profile/RobotIntent/Facet/Base`, **RI-Motion-Joint** is
`http://opcfoundation.org/UA-Profile/RobotIntent/Facet/Motion-Joint`, and **RI-Process-ArcWeld** is
`http://opcfoundation.org/UA-Profile/RobotIntent/Facet/Process-ArcWeld`. These URIs are provisional
while Robot Intent is a draft; the controller's `SupportedFacets` remains the authority on what that
controller satisfies.

## See also

* [Robot Intent](#robot-intent) — the task-level motion verbs this model leaves
  undefined. OPC 40010 describes the robot; Robot Intent commands it, and the two
  are joined by a single `HasIntentController` reference rather than by either
  model depending on the other.
* [Device Integration (DI) developer guide](DeviceIntegration.md) — the base
  model, fluent device builders, and the companion-spec packaging pattern.
* [Relative Spatial Location and Global Positioning](Positioning.md) — the RSL
  and GPOS models used by the robot sample.
* [OpenUSD binding](OpenUsd.md) — rendering a robot cell as a live USD stage and
  driving intents from a viewport pick.
* [State machines](StateMachines.md) — the Part 10 program lifecycle Robot Intent builds on.
* [Subscriptions](Subscriptions.md) — how the Robot Intent client tracks an operation.
* [Robotics samples](../samples/Robotics/README.md) — runnable servers and clients.
* [Dependency Injection](DependencyInjection.md) — the shared `AddOpcUa()`
  hosting surface.
* [Source Generated NodeManagers](NodeManagers.md#source-generated-node-managers) — the fluent
  `INodeManagerBuilder` and generated factories the builders compose.
