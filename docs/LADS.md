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
- [Optional running lifecycle](#optional-running-lifecycle)
- [Providers](#providers)
- [Results and result files](#results-and-result-files)
- [Stored data and restart](#stored-data-and-restart)
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

`AddLadsTypeSystem` registers DI, IA, Machinery, AMB and LADS, then places each
type after any supertype present in the collection. It registers missing
namespaces and retains existing roots, so it can be combined with other models
that load the same dependencies.

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

`LadsServerOptions` configures runtime limits and providers:

| Option | Default | Effect |
| --- | --- | --- |
| `InstanceNamespaceUri` | `urn:opcua-netstandard:lads:instances` | Namespace of the device instances in standalone mode |
| `MaxResultFileBytes` | 16 MiB | Largest result file clients can read through the `FileType` methods |
| `MaxRetainedResults` | 100 | Results kept per program manager; the oldest is removed when a new result exceeds the limit |
| `Store` | Registered `ILadsStore`, or `InMemoryLadsStore` | Retains templates, completed results and file bytes, maintenance records and counters |
| `TimeProvider` | Registered `TimeProvider` | Overrides the LADS execution and counter clock without changing transport or session timing |

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
`RunningOperations()` selects the complete running lifecycle for that unit;
omitting it retains automatic stop after a successful program.

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

`TimerControlFunctionType` values are OPC UA `Duration` values in milliseconds.
The default engineering unit is `LadsUnits.Millisecond`. `CurrentValue`,
`TargetValue` and `DifferenceValue` use the same scale.

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
| Running state (substate of `Running`) | `Idle`, `Starting`, `Execute`, `Completing`, `Complete`, and the hold and suspend states | Optional `Hold`, `Unhold`, `Suspend`, `Unsuspend`, `Reset`, `ToComplete` |
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

## Optional running lifecycle

Select `RunningOperations()` on a functional unit to retain `Complete` after
a successful program. `Reset` moves through `Resetting` to `Idle`, from which
`Start` or `StartProgram` can begin the next process. `Stop` and `Abort` also
leave `Complete`. Applications that omit this option keep the existing
automatic-stop behavior.

```csharp
device.FunctionalUnit("Chamber", unit => unit
    .RunningOperations()
    .Programs(programs => programs
        .Template("Incubate", template => template
            .Step("Hold temperature", TimeSpan.FromMinutes(30)))));

ILadsRunningOperations running = chamber.RunningOperations!;
string runId = await chamber.Programs!.StartProgramAsync("Incubate", cancellationToken: ct);
await running.HoldAsync(ct);
await running.UnholdAsync(ct);
await running.SuspendAsync(ct);
await running.UnsuspendAsync(ct);
await running.ToCompleteAsync(ct);
ILadsResult result = await chamber.Programs.WaitForResultAsync(runId, ct);
await running.ResetAsync(ct);
```

The six methods are optional under OPC 30500-1 section 7.1.6. `Hold` addresses
internal conditions; `Suspend` addresses external conditions. Their acting
states are followed by `Held` or `Suspended` only after an
`ILadsControlledProgramExecutor` acknowledges the equipment operation.
The same direct methods let an application request recovery when its sensors
detect that a condition has cleared. The published graph permits Hold from
Starting, Execute, Suspending, Suspended, Unsuspending and Unholding.
The runtime rejects requests with no permitted transition.

The reference executor acknowledges a pause at an asynchronous processing
checkpoint. No subsequent step or setpoint is applied while paused, and a
step delay retains its remaining duration. Sample-preservation simulation
can continue independently. Hardware executors must implement
`ChangeExecutionAsync` and acknowledge their own physical actions; implementing
`ILadsProgramExecutor` alone does not enable these methods. A rejected or
failed callback leaves the acting state visible rather than reporting a
successful pause or resume. Stop, Abort and server shutdown cancel a pending
callback through its cancellation token.

`ToComplete` requests orderly completion by entering `Completing`; a fast
executor can reach `Complete` before the method returns. The request does not
cancel the run or itself publish a successful result. The reference executor
finishes its protocol, stops the running
control functions and writes its result. Hardware executors must define their
own orderly shutdown. Result finalization waits for `ExecuteAsync` to return
and for any pending `ToComplete` callback to acknowledge success. If that
callback fails after processing ends, the result records Aborted and no
successful cycle is counted.
Stop or Abort while paused uses the existing cancellation path and does not
count a successful cycle.

If an executor finishes while Held, Suspended or awaiting a pause/resume
acknowledgment, the runtime records Aborted rather than reporting successful
processing. Pending acknowledgments are cancelled. A new Start or Reset is
rejected until the previous callback has returned, so it cannot overlap the
next run.

`ActiveProgram.CurrentRuntime` and `CurrentStepRuntime` exclude acknowledged
pauses. `CurrentPauseTime` accumulates all Held and Suspended intervals for the
current run; it does not reset on resume. Result `TotalRuntime` includes pauses
and `TotalPauseTime` reports the accumulated pause duration. Operation duration
also uses elapsed time, so wall-clock corrections do not change its counter.
These values use
the injected `TimeProvider` and are computed when read. The NodeSet
descriptions define these distinct measures.

Method `Executable` follows the active functional-state nesting, the running
transition graph and the configured executor capability. Client
`GetRunningMethodsAsync` reads current method availability and user
executability. This snapshot does not reserve an operation. Use
`ObserveRunningStateAsync` or `WaitForRunningStateAsync` to distinguish an
accepted command from its stable state, and never retry an uncertain physical
command automatically.

Control functions select their optional submachine with
`EnableRunningOperations = true` on their builder options. Their function
provider must also implement `ILadsControlFunctionRunningProvider`.
`ILadsControlFunction.RunningOperations` then exposes the same six operations;
without the provider capability, equipment operations are not executable.
The callback receives the requested stable state, or `Completing` for orderly
completion, and must return only after the hardware operation is acknowledged.
The control function retains Complete until Reset, Stop or Abort.

## Providers

Three provider contracts connect the runtime to the hardware. Each is a
singleton in the service collection and `AddLads` registers an in-memory
default with `TryAdd`, so a registration made before or after `AddLads` wins.

| Contract | Called | Default |
| --- | --- | --- |
| `ILadsDeviceProvider` | Before a device state, operation mode or maintenance task change | `InMemoryLadsDeviceProvider` accepts every change |
| `ILadsFunctionProvider` | Before a function command (`LadsFunctionCommand` with `Function`, `Kind`, `Value`) is applied | `InMemoryLadsFunctionProvider` accepts every command; a new target value is reached at once |
| `ILadsProgramExecutor` | Once per program run | `InMemoryLadsProgramExecutor` runs the template steps in order, applies their setpoints, waits for their durations and writes a CSV protocol as result file |

Device and ordinary function callbacks return a `ServiceResult`. A bad result
rejects their change and leaves the state unchanged. Optional running callbacks
leave the acting state visible when acknowledgment fails.
`ILadsDeviceBuilder.Provider` and
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

The run completes when `ExecuteAsync` returns, with the outcome `Completed`,
unless a pending `ToComplete` still requires provider acknowledgment.
`Stop` cancels the token and records the outcome `Stopped`. `Abort` cancels the
token and records `Aborted`. The first accepted end request determines the
result until finalization starts. Finalization freezes that outcome before
changing states or saving the result. A later Stop or Abort waits for publication
and then applies its standalone functional-unit transition; it does not rewrite
the completed result or cancel a disposed execution token. Cancellation of a
pending completion acknowledgment preserves an accepted end request's outcome;
provider rejection without an end request records Aborted.
A non-cancellation executor exception, a rejected completion acknowledgment
without end-request cancellation, or an invalid completion state records
`Aborted`, even when Stop was accepted before finalization. Such failures take
precedence over the requested end outcome; cancellation caused by an accepted
Stop or Abort does not itself constitute an execution failure.
`DelayAsync` uses the runtime clock. Set
`LadsServerOptions.TimeProvider` to advance LADS execution in tests without
advancing session expiry; otherwise the runtime uses the registered
`TimeProvider`.

Program template content uses semicolon-delimited `step;name;durationMs` and
`set;function;value` records. Names containing semicolons, quotes or newlines
are quoted, and embedded quotes are doubled. Unquoted records remain readable.
The same representation is used by template upload/download and stored-data
recovery. Starting a program copies its recipe structurally, so subsequent
changes to the caller's template do not alter the active run or truncate its
step durations to the exchange format's integer milliseconds.

## Results and result files

Each finished run adds a `ResultType` instance to the unit's `ResultSet` with
the run id, template id, start and stop times, properties, samples, supervisory
ids and the result files. Each result file is a `FileType` object opened
read-only through `IFileTypeBinder`. `MaxResultFileBytes` caps the size a
client can read. When the result set exceeds `MaxRetainedResults`, the oldest
result and its file bindings are removed. `ILadsProgramManager.RemoveResultAsync`
removes a result explicitly.

## Stored data and restart

The default `InMemoryLadsStore` retains data only for its service-provider
lifetime. Select `FileSystemLadsStore` when completed work must survive a process
restart, or register an application implementation of `ILadsStore` before
`AddLads`. `LadsServerOptions.Store` overrides the registered provider and also
supports direct composition.

```csharp
var store = new FileSystemLadsStore(LocalFileSystem.Instance, @"D:\lads-data");
services.AddOpcUa()
    .AddServer<StandardServer>(server => { /* endpoints, security */ })
    .AddLads(options => options.Store = store)
    .ConfigureLads(context => context.AddDevice("Incubator", device => device
        .Identification(id => id.ProductInstanceUri = "urn:lab:incubator:SN-0001")
        .FunctionalUnit("Chamber", unit => unit.Programs(programs => programs
            .Template("Incubate", template => template
                .Step("Hold temperature", TimeSpan.FromMinutes(30)))))));
```

Each device snapshot contains its templates, completed results with their file
bytes, maintenance records and operation counters. A completed result and its
files are committed together before its result node or result event is
published. If the save fails, the caller waiting for the result receives the
error, no completed-cycle increment is applied and the functional unit enters
Aborted. The runtime does not substitute a memory provider after a storage
error.

The storage key is the configured `ProductInstanceUri`; if that is absent it is
the application namespace URI followed by `#` and the device name. Namespace
indexes and session NodeIds are not storage keys. Keep this identity stable
across restarts and use a distinct identity for each physical device.
Registration reserves the key before any load or save and rejects a second
device using the same key, including a configured URI equal to another device's
namespace-and-name fallback. A failed registration releases its name and storage
key only after its registered nodes have been removed. If cleanup leaves any
node registered, both reservations remain held until the runtime is restarted;
a different device cannot adopt the orphaned nodes or overwrite their snapshot.
The key is fixed for the registered device's lifetime; changing its
identification node does not move its stored data.
The file provider hashes it for the file name and stores a bounded, versioned
snapshot; malformed, unsupported or oversized snapshots prevent device
publication rather than silently discarding data.
The default snapshot limit is 64 MiB and can be set through the provider's
`maxBytes` constructor argument. A provider serializes its own writes but does
not coordinate separate processes; assign one writer to each directory.
The application owns an explicitly supplied provider and disposes it after
the server host.

Configured template content wins when a stored template has the same id.
Configured metadata also wins, except that an unspecified Created value retains
the stored date and an unspecified Modified value retains the stored date when
the content is unchanged.
Templates that exist only in storage are restored. An explicit
`RemoveTemplateAsync` is retained as a removal record, so a deleted configured
template does not reappear after restart; adding that id again clears the
removal record. Result deletion and `MaxRetainedResults` pruning remove the
corresponding files from the next committed snapshot.
Template additions and removals, result publication, explicit result deletion
and retention pruning save a prepared snapshot, update the address space, then
commit their in-memory records and counters. The device keeps other checkpoints
and program-data transactions excluded until publication or compensation
finishes. If node registration or removal throws, the runtime restores the
previous nodes, including generated child slots and result-file methods, and
snapshot before reporting the original error; a retry can
then repeat the operation without a duplicate template or missing result file.
Removed file bindings are released only after the removal commits.

If either node compensation or snapshot restoration fails, the runtime reports
all errors, sets the device state to Shutdown and rejects further mutations and
saves. Restart the device after correcting the underlying fault. Recovery uses
the last snapshot the provider successfully retained and rebuilds its nodes;
the failed call does not establish whether the new or previous snapshot remains
durable. A file-binding cleanup error after committed removal is reported
without reversing the removal.
The device orders quarantine against its concrete state and value operations.
Each operation checks the retained failure and writes its attributes while the
device's private lock is held; callers cannot supply code to run under that
lock. Transition guards and hardware providers run before the commit, outside
the lock. A provider acknowledgement or transition guard returning after
quarantine therefore cannot commit another local write, including a transition
out of Shutdown. Node change notifications, state observers and maintenance
events are dispatched after the lock is released, so their handlers can update
a counter or another function from a different thread without waiting for that
lock. Commands already waiting for their command gate also recheck the device
on entry. The runtime cannot reverse
physical actions a provider has already performed.

Before calling an executor, the runtime stores its run id as an interrupted-run
marker. Completion replaces that marker with the result in one snapshot.
Restart restores records and creates new address-space nodes, but never calls
an executor, reapplies a setpoint or resumes equipment. The program manager's
`InterruptedRunId` reports the most recently recovered interruption to the
application; that record remains stored after subsequent successful runs.
Maintenance that was Executing returns to Planned; its previous completion
record remains available.
Device and function operating states always come from the configured startup
policy, not from stored physical state.

Program and maintenance operations checkpoint automatically. Call
`ILadsDevice.SaveStoredDataAsync` after application-managed counter changes
that must be durable immediately. Graceful shutdown also checkpoints, including
an interrupted-run marker, before releasing the runtime.
Checkpoint and cleanup errors are preserved together. Cleanup still attempts
every functional unit, result file and maintenance task, and releases owned
gates when an earlier owner fails. Repeated disposal observes the same completion
or error without attempting those releases again.
Cleanup still cancels and drains an active executor on a quarantined device,
but does not rewrite its unit state or checkpoint its interrupted execution.
Recovery uses the interruption record retained before execution began.
If maintenance hardware has already acknowledged an action when its checkpoint
fails, the storage error is reported without reversing the physical action.
Inspect the equipment state before retrying that command.

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

Running operations require explicit configuration and an executor or hardware
provider that supports them. Optional operations are not universally required
for every LADS device. The Complete state and its Reset graph use the published
model; the ToComplete prose's reference to "Completed" does not introduce an
additional state.

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
are prepared with typed inputs. They require a signed and encrypted connection,
an authorizing deployment policy, and confirmation before the separate run
action. Result files can be exported to a new local file; existing files are
not overwritten.

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
| `LadsRunningOperationsTests` | Opt-in lifecycle, cooperative pauses, acting-state failures, cancellation and client method discovery |
| `LadsStoredDataTests` | Restarted templates, results/files, maintenance and counters, deletion/retention, interrupted execution and storage errors |
| `LadsMaintenanceTaskTests` | The maintenance state machine, invalid transitions, the task methods, provider vetoes |
| `LadsClientServerTests` | Client discovery, snapshots, function and program control, file download and composition with Machinery over a real session |
