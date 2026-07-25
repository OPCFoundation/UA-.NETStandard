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
| `OPCFoundation.NetStandard.Opc.Ua.Robotics.Client` | Continuation-safe discovery of `MotionDeviceSystem` instances and Robotics type classification. |

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
| `IControllerBuilder` | `AddSoftware`, `AddTaskControl`, `AddAuxiliaryComponent`, `AddDrive`, `Controls`, `UsesSafetyState`. |
| `IRoboticsSoftwareBuilder` | Software identification (manufacturer, model, revision). |
| `IMotionDeviceBuilder` | `AddAxis`, `AddPowerTrain`, `AddDrive`, `AddAuxiliaryComponent`, `WithFlangeLoad`, `WithMotionDeviceCategory`, speed-override binding, `UsesTaskControl`. |
| `IAxisBuilder` | `WithMotionProfile`, `AsVirtual`, actual position/speed/acceleration, `WithAdditionalLoad`, `Requires`. |
| `IPowerTrainBuilder` | `AddMotor`, `AddGear`, `Moves`, `HasSlave`. |
| `IMotorBuilder` | Identification, motor temperature, brake-released, effective load rate, `IsDrivenBy`. |
| `IGearBuilder` | Identification, `WithGearRatio(int numerator, uint denominator)`, `WithPitch`. |
| `IDriveBuilder` / `IAuxiliaryComponentBuilder` | Product code, asset id, component name. |
| `ILoadBuilder` | `WithMass`, `WithCenterOfMass`, `WithInertia`. |
| `ISafetyStateBuilder` | `AddEmergencyStop`, `AddProtectiveStop`, emergency-stop / operational-mode / protective-stop values and bindings. |
| `ITaskControlBuilder` | `AddTaskModule`, execution mode, task-program name and loaded flag, `Controls`. |
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
standard `Controls` relation only. The `TaskControlReference` property remains
absent until a `TaskControlOperationType` instance exists for it to target.

### Validation

Registration fails with a `ServiceResultException` (`BadConfigurationError`)
that reports every problem at once when the configured topology violates the
companion specification:

* a motion-device system without at least one controller, one motion device, and
  one safety state;
* a controller without at least one `SoftwareType` and one `TaskControlType`
  instance — both are mandatory placeholders;
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

An application that already owns a `DiNodeManager` can host Robotics in it:

```csharp
builder.ConfigureRoboticsFor<MyDeviceNodeManager>(async context =>
{
    await context.AddMotionDeviceSystemAsync("RobotCell", system => { /* … */ },
        context.CancellationToken);
});
```

Outside the hosting pipeline, load the models and create the context directly:

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

`RoboticsClient` lets a generic client — a viewer, an OpenUSD connector, or a
fleet manager — find and label robot cells without hard-coded NodeIds:

```csharp
using Opc.Ua.Robotics.Client;

ArrayOf<NodeId> systems = await RoboticsClient.DiscoverMotionDeviceSystemsAsync(
    session, ObjectIds.ObjectsFolder, cancellationToken);

if (RoboticsClient.TryGetRoboticsTypeName(
        typeDefinition, session.NamespaceUris, out string? typeName))
{
    // typeName is MotionDeviceSystem, MotionDevice, Axis, or Controller.
}
```

Discovery uses `ManagedBrowseAsync`, so a server that caps references per node
cannot silently truncate the result. When the server does not expose the
Robotics namespace, discovery returns an empty `ArrayOf<NodeId>` and
`TryGetRoboticsTypeName` returns `false` instead of throwing.

## Sample

[`MinimalRobotServer`](../samples/MinimalRobotServer) exposes a
`MotionDeviceSystem` with two independently mobile 6-axis robots, a cell
emergency stop, a speed-override command, and a runtime-mounted gripper. It
combines Robotics with OPC 10000-210 RSL frames and OPC 10000-211 GPOS locations
and binds the whole cell to an OpenUSD stage, so a generic connector renders it
live with no robot-specific code.

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
