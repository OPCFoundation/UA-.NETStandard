# Energy Consumption Management

The OPC 34100 runtime separates device measurements and power behavior from
their OPC UA representation. Applications supply measurement, standby or
Wake-on-LAN (WOL) providers; the server binds their results to the generated
ECM model. The reference providers simulate values and transitions without
changing host power or sending network packets.

The specification-support rating remains **Partial**. The standalone runtime
publishes E0, E1, E2, E3, D0 and D1 measurements with accuracy metadata. The
statistic members of E2, E3 and D1 reuse the shared Industrial Automation
(IA) statistics runtime from pull request #4988, which is not yet on `master`.
This change is prepared and reviewed against that exact prerequisite head only
and has not been built, executed or validated.

## Capability and conformance boundary

[OPC 34100 section 11](https://reference.opcfoundation.org/specs/OPC-34100/11)
requires at least one of measurement, standby or WOL for its base energy
management support. The SDK's completion scope includes all three.
`EcmPowerBinding` supplies standby with optional WOL; `EcmWolBinding`
supplies WOL without a standby graph. `EcmMeasurementBinding` publishes
provider-backed E0, E1, E2, E3, D0 or D1 measurements without requiring a Machinery
Consumption hierarchy.

| Published conformance units | Runtime surface | Current boundary |
|---|---|---|
| ECM Standby Management | `EcmPowerBinding`, `IEcmStandbyProvider`, generated mode/status nodes | Provider snapshots publish state and transition estimates; validation is pending. |
| ECM Standby Management Control | Generated method callbacks and the `PauseTime` write callback | Shared authorization, optional DI ownership and independent Call status/ReturnCode; validation is pending. |
| ECM Sleep Mode WOL | `EcmWolBinding`, `IEcmWolProvider`, `EcmWolClient` and generated power-off nodes | Independent or standby-composed WOL with explicit sleep requests and six-byte MAC metadata. Validation is pending. |
| ECM Energy Measurement | `EcmMeasurementBinding`, `IEcmMeasurementProvider`, generated measurement nodes | E0/E1/D0 acquisition, writable provider-backed ApplicationTag, quality/timestamps, engineering units and optional measurement IDs. |
| ECM EnergyProfile E0, E1, E2, E3, D0 and D1 | Generated interfaces and standalone provider binding | A snapshot declares every profile whose mandatory members are all present, with the published DataTypes. Partial member sets are rejected. E2/E3/D1 need an `IEcmStatisticsProvider`. Unexecuted. |
| ECM Energy Measurement Common DataTypes | Generated `AcPeDataType` and `AcPpDataType` | E0 and E3 preserve the exact typed `AcPeDataType` and `AcPpDataType`; E1, E2, D0 and D1 use Float, and E3 energy statistics use Double. Custom `IEncodeable` values are rejected. |
| ECM Accuracy Domain Percent full scale, Percent actual reading, IEC and EN | Generated domains and custom-domain creation | Bindings verify the referenced domain and declared `AccuracyClass`; applications can add custom `EnumValues`. This does not establish metrological certification. |
| ECM Energy Management Support | Application-selected standby, WOL or measurement configuration | No blanket ECM profile advertisement or Full rating is established by generated declarations. |

Section 11 lists `PauseTime` among methods, but section 7.2.1 and the
published NodeSet define a writable variable. The runtime follows that
variable contract.

### Statistics and metadata ownership

E2, E3 and D1 declare statistic members that the profile exposes with
`HasStatisticComponent`. `EcmMeasurementBinding` passes those members to the
shared `IaStatistics` runtime, which adds the collection `StartTime` and one
`ResetStatistics` method for the whole measurement point. The binding creates
no alternate statistics or reset service. A provider for these profiles
implements `IEcmStatisticsProvider`: it supplies `StatisticsStartTime` in each
snapshot, authorizes a reset and returns the committed statistics after it.
Reset runs inside the binding's serialized operation, so it cannot interleave
with a refresh or an ApplicationTag write. Authorization failure leaves the
provider and the published values unchanged.

The binding owns everything it publishes. Provider values and metadata are
detached before comparison or publication. `LocalizedText` keeps its deferred
text and locale and drops its translation dictionary and supported format
arguments. An unsupported mutable argument is rejected with `ArgumentException`
before it is formatted, before any value is published, and before a refresh
compares metadata; a failed refresh retains the last values and reports
`BadNoCommunication`. Engineering units and accuracy-domain classes are copied
field by field. Values are immutable scalars or the exact `AcPeDataType` and
`AcPpDataType`; a custom `IEncodeable`, a subclass or an array is rejected
because it cannot be detached safely. Statistic values are detached by the
shared runtime. `IaValueCopy` is internal to the IA assembly, so the ECM
binding detaches the exact AcPe and AcPp values field by field before it hands
them over; the shared runtime then detaches them again.
Who may reset statistics? The binding decides before the provider does. A reset
passes the same check as an ApplicationTag write: the supplied authorization
delegate, or the Operator, Engineer or Supervisor role when none is supplied,
and ownership of the DI lock when one is configured. Only a caller that passes
is offered to `AuthorizeStatisticsResetAsync`, so a provider cannot grant a
reset that the binding denies. The check observes cancellation but never
renews a lease or takes a lock.

Which measurements carry a `MeasurementPeriod`? None of the members of the
E0, E1, E2, E3, D0 and D1 profiles. The published MeasurementIDs catalogue
(OPC 34100 version 1.0.1) lists "-" for the period of each of them, and the
specification then requires that the property is omitted. The binding
therefore creates no `MeasurementPeriod`. Catalogue entries with a fixed or
user-defined period are not implemented.

How does a refresh publish one coherent snapshot? The binding opts into the IA
combined-publication option, so the statistics, `StartTime`, the instantaneous
readings and the ApplicationTag of a snapshot are validated and committed
together before any notification runs. A statistic sink therefore sees the new
instantaneous values, and a failing sink leaves the whole snapshot committed
while the measurements report `BadNoCommunication`. A reset supplies the
current instantaneous readings and their timestamps without further provider
I/O. The option also accepts a null value with Uncertain or Bad quality and
still rejects one with Good quality. The binding promises no lock, no commit
callback and no rollback of a provider reset.

Which combinations carry an AcPe or AcPp value through the shared runtime?
E3, and E2 together with E0, publish `AcPeDataType` or `AcPpDataType` as a
statistic or companion. The IA composition detaches exactly these two
generated structures through generated Float field copies; it accepts no other
`IEncodeable`, no `Clone` or `Encode` call and no pre-owned bypass. The binding
still rejects a subclass before the shared call, so a hostile override is never
invoked. The ECM model enables `ModelSourceGeneratorEcmFieldCopies` for this
path.

## Components and ownership

`Opc.Ua.ECM` owns the generated model and the typed control-result contract.
`Opc.Ua.ECM.Server` loads DI, IA and ECM for standalone hosting. Applications
with an existing DI owner can load the ECM model there and attach an
`EcmPowerBinding`, `EcmWolBinding` or `EcmMeasurementBinding` to generated
instances instead of creating another owner.
`Opc.Ua.ECM.Client` provides generated-proxy controls and model-derived
variable accessors over an existing session. `EcmMeasurementClient` discovers
direct measurement-point children, including subtypes, and reads E0-E3/D0/D1
values without discarding quality or timestamps. It also reads the statistics
`StartTime` and invokes the shared `ResetStatistics` method.

`IEcmStandbyProvider` supplies a coherent state snapshot and accepts
`StartPause`, `EndPause` and `SwitchToEnergySavingMode` requests.
`IEcmWolProvider` supplies WOL metadata and `SwitchOffWOL` independently of standby.
Accepting a request does not establish that the device has arrived.
When standby is present, its subsequent snapshots report the moving state until arrival.

`IEcmMeasurementProvider` supplies a snapshot containing a non-empty set of
supported profile readings and the current `ApplicationTag`. `CreateMeasurementAsync`
performs the first acquisition on a staged measurement point, then attaches it
beneath the selected parent or DI `DeviceSet` and registers the graph. The provider
remains application-owned; the manager owns and disposes the binding. Call
`RefreshAsync` to publish later snapshots. A published profile set and its
metadata cannot change during refresh. `ApplicationTag` writes are authorized,
persisted through the provider, and only then reflected in the address space.
The provider must return the updated tag in subsequent snapshots.

Good readings require the profile's declared scalar value: `AcPeDataType`
for E0 and Float for E1/D0. Bad or Uncertain readings accept the same scalar
type or `Variant.Null`; unrelated payloads, including ByteStrings and arrays,
are rejected before publication. A rejected refresh retains the previous
values and timestamps and marks their quality unavailable.
E0 accepts the exact generated `AcPeDataType`, whose fields are the three
Float values `L1`, `L2` and `L3`. CLR subclasses are rejected rather than
invoking a provider-controlled `Clone` override or discarding additional
subtype fields. Publishing custom structured subtypes is outside this
standalone provider contract.
Engineering units and accuracy-domain entries are copied into standard
`EUInformation` and `EnumValueType` instances using their declared fields,
without invoking a caller-supplied `Clone` override. Subtype-specific fields
are not part of this metadata contract.

`CreateAccuracyDomainAsync` adds an application-defined `AccuracyDomainType`
under the ECM `AccuracyDomains` folder. Each class requires a unique numeric
value and a unique, non-empty display name. A measurement that declares an
accuracy class must also identify a published domain whose `EnumValues`
contains that class. Accuracy metadata describes the provider's measurement;
it does not certify the instrument.
The manager supplies its node resolver to the binding. Direct
`EcmMeasurementBinding` users must supply `findNode` when readings reference
accuracy domains; the binding rejects those readings if the resolver is absent
or the domain is not published.

The manager owns bindings created by `CreatePowerAsync` or `CreateWolAsync`. Bindings borrow
providers and do not dispose them. Direct users must register the configured
node graph with its address-space owner, arrange periodic `RefreshAsync`
calls for standby and dispose each binding when that owner stops.
The WOL-only binding publishes fixed provider metadata and does not require periodic acquisition.
Providers own device I/O, persistence and recovery policy. Cancellation stops waiting; it does
not prove that a physical device reversed a request already accepted.

Manager shutdown rejects new creation operations and waits for
admitted creation, registration and rollback before releasing bindings.
A provider read already in progress must finish or observe the creation
caller's cancellation token; manager shutdown does not cancel that read.
Bindings cancel their own queued and active controls when they are disposed.
Disposal waits for admitted callbacks to finish before removing handlers;
a provider that ignores cancellation therefore delays disposal. Repeated
disposal observes the same completion, including cancellation-observer failures.
If creation fails, the manager attempts to dispose the staged binding, delete
its registered nodes and detach it from its parent. A cleanup failure is logged;
the caller still receives the original creation exception.

### Change notifications and same-binding operations

`EcmMeasurementBinding` serializes refreshes and bound `ApplicationTag`
writes. `EcmPowerBinding` serializes refreshes, bound `PauseTime` writes and
the bound `StartPause`, `EndPause`, `SwitchToEnergySavingMode` and
`SwitchOffWOL` controls. Each operation retains its serialized slot while
invoking synchronous node-change sinks and awaiting asynchronous sinks,
including notifications that mark retained values unavailable after a failure.

A notification sink on either binding's graph, including child nodes, must
not await or synchronously block on a same-binding refresh, bound write or
control, or disposal. Another operation cannot proceed until the active
operation releases its slot; disposal waits for the active operation to
finish. Waiting for either from the sink prevents that sink from completing.
Schedule any required follow-up outside the sink and observe its completion
and errors without making sink completion depend on it.

Synchronous sinks run inline and may block the caller before an asynchronous
binding operation returns. After node state is committed, notifications use
a token that cannot be canceled; canceling the caller's token does not
interrupt those notifications. A sink that does not return therefore also
delays operation completion and disposal.

## Standby contract

Durations use milliseconds, power uses kW and transition energy uses kWh.
Energy-saving mode IDs `0x00`, `0xF0`, `0xFE` and `0xFF` are reserved.
Ready-to-operate state uses `0xFF` for both source and destination;
WOL sleep uses `0xFE`.

`PauseTime` is a writable variable, not a method. A nonzero write follows
the same provider and authorization path as `StartPause`; zero follows
`EndPause`. Rejected writes do not replace the cached accepted pause.
Control is serialized per binding. Parallel reads remain available.

By default, control requires the Operator, Engineer or Supervisor role.
Applications can supply an asynchronous authorization policy. When the
optional DI `Lock` object is enabled, control also requires the calling
session to own it; sharing a user identity is not sufficient. Ownership
checks do not renew the DI lease.
Cancellation is checked before and after the application policy returns;
when either check observes cancellation, the request stops before device control.
WOL-only bindings apply the same role policy and can check ownership of their
own NodeId through an application-supplied DI lock service; they do not
publish a standby `Lock` object.

Applications using `DefaultLockService` must attach it once to the hosting
session manager with `AttachToSessionManager` and dispose it with their
other application-owned services. The binding does not own that service.
DI lock properties use authoritative reads; periodic ECM refreshes also
publish lease expiry and release for monitored properties. DI
`RemainingLockTime` is a Duration in milliseconds, although `LockState`
represents its remaining lease in seconds.

The provider publishes actual state, source quality and acquisition time.
A provider failure retains the last values with unavailable quality and
returns a bad Call status. It does not publish an invented successful
arrival. Applications must not automatically repeat a power command after
an uncertain disconnect.

## Call status and ReturnCode

ECM control has two independent result channels:

| Call status | Meaning | ReturnCode |
|---|---|---|
| Good | The device accepted the operation. | `0x00` |
| Uncertain | The request was processed but the device rejected it. | A nonzero device code |
| Bad | The operation could not be executed or was denied. | Not interpreted |

`EcmControlResult.ReturnCode` is absent for a bad Call status. The result
retains the selected/effective mode and every timing output for successful
or uncertain calls. A rejected `StartPause` returns zero for ModeID and
its timing outputs; a rejected `SwitchToEnergySavingMode` retains the
current mode as EffectiveModeID and returns zero timing outputs.
Standard device rejections include no suitable mode
(`0x50`), invalid mode ID (`0x52`) and unavailable internal device state
(`0x54`).
Accepted `SwitchOffWOL` results identify destination `0xFE`; rejected results
return zero ModeID and zero timing outputs.

## Fluent hosting and typed client access

The server sample registers a simulated provider through dependency injection
and creates its nodes after the ECM model has loaded:

```csharp
builder.Services.AddOpcUa().AddServer(options =>
{
    options.ApplicationName = "EcmSimulatedServer";
    options.EndpointUrls.Add("opc.tcp://localhost:62549/ECM");
})
.AddEcm()
.ConfigureEcm(async (manager, context) =>
{
    SimulatedEcmPowerProvider provider =
        context.GetRequiredService<SimulatedEcmPowerProvider>();
    EcmPowerBinding binding = await manager.CreatePowerAsync(
        new QualifiedName("SimulatedPower", manager.InstanceNamespaceIndex),
        provider,
        wolProvider: provider,
        cancellationToken: context.CancellationToken);
});
```

`AddClient(...).AddEcmClient()` registers `EcmClientFactory` over the
container-owned managed session. Factory consumers must not close or dispose
that session. Direct callers can instead construct `EcmClient` with the
session and the standby/WOL NodeIds. `DiscoverAsync` finds direct standby
children, including vendor subtypes, beneath a supplied parent; it does not
search every device recursively.

For WOL-only hosting, call `CreateWolAsync` from the same `ConfigureEcm`
callback with an `IEcmWolProvider`. Direct composition uses
`new EcmWolBinding(context, generatedPowerOff, provider)`.
Neither path creates a standby object. The client factory's
`CreateWolAsync` returns an `EcmWolClient`; its `DiscoverAsync` finds direct
WOL children by type, independently of standby discovery:

```csharp
await foreach (EcmWolClient discovered in EcmWolClient.DiscoverAsync(session, deviceSet, telemetry, ct))
{
    EcmWolClient wol = await factory.CreateWolAsync(discovered.PowerOff.ObjectId, ct);
    var mac = await wol.PowerOff.WOLMagicPacketVariable().ReadAsync(ct);
    if (mac.HasValue && StatusCode.IsGood(mac.DataValue.StatusCode))
    {
        Console.WriteLine($"MAC: {mac.Value}");
    }
    // Call only when the application intends to request device sleep.
    EcmControlResult result = await wol.SwitchOffWolAsync(ct);
    Console.WriteLine($"Call={result.Status}, ReturnCode={result.ReturnCode}");
}
```

Standalone measurements use the same manager and provider-ownership rules:

```csharp
ArrayOf<EnumValueType> accuracyClasses =
[
    new EnumValueType
    {
        Value = 0,
        DisplayName = new LocalizedText("Class 0")
    }
];
AccuracyDomainState accuracyDomain = await manager.CreateAccuracyDomainAsync(
    new QualifiedName("MeterAccuracy", manager.InstanceNamespaceIndex),
    accuracyClasses,
    ct);
// Configure measurementProvider so its readings reference accuracyDomain.NodeId.
EcmMeasurementBinding meter = await manager.CreateMeasurementAsync(
    new QualifiedName("ProductionMeter", manager.InstanceNamespaceIndex),
    measurementProvider,
    parent: device,
    cancellationToken: ct);
await meter.RefreshAsync(ct);
```

`measurementProvider` returns E0, E1, E2, E3, D0 or D1 readings with namespace-qualified
DataTypes, UTC acquisition timestamps and optional engineering units,
measurement IDs and accuracy metadata. It must retain successful
`ApplicationTag` writes as its source of truth.

The typed client exposes generated-variable accessors over the
`EnergyMeasurementTypeClient` proxy:

```csharp
await foreach (EcmMeasurementClient meter in EcmMeasurementClient.DiscoverAsync(
    session, deviceSet, telemetry, ct))
{
    var reading = await meter.AcCurrentPe.ReadAsync(ct);
    if (reading.HasValue)
    {
        Console.WriteLine(
            $"{reading.Value}: {reading.DataValue.StatusCode}, " +
            $"{reading.DataValue.SourceTimestamp:O}");
    }
}
```

The client uses additive generated `WithStatusAsync` methods. Existing
generated method signatures and behavior remain unchanged; only the ECM
model enables the status variants. Bad results contain no interpreted
device outputs, and inconsistent status/return-code or timing outputs
produce a decoding error.

```csharp
EcmControlResult started = await client.StartPauseAsync(10000, ct);
if (StatusCode.IsGood(started.Status))
{
    // Acceptance is distinct from the subsequent physical arrival.
    var status = await client.Standby.StandbyManagementStatusVariable().ReadAsync(ct);
    if (status.HasValue)
    {
        Console.WriteLine($"{status.Value}: {status.DataValue.StatusCode}");
    }
}

StatusCode written = await client.Standby.PauseTimeVariable().WriteWithStatusAsync(0, ct);
Console.WriteLine($"PauseTime write status: {written}");
await foreach (var status in client.Standby.StandbyManagementStatusVariable()
    .ObserveAsync(cancellationToken: ct))
{
    if (status.HasValue)
    {
        Console.WriteLine($"{status.Value}: {status.DataValue.SourceTimestamp:O}");
    }
}
```

Variable accessors retain quality and both timestamps. Inspect `HasValue`
before interpreting a decoded value, and inspect its quality before using
it for a process decision. WOL sleep is an explicit client operation; the
client package provides no wake-packet transport.
Use `WriteWithStatusAsync` when a PauseTime write must distinguish an
Uncertain device rejection from an accepted write. The original `WriteAsync`
signature and behavior remain available for other consumers.

## Simulated sleep and recovery

`WOLMagicPacket` contains exactly six MAC address bytes. It is not a
preassembled magic packet. A production device can disconnect its OPC UA
server after accepting `SwitchOffWOL`; an application-supplied wake
transport is responsible for sending any later wake request.

`SimulatedEcmPowerProvider` keeps the server reachable. Its explicit
`SimulateWakeAsync` operation advances simulated recovery only; it sends
no traffic. The provider chooses the lowest-power eligible configured mode
for a requested pause and rejects conflicting changes during entry.
Repeated requests for the same destination retain the original arrival
time. `EndPause` includes unfinished entry, minimum stay and recovery
in its estimate.
The requested pause determines mode eligibility; the reference provider
does not schedule an automatic `EndPause` when that duration elapses.
Explicit control remains necessary to end a simulated pause.
Transition estimates use elapsed time rather than a calendar arrival timestamp,
so a finite duration can extend beyond the clock's remaining calendar range.
The simulator rejects mode configurations whose combined entry, minimum stay
and recovery would produce an infinite estimate.

The [sample](../samples/ECM/README.md) publishes simulated E0-E3/D0/D1
measurements, including a resettable statistics point, and can demonstrate a complete sleep and recovery sequence with
the explicit `--simulate-wake-after-ms` server option. Without that option,
the sample does not initiate simulated wake.

## Specification references

- [Standby concepts](https://reference.opcfoundation.org/specs/OPC-34100/6.3)
- [Standby methods and PauseTime writes](https://reference.opcfoundation.org/specs/OPC-34100/7.2.1)
- [Mode characteristics and reserved IDs](https://reference.opcfoundation.org/specs/OPC-34100/7.2.4)
- [WOL metadata and control](https://reference.opcfoundation.org/specs/OPC-34100/7.3.1)
- [State and transition structures](https://reference.opcfoundation.org/specs/OPC-34100/9)
