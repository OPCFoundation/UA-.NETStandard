# Opc.Ua.Robotics.Server

Server hosting for the **OPC 40010 Robotics** companion model.

The package uses source-generated model loaders and fluent accessors. It provides:

- `AddRoboticsTypeSystem` — loads the OPC UA DI base model plus the IA and
  Robotics companion models (all source-generated) into a node manager's
  predefined-node collection, in dependency order.
- `RoboticsNodeManager` and `RoboticsNodeManagerFactory` — a stock DI-based
  server manager with an application-owned instance namespace.
- `IRoboticsModelProvider` — deterministic composition of additional compiled
  predefined models. Providers run in ascending `Order`; the built-in DI/IA/
  Robotics provider uses `int.MinValue` so application providers run afterward
  by default. A replacement core provider must advertise both the IA and
  Robotics namespaces.
- `AddRobotics`, `AddRoboticsModel`, `ConfigureRobotics`, and
  `ConfigureRoboticsFor` — Generic Host registration and ordered startup
  configuration APIs.
- `IRoboticsBuildContext` and `IRoboticsConfigurator` — one shared fluent
  builder per manager startup, with narrow service resolution and simulation
  startup on sealing.
- `AddMotionDeviceSystemAsync` — a progressive, validated topology builder for
  controllers, controller software, motion devices, axes, power trains, drives,
  safety state, and task controls. Source-generated factories assign
  per-instance NodeIds before the builder validates and recursively registers
  the completed generated state tree.

```csharp
builder.ConfigureRobotics(async context =>
{
    await context.AddMotionDeviceSystemAsync("RobotCell", system =>
    {
        IMotionDeviceBuilder robot = system.AddMotionDevice("Robot", motion =>
            motion.WithMotionDeviceCategory(
                MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT));
        IDriveBuilder drive = robot.AddDrive(
            "Drive",
            item => item.WithProductCode("DRIVE-1"));
        IPowerTrainBuilder train = robot.AddPowerTrain("PowerTrain");
        IAxisBuilder axis = robot.AddAxis(
            "Axis1",
            item => item.WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                .WithActualPosition(0));
        train.AddMotor("Motor", motor => motor.IsDrivenBy(drive));
        axis.Requires(train);
        train.Moves(axis);

        ISafetyStateBuilder safety = system.AddSafetyState("Safety");
        IControllerBuilder controller = system.AddController("Controller");
        controller.AddSoftware("ControlSoftware", software =>
            software.WithIdentification(data =>
            {
                data.Manufacturer = new LocalizedText("Vendor");
                data.Model = new LocalizedText("Robot Runtime");
                data.SoftwareRevision = "1.0";
            }));
        ITaskControlBuilder taskControl = controller.AddTaskControl(
            "TaskControl",
            task => task.WithComponentName("Main task"));
        controller.Controls(robot).UsesSafetyState(safety);
        taskControl.Controls(robot);
    });
});
```

Each controller must contain at least one `SoftwareType` instance and one
`TaskControlType` instance because both containers define mandatory placeholder
children. The `Controls` and `IsDrivenBy` relationships are optional.

`ITaskControlBuilder.Controls` and `IMotionDeviceBuilder.UsesTaskControl` add
the standard `Controls` relation. When a task control also has a
`TaskControlOperationType`, the controlled motion device receives the matching
`TaskControlReference`.

`IGearBuilder.WithPitch` accepts millimetres of linear travel per output-side
revolution. Pitch is a `BaseDataVariableType`; the builder does not add
EngineeringUnits.

Instantiate Robotics-typed objects with the generated
`ISystemContext.CreateInstanceOf<Type>` factories (for example
`CreateInstanceOfMotionDeviceSystemType`, `CreateInstanceOfMotionDeviceType`,
`CreateInstanceOfAxisType`, `CreateInstanceOfControllerType`) so each instance carries
the full companion-type structure rather than only a type-definition reference.

The default instance namespace is application-specific and can be changed with
`RoboticsServerOptions.InstanceNamespaceUri`. It must be distinct from the OPC
UA, DI, IA, Robotics, and all configured model-provider namespaces.

After a custom `DiNodeManager` has loaded the DI, IA, and Robotics models, call
`manager.CreateRoboticsBuildContext(options)` for direct configuration outside
the hosting pipeline. The helper validates the DeviceSet, Robotics model, and
configured instance namespace before returning the context. Its
`Context.NodeIdFactory` must implement `IRoboticsNodeIdFactory`; the allocator
must be thread-safe, reserve unique NodeIds for unregistered nodes, and allocate
Robotics instances in the configured instance namespace.

`AddRobotics()` owns the DI namespace and cannot be combined with
`AddOpcUaDi()`. Other companion-manager hosting extensions that load DI should
use the shared `DiAddressSpaceOwnership` marker to enforce the same rule.

## Robot Intent (draft)

The package also hosts the **draft** *OPC UA — Robot Intent* model. Robot Intent
adds task-level motion commands that OPC 40010 leaves undefined and represents
each submitted command as a Part 10 program lifecycle, so motion that takes
minutes outlives the `Call` that admitted it.

Use `AddRobotIntent()` to load the Robot Intent NodeSet and register the
DI-hosted controller services, `AddRobotIntentExecutor<TExecutor>()` to provide
the application executor, and `ConfigureRobotIntent(...)` to declare
controllers with the fluent builder. The builder exposes frames, fitted tools,
locations, axes, outputs, programs, safety state, kinematic description,
real-time channels, and truthful capability declarations through
`Accepts<TIntent>()`. For servers that want Robot Intent without OPC 40010 or
DI, `RobotIntentNodeManagerFactory` and `RobotIntentNodeManager` provide the
standalone NodeManager path.

Executors implement `IIntentExecutor`. The host owns admission, queueing,
state transitions, cancellation admission, and outcomes; the executor owns the
actual robot work and reports progress through `IIntentProgress`.

> The namespace `http://opcfoundation.org/UA/RobotIntent/` and every NodeId in
> it are **provisional**. The model is a working-group draft.

See the [Robotics developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Robotics.md),
the [Robot Intent guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Robotics.md#robot-intent),
and the [Dependency Injection guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/DependencyInjection.md).
