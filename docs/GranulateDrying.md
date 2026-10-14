# Granulate drying devices (OPC 40082-6)

The granulate-drying packages implement the reusable server/client interface from
[OPC 40082-6, version 1.0.0](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0).
They do not simulate a dryer or contain recycling-process, vendor, or production-counter logic.
An injected hardware or committed-process provider remains the authority.

| Package | Responsibility |
| --- | --- |
| `Opc.Ua.GranulateDrying` | Existing compiled model, generated states, factories, encodings, method proxies, and event records. |
| `Opc.Ua.GranulateDrying.Server` | Selectable device construction, asynchronous provider bindings, control ownership, committed-value publication, alarms, and hosting composition. |
| `Opc.Ua.GranulateDrying.Client` | Discovery, generated typed operations, typed variables preserving quality/timestamps, maintenance downloads, and event streams. |

The model namespace is `http://opcfoundation.org/UA/PlasticsRubber/GDD/`.
`GranulateDryingDeviceType` derives from **BaseObjectType**, not a DI device type.
Instances are organized under Machinery's `Machines` folder. Their generated identification
add-ins and the existing AMB sidecar provide asset discovery without fictitious inheritance.

## Model provenance and conformance status

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
It also includes modeled functionality omitted from that catalogue, notably protective
airflow reduction and the optional Machinery operation-mode add-in.

## Hosting one address-space owner

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

## Selecting features and real placeholders

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

## Provider and commit contract

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

### Live absolute updates

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

The runtime serves immutable committed snapshots through its read hooks. Consequently the
stack's normal post-write cache assignment cannot expose an optimistic proposal or replace the
provider's source timestamp. `ReadCommittedValue` is also available to local adapters.

Hardware must implement actual temperature regulation, feeder actuation, continuous versus batch
storage/protection behavior, air intervals, drying-time evolution, energy integration, persistence,
device-clock changes, and tuning. Those are provider responsibilities, not a second simulator
running in this package. A sample adapter can therefore remain only a projection of its own
committed coordinator.

## Authorization and observers

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

## Errors, maintenance, and instructions

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

## Typed client

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

## Complete capability matrix

The descriptions below are implementation summaries, not copied specification tables.
“Provider” means that real equipment behavior is supplied through an injected interface,
with validation, authorization, committed publication, and typed client access in this package.

| Catalogue capability | Selection / implementation | Primary clause |
| --- | --- | --- |
| Basic | Concrete device, required identity/configuration/operation, real bins and temperature/mode | [7–9, 12](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/7) |
| Maintenance | Maintenance descriptors, generated attributed reset, typed conditions | [14–16](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/14) |
| Users | `DeviceFeatures.Users`; exact login/logout payload and committed actual user | [17](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/17) |
| DesiccantBed | Nonempty bed descriptors with real selected bed instances | [10–11](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/10) |
| DryingProtection | Bin control pair and response status; provider owns physical reduction | [13.9–13.11](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.9) |
| AllFeeders | Combined feeder control pair/status | [13.20–13.22](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.20) |
| MaterialFeeder | Dryer-feeder control pair/status | [13.14–13.16](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.14) |
| MachineFeeder | Machine-feeder control pair/status | [13.17–13.19](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.17) |
| TemperatureController | Bin temperature-controller control pair/status | [13.23–13.25](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.23) |
| AirFlowInterval | Composite selection of mode and both interval durations | [13.29–13.31](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.29) |
| ResetDryingTime | Generated reset plus committed remaining time and receipt timestamp | [13.33–13.34](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.33) |
| DesiccantBedController | Bed control pair/status | [11.9–11.11](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/11.9) |
| MaterialIdentification | Optional typed bin String, provider-backed write | [13.2](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.2) |
| BatchNumber | Optional typed bin String, provider-backed write | [13.3](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.3) |
| MaterialDryingTime | Optional Duration preset, milliseconds | [13.5](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.5) |
| RemainingMaterialDryingTime | Absolute remaining Duration | [13.6](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.6) |
| DisplayLanguage | Optional LocaleId write; provider may retain a supported language | [7.6](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/7.6) |
| IdentifyDevice | Generated method delegated to physical identification | [8.3](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.3) |
| ResetAllErrors | Provider reset; no optimistic error-list clearing | [8.6](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.6) |
| ResetErrorById | Exact String argument, known-error validation, provider reset | [8.7](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.7) |
| ResetEnergyMeasurement | One committed reset covering selected device/bin/bed counters | [8.8](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.8) |
| MainFailureBehaviour | Optional validated enumeration; persistence belongs to provider | [8.9](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.9) |
| TotalEnergy | Device/bin/bed common EnergyType with optional further common members | [8.10](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.10) |
| EnergyConsumptionResettable | Device/bin/bed absolute kWh counters | [8.11](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.11) |
| MaintenanceInformation | Selected maintenance container and initial NodeVersion | [8.12, 14](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/14) |
| EnergyConsumptionLimit | Provider-backed Double with 0–200 percent bounds | [8.13](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.13) |
| DewPoint | Optional bin analog / bed monitored parameter | [11.2, 13.38](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/11.2) |
| AirFlow | Optional monitored bin / controlled bed parameter, distinct units | [11.3, 13.36](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/11.3) |
| TemperatureOfDesiccantBed | Optional common controlled parameter | [11.4](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/11.4) |
| TemperatureDesiccantBedOut | Optional monitored outlet temperature | [11.5](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/11.5) |
| TemperatureBackToDesiccantBed | Optional monitored return temperature | [11.6](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/11.6) |
| ExpiredDryingTime | Absolute elapsed Duration, no local timer | [13.7](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.7) |
| TemperatureReduction | Provider-backed relative kelvin reduction | [13.8](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.8) |
| MaterialAvailableDryer | Optional authoritative Boolean | [13.12](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.12) |
| MaterialAvailableMachine | Optional authoritative Boolean, not a dry-material guarantee | [13.13](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.13) |
| DustRemovalPulses | Optional typed UInt16 setting | [13.26](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.26) |
| DryingLevel | Optional validated enumeration | [13.28](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.28) |
| AirFlowMode | Optional validated enumeration; INTERVAL requires its variables | [13.29](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.29) |
| IntervalTimeOn | Independently selectable nonnegative Duration | [13.30](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.30) |
| IntervalTimeOff | Independently selectable nonnegative Duration | [13.31](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.31) |
| InitialFillingActive | Optional authoritative Boolean | [13.32](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.32) |
| AirSource | Optional validated fresh/return-air enumeration | [13.35](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.35) |
| TotalAirConsumption | Absolute volume counter with explicit units | [13.37](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.37) |
| DryAirTemperature | Optional monitored parameter | [13.42](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.42) |
| AmbientAirTemperature | Optional monitored parameter | [13.43](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.43) |
| TopTemperature | Optional monitored parameter | [13.44](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.44) |
| ConeTemperature | Optional monitored parameter | [13.46](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.46) |
| CylinderTemperature | Optional monitored parameter | [13.47](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.47) |
| LooseningAid | Optional typed UInt16 setting | [13.45](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.45) |
| MaintenanceInstructions | Selected bounded read-only FileType document | [15.4](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/15.4) |
| RemainingInterval | Optional common maintenance value and matching alarm field | [16.3](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/16.3) |

Additional modeled support:

- Protective airflow-reduction request pair and response status:
  [13.39–13.41](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/13.39).
- Required persistent remote-control semantics:
  [8.14](https://reference.opcfoundation.org/specs/OPC-40082-6/v1.0.0/8.14).
- Optional Machinery operation mode; committed current-state projection uses generated
  Machinery identifiers. No client cause method is invented for those state machines.
- Common configuration naming/location/timezone/clock and optional page images;
  parameter reference/limits/sensitivity/suppression/reset; controlled ramps/PID/tuning;
  common energy and maintenance optional values.

## Validation and boundaries

`tests/Opc.Ua.GranulateDrying.Tests` exercises the public runtime and actual encrypted
ManagedSession clients for standalone and composed registration, each independent option,
generated methods, quality/timestamps/units, AMB discovery, provider failures, cancellation,
ownership/observers, file cleanup, signed common limits, error events, and concrete maintenance
events. Test PKI is confined to uniquely owned directories under worktree `artifacts`.

Implementation validation is pending the coordinated build lane; test source existence is not
a passing test or certification claim. The intended targeted command is:

```powershell
dotnet test tests\Opc.Ua.GranulateDrying.Tests\Opc.Ua.GranulateDrying.Tests.csproj `
  -f net10.0 -p:CustomTargetFrameworks=net10.0 -p:CustomTestTarget=net10.0 `
  -m:1 -p:UseSharedCompilation=false
```

The libraries use compiled factories/codecs and explicit providers, with NativeAOT compatibility
enabled where supported. A NativeAOT publish is a separate validation gate; it is not implied
by the project setting or managed tests.

Known boundaries and publication discrepancies:

- No safety/emergency-stop implementation; those signals are outside the specification's scope.
- Device topology is selected at creation. There is no invented UA “add bin” or “delete maintenance”
  method; a host that replaces topology must also handle its model-change lifecycle.
- Equipment persistence, calibration, scheduling, and process physics belong to providers.
- The inherited `Reset` is not an alternative to `GDDMaintenanceReset`.
- The published catalogue has copied equipment wording, a `DryingTime` description for the
  actual `MaterialDryingTime` member, a duplicated reset entry in the Full facet, and an incorrect
  maintenance cross-reference in clause 8.12. The implementation follows the actual released
  model and linked defining clauses, not those editorial inconsistencies.
- No global session cap is inferred from the scope's single-client wording.
- No formal profile conformance, certification, or unsupported physical behavior is claimed.
