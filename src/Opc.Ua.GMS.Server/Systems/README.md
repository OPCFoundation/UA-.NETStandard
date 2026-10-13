# Native GMS systems

`GmsBuilder` composes one generated `GMSState` with the existing MachineTool
runtime, the same Machinery/DI owner, and `WithGmsResults`. It does not register
the root twice or implement another result, production, file, or subscription
protocol.

## Mandatory defaults and explicit capabilities

Every instance requires its own `IGmsProvider`, identification, and
`IGmsResultStore`. There is no global provider lookup or implicit result store.
Defaults publish the mandatory native equipment, identification, monitoring,
notification, retained active-program, and result-management structure.
Unacquired measurements have bad quality, not successful zero values.

Optional features are selected through `GmsSystemOptions.Features`. A selected
surface needs a corresponding provider observation. An explicitly empty
`ArrayOf<T>` is different from a null array:

- `Sensors` accepts actual `SensorType` and `MultiSensorType` observations.
  Single sensors always have the generated Double `Qualified` life variable.
  Its indication is `Time`, its unit is hours, and `IsCountingUp` is false.
  The provider supplies remaining time; the server does not restart or invent
  a countdown. Negative remaining time after expiry is not clamped.
- `Identification` selects native Cartesian or cylindrical workspace structures
  and a device subclass. The released workspace structures contain no unit
  member; the runtime does not add a fictitious one.
- `Loading` and `ToolMonitoring` publish observed loading state and actual
  active-tool references. Local keys resolve against the prospective inventory.
  Foreign, removed, duplicate, and unknown tool references are rejected.
- `Accessories` and `AdditionalSensors` use the generated rotary-table,
  sensor-rack, tip-rack, and analog-sensor types. Accessories retain the declared
  base `MachineryItemIdentificationType` AddIn and use the shared identity writer.
- `Calibration` uses actual `CalibrationPrognosisType`, including state, last
  date, interval, preparation lead time, prediction, and certificate-reference
  samples. References are not downloaded. `CalibrationHistory` is a bounded
  application-side observation history, not a new standard history service.
- `Production` supplies actual `GMSJobType` and `GMSPartType` factories to the
  retained MachineTool production module. Native measurement reason, durations,
  batch information, part properties, and Number-valued catalogs are preserved.
  Inherited statistics can be selected with `WithMachineToolFeatures`.
- `SensorWarnings` publishes actual `SensorWarningAlarmType` conditions through
  the standard condition lifecycle. Acknowledgement is not an equipment reset.
  Warning identities remain modeled when inactive; ordinary enable, disable,
  comment, and acknowledgement permissions come from the provider.

`WithPrognoses` composes the existing ten generic forecasts with calibration.
`WithNotifications` selects the existing MachineTool operator-message and alert
provider. Refresh that independent provider with `RefreshNotificationsAsync`.
`WithMachinery` permits further shared bindings on the same unpublished root;
its callback must not build or dispose the supplied builder.

## Provider example

```csharp
DateTimeUtc acquired = TimeProvider.System.GetUtcNow().UtcDateTime;
var provider = new InMemoryGmsProvider(new GmsSystemSnapshot
{
    Sensors =
    [
        new GmsSensor
        {
            Tool = new MachineToolTool
            {
                Key = "Probe",
                Control = new MachineToolToolControl(
                    7, ToolManagement.NumberBased, false, ToolLocked.CapabilityUnavailable),
                Sample = new MachineToolSampleInfo(acquired, StatusCodes.Good)
            },
            Class = new DataValue(Variant.From(3u), StatusCodes.Good, acquired),
            Qualified = new GmsQualifiedLife
            {
                RemainingHours = new DataValue(Variant.From(2.5), StatusCodes.Good, acquired)
            }
        }
    ]
});
var store = new InMemoryGmsResultStore(context.Context.AsMessageContext());
await using GmsHandle machine = await context.AddGms("InspectionCell")
    .WithProvider(provider)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example manufacturer");
        identity.SerialNumber = "CMM-001";
        identity.ProductInstanceUri = "urn:example:inspection:CMM-001";
    })
    .WithResults(store)
    .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Sensors })
    .BuildAsync(cancellationToken);
```

Use `DataValue.Null` only to omit an optional field. Use a non-null `DataValue`
with a bad status for a modeled but unavailable field. Optional-field presence
is fixed within a slot. Accessory and analog-sensor slots are static; dynamic
tool and retained-job plans use the explicitly selected shared inventory
features. Provider graphs must remain immutable and acquisitions must honor
cancellation. In-memory replacement assigns a new revision; an unchanged
acquisition retains the same shared snapshot identity.

Native and inherited validation and topology preparation happen before the
shared value commit. Standard condition updates and event reporting run outside
the shared commit gate. Failed publication retains the accepted observation and
event identity for `RetryPublicationAsync`; it is not a rollback of accepted
equipment data. Disposal, including direct deletion by the shared owner,
cancels and joins admitted native operations and releases only that root's
bindings.

## Hosting and clients

Use `AddGms` / `ConfigureGms` for standalone convenience, or
`AddGmsToMachinery` / `ConfigureGmsFor<TNodeManager>` with an existing compatible
owner. The owner must select Machinery building blocks and results. Model
loading retains the exact GMS + MachineTool + Machinery Result dependency
closure; it does not enable WMTP.

`session.CreateGmsClient(telemetry)` creates a caller-session-owned facade.
`AddGmsClient` registers `IGmsClientFactory`; sessions explicitly opened by that
factory are disposed with the facade.

```csharp
await using GmsClient gms = session.CreateGmsClient(telemetry);
ArrayOf<GmsDeviceEntry> systems = await gms.DiscoverAsync(cancellationToken);
GmsSystemReadSnapshot state = await gms.ReadSnapshotAsync(machineId, cancellationToken);
GmsEquipmentReadSnapshot equipment = await gms.ReadEquipmentAsync(machineId, cancellationToken);
GmsCalibrationReadSnapshot? calibration =
    await gms.ReadCalibrationAsync(machineId, cancellationToken);
GmsProductionReadSnapshot production = await gms.ReadProductionAsync(machineId, cancellationToken);
```

Native readings retain actual NodeIds, type definitions, optional presence,
numeric representations, status, and timestamps. Multi-request reads are not
claimed to be atomic histories. Inventory and payload bounds reject rather than
truncate. The shared browser owns continuation cleanup. Bounded data, event,
and retained-condition observations reuse the MachineTool subscription
collector; native sensor-warning fields use the generated decoder.
`GetResults(machineId)` returns the existing GMS result workflow on the same
session, and `GetProxy(machineId)` exposes the generated advanced proxy.

## Conformance boundaries

Live contributions use `IConformanceContributor` and the selected owner's
registration mechanism. CUs depend on configured, committed instances and
providers, not namespace presence. Secure profiles also require actual endpoint
policies, certificates, and certificate user-token capability.

Only the complete GMS Production facet is withheld for the upstream
`GMS GMS_ProductionStatisticsType` inconsistency: section 12.3.2.7 requires it,
but section 12.2 and the released normative NodeSet define no such type or NodeId.
Defined GMS job/part behavior and inherited production statistics are still
implemented. The model's `Qualified/IsCountingUp=true` default is retained in
the canonical XML but overridden to the required countdown at runtime. The
sensor-warning implementation uses the actual released `SensorWarningAlarmType`
(`i=1010`), not the misspelled prose event name.

GMS version 1 production, calibration, and measurement state are observations.
This API introduces no GMS acquisition, calibration-execution, job-start, or
correction-control method.
