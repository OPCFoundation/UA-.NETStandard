# OPC UA Cranes and Hoists — server (preview)

OPC-40020-1 1.00 server support over the released generated CranesHoists,
Robotics, Machinery and DI models. This package is preview-only.

Use `AddCranesHoists().ConfigureCranesHoists(...)` for a standalone owner,
`AddCranesHoistsToMachinery()` for an existing Machinery owner, or
`AddCranesHoistsToRobotics()` and
`ConfigureCranesHoistsFor<RoboticsNodeManager>(...)` for an existing Robotics
owner. All paths use the same `CranesHoistsBuildContext` and awaited
`CraneSystemBuilder.BuildAsync`.

For example, after registering a per-system equipment-provider factory:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Server;

serverBuilder.AddCranesHoists().ConfigureCranesHoists(async (context, ct) =>
{
    ICraneDataProvider provider = context.Machinery
        .GetRequiredService<Func<string, ICraneDataProvider>>()("Crane1");

    await context.AddCraneSystem("Crane1")
        .WithProvider(provider)
        .WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Example manufacturer");
            id.SerialNumber = "Crane1";
            id.ProductInstanceUri = "urn:example:crane:1";
        })
        .AddController("Controller", controller =>
        {
            controller.AddSoftware("ControllerSoftware");
            controller.AddTaskControl("Main");
        })
        .AddSafetyState("Safety")
        .AddDevice("Hoist", CraneMotionDeviceCategoryEnum.HOIST, device =>
            device.WithIdentification(id =>
            {
                id.Manufacturer = new LocalizedText("Example manufacturer");
                id.SerialNumber = "Hoist1";
                id.ProductInstanceUri = "urn:example:hoist:1";
            }).AddAxis("Lift", axis => axis.AddMotor("LiftMotor")))
        .BuildAsync(ct);
});
```

The provider supplies the selected standard signals using their system-local
component IDs. This minimal example needs `Crane1` operating mode/SystemOn,
`Hoist` speed override, `LiftMotor` temperature, and `Safety` emergency-stop,
protective-stop and operating-mode samples. A `CraneSample` carries a complete
`DataValue`; bad/null samples explicitly represent unavailable acquisition.
Add position capability and optional facets only when the provider can report
their corresponding signals.

Supply per-system `ICraneDataProvider` implementations and complete Machinery
identification. Add controllers through the existing Robotics builder
(including software and task controls), safety states, crane devices, axes
and motors. Optional load, working-period, axis-function, moving-state,
brake, protection, functional-group, component and event facets are explicit.

Telemetry, including optional native load center-of-mass/inertia structures,
preserves provider quality and acquisition timestamps. A complete
report is validated before any values are published. Unavailable position
is `BadNoData`, not a fabricated zero. Limit requests require both an
`ICraneWriteAuthorizer` and an `ICraneLimitProvider`; acceptance does not
change provider-reported active limits. No crane motion or safety-bypass
methods are introduced.

`SimulatedCraneProvider` is a bounded, single-host reference adapter: it only
returns supplied samples, records explicitly accepted requests, and never
claims a physical effect. Real equipment and distributed state remain
application-provider responsibilities.

The generated root registers once through Robotics validation. Shared
Machinery identification retains its native nodes and adds discovery
references rather than adopting the tree again. Removing the root or shutting
down its owner drains runtime operations and owned resources. The application
owns borrowed providers; pass `ownsProvider: true` only when this runtime owns
that provider.
