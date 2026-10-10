# OPC UA for Weighing Technology (OPC 40200)

The `Opc.Ua.Scales*` packages implement
[OPC 40200](https://reference.opcfoundation.org/specs/OPC-40200/full)
Weighing Technology 2.00, the companion specification for scales and scale
systems. It builds on OPC 40001-1 Machinery, OPC 10000-100 Device
Integration and OPC 30050 PackML.

- **Server side:** fluent builders publish scales with a working runtime.
  That runtime covers weighing arithmetic, zero and tare, product handling,
  statistics, the type-specific methods, recipes, PackML state machines,
  events and alarms.
- **Client side:** a client discovers the scales, reads them into typed
  snapshots, drives their methods and streams weights and notifications.

## Contents

- [Library layout](#library-layout)
- [The model](#the-model)
- [Where a scale lives](#where-a-scale-lives)
- [Quick start — server](#quick-start--server)
- [Quick start — client](#quick-start--client)
- [Server hosting](#server-hosting)
- [Builder reference](#builder-reference)
- [Runtime reference](#runtime-reference)
- [Client reference](#client-reference)
- [Contracts reference](#contracts-reference)
- [Conformance matrix](#conformance-matrix)
- [Samples](#samples)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [Model provenance](#model-provenance)
- [Where the specification and the NodeSet disagree](#where-the-specification-and-the-nodeset-disagree)
- [MCP tools](#mcp-tools)
- [See also](#see-also)

## Library layout

| Package | Contents |
| --- | --- |
| `Opc.Ua.PackML` | The source-generated OPC 30050 PackML model. It also has `PackMLStateMachineController`, which drives the nested base / machine / execute state machines with their transition events |
| `Opc.Ua.Scales` | The source-generated OPC 40200 model. `ScalesModel` maps the 14 scale kinds, their product types and the Annex C notifications. `ScalesProfiles` holds the 21 facet URIs. The package also has the contracts both sides share |
| `Opc.Ua.Scales.Server` | `ScalesNodeManager`, the builders `IScaleBuilder` / `IScaleSystemBuilder` / `IScaleModuleBuilder` / `IProductionPresetBuilder`, the runtime handles and controllers, and the `AddScales()` / `ConfigureScales()` hosting pipeline |
| `Opc.Ua.Scales.Client` | `ScalesClient` for discovery, weighing, products, PackML, the type methods, recipes, snapshots and streaming. Also `AddScalesClient()` |

The contracts (`ScaleIdentification`, `WeighingRangeDefinition`,
`ScaleReading`, `ScaleSnapshot`, …) live in the model package. The server
fills them from its process and the client reads them back, and neither side
references the other.

All four packages target the stack's library frameworks. They are marked
NativeAOT-compatible on `net10.0`, and so are both samples.

## The model

| Model URI | C# namespace | Model loader |
| --- | --- | --- |
| `http://opcfoundation.org/UA/Scales/V2/` | `Opc.Ua.Scales` | `AddOpcUaScales` |
| `http://opcfoundation.org/UA/PackML/` | `Opc.Ua.PackML` | `AddOpcUaPackML` |

A Scales server must also expose the DI, IA, Machinery and PackML namespaces
(§13.1, Table 186). `ScalesNodeManager` loads all five into one node manager.

`ScaleDeviceType` is abstract. Every scale is one of 14 concrete subtypes,
and `ScaleKind` names them:

| `ScaleKind` | ObjectType | Product type | Facet | Runtime controller |
| --- | --- | --- | --- | --- |
| `Simple` | `SimpleScaleType` | `SimpleProductType` | Simple Scale | — |
| `Laboratory` | `LaboratoryScaleType` | `SimpleProductType` | Laboratory Scale | `LaboratoryController` |
| `Hopper` | `HopperScaleType` | `SimpleProductType` | Hopper Scale | `HopperController` |
| `WeighingModule` | `WeighingModuleType` | `SimpleProductType` | Weighing Bridge | — |
| `AutomaticFilling` | `AutomaticFillingScaleType` | `AutomaticFillingProductType` | AutomaticFillingScale | `AutomaticFillingController` |
| `Catchweigher` | `CatchweigherType` | `CatchweigherProductType` | Catchweigher | zones in `ScaleProductionPreset` |
| `Checkweigher` | `CheckweigherType` | `CheckweigherProductType` | Checkweigher | checkweigher statistics |
| `AutomaticWeightPriceLabeler` | `AutomaticWeightPriceLabelerType` | `AutomaticWeightPriceLabelerProductType` | AutomaticWeightPriceLabeler | zones |
| `Continuous` | `ContinuousScaleType` | `ContinuousProductType` | Continuous Scale | `ContinuousController` |
| `LossInWeight` | `LossInWeightScaleType` | `ContinuousProductType` | LossInWeight Scale | `LossInWeightController` |
| `PieceCounting` | `PieceCountingScaleType` | `PieceCountingProductType` | PieceCountingScale | `PieceCountingController` |
| `Recipe` | `RecipeScaleType` | `RecipeProductType` | RecipeScale | `RecipeController` |
| `TotalizingHopper` | `TotalizingHopperScaleType` | `TotalizingHopperProductType` | Totalizing Hopper Scale | — |
| `Vehicle` | `VehicleScaleType` | `VehicleProductType` | Vehicle Scale | `VehicleController` |

- `ScalesModel.ObjectTypeOf`, `KindOf`, `ProductTypeOf` and `AllKinds` map
  between the kinds and the type NodeIds.
- `ScalesProfiles.FacetOf(kind)` returns the facet URI of a kind.
- `ScaleSystemType` groups several scales. `FeederModuleType` and
  `PrinterModuleType` are sub-devices of a scale.

The weight itself is a `WeightItemType` variable (§9.3) with a `WeightType`
value of `Gross`, `Net` and `Tare`. Properties such as `Overload`,
`Underload`, `TareMode`, `WeightStable`, `InsideZero` and `CurrentRangeId`
qualify it. Every scale has `CurrentWeight`; `RegisteredWeight` holds the last
value `RegisterWeight` recorded.

## Where a scale lives

A scale is both a DI device and a Machinery machine, so a conforming server
publishes the top-level node in two places:

- as a child of the DI `DeviceSet`, and
- through an `Organizes` reference from the Machinery `Machines` folder.
  The Scales specification never mentions this folder. It is still required
  transitively, because the mandatory *Machinery Machine Identification*
  facet includes the CU *Machinery Find Machines*.

`ScalesNodeManager` does both by default
(`ScalesServerOptions.OrganizeIntoMachinesFolder`). With the option turned
off, the server no longer advertises the facets that include *Machinery
Machine Identification*: Base Scale and every kind facet, Scale System,
Feeder Module and Printer Module. The production preset and SI unit facets
do not include it and stay advertised.

Everything below a top-level node follows the DI modular-device pattern
(§6.3):

```text
DeviceSet
├── PackingLine1              ScaleSystemType      (notifier)
│   └── SubDevices
│       └── Checkweigher1     CheckweigherType     (notifier, HasNotifier from PackingLine1)
│           └── SubDevices
│               ├── Infeed    FeederModuleType     (notifier, HasNotifier from Checkweigher1)
│               └── Labeler   PrinterModuleType    (notifier, HasNotifier from Checkweigher1)
└── AnalyticalBalance         LaboratoryScaleType  (notifier)
```

- **SupportedTypes.** The NodeSet's `SupportedTypes` folders carry no
  `Organizes` references. The builders add them per instance.
- **Event notifiers.** Every event source is a notifier: scale systems,
  scales, weighing modules, and feeder and printer modules. A nested source is
  linked from its nearest notifier ancestor by `HasNotifier`. A client can
  therefore subscribe to a nested scale directly, or to the system and
  receive the events of everything below it.

## Quick start — server

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(options => { /* application name, endpoints, … */ })
    .AddScales()
    .ConfigureScales(async context =>
    {
        ScalesNodeManager manager = context.Manager;
        ScaleHandle scale = await manager.CreateScaleAsync(
            new QualifiedName("AnalyticalBalance", manager.InstanceNamespaceIndex),
            ScaleKind.Laboratory,
            b => b
                .WithIdentification(new ScaleIdentification
                {
                    Manufacturer = new LocalizedText("Contoso Lab"),
                    SerialNumber = "AB-220",
                    ProductInstanceUri = "urn:contoso:weighing:AB-220"
                })
                .WithWeighingRange(new WeighingRangeDefinition(0, 0.22, 0.0000001, 0.000001))
                .WithUnit(ScaleUnits.Kilogram)
                .WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram)
                .WithWeightDetails()
                .WithZeroAndTare()
                .WithRegisterWeight()
                .WithTypeFeatures(),
            context.CancellationToken);

        // Keep the handle; the process publishes loads through it.
        scale.PublishLoad(0.1);
    });
```

`WithIdentification` and at least one `WithWeighingRange` are required,
because `Identification` and `<ListOfWeighingRanges>` are mandatory (§7.4.2).
Every other member is materialised only when a builder call asks for it, and
the runtime binds exactly what was materialised. For example, a scale built
without `WithZeroAndTare` has no `SetZero` method, not one that fails.

## Quick start — client

```csharp
ScalesClient scales = session.Scales(telemetry);   // registers the OPC 40200 structures
if (!scales.IsSupported)
{
    return;
}

await foreach (ScaleEntry entry in scales.EnumerateScalesAsync(ct))
{
    ScaleSnapshot snapshot = await scales.ReadSnapshotAsync(entry, ct);
    Console.WriteLine($"{entry.DisplayName.Text}: {snapshot.CurrentWeight.Gross}");
}

await scales.SetTareAsync(scaleId, ct);
ScaleReading? registered = await scales.RegisterWeightAsync(scaleId, ct);

await foreach (ScaleReading reading in scales.ObserveWeightAsync(scaleId, cancellationToken: ct))
{
    Console.WriteLine(reading.Net);
}
```

Create the client through `session.Scales(...)`. It registers the OPC 40200
encodeable types, so `WeightType` and the recipe structures decode into their
generated classes. Without it they arrive as `ExtensionObject`.

## Server hosting

### `AddScales` and `ConfigureScales`

- **`AddScales(configure)`** registers `ScalesNodeManager` and validates the
  options. It also claims the Device Integration address space, because the
  node manager loads DI itself.
- **`ConfigureScales(...)`** registers a delegate that runs once the address
  space exists. It comes in sync and async overloads.
  - Delegates run in registration order.
  - An exception aborts server startup.
  - The delegate receives an `IScalesSetupContext` with the `Manager`, the
    `CancellationToken` and `GetRequiredService<T>()`.

A second DI owner, such as `AddOpcUaDi`, is rejected with an
`InvalidOperationException` at registration time.

### Options

| `ScalesServerOptions` | Default | Meaning |
| --- | --- | --- |
| `OrganizeIntoMachinesFolder` | `true` | Add top-level scales and systems to the Machinery `Machines` folder; required for every facet that includes *Machinery Machine Identification* |
| `RequireSiUnits` | `false` | Accept only SI mass units as method inputs and advertise the *International System of Units* facet |
| `PackMLInitialState` | `Idle` | The state a PackML state machine starts in (`Aborted`, `Stopped` or `Idle`) |
| `ZeroSettingRange` | `0.04` | The zero band as a fraction of the capacity, for `SetZero`, `InsideZero` and underload |
| `AdditionalNamespaceUris` | `[]` | Extra namespaces for vendor extensions |

### Creating scales

| `ScalesNodeManager` member | Purpose |
| --- | --- |
| `CreateScaleAsync(name, kind, configure[, parent])` | A scale below the `DeviceSet`, or below an explicit parent |
| `CreateScaleSystemAsync(name, configure)` | A scale system with its scales |
| `Scales`, `ScaleSystems`, `FindScale(nodeId)` | The handles of everything created, weighing modules and system scales included |
| `LockService` | The DI `ILockService` behind product locking |
| `NamespaceIndices` | The runtime indices of the Scales, DI, Machinery, PackML and IA namespaces |
| `ServerProfiles`, `ConformanceUnits` | What the server advertises, derived from what was built |

## Builder reference

### `IScaleBuilder`

| Call | Materialises |
| --- | --- |
| `WithIdentification(identification)` | The mandatory Machinery `Identification` add-in, mirrored onto the DI nameplate. Manufacturer, SerialNumber and ProductInstanceUri are required |
| `WithWeighingRange(range)` | One `WeighingRangeElementType`. Call it once per range of a multi-range scale; ranges are ordered by capacity |
| `WithUnit(unit)` | The engineering unit of every weight (default kilogram) |
| `WithAllowedEngineeringUnits(units)` | `AllowedEngineeringUnits`, which `SetPresetTare` checks against (§7.4.3). Published automatically with the scale unit when a method takes a unit |
| `WithMinimalWeight(weight)` | `MinimalWeight` |
| `WithLegalForTrade()` | `LegalForTrade`. Weights round to the verification interval `e` instead of `d` |
| `WithWeightDetails()` | The optional `WeightItemType` properties: `WeightStable`, `InsideZero`, `CenterOfZero`, `GrossNegative`, `CurrentRangeId`, `WeightId`, `HighResolutionValue`, `PrintableValue` |
| `WithZeroAndTare(presetTare)` | `SetZero`, `SetTare`, `ClearTare` and, unless `presetTare` is false, `SetPresetTare` |
| `WithRegisterWeight()` | `RegisterWeight` and `RegisteredWeight` |
| `WithProductionPreset(configure)` | `ProductionPreset` with products; see below |
| `WithProductionOutput()` | `ProductionOutput` statistics (`CheckweigherStatisticType` counters on a checkweigher) |
| `WithPackMLState()` | The PackML `State` machine, driven by `PackMLStateMachineController` |
| `WithMachineryBuildingBlocks()` | `MachineryItemState`, `MachineryOperationMode` and the `MachineryBuildingBlocks` folder, wired as add-ins |
| `WithProcessState(id, message)` | `ProcessStateId` and `ProcessStateMessage` |
| `WithTypeFeatures()` | Every optional member of the kind, for example the laboratory draft shields, the vehicle weighing methods, recipe management, or the continuous-scale figures and master totalizer |
| `WithRecipeFiles()` | A `RecipeFile` on every recipe of a recipe scale (*FileRecipeManagement*) |
| `AddTotalizer(name)` | A `<Totalizer>` on a continuous or loss-in-weight scale |
| `AddFeederModule(name, configure)` / `AddPrinterModule(name, configure)` | Sub-devices below `SubDevices` |
| `AddWeighingModule(name, configure)` | A `WeighingModuleType` sub-device. It is a scale of its own with its own builder and handle |
| `With<TState>(configure)` | Direct access to the typed node for anything not covered above |

### `IProductionPresetBuilder`

| Call | Effect |
| --- | --- |
| `AllowSelection()` | `SelectProduct`, `DeselectProduct`, `SwitchProduct` and `CurrentProducts` (*SelectProduct*) |
| `AllowManagement()` | `AddProduct` and `RemoveProduct` (*ManageProduct*, *DynamicProductAddressSpace*) |
| `WithLocking()` | A DI `Lock` on every product. A client changes a product only while holding the lock (§7.8.3) |
| `AddProduct(id, name, configure)` | A product of the kind's product type. `configure` gets the typed node, for example to set a checkweigher's `NominalWeight` |
| `Select(id)` | Puts the product into processing |

A preset without `AllowManagement` is a *StaticProductAddressSpace*. It needs
at least one product, because `<Product>` is a mandatory placeholder.

### `IScaleSystemBuilder` and `IScaleModuleBuilder`

A system takes `WithIdentification`, `WithProcessState`,
`WithProductionPreset`, `WithProductionOutput`, `WithPackMLState` (its
`SystemState`) and `WithMachineryBuildingBlocks`. `AddScale(name, kind,
configure)` adds each scale. §7.3.3 says the scales of one system must be of
different types. The builder does not enforce this.

A module takes `WithIdentification`, whose identification node is a
`MachineryComponentIdentificationType` because the declared
`MachineryItemIdentificationType` is abstract, and
`WithMachineryBuildingBlocks`. A feeder also takes `WithFeederSpeed(min, max,
unit, initial)`. A printer takes `WithLabel(labelTypeId, length, width,
unit)`.

## Runtime reference

### `ScaleHandle`

The application publishes the raw load. The handle does the rest.

| Member | Purpose |
| --- | --- |
| `PublishLoad(rawLoad, stable)` | Evaluates the load and publishes `CurrentWeight` with every property |
| `SetZero()`, `SetTare()`, `ClearTare()`, `SetPresetTare(value, unit)`, `RegisterWeight()` | The same operations the methods run, for local operator panels |
| `CurrentReading`, `RegisteredReading` | The last published values as `ScaleReading` |
| `WeightRegistered` | Raised on every registration, whether by method or locally |
| `CommandInterceptor` | A `Func<ScaleCommand, ServiceResult>` that can veto any method before it runs, for example while the machine is interlocked |
| `Notifications` | Events and alarms, see below |
| `PackML`, `ProductionPreset`, `ProductionOutput` | The PackML controller, the product runtime and the statistics |
| `Continuous`, `LossInWeight`, `PieceCounting`, `Laboratory`, `Hopper`, `Vehicle`, `Recipes`, `AutomaticFilling` | The kind-specific controller, or null |
| `Modules`, `WeighingModules` | Feeder/printer handles and weighing-module scale handles |
| `SetItemState`, `SetOperationMode`, `SetProcessState` | The Machinery states and the process state |
| `Update(action)` | Runs application code that writes several values of the scale (for example typed nodes reached through `With<TState>`) as one step |

**Threading.** The runtime members of a scale, of its type controller and of
its feeder and printer modules take the scale's lock, so they are safe to call
from any thread. A client method call and an equipment-side report such as
`SetActivity` or `CompleteCalibration` therefore never interleave: a method
that checks a flag sees either the whole report or none of it. The lock is
re-entrant, so code inside `Update` may call these members.

The recipe controller, the production preset, the statistics, the
notifications, the PackML controller and a continuous scale's totalizers
synchronise on locks of their own. They are thread-safe as well, but `Update`
does not keep their calls out. The `Update` delegate runs under the scale's
lock and holds up every method call on the scale meanwhile, so keep it short
and synchronous: no I/O, no waiting and no calls into other locks.

- **Operator commands** (`SetZero`, `SetReferencePieceWeight`,
  `InboundWeighing`, …) pass the `CommandInterceptor`, like the methods.
- **Equipment reports** (`PublishLoad`, `PublishHopper`, `SetActivity`,
  `CompleteCalibration`, `PublishFeeder`, …) take the lock but skip the
  interceptor: the equipment reports what happened, it does not ask.
- **Node writes** need no node-manager lock. A `NodeState` guards its own
  attributes, and the scale's lock is what keeps related values consistent.

**Weighing arithmetic (§9.3):**

- **Gross and net.** Gross is the load minus the zero offset `SetZero`
  established. Net is gross minus the tare.
- **Weighing range.** The range is the first one whose upper limit is not
  exceeded.
- **Rounding.** Values round to the range's `d`, or to `e` when the scale is
  legal for trade, and snap to that interval's decimal places. The
  high-resolution value keeps full precision.
- **Overload** is gross above the largest capacity.
- **Underload** is gross below zero by more than the zero band. A scale
  resting slightly below zero needs a zero, not an underload alarm.
- **Unstable weights.** `SetZero`, `SetTare` and `RegisterWeight` refuse an
  unstable weight. `SetZero` also refuses a weight outside the zero band.
- **Registration** refuses an overload or underload.

### `ScaleNotifications`

- `RaiseEvent(id, message, severity, auxParameters)` raises a
  `ScaleEventType` with the Annex C `NotificationId` and its category.
- `RaiseVendorEvent` does the same for vendor ids, which must be above 5000.
- `SetAlarm(id, active, message)` drives one `ScaleAlarmType` condition per
  notification id. The condition is created on first use and carries a
  `HasCondition` reference from its source.
- The runtime raises some notifications by itself:
  - `OVERLOAD_FAULT` and `UNDERLOAD_FAULT` alarms follow the weight.
  - A failed zero or tare raises `ZERO_SETTING_FAULT` or
    `TARE_SETTING_FAULT`.

### `ScaleProductionPreset`

The methods and the local API share the same rules.

- **Products.** `AddProduct`, `RemoveAsync`, `Select`, `Deselect` and
  `Switch`. `Switch` fails with `BadInvalidState` when more than one product
  is in processing (§7.7.8).
- **Locking.** On a preset built `WithLocking`, a client changes a product
  only while holding its DI lock. Otherwise the change fails with
  `BadUserAccessDenied`.
- **Catchweigher zones.** Zones come from `AddZone`.
- **Vehicle information.** `VehicleInformationProvider` answers
  `GetVehicleInformation` from the application's vehicle database.

### `ScaleStatistics`

- `Record`, `RecordAccepted` and `RecordRejected(weight, reason)` maintain
  counts, sum, min, max, mean and standard deviation, and `LastItem`.
- `Reset` clears them.
- On a checkweigher, each `CheckweigherRejectReason` counts into its own
  `PackagesRejectedBy…` counter.

### Type controllers

| Controller | Behaviour |
| --- | --- |
| `ContinuousController` | `PublishFlow`, `Totalize`, `ResetTotalizer` (method and local) |
| `LossInWeightController` | `DischargeStart/Stop`, `RefillStart/Stop` reflected in `Discharging`/`Refilling`; `PublishHopper` |
| `PieceCountingController` | `SetReferencePieceWeight`, `SetNumberOfReferencePieces`, `StartReference` (method and local; the local `SetReferencePieceWeight(weight, unit)` takes the real weight, the method a UInt32); derives `CurrentPieceCount` from the net weight; the product's target counts |
| `LaboratoryController` | Draft shields, leveling, calibration and ionisator with their `*Running` flags. `CompleteImmediately` or `CompleteLeveling` / `CompleteCalibration` end the running operations |
| `HopperController` | `PublishLevels` for the limit switches and levels |
| `AutomaticFillingController` | `EvaluateFilling` sets `Deviation` and `ToleranceState` against the product target |
| `VehicleController` | `InboundWeighing`, `OutboundWeighing`, `OnePassWeighing` (method and local); computes `DeltaWeight` per Table 123 |
| `RecipeController` | Recipe management and processing, see below |

### Recipes

A recipe is a directed acyclic graph of elements linked by
`NextRecipeElement`. The graph can fork and join (Annex B). The start
element's predecessor is the recipe node itself.

**Building and running recipes.**
- `AddRecipe` and `AddRecipeElement` build the graph. An element can only
  be linked from existing elements, so the graph cannot become cyclic.
- `StartRecipe`, `StopRecipe` (pause), `ContinueRecipe`,
  `SkipCurrentRecipeElement` and `AbortRecipe` walk the graph.
- The application executes the elements:
  1. It listens to `ElementStarted`.
  2. It reports each finished element with `CompleteElement`.
- An element whose predecessors are all complete starts next.
- When the graph finishes, `RecipeCompleted` is raised. The report goes to the
  product's `Report` and, if present, its `ReportFile`.
- `ElementStarted` and `RecipeCompleted` are raised after the controller's
  lock is released, and for a method call after the call's processing. A
  handler may therefore call back into the controller, for example complete
  an element that takes no time straight from its `ElementStarted` handler.

**Recipe files.** `WithRecipeFiles()` gives every recipe a `RecipeFile`. The
file format is vendor-specific; the specification does not define one.

- An upload is handed to `RecipeController.RecipeFileHandler`, which parses
  the file and returns a `ServiceResult`. A bad result rejects the upload.
- `ReadRecipeFile` returns the last accepted content.
- Once a recipe came from a file, `AddRecipeElement` and
  `RemoveRecipeElement` on it return `BadInvalidState`. This keeps the file
  and the address space from diverging (§7.31).

### PackML

`PackMLStateMachineController` from `Opc.Ua.PackML` drives the three nested
machines: base (Aborted … Cleared), machine (Stopped … Running) and execute
(Idle … Complete).

- **Commands.** Methods on an inactive sub-machine return
  `BadStateNotActive`. A command that is not valid in the current state
  returns `BadNotExecutable`.
- **Events.** Every change raises the standard transition event, as §5.3
  requires.
- **Acting states** (Starting, Stopping, …) complete automatically unless
  `AutoCompleteActingStates` is false. The equipment then calls
  `CompleteActingState()`.
- **Guards.** `CommandGuard` can veto a command.

## Client reference

### Creating the client

`session.Scales(telemetry)` or `ScalesClientFactory`. `AddScalesClient()`
registers the factory with a hosted client. `IsSupported` tells whether the
server publishes the Scales namespace.

### Discovery

| Method | Returns |
| --- | --- |
| `DiscoverScalesAsync` | Every top-level scale and system from `DeviceSet` and `Machines`, de-duplicated |
| `EnumerateScalesAsync`, `EnumerateScalesUnderAsync(root)` | The same as a stream, or below any node |
| `EnumerateSystemScalesAsync(system)` | The scales of a scale system |
| `GetKindAsync(typeDefinition)` | The `ScaleKind` of a type, vendor subtypes included |

Each `ScaleEntry` carries the `NodeId`, names, type definition, `Kind` and
`IsScaleSystem`.

### Weighing

`ReadCurrentWeightAsync`, `ReadRegisteredWeightAsync`,
`ReadIdentificationAsync`, `ReadWeighingRangesAsync`,
`ReadAllowedEngineeringUnitsAsync`, `SetZeroAsync`, `SetTareAsync`,
`ClearTareAsync`, `SetPresetTareAsync`, `RegisterWeightAsync` (returns the
registered weight) and `ObserveWeightAsync`.

A streamed reading carries gross, net and tare only. Read the units and
properties once with `ReadCurrentWeightAsync`.

`ReadCurrentWeightAsync` throws `Bad_NotFound` for a node without
`CurrentWeight`, such as a scale system or a module.
`TryReadCurrentWeightAsync` returns null instead, the contract
`ReadRegisteredWeightAsync` has for a node without `RegisteredWeight`. A
published but never-written value comes back as NaN gross, net and tare.

### Products, PackML, type methods and recipes

| Area | Methods |
| --- | --- |
| Products | `ReadProductsAsync`, `ReadCurrentProductsAsync`, `AddProductAsync`, `RemoveProductAsync`, `SelectProductAsync`, `DeselectProductAsync`, `SwitchProductAsync` |
| PackML | `ReadPackMLStateAsync` (the innermost active state number), `ExecutePackMLCommandAsync(owner, PackMLCommand)` |
| Type methods | `SetReferencePieceWeightAsync`, `SetNumberOfReferencePiecesAsync`, `StartReferenceAsync`, `SetDraftShieldsAsync`, `WeighVehicleAsync`, `SetFeederSpeedAsync`, and `InvokeAsync(target, method)` for any method without arguments |
| Recipes | `AddRecipeAsync`, `AddRecipeElementAsync`, `ProcessRecipeAsync(scale, method, recipe)` |

### Notifications and snapshots

- **Notifications.**
  - `ObserveNotificationsAsync(source)` streams the scale events and alarms
    of a scale, module or system as `ScaleNotificationInfo`.
  - `CreateNotificationFilter()` and `Decode(fields)` expose the same
    filter for your own subscriptions.
  - `DefinedId` maps the numeric id back to `ScaleNotificationId`.
- **Snapshots.** `ReadSnapshotAsync(entry)` reads a whole scale in one pass:
  identification, both weights, ranges, units, products and PackML state.

## Contracts reference

| Contract | Holds |
| --- | --- |
| `ScaleIdentification` | Manufacturer, SerialNumber, ProductInstanceUri and the optional Machinery identification fields |
| `WeighingRangeDefinition(Low, High, d, e)` | One weighing range; `Validate()` checks the limits and intervals |
| `ScaleReading` | Gross, net, tare, tare mode, overload, underload, stability, zero, range, weight id, unit, timestamp, status |
| `ScaleEntry`, `ScaleProductInfo`, `ScaleNotificationInfo`, `ScaleSnapshot` | The client's discovery, product, notification and snapshot results |
| `ScaleKind`, `ScaleNotificationId`, `ScaleNotificationCategory` | The kinds and the Annex C tables |

## Conformance matrix

Status key: ✅ implemented and tested over a real session ·
🧪 implemented, tested in-process only · 🔧 implemented, not covered by a
test · ❌ not shipped.

OPC 40200 defines server facets only; there are no client facets or client
conformance units (§12). The server advertises the facets and CUs of what
it actually built through `ServerProfiles` and `ConformanceUnits`
(`ServerAdvertisesTheFacetsOfWhatItBuiltAsync`). A facet is advertised only
with the facets it includes: the *Machinery Machine Identification* facet
and its units come with Base Scale, and the kind facets without a controller
are left out (`ScaleFacetsBringTheMachineIdentificationTheyIncludeAsync`,
`ScaleFacetsAreNotClaimedOutsideTheMachinesFolderAsync`,
`KindFacetsWithoutAControllerAreNotClaimedAsync`).

### Facets

| Facet (§12.2.2) | Status | Tests |
| --- | --- | --- |
| Base Scale | ✅ | `EveryKindIsPublishedWithItsMandatoryMembersAsync`; `ClientReadsIdentificationRangesAndUnitsAsync` |
| Scale System | ✅ | `ScaleSystemHostsScalesAndResetsGlobalStatisticsAsync`; `ClientDiscoversEveryScaleOnceWithItsKindAsync` |
| Feeder Module | ✅ | `FeederAndPrinterModulesAsync`; `ClientCallsTheTypeSpecificMethodsAsync` |
| Printer Module | 🧪 | `FeederAndPrinterModulesAsync` |
| Minimal Production Preset | 🧪 | `StaticPresetWithoutProductsIsRejectedAsync`; `ProductionPresetSelectsDeselectsAndSwitchesProductsAsync` |
| Full Production Preset | ✅ | `ProductionPresetAddsAndRemovesProductsAsync`; `LockedProductsRejectOtherClientsAsync`; `ClientManagesAndSelectsProductsAsync` |
| International System of Units | 🧪 | `PresetTareChecksAndConvertsTheUnitAsync`; `SiMassUnitsAreRecognised` |
| Simple, Laboratory, Hopper, Weighing Bridge | ✅ / 🧪 | `LaboratoryShieldsLevelingCalibrationAndIonisatorAsync`; `HopperPublishesLevelsAsync`; `WeighingModulesAreScalesOfTheirOwnAsync`; E2E for Simple, Laboratory and Hopper |
| AutomaticFillingScale | 🧪 | `AutomaticFillingEvaluatesAgainstTheTargetAsync` |
| Catchweigher, AutomaticWeightPriceLabeler | 🧪 | `CatchweigherProductZonesCanBeAddedAndRemovedAsync` (not advertised, see [limitations](#known-limitations)) |
| Checkweigher | ✅ | `CheckweigherStatisticsCountAcceptedAndRejectedAsync`; `ClientSubscribesToANestedScaleThroughTheNotifierHierarchyAsync` |
| Continuous Scale, LossInWeight Scale | ✅ | `ContinuousScaleTotalizesAndResetsAsync`; `LossInWeightDischargesAndRefillsAsync`; `ClientStreamsWeightsEventsAndAlarmsAsync` |
| PieceCountingScale | ✅ | `PieceCountingReferencesAndCountsAsync`; `PieceCountingProductTargetsCanBeSetAsync`; `ClientCallsTheTypeSpecificMethodsAsync` |
| RecipeScale | ✅ | `RecipesAreManagedAndProcessedAlongTheirGraphAsync`; `RecipeProcessingGuardsAsync`; `RecipeFilesAreUploadedParsedAndReportedAsync`; `ClientBuildsAndProcessesARecipeAsync` |
| Totalizing Hopper Scale | 🧪 | `EveryKindIsPublishedWithItsMandatoryMembersAsync` (structure only and not advertised, see [limitations](#known-limitations)) |
| Vehicle Scale | ✅ | `VehicleWeighingComputesTheDeltaAsync`; `ClientCallsTheTypeSpecificMethodsAsync` |

### Cross-cutting conformance units and behaviour

| Area | Status | Tests |
| --- | --- | --- |
| *Scales ScaleDeviceType*: zero, tare, preset tare, register | ✅ | `ZeroTareAndRegisterMethodsDriveTheWeightAsync`; `ClientZeroesTaresAndRegistersWeightsAsync` |
| `WeightItemType` semantics: rounding, ranges, overload, underload, zero band | 🧪 | `WeightIsRoundedToTheIntervalOfItsRange`; `LegalForTradeRoundsToTheVerificationInterval`; `OverloadAndUnderloadFollowCapacityAndZeroBand`; `PublishedLoadReachesEveryWeightPropertyAsync` |
| *Scales DataChange* | ✅ | `ClientStreamsWeightsEventsAndAlarmsAsync` |
| `ScaleEventType` / `ScaleAlarmType` with Annex C ids, vendor ids > 5000 | ✅ | `EventsAndVendorEventsCanBeRaisedAsync`; `FailedZeroOrTareRaisesAFaultAndOverloadAnAlarmAsync`; `ClientStreamsWeightsEventsAndAlarmsAsync` |
| Notifier hierarchy with `HasNotifier`, nested sources subscribable | ✅ | `ClientSubscribesToANestedScaleThroughTheNotifierHierarchyAsync` |
| PackML state information with transition events | ✅ | `PackMLMethodsWalkTheStateMachineAsync`; `PackMLActingStatesCanBeCompletedByTheEquipmentAsync`; `ClientDrivesThePackMLStateMachineAsync`; advertised as *PackML State Information*: `PackMLStateInformationIsClaimedWithAPackMLStateMachineAsync` |
| Machinery Machine Identification, Find Machines | ✅ | `IdentificationWithoutMandatoryFieldsIsRejectedAsync`; `ClientDiscoversEveryScaleOnceWithItsKindAsync` |
| Machinery building blocks, MachineryItemState, OperationMode | 🧪 | `StateSettersPublishTheMachineryStatesAndProcessStateAsync` |
| *RecipeManagment*, *DynamicRecipeManagement*, *FileRecipeManagement* | ✅ | as RecipeScale above |
| Method veto through `CommandInterceptor` | 🧪 | `CommandInterceptorCanVetoACommandAsync`; `PieceCountingLocalCallsMatchTheMethodsAsync` |
| Runtime members thread-safe against method calls | 🧪 | `EquipmentReportsAreAtomicAgainstTheScaleLockAsync`; `CalibrationReportsAreAtomicAgainstTheScaleLockAsync` |
| Local operator calls for piece counting and vehicle weighing | 🧪 | `PieceCountingLocalCallsMatchTheMethodsAsync`; `VehicleLocalCallsMatchTheMethodsAsync` |
| Recipe events raised outside the lock | 🧪 | `HandlersMayCompleteElementsSynchronouslyAsync` |
| Rejecting a second DI owner | 🧪 | `ServerHostingRefusesASecondDiOwner` |
| Client hosting `AddScalesClient` | 🧪 | `ClientHostingRegistersTheFactory` |

## Samples

| Sample | Shows |
| --- | --- |
| [ScalesServer](../samples/Scales/ScalesServer) | A packing line (scale system with a checkweigher, infeed feeder and labeler) plus a laboratory balance, a counting scale, a weighbridge and a recipe batching scale, all with a simulation that rejects packages and runs the labeler out of labels |
| [ScalesClient](../samples/Scales/ScalesClient) | Discovery, snapshots, tare/register/switch product on the nested checkweigher, and streaming of its weights, events and alarms |

See [the samples README](../samples/Scales/README.md) for how to run them.

## Testing

```bash
dotnet test tests/Opc.Ua.Scales.Tests
```

| Fixture | Covers |
| --- | --- |
| `ScalesUnitTests` | The weighing engine, units and the model tables, with no server |
| `ScalesNodeManagerTests` | Every kind, the methods, PackML, presets, zones, facets, in-process |
| `ScaleTypeControllerTests` | The type controllers, modules, recipes, recipe files and systems |
| `ScalesClientServerE2eTests` | A hosted server through `AddScales` / `ConfigureScales`, driven by `ScalesClient` over a real session |

The end-to-end fixture is the one that matters for anything a client sees.
Two defects this work found passed every in-process assertion:

- Alarm conditions without an event type never reached a subscribed client.
- A nested scale was not a notifier, so a subscription on it failed.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `CS0104: 'BrowseNames' is ambiguous` | `Opc.Ua` and `Opc.Ua.Scales` both define `BrowseNames` | Qualify as `Opc.Ua.BrowseNames` or alias it |
| `ArgumentException` from `CreateScaleAsync` | `WithIdentification` or `WithWeighingRange` missing, or an identification without Manufacturer / SerialNumber / ProductInstanceUri | Add them; both are mandatory |
| `InvalidOperationException`: DI address space already owned | `AddScales` combined with `AddOpcUaDi` or another DI owner | Use only `AddScales`; it loads DI |
| `SetZero` returns `BadOutOfRange` | The gross weight is outside the zero band | Unload the scale, or widen `ZeroSettingRange` |
| `SetZero`/`SetTare`/`RegisterWeight` return `BadInvalidState` | The weight is unstable, overloaded or underloaded | Publish a stable load inside the range |
| `SetPresetTare` returns `BadInvalidArgument` | The unit is not in `AllowedEngineeringUnits` (or not SI with `RequireSiUnits`) | Pass an allowed unit |
| A product change returns `BadUserAccessDenied` | The preset is built `WithLocking` and the client holds no lock | Call the product's `Lock/InitLock` first |
| `SwitchProduct` returns `BadInvalidState` | More than one product is in processing | Deselect down to one product |
| PackML method returns `BadNotExecutable` / `BadStateNotActive` | Command not valid in the current state / sub-machine not active | Read `ReadPackMLStateAsync` first |
| Weights arrive as `ExtensionObject` | Client not created through `session.Scales(...)` | Use the extension |
| A streamed reading has no unit | Streams carry the value only | Read the unit once with `ReadCurrentWeightAsync` |

## Known limitations

- **Totalizing hopper.** Published with its full structure, but no
  controller drives `TipCounter`. The application sets it through
  `With<TotalizingHopperScaleState>` or the product node. The Totalizing
  Hopper Scale facet and its CU are therefore not advertised; the scale
  still counts towards Base Scale.
- **Catchweigher and price labeler.** These have no dedicated controller.
  - Zones and `LastItem` statistics are implemented.
  - The application writes item prices and measured dimensions itself.
  - Their kind facets and CUs are therefore not advertised; the scales
    still count towards Base Scale.
- **Recipe elements.** Elements are executed by the application through
  `ElementStarted` / `CompleteElement`. `ConditionSleep` thresholds are not
  evaluated by the server.
- **Recipe file format.** It is vendor-specific. Parsing is up to
  `RecipeFileHandler`.
- **Machinery building block.** The Machinery `Notifications` building block
  is not used. Events are raised on the scale nodes directly.
- **Method status codes.** OPC 40200 defines no method-specific status codes.
  The runtime uses the generic Part 4 codes listed in
  [Troubleshooting](#troubleshooting).

## Model provenance

- **Scales NodeSet.** `src/Opc.Ua.Scales/Model/Opc.Ua.Scales.NodeSet2.xml` is
  the unmodified OPC Foundation publication from
  [`UA-Nodeset/latest/Scales`](https://github.com/OPCFoundation/UA-Nodeset/tree/latest/Scales):
  - model version 2.00, published 2025-03-01;
  - requiring UA 1.05.03, DI 1.04.0, IA 1.01.2, Machinery 1.03.0 and
    PackML 1.01.
- **PackML NodeSet.** `src/Opc.Ua.PackML/Model/Opc.Ua.PackML.NodeSet2.xml` is
  version 1.01 from `UA-Nodeset/latest/PackML`.
- **Identifier tables.** The sibling `*.NodeIds.csv` identifier tables pin the
  symbol-to-NodeId mapping. The generator fails the build (MODELGEN025) when
  they disagree.
- **`RequiredModel` versions** are a lower bound. The repository's newer DI,
  IA and Machinery models satisfy them.
- **Generator fix.** PackML exposed a source-generator bug:
  - It declares seven method `InputArguments` ahead of their methods.
  - The generator derived symbolic ids in the same pass that normalised a
    missing `ParentNodeId`, so those children were named from the bare parent
    name.
  - `NodeSetToModelDesign` now normalises every parent first.
  - No existing generated symbol changed.

## Where the specification and the NodeSet disagree

The implementation follows the NodeSet wherever the text and the NodeSet
differ:

- **CU spellings.** The CU is spelled *Scales RecipeManagment* in Table 162
  and on `RecipeManagementType`, but *RecipeManagement* elsewhere. The server
  advertises the Table 162 spelling.
- **Facet URIs.** Some facet URIs contain spaces (`…/Scales_LossInWeight
  Scale`) or lack `/Server/`. `ScalesProfiles` reproduces them verbatim.
- **State machine browse names.** §7.4.3 calls the state machine
  "ScaleStateMachine", but its browse name is `State`, and `SystemState` on a
  scale system.
- **Method argument types.** `SetReferencePieceWeight` takes a `UInt32`
  weight, and `SetFeederSpeed` takes a `Float`.
- **Browse names that differ from the text.**
  - The totalizing hopper target is `VolumeTargetValue`.
  - The checkweigher percentage is `PercentageLowerToleranceLimit`.
- **Abstract identification type.** Feeder and printer `Identification` are
  declared with the abstract `MachineryItemIdentificationType`. Instances use
  `MachineryComponentIdentificationType`.
- **No `GeneratesEvent` or `HasNotifier`.** The NodeSet has neither, so the
  notifier wiring described in
  [Where a scale lives](#where-a-scale-lives) is the server's own.

## MCP tools

`opcua-mcp --profile scales` exposes bounded discovery (`scales_list`), finite
facet reads (`scales_read`), observations (`scales_observe`) and explicit
weighing, product, recipe, PackML and scale-kind commands.

Use the NodeId returned by discovery. `scales_read` with `CurrentWeight`
returns `available=false` when the object has no weight item; an uninitialized
published measurement remains available with `"NaN"` rather than being
misrepresented as zero. Engineering units and quality accompany measurements.

```json
{
  "nodeId": "ns=2;s=Scale1",
  "facet": "CurrentWeight",
  "sessionName": "plant"
}
```

For products protected by DI locking, run `--profile scales,di`, read the
`Products` facet, and use its actual `lockNodeId` with the DI lock tools.
Commands never take or break a lock automatically. Standard no-argument
scale commands, vehicle operations and recipe actions use finite enums;
arbitrary vendor method names are not accepted.

`scales_add_recipe_element` accepts `input.previousElements` as a standard
JSON array of NodeId strings (1..500), not the CLR `ArrayOf` representation.
Creating a recipe or an element does not start it.

Embed `Opc.Ua.Mcp.Scales` with `AddOpcUaMcpScales()` and
`WithOpcUaScalesTools(...)`. See [industrial companion MCP tools](McpServer.md#industrial-companion-tools)
for shared profiles and error/observation contracts.

## See also

- [Machinery developer guide](Machinery.md): the `Machines` folder and the
  OPC 40001-1 building blocks
- [State machines](StateMachines.md): the finite-state-machine support
  PackML builds on
- [OPC 40200 on the OPC Foundation reference site](https://reference.opcfoundation.org/specs/OPC-40200/full)
