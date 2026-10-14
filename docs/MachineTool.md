# Machine Tools and Wireless Peripherals

The `Opc.Ua.MachineTool*` libraries implement released
[OPC 40501-1 MachineTool](https://reference.opcfoundation.org/specs/OPC-40501-1)
and [OPC 40505 Wireless Machine Tool Peripherals](https://reference.opcfoundation.org/specs/OPC-40505)
(WMTP). They follow the same model, fluent server-builder and typed-client
approach as [Machinery](Machinery.md) and [Machine Vision](Vision.md).

WMTP is bundled in the MachineTool model, server, client and MCP packages and
in this guide. It retains its own wire and CLR namespaces. A wireless-only
server does not need a `MachineToolType` instance.

These libraries expose equipment observations and explicitly bound standard
requests. They are not CNC executors, radio/pairing drivers, firmware installers
or equipment safety systems.

## Contents

- [Library layout](#library-layout)
- [Models and dependencies](#models-and-dependencies)
  - [Versions and namespaces](#versions-and-namespaces)
  - [Package dependencies versus loaded models](#package-dependencies-versus-loaded-models)
- [Quick start - server](#quick-start---server)
  - [Register a mandatory-only machine](#register-a-mandatory-only-machine)
  - [Publish an acquired observation](#publish-an-acquired-observation)
- [Quick start - client](#quick-start---client)
  - [Discover and read typed instances](#discover-and-read-typed-instances)
  - [Use a container-managed connection](#use-a-container-managed-connection)
- [Server hosting and ownership](#server-hosting-and-ownership)
  - [Standalone hosting](#standalone-hosting)
  - [Compose into an existing owner](#compose-into-an-existing-owner)
  - [Custom owners and generated subtypes](#custom-owners-and-generated-subtypes)
- [MachineTool builder reference](#machinetool-builder-reference)
  - [Mandatory structure and monitoring](#mandatory-structure-and-monitoring)
  - [Tools, working units and life](#tools-working-units-and-life)
  - [Retained Production](#retained-production)
  - [Machinery and ISA-95 job management](#machinery-and-isa-95-job-management)
  - [FileSystem and WorkMasters](#filesystem-and-workmasters)
  - [Prognoses and notifications](#prognoses-and-notifications)
- [Wireless peripheral server](#wireless-peripheral-server)
  - [Build an independent peripheral](#build-an-independent-peripheral)
  - [Measurement kinds and acquired values](#measurement-kinds-and-acquired-values)
  - [Trigger and delta configuration](#trigger-and-delta-configuration)
  - [Work and service records](#work-and-service-records)
  - [Calibration, device time and byte transfer](#calibration-device-time-and-byte-transfer)
- [Typed client workflows](#typed-client-workflows)
  - [Machine monitoring, production and services](#machine-monitoring-production-and-services)
  - [Bounded data and event observation](#bounded-data-and-event-observation)
  - [Wireless measurements and configuration](#wireless-measurements-and-configuration)
  - [Wireless record queries](#wireless-record-queries)
- [Lifetime, cancellation and failure handling](#lifetime-cancellation-and-failure-handling)
  - [Server transactions and dynamic removal](#server-transactions-and-dynamic-removal)
  - [Client and provider ownership](#client-and-provider-ownership)
- [MCP integration](#mcp-integration)
  - [Select or embed the bounded profile](#select-or-embed-the-bounded-profile)
  - [Passive and explicitly mutating tools](#passive-and-explicitly-mutating-tools)
  - [Authorization, budgets and cancellation](#authorization-budgets-and-cancellation)
- [Conformance and support limits](#conformance-and-support-limits)
  - [Supported surfaces](#supported-surfaces)
  - [Runtime claims and deployment requirements](#runtime-claims-and-deployment-requirements)
- [Model provenance](#model-provenance)
- [Samples and related documentation](#samples-and-related-documentation)

## Library layout

The names below are assembly/project names. The corresponding NuGet package ID
is `OPCFoundation.NetStandard.` followed by that name; for example,
`OPCFoundation.NetStandard.Opc.Ua.MachineTool.Client`.

| Library | Responsibility |
| --- | --- |
| `Opc.Ua.MachineTool` | Both generated models, node states, identifiers, loaders, client proxies and shared provider/snapshot contracts |
| `Opc.Ua.MachineTool.Server` | Fluent machine/peripheral builders, acquisition and publication, provider-backed methods and hosting extensions |
| `Opc.Ua.MachineTool.Client` | Subtype-aware discovery, bounded reads/observations, generated service navigation and WMTP workflows; no server-runtime dependency |
| `Opc.Ua.Mcp.MachineTool` | The combined MachineTool/WMTP MCP catalog over host-owned named sessions |

The first three libraries target .NET Framework 4.8 and .NET 8, 9 and 10.
The MCP library targets .NET 8, 9 and 10. They follow the repository's normal
root package-version policy, including `.Debug` variants; they are not
independently forced to preview. The released specification version is distinct
from the NuGet version. NativeAOT application examples use the same APIs.

## Models and dependencies

### Versions and namespaces

| Model | Version | Publication date | Wire namespace | CLR namespace |
| --- | --- | --- | --- | --- |
| MachineTool, OPC 40501-1 | `1.02.0` | 2024-11-01 | `http://opcfoundation.org/UA/MachineTool/` | `Opc.Ua.MachineTool` |
| Wireless Machine Tool Peripherals, OPC 40505 | `1.0.0` | 2025-11-01 | `http://opcfoundation.org/UA/WMTP/` | `Opc.Ua.MachineTool.WMTP` |

Use namespace URIs, generated identifiers and proxies rather than hard-coded
namespace indexes. `MachineToolModels.GetNamespaceUris(parts)` gives the
selected non-core closure in dependency order.
`AddMachineToolTypeSystem` loads that closure into an existing node collection
or registers its encodeable factories; the caller commits a factory builder.
Model loading alone does not instantiate a machine or grant any capability.

### Package dependencies versus loaded models

The single model package references the union of both dependency graphs, but
runtime model selection remains independent:

| `MachineToolParts` | Runtime closure beyond UA core |
| --- | --- |
| `MachineTool` (default) | DI, IA, Machinery building blocks, ISA-95 Job Control V2, Machinery Jobs and MachineTool |
| `Wmtp` | DI, IA, Machinery building blocks, IRDI, PA-DIM, Machinery ProcessValues and WMTP |
| `All` | Both closures, deduplicated |

The owner needs Machinery `BuildingBlocks`, plus `Jobs` for MachineTool or
`ProcessValues` for WMTP. Standalone registration selects those parts.
Referencing the WMTP-containing assembly does not cause a MachineTool-only
endpoint to expose WMTP, nor does a WMTP-only endpoint require ISA-95 jobs.

The retained MachineTool `Production` declarations are still present in 1.02.0,
even where deprecated in favor of Machinery Jobs. They are implemented rather
than removed: older clients and [GMS](GMS.md) still depend on their wire identity.

## Quick start - server

### Register a mandatory-only machine

The following host-configuration helper creates one machine on the standard
Machinery owner. `provider` is this machine's application-owned acquisition
adapter; `retainRuntime` stores the returned handle in the application's
acquisition coordinator. It should not dispose the handle at callback exit.

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.Hosting;

static IOpcUaServerBuilder AddMillServer(
    IServiceCollection services,
    IMachineToolProvider provider,
    Action<MachineToolRuntime<MachineToolState>> retainRuntime)
{
    return services.AddOpcUa()
        .AddServer(options =>
        {
            options.ApplicationName = "MachineToolServer";
            options.ApplicationUri = "urn:example:machinetool-server";
            options.EndpointUrls.Add("opc.tcp://localhost:62558/MachineToolServer");
        })
        .AddMachineTool()
        .ConfigureMachineTool(async (context, ct) =>
        {
            MachineToolRuntime<MachineToolState> machine = await context
                .AddMachineTool("Mill")
                .WithProvider(provider)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Example manufacturer");
                    identity.SerialNumber = "Mill-001";
                    identity.ProductInstanceUri = "urn:example:mill:001";
                })
                .BuildAsync(ct);
            retainRuntime(machine);
        });
}
```

Register this while building the application host, then await normal host
startup and shutdown. Configure application certificates, trusted peers,
endpoint policies and user authentication through the existing
[server hosting](DependencyInjection.md#server-feature) and
[certificate infrastructure](CertificateManager.md).
No trust bypass or embedded credential is needed by the builder.

`BuildAsync` acquires and validates the provider snapshot before publishing the
instance. Missing identification, missing providers, invalid selected
capabilities or conflicting identities fail setup; they do not leave an
advertised half-machine. The initial default snapshot explicitly marks
unavailable acquisitions with bad quality.

### Publish an acquired observation

`IMachineToolProvider.ReadAsync` returns an immutable `MachineToolSnapshot`.
The application decides when to acquire and refresh; a client read is not an
instruction to run a program.

For a mandatory-only simulation, create an
`InMemoryMachineToolProvider(new MachineToolSnapshot())` and later publish
an explicitly supplied observation:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

static async ValueTask PublishMillObservationAsync(
    InMemoryMachineToolProvider provider,
    MachineToolRuntime<MachineToolState> machine,
    MachineOperationMode observedMode,
    DateTimeUtc acquiredAt,
    CancellationToken ct)
{
    await provider.SetSnapshotAsync(new MachineToolSnapshot
    {
        Operation = new MachineToolOperation
        {
            Mode = observedMode,
            Sample = new MachineToolSampleInfo(acquiredAt, StatusCodes.Good)
        }
    }, ct);
    await machine.RefreshAsync(ct);
}
```

This example supplies only overall operation; the active program remains
explicitly unavailable. When optional inventories are enabled, provide their
complete coherent observations too. `SetSnapshotAsync` assigns the next
revision in this in-memory adapter. A production adapter must supply
monotonically increasing revisions itself and must not mutate a returned graph.

## Quick start - client

### Discover and read typed instances

The typed facade takes an already connected `ISession`. It registers the
selected model factories, resolves actual subtypes and keeps optional absence
separate from a present bad/uncertain `DataValue`.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;

static async ValueTask ReadMachinesAsync(
    ISession session, Opc.Ua.ITelemetryContext telemetry, CancellationToken ct)
{
    await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
    ArrayOf<MachineToolDeviceEntry> devices = await client.DiscoverAsync(cancellationToken: ct);
    for (int index = 0; index < devices.Count; index++)
    {
        MachineToolDeviceEntry device = devices[index];
        if (device.Kind != MachineToolDeviceKind.MachineTool)
        {
            continue;
        }
        MachineToolReadSnapshot snapshot = await client.ReadSnapshotAsync(device.NodeId, ct);
        Console.WriteLine($"{device.NodeId}: {snapshot.OperationMode.StatusCode}");
        if (snapshot.OperationMode.WrappedValue.TryGetValue(out int mode))
        {
            Console.WriteLine($"Observed mode {mode}, acquired {snapshot.OperationMode.SourceTimestamp}");
        }
    }
}
```

The caller still owns `session`. Disposing the facade cancels and drains its
observations; it does **not** close the session. Use the returned kind to
distinguish machines from wireless peripherals, rather than treating every
discovered root as a `MachineToolType`.

### Use a container-managed connection

Add `AddMachineToolClient()` to an existing `AddOpcUa().AddClient(...)`
registration, then resolve `MachineToolClientFactory`. The equivalent
`Func<CancellationToken, Task<MachineToolClient>>` is also registered.

The injected managed-session accessor caches a shared connection for the
container. Factory-created facades borrow it, just like directly constructed
facades. Disposing one facade, or cancellation immediately after acquisition,
must not disconnect other consumers. The container disposes the shared
session at shutdown; applications that create a session independently retain
its ownership. See [client hosting](DependencyInjection.md#client-feature).

## Server hosting and ownership

### Standalone hosting

`AddMachineTool(options => ...)` selects one `MachineryNodeManager`.
`MachineToolServerOptions.InstanceNamespaceUri` defaults to
`urn:opcua-netstandard:machinetool:instances` and must be an absolute application
URI, not a standard model namespace.

`ConfigureMachineTool` awaits a
`Func<IMachineryBuildContext, CancellationToken, ValueTask>` after the model
closure has loaded. Create multiple machines or peripherals in that callback,
with distinct names, providers and stores. Registration and rollback belong
to the shared owner; a handle belongs to one instance.

### Compose into an existing owner

Do not call `AddMachineTool()` when another registration already owns the same
DI/Machinery address space. Load the additional model through the existing
pipeline and target that owner explicitly:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Server.Hosting;

static IOpcUaServerBuilder AddMachineToolModelsToCell(IOpcUaServerBuilder server)
{
    return server
        .AddMachinery(options =>
        {
            options.Parts = MachineryParts.BuildingBlocks |
                MachineryParts.Jobs | MachineryParts.ProcessValues;
            options.InstanceNamespaceUri = "urn:example:cell:instances";
        })
        .AddMachineToolToMachinery(options => options.Parts = MachineToolParts.All);
}
```

Here `AddMachinery` establishes the cell's owner; omit that call if it is already
registered. Use `ConfigureMachineToolFor<MachineryNodeManager>(...)` to build
on it. Shared composition uses the existing owner's application namespace,
not a second namespace-owner registration.

GMS, cranes and Machine Vision can use the same owner when its required model
closure and Machinery parts are present. The
[CompanionSuite sample](../samples/CompanionSuite/) demonstrates that composition
without registering duplicate `DeviceSet` or `Machines` owners.

### Custom owners and generated subtypes

`ConfigureMachineToolFor<TNodeManager>` targets an existing `DiNodeManager`
subtype that supports the Machinery construction pipeline and node-ID
allocation contracts. Loading model types and implementing the compatible
owner are separate from running the configurator; see
[Machinery composition](Machinery.md#coexisting-with-a-server-that-already-owns-di-or-isa-95).

`context.AddMachineTool<TState>(name, createState)` accepts a generated
MachineTool subtype. The factory must return an unpublished node with the
requested identity. `WithMachinery(...)` configures that same typed Machinery
builder; it must not independently build, register or dispose it. This is how
GMS keeps its native root, result management and derived child types instead
of being represented by a copied base machine.

## MachineTool builder reference

### Mandatory structure and monitoring

`WithFeatures(MachineToolFeatures.None)` is the default. Mandatory inherited
identification, monitoring and retained active-program structure are not removed
just because optional features are off.

| Selection | Provider data and behavior |
| --- | --- |
| `Channels` | Ordinary and combined channels, execution/mode observations and override values; combined channels refer to real ordinary channels |
| `SoftwareIdentification` | Installed software identities, separate from the overall machine revision |
| `AdditionalIdentification` | Requires component name, model, construction year/month and device class on the nameplate |
| `WorkingUnits` | Spindles, lasers and EDM generators with their reported modes and values |
| `Stacklights` | Actual segmented IA stacklights, not a computed substitute for machine state |
| `LegacyPowerOnDuration` | Retained deprecated integral-hours counter |
| `OperationCounters` | DI/Machinery duration counter using the runtime's declared millisecond input |
| `PartsProducedInLifetime` | Explicit lifetime production count, not a count inferred from reads |
| `MachineryState` | Nested Machinery item/operation-mode observations |
| `Obligation`, `MaintenanceMode` | Responsibility and maintenance substate; selections and parent states must be compatible |

`MachineToolSampleInfo` carries acquisition time and quality. Feed overrides,
mode values, counters and inventory references are validated before commit.
An unsupported or missing value is not silently converted into a successful
zero. Optional flags are capability selections, not permission to fabricate data.

`WithMonitoringRequirements` describes the deployment's monitoring needs for
conformance evaluation. It does not increase server quotas or turn on optional
features.

### Tools, working units and life

`WithEquipment(MachineToolEquipmentOptions)` configures `Tools`, `DynamicTools`
and `ToolLife`, with finite tool, element, text and per-tool life-entry limits.
Tools and multi-tools retain their stable provider keys, identifiers, location,
lock state and independent life measurements. Life values do not implicitly
drive a physical tool change.

Dynamic tools use NodeVersion/model-change reporting and the shared final-value
publication barrier. Removed equipment is not disposed while its final required
observation is still waiting to enter a Publish response. Typed factories can
create real generated companion subtypes and return their owned resources.

The options added by `WithEquipment`, `WithProduction` and `WithPrognoses`
contribute feature flags. A later `WithFeatures(...)` replaces the combined
selection, so configure it deliberately rather than unintentionally clearing
an earlier module.

### Retained Production

`WithProduction(MachineToolProductionOptions)` binds the retained Production
model, including jobs, ordered programs, part sets, individual parts, statistics
and interruption conditions. The provider supplies production phases and
transition sequence numbers; `Initializing`, `Running`, `Interrupted`, `Ended`
and `Aborted` are observations, not new execution methods.

| Selection | Effect |
| --- | --- |
| `Production` | Retained jobs/programs and state/transition information |
| `DynamicProductionPlan` | Insertion/removal using stable keys, publication barriers and resource cleanup |
| `ProductionParts` | Part sets, parts and coherent quality counts |
| `ProductionStatistics` | Retained UInt32 lifetime statistics |
| `ProductionInterruptions` | Known interruption reasons as retained standard conditions |

The default bounds include 64 jobs, 128 programs per job, 32 part sets per job,
1,024 parts per set and 4,096 aggregate elements. Select tighter limits in
`MachineToolProductionOptions` when appropriate; there is no silent truncation.
The runtime validates references, count consistency and permitted transitions
before replacing the accepted snapshot.

### Machinery and ISA-95 job management

The modern `JobManagement` AddIn is separate from retained Production.
It lives under `MachineryBuildingBlocks` and uses the shared ISA-95 Job Control
V2 binder. Complete MachineTool job management requires **both**
`IIsa95JobOrderReceiverV2` and `IIsa95JobResponseProviderV2`, plus a per-instance
`IIsa95JobOrderCatalog`. A receiver-only configuration is rejected.

```csharp
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

static MachineToolBuilder<MachineToolState> BindJobSource(
    MachineToolBuilder<MachineToolState> machine,
    MachineToolJobSource source)
{
    return machine
        .WithProvider(source)
        .WithJobManagement(new MachineToolJobManagementOptions
        {
            Receiver = source,
            ResponseProvider = source,
            Catalog = source,
            PredefinedParameters = true
        })
        .WithProduction(new MachineToolProductionOptions
        {
            Features = MachineToolFeatures.Production |
                MachineToolFeatures.DynamicProductionPlan
        });
}
```

Construct the source with explicit `MachineToolJobSourceOptions` and job/program
descriptors. `MachineToolJobSource` is a bounded, single-host, non-durable
reference adapter linking accepted ISA-95 state with retained observations.
It never executes CNC hardware. Keep it application-owned and dispose it after
its runtimes have drained.

Standard verbs remain `Store`, `StoreAndStart`, `Start`, `Stop`, `Abort`,
`Pause`, `Resume`, `Clear`, `Cancel`, `Update` and `RevokeStart`. Their UInt64
`ReturnStatus` is an ISA-95 bitmap: success is **1**, not UA `Good` (zero).
An accepted order is not evidence of physical execution. Providers describing
different equipment must not be combined into one instance's control,
response and monitoring views.

### FileSystem and WorkMasters

`WithFileSystem(IFileSystemProvider, includeWorkMasters, FileDirectoryBindingOptions)`
uses the existing [file-system binder](../src/Opc.Ua.Server/FileSystem/FileDirectoryBinder.cs).
It does not add another
file node manager. When selected, `WorkMasters` must already exist as a directory
in the provider and is exposed with the MachineTool-qualified browse name.
An absent directory or inadequate materialization limits fail setup.

The provider owns storage and access decisions. Operation flags and
`MaxEntries`/`MaxDepth` bound the shared directory binding. The runtime owns
materialized nodes and session-bound file handles, not the application-supplied
provider. Remote paths are scoped UA paths, not permission to read host-local
files. Reading a program file does not execute it.

### Prognoses and notifications

`WithPrognoses(MachineToolPrognosisOptions)` selects explicit
`SupportedKinds` and the `Prognoses`/`DynamicPrognoses` features. The provider
supplies predictions and actual affected-entity references; the library does not
infer equipment forecasts. Limits cover prognosis count, referenced entities
and text.

`WithNotifications(MachineToolNotificationOptions)` requires a separate
`IMachineToolNotificationProvider`. It binds errors, alerts and messages through
the existing alarm/event machinery, with bounded identities, message batches
and refresh time. `RefreshNotificationsAsync` is separate from the ordinary
equipment snapshot refresh.

The notification model carries retained conditions, enable/acknowledge/comment
behavior and original event identities. Passive observation does not acknowledge
or confirm conditions. Use [Alarms and Conditions](AlarmsAndConditions.md) for
the standard condition lifecycle rather than inventing machine-specific verbs.

## Wireless peripheral server

### Build an independent peripheral

Select `MachineToolParts.Wmtp` for a wireless-only endpoint, or `All` to host
machines and peripherals together. Both use `ConfigureMachineTool`; inside the
callback use `AddWirelessPeripheral`, not `AddMachineTool`.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;

static ValueTask<WmtpPeripheral> BuildTemperaturePeripheralAsync(
    IMachineryBuildContext context,
    IWmtpDeviceProvider device,
    IWmtpMeasurementProvider measurements,
    IWmtpAuthorization authorization,
    EUInformation temperatureUnits,
    CancellationToken ct)
{
    return context.AddWirelessPeripheral("TemperatureProbe")
        .WithProvider(device)
        .WithMeasurementProvider(measurements)
        .WithAuthorization(authorization)
        .WithIdentification(identity =>
        {
            identity.Manufacturer = new LocalizedText("Example manufacturer");
            identity.SerialNumber = "Probe-001";
        })
        .AddMeasurement(new WmtpMeasurementDefinition
        {
            Name = "Temperature",
            Kind = WmtpMeasurementKind.Temperature,
            DataType = BuiltInType.Double,
            EngineeringUnits = temperatureUnits,
            EURange = new Opc.Ua.Range { Low = -20, High = 120 },
            InstrumentRange = new Opc.Ua.Range { Low = -20, High = 120 },
            ValuePrecision = 0.1,
            Definition = "Acquired probe temperature",
            TriggerMilliseconds = 1000,
            Delta = Variant.From(0d)
        })
        .BuildAsync(ct);
}
```

The nameplate is the existing component identification inside
`DeviceInformation`. The explicit device adapter can also implement measurement,
calibration, time or transfer interfaces; separate `With...Provider` calls
override those interfaces for this peripheral.

No unkeyed device provider is chosen implicitly from DI. A shared authorization
policy may be resolved from the construction context, but device selection
remains explicit. Selected methods require both their provider and authorization
policy before any peripheral is published.

### Measurement kinds and acquired values

`WmtpMeasurementKind` covers all eight released kinds:

| Kind | Kind | Kind | Kind |
| --- | --- | --- | --- |
| Temperature | RotationSpeed | Humidity | Acceleration |
| Force | Torque | Position | Dimension |

Each measurement is a generated `WMTPMeasurementType` composed with Machinery
ProcessValues/PA-DIM, not a second signal implementation. Its definition supplies
the exact numeric `BuiltInType`, units, engineering/instrument ranges, precision,
sample category, trigger and delta. Distinct sensor names permit repeated kinds;
the builder permits at most 256 sensors per peripheral.

`IWmtpMeasurementProvider.ReadAsync(sensor, ct)` returns
`WmtpMeasurementSample`, including the original `DataValue`, device index and
measurement period. A bad/null sample is unavailable data, not a new zero.
`StaleAfter` controls when a usable acquisition becomes
`UncertainLastUsableValue`; refreshing receipt time does not refresh the
equipment's acquisition timestamp.

| `WmtpMeasurementFeatures` | Additional behavior |
| --- | --- |
| `LimitCounters` | Status and duration above high/high-high with standard references |
| `InternalUpdateInterval` | The sensor's own sampling interval |
| `WorkCycleInformation` | Acquisition timestamp, index and measurement period |
| `Uncertainty` | Absolute and relative uncertainty |
| `LimitAlarm` | The shared ProcessValues limit alarm |

### Trigger and delta configuration

The mandatory `SetTriggerSettings` and `SetDeltaCondition` methods are
provider-backed. Trigger is a whole-millisecond value representable by UInt32.
Delta retains its numeric input type; incompatible, nonfinite, negative or
unrepresentable values are refused rather than rounded into another type.

**Trigger and delta must not both be zero.** Each method passes both proposed
settings to `IWmtpMeasurementProvider.ConfigureAsync`, so acceptance is atomic.
Before acceptance, refusal or cancellation leaves the prior settings unchanged.
After the provider accepts, a later caller cancellation does not discard that
accepted configuration. A client whose request was interrupted must inspect
the current settings instead of assuming rollback or retrying automatically.

### Work and service records

`WithWorkRecords(store, features)` and `WithServiceRecords(store, features)`
select independent datasets. The same store instance cannot back both.
`IWmtpRecordStore` exposes count, complete-copy read and atomic delete; reads
never consume records.

The basic dataset supplies abort, count and complete-report operations.
`WmtpRecordFeatures` explicitly selects time counts, index/time reports,
first/last reports and all/index/time deletion. Index and acquisition-time
ranges are inclusive. First/last refer to storage order.

`CombinedReportAll` means the complete dataset, not an implicit page.
If its encoded response exceeds the configured report budget, it fails.
Use a supported explicit range; do not silently truncate the result.
`InMemoryWmtpRecordStore` is a bounded reference store, not distributed durable
storage. Work and service records remain separate even when they describe the
same sensor.

Operation coordination is peripheral-scoped. An authorized `AbortOperation`
may affect the peripheral's active work, so authorization receives both the
calling session and active operation's session. A local cancellation or timeout
never implicitly sends this method.

### Calibration, device time and byte transfer

| `WmtpFeatures` | Required adapter or observation |
| --- | --- |
| `Calibration` | `IWmtpCalibrationProvider`; observed calibration mode plus explicit mode request |
| `DeviceTime` | `IWmtpDeviceTimeProvider`; UTC time with `TimeZoneDataType` offset/DST information |
| `Firmware`, `License` | `IWmtpTransferProvider`; bounded byte acceptance, plus selected licence metadata |
| `Battery` | Original battery ProcessValue sample |
| `ServiceInformation` | Service dates and device-reported lifecycle status |
| `AdvancedServiceInformation` | Basic service information and the specified lifetime/operation counters |
| `WorkRecords`, `ServiceRecords` | The corresponding explicit record stores |

`WithByteLimits(maximumTransferBytes, maximumReportBytes)` bounds the server
payloads; transport/message quotas may impose a lower limit. The maximum
configurable value is 64 MiB for each, with a minimum report budget of 256 bytes.

Setting device time does not change the server's `TimeProvider`. Calibration-mode
acceptance does not prove a physical calibration completed. Firmware/licence
transfer success means **the provider accepted the bytes**; it does not assert
installation, restart, signature verification or licence activation.
Those responsibilities belong to the application adapter and equipment.

## Typed client workflows

### Machine monitoring, production and services

| API | Result or role |
| --- | --- |
| `DiscoverAsync` | Actual machine/peripheral roots, including vendor subtypes |
| `ReadSnapshotAsync` | Monitoring values with raw quality/time and optional absence |
| `ReadEquipmentAsync` | Tools/multi-tools, identities, locations, locks and life |
| `ReadProductionAsync` | Retained jobs, programs, parts, statistics and transitions |
| `ReadPrognosesAsync` | Predictions and their real target relationships |
| `GetMachineProxy`, `GetPeripheralProxy` | Generated namespace-correct advanced proxies |
| `GetJobManagementAsync` | Optional generated Machinery Jobs proxy |
| `GetFileSystemAsync`, `GetWorkMastersAsync` | Optional standard directory proxies |
| `GetWirelessPeripheral` | Typed WMTP workflow on the same session |

Missing optional service AddIns return null where the accessor documents it.
Missing mandatory structure, denied access and service failures remain errors.
Discovering a namespace or root is not proof of every optional capability.

Use the generated job-role proxies below the returned Jobs object and the
existing [ISA-95](ISA95.md) and [file client](FileSystemClient.md) workflows.
Do not substitute a job-response receiver for the published response provider,
or assume a same-named directory is the qualified WorkMasters node.

### Bounded data and event observation

`ObserveDataAsync` observes a machine's monitoring/active-program values.
`ObserveValuesAsync` accepts 1-256 distinct non-null native variable NodeIds,
including WMTP signal nodes. Both create their own subscription and preserve
source timestamps and status.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;

static ValueTask<MachineToolDataObservation> ObserveMachineAsync(
    MachineToolClient client, NodeId machineId, CancellationToken ct)
{
    return client.ObserveDataAsync(machineId, new MachineToolObservationOptions
    {
        Window = TimeSpan.FromSeconds(5),
        MaximumNotifications = 20
    }, ct);
}
```

The observation window includes setup and cannot exceed 60 seconds.
The maximum retained notification count is 100 and decoded payload is bounded.
Results indicate count-limit completion and loss; an empty timed window is not
proof of a particular machine state.

`ObserveEventsAsync` is passive. `ObserveConditionsAsync` explicitly requests
standard retained-condition refresh and reports refresh completion. Neither
implicitly acknowledges or confirms a condition. Generated event decoders and
the shared event collector preserve concrete subtype identity and field order.
See [subscriptions](Subscriptions.md) for long-lived application subscriptions.

### Wireless measurements and configuration

`GetWirelessPeripheral(peripheralId)` returns a `WmtpPeripheralClient`.
`ReadSnapshotAsync` reads device metadata and `ReadMeasurementsAsync` reads
the bounded native inventory. A measurement's `NodeId` is its configuration
object; its **`ValueNodeId`** is the PA-DIM signal to observe.

The generated measurement proxy preserves the released method signatures:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.WMTP;

static async ValueTask ConfigureWirelessSensorAsync(
    WmtpPeripheralClient peripheral, NodeId measurementId, CancellationToken ct)
{
    WMTPMeasurementTypeClient sensor = peripheral.GetMeasurementProxy(measurementId);
    await sensor.SetDeltaConditionAsync(Variant.From(2d), ct);
    await sensor.SetTriggerSettingsAsync(0, ct);
}
```

This is two explicit requests, not a client-side transaction. Each preserves
the both-not-zero invariant independently; if the second fails, the first
accepted change remains. Choose a delta type compatible with the actual sensor.

`SetCalibrationAsync`, `SetDeviceTimeAsync` and `TransferAsync` use generated
configuration navigation. Transfers take bounded `ByteString` data, not a path
or URL. They do not add installation or licensing semantics.

### Wireless record queries

The wrapper normalizes the two released work/service proxy shapes while keeping
the same standard operations:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.WMTP;

static async ValueTask<ArrayOf<WMTPOutputDataType>> ReadWorkRecordsAsync(
    WmtpPeripheralClient peripheral, CancellationToken ct)
{
    WmtpRecordClient records = await peripheral.GetWorkRecordsAsync(ct);
    return await records.ReadAsync(new WmtpRecordQuery
    {
        Selection = WmtpRecordSelection.Index,
        FromIndex = 1,
        ToIndex = 20
    }, maximumRecords: 20, cancellationToken: ct);
}
```

The endpoint must select index reports for this example. Use
`GetServiceRecordsAsync` for the other dataset.
`CountAsync` supports all/time selections; `ReadAsync` supports
all/index/time/first/last; `DeleteAsync` supports all/index/time.
An absent optional method is not an empty successful result.
`ReadAsync` rejects reports above the caller's bound; the default is 4,096.
`AbortAsync` is always an explicit authorized request.

## Lifetime, cancellation and failure handling

### Server transactions and dynamic removal

MachineTool refresh separates acquisition/validation, coherent commit and
publication. Rejected acquisition does not overwrite the previously accepted
snapshot. A publication failure after acceptance is different:
`RetryPublicationAsync` retries the retained values/events without reacquiring
or repeating equipment effects.

Dynamic production, tools and prognoses use the shared
[final-value publication contract](NodeManagers.md#final-value-publication-before-node-removal).
It waits for the specific terminal queue entry to enter a real Publish
response, not merely an equal older value or a queued notification. It does not
claim network delivery or client acknowledgement. Failed/cancelled publication
does not authorize removing the still-required subtree.

Callbacks must honor cancellation and avoid reentering their own runtime's
acquisition/disposal path. Stop acquisition before shutting down the owning
host. Disposal drains this instance's registrations and owned resources rather
than touching another machine's provider or subscriptions.

### Client and provider ownership

| Resource | Owner |
| --- | --- |
| Application-supplied MachineTool provider, job source, notification provider or file provider | Application |
| WMTP providers/stores supplied normally | Application |
| Resource passed to `WmtpPeripheralBuilder.WithOwnedResource` | Peripheral only after successful construction; do not transfer a shared resource |
| Runtime-created nodes, subscriptions, metadata/file handles and registration resources | Their existing runtime/operation owner |
| Session supplied to `MachineToolClient`, including one acquired by its factory | Application or DI container |

WMTP failed construction does not take ownership of caller resources.
Intentionally shared device adapters must remain application-owned and
coordinate their own equipment access.

Local cancellation ends the caller's wait and triggers call-owned cleanup.
It does not prove a submitted remote write was rolled back, does not send a
wireless Abort implicitly, and does not close a borrowed connection.
Keep independent session shutdown in the code that actually owns the session.

## MCP integration

### Select or embed the bounded profile

The host profile is `machinetool`; there is no separate `wmtp` profile:

```powershell
opcua-mcp --profile machinetool
```

For embedding:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

static IMcpServerBuilder AddMachineToolMcp(IServiceCollection services)
{
    services.AddOpcUaMcpCore().AddOpcUaMcpMachineTool();
    return services.AddMcpServer()
        .WithStdioServerTransport()
        .WithOpcUaMcpFilters()
        .WithOpcUaMachineToolTools(McpToolProfile.MachineTool);
}
```

The profile-set overload supports composed catalogs and deduplicates common
connection tools. Calls borrow the host's `OpcUaSessionManager` sessions;
provide `sessionName` when more than one connected session is available.

### Passive and explicitly mutating tools

| Group | Tools |
| --- | --- |
| Machine inventory/reads | `machinetool_list_devices`, `machinetool_read_machine`, `machinetool_read_equipment`, `machinetool_read_production`, `machinetool_read_prognoses` |
| Observation | `machinetool_observe_values`, `machinetool_observe_machine`, `machinetool_observe_events` |
| Jobs/files | `machinetool_query_jobs`, `machinetool_control_job`, `machinetool_list_files`, `machinetool_read_file`, `machinetool_change_file` |
| Wireless reads | `wmtp_read_peripheral`, `wmtp_count_records`, `wmtp_report_records` |
| Wireless mutations | `wmtp_configure_measurement`, `wmtp_configure_peripheral`, `wmtp_delete_records`, `wmtp_abort_operation`, `wmtp_transfer` |

Mutations require explicit `confirm=true` and remain subject to the remote
server's authorization and provider refusal. Reads do not start jobs, change
settings, consume records or acknowledge conditions. Native job return bitmaps,
UA service status and firmware/licence acceptance are not interchangeable.

### Authorization, budgets and cancellation

Profile selection controls the catalog, not authority.
The host's privileged/live capabilities are independently default-off;
enabling a capability also requires appropriate caller grants and network rules.
Confirmation flags do not replace those controls or OPC UA permissions.
See [MCP execution policy](McpServer.md#privileged-and-live-execution-policy).

Wireless deadlines are 1-60 seconds; complete replies default to 128 KiB and
cap at 1 MiB. Wireless inventories cap at 256 measurements, reports at 4,096
records, and inline transfer data at 1 MiB decoded. Native client/server limits
may be tighter. Machine job/file tools have their own schema limits; file
content is bounded separately from base64/JSON expansion.

Oversize complete results fail rather than silently truncate. Observations
report their bound/loss information and release only their own resources.
MCP cancellation must reach the server, for example through the standard
`notifications/cancelled` notification; abandoning a local client wait is not
necessarily a remote cancellation.

The [MCP package reference](../tools/Opc.Ua.Mcp.MachineTool/NugetREADME.md)
documents exact selectors, typed numeric unions, job JSON, scoped remote paths
and byte limits. Use the live schemas rather than inventing additional
wireless or CNC verbs.

## Conformance and support limits

### Supported surfaces

| Feature | Support |
| --- | --- |
| MachineTool 1.02.0 and bundled WMTP 1.0.0 | Released models; independent runtime selection |
| Monitoring, channels, working units, tools/life, prognoses and conditions | Explicit provider-backed observations |
| Retained Production, states/parts/statistics, Machinery/ISA-95 jobs and remote files | Implemented through native types and shared services |
| All eight WMTP measurement kinds and checked trigger/delta settings | Original type, quality and acquisition time retained |
| Battery/service metadata, calibration mode and UTC/time-zone requests | Selected observations/providers required |
| Independent work/service record operations | Complete-copy reads, explicit range/delete/abort |
| Firmware/licence bytes | Authorized provider acceptance only |
| Standalone/shared hosting, isolated instances, typed clients, MCP and NativeAOT | Same model/runtime APIs; deployment-specific configuration still required |
| Separate WMTP package/profile/guide; CNC, Bluetooth/pairing, flash/licensing or safety drivers | Not supplied |
| External OPC Foundation certification of a deployment | Not claimed |

### Runtime claims and deployment requirements

Model exposure and operational conformance are different. Contributors report
capabilities of committed live instances and selected providers; namespace
presence alone cannot establish a monitoring, job, file or record facet.
Security-dependent claims also require actual endpoint policies, certificates
and user-token support. Configuring a monitoring requirement does not make a
server quota or equipment update rate true.

Facet identities follow the released
[MachineTool profiles](https://reference.opcfoundation.org/specs/OPC-40501-1/14)
and [WMTP profiles](https://reference.opcfoundation.org/specs/OPC-40505/9).
See [Profiles](Profiles.md) for repository-wide scope and certification limits.
The executable evidence is maintained in the
[MachineTool tests](../tests/Opc.Ua.MachineTool.Tests/), including live-session,
publication, provider-refusal and lifetime regressions; support tables are not
a certification result for a particular device.

## Model provenance

Both models are pinned to OPC Foundation UA-Nodeset revision
[`96a08945425d0295edb3607d2a38f6841ef44046`](https://github.com/OPCFoundation/UA-Nodeset/tree/96a08945425d0295edb3607d2a38f6841ef44046).
Original filenames, including WMTP's `Nodeset2` spelling, are preserved:

| Local model | Purpose |
| --- | --- |
| [`Opc.Ua.MachineTool.NodeSet2.xml`](../src/Opc.Ua.MachineTool/Model/MachineTool/Opc.Ua.MachineTool.NodeSet2.xml) | Normative MachineTool wire identities |
| [`Opc.Ua.WMTP.Nodeset2.xml`](../src/Opc.Ua.MachineTool/Model/WMTP/Opc.Ua.WMTP.Nodeset2.xml) | Normative WMTP wire identities |
| `*.Derived.NodeIds.csv` beside each model | Locally derived generator drift guards, not replacement numeric identifiers |
| [`MachineToolModels`](../src/Opc.Ua.MachineTool/MachineToolModels.cs) | Pinned source commit, namespace closure and LF-normalized NodeSet SHA-256 values |

The generated state/proxy types consume dependency payloads from their existing
model assemblies. No second copy of DI, Machinery, ISA-95 or PA-DIM is generated
inside MachineTool. See [model dependencies](ModelDependencies.md) for the
cross-assembly mechanism and [Machinery provenance](Machinery.md#model-sources-identifier-tables-and-repairs)
for identifier-table conventions.

## Samples and related documentation

- [MachineTool/WMTP sample pair](../samples/MachineTool/README.md): machine-only,
  wireless-only and mixed modes; mandatory/advanced selections; bounded,
  encrypted loopback examples and explicit accepted/refused requests.
- [CompanionSuite](../samples/CompanionSuite/): all families on one DI/Machinery
  owner with isolated instances.
- [Machinery](Machinery.md), [Device Integration](DeviceIntegration.md),
  [ISA-95](ISA95.md), [GMS](GMS.md) and [Machine Vision](Vision.md).
- [Node manager lifetimes](NodeManagers.md), [state machines](StateMachines.md),
  [alarms and conditions](AlarmsAndConditions.md), [file services](FileSystemClient.md)
  and [model-change tracking](ModelChangeTracking.md).
- [MCP server](McpServer.md) and the
  [MachineTool MCP package](../tools/Opc.Ua.Mcp.MachineTool/NugetREADME.md).
- [Developer guide](DeveloperGuide.md) for framework-specific builds and tests.
