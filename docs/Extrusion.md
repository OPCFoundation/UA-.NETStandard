# Extrusion servers and clients

The `Opc.Ua.Extrusion.Server` and `Opc.Ua.Extrusion.Client` packages implement
the reusable runtime for **OPC 40084 parts 1–12, version 2.00**. Each device
keeps its real generated companion type. They are not twelve names for an
extruder, and the abstract `ExtrusionDeviceType` is never instantiated.

The runtime is a view of an actual device/controller. It has no production
timer, random values, automatic counter increments, or material-flow simulator.
Applications supply asynchronous authorities for commands and publish absolute
committed observations.

## Contents

- [Packages and ownership](#packages-and-ownership)
- [Standalone and composed hosting](#standalone-and-composed-hosting)
- [Concrete devices and feature selection](#concrete-devices-and-feature-selection)
- [Providers and committed state](#providers-and-committed-state)
  - [Units and monitoring](#units-and-monitoring)
- [Extrusion line jobs and production](#extrusion-line-jobs-and-production)
- [Typed clients](#typed-clients)
  - [Dataset/file workflow](#datasetfile-workflow)
- [Supported parts](#supported-parts)
  - [Filter profile discrepancy](#filter-profile-discrepancy)

## Packages and ownership

One `Opc.Ua.Extrusion` model assembly owns all twelve parts' standard
namespaces, identifiers, generated states, structures, method proxies and event
records. The family consists of exactly three extrusion packages:

| Package | Responsibility |
| --- | --- |
| `Opc.Ua.Extrusion` | All twelve unmodified MIT-licensed OPC 40084 v2 models and generated interfaces. |
| `Opc.Ua.PlasticsRubber.Server` / `.Client` | Shared OPC 40083 start/stop, monitoring/control, maintenance, configuration, MES, production-dataset and file behavior. |
| `Opc.Ua.Extrusion.Server` | Concrete instance construction, exact optional-feature selection, instance identity, extrusion state projection, line jobs, cutter completion and events. |
| `Opc.Ua.Extrusion.Client` | Discovery, generated device proxies, model-derived typed variable accessors, placeholder enumeration, common workflows and generated event decoding. |

Source is organized by specification part: `GeneralTypes`, `Line`, `Extruder`,
`HaulOff`, `MeltPump`, `Filter`, `Die`, `Pelletizer`, `Cutter`, `Calibrator`,
`Corrugator` and `Calender`. Model inputs, generated client accessors and
part-specific runtime entrypoints use those folders. CLR namespaces and
per-part compiled loaders remain distinct; consumers reference the three
family packages, not per-device packages.

The common model's provenance is distinct: it is an independently authored
1.03-compatible interface on the licensed 1.02 basis, **not** a redistributed
official 1.03 XML. See the packaged `licenses/CompanionModels.NOTICE.txt` and
the [model manifest](../src/Opc.Ua.PlasticsRubber/Model/companion-models.json).

One DI/Machinery node manager owns both models and instances. Standalone
hosting uses that same owner; composition does not register another DI manager.
AMB remains a sidecar, with the real extrusion object registered as an asset.

## Standalone and composed hosting

```csharp
services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "Extrusion server";
        options.ApplicationUri = "urn:example:extrusion:server";
        options.EndpointUrls.Add("opc.tcp://localhost:4840/Extrusion");
    })
    .AddExtrusion(options =>
        options.InstanceNamespaceUri = "urn:example:extrusion:instances")
    .ConfigureExtrusion(async (context, ct) =>
    {
        var options = new ExtrusionDeviceOptions(new ExtrusionIdentity(
            "Example", "E-1", new LocalizedText("Extruder"),
            "urn:example:extruder:E-1", "Controller"))
        {
            LineId = "Line-1",
            Commands = context.GetRequiredService<IExtrusionCommandProvider>()
        };
        options.Features
            .WithInstances("TemperatureZones/TemperatureZone_<Nr>", 3)
            .Include("BarrelId");

        ExtrusionDevice<Extruder_InterfaceState> extruder =
            await context.Runtime.CreateExtruderAsync(
                options, cancellationToken: ct);
        // Retain the typed handle in the application's device registry.
    });
```

For an existing owner, replace `AddExtrusion(...)` with:

```csharp
builder
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddExtrusionToMachinery()
    .AddAssetManagement()
    .ConfigureExtrusion(async (context, ct) =>
    {
        ExtrusionDevice<Filter_InterfaceState> filter =
            await context.Runtime.CreateFilterAsync(filterOptions, cancellationToken: ct);
        await context.GetRequiredService<IAssetManagement>()
            .RegisterAssetAsync(filter.AsNode(), cancellationToken: ct);
    });
```

Do not also call `AddOpcUaDi` or register a second Machinery owner. Custom
owners load `MachineryModelProvider(MachineryParts.BuildingBlocks)` and
`ExtrusionModelProvider`, then use `ConfigureExtrusionFor<TManager>`.
Outside dependency injection, use `ExtrusionRuntime.Create(existingManager)`.
The same runtime is returned on subsequent calls. A custom owner releases its
Machinery resources through `DisposeMachineryResourcesAsync`.

Runtime disposal stops its commands, subscriptions' data sources, temporary
file bindings and other owned resources. Device nodes themselves belong to the
node manager. Injected hardware/persistence providers remain application-owned.

## Concrete devices and feature selection

Typed constructors exist for Line, Extruder, HaulOff, MeltPump, Filter, Die,
Pelletizer, Cutter, Calibrator, Corrugator and Calender. Constructor identities
generate the standard device BrowseName and stable application-namespace
NodeIds. Child identity uses the **BrowseName namespace URI**, not a numeric
namespace-table position.

`ExtrusionFeatures` uses the actual compiled declarations:

- Mandatory members always exist.
- `Include("path")` selects an optional subtree.
- `IncludeAll()` selects applicable optional members, not imaginary equipment.
- `WithInstances(".../<Nr>", count)` supplies the actual number of repeated
  instances. It creates required children, but does not implicitly turn on
  every optional control on those instances.
- `Exclude("path")` removes a selected optional subtree; excluding mandatory
  members is an error.
- Misspelled paths, impossible counts, duplicate identities and absent required
  providers are errors, not silently ignored options.

Examples:

```csharp
filterOptions.Features
    .WithInstances("FilterPackage_<Nr>", 2)
    .WithInstances("FilterPackage_<Nr>/ScreenPackage_<Nr>", 3)
    .Include("FilterPackage_<Nr>/BackflushCounter")
    .Include("FilterPackage_<Nr>/BackflushTime")
    .Include("BackflushPressure");

calenderOptions.Features
    .IncludeAll()
    .WithInstances("Rolls/Roll_<Nr>", 3)
    .WithInstances("Gaps/Gap_<Nr>", 2)
    .WithInstances("Rolls/Roll_<Nr>/PeripheralDevices/CleaningSystem_<Nr>", 1);
```

Repeated names follow the relevant part, starting at `001`; generic measuring
containers are refined to names such as `MeltPressure_001`,
`MeltTemperatureZone_001` and `ScrewTemperature_001`. Cutter always contains real
mandatory `CuttingProduct` instances, five by default; a different positive
count is selectable. No literal placeholder names are published.

Line deliberately omits inherited `Target` and `Strand` as required by
[OPC 40084-2 §6.2](https://reference.opcfoundation.org/specs/OPC-40084-2/v2.00/6.2).
Job groups/jobs are created through their typed protocol with complete metadata,
not by configuring anonymous placeholder counts.

## Providers and committed state

`IExtrusionCommandProvider` receives `ExtrusionCommand`, including the **original
request `ISystemContext`**, the actual device and member, and the exact command
kind. Authorization runs before execution. Forwarding adapters must preserve
that user's authorization; the runtime does not substitute its startup/service
identity.

Writes and output resets require committed values from the authority.
Opening/closing a clamp and manual/sample-cut requests can acknowledge an
accepted request before physical completion, but do not fabricate clamp
feedback, a completed cut, or counter changes.

Common controllers are supplied through `ExtrusionDeviceOptions.Common`:
`IStartDeviceProvider`, `IMonitoredParameterProvider`,
`IClosedLoopControlProvider`, `IMaintenanceProvider`,
`IMachineConfigurationProvider`, `IMesMessageProvider`,
`IProductionDatasetProvider`, and an explicit `IPlasticsRubberAccessPolicy`.
The common package owns their protocols. Selecting a command-bearing capability
without its authority fails creation.

A line's MaterialList is bound to `ExtrusionDeviceOptions.Common.Materials`.
OPC 40083 declares its `DensityUnit` without a value, so a bound catalogue also
needs `Common.MaterialDensityUnit`, the unit every material density is given in;
creation fails without it. An unbound list publishes the configured unit, or a
null `DensityUnit` when none is configured.

Observation publication is independent of an awaited hardware command, allowing
an authoritative coordinator to deliver its committed state before replying to
a forwarded command.

Common parameter and controller settings are snapshot-owned too. A successful
setting command does not publish its requested value: the previous observation
remains until `ApplySnapshotAsync` supplies committed data. Later observations
cannot be overwritten by an older write completion. Reads and parameter-limit
validation use the committed image; file-transfer and other protocol-owned
state retain their existing bindings.

```csharp
await extruder.State.ApplyAsync(
    new ExtrusionOperatingSnapshot(
        committedRevision,
        MachineryItemStateValue.Executing,
        MachineryOperationModeValue.Processing,
        ExtrusionExecutingState.ControlledRun,
        sourceTimestamp),
    [
        new ExtrusionValue(extruder.Node.IsPresent!.NodeId,
            new DataValue(Variant.From(true), StatusCodes.Good, sourceTimestamp)),
        new ExtrusionValue(extruder.Node.BarrelId!.NodeId,
            new DataValue(Variant.From(barrelId), quality, sourceTimestamp))
    ],
    cancellationToken);
```

For arbitrary selected generated variables, use `ApplySnapshotAsync`. A snapshot
is validated before publication. Repeating the same revision and values is
idempotent; stale or conflicting revisions are rejected. Values retain their
quality and source time. Unobserved physical values start with
`BadWaitingForInitialData`, not a fabricated `Good` zero.

The item state, operation mode and execution substate remain distinct. When the
item is not `Executing`, the execution submachine's `CurrentState` and
`LastTransition` report `BadStateNotActive`
([OPC 40084-1 §6.3](https://reference.opcfoundation.org/specs/OPC-40084-1/v2.00/6.3)).
Repeated observations of the same state preserve the last transition and its
committed transition time.
The core Part 16 dispatcher supplies state projection; no alternative state
model is substituted.

### Units and monitoring

Known standard dimensions receive UNECE engineering units, including Celsius,
bar, millimetre, micrometre, square centimetre, kilogram/hour, metre/second,
kilowatt, ampere and percent. Configure application-defined dimensions explicitly
with `ExtrusionUnits.Configure` before publication. Do not attach a unit to a
number whose actual physical dimension is different.

Common monitoring is driven by the same committed actual value and quality.
Lower relative tolerances are **signed negative offsets**: for a nominal value
of 190, `LowerTolerance = -8` means a lower boundary of 182. A second lower
tolerance must be at or below the first. They are not positive magnitudes.
The common package validates these rules and owns automatic monitoring,
sensitivity, suppression, controlled setpoints/ramps and PID tuning.

## Extrusion line jobs and production

`ExtrusionJobController` implements
[OPC 40084-2 §8](https://reference.opcfoundation.org/specs/OPC-40084-2/v2.00/8):

- Group IDs are unique within a line; job IDs are unique **within a group**.
- Strand and sequence are one-based. Duplicate sequence positions on a strand
  are rejected, and a strand cannot have two locally active jobs.
- Standard configuration IDs are 1–7 with Double values; vendor IDs start at
  100. Unspecified settings receive the group's offered defaults.
- Group metadata retains dataset names, material/lot/hopper mappings, priorities
  and planned times. Cross-server reference validation belongs to the injected
  authority rather than guessing from an incomplete local address space.
- Start, interrupt, finish and remove operations are authorized, state-checked
  and committed through `IExtrusionJobProvider`.
- Structural changes update `NodeVersion` and emit model-change events.
- Status events are emitted from `JobGroups` while retaining the actual
  group/job as `SourceNode`.
- Absolute production snapshots carry the actual unit/quality and lot completion
  facts. Restoration baselines can have empty event lists. Replays cannot
  increment counters or repeat those events.
- A bad active strand prevents the line from claiming good product. True-to-false
  quality changes create real common `MessageCondition` alarms using the core
  condition lifecycle, without falsely stopping the machine.

Status commands update that active-job quality constraint immediately; interrupting
or finishing the last bad active job allows a later committed good-quality observation.

The wire `AddJob` argument is named `ProductName`; the corresponding job
variable is `ProductId`. The generated wire spelling is retained. These are not
Machinery/ISA-95 job orders, and the runtime does not replace their semantics.

`LineControl.RequestMaterialAsync` emits the standard material-information
request even when optional `JobGroups` is absent.

For Cutter, `Cuts.ApplyAsync(ExtrusionCutCommit)` publishes the absolute
`ActualOutput`/`TotalOutput`, actual active product and one generated `CutEvent`
per committed physical cut. The hardware authority owns cutting-product
sequencing, sample-cut timing, wall thickening and waste cutting. Calling
`ManualCut` alone does not manufacture a completed cut.

## Typed clients

```csharp
services.AddOpcUa().AddClient(clientOptions).AddExtrusionClient();
ExtrusionClient client = await services
    .GetRequiredService<Func<CancellationToken, Task<ExtrusionClient>>>()(ct);

await foreach (ExtrusionDeviceEntry device in client.DiscoverAsync(
    cancellationToken: ct))
{
    // TypeDefinition is the actual sibling or vendor subtype, not an inferred label.
}

Extruder_InterfaceTypeClient extruder = client.Extruder(extruderId);
ClientRead<bool> present = await extruder.IsPresentVariable().ReadAsync(ct);
await extruder.LineIdVariable().WriteAsync("Line-1", ct);

JobGroupsTypeClient groups = await client.Line(lineId).JobsAsync(ct);
await foreach (JobGroupTypeClient group in groups.EnumerateJobGroupAsync(ct))
{
    await foreach (JobTypeClient job in group.EnumerateJobAsync(ct))
    {
        ClientRead<JobStatusEnumeration> status = await job.StatusVariable().ReadAsync(ct);
    }
}

await foreach (ExtrusionEvent notification in client.ObserveEventsAsync(
    lineId, cancellationToken: ct))
{
    // Generated JobStatusChangedEventTypeRecord, UnitFinishedEventTypeRecord,
    // CutEventTypeRecord, common condition/logbook records, etc.
}
```

Every concrete entrypoint returns its actual generated proxy. Generated child
accessors expose drives, monitored parameters, zones, materials, users, common
maintenance and all remaining optional objects. Model-derived extensions supply
**141 typed variable accessors and 17 typed placeholder enumerators** from the
unchanged licensed declarations. Their codecs use exact native/generated types,
not reflection or boxed-value conversion. The client project opts into
`ModelSourceGeneratorClientAccessorsOnly`, so Roslyn emits these extensions during
the build; no generated C# or Python generation step is checked in.

The reusable `ClientVariable<T>`, `ClientRead<T>` and client-model access helpers
live in `Opc.Ua.Client`. See the [source generator options](../tools/Opc.Ua.SourceGeneration/readme.md#optional-client-member-accessors)
to enable the same extensions for another companion model.

`ReadAsync` returns `ClientRead<T>` with the original `DataValue`; bad quality
and source timestamps are not discarded. `ObserveAsync` uses the existing
bounded streaming subscription. Disposing an observation removes its monitored
item, not another caller's shared session.

### Dataset/file workflow

```csharp
ProductionDatasetClient datasets =
    await client.Extruder(extruderId).ProductionDatasetsAsync(ct);
await datasets.UploadAsync(
    new ProductionDatasetWriteOptionsType
    {
        Storage = StorageEnumeration.FILE_SYSTEM,
        Name = "production-settings",
        Components = selectedComponentCodes
    },
    sourceStream,
    information,
    ct);
await datasets.LoadAsync(
    StorageEnumeration.PRODUCTION, "production-settings", selectedComponentCodes, ct);
```

This delegates to the common chapter-20 implementation and the existing
temporary-file/FileType clients. Production and preparation are separate layers;
frozen checks, per-session handles, bounded files, metadata exchange, component
selection and commit semantics remain the common protocol. Binding preserves
independently selected active/preparation optional members.

## Supported parts

Support refers to model and protocol functionality with the required providers,
not a certification claim.

| Part | Name | Support |
| --- | --- | --- |
| 40084-1 | General types | Supported |
| 40084-2 | Extrusion line | Supported |
| 40084-3 | Extruder | Supported |
| 40084-4 | Haul-off | Supported |
| 40084-5 | Melt pump | Supported |
| 40084-6 | Filter | Partial |
| 40084-7 | Die | Supported |
| 40084-8 | Pelletizer | Supported |
| 40084-9 | Cutter | Supported |
| 40084-10 | Calibrator | Supported |
| 40084-11 | Corrugator | Supported |
| 40084-12 | Calender | Supported |

Hardware-specific closed-loop algorithms, physical clamp motion, filter
backflushing, feeder/dosing behavior and cutter motion are implemented by the
explicit asynchronous providers, not fabricated in the package. Selecting them
does not grant authority to operate actual equipment.

### Filter profile discrepancy

The published
[OPC 40084-6 v2.00 §9.2.2 table](https://reference.opcfoundation.org/specs/OPC-40084-6/v2.00/9.2.2)
references **OPC 40084-5 Basic**, although part 6 defines Filter and its own
Filter conformance unit. This runtime implements the real Filter model and
**does not advertise that disputed profile**. It does not modify the standard
XML, insert a fictional melt pump or interpret a loaded MeltPump *type* as a
physical MeltPump *instance*. Other copied equipment names in prose likewise do
not change wire types.

No formal OPC Foundation certification is claimed. Loading a namespace, packing
a NuGet package, static member coverage and passing tests are different evidence.
