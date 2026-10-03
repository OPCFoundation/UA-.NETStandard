# OPC UA for Machinery (OPC 40001)

The `Opc.Ua.Machinery*` packages implement the OPC 40001 specification series —
the building blocks every machine shares, plus the four specialised parts that
production equipment adds depending on its use case.

## Contents

- [Library layout](#library-layout)
- [Models and namespaces](#models-and-namespaces)
- [Dependency specifications](#dependency-specifications)
- [Quick start — server](#quick-start--server)
- [Quick start — client](#quick-start--client)
- [Server hosting model](#server-hosting-model)
- [Coexisting with a server that already owns DI or ISA-95](#coexisting-with-a-server-that-already-owns-di-or-isa-95)
- [Builder reference](#builder-reference)
  - [OPC 40001-1 building blocks](#opc-40001-1-building-blocks)
  - [OPC 40001-2 process values](#opc-40001-2-process-values)
  - [OPC 40001-3 job management](#opc-40001-3-job-management)
  - [OPC 40001-4 energy](#opc-40001-4-energy)
  - [OPC 40001-101 results](#opc-40001-101-results)
- [Abstract event types](#abstract-event-types)
- [State machines are server-driven](#state-machines-are-server-driven)
- [Client](#client)
- [Conformance matrix](#conformance-matrix)
- [Model sources, identifier tables and repairs](#model-sources-identifier-tables-and-repairs)
- [Generator gaps found while adding these models](#generator-gaps-found-while-adding-these-models)

## Library layout

Each part is its own model assembly, so a server takes only what it serves. A
machine tool that reports jobs and results does not have to carry PADIM.

| Package | Specification | Model URI |
| --- | --- | --- |
| `Opc.Ua.Machinery` | OPC 40001-1 Basic Building Blocks | `http://opcfoundation.org/UA/Machinery/` |
| `Opc.Ua.Machinery.ProcessValues` | OPC 40001-2 Process Values | `…/UA/Machinery/ProcessValues/` |
| `Opc.Ua.Machinery.Jobs` | OPC 40001-3 Job Management | `…/UA/Machinery/Jobs/` |
| `Opc.Ua.Machinery.Energy` | OPC 40001-4 Energy Management | `…/UA/Machinery/Energy/` |
| `Opc.Ua.Machinery.Result` | OPC 40001-101 Result Transfer | `…/UA/Machinery/Result/` |

Two more packages turn those models into a running server and a client:

| Package | What it adds |
| --- | --- |
| `Opc.Ua.Machinery.Server` | `MachineryNodeManager`, the fluent machine builder, the state-machine controllers, the OPC 40001-101 download path, and the `AddMachinery()` hosting pipeline |
| `Opc.Ua.Machinery.Client` | `MachineryClient` — discovery from the `Machines` folder, identification, state observation, job control and result download |

## Models and namespaces

The generated C# namespace of each model is derived from its model URI by
`NodeSetToModelDesign.CreateNamespace`, and every project pins the derived value
explicitly — it is part of the contract each assembly publishes through
`[assembly: ModelDependencyAttribute]` (see [ModelDependencies.md](ModelDependencies.md)).

| Model URI | C# namespace | `Namespaces` member | Model loader |
| --- | --- | --- | --- |
| `…/UA/Machinery/` | `Opc.Ua.Machinery` | `Machinery` | `AddOpcUaMachinery` |
| `…/UA/Machinery/ProcessValues/` | `Opc.Ua.Machinery.ProcessValues` | `MachineryProcessValues` | `AddOpcUaMachineryProcessValues` |
| `…/UA/Machinery/Jobs/` | `Opc.Ua.Machinery.Jobs` | `MachineryJobs` | `AddOpcUaMachineryJobs` |
| `…/UA/Machinery/Energy/` | `Opc.Ua.Machinery.Energy` | `MachineryEnergy` | `AddOpcUaMachineryEnergy` |
| `…/UA/Machinery/Result/` | `Opc.Ua.Machinery.Result` | `MachineryResult` | `AddOpcUaMachineryResult` |
| `…/UA/IA/` | `Opc.Ua.IA` | `IA` | `AddOpcUaIA` |
| `…/UA/ECM/` | `Opc.Ua.ECM` | `ECM` | `AddOpcUaECM` |
| `…/UA/PADIM/` | `Opc.Ua.PADIM` | `PADIM` | `AddOpcUaPADIM` |
| `…/UA/Dictionary/IRDI` | `Opc.Ua.IRDI` | `IRDI` | `AddOpcUaIRDI` |

Note on placement: the `Machines` folder (`FolderType`) that OPC 40001-1 defines
is `Organizes`-referenced from `Objects`, **not** from the DI `DeviceSet`. There
is no `MachineryItems` folder in 1.04.1, and no `MachineryBuildingBlocksType` —
the building blocks are AddIn instances (`Identification`, `Monitoring`,
`Components`, `MachineryEquipment`, `Notifications`).

## Dependency specifications

Adding OPC 40001 pulled in three specifications the repository did not have, and
moved a fourth out of `Opc.Ua.Robotics`:

| Package | Specification | Why |
| --- | --- | --- |
| `Opc.Ua.IA` | OPC 10000-200 Industrial Automation | 40001-1 types `MonitoringType.Status.Stacklight` with IA's `BasicStacklightType`. IA used to be generated inside `Opc.Ua.Robotics`; two assemblies generating the same model URI under the same C# prefix collide at the consumer, so it moved to its own package and Robotics now references it. The generated types keep living in the `Opc.Ua.IA` namespace and `AddOpcUaIA` is unchanged. |
| `Opc.Ua.PADIM` | OPC 30081 PADIM + the IRDI dictionary | 40001-2 derives `ProcessValueType` / `ProcessValueVariableType` from PADIM's `AnalogSignalType` / `AnalogSignalVariableType`. IRDI ships in the same assembly because PADIM references it several hundred times and is unusable without it. |
| `Opc.Ua.ECM` | OPC 34100 Energy Consumption Management | 40001-4's energy carriers are instances of ECM's `EnergyMeasurementValueType`. |
| `Opc.Ua.ISA95` *(existing)* | OPC 10031-4 ISA-95 Job Control V2 | 40001-3's `JobManagementType` composes the Job Control V2 receiver and response provider. |

The resulting reference graph:

```
Opc.Ua.Core
 └── Opc.Ua.Di ──┬── Opc.Ua.IA ──┬── Opc.Ua.Robotics
                 │               ├── Opc.Ua.ECM ─── Opc.Ua.Machinery.Energy
                 │               └── Opc.Ua.Machinery
                 └── Opc.Ua.PADIM (+ IRDI) ─── Opc.Ua.Machinery.ProcessValues
Opc.Ua.Core ─── Opc.Ua.Machinery.Result
Opc.Ua.ISA95 ─── Opc.Ua.Machinery.Jobs
```

## Quick start — server

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MachineryServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:MachineryServer";
        options.EndpointUrls.Add("opc.tcp://localhost:62546/MachineryServer");
    })
    .AddMachinery(options => options.Parts = MachineryParts.All)
    .ConfigureMachinery(async machinery =>
    {
        IMachineHandle<BaseObjectState> press = await machinery
            .AddMachine("Press-1")
            .WithIdentification(id =>
            {
                id.Manufacturer = new LocalizedText("Acme");
                id.Model = new LocalizedText("P-500");
                id.SerialNumber = "SN-0001";
                id.ProductInstanceUri = "urn:acme:press:1";
            })
            .WithMonitoring(monitoring => monitoring
                .WithMachineryItemState()
                .WithOperationMode())
            .BuildAsync();

        // The handle is how the application drives the machine afterwards.
        await press.ItemState!.SetStateAsync(MachineryItemStateValue.NotExecuting);
    });
```

Nothing reaches the address space until `BuildAsync` runs, and a failure
anywhere in the chain rolls the machine back — the server is never left with
half a machine.

`MachineryParts` decides which models the manager loads, which namespaces it
announces and which conformance units it can advertise. Trim it so the server
carries only what it serves:

| Value | Needs |
| --- | --- |
| `BuildingBlocks` | DI + IA |
| `ProcessValues` | `BuildingBlocks` + PADIM + IRDI |
| `Jobs` | ISA-95 Job Control V2 only — **no DI** |
| `Energy` | `BuildingBlocks` + ECM |
| `Result` | UA core only — **no DI, no machine model** |

A server that publishes nothing but measurement results skips the machine model
entirely:

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(/* … */)
    .AddMachineryResults(options => options.RootBrowseName = "ResultManagement");
```

That registers [`MachineryResultNodeManager`](../src/Opc.Ua.Machinery.Server/MachineryResultNodeManager.cs),
which shares its method binder with the `ResultManagement` object below a
machine, so both answer a client identically.

## Quick start — client

```csharp
MachineryClient machinery = session.Machinery(telemetry);

await foreach (MachineEntry machine in machinery.EnumerateMachinesAsync(ct))
{
    MachineIdentification? id = await machinery
        .ReadIdentificationAsync(machine.NodeId, ct);
    FiniteStateSnapshot? state = await machinery
        .GetItemStateAsync(machine.NodeId, ct);

    Console.WriteLine(
        $"{id?.Manufacturer.Text} {id?.Model.Text} — {state?.CurrentState.Text}");
}
```

Every accessor returns `null` rather than throwing when a machine does not
publish the block: OPC 40001-1 makes nearly everything optional, so a client
that insists on a block would work against one vendor and fail against the next.

## Server hosting model

`AddMachinery()` registers `MachineryNodeManager`, a `DiNodeManager` subclass —
the same shape `RoboticsNodeManager` has. One manager serves every configured
part, because the parts share one instance tree: a machine's process values,
jobs, energy carriers and results all hang off the same object.

`ConfigureMachinery(...)` runs during server startup, after the models are
loaded and the address space is wired. Three overloads take a delegate
(sync, async, async with token) and one takes an `IMachineryConfigurator`
class, which is the one to reach for when the configuration needs services
from the container.

Instances are minted into an application-owned namespace,
`MachineryServerOptions.InstanceNamespaceUri`
(default `urn:opcua-netstandard:machinery:instances`). It must not be a model
namespace, and the options validate that.

## Coexisting with a server that already owns DI or ISA-95

`AddMachinery()` claims the Device Integration address space, and — when the
`Jobs` part is selected — the ISA-95 namespaces as well. A second registration
that would load the same models fails immediately with a message naming the
current owner, rather than producing a server with two `DeviceSet` trees.

A server that already owns either one loads the Machinery models into its own
manager and drives them through the same builder:

```csharp
protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
    ISystemContext context, CancellationToken ct = default)
{
    var nodes = new NodeStateCollection();
    nodes.AddMachineryTypeSystem(context, MachineryParts.BuildingBlocks);
    nodes.AddOpcUaPumps(context);
    return new ValueTask<NodeStateCollection>(nodes);
}
```

```csharp
builder.ConfigureMachineryFor<PumpNodeManager>(async machinery =>
{
    await machinery.AddMachine(pump, new QualifiedName("Pump-1"))
        .WithIdentification(/* … */)
        .BuildAsync();
});
```

`AddMachine<TState>(TState machine, …)` adopts an instance a companion
specification created — an OPC 40223 `PumpState`, an OPC 40223-style
`GeneratorSetState` — so the vendor model keeps its own type definition and
still answers the Machinery browse paths. The custom manager only has to expose
an `IMachineryNodeIdFactory`; `MachineryNodeManager` implements it, and a
custom manager implements it with a thread-safe allocator for the configured
instance namespace.

## Builder reference

### OPC 40001-1 building blocks

Every block is attached to its machinery item with `HasAddIn`, which is what
OPC 40001-1 uses — not `HasComponent`.

| Call | What it adds |
| --- | --- |
| `WithIdentification(…)` | `Identification`, typed `MachineIdentificationType`. The browse name comes from the DI namespace because the type derives from DI's `FunctionalGroupType`. `Manufacturer`, `SerialNumber` and (for a machine) `ProductInstanceUri` are mandatory and validated. |
| `WithMonitoring(…)` | `Monitoring`, and below it `Status/MachineryItemState`, `Status/MachineryOperationMode`, `Status/Stacklight` (OPC 10000-200 IA), `Health`, `Process`, `Consumption` — each only when asked for |
| `WithComponents(…)` | `Components`, and one `AddComponent` per machine component. A component gets the narrower `MachineryComponentIdentificationType`. |
| `WithMachineryEquipment(…)` | `MachineryEquipment` and the equipment below it. `MachineryEquipmentTypeId` is mandatory and required by the call; `WithEquipmentLife(…)` adds the DI `LifetimeVariableType` the equipment interface declares. |
| `WithNotifications(…)` | `Notifications`, with `EventNotifier` set, registered as a root notifier, and a publisher the application reports its own events on |
| `WithOperationCounters(…)` | `OperationCounters` — `PowerOnDuration`, `OperationDuration`, `OperationCycleCounter` |
| `WithLifetimeCounters(…)` | `LifetimeCounters` and one DI `LifetimeVariableType` per `AddLifetimeVariable` |

#### The `MachineryBuildingBlocks` organizer

OPC 40001-1 §7.1 puts the building blocks below a `FolderType` object named
`MachineryBuildingBlocks` (Machinery namespace), referenced from the machinery
item with `HasComponent`, and the blocks themselves with `HasAddIn` from that
folder. Table 12 of the specification says the item state, the operation mode,
both counters, monitoring, equipment and notifications **shall** be reachable
that way, and identification and the components folder **may** be.

This is not presentation. Every conformance unit for those blocks is worded
*"has this AddIn under its MachineryBuildingBlocks folder"*, and OPC 40001-3's
`Machinery Job Management Base` is worded the same way — so a server without
the folder satisfies none of them. The builder creates it on first use and
references every block it builds; §7.3 forbids duplicating the node, so the
second path is a reference to the same node, not a copy. The blocks keep their
direct `HasAddIn` from the machinery item as well, which §7.1 recommends as the
top-level entry point.

There is no `MachineryItems` folder and no `MachineryBuildingBlocksType` in
1.04.1 — both appear in older drafts and in reduced NodeSet copies. The folder
has no type of its own: it is a plain `FolderType` identified by its browse
name, which is why the builder assembles it rather than a generated factory.
The `Machines` folder is `Organizes`-referenced from `Objects`.

#### Default instance browse names are load-bearing

Every OPC 40001-1 conformance unit is worded *"using the
DefaultInstanceBrowseName"*, so the namespace a block's browse name sits in is
part of the contract, and it is not always the Machinery one:
`MachineryOperationCounterType` declares `2:OperationCounters` — the Device
Integration namespace — because OPC 40001-1 reuses the OPC 10000-100 functional
group so a client written against Device Integration finds it.
`MachineryItemIdentificationType` and its two subtypes do the same with
`2:Identification`. `MachineryNodeSetConformanceTests` pins all ten values
against the vendored NodeSet.

### OPC 40001-2 process values

```csharp
.WithProcessValue(new QualifiedName("OilTemperature"), pv => pv
    .WithEngineeringUnits(degreeCelsius, new Range { Low = -20, High = 120 })
    .WithLimits(lowLow: 5, low: 15, high: 65, highHigh: 80)
    .WithSetpoint(45)
    .WithDeviationLimits(low: -10, high: 10)
    .WithPercentageValue()
    .WithStatus()
    .WithLimitAlarm()
    .WithDeviationAlarm()
    .WithZeroPointAdjustment(async (processValue, ct) => await ZeroAsync(ct))
    .WithValue(45)
    .Bind(out IProcessValueHandle oilTemperature))
```

The application drives the value through the handle afterwards, and everything
derived from it follows:

```csharp
await oilTemperature.SetValueAsync(70.0);   // percentage, both alarms
await oilTemperature.SetSetpointAsync(55);  // deviation alarm
```

`WithLimitAlarm()` and `WithDeviationAlarm()` do more than create the objects
the model declares. Each one mirrors the limits onto the alarm — absolute
limits from `WithLimits`, deviations from `WithDeviationLimits` — points the
condition at the signal as its `InputNode`, gives it the source, name and
enable state OPC 10000-9 needs, registers it as a root notifier, and
re-evaluates it on every `SetValueAsync`. An alarm that is only created never
activates and never reports, which is what the objects alone amount to.

`WithZeroPointAdjustment` binds the optional OPC 30081 method and reports a
`ZeroPointAdjustmentEventType` event on **every** call — the part's conformance
unit says all instances supporting the method do — with the handler deciding
only the `ZeroPointAdjustmentResult` status the event carries. Because that
event type is abstract, the event is reported with a concrete subtype; see
[Abstract event types](#abstract-event-types).

`ProcessValueType` derives from PADIM's `AnalogSignalType`, and its mandatory
`AnalogSignal` child keeps PADIM's `AnalogSignalVariableType` as its declared
type — OPC 40001-2 does **not** narrow it. What the part adds is
`ProcessValueVariableType`, a *subtype* of that variable type carrying
`LowLowLimit` / `LowLimit` / `HighLimit` / `HighHighLimit` and
`PercentageValue`. An instance may use a subtype in a declared slot, so the
builder puts the richer type there and the limits become reachable. The
generated factory alone would leave the PADIM type in place and
`WithLimits(...)` would have nowhere to write.

Substituting the subtype costs one repair. Two PADIM members the derived types
inherit used to come out of the generator without their reference type — and,
for `EngineeringUnits`, without its namespace-0 browse name — when
`ProcessValueVariableType` is instantiated on its own, which is exactly the path
the substitution takes. A node whose `ReferenceTypeId` is null is in the node
tree but produces no reference in a filtered Browse, so no client ever sees it:
`SignalTag` is mandatory on PADIM's `AnalogSignalType`, and `EngineeringUnits`
is where every Data Access client reads the unit — and what the
`0:Data Access AnalogUnitType` unit of the OPC 40001-2 base facet rests on.
The generator carries both through the dependency payload now; see
[Generator gaps](#generator-gaps-found-while-adding-these-models).

### OPC 40001-3 job management

```csharp
.WithJobManagement(jobs => jobs
    .WithJobOrderReceiver(provider)
    .WithJobResponseProvider(provider)
    .WithJobOrderCatalog(provider))
```

`JobManagementType` has exactly two mandatory children: `JobOrderControl`, an
ISA-95 `JobOrderReceiverObjectType`, and `JobOrderResults`, an ISA-95
`JobResponseProviderObjectType`. The eleven job verbs — `Store`,
`StoreAndStart`, `Start`, `Stop`, `Abort`, `Pause`, `Resume`, `Clear`,
`Cancel`, `Update`, `RevokeStart` — belong to the ISA-95 type, not to
OPC 40001-3, and are bound by the shared
[`Isa95JobControlV2Binder`](../src/Opc.Ua.ISA95.Server/Providers/Isa95JobControlV2Binder.cs)
that `Isa95NodeManager` uses. A client therefore sees the same verbs with the
same semantics whether it reaches them through a stand-alone ISA-95 root or
through a machine.

When no provider is supplied explicitly the builder resolves
`IIsa95JobOrderReceiverV2` from the application services, so
`AddInMemoryIsa95JobControlProvider()` is enough for a demo server.

`MachineryEndToEndTests.JobControlPausesResumesAndRevokesStartThroughAMachineAsync`
drives `Pause`, `Resume` and `RevokeStart` through a machine over `opc.tcp`,
both the accepted transitions and the refused ones (revoking a running order,
pausing one that is not running, resuming one that is), and checks the state
the published `JobOrderList` reports after each.

`JobOrderList` and `JobOrderResponseList` are snapshots the builder keeps
current from the provider's change streams, because a machine changes its
orders by routes that never pass an OPC UA method: `IIsa95JobStatusSourceV2`
and `IIsa95JobOrderCatalogChangeSource` refresh both lists, and
`IIsa95JobResponseChangeSource` refreshes the response list when the machine
reports a response while the order keeps running — progress such as
`RunsCompleted`, which changes neither the order's state nor the catalog.
`InMemoryIsa95JobControlProvider` implements all three.

### OPC 40001-4 energy

```csharp
.WithEnergy(energy => energy
    .AddResource(MachineryEnergyCarrier.CompressedAir, air =>
    {
        air.Main
            .WithApplicationTag("press/CompressedAir")
            .WithNonElectricalEnergy(importHighPrecision: 0, exportHighPrecision: 0)
            .WithVolumeFlow(volume: 184_320f, volumeFlowRate: 18.4f)
            .WithBaseFlow(pressure: 620_000f, temperature: 294.65f);

        air.AddMeteringPoint(
            new QualifiedName("ClampingCylinders"),
            point => point.WithNonElectricalEnergy(0, 0));
    }))
```

OPC 40001-4 is an extension of the OPC 40001-1 `Monitoring` building block
rather than a block of its own (§6.1), and its structure is three levels deep:

```
Monitoring
  Consumption                    ← OPC 40001-1 MonitoringType
    CompressedAir                ← well-known FolderType browse name, §9.1
      Main                       ← well-known EnergyMeasurementType name, §9.2
      ClampingCylinders          ← further metering points
```

A *metering point* is an OPC 34100 `EnergyMeasurementType` object that
*implements* `INonElectricalEnergyType` and the flow interfaces; the interfaces
are abstract, so nothing is ever an instance of them. `WithEnergy` materialises
the monitoring add-in and the `Consumption` folder when the machine does not
already publish them, and `AddResource` always creates `Main` — the
`Machinery Energy Main grouping` unit requires one for *each* resource folder,
so a resource without it would make the server non-conformant for every other
resource too.

`Contains` (§8.1) runs **from** the main metering point **to** a sub-meter whose
readings are part of it. Both ends must be `EnergyMeasurementType` objects,
which is why it never points at a resource folder or at the same-named Object
the Energy model carries — those Objects exist only to publish the well-known
browse names.

Electricity is the one resource OPC 40001-4 describes with no interfaces of its
own: §6.5 defers it to OPC 34100's energy profiles. `WithInterface` and
`AddMeasurementValue` are how a metering point takes one:

```csharp
.AddResource(MachineryEnergyCarrier.Electricity, electricity =>
    electricity.Main
        .WithApplicationTag("press/Electricity")
        .WithInterface(NodeId.Create(
            Opc.Ua.ECM.ObjectTypes.IEnergyProfileE1Type,
            Opc.Ua.ECM.Namespaces.ECM,
            context.Context.NamespaceUris))
        .AddMeasurementValue(
            new QualifiedName(Opc.Ua.ECM.BrowseNames.AcActivePowerTotal),
            Variant.From(41_250.0f),
            watt))
```

The units OPC 40001-4 pins are the UNECE Recommendation 20 codes from its
attribute tables and are applied by the builder: watt hours for the two energy
readings, pascal for `Pressure`, kelvin for `Temperature`.

### OPC 40001-101 results

```csharp
.WithResultManagement(results => results
    .WithInMemoryStore(capacity: 32)
    .WithResultsFolder(publishedResults: 4)
    .WithFileTransfer())
```

All five methods of `ResultManagementType` are optional; the builder publishes
`GetLatestResult`, `GetResultById`, `GetResultIdListFiltered`,
`AcknowledgeResults` and `ReleaseResultHandle` once a store is bound, and the
`ResultTransfer` object only when downloads are enabled.

#### Bringing your own store

Substitute a real store by implementing
[`IMachineryResultStore`](../src/Opc.Ua.Machinery.Server/Results/MachineryResult.cs)
and passing it to `WithStore(...)`, or by registering it in the container. The
machine's result publisher (`IMachineHandle.Results`, and `Publisher` on the
stand-alone `MachineryResultNodeManager`) works with any store:

- a store that also implements `IWritableMachineryResultStore` receives every
  published result through `AddResultAsync` — `InMemoryMachineryResultStore`
  is one;
- a store that fills itself through a path of its own — a database a PLC
  gateway writes to — does not implement it. Publishing then only announces
  the result: the result variables and the `ResultReadyEventType` event. The
  store has to hold the result already; one it cannot return from
  `GetResultByIdAsync` is refused with `Bad_InvalidState`, because an event
  must not announce a result the methods cannot hand out.

With `WithPredefinedResultMetaData()` the metadata check runs before the store
sees the result, whichever kind of store it is.

#### Filtering and ordering the result list

`GetResultIdListFiltered` evaluates both arguments OPC 40001-101 §7.1.3
defines:

- **`filter`** is a `ContentFilter` whose BrowsePaths "can be built from the
  fields of the `ResultReadyEventType`, the `ResultType` VariableType or the
  `ResultDataType`". All three spellings resolve to the same field — an
  optional leading `Result`, then `ResultMetaData` (with its fields, and
  `ProcessingTimes` one level further down) or `ResultContent` — and names are
  matched without their namespace index, because a DataType field has none.
  The comparison, logical and `Like`/`Between`/`InList`/`OfType`/bitwise
  operators are supported; `InView` and `RelatedTo` need a node and are FALSE.
- **`orderedBy`** sorts by each RelativePath in turn, ascending. Results that
  leave a key unset sort last, and results that tie keep the store's
  newest-first order. An empty `orderedBy` "executes no ordering", so the
  store's own order stands.
- **`maxResults`** caps the list after filtering and ordering; zero means no
  cap.

Most metadata fields are optional, so NULL handling decides what a filter
returns. The evaluator follows OPC 10000-4 §7.7.3: a comparison with an unset
field is NULL and never selects a result — neither does its negation — and only
`IsNull` sees the missing value. An optional field counts as set when its
encoding-mask bit is set or when it carries a non-default value; deciding that
needs the structure, which is why the result query carries
[its own evaluator](../src/Opc.Ua.Machinery.Server/Results/MachineryResultFilterEvaluator.cs)
next to the stack's node-oriented `FilterEvaluator`. Both apply the same NULL
rules.
An invalid filter is refused with `Bad_ContentFilterInvalid`, an `orderedBy`
path that names no field with `Bad_InvalidArgument`.

The query reads the candidates through `GetResultIdsAsync(0)` and
`GetResultByIdAsync`, so a custom store is filtered and ordered without
implementing anything new. The `timeout` argument of the three getters is the
client's hint for how long it will need the data (§6.4); it is not a deadline
for the call.

#### Downloads, and the refused write side

`ResultTransferType` derives from `TemporaryFileTransferType`, so the download
is the standard OPC 10000-5 sequence: `GenerateFileForRead` returns a transient
`FileType` object and a handle that is **already open**, the client reads from
it and closes it.
[`MachineryResultTransferManager`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultTransferManager.cs)
serves it in the shape `SoftwareUpdateFileTransferManager` uses for DI
software uploads: one transient `FileState` per handle, the handle bound to
the session that opened it, a cap on concurrent handles, and a timeout after
which an abandoned handle is reclaimed. Reclaiming is swept at the start of
each `GenerateFileForRead` rather than driven by a per-object timer, so an
abandoned handle survives until the next download — where it costs nothing
but a transient node.

`GenerateFileForWrite` and `CloseAndCommit` are mandatory on every
`TemporaryFileTransferType` (OPC 10000-20 §4.4.1), so a `ResultTransfer`
object always carries them, and they answer. OPC 40001-101 §7.2.1 leaves
`GenerateFileForWrite` unspecialised "as results are supposed to be only
generated by the Server, not received", which is the read-only case
OPC 10000-20 names a code for: `GenerateFileForWrite` returns
`Bad_NotWritable` (§4.4.4), and `CloseAndCommit` returns
`Bad_InvalidArgument` (§4.4.5) for any handle — a download handle included,
which stays open and is closed with the file's own `Close`.

`WithResultsFolder` publishes the most recent results as `ResultType` variables
as well, which is what the `Machinery-Result ResultVariables` unit asks for —
an empty folder satisfies nothing. The variables are a fixed ring created with
the machine: the newest result lands in the first slot and the others shift
down, so a client can subscribe to a slot once and keep receiving results, and
the server never adds or deletes nodes while it runs.

`GetLatestResult`, `GetResultById` and `GetResultIdListFiltered` each return a
`ResultHandle` that pins what they handed out until the client calls
`ReleaseResultHandle`. The handle is bound to the calling session, and
releasing one twice is refused.

Because the payload is materialised from the store synchronously,
`GenerateFileForRead` returns `NodeId.Null` for `completionStateMachine` — the
value OPC 10000-5 defines for "no completion state machine".

## Abstract event types

Two of the event types the series declares are **abstract**:
OPC 40001-101's `ResultReadyEventType` and OPC 40001-2's
`ZeroPointAdjustmentEventType`. OPC 10000-3 forbids an instance of an abstract
type, and a client filtering on a concrete `EventType` never sees an event
reported with an abstract one — so a server that means to publish them has to
derive a concrete subtype of its own.

[`MachineryConcreteEventTypes`](../src/Opc.Ua.Machinery.Server/MachineryConcreteEventTypes.cs)
mints one per node manager into the application-owned instance namespace and
every object reporting that event shares it; deriving one per instance would
multiply types a client has to know about for no gain. Both the
`TypeDefinitionId` and the `EventType` field are set, because the second is
what an event filter actually selects on.
`Isa95NodeManager.CreateV2StatusEventTypeAsync` does the same for the ISA-95
job-order status event.

The client side mirrors it. An `OfType` filter on the abstract type still
selects the subtype — that is what `OfType` means — but the generated decoder
registry is keyed by the exact event type, so
[`MachineryClient.Events`](../src/Opc.Ua.Machinery.Client/MachineryClient.Events.cs)
re-decodes against the declared abstract type once the session's type cache
confirms the subtype relationship.

## State machines are server-driven

Both OPC 40001-1 state machines — `MachineryItemState_StateMachineType` with
`Executing` / `NotExecuting` / `NotAvailable` / `OutOfService`, and
`MachineryOperationModeStateMachineType` with `None` / `Maintenance` / `Setup` /
`Processing` — declare four states and sixteen transitions each, including the
self-transitions. **Neither declares a cause method.** The whole Machinery
NodeSet contains no `UAMethod` at all, so a client can never request a
transition.

The server surface follows: `IMachineryItemStateController.SetStateAsync` and
`IMachineryOperationModeController.SetModeAsync`, reachable from the machine
handle. There is deliberately no `WithCause`.

```csharp
await press.ItemState!.SetStateAsync(MachineryItemStateValue.Executing);
await press.OperationMode!.SetModeAsync(MachineryOperationModeValue.Processing);
```

The state and transition tables live in
[`MachineryStateMachineTables`](../src/Opc.Ua.Machinery.Server/StateMachines/MachineryStateMachineTables.cs),
spelled out from the generated `*TypeIds` constants — the generator emits these
two types as plain `FiniteStateMachineState` subclasses without the stack's
`StateTable` / `TransitionTable` / `TransitionMappings` overrides, so the
stack's own `SetState` would write nothing. The driver still invokes the
machine's `OnBeforeTransition` / `OnAfterTransition` delegates around the
variable update, which is exactly where `StateMachineBuilder.For(...)` installs
its guards and observers, so the repository's state-machine lifecycle surface
keeps working.

## Client

`MachineryClient` starts at the `Machines` folder, so it needs no configured
NodeIds and works against a server it has never seen.

| Concern | API |
| --- | --- |
| Discovery | `EnumerateMachinesAsync`, `DiscoverMachinesAsync`, `EnumerateComponentsAsync`, `EnumerateBuildingBlocksAsync` |
| Identification | `ReadIdentificationAsync` — one `TranslateBrowsePaths` plus one batched `Read` |
| State | `ItemStateAsync` / `OperationModeAsync` return the generated FSM proxies; `GetItemStateAsync` / `ObserveItemStateAsync` wrap them |
| Health | `ReadDeviceHealthAsync` |
| Counters | `ReadOperationCountersAsync`, `ReadLifetimeCountersAsync` |
| Equipment | `EnumerateEquipmentAsync` — including `EquipmentLife` |
| Process values | `EnumerateProcessValuesAsync`, `ReadProcessValueAsync`, `ZeroPointAdjustmentAsync`, `ObserveZeroPointAdjustmentsAsync` |
| Energy | `ResolveConsumptionAsync`, `EnumerateEnergyResourcesAsync`, `EnumerateMeteringPointsAsync`, `ReadMainMeteringPointAsync`, `ReadMeteringPointAsync`, and for the sub-meters a point `Contains` `EnumerateSubMetersAsync` / `ReadSubMetersAsync` |
| Jobs | `JobManagementAsync` returns the ISA-95 `Isa95JobControlV2Client`; `ReadJobOrdersAsync` / `ReadJobResponsesAsync` read the published lists; `ReadJobOrderParametersAsync` / `ReadJobResponseParametersAsync` return the predefined OPC 40001-3 parameters typed |
| Results | `ResultManagementAsync` (whose `GetResultIdListFilteredAsync` takes the filter and order), `DownloadResultAsync`, `ReadPublishedResultsAsync`, `ObserveResultsAsync` |
| Notifications | `ResolveNotificationsAsync`, `ObserveNotificationsAsync` |
| Device Integration | `Topology` — for a machine that is also a DI device |

Process values are found by type definition rather than by browse path:
OPC 40001-2 does not fix where they hang — its examples put them on a sensor
component, on the machine, and below `Monitoring` — so every object below the
item whose type is `ProcessValueType` counts.

A metering point's measurements are read with one browse and one batched
`Read`. Each `MachineryMeasurementValue` carries the `NodeId` of the variable
its reading came from next to the value, so a client that wants live values
subscribes to those nodes without browsing the point again:

```csharp
MachineryMeteringPoint? main = await machinery.ReadMainMeteringPointAsync(resource, ct);
foreach (MachineryMeasurementValue measurement in main!.Measurements)
{
    subscription.TryAddMonitoredItem(
        measurement.BrowseName.Name!,
        measurement.NodeId,
        options => options with { SamplingInterval = TimeSpan.FromSeconds(1) },
        out _);
}
```

State observation rides the generated `*StateMachineTypeClient` proxies, which
inherit `GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync` and
`WaitForStateAsync` from `FiniteStateMachineTypeClient` — so a vendor subtype of
either state machine is observed the same way. See
[StateMachines.md](StateMachines.md).

Constructing the client registers the OPC 40001-101 and OPC 40001-3
structured types, and the ISA-95 Job Control V2 ones the job lists travel as,
with the session's encodeable factory, so `GetLatestResult` decodes into
`ResultDataType` and `JobOrderList` into `ISA95JobOrderAndStateDataType`
instead of an opaque `ExtensionObject`.

The predefined job parameters of OPC 40001-3 §7.2 Table 1 travel as ISA-95
key-value pairs rather than as nodes, so the client reads them out of the
payload: `MachineryJobOrderParameters.FromJobOrder` and
`MachineryJobResponseParameters.FromJobResponse` return each one with the
type the table declares — `Duration` in milliseconds, the series' own
enumerations as `JobExecutionMode` / `JobResult`, and `Overproduction` from
where Table 1 puts it, below `PlannedOrderQuantity` — and keep every
parameter as received in `Parameters`. A parameter with the wrong type counts
as not carried. Optional fields of the generated ISA-95 structures, such as
`JobOrderParameters` and `Subparameters`, only reach the wire when their
`EncodingMask` bit is set.

## Conformance matrix

Status key: ✅ implemented and tested · 📄 static NodeSet structure only ·
🔲 optional per the published modelling rule · ❌ not shipped.

Conformance units are advertised at runtime, not statically. The series has two
kinds of unit and the manager answers them differently:

- a **type-exposure** unit — "the server exposes this type and all its
  supertypes" — is satisfied by loading the model, so it follows
  `MachineryParts`;
- every other unit talks about instances, methods or references, and is
  reported only once a build actually materialised **and** wired the structure
  behind it.

A server that builds no machine therefore advertises nothing but
`Machinery Find Machines` and whatever types its parts loaded.

### OPC 40001-1 building blocks

| Area | Static | Runtime | Source | Tests |
|---|---|---|---|---|
| `Machines` folder organized from `Objects` (`Machinery Find Machines`) | ✅ | ✅ | [`MachineryNodeManager`](../src/Opc.Ua.Machinery.Server/MachineryNodeManager.cs) | `MachineryModelLoadingTests` |
| `MachineryBuildingBlocks` organizer (`Machinery Building Block Organization`) | ✅ | ✅ | [`MachineryItemBlocks`](../src/Opc.Ua.Machinery.Server/Builders/MachineryBlockBuilders.cs) | `MachineryBuildingBlockTests` |
| Machine identification (`Machinery Machine Identification`) | ✅ | ✅ | [`MachineryBlockBuilders`](../src/Opc.Ua.Machinery.Server/Builders/MachineryBlockBuilders.cs) | `MachineBuilderTests` |
| Component identification and discovery (`Machinery Component Identification`, `Machinery Find Components of Machines`) | ✅ | ✅ | same | `MachineBuilderTests` |
| Monitoring add-in (`Machinery Monitoring`) | ✅ | ✅ | same | `MachineBuilderTests` |
| `Monitoring/Health` with DI `DeviceHealth` and `DeviceHealthAlarms` | ✅ | ✅ | [`MachineryHealthBuilder`](../src/Opc.Ua.Machinery.Server/Builders/MachineryBlockBuilders.cs) | `MachineBuilderTests`; `MachineryEndToEndTests` |
| Item state machine, four states / sixteen transitions, no causes (`Machinery MachineryItem State`) | ✅ | ✅ | [`MachineryStateMachineTables`](../src/Opc.Ua.Machinery.Server/StateMachines/MachineryStateMachineTables.cs) | `MachineBuilderTests`; `MachineryStateMachineTests` |
| Operation-mode state machine (`Machinery Operation Mode`) | ✅ | ✅ | same | same |
| Operation counters (`Machinery Operation Counter`) | ✅ | ✅ | [`MachineryBlockBuilders`](../src/Opc.Ua.Machinery.Server/Builders/MachineryBlockBuilders.cs) | `MachineBuilderTests`; `MachineryEndToEndTests` |
| Lifetime counters (`Machinery Lifetime Counter`) | ✅ | ✅ | same | `MachineBuilderTests` |
| Machinery equipment (`Machinery MachineryEquipment`) incl. `EquipmentLife` | ✅ | ✅ | same | `MachineBuilderTests`; `MachineryEndToEndTests` |
| Notifications (`Machinery Notifications`) with a publish seam | ✅ | ✅ | [`MachineryNotificationsBuilder`](../src/Opc.Ua.Machinery.Server/Builders/MachineryNotificationsBuilder.cs) | `MachineBuilderTests` |
| Stacklight over OPC 10000-200 IA | ✅ | 🔲 | [`MonitoringBuilder`](../src/Opc.Ua.Machinery.Server/Builders/MachineryBlockBuilders.cs) | `MachineBuilderTests` |
| Writable identification (`Machinery Machine Identification Writable`, `Component Identification Mandatory`, `Component Identification Writable`) | ✅ | ✅ opt-in via `MachineryIdentificationData.Writable` | [`MachineryBlockBuilders`](../src/Opc.Ua.Machinery.Server/Builders/MachineryBlockBuilders.cs) | `MachineryPartsBuilderTests` |

### OPC 40001-2 process values

| Area | Static | Runtime | Source | Tests |
|---|---|---|---|---|
| `ProcessValueType` instances over the `ProcessValueVariableType` subtype (`… Base Types`, `… Analog Object Instances`) | ✅ | ✅ | [`ProcessValueBuilder`](../src/Opc.Ua.Machinery.Server/Builders/ProcessValueBuilder.cs) | `MachineryProcessValueTests`; `MachineryEndToEndTests` |
| Setpoint (`… Base SetpointType`, `… Base Process Value Setpoint`) | ✅ | ✅ | same | `MachineryProcessValueTests` |
| Limits on the signal (`… Limits Base`) | ✅ | ✅ | same | `MachineryProcessValueTests` |
| Exclusive limit alarm driven by the value (`… Limits Alarm`, `… Limits Alarm Object`) | ✅ | ✅ | [`MachineryProcessValueAlarm`](../src/Opc.Ua.Machinery.Server/Builders/MachineryProcessValueAlarm.cs) | `MachineryProcessValueTests` |
| Deviations on the setpoint (`… Deviation Base`, `… Deviation AutoAdjustment`, `… Deviation Sensitivity`) | ✅ | ✅ | [`ProcessValueBuilder`](../src/Opc.Ua.Machinery.Server/Builders/ProcessValueBuilder.cs) | `MachineryProcessValueTests` |
| Exclusive deviation alarm against the setpoint (`… Deviation Alarm`, `… Deviation Alarm Object`) | ✅ | ✅ | [`MachineryProcessValueAlarm`](../src/Opc.Ua.Machinery.Server/Builders/MachineryProcessValueAlarm.cs) | `MachineryProcessValueTests` |
| `PercentageValue` kept in step with the range (`… Percentage Value`) | ✅ | ✅ | [`ProcessValueBuilder`](../src/Opc.Ua.Machinery.Server/Builders/ProcessValueBuilder.cs) | `MachineryProcessValueTests` |
| `Status` and `AlarmSuppression` (`… Monitoring`, `… AlarmSuppression`) | ✅ | ✅ | same | `MachineryProcessValueTests` |
| `ZeroPointAdjustment` method and its event (`… Base EventTypes`, `… ZeroPointAdjustment Events`) | ✅ | ✅ | same | `MachineryProcessValueTests`; `MachineryEndToEndTests` |
| Simulation (`PA-DIM AnalogSignalVariable Simulation`) | ✅ | ✅ opt-in via `WithSimulation()` | [`ProcessValueBuilder`](../src/Opc.Ua.Machinery.Server/Builders/ProcessValueBuilder.cs) | `MachineryProcessValueTests` |
| Device object (`… Device Object`, `… Simple Device Info`) | ✅ | ✅ opt-in via `WithProcessValueDevice()` | [`MachineBuilder`](../src/Opc.Ua.Machinery.Server/Builders/MachineBuilder.cs) | `MachineryPartsBuilderTests` |

### OPC 40001-3 job management

| Area | Static | Runtime | Source | Tests |
|---|---|---|---|---|
| `JobManagement` as an AddIn under the organizer, composing ISA-95 Job Control V2 (`Machinery Job Management Base`, `… Minimum String Length`) | ✅ | ✅ | [`JobManagementBuilder`](../src/Opc.Ua.Machinery.Server/Builders/JobManagementBuilder.cs) | `MachineryPartsBuilderTests`; `MachineryEndToEndTests` |
| Job verbs through a machine, including `Pause`, `Resume` and `RevokeStart` and their refusals | ✅ | ✅ | [`Isa95JobControlV2Binder`](../src/Opc.Ua.ISA95.Server/Providers/Isa95JobControlV2Binder.cs) | `MachineryEndToEndTests` |
| `JobOrderList` and `JobOrderResponseList` kept current, including responses reported while an order runs | ✅ | ✅ | [`JobManagementBuilder`](../src/Opc.Ua.Machinery.Server/Builders/JobManagementBuilder.cs) | `MachineryEndToEndTests` |
| Job results (`Machinery Job Management Result Base`) | ✅ | ✅ | same | `MachineryPartsBuilderTests` |
| The predefined `Planned …` / `Result …` job-order parameters (42 units) | ✅ | ✅ opt-in via `WithPredefinedParameters()` — the 37 IDs of §9 are recognised and their declared types enforced; anything the series does not predefine still travels untouched | [`MachineryJobParameters`](../src/Opc.Ua.Machinery.Server/Jobs/MachineryJobParameters.cs) | `MachineryPartsBuilderTests` |

### OPC 40001-4 energy

| Area | Static | Runtime | Source | Tests |
|---|---|---|---|---|
| Resource folders below `Monitoring/Consumption` (`Machinery Energy Base Structure`) | ✅ | ✅ | [`MachineryEnergyBuilder`](../src/Opc.Ua.Machinery.Server/Builders/MachineryEnergyBuilder.cs) | `MachineryPartsBuilderTests`; `MachineryEndToEndTests` |
| A `Main` metering point per resource (`Machinery Energy Main grouping`) | ✅ | ✅ — checked at build time, so a resource folder attached to `Consumption` past the builder fails the build instead of breaking the unit | same; check in [`MachineryBuildScope`](../src/Opc.Ua.Machinery.Server/Builders/MachineryBuildScope.cs) | same; `MachineryPartsBuilderTests` |
| `INonElectricalEnergyType` on a metering point (`… Non Electrical Base`) | ✅ | ✅ | same | same |
| Volume- and mass-flow interfaces (`… Volume Flow`, `… Mass Flow`) | ✅ | ✅ | same | `MachineryPartsBuilderTests` |
| `Contains` from `Main` to a sub-meter (`Machinery Energy Contains`) | ✅ | ✅ | same | same; `MachineryEndToEndTests` |
| Electricity | ✅ | ✅ | same — OPC 40001-4 defers it to OPC 34100's own interfaces, which `WithInterface` and `AddMeasurementValue` attach | `MachineryEndToEndTests` |

### OPC 40001-101 results

| Area | Static | Runtime | Source | Tests |
|---|---|---|---|---|
| Result types (`Machinery-Result Types`) | ✅ | ✅ | [`MachineryResultManagementBinder`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultManagementBinder.cs) | `MachineryPartsBuilderTests` |
| The five optional methods (`… GetLatestResult`, `… GetResultById`, `… GetResultsFiltered`, `Machinery Result AcknowledgeResults`) | ✅ | ✅ | same | `MachineryPartsBuilderTests` |
| `filter`, `orderedBy` and `maxResults` of `GetResultIdListFiltered` (§7.1.3), with OPC 10000-4 NULL semantics | ✅ | ✅ | [`MachineryResultQuery`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultQuery.cs), [`MachineryResultFilterEvaluator`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultFilterEvaluator.cs) | `MachineryResultQueryTests`; `MachineryEndToEndTests` |
| Mandatory `GenerateFileForWrite` / `CloseAndCommit` answering for a read-only result file | ✅ | ✅ | [`MachineryResultTransferManager`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultTransferManager.cs) | `MachineryResultQueryTests`; `MachineryEndToEndTests` |
| Publishing through an application-supplied store, writable or self-ingesting | — | ✅ | [`MachineryResult`](../src/Opc.Ua.Machinery.Server/Results/MachineryResult.cs) | `MachineryResultQueryTests` |
| `GenerateFileForRead` download path with session-bound handles, cap and timeout (`… ResultFiles`) | ✅ | ✅ | [`MachineryResultTransferManager`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultTransferManager.cs) | `MachineryPartsBuilderTests`; `MachineryEndToEndTests` |
| Result variables in the `Results` folder (`… ResultVariables`) | ✅ | ✅ | [`MachineryResultVariables`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultVariables.cs) | `MachineryPartsBuilderTests`; `MachineryEndToEndTests` |
| Predefined result metadata (`Machinery-Result PredefinedResultMetaData`) | ✅ | ✅ opt-in via `WithPredefinedResultMetaData()` — refused on publish and on the way out of a bound store | [`PredefinedResultMetaData`](../src/Opc.Ua.Machinery.Server/Results/PredefinedResultMetaData.cs) | `MachineryPartsBuilderTests` |
| Result-ready events with a concrete event type (`… ResultEvents`) | ✅ | ✅ | [`MachineryResultManagementBinder`](../src/Opc.Ua.Machinery.Server/Results/MachineryResultManagementBinder.cs) | `MachineryPartsBuilderTests`; `MachineryEndToEndTests` |
| Stand-alone result server without DI or the machine model, reporting the same units and facets | ✅ | ✅ | [`MachineryResultNodeManager`](../src/Opc.Ua.Machinery.Server/MachineryResultNodeManager.cs) | `MachineryResultServerTests` |
| Durable result store | ❌ not shipped — the storage technology is an open decision; an application plugs its own in through `IMachineryResultStore` and publishes through it | — | — | — |

### Client and hosting

| Area | Static | Runtime | Source | Tests |
|---|---|---|---|---|
| Discovery, identification, components, building blocks | — | ✅ | [`MachineryClient`](../src/Opc.Ua.Machinery.Client/MachineryClient.cs) | `MachineryClientTests`; `MachineryEndToEndTests` |
| State observation over both state machines | — | ✅ | [`MachineryClient.StateMachines`](../src/Opc.Ua.Machinery.Client/MachineryClient.StateMachines.cs) | same |
| Process values, zero-point adjustment | — | ✅ | [`MachineryClient.ProcessValues`](../src/Opc.Ua.Machinery.Client/MachineryClient.ProcessValues.cs) | `MachineryEndToEndTests` |
| Energy resources, metering points and sub-meters | — | ✅ | [`MachineryClient.Energy`](../src/Opc.Ua.Machinery.Client/MachineryClient.Energy.cs) | same |
| Counters, equipment, device health | — | ✅ | [`MachineryClient.Counters`](../src/Opc.Ua.Machinery.Client/MachineryClient.Counters.cs) | same |
| Job control | — | ✅ | [`MachineryClient.Accessors`](../src/Opc.Ua.Machinery.Client/MachineryClient.Accessors.cs) | same |
| Job lists and predefined job parameters | — | ✅ | [`MachineryClient.Jobs`](../src/Opc.Ua.Machinery.Client/MachineryClient.Jobs.cs), [`MachineryJobParameters`](../src/Opc.Ua.Machinery.Client/MachineryJobParameters.cs) | `MachineryJobParameterClientTests`; `MachineryEndToEndTests` |
| Result download, published result variables, result and zero-point events | — | ✅ | [`MachineryClient.Events`](../src/Opc.Ua.Machinery.Client/MachineryClient.Events.cs) | same |
| Server/client DI wiring (`AddMachinery`, `AddMachineryResults`, `AddMachineryClient`) | — | ✅ | [`OpcUaServerMachineryBuilderExtensions`](../src/Opc.Ua.Machinery.Server/Hosting/OpcUaServerMachineryBuilderExtensions.cs) | `MachineryHostingTests`; `MachineryAotTests` |
| Facet URIs on `ServerProfileArray` | — | ✅ | [`ServerProfiles`](../src/Opc.Ua.Machinery.Server/ConformanceUnit.cs) | `MachineryEndToEndTests` reads them back over the wire |

### Where the facet URIs come from

Each URI in [`ServerProfiles`](../src/Opc.Ua.Machinery.Server/ConformanceUnit.cs)
is the published value from the "Profile URIs" table of the matching part, at
the exact model version this repository vendors. The three URI shapes are the
specifications' own and are reproduced verbatim, because conformance tooling
matches a facet URI literally:

| Part | Shape | Example |
| --- | --- | --- |
| 40001-1, -3, -101 | `…/UA-Profile/Machinery/…`, no trailing slash | `http://opcfoundation.org/UA-Profile/Machinery/Server/State` |
| 40001-2 | `…/UA/Machinery/ProcessValues/…`, **with** a trailing slash | `http://opcfoundation.org/UA/Machinery/ProcessValues/Server/Base/` |
| 40001-4 | `…/UA-Profile/Machinery/Energy/…` | `http://opcfoundation.org/UA-Profile/Machinery/Energy/Server/Base` |

A facet is advertised only when every conformance unit the specification marks
mandatory for it, **and that this library is responsible for**, was
materialised and wired. The base-server units each facet also inherits —
address space, view, attribute, method and event subscription — are the stack's
to report and already appear in `base.ServerProfiles`.

## Model sources, identifier tables and repairs

Every NodeSet is vendored unmodified from
[OPCFoundation/UA-Nodeset](https://github.com/OPCFoundation/UA-Nodeset) (branch
`latest`). Two files are renamed on the way in, because upstream's names do not
match the assembly they belong to; renaming is safe because
`NodeSetToModelDesign.IsNodeSet` inspects the document, not the file name.

| Repository path | Upstream path |
| --- | --- |
| `src/Opc.Ua.Machinery/Model/Opc.Ua.Machinery.NodeSet2.xml` | `Machinery/Opc.Ua.Machinery.NodeSet2.xml` |
| `src/Opc.Ua.Machinery.ProcessValues/Model/Opc.Ua.Machinery.ProcessValues.NodeSet2.xml` | `Machinery/ProcessValues/Opc.Ua.Machinery.ProcessValues.NodeSet2.xml` |
| `src/Opc.Ua.Machinery.Jobs/Model/Opc.Ua.Machinery.Jobs.NodeSet2.xml` | `Machinery/Jobs/Opc.Ua.Machinery.Jobs.Nodeset2.xml` *(lower-case `s`)* |
| `src/Opc.Ua.Machinery.Energy/Model/Opc.Ua.Machinery.Energy.NodeSet2.xml` | `Machinery/Energy/Opc.Ua.Machinery.Energy.NodeSet2.xml` |
| `src/Opc.Ua.Machinery.Result/Model/Opc.Ua.Machinery.Result.NodeSet2.xml` | `Machinery/Result/Opc.Ua.Machinery_Result.NodeSet2.xml` *(underscore)* |
| `src/Opc.Ua.IA/Model/Opc.Ua.IA.NodeSet2.xml` | `IA/Opc.Ua.IA.NodeSet2.xml` |
| `src/Opc.Ua.ECM/Model/Opc.Ua.ECM.NodeSet2.xml` | `ECM/Opc.Ua.ECM.NodeSet2.xml` |
| `src/Opc.Ua.PADIM/Model/Opc.Ua.PADIM.NodeSet2.xml` | `PADIM/Opc.Ua.PADIM.NodeSet2.xml` |
| `src/Opc.Ua.PADIM/Model/Opc.Ua.IRDI.NodeSet2.xml` | `PADIM/Opc.Ua.IRDI.NodeSet2.xml` |

### What the identifier table is and is not

Each model carries a `Model/*.NodeIds.csv` wired up as
`ModelSourceGeneratorIdentifierFile`. It is **not** a NodeId source — for a
NodeSet2 input the NodeIds come from the XML
(`ModelDesignValidator.LoadDesignFile` routes NodeSet2 documents through
`NodeSetToModelDesign.Import`; only ModelDesign inputs use the CSV fallback).
It is a **drift guard**: the generator validates every row against the imported
symbols and fails the build with `MODELGEN022`…`MODELGEN029` when the vendored
model and the pinned mapping disagree. That is how a silently dropped type gets
caught instead of quietly disappearing from the generated API.

`Model/*.Upstream.NodeIds.csv` is the OPC Foundation's publication unmodified
and is deliberately kept out of `<AdditionalFiles>`, so the two can be diffed to
review exactly what the derivation changed — the convention `src/Opc.Ua.ISA95`
established.

### Why the published tables need deriving

The published tables carry the UA-ModelCompiler's symbolic names, which differ
from the ones this repository's generator derives. Three differences occur; all
three are handled by
[`tools/nodesets/derive-identifier-table.py`](../tools/nodesets/derive-identifier-table.py),
which reimplements `NodeSetToModelDesign.ImportSymbolicName` / `BuildSymbolicId`:

| Published | Derived | Why |
| --- | --- | --- |
| `Server_Namespaces_http___…_` | `http___…_` | the publication qualifies the namespace-metadata object with its server path |
| `X_ControlChannel` | `X_ControlChannel_Placeholder` | a `<Placeholder>` BrowseName maps to `Name_Placeholder` |
| `DefaultBinary` | `RGBWDataType_Encoding_DefaultBinary` | encoding nodes are qualified with the data type they encode |

Two publications also deviate in shape rather than in naming, and the script
reads both: **ECM** publishes its table as `Opc.Ua.ECM.NodeSet2.csv` rather than
`*.NodeIds.csv`, and **PADIM** publishes a tab-separated table with an
`ID / Browsename / Node Class` header — identifier first, and BrowseNames rather
than symbolic names. **IRDI** has no published table at all and therefore
carries no drift guard.

No numeric id or node class disagreed with its NodeSet in any of the nine
models, so no NodeSet needed a normative repair of the kind `docs/ISA95.md`
documents for OPC-10030.

## Generator gaps found while adding these models

Three defects in the cross-assembly dependency machinery only surfaced once a
model consumed a dependency through a referenced assembly's payload rather
than through `<AdditionalFiles>`. All three are now fixed in the generator.
They are worth knowing about when adding the next companion specification.

**A VariableType's data type restriction was not carried.** `DependencyNode`
recorded base type, numeric id, abstractness, data type fields and children, but
never a VariableType's own `DataType` / `ValueRank`. A consumer that typed a
variable with such a VariableType resolved a null `DataTypeNode`, and
`ModelDesignExtensions.GetNodeStateClassName` dereferenced it — a bare
`NullReferenceException` surfaced as `MODELGEN003`. OPC 40001-1's
`EquipmentLife` and `<LifetimeVariable>`, typed by DI's `LifetimeVariableType`,
are the first nodes in this repository to hit it. This is what the reduced
Machinery NodeSet copies in the samples were working around.

The payload now carries the restriction behind a new node flag, so payloads
written before the change stay readable, and `GetNodeStateClassName` throws a
message naming the variable and its type rather than a bare null dereference if
a gap ever remains.

**A method declaration was not chained to its method state.** When a payload
child carried both a method-state identity and a declaration identity, the
consumer replaced the method-state node with the declaration node without
linking the two. `ResolveMethodStateIdentity` then stopped at the declaration —
whose symbolic id is the composed `OwnerType_Method` — and a consumer that
re-declares an inherited method emitted a reference to an
`OwnerType_MethodMethodState` class the producer never generated. OPC 34100 ECM
re-declaring the DI `LockingServices` methods is the first model to hit it.

**An inherited child lost its reference type and its browse-name namespace.**
`DependencyChild` carried the child's browse name as a bare string and no
reference type, so when a derived type in a consuming model was instantiated
*on its own*, the generator emitted the inherited child with
`ReferenceTypeId = NodeId.Null` and the browse name qualified with the
declaring model's namespace. Both were wrong, and the first was not cosmetic: a
node with a null reference type is in the node tree but produces no reference
in a filtered Browse, so no client ever saw it.

Two OPC 40001-2 members were affected, both inherited from OPC 30081 PA-DIM:

| Member | Reference type | Browse name |
| --- | --- | --- |
| `ProcessValueType.SignalTag` | lost | correct (`3:SignalTag`) |
| `ProcessValueVariableType.EngineeringUnits` | lost | wrong (PA-DIM, should be namespace 0) |

The same member reached the other way — `ProcessValueType.AnalogSignal`'s
`EngineeringUnits`, generated as a grandchild of the object type — was correct,
which is what made the inconsistency visible. `SignalTag` is mandatory, and
`EngineeringUnits` in namespace 0 is where a Data Access client reads the unit,
so both matter for the OPC 40001-2 base facet.

`DependencyChild` now carries a `BrowseNameNamespace` and a
`ReferenceTypeName` / `ReferenceTypeNamespace`, behind two flag bits in the
per-child flag byte, the way the VariableType restriction was added — so a
payload written before the change stays readable. The producer records a browse
namespace only when it differs from the declaring model and a reference type
only when it departs from the kind default, which keeps the payload byte-identical
for the ordinary case. The consumer (`MaterialiseDependencyChild`) now always
assigns a reference type: the carried one when present, otherwise the same kind
default `ImportInstance` applies to a locally declared instance. That last part
matters — it means a model still consuming an older prebuilt payload is fixed
too, not only one rebuilt against the new generator.

The former stop-gap `ProcessValueBuilder.RepairInheritedPadimMembers` is gone;
what remains of it is `ApplyDefaultSignalTag`, which was never a workaround but
the OPC 30081 default value for a mandatory tag. Only the in-process tests
missed the defect; the end-to-end test over `opc.tcp` is what surfaced it,
because a filtered Browse is where a null reference type finally shows, and
`MachineryProcessValueTests
.TheSignalKeepsThePadimBrowseNameAndTheNarrowerTypeAsync` pins all four
attributes.

## See also

- [Machinery samples](../samples/Machinery/README.md) — a simulated press that
  exercises all five parts, and a client that walks it
- [Device Integration (DI) developer guide](DeviceIntegration.md)
- [ISA-95 developer guide](ISA95.md) — OPC 40001-3 sits on Job Control V2
- [Cross-assembly model dependencies](ModelDependencies.md)
- [Robotics developer guide](Robotics.md) — shares `Opc.Ua.IA`
