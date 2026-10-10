# OPC UA for Pumps and Vacuum Pumps (OPC 40223)

The `Opc.Ua.Pumps*` packages implement
[OPC 40223](https://reference.opcfoundation.org/specs/OPC-40223), the
companion specification for pumps, compressors and vacuum pumps, on top of
OPC 40001-1 Machinery and OPC 10000-100 Device Integration. A server
publishes pumps with a fluent builder; a client discovers them and reads
them back into typed snapshots. Both sides share one set of contracts.

## Contents

- [Library layout](#library-layout)
- [The model](#the-model)
- [Where a pump lives](#where-a-pump-lives)
- [Quick start — server](#quick-start--server)
- [Quick start — client](#quick-start--client)
- [Server hosting](#server-hosting)
- [Builder reference](#builder-reference)
- [Updating values at runtime](#updating-values-at-runtime)
- [Client reference](#client-reference)
- [Contracts reference](#contracts-reference)
- [Conformance matrix](#conformance-matrix)
- [Samples](#samples)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Model provenance](#model-provenance)
- [Generator gap this model uncovered](#generator-gap-this-model-uncovered)
- [MCP tools](#mcp-tools)
- [See also](#see-also)

## Library layout

| Package | Contents |
| --- | --- |
| `Opc.Ua.Pumps` | The source-generated OPC 40223 model (typed node states, `ObjectTypeIds`, `BrowseNames`, the 13 enumerations and 5 option sets, `PhysicalAddressDataType`, `AddOpcUaPumps`), `PumpsModel`, and the snapshot contracts both sides share |
| `Opc.Ua.Pumps.Server` | `PumpsNodeManager`, the fluent builders `IPumpBuilder` / `IPumpGroupBuilder`, and the `AddPumps()` / `ConfigurePumps()` hosting pipeline |
| `Opc.Ua.Pumps.Client` | `PumpsClient` for discovery, nameplate, configuration, measurements, supervision, maintenance, ports and whole-pump snapshots, plus `AddPumpsClient()` |

The contracts (`PumpNameplate`, `PumpValueSet`, `PumpSupervisionStatus`,
`PumpSnapshot`, …) live in the model package on purpose. A server fills them
from its process and a client reads them back, and neither side references the
other.

All three packages target the stack's library frameworks and are marked
NativeAOT-compatible on `net10.0`.

## The model

| Model URI | C# namespace | `Namespaces` member | Model loader |
| --- | --- | --- | --- |
| `http://opcfoundation.org/UA/Pumps/` | `Opc.Ua.Pumps` | `Pumps` | `AddOpcUaPumps` |

The NodeSet defines 51 object types, 19 data types and roughly 8,900
variables. OPC 40223 defines no methods of its own; the 408 methods in the
NodeSet all belong to the `FileType` instances of the documentation groups.

`PumpType` is the entry point. It derives directly from the OPC 10000-100 DI
`TopologyElementType` (not from DI `ComponentType` and not from any Machinery
type) and carries seven groups:

| Group | Browse-name namespace | Type | What it holds |
| --- | --- | --- | --- |
| `Identification` | DI | `PumpIdentificationType` | The nameplate. Mandatory |
| `Configuration` | DI | `ConfigurationGroupType` | `Design` (78 parameters), `Implementation` (33), `SystemRequirements` (37) |
| `Operational` | DI | `OperationalGroupType` | `Measurements` (50), `Signals` (21), `Control`, `PumpActuation`, `BypassActuation`, `ThrottleValveActuation`, `MultiPump` |
| `Maintenance` | DI | `MaintenanceGroupType` | `GeneralMaintenance`, `PreventiveMaintenance`, `ConditionBasedMaintenance`, `BreakdownMaintenance` |
| `Events` | Pumps | `SupervisionType` | Seven supervision categories, about 150 boolean fault signals |
| `Ports` | Pumps | `PortsGroupType` | Drive, inlet-connection and outlet-connection ports |
| `Documentation` | Pumps | `DocumentationType` | 22 document/link pairs |

**Mind the namespace column.** `Identification`, `Configuration`,
`Maintenance` and `Operational` are OPC 10000-100 §5.6 *instance* names in
the DI namespace, not nodes of the Pumps model, so the Pumps generator emits
no `BrowseNames` constants for them. They are in `PumpsModel.DiGroups`
instead. Qualifying one of these four with the Pumps namespace index is the
most common way to get a Pumps browse path wrong, and it fails silently: the
path simply does not resolve.

Children of a group are spread over namespaces too. The nameplate is the
extreme case: eleven fields come from OPC 10000-100, four from OPC 40001-1
and eleven from OPC 40223. The builder and the client search the Pumps, DI,
Machinery and core namespaces in that order, so callers pass bare browse
names and never have to know which namespace a field lives in.

`MultiPumpType`, despite the name, is not a pump. It is a functional group
below `Operational` describing one pump's role inside a multi-pump set.

## Where a pump lives

A `PumpType` is a Device Integration topology element that OPC 40001-1
clients also treat as a machine, so a conforming server publishes it twice:

- as a child of the DI `DeviceSet`, and
- through an `Organizes` reference from the Machinery `Machines` folder.

`PumpsNodeManager` does both by default
(`PumpsServerOptions.OrganizeIntoMachinesFolder`), and
`PumpsClient.EnumeratePumpsAsync` looks in both places and de-duplicates.
Matching goes by type hierarchy, so a vendor subtype of `PumpType` is found
as well.

## Quick start — server

```csharp
using Opc.Ua;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
// Opc.Ua and Opc.Ua.Pumps both declare BrowseNames, and System declares Range.
using BrowseNames = Opc.Ua.Pumps.BrowseNames;
using Range = Opc.Ua.Range;

services.AddOpcUa()
    .AddServer<StandardServer>(ConfigureServer)
    .AddPumps()
    .ConfigurePumps(async context =>
    {
        IPumpBuilder pump = await context.Manager.CreatePumpAsync(
            new QualifiedName("Pump_1", context.Manager.InstanceNamespaceIndex),
            context.CancellationToken);

        pump.WithNameplate(new PumpNameplate
        {
            NodeId = NodeId.Null,
            Manufacturer = new LocalizedText("Acme Pumps"),
            SerialNumber = "SN-001",
            Model = new LocalizedText("PumpX-2000"),
            Location = "Plant 1 / Utility Skid",
            CountryOfOrigin = "DE"
        });

        pump.Measurements
            .SetAnalog(BrowseNames.DifferentialPressure, 350_000,
                new EUInformation { DisplayName = new LocalizedText("Pa") },
                new Range { Low = 0, High = 1_000_000 })
            .SetAnalog(BrowseNames.MassFlow, 12.5);

        pump.Signals.SetDiscrete(BrowseNames.PumpOperation, true);

        pump.Supervision(BrowseNames.SupervisionProcessFluid)
            .SetDiscrete(BrowseNames.Cavitation, false);

        pump.MaintenanceCategory(BrowseNames.GeneralMaintenance)
            .Set(BrowseNames.StateOfTheItem, Variant.From(StateOfTheItemEnum.OperatingState));

        pump.AddPort(PumpPortKind.InletConnection, "Suction")
            .Set(BrowseNames.Direction, Variant.From(PortDirectionEnum.In));
    });
```

The pump publishes exactly what the configurator set: the nameplate fields,
two measurements with their unit and range, one signal, one supervision flag,
one maintenance state and one port. Everything else in the ~3,000-node
`PumpType` surface stays unmaterialised.

## Quick start — client

```csharp
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Client;
using BrowseNames = Opc.Ua.Pumps.BrowseNames;

PumpsClient pumps = session.Pumps(telemetry);
if (!pumps.IsSupported)
{
    return; // the server does not publish OPC 40223
}

await foreach (PumpEntry entry in pumps.EnumeratePumpsAsync(ct))
{
    PumpNameplate? nameplate = await pumps.ReadNameplateAsync(entry.NodeId, ct);
    PumpValueSet measurements = await pumps.ReadMeasurementsAsync(entry.NodeId, cancellationToken: ct);

    double? flow = measurements.GetDouble(BrowseNames.MassFlow);
    EUInformation? unit = measurements[BrowseNames.MassFlow]?.EngineeringUnits;

    PumpSupervisionStatus? supervision = await pumps.ReadSupervisionAsync(entry.NodeId, cancellationToken: ct);
    foreach ((string group, PumpValue signal) in supervision?.ActiveSignals ?? [])
    {
        Console.WriteLine($"{nameplate?.SerialNumber}: {group}/{signal.Name}");
    }
}
```

Or the whole pump in one pass:

```csharp
PumpSnapshot snapshot = await pumps.ReadPumpAsync(entry.NodeId, cancellationToken: ct);
```

## Server hosting

### `AddPumps` and `ConfigurePumps`

| Call | Effect |
| --- | --- |
| `AddPumps(Action<PumpsServerOptions>? configure = null)` | Registers `PumpsNodeManager` through `PumpsNodeManagerFactory`, loads DI, IA, Machinery and Pumps, and claims the Device Integration address space |
| `ConfigurePumps(Func<IPumpsSetupContext, ValueTask>)` / `ConfigurePumps(Action<IPumpsSetupContext>)` | Runs once the address space is initialised. Several registrations run in registration order; an exception aborts server startup |

`IPumpsSetupContext` gives the configurator the initialised `Manager`, the
underlying DI context (`DiContext`), the hosting `CancellationToken` and
`GetRequiredService<T>()` for application services, e.g. the process
connection that supplies the values.

`PumpsNodeManager` derives from `DiNodeManager`, so
`ConfigureDevicesFor<PumpsNodeManager>()` works on it unchanged;
`ConfigurePumps` is the typed shortcut.

### Options

| `PumpsServerOptions` | Default | Meaning |
| --- | --- | --- |
| `OrganizeIntoMachinesFolder` | `true` | Also reference every pump from the OPC 40001-1 `Machines` folder. Turning it off hides pumps from Machinery clients |
| `LoadIndustrialAutomationModel` | `true` | Load OPC 10000-200 IA. Machinery types `MonitoringType/Status/Stacklight` with the IA `BasicStacklightType`, so without IA the Machinery model fails to load; there is rarely a reason to turn this off |
| `AdditionalNamespaceUris` | `[]` | Extra namespaces for a subclass that composes further models into the same address space |

### Load order

The load order is fixed: DI, then IA, then Machinery, then Pumps. IA has to be
in the address space before Machinery loads, and Pumps builds on both.

### Creating pumps

| `PumpsNodeManager` member | Meaning |
| --- | --- |
| `CreatePumpAsync(QualifiedName browseName, CancellationToken)` | Creates a pump below the DI `DeviceSet`. A browse name already taken there is rejected with `BadBrowseNameDuplicated` |
| `CreatePumpAsync(QualifiedName, NodeState? parent, CancellationToken)` | Same, below an explicit parent |
| `Pump(PumpState)` / `Pump(NodeId)` | A builder for a pump the manager already created (`null` for an unknown NodeId) |
| `Pumps` | Every pump created so far, in creation order |
| `NamespaceIndices` | The Pumps, DI and Machinery namespace indices, resolved once |

`CreatePumpAsync` does not use `DiNodeManager.CreateDeviceAsync`: that method
requires a `ComponentType` descendant, and OPC 40223 derives `PumpType`
directly from DI `TopologyElementType`, the base of `ComponentType`. Each pump gets its own instance NodeIds, is registered
before the builder is returned (groups added later are visible immediately),
and is marked `SubscribeToEvents` so clients can subscribe to it as an event
notifier.

### Coexisting with a server that already owns DI

`AddPumps` claims the Device Integration address space, so it cannot be
combined with `AddOpcUaDi` or another DI-owning registration; the second
registration throws `InvalidOperationException` at startup. A server that
already has a DI manager loads the model into it and configures it through
the DI pipeline:

```csharp
protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
    ISystemContext context, CancellationToken ct = default)
{
    var nodes = new NodeStateCollection();
    nodes.AddOpcUaDi(context);
    nodes.AddOpcUaIA(context);
    nodes.AddOpcUaMachinery(context);
    nodes.AddOpcUaPumps(context);
    return new ValueTask<NodeStateCollection>(nodes);
}
```

The [PumpDeviceIntegrationServer sample](../samples/DI/PumpDeviceIntegrationServer)
follows the same pattern in its own `DiNodeManager` subclass, because the same
manager also owns OpenUSD. It still source-generates its own copies of the
Machinery and Pumps models, which other test suites use as fixtures; moving it
onto these packages is left to a follow-up.

## Builder reference

### Nothing is materialised until it is asked for

The full `PumpType` surface is roughly 3,000 nodes. A pump that reports a flow
rate and a cavitation flag should publish those two, not 3,000 mostly empty
variables that cost every client a browse and a read to find out there is
nothing there. Every group and every leaf is therefore created on first use.
Touching `pump.Measurements` creates `Operational` and `Measurements`;
`SetAnalog(BrowseNames.MassFlow, …)` then creates `MassFlow` and, if a unit or
range is passed, its `EngineeringUnits` and `EURange` properties.

### Addressing children by browse name

Both the builder and the value sets address everything below a group by
browse name, using the generated `Opc.Ua.Pumps.BrowseNames` constants.
`DesignType` alone declares 78 optional variables and the model some 3,000
across its types, so a method or property per child would make an unusable
API and an unmaintainable one. Passing a generated constant keeps the call
compile-time checked all the same: a rename in the model breaks the build.

A name the group does not declare is **rejected** with an
`ArgumentException`, rather than silently creating a node. A server that
invents children is worse than one that omits them, because a client cannot
tell a vendor extension from a typo.

### `IPumpBuilder`

| Member | Returns a builder for |
| --- | --- |
| `Identification`, `Configuration`, `Operational`, `Events`, `Maintenance`, `Documentation` | The seven top-level groups (ports via `AddPort`) |
| `Design`, `Implementation`, `SystemRequirements` | The three `Configuration` sub-groups |
| `Measurements`, `Signals`, `Control`, `PumpActuation`, `MultiPump` | `Operational` sub-groups. `BypassActuation` and `ThrottleValveActuation` via `Operational.Nested(...)` |
| `Supervision(string)` | One of the seven `Events` categories (`BrowseNames.Supervision…`) |
| `MaintenanceCategory(string)` | One of the four `Maintenance` categories (`BrowseNames.…Maintenance`) |
| `WithNameplate(PumpNameplate)` | Fills `Identification` from the shared contract; see below |
| `AddPort(PumpPortKind, string name)` | Creates a drive, inlet or outlet port below `Ports` and returns its builder |
| `With(Action<PumpState>)` | Escape hatch for anything else on the pump node |
| `Pump`, `NodeId` | The pump itself |

### `IPumpGroupBuilder`

| Member | Effect |
| --- | --- |
| `Add(params string[] browseNames)` | Materialises children without setting values |
| `Set(string browseName, Variant value)` | Materialises a variable, sets its value, status `Good` and timestamp, and publishes the change |
| `SetAnalog(string, double, EUInformation?, Range?)` | As `Set`, and also publishes `EngineeringUnits` / `EURange` if the child declares them |
| `SetDiscrete(string, bool)` | Sets a boolean in either shape OPC 40223 uses (see below) |
| `Nested(string)` | Builder for a nested group (an object child) |
| `AddVibration(string name)` | Creates a named `<Vibration>` instance in a `Measurements` group; see [below](#vibration-measurements-and-other-placeholders) |
| `Child(string)` | The materialised child node, or `null` when the group declares no such child |
| `With(string, Action<BaseInstanceState>)` | Escape hatch: access levels, historizing, a method callback, `OnReadValue` |
| `Node` | The group node |

`SetDiscrete` covers both boolean shapes in the model:

- the `TwoStateDiscreteType` **variables** of the supervision groups
  (`Cavitation`, `Dry`, `MotorOverheat`, …), which carry the state
  themselves;
- the `DiscreteInputObjectType` / `DiscreteOutputObjectType` **objects** of the
  signal and actuation groups (`PumpOperation`, …), whose state sits one level
  down in `DiscreteInputValue` or `DiscreteOutputValue`.

### The nameplate

`WithNameplate` publishes every field of `PumpNameplate` that is set and
leaves the rest unmaterialised. `PumpNameplate.NodeId` and `MarkingsFolderId`
are ignored on the server side; they are filled in by the client when reading.

| Fields | Namespace |
| --- | --- |
| `Manufacturer`, `ManufacturerUri`, `Model`, `ProductCode`, `HardwareRevision`, `SoftwareRevision`, `DeviceClass`, `SerialNumber`, `ProductInstanceUri`, `AssetId`, `ComponentName` | OPC 10000-100 DI |
| `Location`, `InitialOperationDate`, `YearOfConstruction`, `MonthOfConstruction` | OPC 40001-1 Machinery |
| `DayOfConstruction`, `ArticleNumber`, `OrderProductCode`, `TypeOfProduct`, `Supplier`, `CountryOfOrigin`, `FabricationNumber`, `GTINCode`, `NationalStockNumber`, `PhysicalAddress` | OPC 40223 Pumps |

### Ports

`PortsGroupType` declares its ports as placeholders: which ports a pump has is
an instance property, not a type property. `AddPort` therefore creates the
instance from the port type and names it. The returned builder reaches the
port's own groups:

| `PumpPortKind` | Type | Groups |
| --- | --- | --- |
| `Drive` | `DrivePortType` | `Design`, `Measurements` |
| `InletConnection` | `InletConnectionPortType` | `Design`, `Implementation`, `Measurements`, `SystemRequirements` |
| `OutletConnection` | `OutletConnectionPortType` | `Design`, `Implementation`, `Measurements`, `SystemRequirements` |

```csharp
pump.AddPort(PumpPortKind.OutletConnection, "Discharge")
    .Set(BrowseNames.Direction, Variant.From(PortDirectionEnum.Out));
```

### Vibration measurements and other placeholders

OPC 40223 §7.32 declares vibration measurements as the OptionalPlaceholder
`<Vibration>` of `MeasurementsType`, typed `VibrationMeasurementType` (§7.33,
35 analog values after IEC 61987 plus `ReferenceStandardForVibrationMeasurement`).
A pump publishes one instance per measuring point, each named by the
application, which is what `AddVibration` creates:

```csharp
pump.Measurements.AddVibration("DriveEndBearing")
    .SetAnalog(BrowseNames.OverallVibrationVelocityRMS, 2.8,
        new EUInformation { DisplayName = new LocalizedText("mm/s") })
    .Set(BrowseNames.ReferenceStandardForVibrationMeasurement, Variant.From("ISO 10816-3"));
```

Asking again for an existing name returns the same instance. A group without
a `<Vibration>` placeholder rejects the call.

A placeholder is a template, not a child: `Nested("Vibration")` fails with
*"'Measurements' declares '<Vibration>' as a placeholder, not as a child -
create named instances with AddVibration(name)"*. The model has three more
placeholder kinds:

| Placeholder | Type | Builder API |
| --- | --- | --- |
| `<Vibration>` in `MeasurementsType` | `VibrationMeasurementType` | `AddVibration(name)` |
| `<Drive>`, `<InletConnection>`, `<OutletConnection>` in `PortsGroupType` | the three port types | `AddPort(kind, name)` |
| `<Marking>` in `MarkingsType` | `FileType` | none; a marking is a file, see [Documentation files](#documentation-files) |

### Documentation files

The `Documentation` group pairs each document link with a `FileType` object.
The library publishes the links; it does **not** implement the `FileType`
methods (`Open`, `Read`, …). To serve the files, attach the handlers yourself
through `With(browseName, node => …)`, or leave the file objects out and
publish only the links.

## Updating values at runtime

The builder is also how an application keeps values current, e.g. from a
polling loop against the pump controller:

```csharp
IPumpBuilder pump = manager.Pump(pumpNodeId)!;
pump.Measurements.SetAnalog(BrowseNames.MassFlow, reading.MassFlow);
pump.Supervision(BrowseNames.SupervisionProcessFluid)
    .SetDiscrete(BrowseNames.Cavitation, reading.Cavitation);
```

Every `Set`, `SetAnalog` and `SetDiscrete` stamps the value with status
`Good` and the current time and publishes the change. `PumpsNodeManager`
reports data changes by exception, so this publish is what makes a monitored
item fire; a client subscribed to `MassFlow` sees each update, and
`PumpsClientServerE2eTests` checks exactly that over a real subscription.
For a signal object the publish happens on the `DiscreteInputValue` /
`DiscreteOutputValue` child that carries the state, which is also the node a
client subscribes to (`PumpValue.NodeId`).

Keep the builders you need, as the
[PumpsServer sample](../samples/Pumps/PumpsServer/PumpFleet.cs) does, instead
of resolving them on every update. Values that are only ever read and never
subscribed to can instead be bound to their source once with
`With(browseName, node => ((BaseVariableState)node).OnReadValue = …)`.

## Client reference

### Creating the client

| Entry point | Use |
| --- | --- |
| `session.Pumps(telemetry)` | Extension on `ISession`; registers the OPC 40223 structured types with the session's encodeable factory |
| `new PumpsClient(session, telemetry)` | The same without the extension |
| `AddPumpsClient()` on `AddClient(...)` | Registers `PumpsClientFactory` and a `Func<CancellationToken, Task<PumpsClient>>` over the managed session, plus the DI client services |

The registered types (`PhysicalAddressDataType` and the five option sets)
decode into the generated classes instead of staying opaque
`ExtensionObject`s.

### Discovery

| Member | Meaning |
| --- | --- |
| `IsSupported` | The server publishes the Pumps namespace |
| `PumpTypeId`, `MachinesFolderId` | Resolved against the session's namespace table, `NodeId.Null` if absent |
| `EnumeratePumpsAsync()` | Every pump below the DI `DeviceSet` and the Machinery `Machines` folder, each returned once |
| `EnumeratePumpsUnderAsync(NodeId root)` | Pumps directly below one folder |
| `DiscoverPumpsAsync()` | The NodeIds of `EnumeratePumpsAsync` |
| `IsPumpAsync(NodeId typeDefinition)` | Whether a type is or derives from `PumpType` |
| `ResolveIdentificationAsync`, `ResolveDiGroupAsync`, `ResolvePumpsChildAsync`, `ResolveOperationalAsync`, `ResolvePortsAsync` | Resolve a group's NodeId for use with `ReadValueSetAsync` |

`Topology` exposes the underlying `DiTopologyClient`.

### Reading groups

| Member | Returns |
| --- | --- |
| `ReadNameplateAsync(pump)` | `PumpNameplate?` |
| `ReadMarkingsAsync(markingsFolder)` | The entries of the nameplate's `Markings` folder (`PumpNameplate.MarkingsFolderId`) |
| `ReadConfigurationAsync(pump, options)` | `PumpConfigurationData?` with `Design`, `Implementation`, `SystemRequirements` |
| `ReadOperationalAsync(pump, options)` | `PumpOperationalData?` with all operational groups and `MultiPump` |
| `ReadMeasurementsAsync`, `ReadSignalsAsync` | One `PumpValueSet` |
| `ReadMultiPumpAsync(pump)` | `MultiPumpConfiguration?` |
| `ReadSupervisionAsync(pump, options)` | `PumpSupervisionStatus?` |
| `ReadMaintenanceAsync(pump, options)` | `PumpMaintenanceData?` |
| `ReadDocumentationAsync(pump)` | The document links as a `PumpValueSet` |
| `EnumeratePortsAsync`, `ReadPortsAsync` | `PumpPortDescriptor`s with their groups |
| `ReadPumpAsync(pump, options)` | `PumpSnapshot`: everything above in one pass |
| `ReadValueSetAsync(group, options)` | Any group by NodeId |

A `null` result means the pump does not publish that group.

### Value sets, not records

The large groups are open sets of optional variables, and a real pump
publishes a handful of them. A record with one property per variable would be
mostly `null` and would need revising with every revision of the
specification. `ReadValueSetAsync` browses the group instead and returns a
`PumpValueSet` filled from what the server actually publishes: three entries
for a pump that publishes three `DesignType` parameters, 78 for one that
publishes all of them.

- Discrete signal objects are followed automatically, and their value is
  reported under the object's name (`PumpOperation`, not
  `DiscreteInputValue`).
- Other nested OPC 40223 objects are groups of their own and come back in
  `PumpValueSet.Groups`, one level deep: the `<Vibration>` instances below
  `Measurements`, for example.

  ```csharp
  foreach ((string point, PumpValueSet vibration) in measurements.Groups)
  {
      double? velocity = vibration.GetDouble(BrowseNames.OverallVibrationVelocityRMS);
  }
  ```

  Objects of the base models, such as the `FileType` documents, are left
  out; they carry no values.

The closed parts of the specification are records: `PumpNameplate` (the
nameplate list is fixed by OPC 40223) and `MultiPumpConfiguration`, which
maps all eleven `MultiPumpType` variables (§7.34), including the string
arrays `DistributionPriority`, `PumpCollectiveIDs` and `RedundantPumpIDs`.

### Read options

Each `PumpValue` carries the engineering unit and instrument range next to the
reading. An OPC 40223 measurement without its unit cannot be interpreted
(350 kPa and 350 Pa are different machines), and the specification fixes no
unit for its analog variables. That costs one extra round trip per group.

| `PumpReadOptions` | Default | |
| --- | --- | --- |
| `IncludeEngineeringUnits` | `true` | Read `EngineeringUnits` |
| `IncludeRanges` | `true` | Read `EURange` |
| `IncludeTimestamps` | `false` | Return source timestamps |
| `PumpReadOptions.ValuesOnly` | | One browse and one read, no metadata |

Read once with the defaults and poll with `ValuesOnly`: the metadata does not
change.

### Supervision

`SupervisionType` spreads about 150 boolean fault signals over seven groups.
What a caller usually wants is not one named flag but the answer to "what is
this pump complaining about":

```csharp
PumpSupervisionStatus? supervision = await pumps.ReadSupervisionAsync(pumpId, cancellationToken: ct);

if (supervision?.HasActiveSignals == true)
{
    foreach ((string group, PumpValue signal) in supervision.ActiveSignals)
    {
        Console.WriteLine($"{group}/{signal.Name}");
    }
}
```

`ActiveSignals` yields only signals that are currently raised. A signal
published as `false`, or with a bad status, is left out; an empty result is
the healthy case.

### Maintenance

`PumpMaintenanceData` exposes the four categories as value sets plus three
derived answers: `StateOfTheItem` and `MaintenanceLevel` (from
`GeneralMaintenance`) and `HasFailure` (from `BreakdownMaintenance`).

## Contracts reference

| Type | Kind | Filled by |
| --- | --- | --- |
| `PumpEntry` | Record: NodeId, browse name, display name, type definition | Discovery |
| `PumpNameplate` | Closed record | `WithNameplate` / `ReadNameplateAsync` |
| `PumpValue` | One reading with status, source timestamp, unit and range; `AsDouble`, `AsBoolean`, `AsString`, `AsStringArray`, `AsUInt32`, `AsEnum<T>`, `AsDateTime` | Value sets |
| `PumpValueSet` | Browse-name-keyed readings; `GetDouble`, `GetBoolean`, `GetString`, `GetStringArray`, `GetUInt32`, `GetDateTime`, `GetEnum<T>`, indexer, `Names`, `Contains`; nested groups in `Groups` / `Group(name)` | All open groups |
| `PumpConfigurationData`, `PumpOperationalData`, `PumpMaintenanceData` | Records of value sets | Group reads |
| `MultiPumpConfiguration` | Record of all eleven `MultiPumpType` variables | `ReadMultiPumpAsync` |
| `PumpSupervisionStatus` | Seven value sets, `ActiveSignals`, `HasActiveSignals` | `ReadSupervisionAsync` |
| `PumpPortDescriptor` | Port kind, direction, category, id carrier, four value sets | Port reads |
| `PumpSnapshot` | Everything above for one pump | `ReadPumpAsync` |

Absent names read as `null`, never as an exception, and a reading with a bad
status reads as no value. Integer readings convert to `double` in `GetDouble`.

## Conformance matrix

Status key: ✅ implemented and tested over a real session ·
🧪 implemented, tested in-process only · 🔧 implemented, not covered by a test ·
❌ not shipped.

### Profiles and conformance units

OPC 40223 1.0 defines ten server conformance units (§9.1, Table 152) and two
server profiles (§9.2, Tables 153–155). It defines no client facets.

| Profile | URI | Status |
| --- | --- | --- |
| Pump Base Server Profile (Table 154) | `http://opcfoundation.org/UA-Profile/Pumps/Server/Base` | Partial: *Base System* and *Pump PumpType Mandatory Nodes* are met, but the mandatory *3:Machine Identification Writable Server Facet* is not, because client writes to the identification are not stored |
| Pump Advanced Server Profile (Table 155) | `http://opcfoundation.org/UA-Profile/Pumps/Server/Advanced` | ❌ Not met: the mandatory alarm, notifier and historical-access facets are not implemented |

`PumpsNodeManager` therefore advertises neither profile URI.

| Conformance unit (Table 152) | Status | Where |
| --- | --- | --- |
| Base System | ✅ | `PumpType` instances, see the server table below |
| Pump PumpType Mandatory Nodes | ✅ | `CreatePumpAsync` materialises every mandatory node |
| Pump Identification | ✅ | `Identification` nameplate |
| PumpClass Data | 🔧 | `PumpClass` in the `Design` group can be set through the group builder; nothing enforces it or tests it |
| Pump Connection Port | ✅ | Inlet and outlet connection ports |
| Pump Drive Port | ✅ | Drive port |
| Pump Control | 🧪 | `PumpActuation`; `Control` is not covered by a test |
| Historizing | ❌ | No historical access for measurements |
| Limit Alarm Status | ❌ | No `LimitAlarmType` alarms |
| Supervision Health Status | ❌ | No `DeviceHealthDiagnosticAlarmType` alarms |

The rows below follow the specification's structure.

### Model

| Area | Status | Source | Tests |
| --- | --- | --- | --- |
| Namespace URI, all 51 object types, 13 enumerations, 5 option sets, `PhysicalAddressDataType` generated | ✅ | [`Model/`](../src/Opc.Ua.Pumps/Model) | `PumpsModelTests` |
| DI group names outside the Pumps `BrowseNames` (`PumpsModel.DiGroups`) | 🧪 | [`PumpsModel`](../src/Opc.Ua.Pumps/PumpsModel.cs) | `PumpsModelTests` |
| Port type classification (`PumpsModel.ClassifyPort`) | 🧪 | same | `PumpsModelTests` |
| Model loading DI → IA → Machinery → Pumps | 🧪 | [`PumpsNodeManager`](../src/Opc.Ua.Pumps.Server/PumpsNodeManager.cs) | `PumpsNodeManagerTests` |

### Server

| Area | Status | Source | Tests |
| --- | --- | --- | --- |
| `PumpType` instance below `DeviceSet` with its own instance NodeIds | ✅ | [`PumpsNodeManager`](../src/Opc.Ua.Pumps.Server/PumpsNodeManager.cs) | `PumpsNodeManagerTests`; `PumpsClientServerE2eTests` |
| Organized from the Machinery `Machines` folder, optional | ✅ | same | `PumpsNodeManagerTests`; `PumpsClientServerE2eTests` |
| Duplicate pump names rejected | 🧪 | same | `PumpsNodeManagerTests` |
| Pump is an event notifier | 🧪 | same | `PumpsNodeManagerTests` |
| Lazy materialisation of groups and leaves | 🧪 | [`PumpGroupBuilder`](../src/Opc.Ua.Pumps.Server/Builders/PumpGroupBuilder.cs) | `PumpBuilderTests` |
| Materialised children carry reference type, type definition, data type and access level | ✅ | same | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| Undeclared browse names rejected | 🧪 | same | `PumpBuilderTests` |
| `Identification` nameplate across DI / Machinery / Pumps | ✅ | [`PumpBuilder`](../src/Opc.Ua.Pumps.Server/Builders/PumpBuilder.cs) | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `Configuration`: `Design`, `SystemRequirements` | ✅ | [`PumpGroupBuilder`](../src/Opc.Ua.Pumps.Server/Builders/PumpGroupBuilder.cs) | `PumpsClientServerE2eTests` |
| `Configuration`: `Implementation` | 🔧 | same | — (same code path) |
| `Operational`: `Measurements` with unit and range | ✅ | same | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `Operational`: `Signals` (discrete input objects) | ✅ | same | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `Operational`: `PumpActuation` (discrete output objects) | 🧪 | same | `PumpBuilderTests` |
| `Operational`: `MultiPump`, all eleven variables | ✅ | same | `PumpsClientServerE2eTests` |
| `Operational`: `Control`, `BypassActuation`, `ThrottleValveActuation` | 🔧 | same | — |
| `Measurements`: `<Vibration>` instances of `VibrationMeasurementType` | ✅ | [`PumpGroupBuilder`](../src/Opc.Ua.Pumps.Server/Builders/PumpGroupBuilder.cs) | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `Events`: seven supervision categories | ✅ | same | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `Maintenance`: four categories | ✅ | same | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `Ports`: drive, inlet and outlet ports with their groups | ✅ | [`PumpBuilder`](../src/Opc.Ua.Pumps.Server/Builders/PumpBuilder.cs) | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `Documentation` links | 🔧 | builder | — |
| `Documentation` `FileType` methods | ❌ | — | Optional (§7.5); attach through `With(...)` |
| `MarkingsType` `<Marking>` files | ❌ | — | Optional; `FileType`, see [Documentation files](#documentation-files) |
| CU *Supervision Health Status*: NE 107 alarms (`2:DeviceHealthDiagnosticAlarmType` subtypes) for supervision signals | ❌ | — | Optional (Table 152/155). OPC 40223 defines no event types of its own; the pump is an event notifier, the alarms are not emitted |
| CU *Limit Alarm Status*: `0:LimitAlarmType` alarms for measurements | ❌ | — | Optional (Table 152/155) |
| Data changes to subscribed clients on runtime updates | ✅ | [`PumpGroupBuilder`](../src/Opc.Ua.Pumps.Server/Builders/PumpGroupBuilder.cs) | `PumpBuilderTests`; `PumpsClientServerE2eTests` |
| `AddPumps` / `ConfigurePumps` hosting | ✅ | [`OpcUaPumpsServerBuilderExtensions`](../src/Opc.Ua.Pumps.Server/Hosting/OpcUaPumpsServerBuilderExtensions.cs) | `PumpsClientServerE2eTests` |
| Rejecting a second DI owner | 🔧 | same | — |

### Client

| Area | Status | Source | Tests |
| --- | --- | --- | --- |
| Discovery from `DeviceSet` and `Machines`, de-duplicated, subtypes included | ✅ | [`PumpsClient`](../src/Opc.Ua.Pumps.Client/PumpsClient.cs) | `PumpsClientServerE2eTests` |
| Nameplate | ✅ | [`PumpsClient.Identification`](../src/Opc.Ua.Pumps.Client/PumpsClient.Identification.cs) | `PumpsClientServerE2eTests` |
| Markings | 🔧 | same | — |
| Configuration, measurements, signals, supervision, maintenance | ✅ | [`PumpsClient.Groups`](../src/Opc.Ua.Pumps.Client/PumpsClient.Groups.cs) | `PumpsClientServerE2eTests` |
| Multi-pump configuration, all eleven variables | ✅ | same | `PumpsClientServerE2eTests` |
| Documentation links | 🔧 | same | — |
| Value sets with units, ranges and discrete objects | ✅ | [`PumpsClient.Values`](../src/Opc.Ua.Pumps.Client/PumpsClient.Values.cs) | `PumpValueSetTests`; `PumpsClientServerE2eTests` |
| Nested OPC 40223 objects (vibration measurements) as `PumpValueSet.Groups` | ✅ | same | `PumpsClientServerE2eTests` |
| Ports with kind and groups | ✅ | [`PumpsClient.Ports`](../src/Opc.Ua.Pumps.Client/PumpsClient.Ports.cs) | `PumpsClientServerE2eTests` |
| Whole-pump snapshot | ✅ | [`PumpsClient.Snapshot`](../src/Opc.Ua.Pumps.Client/PumpsClient.Snapshot.cs) | `PumpsClientServerE2eTests` |
| Supervision evaluation (`ActiveSignals`) | 🧪 | [`PumpSupervisionStatus`](../src/Opc.Ua.Pumps/Contracts/PumpSupervisionStatus.cs) | `PumpValueSetTests` |
| `AddPumpsClient` hosting | 🔧 | [`OpcUaPumpsClientBuilderExtensions`](../src/Opc.Ua.Pumps.Client/Hosting/OpcUaPumpsClientBuilderExtensions.cs) | — |

## Samples

| Sample | Shows |
| --- | --- |
| [PumpsServer](../samples/Pumps/PumpsServer) | `AddPumps` / `ConfigurePumps` with two pumps: nameplate, every group builder, units and ranges, ports, documentation links, and a simulation that publishes runtime updates to subscribers |
| [PumpsClient](../samples/Pumps/PumpsClient) | `PumpsClient` discovery, `ReadPumpAsync` snapshots, supervision and maintenance evaluation, `ValuesOnly` polling, and streaming subscriptions on `PumpValue.NodeId` |
| [PumpDeviceIntegrationServer](../samples/DI/PumpDeviceIntegrationServer) | A datasheet-driven pump simulation in its own `DiNodeManager` subclass with the Pumps model loaded next to OpenUSD, published data changes, and an OpenUSD twin per pump |
| [WoT aggregation](../samples/WotCon/README.md) | A DI / Machinery / Pumps `PumpType` instance loaded at runtime from WoT documents, with no Pumps assembly referenced |
| [SiteCompositionServer](../samples/OpenUsd/SiteCompositionServer) | A supervisory server composing the pump and generator servers into one scene |

## Testing

```bash
dotnet test tests/Opc.Ua.Pumps.Tests
```

| Fixture | Covers |
| --- | --- |
| `PumpsModelTests` | The generated model against the specification |
| `PumpsNodeManagerTests` | Model loading, pump creation, folders, NodeIds |
| `PumpBuilderTests` | Every builder shape in-process |
| `PumpValueSetTests` | Value-set and supervision evaluation |
| `PumpsClientServerE2eTests` | A server through `AddPumps`/`ConfigurePumps`, read back by `PumpsClient` over a real session |

The end-to-end test is the one that matters for anything a client sees. An
in-process assertion on the typed model passes even when a client sees
nothing, as the [generator gap](#generator-gap-this-model-uncovered) below
shows.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `CS0104: 'BrowseNames' is an ambiguous reference` | `using Opc.Ua;` and `using Opc.Ua.Pumps;` both bring a `BrowseNames` class (and `System` a `Range`) | Alias them: `using BrowseNames = Opc.Ua.Pumps.BrowseNames;`, `using Range = Opc.Ua.Range;` |
| `ArgumentException: '…' declares no child named '…'` | The browse name is not declared by that group, or belongs to another group | Use a generated `BrowseNames` constant on the group that declares it (e.g. `Cavitation` is in `SupervisionProcessFluid`, not `Measurements`) |
| Browse path to `Identification`/`Configuration`/`Operational`/`Maintenance` does not resolve | Qualified with the Pumps namespace | Use the DI namespace index; see [The model](#the-model) |
| `InvalidOperationException`: DI address space already owned | `AddPumps` combined with `AddOpcUaDi` or another DI owner | [Load the model into the existing manager](#coexisting-with-a-server-that-already-owns-di) |
| `BadBrowseNameDuplicated` from `CreatePumpAsync` | A pump of that name already exists below the parent | Use unique names, or `manager.Pump(nodeId)` to reconfigure |
| Machinery clients don't find the pumps | `OrganizeIntoMachinesFolder = false` | Leave it on |
| Machinery model fails to load | `LoadIndustrialAutomationModel = false` | Leave it on |
| `ArgumentException: … declares '<Vibration>' as a placeholder` | `Nested("Vibration")` on a placeholder | Use `AddVibration(name)` |
| A measurement has no unit on the client | Server set it without `EngineeringUnits`, or client read with `ValuesOnly` | Pass the unit to `SetAnalog`; read metadata once with the defaults |
| `PhysicalAddress` or option sets arrive as `ExtensionObject` | Client not created through `session.Pumps(...)` | Use the extension |
| A vendor port comes back as `PumpPortKind.Unknown` | Vendor subtype of `PortType` | Expected; its groups are still read |

## Model provenance

`src/Opc.Ua.Pumps/Model/Opc.Ua.Pumps.NodeSet2.xml` is the unmodified OPC
Foundation publication from
[`UA-Nodeset/latest/Pumps`](https://github.com/OPCFoundation/UA-Nodeset/tree/latest/Pumps):
model version 1.0.0, published 2021-04-19, requiring UA 1.04.7, DI 1.02.2 and
Machinery 1.0.0. The sibling `Opc.Ua.Pumps.NodeSet2.csv` pins the
symbol-to-NodeId mapping, so a client that caches NodeIds across builds keeps
working; the generator fails the build when the two disagree.

The `RequiredModel` versions are a lower bound, not a pin. The repository's
DI and Machinery models are newer and satisfy them.

## Generator gap this model uncovered

The generated state classes implement
`NodeState.CreateChild(context, browseName)` for every optional child the
model declares, which is what lets one builder cover all ~3,000 of them. That
generic path creates a child of the right CLR type and browse name, but does
**not** stamp the modelling metadata: the child comes back with no
`ReferenceTypeId`, no `TypeDefinitionId` and `BaseDataType` as its data type.
(The per-child `AddXxx(context)` helpers go through the model factory and do
set all of it.)

Such a node looks fine from inside the server and can even be read by NodeId,
but it is **invisible to a client**: every browse a client makes is filtered
on `HierarchicalReferences`, and the child has no reference type to match.
The same applies to `TranslateBrowsePathsToNodeIds`.

`PumpGroupBuilder` therefore completes each newly created child from its
instance declaration on the type, copying `ReferenceTypeId`,
`TypeDefinitionId`, `DataType`, `ValueRank`, access levels and sampling
interval before registering it, and filling only members the generic path
left unset. `PumpsClientServerE2eTests` is what catches a regression here.

## MCP tools

`opcua-mcp --profile pumps` exposes eight read-only tools: discovery,
nameplate, markings, finite semantic groups, ports, value sets, snapshots
and type classification. It does not invent pump start/stop operations.

Discover with `pumps_list_pumps`, then call `pumps_read_group` with the
returned pump NodeId and a facet such as `Measurements`, `Supervision` or
`Maintenance`. `pumps_list_markings` takes the **markings folder** NodeId
returned by `pumps_read_nameplate`, not the pump NodeId.

```json
{
  "pumpNodeId": "ns=2;s=Pump1",
  "facet": "Measurements",
  "includeMetadata": true,
  "includeTimestamps": true,
  "sessionName": "plant"
}
```

Values preserve UA type, quality, engineering units and timestamps.
Discovery deduplicates pumps visible under both `DeviceSet` and `Machines`.
Paged results describe a live enumeration, not a durable snapshot.

Embed `Opc.Ua.Mcp.Pumps` with `AddOpcUaMcpPumps()` and `WithOpcUaPumpsTools(...)`.
See [industrial companion MCP tools](CompanionMcp.md) for composition and
session/error contracts.

## See also

- [Device Integration developer guide](DeviceIntegration.md): `DiNodeManager`,
  `ConfigureDevicesFor`, the DI client
- [Machinery developer guide](Machinery.md): the `Machines` folder and the
  OPC 40001-1 building blocks `PumpType` inherits
- [Subscriptions](Subscriptions.md): monitored items and data-change delivery
- [OPC 40223 on the OPC Foundation reference site](https://reference.opcfoundation.org/specs/OPC-40223)
