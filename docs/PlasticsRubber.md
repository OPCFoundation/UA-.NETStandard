# Plastics and rubber common capabilities

OPC 40083 defines capabilities reused by plastics and rubber machinery. It does
not define a universal machine root. A concrete companion model, such as
[Extrusion](Extrusion.md) or [Granulate drying](#granulate-drying-devices-opc-40082-6), composes
these capabilities into its own equipment interface.

## Packages and model provenance

`Opc.Ua.PlasticsRubber` owns the standard namespace
`http://opcfoundation.org/UA/PlasticsRubber/GeneralTypes/`. It supplies generated
states, structured encodings, typed clients and compiled model loading.
`Opc.Ua.PlasticsRubber.Server` adds fluent accessors and provider bindings.
`Opc.Ua.PlasticsRubber.Client` adds managed-session integration and common workflows.

The same three packages also contain OPC 40082-6 granulate-drying models, server behavior
and typed clients. There are no separate GranulateDrying packages. Their wire and CLR
namespaces remain distinct from the common capabilities.

- [Common capability hosting](#one-owner-explicit-providers)
- [Production dataset protocol](#production-dataset-protocol)
- [Granulate drying devices](#granulate-drying-devices-opc-40082-6)

The common model is an independently authored implementation of the 1.03
interface changes on the expressly MIT-licensed 1.02 implementation. It is
**not a redistribution of the official 1.03 XML**, and the presence of this
implementation does not establish a redistribution license for that artifact.
The reproducible authoring input, script and notices are under
`src/Opc.Ua.PlasticsRubber/Model`. Each package includes the companion-model
notice. The model version is distinct from the NuGet package version.

Generated declarations, field/encoding comparisons and tests do not constitute
formal OPC Foundation certification. The device guides identify additional
feature requirements and any upstream conformance-table discrepancies.

## One owner, explicit providers

Use `AddPlasticsRubber()` to start a standalone Machinery-backed owner, or
`AddPlasticsRubberToMachinery()` after an existing `AddMachinery()` registration.
Do not register another DI-owning manager just to add common capabilities.

The direct path is:

```csharp
var runtime = new PlasticsRubberRuntime(owner, accessPolicy);
StartDeviceBinding binding =
    await runtime.BindStartDeviceAsync(startDevice, startProvider);
```

Here `owner` is the existing DI/Machinery manager, `startDevice` is the actual
generated `StartDeviceState` selected by the equipment model, and
`startProvider` is its device authority. The runtime registers the binding with
the existing Machinery resource lifecycle. Direct binding constructors also
exist; their callers must dispose them or register them with that lifecycle.

`ConfigurePlasticsRubber()` and `ConfigurePlasticsRubberFor<TNodeManager>()`
participate in the existing Machinery setup pipeline. They require an
explicitly registered `IPlasticsRubberAccessPolicy`; no allow-all policy or
default hardware authority is installed.

Every provider receives the original `ISystemContext` and target instance
through `PlasticsRubberRequestContext`. Authorization occurs before provider
invocation. A failed provider operation is returned as a failure, not a
successful simulation of the requested action.

## Authority and observed state

Bindings acknowledge a request only after the provider's commit boundary.
They do not equate request acceptance with completion of physical movement,
starting, stopping or self-optimization. The device authority publishes the
actual state and absolute counters.

For a distributed or simulated system, keep production state, watchdogs and
control leases at the authoritative device or coordinator. In particular,
`IProductionControlProvider` must enforce the single controlling MES,
connection-loss behavior and watchdog deadline. A view must not run a second
production clock or turn its process-service identity into an operator identity.
Read-only observers are not subject to an invented one-session server limit.

### Snapshot-owned settings

Snapshot-backed equipment separates command completion from observation publication.
Common parameter and closed-loop bindings can opt into this behavior through their
committed-settings constructor overloads. The monitored-parameter reader supplies
limits and automatic-monitoring mode from one committed observation; the controller
reader supplies committed automatic mode. These bindings return
`AttributeWriteResult.SuppressCacheUpdate = true`, so successful writes retain the
last published value, quality and source timestamp until the owner publishes another
observation. A snapshot arriving during the write cannot be overwritten by completion.

With a committed-settings reader, `ResetMonitoring` also leaves tolerance publication
to the owner. Call `RefreshWriteAccess` when committed monitoring mode changes, passing
`allowWrites: false` when the owning device disables remote mutation. The existing
constructors retain standalone write-through and calibration-result publication.
File-transfer state and other protocol-owned fields keep their dedicated bindings.

Common bindings cover start/stop requests and their client interlock,
maintenance reset, parameter monitoring/settings, closed-loop control,
machine configuration/pages, machine sleep requests, MES messages,
production-control requests and active-job operations. Optional members are
selected on the generated instance before binding. A missing optional member
is not fabricated as an executable success-shaped placeholder.

`JobsBinding` and `CyclicJobInformationBinding` support the common list and
configuration exchanges. Receiving a job list does not start a job. Hardware
interlocks, job completion and committed cyclic counters remain with the
explicit provider; the binding does not substitute ISA-95 job semantics.

For common production datasets, see the
[chapter 20 server/client guide](#production-dataset-protocol). It documents
storage flags, active/preparation layers, metadata, freezing and temporary-file
ownership. These are not interchangeable with similarly named protocols from
other companion specifications.

## Material catalogues and diagnostics

`MaterialListBinding` publishes real generated `MaterialType` children from a
complete `MaterialListSnapshot`. The authority supplies stable slots
`001` through `999`, unique material identifiers, names, density and the
commit's source time. Density uses the list's declared `DensityUnit`.
`NodeVersion` follows the committed catalogue revision. Identical replay is
idempotent; a stale revision or conflicting replay is rejected.

Select the optional generated add/remove methods before binding when remote
mutation is required. `IMaterialListProvider` commits the mutation and returns
the full resulting catalogue. It must reject removal of an in-use material
where appropriate. No implicit privileged read is performed at construction:
refresh uses an explicitly supplied caller, or the host can publish an already
committed snapshot.

`DiagnosticsBinding` implements the common start/stop protocol and emits
generated step/end records from concrete application-owned derivatives of the
abstract standard event types. The actual device interface is the event source.
`IDiagnosticsProvider.RunAsync` returns an active state; later completion is
published as an absolute `DiagnosticsSnapshot`, including the required end
event. Stopping returns `OFF`. Vendor-specific diagnostic algorithms do not
belong in the common package.

The client facade includes `ObserveDiagnosticsStepsAsync`,
`ObserveDiagnosticsCompletionsAsync`, `ObserveLogbookAsync` and
`ObserveAlarmsAsync`. These consume an existing bounded streaming subscription
or a managed session's default stream and use generated record decoders.
Unknown application event subtypes are checked against the declared standard
base type; malformed or unrelated event payloads fail explicitly.

## Monitoring semantics

`MonitoredParameterLimits` uses signed relative offsets: lower tolerances are
nonpositive, upper tolerances are nonnegative, and the second band contains
the first. Absolute minimum/maximum alarms take precedence over relative
warnings. Exact boundaries are inside their corresponding limit.

Missing optional limits remain absent; they are not replaced with zero.
Relative bands need a setpoint. Nonfinite settings, reversed boundaries and
incorrectly signed offsets fail validation. Bad or uncertain measurements have
an unknown monitoring status instead of a fabricated in-tolerance result.

`MonitoredParameterBinding.Publish` preserves the supplied measurement quality
and source timestamp. Alarm suppression does not erase the observed monitoring
status. Device-specific calibration and alarm side effects belong to the
injected authority. A plain `MonitoredParameterType` setpoint is informational;
only `ControlledParameterType` and its subtypes use it to command hardware.

The underlying [OPC 40083 parameter definitions](https://reference.opcfoundation.org/specs/OPC-40083/v1.03/22)
and [temperature-zone definitions](https://reference.opcfoundation.org/specs/OPC-40083/v1.03/17.2.7)
describe these distinctions. Every selected AnalogItem must carry its
engineering units.

## Typed clients

Use the existing managed session rather than creating a second connection:

```csharp
var common = new PlasticsRubberClient(session, telemetry);
StartDeviceTypeClient start = common.StartDevice(startDeviceNodeId);
await start.StartRequestAsync(cancellationToken);

MonitoredParameterTypeClient parameter =
    common.MonitoredParameter(parameterNodeId);
await parameter.ResetMonitoringAsync(cancellationToken);
```

`AddPlasticsRubberClient()` registers `PlasticsRubberClientFactory` with the
existing client builder. The factory acquires the application's managed
session and registers the generated common encodings. It does not change
credentials or own the shared session's disposal.

Resolve capabilities through the concrete companion client's generated child
accessors or namespace-URI-based browsing. OPC 40083 does not prescribe a
global folder containing every common capability. Generated clients for all
remaining common types are available directly from the model assembly.

## Production dataset protocol

`Opc.Ua.PlasticsRubber.Server` and `Opc.Ua.PlasticsRubber.Client` implement
[OPC 40083 version 1.03, chapter 20](https://reference.opcfoundation.org/specs/OPC-40083/v1.03/20).
The protocol uses the generated common-model structures, method states, object
clients and event records. It is independent of an extrusion-device family,
machine vendor, file format or sample application.

### Server composition

Bind an existing generated `ProductionDatasetManagementState` to its owning
`AsyncCustomNodeManager`. The application supplies **both** a per-instance
`IProductionDatasetProvider` and the common `IPlasticsRubberAccessPolicy`.
There is no implicit allow-all policy or fabricated hardware activation.

```csharp
services.AddProductionDatasets();

// Resolve through the application's existing service provider.
var factory = serviceProvider.GetRequiredService<ProductionDatasetBindingFactory>();
ProductionDatasetBinding binding = await factory.BindAsync(
    nodeManager,
    managementNode,
    machineDatasetProvider,
    accessPolicy,
    new ProductionDatasetOptions
    {
        MaximumFileBytes = 8 * 1024 * 1024,
        MaximumTransfers = 4,
        TransferTimeout = TimeSpan.FromMinutes(1)
    },
    cancellationToken);
```

`ProductionDatasetBinding.CreateAsync` is the direct-construction alternative.
Both paths accept the existing `IFileTypeBinder` infrastructure; the direct
path also accepts an injected `TimeProvider`. The owner must load the generated
common model and its dependencies before creating instances.

By default the binding materializes every optional chapter 20 member.
`ProductionDatasetFeatures` selects grouped optional capabilities. For an
existing family materializer that selects individual optional nodes, set:

```csharp
new ProductionDatasetOptions { PreserveExistingSelection = true }
```

This mode does not add absent optional members. Active and preparation layers
may independently include `Load`, `Save`, `Frozen` and `Modified`. The two
information-exchange methods can also be selected independently. Mandatory
members of every selected object are still required.

Retain the binding and asynchronously dispose it before disposing the owner.
The binding does not own or dispose the machine provider or management node.

#### Device authority contract

`IProductionDatasetProvider` is the application boundary:

| Operation | Required authority behavior |
| --- | --- |
| `ReadStatusAsync` | Return current information, modified state and frozen policy for one control layer. |
| `ReadAsync` | Capture matching file bytes and metadata from one source. |
| `LoadAsync` | Actually activate the requested stored components before returning the resulting status. |
| `SaveAsync` | Persist the control-layer data before reporting the resulting name and saved state. |
| `SetFrozenAsync` | Commit the device's parameter-change restriction, including local/operator changes. |
| `CommitAsync` | Atomically apply every selected destination before returning affected control-layer snapshots. |
| `GetListAsync` | Return bounded file-system candidates; the binding enforces `*`, `?` and exact mould filtering. |
| `ReceiveListAsync` | Accept the client's available dataset descriptions, including an empty list. |

The generic package does not interpret machine-specific dataset bytes.
Implementations must validate their format, enforce their own storage bounds,
honor cancellation, and enforce frozen state inside the authority transaction.
For a multi-destination upload, validate every destination before modifying
any of them. Do not return after a partial application. If a device cannot
perform the requested operation, fail explicitly with a `ServiceResultException`.

`CommitAsync` receives optional metadata: clients need not implement the
optional `SendProductionDatasetInformation` method. When absent, an authority
must obtain required information from its supported format rather than claim
an unperformed activation.

The binding checks frozen state before mutations and again immediately before
commit. The provider must protect against concurrent physical/operator changes
as well; protocol serialization is not a substitute for hardware atomicity.
`Save` is blocked while frozen because it also changes the dataset's recorded
name/save status. Reads remain available. An authorized `SetFrozenAsync(false)`
can release the restriction.

No provider is called during binding creation using a substitute server user.
Status properties initially report `BadWaitingForInitialData`; authenticated
reads fetch authoritative snapshots. `RefreshAsync(context)` can publish
external device changes when the application supplies the operation context
and authorization policy explicitly.

#### Caller identity and access

Every method and value read/write is checked through
`IPlasticsRubberAccessPolicy.AuthorizeAsync`. Temporary-file operations and
metadata reads are also checked, not just transfer creation.

The `PlasticsRubberRequestContext.SystemContext` is the original caller context.
`TargetId` is the actual method-owning or property-owning instance, which can
be a status object, list object, transfer object or temporary file. The provider
is already bound to its machine; it must not resolve authority solely by
assuming that every target is the management node.

An upload commit uses the **commit request's** user context, not the context
that originally opened the file. This matters when a session changes identity.
Files remain session-owned. A different session cannot use another session's
file handle, read its metadata, send information, or commit its bytes.

Normal server role permissions still govern browsing, event subscriptions and
other attributes. Configure those permissions on the containing address space;
the asynchronous operation policy does not replace the stack's subscription
authorization.

### Options, state and lifecycle

- Read options select exactly one of `PRODUCTION` (1), `PREPARATION` (2) or
  `FILE_SYSTEM` (4). Only file-system reads have a nonempty required name.
  Control-layer reads require an empty name.
- Write options select any nonzero combination of bits 1, 2 and 4. Unknown bits
  are rejected; destinations are never silently dropped.
- Write names are required. Component selection applies to control-layer
  destinations, not a file-system-only upload. Empty selection means all.
- Information preserves component ordering and multiplicity: `[0, 1, 1]`
  describes three component occurrences, not a set of two types.
- `Information`, `Modified` and `Frozen` are provider-backed. No proposed or
  failed change is published as committed state.
- `GenerateFileForRead` returns a pre-opened read handle and matching metadata.
  `GetProductionDatasetInformation` is valid only while that read handle lives.
- `GenerateFileForWrite` returns a pre-opened writer. Optional metadata belongs
  to that handle; `SendProductionDatasetInformation` is rejected for readers,
  foreign sessions, closed or expired handles, and a different dataset name.
- `CloseAndCommit` awaits the authority and shared FileType commit hook before
  returning success. This server completes processing within the call and
  therefore returns a null completion-state-machine NodeId.
- A failed commit preserves the writer for retry or explicit abort. Plain
  `Close` discards uncommitted content. Additional `Open` calls on a temporary
  file are rejected: each transfer owns its single generated handle.
- Expiry, close, successful commit, session shutdown and disposal remove owned
  temporary files. Stream cleanup on session shutdown is supplied by
  `FileTypeBinder`; the periodic sweep reclaims the remaining node.
- Disposal cancels providers first, drains active and queued operations, and
  then releases streams and nodes. Concurrent callers await the same disposal
  outcome. Cleanup failures are propagated rather than reported as success.

Defaults are 16 MiB per file, 16 transfers, 1,024 descriptions per list, 256
component codes/product names, 1,024 characters per string, a 60-second inactivity
timeout and a 30-second operation deadline. Limits are configurable and
validated. Periodic cleanup runs at most one second apart; `ReclaimExpiredAsync`
also supports explicit maintenance and deterministic expiry tests.

Chapter 20.8's lifecycle description specifies sending information for a
**write** transfer; its argument-table reference to a read handle is not used
to admit metadata changes on download handles. Transfer-request events use the
current chapter 20 storage mask, including preparation, rather than older
control-system naming found in descriptive text.

### Client operations

Create `ProductionDatasetClient` directly, or register
`services.AddProductionDatasetClient()` and resolve
`ProductionDatasetClientFactory`. Sessions remain owned by the caller.

```csharp
var datasets = new ProductionDatasetClient(session, managementNodeId);

ArrayOf<ProductionDatasetInformationType> available =
    await datasets.GetListAsync("recipe-*", mouldId: "", cancellationToken);

await datasets.LoadAsync(
    StorageEnumeration.PRODUCTION, "recipe-300", [], cancellationToken);

await datasets.SetFrozenAsync(
    StorageEnumeration.PRODUCTION, true, cancellationToken);

bool modified = await datasets.ReadModifiedAsync(
    StorageEnumeration.PRODUCTION, cancellationToken);
```

Upload bytes in the device's supported format:

```csharp
await datasets.UploadAsync(
    new ProductionDatasetWriteOptionsType
    {
        Name = "recipe-301",
        Storage = StorageEnumeration.FILE_SYSTEM | StorageEnumeration.PREPARATION,
        Components = []
    },
    inputStream,
    new ProductionDatasetInformationType
    {
        Name = "recipe-301",
        Components = [6, 1, 1]
    },
    cancellationToken);
```

`DownloadAsync(options, destination)` returns matching handle-scoped information.
Use `DownloadContentAsync` when the server does not select optional information
exchange. `OpenReadAsync` and `OpenWriteAsync` expose the existing
`UaFileStream`/`UaTemporaryWriteFile` lifecycles for advanced callers.
Dispose them asynchronously; disposing a writer without committing aborts it.

The high-level client applies independent byte/list limits and an overall
deadline. Uploads to peers that return an asynchronous completion state machine
wait for `Idle` and reject `Error`; an outstanding commit is not reported as
completed. A canceled or failed download can leave partial bytes in the
caller-owned destination stream. A communication failure during commit is
inherently ambiguous: inspect authoritative state before retrying a new upload.

Optional-property absence is reported as unsupported, not a false Boolean or
empty successful response. `Proxy` and `GetStatusProxyAsync` expose the generated
typed clients; no reflection or hand-written production-dataset wire types are
used.

### Request events

The binding publishes list, download and upload requests through
`RequestListAsync`, `RequestReadAsync` and `RequestWriteAsync`. Callers supply an
operation context and pass authorization before publication.

The published events are concrete application subtypes of the model's abstract
request event types. List requests originate on `ProductionDatasetLists`;
transfer requests originate on `ProductionDatasetTransfer`. Both propagate
through the management object's notifier hierarchy.

The client supplies `ObserveListRequestsAsync`, `ObserveReadRequestsAsync` and
`ObserveWriteRequestsAsync`. Pass an existing `IStreamingSubscription`; its
queue limits and lifetime remain caller-controlled. The methods return the
generated `RequestProductionDataset*EventTypeRecord` types, including verified
concrete subtypes, without a second event schema.

## Granulate drying devices (OPC 40082-6)

The granulate-drying module in the PlasticsRubber package family implements the server/client interface from
[OPC 40082-6, version 1.0.0](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0).
They do not simulate a dryer or contain recycling-process, vendor, or production-counter logic.
An injected hardware or committed-process provider remains the authority.

| Package | Responsibility |
| --- | --- |
| `Opc.Ua.PlasticsRubber` | Existing compiled model, generated states, factories, encodings, method proxies, and event records. |
| `Opc.Ua.PlasticsRubber.Server` | Selectable device construction, asynchronous provider bindings, control ownership, committed-value publication, alarms, and hosting composition. |
| `Opc.Ua.PlasticsRubber.Client` | Discovery, generated typed operations, typed variables preserving quality/timestamps, maintenance downloads, and event streams. |

The model namespace is `http://opcfoundation.org/UA/PlasticsRubber/GDD/`.
The `Opc.Ua.GranulateDrying`, `.Server`, and `.Client` CLR namespaces remain unchanged;
their implementation lives in the corresponding PlasticsRubber projects' `GranulateDrying` folders.
`GranulateDryingDeviceType` derives from **BaseObjectType**, not a DI device type.
Instances are organized under Machinery's `Machines` folder. Their generated identification
add-ins and the existing AMB sidecar provide asset discovery without fictitious inheritance.

### Model provenance and conformance status

The GDD foundation uses the unchanged, explicitly MIT-licensed 1.0.0 NodeSet. Its common-model
dependency is this repository's independently authored 1.03 interface implementation on the
MIT-licensed 1.02 basis; it is **not** a redistributed copy of the official unlicensed 1.03 input.
See the [companion model notice](../src/Opc.Ua.PlasticsRubber/Model/NOTICE.txt).
The packages neither copy specification chapters nor replace published numeric identifiers.

This implementation is **based on** the specification; it is not OPC Foundation/EUROMAP certified.
No Basic, DesiccantBedDryer, or Full profile URI is advertised merely because the model loads.
An application's supported capabilities depend on its selected options, supplied providers,
security configuration, and tests against its actual equipment.

The inventory below covers all **51 distinct conformance-unit names** in
[clause 18.1](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/18.1).

### Hosting one address-space owner

For a standalone drying endpoint, `AddGranulateDrying` creates the stock Machinery owner
with its basic building blocks. For an existing owner, use `AddGranulateDryingToMachinery`.
Do not register a second DI or Machinery manager on the same endpoint.

```csharp
services.AddSingleton<DeviceProvider>();
services.AddSingleton<DeviceAuthorization>();

services.AddOpcUa()
    .AddServer<StandardServer>(options =>
    {
        options.ApplicationName = "Drying interface";
        options.ApplicationUri = "urn:example:drying";
        options.ProductUri = "urn:example:drying-interface";
        options.EndpointUrls.Add("opc.tcp://localhost:4840/drying");
        // Configure PKI, trust and authentication through the normal stack infrastructure.
    })
    .AddGranulateDrying()
    .AddAssetManagement()
    .ConfigureGranulateDrying(async (context, ct) =>
    {
        var options = new GranulateDryingDeviceOptions
        {
            BrowseName = "Dryer",
            Manufacturer = new LocalizedText("Equipment manufacturer"),
            Model = new LocalizedText("Device model"),
            SerialNumber = "device-serial",
            ProductInstanceUri = "urn:example:asset:dryer",
            Features = GranulateDryingDeviceFeatures.Users |
                GranulateDryingDeviceFeatures.EnergyConsumptionResettable |
                GranulateDryingDeviceFeatures.ResetEnergyMeasurement,
            Bins =
            [
                new GranulateDryingBinOptions
                {
                    Features = GranulateDryingBinFeatures.MaterialIdentification |
                        GranulateDryingBinFeatures.BatchNumber |
                        GranulateDryingBinFeatures.TemperatureController |
                        GranulateDryingBinFeatures.MaterialDryingTime |
                        GranulateDryingBinFeatures.RemainingMaterialDryingTime
                }
            ],
            DesiccantBeds =
            [
                new GranulateDryingBedOptions
                {
                    Features = GranulateDryingBedFeatures.TemperatureOfDesiccantBed |
                        GranulateDryingBedFeatures.DewPoint |
                        GranulateDryingBedFeatures.Controller
                }
            ]
        };

        GranulateDryingDevice dryer = await context.Runtime.CreateDeviceAsync(
            options,
            context.GetRequiredService<DeviceProvider>(),
            context.GetRequiredService<DeviceAuthorization>(),
            ct);

        await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
            dryer.AsNode(), cancellationToken: ct);
    });
```

`DeviceProvider` and `DeviceAuthorization` above are application-supplied implementations of
`IGranulateDryingProvider` and `IGranulateDryingAuthorization`, not built-in simulation providers.

Composition changes only the registration:

```csharp
serverBuilder
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddGranulateDryingToMachinery()
    .ConfigureGranulateDrying(async (context, ct) =>
    {
        // Create devices with the same runtime and providers as standalone hosting.
    });
```

`ConfigureGranulateDryingFor<TNodeManager>` supports an exact custom DI/Machinery owner.
That owner must load `GranulateDryingModelProvider` and dispose its Machinery resources.
The provider reuses `PlasticsRubberModelProvider` and existing DI/Machinery dependency producers;
there is one registration per namespace.

Without a DI container, construct the existing `MachineryNodeManager` with the Machinery and
granulate-drying model providers, create its address space, then call
`manager.CreateGranulateDryingRuntime()`. This is the direct construction path; it returns the
same owner-scoped runtime used by hosting. The owner adopts its asynchronous lifetime.

### Selecting features and real placeholders

- A device always has at least one concrete `BinType` instance: `Bin_001`, `Bin_002`, and so on.
- Beds are optional. Supplying descriptors creates `DesiccantBeds` and real
  `DesiccantBed_001` instances. Each selected bed must expose at least one capability.
- Supplying maintenance descriptors creates `MaintenanceInformation`, its `NodeVersion`,
  and real, uniquely identified `Maintenance_001` categories.
- `GranulateDryingDeviceFeatures`, `GranulateDryingBinFeatures`, and
  `GranulateDryingBedFeatures` select independent optional groups. `All` is available but is not
  the default. Method/status groups are created together.
- `OptionalMembers` selects additional **existing compiled standard declarations**, including
  common parameter limits, reference values, ramps, PID settings, optional energy figures,
  maintenance intervals, and identification fields. Unknown paths fail configuration rather
  than creating arbitrary tags.
- `ConfigureIdentification` can use generated typed accessors for additional nameplate fields.
  The required identity is enforced afterwards, including `DeviceClass = "Granulate Drying Device"`.
- `AnalogMetadata` supplies exact equipment units and engineering ranges for selected variables.
  Values are not converted: the provider must already use those units. Defaults use the documented
  SI units and the finite Double domain, not invented calibrated equipment limits.

For example, selecting common temperature monitoring options does not change the wire type:

```csharp
var binOptions = new GranulateDryingBinOptions
{
    OptionalMembers =
    [
        "Temperature/SetValue",
        "Temperature/LowerTolerance",
        "Temperature/UpperTolerance",
        "Temperature/Status",
        "Temperature/ResetMonitoring"
    ]
};
```

The plain bin `Temperature` is a common `MonitoredParameterType`; its reference is informational.
The bed's `TemperatureOfDesiccantBed` and `AirFlow` are `ControlledParameterType` instances.
Common providers must preserve that distinction. Lower tolerance offsets are **signed negative
offsets**, not positive magnitudes; for example, nominal 190 and lower offset -8 define 182.
The reused common binding enforces signed ordering and automatic-monitoring ownership.

### Provider and commit contract

`IGranulateDryingProvider.ReadAsync` returns initial authoritative state. It must explicitly
supply the persistently stored `RemoteControlEnabled` setting and a complete `Errors` array,
including an empty array when no errors exist. Unreported process values remain
`BadWaitingForInitialData`; the runtime does not fabricate good zeros.

`ExecuteAsync` receives a typed `GranulateDryingCommand`:

- the exact standard operation and target (device, numbered bin, bed, or maintenance category);
- the original authenticated session identifier;
- a typed HMI user or attributed maintenance-reset payload where applicable;
- the exact standard path and `Variant` for a validated value write.

The provider returns the exact UA `StatusCode` and its committed effects. A successful
state-changing request requires a newer commit. `IdentifyDevice` may succeed without a state
change because its effect is physical identification. Unsupported operations return
`BadNotSupported`; a successful-looking no-op is not a substitute for hardware support.

Function requests must publish `ON`/`OFF` or the corresponding `REQUESTED_ON`/`REQUESTED_OFF`
response. A request does not optimistically publish the final operating state.
Drying-timer reset requires its response timestamp and, when the corresponding variables are
published, the reset remaining duration. Energy reset must cover every published resettable
device/bin/bed counter; it does not reset lifetime consumption.

Use `GranulateDryingCommonProviders` for existing `IMonitoredParameterProvider`,
`IClosedLoopControlProvider`, and `IMachineConfigurationProvider` implementations.
Their existing OPC 40083 binders perform the common protocol. After a common mutation,
the drying authority is reread and must expose its newer committed effects.
The inherited maintenance `Reset` is deliberately refused: GDD requires the attributed
`GDDMaintenanceReset` operation instead.

#### Live absolute updates

```csharp
await dryer.PublishAsync(new GranulateDryingCommit(revision, commitTime)
{
    Values =
    [
        new GranulateDryingValue(
            GranulateDryingTarget.Bin(1),
            "Temperature/ActualValue",
            new DataValue(Variant.From(actualTemperature), quality, measurementTime)),
        new GranulateDryingValue(
            GranulateDryingTarget.Bin(1),
            "RemainingMaterialDryingTime",
            new DataValue(Variant.From(remainingMilliseconds), StatusCodes.Good, measurementTime))
    ]
}, cancellationToken);
```

Values are absolute, not increments. A commit is validated before its values are published.
Older revisions and contradictory repeated revisions fail; repeating identical committed state
is idempotent. Omitted values stay unchanged. A null error array means unchanged; an empty array
clears it. Per-value source quality and timestamps are preserved. Wire server timestamps are
assigned by the server, not copied from another server.

The runtime serves immutable committed snapshots through its read hooks and suppresses
post-write cache assignment for snapshot-owned values. Common parameter validation uses that
same committed image, and remote-control disablement still removes write access.
`ReadCommittedValue` is also available to local adapters.

Hardware must implement actual temperature regulation, feeder actuation, continuous versus batch
storage/protection behavior, air intervals, drying-time evolution, energy integration, persistence,
device-clock changes, and tuning. Those are provider responsibilities, not a second simulator
running in this package. A sample adapter can therefore remain only a projection of its own
committed coordinator.

### Authorization and observers

The interface does **not** impose a global one-session transport limit.
Observers can remain connected and read/subscribe while one authorized session controls a device.

`IGranulateDryingAuthorization.CanControl` is a nonblocking local ACL check used for
`UserAccessLevel` and `UserExecutable`. `AuthorizeAsync` revalidates every mutation using the
original UA context. HMI `UserRole`, `UserLevel`, card IDs, or login names never grant UA rights.
Common parameter mutations use their actual standard paths, such as `Temperature/MinValue`,
in every authorization check, not the provider enum's setting names.

The first successful authorized mutation claims the device's control lease. A second controller
is denied while that lease exists. Closing the owning session, explicitly releasing its lease,
or committing remote-control disablement releases ownership. Different devices can have different
owners.

With `RemoteControlOpcUa = false`, the standard writable variables become read-only and methods
become nonexecutable, including FileType methods. Normal attribute reads and subscriptions remain
available. Read-only page retrieval and instruction downloads do not themselves claim a control
lease. Application-added features or replacement callbacks must preserve the application's
authorization policy; the runtime does not override trusted host customizations.

### Errors, maintenance, and instructions

The complete committed error list drives both mandatory `ActiveErrors` and
`HighestActiveAlarmSeverity`. Entries retain their source NodeIds, classification, identifier,
severity, and localized message. Error reset is a provider operation; acknowledging a UA
condition does not remove a physical error.

Error conditions reuse generated common `HelpOffNormalAlarmState` and core A&C lifecycle behavior.
Maintenance uses the generated GDD `MaintenanceAlarmState`, with an application-owned concrete
subtype because the published `MaintenanceAlarmType` is abstract. Its standard `Id`, `Status`,
and selected `RemainingInterval` fields remain intact. WARNING and DUE activate the condition;
severity is configurable per category.

Conditions preserve unacknowledged return-to-normal states and use core condition branches and
refresh behavior. Event delivery is asynchronous through the existing notifier chain.
Retained error/branch growth is bounded; exceeding the configured limit reports
`BadTooManyOperations` rather than silently discarding required acknowledgement state.

Maintenance instructions use the existing namespace-zero `IFileTypeBinder`: read-only,
session-owned handles, bounded content, exact standard FileType methods, and disposal cleanup.
A null `ByteString` omits the document; an empty one publishes an empty file.
The typed client closes its handle on successful download or cancellation and enforces its own
download bound.

### Typed client

```csharp
var drying = new GranulateDryingClient(managedSession, telemetry);
ArrayOf<GranulateDryingDeviceEntry> devices =
    await drying.DiscoverDevicesAsync(cancellationToken: ct);
GranulateDryingDeviceClient dryer = drying.Device(devices[0].NodeId);
GranulateDryingBinClient bin = (await dryer.BinsAsync(ct))[0];

GranulateDryingReading<double> temperature = await bin.Temperature.ReadAsync(ct);
if (temperature.IsPresent && temperature.HasValue &&
    StatusCode.IsGood(temperature.DataValue.StatusCode))
{
    double actual = temperature.Value;
    DateTimeUtc sourceTime = temperature.DataValue.SourceTimestamp;
}

await bin.Proxy.ActivateTemperatureControllerAsync(ct);
GranulateDryingReading<FunctionStatusEnumeration> status =
    await bin.TemperatureControllerStatus.ReadAsync(ct);

GranulateDryingParameterClient parameters = drying.Parameter(
    (await bin.TemperatureParametersAsync(ct))!);
await parameters.SetValue.WriteAsync(referenceTemperature, ct);
await parameters.Proxy.ResetMonitoringAsync(ct);
```

Every method uses the generated proxy; no handwritten raw `Call` implementation substitutes
for generated operations. The generic variable handle supplies typed reads/writes because the
model generator currently emits object/method proxies, not variable accessors. Missing optional
values produce `IsPresent = false`; unavailable values preserve their bad/uncertain status.
Writes and methods propagate their exact UA failures.

`GranulateDryingClient.Parameter`, `.Energy`, and `.Configuration` add typed value access beside
generated common proxies. `UsersAsync`, `MaintenanceAsync`, `DesiccantBedsAsync`, and the bin facade
cover the corresponding selected groups. `ObserveMaintenanceAsync` and `ObserveAlarmsAsync`
reuse an existing streaming subscription and generated namespace-aware event filters/decoders.
Cancel or dispose enumerators to release their monitored items.

For DI, call `AddGranulateDryingClient` on the existing client builder. It registers
`GranulateDryingClientFactory` over the current `ManagedSession` factory; it does not create a
second connection owner. Recreate a device facade after a server topology/NodeId replacement,
as generated child proxies cache resolved instance NodeIds.

### Granulate drying conformance units

The conformance-unit names are from [OPC 40082-6 v1.0.0, clause 18.1, Table 44](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/18.1). Supported means selectable model and protocol support with the required application-supplied providers; it is not a certification claim.

| Conformance unit | Support |
| --- | --- |
| OPC 40082-6 Basic | Supported |
| OPC 40082-6 Maintenance | Supported |
| OPC 40082-6 Users | Supported |
| OPC 40082-6 DesiccantBed | Supported |
| OPC 40082-6 DryingProtection | Supported |
| OPC 40082-6 AllFeeders | Supported |
| OPC 40082-6 MaterialFeeder | Supported |
| OPC 40082-6 MachineFeeder | Supported |
| OPC 40082-6 TemperatureController | Supported |
| OPC 40082-6 AirFlowInterval | Supported |
| OPC 40082-6 ResetDryingTime | Supported |
| OPC 40082-6 DesiccantBedController | Supported |
| OPC 40082-6 MaterialIdentification | Supported |
| OPC 40082-6 BatchNumber | Supported |
| OPC 40082-6 MaterialDryingTime | Supported |
| OPC 40082-6 RemainingMaterialDryingTime | Supported |
| OPC 40082-6 DisplayLanguage | Supported |
| OPC 40082-6 IdentifyDevice | Supported |
| OPC 40082-6 ResetAllErrors | Supported |
| OPC 40082-6 ResetErrorById | Supported |
| OPC 40082-6 ResetEnergyMeasurement | Supported |
| OPC 40082-6 MainFailureBehaviour | Supported |
| OPC 40082-6 TotalEnergy | Supported |
| OPC 40082-6 EnergyConsumptionResettable | Supported |
| OPC 40082-6 MaintenanceInformation | Supported |
| OPC 40082-6 EnergyConsumptionLimit | Supported |
| OPC 40082-6 DewPoint | Supported |
| OPC 40082-6 AirFlow | Supported |
| OPC 40082-6 TemperatureOfDesiccantBed | Supported |
| OPC 40082-6 TemperatureDesiccantBedOut | Supported |
| OPC 40082-6 TemperatureBackToDesiccantBed | Supported |
| OPC 40082-6 ExpiredDryingTime | Supported |
| OPC 40082-6 TemperatureReduction | Supported |
| OPC 40082-6 MaterialAvailableDryer | Supported |
| OPC 40082-6 MaterialAvailableMachine | Supported |
| OPC 40082-6 DustRemovalPulses | Supported |
| OPC 40082-6 DryingLevel | Supported |
| OPC 40082-6 AirFlowMode | Supported |
| OPC 40082-6 IntervalTimeOn | Supported |
| OPC 40082-6 IntervalTimeOff | Supported |
| OPC 40082-6 InitialFillingActive | Supported |
| OPC 40082-6 AirSource | Supported |
| OPC 40082-6 TotalAirConsumption | Supported |
| OPC 40082-6 DryAirTemperature | Supported |
| OPC 40082-6 AmbientAirTemperature | Supported |
| OPC 40082-6 TopTemperature | Supported |
| OPC 40082-6 ConeTemperature | Supported |
| OPC 40082-6 CylinderTemperature | Supported |
| OPC 40082-6 LooseningAid | Supported |
| OPC 40082-6 MaintenanceInstructions | Supported |
| OPC 40082-6 RemainingInterval | Supported |
