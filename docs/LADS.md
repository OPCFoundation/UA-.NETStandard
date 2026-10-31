# Laboratory and Analytical Devices (LADS)

The LADS packages implement
[OPC 30500-1 OPC UA for Laboratory and Analytical Devices](https://reference.opcfoundation.org/specs/OPC-30500-1)
(release 1.0.0, 2023-11-30). A LADS device is a DI device with functional units.
Each functional unit holds sensor and control functions, runs programs from
templates and publishes results with files. A device also carries Machinery
state and maintenance tasks.

| Package | What it adds |
| --- | --- |
| `Opc.Ua.LADS` | The information model: node states, the three DataTypes, identifiers and encodeables |
| `Opc.Ua.LADS.Server` | Device hosting on the Machinery node manager, a fluent device builder, the state machines, programs and results, maintenance tasks, and provider contracts for the hardware |
| `Opc.Ua.LADS.Client` | Device discovery, typed handles for devices, functional units and functions, program control, result file download and asynchronous value and state streams |

The [LADS sample](../samples/LADS/) runs a simulated shaking incubator and a
client that drives an incubation program end to end.

## Contents

- [Model and dependencies](#model-and-dependencies)
- [What the model package contains](#what-the-model-package-contains)
- [Loading the model](#loading-the-model)
- [Hosting devices](#hosting-devices)
  - [Coexisting with a server that already owns Machinery](#coexisting-with-a-server-that-already-owns-machinery)
  - [Placement](#placement)
  - [Direct composition](#direct-composition)
- [Building a device](#building-a-device)
  - [Functions](#functions)
  - [Programs](#programs)
  - [Maintenance tasks](#maintenance-tasks)
- [Driving the device at run time](#driving-the-device-at-run-time)
- [State machines](#state-machines)
- [Providers](#providers)
- [Results and result files](#results-and-result-files)
- [Conformance](#conformance)
- [Client](#client)
- [Sample](#sample)
- [MCP tools](#mcp-tools)
- [UA Lens](#ua-lens)
- [Provenance](#provenance)

## Model and dependencies

| Item | Value |
| --- | --- |
| Package | `OPCFoundation.NetStandard.Opc.Ua.LADS` |
| Model URI | `http://opcfoundation.org/UA/LADS/` |
| Model version | 1.0.0, published 2023-11-30 |
| Required models | OPC UA 1.05.02, [Device Integration](DeviceIntegration.md) 1.04.0, [Asset Management Basics](AMB.md) 1.01.0, [Machinery](Machinery.md) 1.03.0 (lower bounds) |
| Generated CLR namespace | `Opc.Ua.LADS` |
| Source | [UA-Nodeset `LADS`](https://github.com/OPCFoundation/UA-Nodeset/tree/latest/LADS) |

`LADSDeviceType` derives from the DI `DeviceType` and uses Machinery building
blocks and the AMB maintenance types. The package references `Opc.Ua.Di`,
`Opc.Ua.AMB` and `Opc.Ua.Machinery` and loads the versions the stack ships,
which are newer than the required ones and backward compatible with them.

## What the model package contains

- 53 ObjectTypes, among them `LADSDeviceType`, `FunctionalUnitType`, the
  function types (`AnalogControlFunctionType` and its totalizer, relative and
  composed variants, `MultiModeAnalogControlFunctionType`, the discrete
  controls, `TimerControlFunctionType`, the scalar, array, discrete and multi
  sensor types, `CoverFunctionType`), `ProgramManagerType`,
  `ProgramTemplateType`, `ResultType`, `MaintenanceTaskType` and the state
  machine types for the device, functional unit, control function and cover.
- The DataTypes `KeyValueType`, `SampleInfoType` and the enumeration
  `MaintenanceTaskResultEnum`.
- `LadsModel` (model identity, `GetNamespaceUris()`) and the
  `AddLadsTypeSystem` registration overloads.

## Loading the model

```csharp
using Opc.Ua;
using Opc.Ua.LADS;

// Encodeables (DI, IA, Machinery, AMB and LADS); the caller commits the builder.
messageContext.Factory.Builder.AddLadsTypeSystem().Commit();

// Type nodes for a node manager's predefined nodes.
var predefinedNodes = new NodeStateCollection();
predefinedNodes.AddLadsTypeSystem(systemContext);

// Typed node states for instances of the LADS types.
nodeStateFactory.AddLadsTypeSystem();
```

`AddLadsTypeSystem` loads DI, IA, Machinery, AMB and LADS in that order. It
registers missing namespaces and skips roots that are already present, so it
can be combined with other models that load the same dependencies.

## Hosting devices

LADS needs a Machinery node manager that owns DI and Machinery, and the Asset
Management sidecar that owns AMB. `AddLads` registers both and then the LADS
model. `ConfigureLads` adds devices once the node manager has loaded its
models.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.LADS.Server;

services.AddOpcUa()
    .AddServer<StandardServer>(server => { /* endpoints, security */ })
    .AddLads(options => options.MaxRetainedResults = 50)
    .ConfigureLads(context => context.AddDevice("Incubator", device => device
        .Identification(id =>
        {
            id.Manufacturer = new LocalizedText("OPC Foundation");
            id.Model = new LocalizedText("Shaking incubator");
            id.SerialNumber = "SN-0001";
        })
        .FunctionalUnit("Chamber", unit => unit
            .AnalogControl("Temperature", t =>
            {
                t.EngineeringUnits = LadsUnits.Celsius;
                t.Range = new Range(60, 0);
                t.InitialTargetValue = 37;
            })
            .AnalogScalarSensor("CO2", s => s.EngineeringUnits = LadsUnits.Percent))));
```

`LadsServerOptions` holds the run-time limits:

| Option | Default | Effect |
| --- | --- | --- |
| `InstanceNamespaceUri` | `urn:opcua-netstandard:lads:instances` | Namespace of the device instances in standalone mode |
| `MaxResultFileBytes` | 16 MiB | Largest result file clients can read through the `FileType` methods |
| `MaxRetainedResults` | 100 | Results kept per program manager; the oldest is removed when a new result exceeds the limit |

`Validate()` rejects a limit that is not positive and an invalid namespace, so
`AddLads` throws during registration rather than at server start.

`ConfigureLads` has a synchronous overload and one that takes
`Func<ILadsSetupContext, CancellationToken, ValueTask>`. Several calls run in
registration order. `AddDevice` returns the device handle immediately and
publishes the device before the callback completes. `AddDeviceAsync` awaits
the publication. A device name already used below `DeviceSet` is rejected with
`BadBrowseNameDuplicated`.

### Coexisting with a server that already owns Machinery

A server has one DI `DeviceSet`, and one node manager owns the DI namespace.
When the server already hosts Machinery, register the Machinery owner yourself
and add LADS to it with `AddLadsToMachinery`:

```csharp
services.AddOpcUa()
    .AddServer<StandardServer>(server => { })
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddAssetManagement()
    .AddLadsToMachinery()
    .ConfigureLads(context => { /* devices */ });
```

`AddLadsToMachinery` throws `InvalidOperationException` when no Machinery
owner is registered. It registers `AddAssetManagement` when that is missing,
so the order of `AddAssetManagement` and `AddMachinery` does not matter. The
Machinery owner must include the building blocks part, which
`LadsServerOptions.RequiredMachineryParts` names. A second `AddLads` or
`AddLadsToMachinery` throws `InvalidOperationException`.

`ConfigureLadsFor<TNodeManager>` targets a node manager derived from
`DiNodeManager` other than the stock Machinery node manager. It has the same
overloads as `ConfigureLads`.

### Placement

Every device is a `LADSDeviceType` instance below the DI `DeviceSet`, with an
`Organizes` reference from the Machinery `Machines` folder. The Machinery
client and the LADS client therefore both find the device. The LADS client
deduplicates the two paths by NodeId. Device identification, operation
counters and `MachineryItemState` are building blocks reached through
`HasAddIn`, as Machinery requires.

### Direct composition

Without dependency injection, call `LadsSetup.ConfigureAsync` with the
`IMachineryBuildContext` of a Machinery node manager:

```csharp
await LadsSetup.ConfigureAsync(buildContext, context =>
{
    context.AddDevice("Incubator", device => { /* ... */ });
}, cancellationToken);
```

The providers then come from the service provider of the build context, or
from the in-memory defaults when none are registered.

## Building a device

`ILadsDeviceBuilder` sets up a device:

| Method | Effect |
| --- | --- |
| `Identification(Action<LadsIdentification>)` | Manufacturer, model, serial number, revisions, product codes, asset id, component name and initial operation date |
| `Location(operational, hierarchical)` | The `OperationalLocation` and optional `HierarchicalLocation` |
| `InitialState(state, mode)` | Device state and Machinery operation mode after start; the default is `Operate` and `Processing` |
| `FunctionalUnit(name, configure)` | A functional unit with functions and an optional program manager |
| `MaintenanceTask(name, configure)` | A maintenance task in the device's maintenance set |
| `Component(name, identification)` | A component with its own identification |
| `Provider(ILadsDeviceProvider)` | A device provider for this device only |

`ILadsFunctionalUnitBuilder` adds functions (see below), `SupportedProperty(name)`
for the `SupportedPropertiesSet`, `Programs(configure)` for a program manager
and `FunctionProvider(provider)` for a function provider for this unit only.

### Functions

Each function builder method has an overload with an `out` parameter that
returns the typed handle:

| Builder method | Handle | Options |
| --- | --- | --- |
| `AnalogControl` | `IAnalogControlFunction` | `Kind` (`Plain`, `Totalizer`, `Relative`, `Composed`), units, range, initial target and current value, increase and decrease rate, composed targets |
| `MultiModeAnalogControl` | `IMultiModeAnalogControlFunction` | Modes, initial mode, units, range |
| `TwoStateDiscreteControl` | `ITwoStateDiscreteControlFunction` | True and false state names (default `On` and `Off`), initial value |
| `MultiStateDiscreteControl` | `IMultiStateDiscreteControlFunction` | State names, initial value |
| `Timer` | `ITimerFunction` | Units, range, initial target value |
| `AnalogScalarSensor` | `IAnalogScalarSensorFunction` | Units, range, initial value, compensation value |
| `AnalogArraySensor` | `IAnalogArraySensorFunction` | Units, range, initial values |
| `TwoStateDiscreteSensor` | `ITwoStateDiscreteSensorFunction` | State names, initial value |
| `MultiStateDiscreteSensor` | `IMultiStateDiscreteSensorFunction` | State names, initial value |
| `MultiSensor` | `IMultiSensorFunction` | Nested sensors |
| `Cover` | `ICoverFunction` | Initial state (default `Closed`), `Lockable` (default `true`) |

`LadsUnits` has the UNECE units used in the sample (`Celsius`,
`RevolutionsPerMinute`, `Percent`, `Second`, `Millisecond`, `PartsPerMillion`)
and `Create(code, symbol, description)` for others.

### Programs

`Programs` adds a program manager with templates. A template is a list of
steps. Each step has a duration and setpoints for control functions of the
same unit:

```csharp
unit.Programs(programs => programs
    .Template("Incubate", template => template
        .WithDescription(new LocalizedText("Heat, shake, hold."))
        .Step("Heat", TimeSpan.Zero,
            new LadsSetpoint("Temperature", Variant.From(37.0)),
            new LadsSetpoint("ShakerSpeed", Variant.From(200.0)))
        .Step("Hold", TimeSpan.FromHours(2))));
```

The template content that clients download and upload is the serialized step
list. `LadsProgramTemplate.ToContent()` writes it and
`LadsProgramTemplate.Parse(id, content)` reads it. Clients can upload new
templates through the `ProgramTemplateSet` `Upload` method. `Download` and
`Remove` are also bound.

### Maintenance tasks

`MaintenanceTask(name, options)` adds a `MaintenanceTaskType` instance: a
`MaintenanceRequiredAlarmType` condition with the AMB maintenance state machine
(`Planned`, `Executing`, `Finished`). The options set the message, recurrence
period, planned date and estimated downtime. `StartTask` moves the task to
`Executing`, `StopTask` records a `MaintenanceTaskResultEnum` result and the
last execution date and moves it to `Finished`, and `ResetTask` returns it to
`Planned`. The condition is active while the task is planned and inactive once
it is finished.

## Driving the device at run time

The handles returned by the builder, or by `ILadsDeviceRegistry`, connect the
device to the application. `ILadsDeviceRegistry` is a singleton in the service
collection. `WaitForDeviceAsync(name)` returns a device after
`ConfigureLads` publishes it.

```csharp
ILadsDevice incubator = await registry.WaitForDeviceAsync("Incubator", ct);
ILadsFunctionalUnit chamber = incubator.GetFunctionalUnit("Chamber");
IAnalogControlFunction temperature = chamber.GetFunction<IAnalogControlFunction>("Temperature");
IAnalogScalarSensorFunction co2 = chamber.GetFunction<IAnalogScalarSensorFunction>("CO2");

temperature.UpdateCurrentValue(36.4);   // a new reading from the heater
co2.UpdateValue(5.1);

string runId = await chamber.Programs!.StartProgramAsync(
    "Incubate", default, "job-17", "task-3", default, ct);
ILadsResult result = await chamber.Programs.WaitForResultAsync(runId, ct);
```

`GetFunctionalUnit`, `GetFunction<T>` and `GetMaintenanceTask` throw
`ServiceResultException` with `BadNotFound` for an unknown name.
`TryGetFunction<T>` returns `false` instead. A function nested in a multi
sensor is addressed as `Parent/Child`.

Device handles expose `State`, `OperationMode`, `ItemState`, the operation
counters, and `StateChanged` and `OperationModeChanged` events.
`IncrementOperationCycles()` counts a completed cycle. `PowerOnDuration` and
`OperationDuration` are computed when they are read.

## State machines

| State machine | States | Methods bound on the server |
| --- | --- | --- |
| Device state | `Initialization`, `Operate`, `Sleep`, `Shutdown` | `GotoOperate`, `GotoSleep`, `GotoShutdown` |
| Machinery operation mode | `Maintenance`, `Processing`, `Setup` and the others of Machinery | `GotoMaintenance`, `GotoProcessing`, `GotoSetup` |
| Machinery item state | `NotAvailable`, `OutOfService`, `NotExecuting`, `Executing` | none; derived from the device state |
| Functional unit state | `Stopped`, `Running`, `Stopping`, `Aborting`, `Aborted`, `Clearing` | `Start`, `StartProgram`, `Stop`, `Abort`, `Clear` |
| Running state (substate of `Running`) | `Idle`, `Starting`, `Execute`, `Completing`, `Complete`, and the hold and suspend states | none |
| Control function state | Same as the functional unit state | `Start`, `StartWithTargetValue`, `Stop`, `Abort`, `Clear` |
| Cover state | `Opened`, `Closing`, `Closed`, `Locking`, `Locked`, `Unlocking`, `Opening`, `Error` | `Open`, `Close`, `Lock`, `Unlock`, `Reset` |
| Maintenance state | `Planned`, `Executing`, `Finished` | `StartTask`, `StopTask`, `ResetTask` |

The item state follows the device: `Shutdown` maps to `OutOfService`,
`Operate` maps to `Executing` while a functional unit is running and to
`NotExecuting` otherwise, and every other state maps to `NotAvailable`.

A method that is not valid in the current state fails with
`BadInvalidState`. `GotoSleep` and `GotoShutdown` fail while a functional unit
is running. A provider can reject a transition with any bad status, which the
client receives as the method result.

## Providers

Three provider contracts connect the runtime to the hardware. Each is a
singleton in the service collection and `AddLads` registers an in-memory
default with `TryAdd`, so a registration made before or after `AddLads` wins.

| Contract | Called | Default |
| --- | --- | --- |
| `ILadsDeviceProvider` | Before a device state, operation mode or maintenance task change | `InMemoryLadsDeviceProvider` accepts every change |
| `ILadsFunctionProvider` | Before a function command (`LadsFunctionCommand` with `Function`, `Kind`, `Value`) is applied | `InMemoryLadsFunctionProvider` accepts every command; a new target value is reached at once |
| `ILadsProgramExecutor` | Once per program run | `InMemoryLadsProgramExecutor` runs the template steps in order, applies their setpoints, waits for their durations and writes a CSV protocol as result file |

Each callback returns a `ServiceResult`. A bad result rejects the change and
leaves the state unchanged. `ILadsDeviceBuilder.Provider` and
`ILadsFunctionalUnitBuilder.FunctionProvider` override the service for one
device or unit.

A program executor receives a `LadsProgramRun` with the run id, the template,
the properties, the supervisory job and task ids and the samples. It reports
progress through the run:

```csharp
public sealed class HplcExecutor : ILadsProgramExecutor
{
    public async ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken ct)
    {
        ArrayOf<LadsProgramStep> steps = run.Template.Steps;
        for (int i = 0; i < steps.Count; i++)
        {
            LadsProgramStep step = steps[i];
            run.ReportStep((uint)(i + 1), step.Name);
            for (int j = 0; j < step.Setpoints.Count; j++)
            {
                await run.ApplySetpointAsync(step.Setpoints[j], ct);
            }
            await run.DelayAsync(step.Duration, ct);
        }
        run.SetResultVariable("PeakCount", "4");
        run.AddResultFile("chromatogram.csv", "text/csv", await AcquireAsync(ct));
    }
}
```

The loops are indexed because `ArrayOf<T>` enumerates through a span, which
cannot cross an `await`.

The run completes when `ExecuteAsync` returns, with the outcome `Completed`.
`Stop` cancels the token and records the outcome `Stopped`. `Abort` cancels the
token and records `Aborted`. An exception from the executor also records
`Aborted`. `DelayAsync` waits on the `TimeProvider` from the service
collection, so tests can advance time with a fake provider.

## Results and result files

Each finished run adds a `ResultType` instance to the unit's `ResultSet` with
the run id, template id, start and stop times, properties, samples, supervisory
ids and the result files. Each result file is a `FileType` object opened
read-only through `IFileTypeBinder`. `MaxResultFileBytes` caps the size a
client can read. When the result set exceeds `MaxRetainedResults`, the oldest
result and its file bindings are removed. `ILadsProgramManager.RemoveResultAsync`
removes a result explicitly.

## Conformance

The server implements:

- The device, functional unit, function and program manager structure of
  OPC 30500-1, with the mandatory children of every instantiated type.
- All function types listed in [Functions](#functions), including nested
  multi sensors.
- Program templates (with `Download`, `Upload` and `Remove`), the active program
  with current step and progress, and results with `FileType` result files.
- Maintenance tasks as conditions with the AMB maintenance state machine.
- Machinery identification, item state, operation mode and operation counters
  as building blocks.

The following are not implemented by the server:

- The optional `Hold`, `Unhold`, `Suspend`, `Unsuspend`, `Reset` and
  `ToComplete` methods of the running state machine. The client has wrappers
  for them, for use with other servers.
- Persistence. Templates, results and maintenance history live in memory and
  are lost when the server restarts.

## Client

`Opc.Ua.LADS.Client` wraps the generated proxies with typed handles.

```csharp
using Opc.Ua.LADS.Client;

LadsClient lads = session.GetLadsClient(telemetry);   // or AddLadsClient() in DI
LadsDevice device = await lads.GetDeviceAsync("Incubator", ct);
LadsFunctionalUnit chamber = await device.GetFunctionalUnitAsync("Chamber", ct);

LadsFunction temperature = await chamber.GetFunctionAsync("Temperature", ct);
await temperature.StartAsync(Variant.From(37.0), ct);

string runId = await chamber.StartProgramAsync(
    LadsProgramRequest.For("Incubate")
        .WithProperty("Volume", "10")
        .WithSample("Plate-1", "S-1")
        .WithJob("job-17"),
    ct);

await foreach (LadsValueChange change in temperature.ObserveAsync(cancellationToken: ct))
{
    // change.Name is a LadsFunctionMembers constant, change.Value the DataValue
}

await chamber.WaitForStateAsync("Stopped", TimeSpan.FromHours(3), cancellationToken: ct);
LadsResult? result = await chamber.FindResultAsync(runId, ct);
foreach (LadsResultFile file in result!.Files)
{
    ByteString content = await lads.ReadResultFileAsync(file, ct);
}
```

Discovery browses `DeviceSet` and `Machines` for instances of
`LADSDeviceType` and its subtypes and deduplicates them by NodeId.
`GetFunctionsAsync` follows nested function sets and classifies each function
as a `LadsFunctionKind`. `LadsFunction.IsSensor` and `IsControl` derive from
that kind. A rejected method call throws `ServiceResultException` with the
server's status, and every asynchronous member honours cancellation.

The client keeps no state between calls. The handles hold only the client and a
NodeId, and every read or call goes to the server. `ReadDeviceAsync`,
`ReadFunctionalUnitAsync` and `ReadFunctionAsync` return immutable snapshots.
`ObserveAsync` and `ObserveStateAsync` use the streaming subscription passed in,
or the default streaming subscription of a `ManagedSession` when none is
passed. The monitored items live as long as the enumeration.

`LadsClientOptions` bounds every operation:

| Option | Default | Effect |
| --- | --- | --- |
| `MaximumBrowseReferences` | 4096 | References read per browse; exceeding it fails with `BadEncodingLimitsExceeded` |
| `MaximumBrowsePages` | 128 | Browse continuation pages per node |
| `MaximumFunctionDepth` | 8 | Nesting depth of function sets followed |
| `MaximumFileSize` | 64 MiB | Largest result file downloaded |
| `FileChunkSize` | 64 KiB | Bytes per `FileType.Read` call |
| `SamplingInterval` | 250 ms | Sampling interval of observed values |
| `QueueSize` | 10 | Server queue size of observed values |

`AddLadsClient()` registers a `LadsClientFactory` and a
`Func<CancellationToken, Task<LadsClient>>` on the managed session of the
client builder. The overloads take explicit options or a function that
modifies the defaults.

## MCP tools

The `OPCFoundation.NetStandard.Opc.Ua.Mcp.LADS` package adds the `lads` tool
profile to the aggregate MCP host. It discovers devices and reads device,
functional-unit, function, program-template, result and maintenance snapshots.
Separate tools can request device, functional-unit, function and maintenance
state-machine commands, start a published program template, and download a
result file beneath the host's configured transfer root.

For an embedded host, register the stack services and LADS catalog explicitly:

```csharp
services.AddOpcUaMcpCore()
    .AddOpcUaMcpLads();

services.AddMcpServer()
    .WithOpcUaLadsTools(McpToolProfile.Lads);
```

Commands are explicit requests; server authorization and state-machine
preconditions apply, and acceptance does not mean that a physical operation
has completed. File downloads use the MCP host's configured transfer policy.
See the [MCP LADS catalog](McpServer.md#lads-tools) for the complete tool list.

## UA Lens

The UaLens showcase includes an **LADS** companion view. It discovers LADS
devices, displays device and functional-unit state, function identities,
active-program progress and maintenance state, and exposes read-only program
template and maintenance tasks. Program starts and control-function commands
are prepared with typed inputs and require a separate run action. Result files
can be exported to a new local file; existing files are not overwritten.

## Sample

[`samples/LADS`](../samples/LADS/) contains a server that hosts a shaking
incubator (temperature and shaker speed controls, a CO2 sensor, a door cover, a
timer, two program templates and a calibration task) and a client that finds
the device, starts an incubation program, watches the temperature, CO2 and
shaker speed, waits for the run to finish and downloads the result file.

## Provenance

`src/Opc.Ua.LADS/Model` holds the unmodified NodeSet and identifier table from
UA-Nodeset revision `9f529354fc05a261295b4ce3e937a47410f7cced`
(tag `LADS-1.0.0-2023-11-30`). `provenance.json` records the source URLs and
SHA-256 hashes. NodeIds come from the NodeSet. The identifier table is the
source generator's drift guard.

The [LADS tests](../tests/Opc.Ua.LADS.Tests/) pin the file hashes, load the model
with its dependencies, round-trip the DataTypes through the binary, JSON and XML
encodings, and run hosted servers with the LADS client over a real session.

| Fixture | Covers |
| --- | --- |
| `LadsModelAssetTests` | File hashes, model identity and required models, generated identifiers against the NodeSet and the identifier table |
| `LadsHostingTests` | `AddLads` and `AddLadsToMachinery` in both registration orders, duplicate registration, option validation, provider defaults |
| `LadsAddressSpaceTests` | The mandatory children of devices, functional units, functions, program managers, templates and results |
| `LadsDeviceTests` | Device and operation mode state machines, item state, counters, providers rejecting transitions |
| `LadsFunctionTests` | Every function type, control and cover state machines, provider vetoes |
| `LadsProgramTests` | Template management, program runs, stop and abort, executor failures, results and result files |
| `LadsMaintenanceTaskTests` | The maintenance state machine, invalid transitions, the task methods, provider vetoes |
| `LadsClientServerTests` | Client discovery, snapshots, function and program control, file download and composition with Machinery over a real session |
