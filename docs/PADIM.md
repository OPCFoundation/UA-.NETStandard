# PA-DIM devices, signals and analysers

The Process Automation Device Information Model (PA-DIM), OPC 30081 version
1.02.0, describes process instruments independently of their field
communication protocol. `Opc.Ua.PADIM.Server` publishes standalone instruments
under the Device Integration (DI) `DeviceSet`; `Opc.Ua.PADIM.Client` discovers
them and uses generated proxies for their methods. A server does not need
Machinery to publish these devices.

The generated `Opc.Ua.PADIM` assembly remains the only producer of the PA-DIM
model and its required IRDI dictionary. The runtime projects reference that
assembly; they do not generate competing type definitions or copy dictionary
entries into device instances. Model availability alone does not establish
support for every PA-DIM facet.

## Host a standalone device

Register providers with the application's service container, then configure the
address space after the model namespaces have been initialized:

```csharp
builder.Services.AddSingleton<InstrumentProvider>();
builder.Services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "InstrumentServer";
        options.EndpointUrls.Add("opc.tcp://localhost:62562/InstrumentServer");
    })
    .AddPadim()
    .ConfigurePadim(async context =>
    {
        InstrumentProvider provider = context.GetRequiredService<InstrumentProvider>();
        var name = new QualifiedName("Transmitter", context.Manager.InstanceNamespaceIndex);
        var builder = await context.Manager.CreatePadimDeviceAsync(name);
        builder.WithIdentification(identity =>
        {
            identity.Manufacturer = "Instrument manufacturer";
            identity.ManufacturerUri = "urn:example:instrument-manufacturer";
            identity.Model = "Multivariable transmitter";
            identity.SerialNumber = "12345";
            identity.ProductCode = "MV";
            identity.HardwareRevision = "1";
            identity.SoftwareRevision = "1";
            identity.ProductInstanceUri = "urn:example:instrument:12345";
            identity.RevisionCounter = 0;
        });
        builder.Device.AssetId!.Value = "TT-12345";
        builder.Device.DeviceHealth!.Value = DeviceHealthEnumeration.NORMAL;

        PadimDevice device = builder.BindPadim();
        device.WithHealthAlarms("health", provider);
        AnalogSignalObjectState temperature = await device.AddMeasurementAsync(
            new QualifiedName("Temperature", context.Manager.InstanceNamespaceIndex),
            PadimMeasurementKind.Temperature,
            "TT-12345");
        device.BindValue(temperature.AnalogSignal!, "temperature", provider)
            .WithEngineeringUnits(
                new EUInformation("degC", "http://www.opcfoundation.org/UA/units/un/cefact")
                {
                    UnitId = 4408652
                },
                new Opc.Ua.Range { Low = -40, High = 150 });
        await device.RegisterAsync(context.CancellationToken);
        await device.RefreshAsync(context.CancellationToken);
    });
```

`InstrumentProvider` implements `IPadimValueProvider`. The provider returns a
`DataValue`, not a value converted to `Double`; the server retains its
`Variant`, status and source timestamp. The binding checks the generated
`DataType` and `ValueRank` before changing the published value. A bad-quality
sample may have a null value. A healthy null sample or an incompatible value
throws `BadTypeMismatch` and leaves the previous published value unchanged.
The example's `health` key supplies the standard DI device-health category;
setting the initial generated property is not a substitute for that binding.

The application schedules acquisition by calling `RefreshAsync`. A device
serializes provider calls, writes and physical-operation requests. Disposal
rejects new work, cancels admitted work and waits for admitted callbacks to
leave before releasing resources. Providers must observe cancellation so
shutdown can complete.

`AddPadim()` registers the DI owner. When another companion already owns DI,
use `AddPadimModel()` to add the models to that owner rather than creating a
second `DeviceSet`. A directly constructed `DiNodeManager` can receive
`PadimModelProvider` through its model-provider constructor argument and use the
same `CreatePadimDeviceAsync` and `BindPadim` APIs.

## Device and signal families

`PadimDeviceKind` selects the base PA-DIM device, the generic process analyser,
or a concrete analyser family. `PadimSignalKind` selects analog and continuous
control, discrete and two-/multi-state control, or analytical signal objects.
The analyser and analytical signal selections include pH, conductivity,
amperometric, fluorescence quenching, TOC, gas chromatography, infrared,
paramagnetic, thermal conductivity, tunable diode laser, zirconium dioxide,
catalytic bead, diode array, Raman and FTNIR/FTIR families.

`AddMeasurementAsync` selects the VariableType of an analog signal without
flattening temperature, pressure, flow, level, density or totalizer measurements
into an application-defined variable. `PadimMeasurementKind` also selects mass
flow and actual/normalized volume flow, analytical measurement and PAT types.
The generated declarations retain the published data types, ranks and
semantic references.

`AddSignalAsync` takes a configuration callback. Configure optional generated
members in that callback so the manager registers the completed subtree.
`BindValue` can bind any generated measurement, nameplate, condition or
calibration variable below the device. It rejects a member belonging to another
device. Call `RegisterAsync` after adding optional members to an already
registered device; assigning a local object alone does not make its NodeId
readable or subscribable.
Facet contributions also require the relevant members and every node on their
ownership path to be indexed by the DI manager. Adding a child and binding its
provider after registration does not contribute its capability until
`RegisterAsync` registers that addition; configuration before initial
registration uses the same check.

The contributor getters describe current configuration; the server's live
`ServerProfileArray` and `ConformanceUnits` arrays are publication snapshots.
`StandardServer` publishes them at startup. After configuring or disposing a
device at runtime, explicitly publish the aggregate through the existing
`ConformanceUnitsManager` contract:

```csharp
await device.RegisterAsync(cancellationToken);
await device.RefreshAsync(cancellationToken);
var server = (ServerInternalData)device.Manager.Server;
await server.ConformanceUnitsManager.PublishAsync(cancellationToken);

await device.DisposeAsync();
await server.ConformanceUnitsManager.PublishAsync(cancellationToken);
```

`RegisterAsync`, `RefreshAsync` and `DisposeAsync` do not publish those arrays.
Until `PublishAsync` completes, clients retain the preceding advertised
capabilities and `ConformanceUnitsManager.IsSupported` may retain its preceding
result. Publish once after a batch of configuration changes rather than after
each provider sample. The example uses `StandardServer`'s `ServerInternalData`;
a custom server host must use its own conformance manager.

The generated family accessors retain the specification's spelling, including
`Ph` and `OpticalFluorescenseQuenching`.

## Calibration and condition members

Select condition and calibration fields from the actual device or signal
declaration. Analytical families can override these declarations with different
interfaces and additional fields; creating a generic object with the same
BrowseName would lose that metadata.

`ConfigureDeviceConditionsAsync` and `ConfigureSignalConditionsAsync` create
the corresponding declared condition group. `AddCalibrationAsync` creates a
named instance of the signal's calibration placeholder. Their
`PadimGroupBuilder` callback selects fields with `Bind` and nested groups with
`Group`; it rejects fields that the selected family does not declare.

```csharp
ushort ns = context.Manager.SystemContext.NamespaceUris.GetIndexOrAppend(Namespaces.PADIM);
QualifiedName Member(string name) => new(name, ns);

await device.ConfigureSignalConditionsAsync(phSignal, group =>
{
    group.Bind(Member("PhMeasuringMethod"), "ph/method", provider);
    group.Bind(Member("SensingElementTemperature"), "ph/temperature", provider);
    group.Bind(Member("SensingElementImpedance"), "ph/impedance", provider);
});
await device.AddCalibrationAsync(
    phSignal,
    new QualifiedName("Calibration", context.Manager.InstanceNamespaceIndex),
    group =>
    {
        group.Bind(Member("CalibrationTimestamp"), "ph/calibratedAt", provider);
        group.Bind(Member("SensorSlope"), "ph/slope", provider);
        group.Bind(Member("SensorAsymmetryPotential"), "ph/asymmetry", provider);
    });
```

The selector retains mandatory children and materializes requested optional
members. Only placeholders can receive a different instance BrowseName.
Each selected subtree receives instance NodeIds while its interface and IRDI
references continue to identify the published definitions. Reusing an existing
calibration name is rejected; a failed configuration removes its subtree and
provider bindings so the same name can be configured again.

`AddSubDeviceAsync` creates another PA-DIM device below the published
`SubDevices` group. The child has its own provider pipeline and is registered
with the same DI owner.

## Device health and diagnostic alarms

`WithHealthAlarms("health", provider)` binds the standard DI
`DeviceHealthEnumeration` value and creates the four DI diagnostic conditions:
Failure, CheckFunction, OffSpec and MaintenanceRequired. The provider must map
vendor diagnostics to the standard categories before returning them; numeric
categories outside 0 through 4 are rejected before the device value changes.

Each condition uses the device as its event source and is organized through
`DeviceHealthAlarms`. The server's existing alarm infrastructure handles
enable/disable, acknowledgements and condition refresh. An active condition
remains retained after acknowledgement; when the acquired category returns to
Normal, an acknowledged condition no longer needs to be retained. Bad-quality
acquisition without a value updates condition quality without inventing a
Normal category or changing the previous active state.

## Simulation, damping and configuration

Writes require both `IPadimWriteProvider` and `IPadimAuthorizer`. The authorizer
receives the calling session context, target NodeId and stable provider key.
When authorization is denied, the server returns `BadUserAccessDenied` without
calling the write provider. A provider rejection leaves the published value
unchanged. A provider must commit an accepted value before returning a Good
status.

```csharp
PadimValueBinding binding =
    device.BindValue(temperature.AnalogSignal!, "temperature", provider);
binding.WithSimulation(Variant.From(25f), configurationProvider, authorizer);
binding.WithDamping(provider, configurationProvider, authorizer);
await device.RegisterAsync();
```

`WithSimulation` supports analog, generic discrete, two-state and multi-state
process variables. It materializes `ActualValue`, `SimulationValue` and
`SimulationState`. Simulation starts disabled. While enabled, reported `Value`
uses the configured simulation value and Good quality; `ActualValue` retains
the latest acquired value with its original quality and source timestamp.
Disabling simulation restores the latest acquired sample rather than the
sample that preceded simulation. The configuration keys are the acquisition
key followed by `/SimulationValue` and `/SimulationState`.

`WithDamping` is an explicit provider capability. It materializes the Float
`Damping` parameter in seconds with the specified initial value `1.0`, then
binds acquisition and authorized writes to `<key>/Damping`. The application
implements the physical filter and configuration persistence. The SDK does
not filter an incoming sample again.

Multi-state acquisition and accepted writes use the common enum-state
implementation to validate the published `EnumValues`. `ValueAsText` and
`ValueAsDictionaryEntries` follow the selected row of `EnumDictionaryEntries`,
including a simulated value. An unknown enum value or malformed dictionary
matrix is rejected before the acquisition cache or provider configuration
changes. Bad-quality null acquisition retains the last derived text and
dictionary entries with the new quality.

Configuration callbacks and generated member selection belong to address-space
setup. Refresh and remote writes use the device's serialized provider pipeline.
Do not mutate those configuration objects concurrently with acquisition.

`WithAdministration` materializes `DisplayLanguage` and `DateOfLastChange`.
The language is an authorized provider-backed parameter. The last-change
timestamp is acquired from the provider rather than inferred from a local
cache write, so it can also represent changes made outside the UA session.

## Physical operations and authorization

Optional methods remain absent until the application materializes and binds
them. The overloads accepting a device or signal materialize the corresponding
generated optional method. Lower-level overloads accept an already selected
method. The server binding installs asynchronous callbacks:

| Binding | Accepted input | Application responsibility |
| --- | --- | --- |
| `WithFactoryReset` | Application `1`, Communication `2712`, Factory `2713` | Reset the device parameters selected by the mode; do not reset UA server security |
| `WithZeroPointAdjustment` | No arguments | Perform or initiate the physical adjustment |
| `WithAutoAdjustPositioner` | Start `2`, Abort `255` | Start or abort the manufacturer-specific commissioning cycle |

Bindings reject other argument counts or enum codes with `BadInvalidArgument`.
The session authorizer runs before the provider. A Good method result means
the provider accepted the request; it does not prove that a physical
calibration or adjustment has completed. Publish completion and errors through
the applicable generated calibration/condition members.

Repeated-operation policy, physical algorithms and durable state belong to
the application provider. The SDK serializes calls but does not replay a
method after a connection loss. PA-DIM zero-point adjustment does not add
Machinery's separate zero-point event requirements.

## Discover and observe from a client

```csharp
builder.Services.AddOpcUa()
    .AddClient(options => options.ApplicationName = "InstrumentClient")
    .AddDiscoveryAndConnect(options =>
        options.DiscoveryUrl = "opc.tcp://localhost:62562/InstrumentServer")
    .AddSubscriptions()
    .AddPadimClient();

PadimClient client = await services.GetRequiredService<PadimClientFactory>()
    .CreateAsync(cancellationToken);
ArrayOf<PadimEntry> devices = await client.DiscoverDevicesAsync(cancellationToken);
for (int ii = 0; ii < devices.Count; ii++)
{
    ArrayOf<PadimEntry> signals =
        await client.DiscoverSignalsAsync(devices[ii].NodeId, cancellationToken);
    for (int jj = 0; jj < signals.Count; jj++)
    {
        ArrayOf<PadimValue> values =
            await client.ReadValuesAsync(signals[jj].NodeId, cancellationToken);
    }
}
```

The injectable factory borrows the cached session registered by `AddClient`;
consumers must not close or dispose that session. Direct construction accepts
an existing `ISession` and `ITelemetryContext` with the same borrowing rule.
Client construction registers the generated PA-DIM structure decoders in that
session's message context so PAT values can be read as `ChemicalSubstance`.

Discovery follows the DI hierarchy, recognizes vendor subtypes and prevents
cycles. Its default limit is 4,096 visited nodes; exceeding the configured
limit throws instead of returning an incomplete result as complete. Browse
continuation points use the common managed-browse implementation. Remote
server references are rejected rather than interpreted as local NodeIds.

`ReadValuesAsync` retains each complete `DataValue`.
`ReadDictionaryEntriesAsync` reads `HasDictionaryEntry` on the actual instance
and returns namespace-URI-qualified identifiers. `IsSupported` only reports
that the connected namespace table includes PA-DIM; applications still need to
discover the actual device and its optional capabilities.

`Device`, `AnalogSignal` and `ControlSignal` return generated method proxies.
The convenience operation methods use those proxies rather than constructing
untyped `Call` requests. On a managed session, `ObserveValueAsync` uses the
shared streaming subscription. Disposing an enumeration removes that
enumeration's monitored item; it does not dispose the shared session.

`ProcessValue(measurementNodeId)` returns typed member views for `ActualValue`,
`SimulationValue`, `SimulationState` and `Damping`. These views use the common
`ClientVariable<T>` resolver and preserve the complete read `DataValue`.
Resolve an optional member before using it on a device that may not support
the capability. Setting a simulation value and enabling simulation are two
independently authorized writes; failure of the second write does not undo a
committed first write. Disable simulation explicitly when the demonstration
ends.

## Example and support boundaries

[`PadimDemo`](../samples/PADIM/PadimDemo) hosts a multivariable transmitter,
continuous control and discrete input, and a pH analyser. Its in-memory provider
uses fixed typed values and timestamps advanced by refresh count. Writes are
disabled by default; the sample requires an explicit demonstration option to
authorize them.

The client prints device identity and health, pH condition fields and calibration
metadata, including the neutral calibration point. Reading these fields does not
require enabling the demonstration writes.

The pH signal includes its measuring method, sensing-element temperature and
impedance, PAT measurand description, calibration timestamp/slope/asymmetry
potential, and a calibration point. The optional demonstration positioner operation
accepts Start and completes after two refresh cycles; readback then becomes
50 percent and the provider advances `DateOfLastChange`. Abort cancels a
pending cycle. Starting an already pending cycle or aborting an idle one
returns `BadInvalidState`. The pH adjustment records a deterministic neutral
reading and calibration timestamp. These operations do not implement physical
calibration algorithms, and the sample does not expose FactoryReset.
The demonstration client observes the provider's completion timestamp before
printing the resulting measurement. Each completion observation has a
ten-second timeout; a successful method result alone does not end the wait.

```powershell
dotnet run --project samples\PADIM\PadimDemo -p:CustomTestTarget=net10.0 -- `
    --pki-root D:\temp\padim-server-pki --run-seconds 30
dotnet run --project samples\PADIM\PadimDemo -p:CustomTestTarget=net10.0 -- `
    --client true --pki-root D:\temp\padim-client-pki
```

Trust the client/server certificates through the configured certificate stores.
For an isolated demonstration, `--insecure true --auto-accept true` explicitly
enables insecure endpoints and automatic certificate trust. Neither option is
the default.

## Facet requirements and evidence

The support rating remains **Partial**. The released
[PADIM 1.02 catalogue](https://profiles.opcfoundation.org/?pg=219) contains
56 server facets and 194 distinct conformance units: 65 are mandatory in at
least one facet, while 129 are optional everywhere they occur. An optional
unit does not become mandatory merely because another facet includes its
parent facet. In particular, Base Signal Calibration has four optional units
and no mandatory units.

`PadimDevice` contributes configured capabilities through the DI owner's
existing `IConformanceContributor` aggregation. A signal-family facet requires
a provider binding for its primary process variable, not merely its tag or a
loaded type. Measurement facets require the bound PA-DIM-qualified
`AnalogSignal` child; a same-type sibling does not satisfy that path.
Analytical facets additionally require the condition interfaces
and fields named by their mandatory family unit; four families also require
a complete calibration on one calibration instance. Where the facet requires
`PatMeasurementVariableType`, bind the generated `PatMeasurandDescription`
property with a `ChemicalSubstanceDataType` value. Generated defaults and a
primary measurement binding cannot substitute for these capabilities.

The following requirements are additional to the primary measurement and its
published VariableType. The names identify provider-bound members of
`SignalConditionSet` and a named calibration instance, respectively.

| Analytical family | Required condition fields | Required calibration fields |
| --- | --- | --- |
| Amperometric | `SensingElementTemperature` | `AmperometricSensorSlope`, `AmperometricSensorZeroPoint` |
| Conductivity | `ConductivityMeasuringMethod`, `TemperatureCompensationStyle`, `SensingElementTemperature` | `ConductivityCellConstant` |
| Fluorescence quenching | `SensingElementTemperature` | `OpticalFluorescenseQuenchingSensorSlope`, `OpticalFluorescenseQuenchingSensorZeroPoint` |
| pH | `PhMeasuringMethod`, `SensingElementTemperature` | `SensorSlope`, `SensorAsymmetryPotential` |
| NDIR | `SourceResidualLife`, `SampleCellTemperature` | None |
| Paramagnetic | `SampleTemperature`, `SensingElementTemperature` | None |
| Thermal conductivity, tunable diode laser | `SampleTemperature` | None |
| TOC | `DetectorZeroSignal`, `SourceResidualLife`, `SampleCellTemperature` | None |
| Zirconium dioxide, diode array, FTNIR/FTIR | `SensingElementTemperature` | None |
| Amperometric gas detector, gas chromatograph, Raman, infrared, catalytic bead | The family's declared condition interface and object; its individual fields are optional | None |
| Flame ionisation | No additional condition group required by the family unit | None |

`AmperometricGasDetectorSignal`, `GasChromatographSignal` and
`InfraredSensorSignal` do not require the PAT measurement unit. The separate
`InfraredSignal` facet does. Both infrared facet URIs retain their published
spelling.

Measurement and control facets also require acquisition for the mandatory
parameters of their VariableType. Temperature requires `SensorType`; flow and
its mass/actual-volume/normalized-volume specialisations require
`LowFlowCutOff`; totalizers require `PulseValue` and `PulseWidth`.
Continuous control requires `Setpoint`, `OperatingDirection` and `ActuatorType`;
two-state control requires `Setpoint` and `OperatingDirection`; multi-state
control requires `Setpoint`. An unbound generated default does not establish
support for these configuration parameters.

Device-family and Core facets require a health provider and all mandatory
base identity values: `Manufacturer`, `ManufacturerUri`, `Model`,
`SerialNumber`, `SoftwareRevision`, `HardwareRevision`, `ProductCode`,
`ProductInstanceUri`, `AssetId` and `RevisionCounter`. The SDK resolves
these identity members and `DeviceHealth` by their full DI-qualified
BrowseNames, including when namespace indexes are remapped.
The TOC analyser additionally requires its device-condition interface and
`ReactorTemperature`, `CoolerTemperature`, `CarrierGasVolumeFlow` and
`ReferenceInjectionVolume` bindings.
Administration, simulation and alarming facets follow their explicit
provider-backed configuration. Optional multi-field units require the complete
set on the same owning instance: for example, one gas-chromatograph signal's
expected retention time cannot be combined with another signal's actual
retention time. Disposing a device withdraws its contribution.

Evidence must distinguish the following checks rather than treating a passing
suite or a list of advertised URIs as verification of every published unit:

| Scope | Evidence and remaining limit |
| --- | --- |
| All 20 device selections and 26 signal selections | XML contract fixtures check inherited condition/calibration fields, interfaces, types, ranks, instance registration and IRDI identity. `HostedAllPublishedFamiliesDiscoverAndReadTypedFacetSamples` reads every configured acquisition sample through a client, including values, quality and source timestamps; it also checks mandatory identity and configured administration values. |
| All 12 measurement selections | Structural fixtures retain published types, numeric restrictions and units. The hosted client reads each primary value, required measurement parameter and structured PAT value; optional-only measurement properties are not all instantiated. |
| 56 facet URIs | `ConfiguredFamiliesCoverAllFiftySixReleasedFacetUris` checks the exact URI set. The hosted test reads `ServerProfileArray` and `ConformanceUnits` and compares their PA-DIM entries with the configured contributors. These tests do not constitute a 194-unit behavioral conformance test. |
| Simulation, authorization, operations and alarms | Runtime tests exercise accepted/rejected writes, cancellation, provider drain, simulation reversal, health-category transitions, acknowledgement and retained-condition refresh. Physical adjustment success remains the provider's responsibility. |
| Required family members | Contributor tests withhold incomplete pH and TOC configurations, unregistered dynamic members and unregistered ownership paths, and reject combining different calibration or signal instances. The hosted fixture checks that late-bound health and primary measurement nodes return `BadNodeIdUnknown` before registration, then browses and reads their exact provider samples after registration. |
| Runtime publication | `HostedRuntimeFacetChangesRequireExplicitPublication` starts with no PA-DIM facet, configures a discrete signal after startup, and reads both live arrays before and after explicit publication. It disposes the device and republishes to verify withdrawal; `ConformanceUnitsManager.IsSupported` is checked at each stage. |

Two catalogue inconsistencies remain unresolved. The `CatalyticBeadSensor`
facet lists seven optional `CatalyticBeadSignalType` condition units, but its
published device-condition declaration has neither that signal interface nor
those fields. The `InfraredSignalType PowerOnDurationSensor` and
`InfraredSignalType SensingElementResidualLife` descriptions both refer to
`SourceResidualLife`, although the NodeSet declares three distinct members.
The SDK retains the published model and uses the named members; it does not
invent device fields or treat these descriptions as interchangeable.

The PA-DIM Nano Embedded unit explicitly requires Nano Embedded Device 2017
for OPC UA v1.04 or Nano Embedded Device 2022 for OPC UA v1.05. The Core facet
therefore requires the host's `ServerProfileArray` to declare one of those
profiles or a Micro Embedded, Embedded UA or Standard UA profile from the
same year; the [published profile graph](https://profiles.opcfoundation.org/?pg=171)
includes the required Nano profile through mandatory inclusion edges.
A 2025-only declaration does not satisfy this check. Nano Embedded Device
2025 supersedes the 2022 profile, but makes Security ECC Policy optional,
whereas the 2022 profile requires it. Supersession alone does not establish
support for the profile named by PA-DIM. A host that implements the required
2017 or 2022 profile can declare it alongside its 2025 profile.

This checks the host's declaration; it does not verify the host's behavior
against every Nano Embedded requirement. Loading PA-DIM alone does not
establish host profile support. The hosted fixture exercises all configured
PA-DIM facet contributions and reads them back, but it does not independently test the
host's Nano Embedded behavior or every optional-only conformance unit. Keep the
rating at Partial until those evidence limits and the catalogue
interpretations above are resolved. No OPC Foundation certification is implied.
Physical calibration algorithms, field-protocol adapters, optional MCP tools
and UA Lens integration are outside this SDK runtime's scope.

Existing Machinery process-value simulation remains implemented by
`ProcessValueBuilder`; this runtime does not replace it.

Specification references:
[OPC 30081](https://reference.opcfoundation.org/specs/OPC-30081),
[analog variable semantics](https://reference.opcfoundation.org/specs/OPC-30081/8.2.2),
[FactoryReset](https://reference.opcfoundation.org/specs/OPC-30081/9.1) and
[positioner operation](https://reference.opcfoundation.org/specs/OPC-30081/9.3).
