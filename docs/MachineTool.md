# Machine Tools and Wireless Peripherals

The `Opc.Ua.MachineTool*` packages implement two independent released models:

- [OPC 40501-1](https://reference.opcfoundation.org/specs/OPC-40501-1/full):
  Machine Tools 1.02.0.
- [OPC 40505](https://reference.opcfoundation.org/specs/OPC-40505/full):
  Wireless Machine Tool Peripherals (WMTP) 1.0.0.

They share model, server, client and MCP packages, not wire identities.
A WMTP-only endpoint does not need a MachineTool instance. Providers connect
observations and operations to equipment; generated types describe the
protocol. These packages do not execute CNC motion, implement Bluetooth
pairing, flash firmware, or replace a safety controller.

## Packages and model selection

NuGet IDs have the prefix `OPCFoundation.NetStandard.`.

| Package | Purpose |
| --- | --- |
| `Opc.Ua.MachineTool` | Both released models, generated states/proxies/factories and shared contracts |
| `Opc.Ua.MachineTool.Server` | Fluent hosting, coherent acquisition, production/equipment, WMTP records and configuration |
| `Opc.Ua.MachineTool.Client` | Discovery, typed snapshots, bounded observations, job/file and WMTP workflows |
| `Opc.Ua.Mcp.MachineTool` | Bounded MachineTool and WMTP tools over existing named OPC UA sessions |

There is no separate WMTP package, profile or guide. This family follows the
normal root-version policy rather than forced preview classification.
Development builds can still have a prerelease root version.

| Model | Wire namespace | Generated CLR namespace |
| --- | --- | --- |
| MachineTool | `http://opcfoundation.org/UA/MachineTool/` | `Opc.Ua.MachineTool` |
| WMTP | `http://opcfoundation.org/UA/WMTP/` | `Opc.Ua.MachineTool.WMTP` |

`MachineToolServerOptions.Parts` selects `MachineToolParts.MachineTool`,
`MachineToolParts.Wmtp`, or `MachineToolParts.All`. The default is MachineTool
alone. Model selection loads dependencies, not instances or optional features.
MachineTool uses DI, IA, Machinery, Machinery Jobs and ISA95 JobControl V2.
WMTP uses DI, Machinery ProcessValues, PA-DIM and Dictionary/IRDI.

## Hosting and ownership

Configure application identity, endpoints, certificates and authentication
through the existing server builder, then add the family:

```csharp
serverBuilder
    .AddMachineTool(options => options.Parts = MachineToolParts.All)
    .ConfigureMachineTool(async (context, cancellationToken) =>
    {
        await ConfigureInstancesAsync(context, cancellationToken);
    });
```

`ConfigureInstancesAsync` is the application's configuration routine. Startup
awaits it and drains registrations on failure. Retain handles for acquisition;
do not dispose them at the end of the configuration callback.

Use `AddMachineToolToMachinery` and `ConfigureMachineToolFor<TNodeManager>`
with an existing compatible DI/Machinery owner. Standalone `AddMachineTool`
selects the necessary Machinery parts automatically. Do not combine standalone
registrations that each claim the same namespaces.

One owner can host multiple machines, independent peripherals, GMS and
MachineVision instances. Providers are selected per instance. An explicitly
shared adapter must coordinate its equipment state and remain application
owned; accidentally resolving one unkeyed hardware adapter for every
peripheral is not supported.

Direct builders on `IMachineryBuildContext` use the same implementation.
Equipment providers are borrowed unless an API explicitly transfers ownership.
Successful-construction-only ownership does not transfer during failed setup.

## Creating and refreshing a machine

Inside an awaited configuration routine:

```csharp
using Opc.Ua;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot());
MachineToolRuntime<MachineToolState> machine = await context
    .AddMachineTool("Mill")
    .WithProvider(provider)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example manufacturer");
        identity.SerialNumber = "Mill-001";
        identity.ProductInstanceUri = "urn:example:mill:001";
    })
    .BuildAsync(cancellationToken);
```

The initial observation is explicitly unavailable, not successful zero data.
The application supplies actual acquisition metadata:

```csharp
DateTimeUtc acquired = timeProvider.GetUtcNow().UtcDateTime;
await provider.SetSnapshotAsync(new MachineToolSnapshot
{
    Operation = new MachineToolOperation
    {
        Mode = MachineOperationMode.Automatic,
        Sample = new MachineToolSampleInfo(acquired, StatusCodes.Good)
    }
}, cancellationToken);
await machine.RefreshAsync(cancellationToken);
```

`SetSnapshotAsync` advances the reference provider's revision. A revision alone
does not authorize skipped production transitions. The runtime validates and
prepares selected modules before coherent commit; provider/factory callbacks
do not run under the commit gate.

After acceptance, a notification failure cannot undo equipment data.
`RetryPublicationAsync` retries retained publication with its existing event
identities, not hardware commands.

## Monitoring, tools and prognoses

Mandatory structure is the default. `WithFeatures`, `WithEquipment`,
`WithProduction` and `WithPrognoses` explicitly select optional observations.
The specialized option methods add feature bits; a later `WithFeatures`
replaces the selection.

| Surface | Supplied observations | Runtime behavior |
| --- | --- | --- |
| Operation | Mode, overrides, warm-up, counters, obligation | Type/range/quality checks and Machinery state projection |
| Channels/working units | Ordinary/combined channels, spindles, laser/EDM | Actual generated types and coherent monitoring |
| Stacklights/software | Installed segments/components | Standard IA/software structure, not inferred hardware |
| Tools/multi-tools | Keys, control identity, locks, locations | Typed inventories, uniqueness, references and lifetime |
| Tool life | Number values, units, counting direction, thresholds | Preserved numeric representation and independent counters |
| Prognoses | Explicit supported kinds, targets, predictions | Native subtypes and prospective same-instance references |
| Dynamic inventories | Complete prospective collection | NodeVersion/model-change events, rollback and node retirement |

For example:

```csharp
builder.WithEquipment(new MachineToolEquipmentOptions
{
    Features = MachineToolFeatures.Tools |
        MachineToolFeatures.DynamicTools |
        MachineToolFeatures.ToolLife,
    MaximumTools = 256,
    MaximumLifeEntriesPerTool = 4,
    MaximumElements = 1024
});
```

An empty dynamic inventory differs from absent required data. A slot retains
its type, parent and selected optional shape; use a new identity to change
that shape. Reaching a tool-life threshold does not fabricate a hardware lock.

`WithPrognoses` requires explicit `SupportedKinds`. The ten standard concrete
variants cover maintenance/manual activity, part load/unload, process
changeover, job end, tool change/load/unload and utility change. Targets
resolve against the prospective tool/job/part inventory of the same instance.
Unknown, ambiguous, removed or foreign targets fail. Prediction quality/time
remain intact; overdue forecasts are not silently discarded.

Dynamic removal waits for the shared owner's final-value publication boundary:
inclusion in server Publish responses, not client transport acknowledgment.
See [Final value publication](FinalValuePublication.md).

## Production, jobs and files

MachineTool 1.02 retains legacy `Production` declarations even where deprecated.
GMS inherits them, so the implementation supports both these observations and
the newer Machinery JobManagement AddIn.

| Interface | Meaning |
| --- | --- |
| Mandatory `ActiveProgram` | Selected-program observation, including explicit unavailability |
| `WithProduction` | Jobs, programs, plans, parts, statistics, transitions and interruption conditions |
| `WithJobManagement` | Actual Machinery/ISA95 V2 receiver and response-provider methods |
| `MachineToolJobSource` | Bounded, non-durable consistent reference source for both representations |
| `WithFileSystem` | Explicit existing file provider, optionally including actual WorkMasters |

A static plan requires a job; a selected dynamic plan can be empty.
Job/program transition sequences are contiguous, including Running-to-Running
transitions. Initial attachment does not invent earlier history. Current-run,
cumulative and lifetime counters retain their different scopes.

JobManagement requires both per-instance receiver and response providers.
The reference `MachineToolJobSource` uses the existing ISA95 engine with
explicit job/work-master/program mappings. Bind it to both representations
and use `source.DrainAsync(machine.RefreshAsync)` to publish each accepted
revision. Reading only the latest snapshot can omit intermediate transitions.
Accepted Start/StoreAndStart is not equipment execution; the application must
supply separate execution and part observations.

`WithFileSystem` binds an `IFileSystemProvider` to the actual FileSystem node.
`includeWorkMasters: true` requires a provider directory with that identity.
Provider capabilities and `FileDirectoryBindingOptions` govern changes.
Streams/materialized nodes follow their session and machine lifetimes.
Reuse [FileSystemClient](FileSystemClient.md) from generated directory proxies;
do not implement another file protocol or expose arbitrary host paths.

## Notifications and conditions

`WithNotifications` selects an `IMachineToolNotificationProvider` through
`MachineToolNotificationOptions`. It supplies complete alert inventory and
consecutive informational messages. `RefreshNotificationsAsync` refreshes this
independent source.

Errors/warnings use `AlertType`; informational messages use a concrete subtype
of abstract `NotificationEventType`. Production interruptions have their own
standard conditions. Nested/root notifier propagation does not duplicate events.

Acknowledge does not clear an equipment cause. An inactive unacknowledged
condition may remain retained. Enable, Disable, AddComment, Acknowledge and
ConditionRefresh use the existing Part 9 lifecycle and authorization.

## Wireless Machine Tool Peripherals

### Explicit adapters and measurements

Use `context.AddWirelessPeripheral` with `MachineToolParts.Wmtp` or `All`.
Its DeviceInformation, Measurements and MachineryBuildingBlocks keep their
own standard identity; identification is nested under DeviceInformation.

The application supplies instance-specific `device`, `measurements`,
`authorization`, `workRecords` and `temperatureUnit` in this scoped example:

```csharp
WmtpPeripheral peripheral = await context.AddWirelessPeripheral("ToolHolder")
    .WithProvider(device)
    .WithMeasurementProvider(measurements)
    .WithAuthorization(authorization)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example manufacturer");
        identity.SerialNumber = "Holder-001";
        identity.ProductInstanceUri = "urn:example:holder:001";
    })
    .AddMeasurement(new WmtpMeasurementDefinition
    {
        Name = "Temperature",
        Kind = WmtpMeasurementKind.Temperature,
        EngineeringUnits = temperatureUnit,
        EURange = new Opc.Ua.Range { Low = -20, High = 100 },
        InstrumentRange = new Opc.Ua.Range { Low = -40, High = 125 },
        TriggerMilliseconds = 1000,
        Delta = Variant.From(0d)
    })
    .WithWorkRecords(workRecords,
        WmtpRecordFeatures.ReportIndex | WmtpRecordFeatures.ReportTime)
    .BuildAsync(cancellationToken);
```

An explicitly selected device adapter may also implement measurement,
calibration, time and transfer interfaces. Separate adapters can be selected
through the corresponding builder methods. There is no automatic unkeyed
equipment-provider fallback.

The eight kinds are temperature, rotation speed, humidity, acceleration,
force, torque, position and dimension. WMTPMeasurement inherits Machinery
ProcessValue and PA-DIM AnalogSignal behavior.

SetTriggerSettings/SetDeltaCondition preserve checked type/range handling and
the invariant that both settings cannot be zero. Delta retains the sensor's
numeric type, including UInt64 precision. Refusal preserves accepted state.
Once the provider accepts a configuration, later caller cancellation cannot
discard its accepted trigger/delta cache.

Samples preserve acquisition time separately from receipt. Optional index,
period, uncertainty, internal interval and high/high-high duration counters
are explicit. `StaleAfter` changes stale usable data to
`UncertainLastUsableValue`, never a fabricated fresh sample.

### Configuration and transfer

Optional providers cover calibration, UTC device time with `TimeZoneDataType`,
firmware/licence transfer, battery, service/lifecycle and counters.
Transfer acceptance is **not installation or licence activation**. Payloads
are bounded and must not be logged. BLE discovery, pairing, radio transports
and flash/licensing engines remain manufacturer responsibilities.

### Independent work and service records

`WithWorkRecords` and `WithServiceRecords` select independent `IWmtpRecordStore`
instances. `InMemoryWmtpRecordStore` is bounded reference storage, not a durable
database.

| Workflow | Contract |
| --- | --- |
| Count | All records or an acquisition-time interval |
| Report | All, inclusive index/time range, or selected first/last methods |
| Delete | Explicit all/index/time selection, never a read side effect |
| Abort | Explicit peripheral-operation request under authorization |

Reports copy records. CombinedReportAll returns the whole report or fails on
limits; it never truncates or invents continuation points. Use supported
index/time ranges for smaller selections.

```csharp
WmtpPeripheralClient peripheral = client.GetWirelessPeripheral(peripheralId);
WmtpRecordClient records = await peripheral.GetWorkRecordsAsync(cancellationToken);
var range = new WmtpRecordQuery
{
    Selection = WmtpRecordSelection.Index,
    FromIndex = 0,
    ToIndex = 99
};
var report = await records.ReadAsync(range, maximumRecords: 100,
    cancellationToken: cancellationToken);
```

Client cancellation never implicitly calls AbortOperation, which could affect
another caller. `AbortAsync` is a separate authorized action. Failed setup
does not take ownership of caller resources; successful construction transfers
only explicitly designated resources.

## Typed clients

```csharp
using Opc.Ua.MachineTool.Client;

await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
var machines = await client.DiscoverAsync(cancellationToken: cancellationToken);
var snapshot = await client.ReadSnapshotAsync(machineId, cancellationToken);
var equipment = await client.ReadEquipmentAsync(
    machineId, cancellationToken: cancellationToken);
var prognoses = await client.ReadPrognosesAsync(
    machineId, cancellationToken: cancellationToken);
```

`AddMachineToolClient` registers the managed-session factory path. Factory-opened
sessions are owned; supplied sessions remain application owned. Generated
proxies remain available for advanced standard operations.

Discovery recognizes vendor subtypes, deduplicates references and releases
continuation points. Reads preserve identities, optional presence, numeric
types, quality and acquisition time. Multi-request reads are not atomic history.

ObserveData/Events/Conditions use private bounded subscriptions. Failed setup,
cancellation and communication errors are not empty successful observations.
Condition observation requires actual RefreshEnd; it never acknowledges or
enables conditions. `ObserveValuesAsync` reuses that collector for selected
native variables. WMTP measurement readings expose `ValueNodeId` for the
AnalogSignal variable, distinct from the measurement-object `NodeId` used by
configuration methods.

## MCP and samples

The `machinetool` MCP profile covers both families, including WMTP-only
endpoints. It provides discovery/capabilities, snapshots, tools/life,
production/jobs, forecasts, bounded observation, remote files and WMTP
configuration/records. Reads do not start jobs, change sampling, delete
records, transfer bytes or abort operations. Changes require explicit consent
and remain subject to OPC UA authorization and real instance capability.
See [MCP Server](McpServer.md) for catalogs and composition.

The [MachineTool samples](../samples/MachineTool/) contain a secure-by-default
server and passive-by-default client. `--mode machine|wmtp|mixed` selects
independent model loading; `--advanced` and `--instances` select optional
simulation and isolation. `--help` describes bounded commands and explicit
loopback-only demo trust. The [mixed sample](../samples/CompanionSuite/) hosts
all companion families on one owner.

## Conformance and verification

Contributions reflect committed instances, selected features and actual
providers, not namespace presence. Basic/BasicSecure claims also require the
specified monitoring quotas, endpoint security and user-token policy.
Mandatory structure alone does not claim every Basic requirement.

| Requirement | Regression suites |
| --- | --- |
| Canonical models, encoding and generated proxies | Model asset/generation/encoding/proxy suites |
| Monitoring, quality and capability gates | `MachineToolRuntimeTests`, capability/monitoring suites |
| Tools/life and dynamic lifetime | Equipment validation/factory/lifetime suites |
| Retained production and state histories | Production runtime/session/client/lifetime suites |
| Modern ISA95 and consistent source | `MachineToolServicesTests`, job-source/session/lifetime suites |
| Notifications, conditions and routing | Notification/client/validation/lifetime suites |
| Forecast references | Prognosis validation/lifetime suites |
| Files and session/node teardown | `MachineToolFileLifetimeTests` |
| WMTP configuration, models and facets | Runtime/validation/conformance/review-regression suites |
| Records and operation isolation | `WmtpRecordStoreTests`, `WmtpSessionTests`, `WmtpLifetimeTests` |
| Hosting and typed workflows | `MachineToolHostingTests`, `MachineToolClientTests` |

```powershell
dotnet test tests\Opc.Ua.MachineTool.Tests\Opc.Ua.MachineTool.Tests.csproj -p:CustomTestTarget=net10.0
dotnet test tests\Opc.Ua.MachineTool.Tests\Opc.Ua.MachineTool.Tests.csproj -p:CustomTestTarget=net48
```

NativeAOT checks publish **and execute** actual applications/MCP paths.
Internal tests and facet metadata are not OPC Foundation certification.

## Troubleshooting and provenance

| Symptom | Action |
| --- | --- |
| Duplicate namespace owner | Use the `...ToMachinery` composition path |
| Missing selected provider/data | Supply complete per-instance adapters/observations or omit that feature |
| WMTP absent | Check independent `Parts` selection |
| Trigger/delta rejected | Preserve numeric/unit bounds and a nonzero trigger condition |
| Oversized record report | Select a supported smaller index/time range |
| Schema change rejected | Preserve slot type/parent/optional shape or use a new identity |
| Publication blocks refresh | Retry accepted publication, not equipment effects |
| Unexpected zero/unavailable data | Inspect original quality and source timestamp |
| Transfer accepted, device unchanged | Acceptance is not firmware installation/licence activation |

MachineTool model 1.02.0 has namespace date 2024-11-01; WMTP 1.0.0 uses
2025-11-01. Pinned provenance and identifier guards are under
`src\Opc.Ua.MachineTool\Model`. The canonical models retain distinct namespaces
and deprecated production declarations. Do not repair prose/XML differences
by inventing standard identifiers.

Related: [Machinery](Machinery.md), [DI](DeviceIntegration.md),
[ISA95](ISA95.md), [GMS](GMS.md), [Alarms](AlarmsAndConditions.md),
[NativeAOT](NativeAoT.md), [Developer guide](DeveloperGuide.md).
