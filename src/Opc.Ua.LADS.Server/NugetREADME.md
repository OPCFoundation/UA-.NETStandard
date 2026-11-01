# OPC UA LADS server hosting

`OPCFoundation.NetStandard.Opc.Ua.LADS.Server` hosts OPC 30500-1 laboratory and
analytical devices on the Machinery node manager. It builds devices,
functional units and functions with a fluent API, runs the LADS state machines,
executes programs and keeps their results. Providers connect the devices to
hardware.

```csharp
services.AddOpcUa()
    .AddServer<StandardServer>(server => { /* endpoints, security */ })
    .AddLads()
    .ConfigureLads(context => context.AddDevice("Incubator", device => device
        .Identification(id => id.SerialNumber = "SN-0001")
        .FunctionalUnit("Chamber", unit => unit
            .AnalogControl("Temperature", t => t.InitialTargetValue = 37)
            .AnalogScalarSensor("CO2", s => s.EngineeringUnits = LadsUnits.Percent))));
```

`AddLads` registers a Machinery node manager, the Asset Management sidecar and
the LADS model. When the server already hosts Machinery, use
`AddLadsToMachinery` instead so LADS shares the existing `DeviceSet`:

```csharp
services.AddOpcUa()
    .AddServer<StandardServer>(server => { })
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddLadsToMachinery()
    .ConfigureLads(context => { /* devices */ });
```

Each device is a `LADSDeviceType` instance below the DI `DeviceSet`, organized
from the Machinery `Machines` folder. Without dependency injection,
`LadsSetup.ConfigureAsync` composes devices into the build context of a
Machinery node manager.

## What a device supports

- The device, operation mode, functional unit, control function, cover and
  maintenance task state machines, with their methods bound.
- Analog, multi-mode, discrete and timer control functions; scalar, array,
  discrete and multi sensors; covers.
- Program templates, program runs with steps and setpoints, results with
  variables and result files that clients read through `FileType`.
- `MachineryItemState` derived from the device and functional unit states.
- Run-time handles that update sensor and control values and drive the state
  machines from application code.

## Providers

`ILadsDeviceProvider` and `ILadsFunctionProvider` can veto a transition or
execute it against hardware. An `ILadsProgramExecutor` receives a
`LadsProgramRun` that reports steps, applies setpoints and adds result variables
and files. In-memory providers are registered by default, so a device works
without hardware.

`LadsServerOptions` limits the result file size and the number of retained
results.

See the [LADS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/LADS.md).