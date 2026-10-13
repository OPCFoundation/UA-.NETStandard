# Geometric Measurement Systems

The `Opc.Ua.GMS*` packages implement released
[OPC 40210](https://reference.opcfoundation.org/specs/OPC-40210/full).
The reference document is edition 1.0.1; its normative released NodeSet and
namespace remain model version 1.0.0.

A GMS is a generated MachineTool subtype. Its runtime reuses MachineTool
monitoring/production/equipment and Machinery results on one namespace owner,
preserving native GMS nodes. It does not invent measurement-start, physical
calibration, GMS job-start or correction-execution methods.

## Packages and hosting

NuGet IDs have the prefix `OPCFoundation.NetStandard.`.

| Package | Purpose |
| --- | --- |
| `Opc.Ua.GMS` | Released model, contracts, generated factories and proxies |
| `Opc.Ua.GMS.Server` | Native systems, provider observations, result bindings and hosting |
| `Opc.Ua.GMS.Client` | Discovery, typed reads, bounded observation and result workflows |
| `Opc.Ua.Mcp.GMS` | Bounded GMS tools on existing named OPC UA sessions |

The family follows normal root versions, not forced preview classification.
Its namespace is `http://opcfoundation.org/UA/GMS/`. Dependencies include
MachineTool, Machinery Result, Machinery, DI and IA with their canonical
closure. Hosting GMS does not activate WMTP merely because the MachineTool
assembly contains it.

Use `AddGms`/`ConfigureGms` for standalone hosting, or `AddGmsToMachinery` and
`ConfigureGmsFor<TNodeManager>` with an existing compatible owner. The owner
must support the required Machinery building blocks and results.

Every system needs explicitly selected `IGmsProvider`, identification and
`IGmsResultStore`. Inside an awaited configuration callback:

```csharp
using Opc.Ua;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;

var provider = new InMemoryGmsProvider(new GmsSystemSnapshot());
var store = new InMemoryGmsResultStore(context.Context.AsMessageContext());

GmsHandle system = await context.AddGms("InspectionCell")
    .WithProvider(provider)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example manufacturer");
        identity.SerialNumber = "CMM-001";
        identity.ProductInstanceUri = "urn:example:inspection:CMM-001";
    })
    .WithResults(store)
    .BuildAsync(cancellationToken);
```

Unavailable initial observations are not successful zero measurements.
Retain the handle rather than disposing it when configuration returns.
Applications own supplied providers/stores and release them after runtimes.
`SetSnapshotAsync` replaces the complete reference observation and advances
its revision; `RefreshAsync` validates/publishes through the shared transaction.

One owner can host independent GMS, MachineTool, WMTP, CranesHoists and
MachineVision instances. Providers, stores, handles and callbacks remain
per-instance. Duplicate standalone owners are not a composition mechanism.

## Optional features

`GmsSystemOptions.Features` selects native surfaces. Defaults create mandatory
Equipment, Identification, Monitoring, Notification, retained ActiveProgram
and actual GMS ResultManagement.

| Feature | Observations and behavior |
| --- | --- |
| Sensors | Native sensor/multi-sensor tools, identity/control and qualification |
| Identification | Cartesian/cylindrical workspace and device subclass |
| Loading | Actual loading state |
| ToolMonitoring | Same-instance active-tool references |
| Accessories | Rotary tables, sensor racks and tip racks |
| AdditionalSensors | Non-geometrical measurements such as ambient temperature |
| Calibration | Calibration/date/forecast/interval/preparation/certificate references |
| CalibrationHistory | Bounded application-side history, not an invented UA history service |
| Production | Native GMS jobs/parts through retained MachineTool behavior |
| SensorWarnings | Alarms for additional sensors outside allowed ranges |

Empty collections differ from absent required data. A modeled but unavailable
sample is a present `DataValue` with bad status. `WithMachineToolFeatures`
selects inherited monitoring; `WithProduction` retains GMS factories while
setting shared bounds; `WithPrognoses` includes native calibration.
`WithNotifications` supplies a distinct MachineTool alert/message provider.
`WithMachinery` configures the same unpublished root, not another registration.

## Sensors, equipment and calibration

Primary sensors directly perform geometrical measurements. Native
SensorType/MultiSensorType objects retain shared tool-list/life behavior.
Their required `Qualified` entry is GMS-qualified, Time-indicated, Double
hours remaining, and `IsCountingUp=false`. Acquisition quality/time are kept.
The runtime does not manufacture a countdown or clamp expired negative values.

Accessories use their declared Machinery item-identification AddIn.
Workspaces retain released Cartesian/cylindrical structures without invented
unit fields. Additional sensors use their declared analog variables.

[OPC 40210 section 9.1](https://reference.opcfoundation.org/specs/OPC-40210/9.1)
reserves `SensorWarningAlarmType` for sensors **not directly involved in
geometrical measurement**. Select `AdditionalSensors | SensorWarnings`.
`GmsSensorWarning.SensorKey` identifies a configured additional sensor;
SourceNode and InputNode identify its actual measured variable. An explicit
InputNode must identify that variable, not foreign inputs or metadata.

Primary probe qualification expiry is not this environmental warning.
Appropriate separate inherited notifications can use `WithNotifications` and
MachineTool `AlertType`. Acknowledge changes condition state, not its
underlying physical cause.

Native `CalibrationPrognosisType` exposes calibrated state/date, prediction,
interval/preparation and optional certificate references. Certificate values
are observed references, not commands to download certificates or calibrate
equipment. Qualification and system calibration remain distinct.

## Production and coherent publication

GMS jobs/parts are native subtypes of retained MachineTool declarations, even
where MachineTool 1.02 deprecates those declarations. Batch, measurement reason,
duration, part properties and Number-valued catalogs remain available.

All inherited/native validation and conversion completes before committing
matching observations and indexes. A rejected B acquisition cannot contaminate
metadata when a previously accepted A object is replayed. Unknown, foreign or
removed references fail; optional slot shape remains stable.

Dynamic changes reuse NodeVersion, model-change notification and final-value
publication. After accepted data, a publication failure is retried through
`RetryPublicationAsync` with stable identities, not by replaying equipment
effects. Removal, startup failure and shutdown drain owned bindings without
disposing siblings' providers.

## Results, characteristics and corrections

`GmsHandle.Results` publishes through the actual `GMSResultManagementState`.
It reuses Machinery store/filter/query, metadata handles and temporary files,
with real GMS variables and events.

`IGmsResultStore` is per instance. `InMemoryGmsResultStore` refuses overflow
rather than silently evicting retained data. Durable retention belongs in an
application provider. `GmsResultBindingOptions` explicitly enables ring size,
used tools, files, intermediate events and corrections; files are not enabled
by the mandatory binding alone.

| Element | Semantics |
| --- | --- |
| Result envelope | Unchanged Machinery ResultDataType in acquired DataValue |
| Published variables | Native GMSResultType with quality/time across ring rotation |
| Used tools | The actual optional UsedTools property |
| Characteristics | Independent geometric validity, tolerance evaluation, units and limits |
| Intermediate event | Concrete IntermediateResultEventType subtype with selected characteristics |
| Final event | Distinct concrete Machinery ResultReadyEventType subtype |
| Corrections | Reported relative/absolute deviations, not machine commands |

Values are copied across store boundaries. UA quality, geometric validity and
tolerance evaluation are independent. Absent corrections omit the folder;
an explicitly empty selection can create an empty folder.
`PublishCorrectionAsync` updates declared monitoring values; changing a
correction's identity/reference/shape requires a new slot.

`WithGmsResults` can bind a staged `IMachineBuilder<GMSState>` for advanced
composition without a second root/result manager. A results-only binding is
not by itself a full GMS system or conformance claim.

## Typed clients

```csharp
using Opc.Ua.GMS.Client;

await using GmsClient gms = session.CreateGmsClient(telemetry);
var systems = await gms.DiscoverAsync(cancellationToken);
var state = await gms.ReadSnapshotAsync(gmsId, cancellationToken);
var equipment = await gms.ReadEquipmentAsync(gmsId, cancellationToken);
var calibration = await gms.ReadCalibrationAsync(gmsId, cancellationToken);
var production = await gms.ReadProductionAsync(gmsId, cancellationToken);
```

`AddGmsClient` registers `IGmsClientFactory`. Factory-opened sessions are owned;
supplied sessions remain application owned. `GetProxy` exposes generated
advanced operations. Discovery owns continuation points and recognizes vendor
subtypes. Bounds reject overflow, while reads retain actual identity, numeric
types, optional presence, quality and time. Separate reads are not atomic
equipment history.

Results use the same session:

```csharp
var results = gms.GetResults(gmsId, new GmsResultLimits
{
    MaximumResults = 32,
    MaximumMetadataBytes = 256 * 1024,
    MaximumFileBytes = 8 * 1024 * 1024
});
var latest = await results.GetLatestAsync(cancellationToken);
var identifiers = await results.QueryAsync(
    maximumResults: 16, cancellationToken: cancellationToken);
await results.DownloadAsync(
    latest.ResultMetaData!.ResultId!, destination, cancellationToken);
```

This scoped example assumes an available result and application-owned writable
destination. No-data is not fabricated success. Metadata handles and owned
remote streams close on success, failure and cancellation. The caller's
destination remains open and may contain a partial prefix on failure.
Domain, UA and cancellation exceptions remain distinguishable; cleanup
failures do not replace operation failures.

## Events and conditions

The GMS facade reuses the bounded MachineTool collector and adds generated
sensor-warning decoding. It does not implicitly acknowledge/enable conditions.

`GmsResultClient.ObserveIntermediateResultsAsync` also supports a caller-owned
`IStreamingSubscription`, removing only its own monitored item. Characteristic
placeholder names are explicitly qualified. Its readiness-callback overload
uses `SubscribeEventsWhenReadyAsync` to verify server-side creation and native
errors before signaling readiness. A setup deadline is not an empty successful
window; custom streams without that support fail explicitly.

Event fields are Variants, not per-field DataValues. Read result variables for
acquisition-quality semantics that event fields do not transmit. Observation
counts, queues, time and payloads are bounded; retained refresh requires its
actual end notification. Caller cancellation never reports empty success.

## MCP and samples

The `gms` profile provides discovery/capabilities, equipment/qualification,
calibration and production observations, result queries/characteristics/
corrections/downloads, and bounded events/conditions. It uses existing named
sessions and typed clients, with no invented acquisition/calibration/job-start
or correction commands.

Read/observe tools never acknowledge conditions or alter equipment.
Explicit result acknowledgment uses standard result identifiers and requires
consent; it is distinct from Part 9 condition acknowledgment. Oversized output
is an error, not truncated success. See [MCP Server](McpServer.md) for the exact
catalog and mixed profiles.

The [GMS samples](../samples/GMS/) demonstrate real typed server/client
workflows with explicitly simulated providers. The
[combined sample](../samples/CompanionSuite/) hosts all families on one owner.

## Conformance and upstream exception

Contributions reflect committed instances/providers, not loaded namespaces.
Secure profiles also require actual endpoint, certificate and user-token
capabilities.

| Published evidence | Runtime interpretation |
| --- | --- |
| Document 1.0.1; NodeSet/model 1.0.0 | Preserve released namespace/version |
| Production facet requires undefined GMS_ProductionStatisticsType | Implement defined jobs/parts and inherited statistics, but withhold complete GMS Production facet |
| Qualified XML default counts up; CU requires remaining time/counting down | Keep provenance unchanged and bind Qualified/Time/IsCountingUp=false |
| Warning prose names differ from XML | Preserve SensorWarningAlarmType, namespace-qualified i=1010, with additional-sensor semantics |
| Identifier table entries missing from XML | Preserve provenance; do not invent standard nodes |

[Annex A.1](https://reference.opcfoundation.org/specs/OPC-40210/annex-a)
makes the released NodeSet the normative mapping. Section 12.3.2.7 nevertheless
requires the undefined statistics CU. This is a specific upstream conformance
exception, not absent implementation of all defined production behavior.
Never advertise the complete GMS Production facet by assuming that type exists.

## Verification and troubleshooting

| Requirement | Regression suites |
| --- | --- |
| Canonical model/encoding/proxies | Model asset/declaration/encoding/generation/proxy suites |
| Mandatory systems and hosting | `GmsSystemTests`, hosting/guard suites |
| Sensors/workspace/equipment | `GmsEquipmentTests`, `GmsSystemValidationTests` |
| Calibration | `GmsCalibrationTests` |
| Native jobs/parts and rejected acquisition | `GmsSystemProductionTests` |
| Environmental warnings and Part 9 | `GmsSensorWarningTests` |
| Results/characteristics/corrections | Contract/measurement/factory suites |
| Stores, metadata and file lifetime | Store/provider/query/integration suites |
| Typed observation | Client/system-client/result-observation suites |
| Shutdown/removal/cancellation | System lifetime and result integration suites |

```powershell
dotnet test tests\Opc.Ua.GMS.Tests\Opc.Ua.GMS.Tests.csproj -p:CustomTestTarget=net10.0
dotnet test tests\Opc.Ua.GMS.Tests\Opc.Ua.GMS.Tests.csproj -p:CustomTestTarget=net48
```

NativeAOT and real-session/MCP execution complement these tests; neither
generated completeness nor passing tests constitutes independent certification.

| Symptom | Action |
| --- | --- |
| Missing provider/store | Select per-system providers before building |
| Rejected optional data | Check feature dependencies, presence and raw samples |
| Warning names a primary probe | Use an additional environmental measurement |
| Unknown tool/correction reference | Resolve within the same prospective system |
| Oversized download | Use legitimate provider/workflow bounds, never truncated success |
| Inactive alarm remains retained | Check acknowledgment separately from physical clearing |
| Full Production facet absent | Preserve the documented undefined-CU exception |
| Measurements look like successful zeros | Inspect presence, quality and source timestamp |

Related: [MachineTool/WMTP](MachineTool.md), [Machinery](Machinery.md),
[Alarms](AlarmsAndConditions.md), [MCP](McpServer.md),
[NativeAOT](NativeAoT.md), [Developer guide](DeveloperGuide.md).
