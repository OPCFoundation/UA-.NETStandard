# Geometric Measurement Systems

The `Opc.Ua.GMS*` libraries implement
[OPC 40210 Geometric Measurement Systems](https://reference.opcfoundation.org/specs/OPC-40210).
They combine the released GMS model with the existing
[MachineTool](MachineTool.md) runtime, [Machinery](Machinery.md) building blocks
and result services. A system remains a native `GMSType` instance, not a
base MachineTool object with an unrelated result tree.

The reference document is **1.0.1**; its released normative NodeSet is
**1.0.0**. GMS version 1 is a monitoring and result model. It does not define
new measurement-start, physical-calibration, GMS job-start or
correction-actuation commands.

## Contents

- [Library layout](#library-layout)
- [Models and dependencies](#models-and-dependencies)
  - [Reference edition and wire namespace](#reference-edition-and-wire-namespace)
  - [Reuse of MachineTool and Machinery](#reuse-of-machinetool-and-machinery)
- [Quick start - server](#quick-start---server)
  - [Build a mandatory-only system](#build-a-mandatory-only-system)
  - [Acquire and refresh observations](#acquire-and-refresh-observations)
- [Quick start - client](#quick-start---client)
  - [Discover systems and read results](#discover-systems-and-read-results)
  - [Factory and session ownership](#factory-and-session-ownership)
- [Server hosting and composition](#server-hosting-and-composition)
  - [Standalone and existing Machinery owners](#standalone-and-existing-machinery-owners)
  - [Custom owners and one native root](#custom-owners-and-one-native-root)
- [System builder and provider contracts](#system-builder-and-provider-contracts)
  - [Mandatory defaults and optional features](#mandatory-defaults-and-optional-features)
  - [Sensors, multisensors and qualification](#sensors-multisensors-and-qualification)
  - [Workspace, loading and accessories](#workspace-loading-and-accessories)
  - [Additional sensors and warnings](#additional-sensors-and-warnings)
  - [Calibration observations](#calibration-observations)
  - [Native production and inherited behavior](#native-production-and-inherited-behavior)
- [Results, characteristics and corrections](#results-characteristics-and-corrections)
  - [Per-instance stores and result binding](#per-instance-stores-and-result-binding)
  - [Publish an acquired result](#publish-an-acquired-result)
  - [Intermediate characteristics and tolerances](#intermediate-characteristics-and-tolerances)
  - [Calculated corrections are observations](#calculated-corrections-are-observations)
  - [Queries, metadata handles and files](#queries-metadata-handles-and-files)
- [Typed client workflows](#typed-client-workflows)
  - [System reads and optional absence](#system-reads-and-optional-absence)
  - [Result access and downloads](#result-access-and-downloads)
  - [Bounded observations and generated proxies](#bounded-observations-and-generated-proxies)
- [Lifetime, cancellation and bounds](#lifetime-cancellation-and-bounds)
  - [Coherent refresh and retained publication](#coherent-refresh-and-retained-publication)
  - [Resource ownership and cleanup](#resource-ownership-and-cleanup)
  - [Configuration limits](#configuration-limits)
- [MCP integration](#mcp-integration)
  - [Select or embed the GMS profile](#select-or-embed-the-gms-profile)
  - [Catalog and explicit acknowledgement](#catalog-and-explicit-acknowledgement)
  - [Host policy and bounded replies](#host-policy-and-bounded-replies)
- [Conformance and support limits](#conformance-and-support-limits)
  - [Supported surfaces](#supported-surfaces)
  - [The undefined Production statistics requirement](#the-undefined-production-statistics-requirement)
  - [Instance capabilities versus certification](#instance-capabilities-versus-certification)
- [Model provenance](#model-provenance)
- [Samples and related documentation](#samples-and-related-documentation)

## Library layout

The names in this table are assembly/project names. NuGet package IDs add
the `OPCFoundation.NetStandard.` prefix, for example
`OPCFoundation.NetStandard.Opc.Ua.GMS.Server`.

| Library | Responsibility |
| --- | --- |
| `Opc.Ua.GMS` | Released generated states, identifiers, loaders, typed proxies and shared observation/result contracts |
| `Opc.Ua.GMS.Server` | Native GMS builder, equipment/production binding, result publisher, bounded reference stores and hosting |
| `Opc.Ua.GMS.Client` | Discovery, bounded native reads, results/downloads and observations over an existing session |
| `Opc.Ua.Mcp.GMS` | Six bounded GMS MCP workflows over the host's named sessions |

Model/server/client target .NET Framework 4.8 and .NET 8, 9 and 10.
MCP targets .NET 8, 9 and 10. GMS packages, including `.Debug` variants, follow
the normal root version policy; they are not independently forced to preview.
The model is server/client-independent, and the typed client does not depend
on the server runtime.

## Models and dependencies

### Reference edition and wire namespace

| Item | Value |
| --- | --- |
| Reference document | OPC 40210, edition `1.0.1` |
| Released NodeSet model version | `1.0.0`, published 2023-07-01 |
| Wire namespace | `http://opcfoundation.org/UA/GMS/` |
| Generated CLR namespace | `Opc.Ua.GMS` |
| Shared contract namespace | `Opc.Ua.GMS.Contracts` |
| Model metadata | `GmsModel.ReferenceEdition`, `Version`, `NamespaceUri`, `SourceCommit` |

`GmsModel.GetNamespaceUris()` returns the dependency closure in order.
`AddGmsTypeSystem` loads/registers GMS and its existing dependencies rather
than generating duplicate copies of their types.
Generated proxies resolve namespace-qualified identities; clients must not
assume the GMS namespace has a fixed numeric index.

### Reuse of MachineTool and Machinery

GMS derives from MachineTool and consumes DI, IA, Machinery, MachineTool's
ISA-95/Machinery Jobs dependency closure, and Machinery Result.
Although the MachineTool assembly also contains WMTP, GMS does **not** select
WMTP, PA-DIM or ProcessValues merely by referencing that assembly.

The root, identification, tools, jobs/parts and result management keep their
GMS-derived types. Shared machinery supplies registration, model-change
tracking, state observations, conditions, metadata handles and temporary-file
transfer. The server reuses these services rather than implementing another
subscription, result-transfer or session-lifetime protocol.

The NodeSet's required-model versions are publication dependencies, not a
second set of generated assemblies to install beside the repository's current
compatible models. In particular, retained MachineTool Production remains
available for GMS despite its partial deprecation in newer MachineTool editions.

## Quick start - server

### Build a mandatory-only system

Every complete system needs an explicit equipment provider, result store and
nameplate. The following configuration helper receives those application-owned
services and gives the resulting runtime to an acquisition coordinator:

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Server.Hosting;

static IOpcUaServerBuilder AddInspectionServer(
    IServiceCollection services,
    IGmsProvider observations,
    IGmsResultStore results,
    Action<GmsHandle> retainRuntime)
{
    return services.AddOpcUa()
        .AddServer(options =>
        {
            options.ApplicationName = "GmsServer";
            options.ApplicationUri = "urn:example:gms-server";
            options.EndpointUrls.Add("opc.tcp://localhost:62559/GmsServer");
        })
        .AddGms()
        .ConfigureGms(async (context, ct) =>
        {
            GmsHandle system = await context.AddGms("InspectionCell")
                .WithProvider(observations)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Example manufacturer");
                    identity.SerialNumber = "CMM-001";
                    identity.ProductInstanceUri = "urn:example:inspection:CMM-001";
                })
                .WithResults(results)
                .BuildAsync(ct);
            retainRuntime(system);
        });
}
```

Invoke it during host construction, then await the normal application/server
startup and shutdown. Configure certificates, trust, endpoints and user
authentication through [server hosting](DependencyInjection.md#server-feature) and
[CertificateManager](CertificateManager.md).

For a simulation, `InMemoryGmsProvider(new GmsSystemSnapshot())` starts with
explicitly unavailable observations. `InMemoryGmsResultStore` takes the existing
`IServiceMessageContext`, such as `context.Context.AsMessageContext()` inside
configuration. It is a bounded reference store, not durable production storage.

Do not put the returned `GmsHandle` in an `await using` scoped to the
configuration callback: that would shut it down as startup completes.
`retainRuntime` should only retain it for application-managed refresh.

### Acquire and refresh observations

`IGmsProvider.ReadAsync` returns an immutable, coherent `GmsSystemSnapshot`.
Its `MachineTool` member carries the inherited revision and observations;
native sensor/calibration/job/part observations extend that same acquisition.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;

static async ValueTask PublishGmsObservationAsync(
    InMemoryGmsProvider provider,
    GmsHandle system,
    GmsSystemSnapshot acquisition,
    CancellationToken ct)
{
    await provider.SetSnapshotAsync(acquisition, ct);
    await system.RefreshAsync(ct);
}
```

The in-memory adapter assigns a strictly increasing inherited revision.
A real provider must do so itself and keep returned graphs immutable.
Updating the adapter does not itself refresh OPC UA nodes; the runtime's
refresh performs validation and coherent publication. Neither call starts a
physical measurement.

## Quick start - client

### Discover systems and read results

The facade borrows an existing connected session and discovers actual GMS
subtypes, not arbitrary objects in a namespace:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;

static async ValueTask ReadInspectionResultsAsync(
    ISession session, ITelemetryContext telemetry, CancellationToken ct)
{
    await using GmsClient client = session.CreateGmsClient(telemetry);
    ArrayOf<GmsDeviceEntry> systems = await client.DiscoverAsync(ct);
    for (int index = 0; index < systems.Count; index++)
    {
        GmsDeviceEntry system = systems[index];
        GmsSystemReadSnapshot snapshot = await client.ReadSnapshotAsync(system.NodeId, ct);
        GmsResultClient results = client.GetResults(system.NodeId);
        ArrayOf<string> ids = await results.QueryAsync(16, cancellationToken: ct);
        Console.WriteLine($"{system.NodeId}: {ids.Count} result identifiers");
        if (ids.Count != 0)
        {
            ResultDataType result = await results.GetByIdAsync(ids[0], ct);
            Console.WriteLine(result.ResultMetaData!.ResultId);
        }
    }
}
```

`QueryAsync` returns up to the requested result count; it is not a statement
that the entire store contains no more results. Every acquired metadata handle
is released by the typed operation. Use `ReadPublishedResultsAsync` when raw
result-variable `DataValue` quality and acquisition timestamps are needed,
rather than treating the returned `ResultDataType` envelope as a DataValue.

### Factory and session ownership

`AddGmsClient()` extends an existing `AddOpcUa().AddClient(...)` registration
and registers `IGmsClientFactory`. `CreateAsync` uses the configured
container-cached `ManagedSession` and **borrows** it.

Direct constructors, `session.CreateGmsClient(...)`, `GmsClientFactory` and
`GmsResultClientFactory` do not transfer session ownership. Disposing one GMS
facade drains only its own work and leaves other consumers connected.
Cancellation after session acquisition or a failed facade constructor also
leaves the shared session with its owner.

The DI container closes its cached session at shutdown. An application that
creates an independent session must close/dispose it separately. See
[client hosting](DependencyInjection.md#client-feature) and the
[MachineTool lifetime rules](MachineTool.md#client-and-provider-ownership).

## Server hosting and composition

### Standalone and existing Machinery owners

`AddGms` registers one Machinery owner with
`MachineryParts.BuildingBlocks | MachineryParts.Result`.
`GmsServerOptions.InstanceNamespaceUri` chooses the standalone application
namespace. Optional system features are still off until the per-instance
builder selects them.

When the owner already exists, use `AddGmsToMachinery`, not a second `AddGms`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;

static IOpcUaServerBuilder AddGmsModelsToCell(IOpcUaServerBuilder server)
{
    return server.AddMachinery(options =>
    {
        options.Parts = MachineryParts.BuildingBlocks | MachineryParts.Result;
        options.InstanceNamespaceUri = "urn:example:cell:instances";
    }).AddGmsToMachinery();
}
```

The example establishes the owner first; omit `AddMachinery` if the application
already registered it. The existing owner must include both required parts.
`ConfigureGmsFor<MachineryNodeManager>(...)` uses that owner's context.
Multiple systems require independent per-instance providers and stores.

MachineTool, WMTP, cranes and Machine Vision can share the owner when all their
required model/part selections are present. See the
[CompanionSuite sample](../samples/CompanionSuite/) for the combined setup.

### Custom owners and one native root

`ConfigureGmsFor<TNodeManager>` targets a compatible existing `DiNodeManager`
subtype with the shared Machinery construction services.
`GmsModelProvider` plugs into its existing model-provider pipeline.
The model loader does not create another `DeviceSet` or MachineTool owner.

The native builder uses `AddMachineTool<GMSState>` with GMS-owned factories.
`WithMachinery(...)` adds bindings to the same unpublished typed root; that
callback must not call `BuildAsync` itself. Registration, rollback and
root-bound resources follow the existing owner's transaction.

For advanced result-only binding on an already staged GMS root,
`WithGmsResults(store, out binding, options)` adopts its declared
`GMSResultManagementState`. `binding.CreatePublisher(machineHandle)` is tied
to that exact registered root. It does not create another base AddIn and cannot
be attached to another machine. The staged root still needs its non-result
GMS requirements; this binding alone is not a full GMS system.

## System builder and provider contracts

### Mandatory defaults and optional features

`GmsFeatures.None` retains mandatory identification, equipment, monitoring,
production and result-management structure. It does not silently install
optional sensors, files or command adapters.

`GmsBuilder.WithOptions(GmsSystemOptions)` controls native optional surfaces:

| Feature | Required data or relationship |
| --- | --- |
| `Identification` | Workspace and device-subclass observations |
| `Sensors` | Native sensor/multisensor inventory, including qualification countdowns |
| `DynamicSensors` | Requires `Sensors`; permits inventory changes through shared lifetime handling |
| `Loading` | Part-loading state and optional loading position |
| `ToolMonitoring` | Requires `Sensors`; references actual active sensor keys |
| `Accessories` | Rotary tables, sensor exchange racks and tip exchange racks |
| `AdditionalSensors` | Environmental/additional analog measurements with class and units |
| `Calibration` | Calibration state, dates, interval/certificate and forecast observations |
| `Production` | GMS jobs/parts on retained MachineTool Production |
| `DynamicProduction` | Requires `Production`; permits changing production plans |
| `SensorWarnings` | Requires `AdditionalSensors`, not primary measurement sensors |

Selected data must be complete and type-correct before publication.
Invalid feature combinations fail with `BadConfigurationError`.
Enabling `All` does not supply the missing equipment observations.

`WithMachineToolFeatures` selects inherited monitoring independently.
`WithProduction`, `WithPrognoses` and `WithNotifications` reuse the corresponding
MachineTool options, while the GMS builder retains its own native job, part and
sensor factories.

### Sensors, multisensors and qualification

`GmsSystemSnapshot.Sensors` describes native single sensors and nested
multisensors. `GmsSensor.Tool` supplies the inherited tool identity/life/location
data; native fields retain their declared types, including class, capabilities,
alignment, axes, probe information, measuring/working range and resolution.

`GmsQualifiedLife.RemainingHours` is a **Double countdown in hours** under the
native Qualified life entry. The binding preserves `Qualified`/`Time` and
`IsCountingUp=false`. Negative remaining hours after expiry are meaningful
and are not clamped to zero. Acquisition quality, geometric qualification
status and countdown expiry are different facts.

Dynamic sensor changes use stable keys and the existing MachineTool tool
module. `ToolMonitoring.ActiveSensorKeys` resolves the accepted sensor
inventory, not arbitrary identifiers from another instance. The runtime
rejects incompatible references before committing the acquisition.

### Workspace, loading and accessories

Workspace and device-subclass values belong to `GmsIdentificationObservation`.
Part loading is a `GmsLoadingObservation`; active-tool monitoring is a
`GmsToolMonitoringObservation`. Accessory kinds use the real released rotary
table and exchange-rack types, with their own identification where required.

These are observations, not automatic part loading, tool changing or
calibration procedures. Static optional-node shapes are configured before
publication. A provider cannot add a new optional property to a fixed published
slot merely by changing an untyped payload.

### Additional sensors and warnings

`GmsAdditionalSensor` preserves its identifier, class, engineering units and
actual `DataValue`. These sensors measure environmental or auxiliary conditions
not directly involved in the geometrical measurement itself.

`GmsSensorWarning` binds a retained `SensorWarningAlarmType` condition to an
additional sensor using `SensorKey`, or an explicit compatible `InputNodeId`.
Its limits, active state, severity, message and acquisition metadata remain
provider observations. The warning's input is **not** a primary probe's
qualification countdown.

Use the separate inherited MachineTool notification provider for unrelated
operator alerts. Standard condition methods remain subject to their allowed
operations and the server's authorization. Reading or observing a warning does
not acknowledge or confirm it.

### Calibration observations

`GmsCalibrationObservation` includes the acquired calibrated state, date of
calibration, forecast time and optional interval, preparation time and certificate
reference. `GmsHandle.CalibrationHistory` is a bounded application-side history
of accepted observations, not a new historical-access or calibration-execution
service.

The client preserves these values and returns null when the optional calibration
surface is absent. It does not fetch a certificate URI automatically, derive a
calibration result from elapsed host time or command the measuring equipment.

### Native production and inherited behavior

GMS jobs and parts extend the retained MachineTool Production model.
The provider supplies matching keys across inherited job/program/part-set/part
observations and native `GmsJobObservation`/`GmsPartObservation`.
Native fields include batch identity, duration/remaining time, measurement
reason, part descriptions and catalog-backed production metadata.

Catalog observations preserve their declared Number scalar/array representation,
enumeration metadata and `ValueAsText`; they are not normalized through an
untyped string or floating-point representation. Rejected acquisition does
not commit new job/part indexes.

Inherited production statistics remain implemented. This is distinct from
claiming the complete GMS Production facet, whose undefined requirement is
documented [below](#the-undefined-production-statistics-requirement).
No GMS-specific job execution verbs are introduced.

## Results, characteristics and corrections

### Per-instance stores and result binding

`IGmsResultStore` belongs to the model contract assembly and is injectable.
It supplies newest/by-ID reads, a bounded newest-first ID list, atomic writes
and per-ID acknowledgement results. It must honor cancellation and return
immutable snapshots. The same store object cannot back two active GMS bindings.

`InMemoryGmsResultStore` rejects count/byte overflow instead of silently evicting
results. Applications choose retention explicitly with `RemoveAsync`.
Durability, distributed concurrency and equipment/database transactions belong
to an application implementation of `IGmsResultStore`.

`GmsResultBindingOptions` controls the result subtree separately from system
features:

| Option | Meaning |
| --- | --- |
| `PublishedResults` | Fixed ring of native result-variable slots; default 4, zero selects method-only access |
| `IncludeUsedTools` | Exposes `GMSResultType.UsedTools` |
| `EnableIntermediateEvents` | Allows concrete intermediate-result events |
| `EnableFileTransfer` | Selects the shared temporary-file download path |
| `Corrections` | Null omits the optional folder; an empty array exposes an explicitly empty folder |
| `Limits` | Result, metadata, file, stored-byte, characteristic and correction budgets |

**Default distinction:** `GmsBuilder.WithResults(store)` disables file transfer
for the mandatory-only system. A separately constructed
`new GmsResultBindingOptions()` defaults `EnableFileTransfer` to true.
Set that property explicitly when supplying an options object.

### Publish an acquired result

`GmsResult` wraps an acquired `DataValue` carrying the unchanged Machinery
`ResultDataType`. The envelope must contain `ResultMetaData` with a nonempty
`ResultId`; optional structured fields need their encoding-mask bits.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Result;

static ValueTask PublishInspectionAsync(
    GmsHandle system,
    ResultDataType equipmentResult,
    StatusCode acquisitionQuality,
    DateTimeUtc acquiredAt,
    CancellationToken ct)
{
    var sample = new DataValue(
        Variant.FromStructure(equipmentResult), acquisitionQuality, acquiredAt, acquiredAt);
    return system.Results.PublishAsync(new GmsResult(sample), ct);
}
```

The provider supplies the real envelope, quality and acquisition time; the
server does not invent a successful measurement. The result boundary copies
structured metadata, samples and buffers. Stable result-variable slots retain
their projection and acquisition metadata when the ring advances, even if the
backing store later removes an older entry.

Final results emit a concrete subtype of Machinery's abstract
`ResultReadyEventType`. The concrete type is registered in the application
namespace while retaining the standard base relationship.

### Intermediate characteristics and tolerances

An intermediate result requires at least one `GmsCharacteristic`. Its geometric
validity and tolerance evaluation are independent of OPC UA quality:

```csharp
using Opc.Ua;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;

static GmsResult CreateIntermediateInspection(
    in DataValue resultEnvelope,
    GmsAnalogValue measurement,
    GmsTolerance lower,
    GmsTolerance upper,
    ResultEvaluationEnum evaluation,
    bool geometricallyValid)
{
    return new GmsResult(resultEnvelope,
        characteristics:
        [
            new GmsCharacteristic("Bore")
            {
                Identifier = "BoreDiameter",
                ResultValue = measurement,
                LowerTolerance = lower,
                UpperTolerance = upper,
                Evaluation = evaluation,
                IsValid = geometricallyValid
            }
        ],
        programName: "Inspection",
        isIntermediate: true);
}
```

`resultEnvelope` must contain the same required `ResultDataType` metadata as a
final result. `GmsResult` marks intermediate metadata as `IsPartial`.
Enable intermediate events in the binding before publishing this result.

`GmsAnalogValue` carries a Double sample and units; a bad/null value represents
unavailable acquisition. Every configured `GmsTolerance` carries its mandatory
`ToleranceLimitEnum` form. Nominal values, importance class and formula are
separate optional fields. Neither tolerance evaluation nor `IsValid` is inferred
from a Good/Bad UA status.

Intermediate events derive from `IntermediateResultEventType`, not the
final-result event type. Generated decoding preserves the explicitly selected
characteristic placeholders and their qualified names.

### Calculated corrections are observations

Each `GmsCorrection` requires an identifier, a characteristic identifier and at
least one relative or absolute `GmsAnalogValue`. Configure the correction
objects through `GmsResultBindingOptions.Corrections`, then update their
observations using `system.Results.PublishCorrectionAsync(correction, ct)`.

The configured identity, characteristic reference and optional-node shape
remain fixed. Changing them requires rebuilding the instance, not silently
retargeting a correction node.
Publishing or reading a correction never applies it to a machine.

### Queries, metadata handles and files

GMS binds the real `GMSResultManagementState` through the existing
Machinery Result implementation:

- `GetLatestResult`, `GetResultById` and `GetResultIdListFiltered` return
  session-bound metadata handles that pin their result.
- `ReleaseResultHandle` releases that caller's handle. Session termination
  reclaims its handles without invalidating another session's results.
- Queries use the standard `ContentFilter` and ordered `RelativePath` list,
  with the Machinery evaluator's NULL and ordering rules.
- Result files use `GenerateFileForRead` and the existing
  `TemporaryFileTransferClient`/`UaFileStream` workflow, not a private protocol.
- Result acknowledgement returns domain statuses; it does not delete results
  or start a measurement.

The shared result-transfer path is read-only. Its required write-side methods
return the standard refusal statuses instead of pretending to accept uploads.
Metadata/file cleanup, cancellation and retained-root deletion use the shared
[Machinery result lifetime](Machinery.md#downloads-and-the-refused-write-side).

## Typed client workflows

### System reads and optional absence

| `GmsClient` API | Workflow |
| --- | --- |
| `DiscoverAsync` | Native GMSType and vendor-subtype discovery |
| `ReadSnapshotAsync` | Identification and inherited/native system values |
| `ReadEquipmentAsync` | Sensors/multisensors, qualification, loading, tool monitoring and accessories/additional sensors |
| `ReadCalibrationAsync` | Optional calibration observation; absent surface is null |
| `ReadProductionAsync` | Native job/part metadata and retained production |
| `GetProxy` | Generated `GMSTypeClient` |
| `GetResults` | Bounded `GmsResultClient` on the same session |

Reads preserve NodeIds, optional presence, numeric types, quality and acquisition
timestamps. A null `ArrayOf<T>` is checked with `IsNull`, not a CLR nullable
wrapper. An absent optional DataValue is distinct from a present bad-quality
sample. Missing mandatory structure and service/access errors are not converted
to successful empty snapshots.

Separate client service calls are not an atomic equipment-history transaction.
Use the returned acquisition metadata and application correlation when a
cross-read snapshot is required.

### Result access and downloads

| `GmsResultClient` API | Ownership or outcome |
| --- | --- |
| `GetLatestAsync`, `GetByIdAsync` | Detached result envelope; call-owned metadata handle released |
| `QueryAsync` | Bounded standard filter/order query; query handle released |
| `ReadPublishedResultsAsync` | Original result-slot DataValues and optional UsedTools |
| `ReadCorrectionsAsync` | Native correction values/units, not commands |
| `DownloadAsync` | Bounded ByteString or streamed download into a caller-owned destination |
| `AcknowledgeAsync` | One unchanged domain result per identifier; no implicit retry |

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Client;

static ValueTask DownloadInspectionAsync(
    GmsResultClient results,
    string resultId,
    Stream destination,
    CancellationToken ct)
{
    return results.DownloadAsync(resultId, destination, ct);
}
```

The destination stays open; after failure it may contain a partial prefix.
The client releases its remote file and metadata resources on success, failure
and cancellation. The ByteString overload shares that implementation.
`GmsResultDomainException` represents a nonzero result-method domain error,
separately from `ServiceResultException` and cancellation. When both an operation
and its cleanup fail, both errors remain visible.

### Bounded observations and generated proxies

`ObserveDataAsync`, `ObserveEventsAsync` and `ObserveConditionsAsync` reuse the
MachineTool collector and `MachineToolObservationOptions`. Windows include
setup, are at most 60 seconds, and retain at most 100 notifications within a
finite decoded-byte budget. Condition refresh is explicit; observation does
not acknowledge/confirm warnings or execute jobs.

`GmsResultClient.ObserveIntermediateResultsAsync` composes an existing
`IStreamingSubscription`. Select the real, instance-qualified characteristic
placeholders through `CreateIntermediateResultFilter`; do not assume their
namespace is the GMS model namespace. The readiness-callback overload signals
only after actual monitored-item creation succeeds.
The enumerator removes its own monitored item, while the caller retains the
streaming subscription.

`GetProxy` and `GmsResultClient.Root` expose generated proxies for advanced
navigation. Reuse them rather than manually composing unqualified browse paths
or low-level Call arrays. Event fields retain their actual values but do not
gain invented per-field acquisition timestamps; use result variables for raw
sample quality/time.

## Lifetime, cancellation and bounds

### Coherent refresh and retained publication

The GMS binding prepares native projections within MachineTool's
acquire/validate/commit/publish transaction. Rejected observations do not replace
accepted sensor, job or part indexes. `Observation` and `CalibrationHistory`
reflect accepted state.

If notification publication fails after commit,
`RetryPublicationAsync` retries retained publication without reacquiring or
repeating equipment effects. It is not an automatic retry of a remote
acknowledgement or a physical operation.

Dynamic sensor/production removal observes the
[final-value publication barrier](NodeManagers.md#final-value-publication-before-node-removal):
the exact required terminal entry must enter a real Publish response before its
subtree can be removed. Queuing, equality with an older sample, network delivery
and client acknowledgement are different boundaries.

### Resource ownership and cleanup

| Resource | Owner |
| --- | --- |
| `IGmsProvider`, `IGmsResultStore`, inherited notification/provider services | Application; use per-instance selections |
| GMS root and result binding | One MachineTool/Machinery registration |
| Runtime-created observation and result-transfer resources | The corresponding runtime or call |
| Session used by any GMS facade/factory | Application or DI container |
| Stream passed as a download destination | Caller |
| Streaming subscription passed to intermediate observation | Caller; enumerator owns only its item |

`GmsHandle.DisposeAsync` drains the native binding and shared runtime.
Deleting the bound root or stopping its manager also releases the registration's
resources. Providers must honor cancellation and avoid reentering their own
refresh/disposal path.

`GmsClient.DisposeAsync` cancels and joins its admitted operations and inherited
observations without closing a borrowed session. Cancellation is not proof that
a submitted acknowledgement was undone; inspect the result and never retry an
uncertain mutation implicitly.

### Configuration limits

| Options | Representative defaults |
| --- | --- |
| `GmsSystemOptions` | 128 sensors, 128 accessory/additional-sensor slots, 32 calibration-history entries, 128 warning identities, 1,024 array entries |
| `GmsClientOptions` | 128 native elements, 16,384 examined references, 1 MiB raw sample payload |
| `GmsResultLimits` | 64 results, 256 KiB metadata, 16 MiB per file, 64 MiB total stored bytes, 64 characteristics/used tools/corrections |

Bounds are validated before service work or instance publication.
Provider-side, client-side and transport quotas are independent; the most
restrictive applicable bound wins. Oversize complete payloads fail rather than
silently omit measurements. A result query's explicit maximum is a query
selection limit, not unlimited export.

## MCP integration

### Select or embed the GMS profile

```powershell
opcua-mcp --profile gms
```

For embedding, select only the GMS catalog and shared connection tools:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

static IMcpServerBuilder AddGmsMcp(IServiceCollection services)
{
    services.AddOpcUaMcpCore().AddOpcUaMcpGms();
    return services.AddMcpServer()
        .WithStdioServerTransport()
        .WithOpcUaMcpFilters()
        .WithOpcUaGmsTools(McpToolProfile.Gms);
}
```

The profile-set overload composes catalogs and deduplicates common connection
tools. It borrows host-managed named sessions. Omit `sessionName` only when
exactly one session is connected.

### Catalog and explicit acknowledgement

| Tool | Workflow |
| --- | --- |
| `gms_list_systems` | Actual GMS/vendor-subtype inventory |
| `gms_read_system` | `System`, `Equipment`, `Calibration`, `Production` or `Capabilities` |
| `gms_read_results` | `Latest`, `ById`, bounded `Query`, raw `Published` slots or `Corrections` |
| `gms_download_result` | Bounded inline result bytes; no host-local path |
| `gms_observe` | `Data`, `Events`, explicit retained `Conditions`, or `IntermediateResults` |
| `gms_acknowledge_results` | Standard result acknowledgement with `confirmAcknowledgement=true` |

Passive workflows do not acknowledge results/conditions, fetch calibration
certificate URLs, execute measurements/jobs or apply corrections.
Acknowledgement is the separate standard state change, with explicit consent
and authoritative server permission; per-result domain failures remain visible.

For `gms_read_results`, native query options belong inside the `query` object:

```json
{
  "systemNodeId": "ns=12;s=InspectionCell",
  "operation": "Query",
  "query": {"filterField": "JobId", "filterValue": "J-1", "orderBy": "ResultId"},
  "maximumResults": 20,
  "sessionName": "metrology"
}
```

Use the NodeId actually returned by discovery; the namespace index above is
illustrative. This remains a standard Machinery filter, not a new GMS query
protocol.

### Host policy and bounded replies

Catalog selection is not an authorization grant. Privileged/live host
capabilities are independently **default-off** and require explicit caller
grants/network policy when enabled. A confirmation argument does not create
OPC UA authority. See [MCP execution policy](McpServer.md#privileged-and-live-execution-policy).

The GMS tools cap inventory/results/notifications at 100, observation windows
at 60 seconds, downloads at 524,288 bytes and complete UTF-8 replies at 1 MiB
(default 128 KiB). Native read limits may be tighter.
The MCP query workflow requests one extra identifier to detect a result set
that exceeds the requested complete-reply bound.

Unknown/duplicate query properties and incorrectly typed selector values fail
before session use. Incomplete or lossy retained-condition refresh is an MCP
error, not an apparently complete empty set.
Calls release their own subscription, metadata and file resources; cancellation
must reach the server through the transport, such as the standard MCP
`notifications/cancelled` notification. No persistent handle or implicit retry
is introduced.

See the [GMS MCP reference](../tools/Opc.Ua.Mcp.GMS/NugetREADME.md)
for exact schemas, characteristic selection, bounds and cancellation behavior.

## Conformance and support limits

### Supported surfaces

| Feature | Support |
| --- | --- |
| OPC 40210 reference 1.0.1 / released model 1.0.0 | Implemented |
| Sensors/multisensors, qualification, workspace, loading, tools and accessories | Explicit provider-backed observations |
| Calibration and additional environmental sensors | Monitoring; no physical calibration |
| Native GMS jobs/parts and inherited production statistics | Implemented |
| Results, characteristics/tolerances, corrections, queries, downloads and events | Native types with shared result services; corrections do not actuate |
| `SensorWarningAlarmType` | Additional environmental-sensor conditions |
| Standalone/shared hosting, typed clients, bounded MCP and NativeAOT | Supported through the same runtime APIs |
| Complete GMS Production facet | Withheld because of the undefined requirement below |
| Invented measurement-start, calibration-execution or GMS job/correction-control verbs | Not supplied |
| External OPC Foundation certification of a deployment | Not claimed |

### The undefined Production statistics requirement

[OPC 40210 section 12.3.2.7](https://reference.opcfoundation.org/specs/OPC-40210/12.3.2.7)
requires `GMS_ProductionStatisticsType` for the Production conformance unit.
The released specification does not define that type or its NodeId, and
[Annex A.1's normative NodeSet](https://reference.opcfoundation.org/specs/OPC-40210/annex-a)
contains no such definition.

The implementation does not fabricate a type, reuse a different type under that
name or silently claim the complete Production facet. Defined native GMS
jobs/parts and inherited MachineTool statistics remain supported. This is a
specific upstream conformance exception, not a statement that all production
monitoring is absent.

### Instance capabilities versus certification

`GmsHandle.Capabilities` describes current native conformance units,
dependency-qualified server profiles and the precise upstream exception.
Contributions follow successfully committed live instances and selected
features, not assembly references or namespace presence.

The profile definitions are in
[OPC 40210 section 12](https://reference.opcfoundation.org/specs/OPC-40210/12).
Endpoint security, certificates, user roles, quotas and actual equipment
behavior remain deployment responsibilities.
[Profiles](Profiles.md) records repository-wide scope; it is not an external
certification claim.

The [GMS tests](../tests/Opc.Ua.GMS.Tests/) cover native model/proxy identity,
equipment and production, result filtering/transfers, quality preservation,
warning sources, rejected acquisitions and real-session ownership/cleanup.
The test fixtures and runtime capability checks are distinct from certification
of a vendor device.

## Model provenance

The normative model is pinned to UA-Nodeset revision
[`96a08945425d0295edb3607d2a38f6841ef44046`](https://github.com/OPCFoundation/UA-Nodeset/tree/96a08945425d0295edb3607d2a38f6841ef44046).

| Source | Role |
| --- | --- |
| [`Model/opc.ua.gms.nodeset2.xml`](../src/Opc.Ua.GMS/Model/opc.ua.gms.nodeset2.xml) | Original lowercase publication filename and normative wire identities |
| [`Opc.Ua.GMS.Derived.NodeIds.csv`](../src/Opc.Ua.GMS/Model/Opc.Ua.GMS.Derived.NodeIds.csv) | Locally derived symbolic drift guard for the generator |
| [`GmsModel`](../src/Opc.Ua.GMS/GmsModel.cs) | Reference/model versions, namespace closure, source commit and LF-normalized NodeSet SHA-256 |
| [`Opc.Ua.GMS.csproj`](../src/Opc.Ua.GMS/Opc.Ua.GMS.csproj) | Explicit generator namespace, model URI and existing dependency assemblies |

The NodeSet supplies numeric identifiers; a derived identifier table is not a
replacement specification. Shared MachineTool, Machinery and DI definitions
are consumed through their existing assembly payloads. See
[ModelDependencies](ModelDependencies.md) and
[Machinery's identifier-table explanation](Machinery.md#what-the-identifier-table-is-and-is-not).

## Samples and related documentation

- [GMS sample pair](../samples/GMS/README.md): mandatory/advanced simulations,
  independent providers/stores, encrypted loopback sessions, bounded
  observations, result download/acknowledgement and cancellation cleanup.
- [CompanionSuite](../samples/CompanionSuite/): GMS alongside MachineTool, WMTP,
  cranes and Machine Vision on one owner.
- [MachineTool and wireless peripherals](MachineTool.md),
  [Machinery](Machinery.md), [Device Integration](DeviceIntegration.md)
  and [Machine Vision](Vision.md).
- [Final-value publication](NodeManagers.md#final-value-publication-before-node-removal),
  [model-change tracking](ModelChangeTracking.md),
  [alarms and conditions](AlarmsAndConditions.md), [file services](FileSystemClient.md)
  and [subscriptions](Subscriptions.md).
- [MCP server](McpServer.md) and [GMS MCP package](../tools/Opc.Ua.Mcp.GMS/NugetREADME.md).
- [Developer guide](DeveloperGuide.md) for builds, framework selection and tests.
