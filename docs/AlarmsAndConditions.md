# Alarms and Conditions (OPC UA Part 9)

This guide covers the stack's OPC UA Part 9 *Alarms & Conditions* support.
It explains how to build alarm-aware servers and consume alarm events on
clients. It also introduces the helpers for latched alarms, alarm groups,
suppression, alarm metrics, and streaming alarm records.

For the formal model, see
[OPC UA Part 9 — Alarms and Conditions](https://reference.opcfoundation.org/specs/OPC-10000-9/full).

- [Quick reference](#quick-reference)
- [Server side](#server-side)
  - [Creating an alarm](#creating-an-alarm)
  - [Driving state from your process](#driving-state-from-your-process)
  - [Latched alarms](#latched-alarms)
  - [Re-alarming](#re-alarming)
  - [Audible alarms and silencing](#audible-alarms-and-silencing)
  - [Suppression, out-of-service, shelving](#suppression-out-of-service-shelving)
  - [Filtered retain](#filtered-retain)
  - [Alarm groups and first-in-group](#alarm-groups-and-first-in-group)
  - [Alarm metrics (rate tracking)](#alarm-metrics-rate-tracking)
  - [Vetoing alarm operations](#vetoing-alarm-operations)
  - [Audit events](#audit-events)
- [Client side](#client-side)
  - [`AlarmClient` — typed operations](#alarmclient--typed-operations)
  - [Subscribing to alarms with `IAsyncEnumerable`](#subscribing-to-alarms-with-iasyncenumerable)
  - [Typed alarm records](#typed-alarm-records)
  - [Per-record event filters](#per-record-event-filters)
  - [Dialog conditions](#dialog-conditions)
  - [`ConditionRefresh`](#conditionrefresh)
- [Reference](#reference)

## Quick reference

| Concern | Server entry point | Client entry point |
|---|---|---|
| Create an alarm | `new AlarmConditionState(telemetry, parent); alarm.Create(...)` | n/a |
| Acknowledge / Confirm | `AcknowledgeableConditionState.OnAcknowledgeCalled` (auto-wired) | `AlarmClient.AcknowledgeAsync` / `ConfirmAsync` |
| Silence | `AlarmConditionState.SetSilenceState` | `AlarmClient.SilenceAsync` |
| Suppress / Unsuppress | `SetSuppressedState` | `AlarmClient.SuppressAsync` / `UnsuppressAsync` |
| Out-of-service | `SetOutOfServiceState` | `AlarmClient.RemoveFromServiceAsync` / `PlaceInServiceAsync` |
| Latched + Reset | `SetLatchedState` + auto from `SetActiveState` | `AlarmClient.ResetAsync` |
| Shelve / Unshelve | `SetShelvingState` | `AlarmClient.TimedShelveAsync` / `OneShotShelveAsync` / `UnshelveAsync` |
| Re-alarm | `ProcessReAlarm` | n/a (server timer) |
| Filtered retain | `ConditionState.SupportsFilteredRetain` | n/a (per-subscription) |
| Alarm groups | `AlarmGroup`, `AlarmSuppressionEngine` | `AlarmClient.GetGroupMembershipsAsync` |
| Alarm rate | `AlarmRateTracker` | read `AlarmMetricsType` attributes |
| Refresh state | `Server.ConditionRefresh` (server-driven) | `AlarmClient.ConditionRefreshAsync` / `ConditionRefresh2Async` |
| Stream alarm events | n/a | `IStreamingSubscription.SubscribeAlarmsAsync` |
| Decode raw event fields | n/a | `EventRecordDecoderRegistry.Default.Decode` |
| Build an event filter | n/a | `{Type}Record.EventFilters.Build(registry?)` |

Create `AlarmClient` from any `ISession` and a telemetry context. It delegates
each Part 9 method call to the matching source-generated `*TypeClient` proxy.
The generated proxy is the single source for each method NodeId:

```csharp
AlarmClient alarms = session.GetAlarmClient(telemetry);
```

Dependency-injected hosts can call the
[`builder.AddAlarms()`](DependencyInjection.md#alarms-and-conditions)
extension and resolve `AlarmClientFactory`. The factory passes the host's
telemetry context to the client:

```csharp
var factory = sp.GetRequiredService<AlarmClientFactory>();
AlarmClient alarms = factory.Create(session);
```

If you already have a typed handle to a specific condition node you
can also construct a proxy directly — `AlarmClient` is a convenience
façade over these:

```csharp
await new AlarmConditionTypeClient(session, conditionId, telemetry)
    .AcknowledgeAsync(eventId, comment);
```

## Server side

Fluent-created alarms have distinct per-instance NodeIds for their entire
subtree, including condition Methods and state properties. Configuring
`WithLimits` materializes the standard typed limit properties, assigns
instance identifiers, and registers them for browse/read access. Repeated
limit configuration updates the same property in place; omitted limits
remain absent.

### Creating an alarm

`AlarmConditionState` is the central server-side state type for all
Part 9 alarms. It is *source-generated* from the standard NodeSet and
extended with hand-written behavior. The behavior partial file
(`src/Opc.Ua.Core.Types/State/AlarmConditionState.Methods.cs`) wires
every Part 9 method handler during `OnAfterCreate`, so as soon as you
populate the optional state nodes the corresponding methods are
callable.

```csharp
var alarm = new AlarmConditionState(telemetry, parent);
alarm.Create(
    context,
    nodeId: NodeId.Null,         // server-assigned
    browseName: new QualifiedName("MyAlarm"),
    displayName: null,
    assignNodeIds: true);

alarm.SetEnableState(context, enabled: true);
alarm.SetSeverity(context, EventSeverity.High);
alarm.SetActiveState(context, active: true);
alarm.ReportEvent(context, alarm);
```

Before calling `Create`, assign any optional state nodes to properties such as
`alarm.SilenceState`, `alarm.OutOfServiceState`, and `alarm.LatchedState`.
The quickstart reference server creates these nodes through
`AlarmConditionTypeHolder.Initialize` in
`samples/Quickstarts.Servers/Alarms/AlarmHolders/`.

### Driving state from your process

All state transitions go through typed setters that:

- update the `TwoStateVariableState` value + `Id`
- stamp `TransitionTime`
- recompute `EffectiveDisplayName` and the composite `SuppressedOrShelved` flag
- clear `ChangeMasks` so the next publish cycle sees the new state

| Setter | Notes |
|---|---|
| `SetEnableState(context, enabled)` | Inherited from `ConditionState`. Disabling clears `Retain` per Part 9 §5.5.2. |
| `SetSeverity(context, severity)` | Records `LastSeverity` before updating. |
| `SetActiveState(context, active)` | On *true*: if `LatchedState` is present, sets it true; if `SilenceState` is present and silenced, clears it. On *false*: if shelved as `OneShotShelve`, unshelves. |
| `SetSuppressedState(context, suppressed)` | Updates `SuppressedOrShelved` taking `OutOfServiceState` and `ShelvingState` into account. |
| `SetOutOfServiceState(context, outOfService)` | Sets `SuppressedOrShelved` true when out of service (Part 9 §5.8.2). |
| `SetShelvingState(context, shelved, oneShot, shelvingTime)` | Drives the `ShelvedStateMachineState`, runs the unshelve timer, computes `UnshelveTime`. |
| `SetLatchedState(context, latched)` | Direct latched-state setter; usually called automatically from `SetActiveState`. |
| `SetSilenceState(context, silenced)` | Direct silence-state setter; usually called automatically on activation / re-alarm. |

### Latched alarms

If you populate `alarm.LatchedState`, the alarm becomes a *latching
alarm* (Part 9 §4.8). The semantics:

- `SetActiveState(context, true)` — also sets `LatchedState = true`.
- `SetActiveState(context, false)` — `ActiveState` reflects the real
  process state, but `LatchedState` stays `true`.
- `Reset` (server-side method, auto-wired) — clears `LatchedState`.
  `Reset` accepts the request only when the alarm is enabled, inactive, and
  acknowledged, and when it is confirmed if `ConfirmedState` is present.
  Otherwise, it returns `Bad_InvalidState`.

The server retains latched alarms (`Retain = true`) while `LatchedState.Id`
is `true`, so clients can see them during a refresh.

### Re-alarming

A re-alarm reminder fires when an alarm has been active and
unacknowledged longer than `ReAlarmTime`. The state type provides a
helper rather than an automatic timer (so the host owns scheduling and
event generation):

```csharp
// In your re-alarm scheduler (e.g. an external Timer)
if (alarm.IsReAlarmEnabled && alarm.ActiveState.Id.Value
    && alarm.AckedState?.Id.Value != true)
{
    alarm.ProcessReAlarm(context);
}

// On deactivation / acknowledge:
alarm.ResetReAlarmRepeatCount(context);
```

`ProcessReAlarm` performs these actions:

- clears `AckedState` (forces re-acknowledgement)
- clears `SilenceState` (audible annunciation resumes)
- increments `ReAlarmRepeatCount`
- calls `ReportStateChange` to publish the new event

### Audible alarms and silencing

When `AudibleEnabled = true` and `AudibleSound` has a value,
`UpdateAudibleState` clears the silence state on activation. The next
activation is then audible:

```csharp
ByteString sound = LoadWavFile();
alarm.UpdateAudibleState(context, active: true, soundData: sound);
```

`SilenceAsync` from the client (or the `Silence` method handler on the
server) sets `SilenceState.Id = true`.

### Suppression, out-of-service, shelving

All three states contribute to the `SuppressedOrShelved` boolean. The
state-type setters keep the flag in sync. Clearing one state does **not**
clear `SuppressedOrShelved` while another state remains active:

```csharp
alarm.SetSuppressedState(context, true);      // SuppressedOrShelved = true
alarm.SetOutOfServiceState(context, true);    // SuppressedOrShelved stays true
alarm.SetSuppressedState(context, false);     // SuppressedOrShelved stays true
alarm.SetOutOfServiceState(context, false);   // SuppressedOrShelved = false
```

### Filtered retain

Part 9 B.1.4 lets a server set a client-specific `Retain` flag based on
that client's event filter. When a condition no longer matches a client's
where clause, the server sends that client a final event with
`Retain = false`. The alarm remains active for everyone else.

Opt a condition in by setting `SupportsFilteredRetain`:

```csharp
alarm.SupportsFilteredRetain =
    PropertyState<bool>.With<VariantBuilder>(alarm, value: true);
```

Notes on the shape of that property:

* Part 9 provides `SupportsFilteredRetain` **on the ConditionType
  only** — the standard nodeset declares no modelling rule for it, so
  condition instances do not carry it as a child. The C# property is
  therefore the server side switch, not an address space node:
  `FindChild(BrowseNames.SupportsFilteredRetain)` on an instance
  returns null by design. Clients read the flag from the type node,
  which the generated address space already builds.
* Branches inherit the parent's setting — `CreateBranch` copies it,
  since nothing that walks the instance children would.
* Each condition and branch is tracked separately, so one branch
  leaving filter scope does not affect its siblings or its parent.
* `ConditionRefresh` reevaluates refreshed conditions against the where
  clause and primes the tracking again.
* Changing a monitored item's where clause with `ModifyMonitoredItems`
  discards tracking for the previous filter. Durable subscriptions preserve
  tracking across a restart.
* Each client receives a trailing event with `Retain = false` (Part 9,
  5.5.2, Figure 11), regardless of the server's `Retain` value. Otherwise,
  the client would keep an alarm that it should drop. The override applies
  only to that client's event fields; it leaves the shared event snapshot
  unchanged. Other clients whose filters still include the condition keep
  receiving the server's actual value.

### Alarm groups and first-in-group

`src/Opc.Ua.Server/Alarms/AlarmGroup.cs` wraps a generated
`AlarmGroupState` and provides typed add/remove/enumerate:

```csharp
var group = new AlarmGroup(motorAlarmGroupState);
group.AddMember(motorHighTempAlarm);
group.AddMember(motorLowOilAlarm);

foreach (NodeId id in group.GetMemberIds(context))
{
    // ...
}
```

`AlarmSuppressionEngine` centralizes both the **AlarmSuppressionGroup**
pattern and the **FirstInGroup** pattern. Register on startup, call
`Evaluate` from your simulation/process loop, and the engine routes
suppression to the right alarm members:

```csharp
using var engine = new AlarmSuppressionEngine();

engine.RegisterSuppressionGroup(
    suppressionGroup: motorShutdownGroup,
    suppressionSource: () => motorIsShutDown.Value,
    alarmMembers: new[] { motorHighTempAlarm, motorLowOilAlarm });

engine.RegisterFirstInGroupAlarm(
    firstAlarm: masterTripAlarm,
    group: tripGroup,
    otherMembers: dependentTripAlarms);

// In your periodic update:
engine.Evaluate(context);

// On master trip activation:
engine.OnFirstInGroupActiveChanged(context, masterTripAlarm, tripGroup,
    firstActive: true);
```

The first `Evaluate` call applies the current state. Clients therefore see
a coherent suppression state immediately after registration; no state
transition is required.

> **Live demo:** The reference implementation in
> `samples/Quickstarts.Servers/Alarms/AlarmNodeManager.cs` exposes an
> `/Alarms/AnalogGroup` (`AlarmGroupType`) containing every analog-source
> alarm and a writable `/Alarms/MaintenanceMode` boolean. Writing `true` to
> `MaintenanceMode` runs `AlarmSuppressionEngine.Evaluate(...)` and
> suppresses every group member. Writing `false` clears suppression on the
> next evaluation. The reference node manager derives from
> `AsyncCustomNodeManager`, so this file also demonstrates the modern async
> base class.

### Alarm metrics (rate tracking)

`AlarmRateTracker` records activations into a sliding window and
exposes `CurrentAlarmRate` / `MaximumAlarmRate` suitable for surfacing
through an `AlarmMetricsType` instance:

```csharp
var tracker = new AlarmRateTracker(TimeSpan.FromMinutes(1));

alarm.OnSilenceRequested = (ctx, a) =>
{
    tracker.RecordActivation();
    return ServiceResult.Good;
};

// Periodically push to AlarmMetrics:
metrics.CurrentAlarmRate.Value = tracker.CurrentAlarmRate;
metrics.MaximumAlarmRate.Value = tracker.MaximumAlarmRate;
```

### Vetoing alarm operations

Each Part 9 alarm method has an *optional* delegate that runs **before**
the default state transition. Returning a `Bad` status from the
delegate aborts the operation; returning `Good` (or `null`) lets the
default behavior run:

| Delegate | Triggered by |
|---|---|
| `alarm.OnSilenceRequested` | `Silence` method |
| `alarm.OnSuppressRequested(suppressing: bool)` | `Suppress` / `Unsuppress` |
| `alarm.OnOutOfServiceRequested(outOfService: bool)` | `RemoveFromService` / `PlaceInService` |
| `alarm.OnResetRequested` | `Reset` (latched alarms) |
| `alarm.OnShelve` | `OneShotShelve` / `TimedShelve` / `Unshelve` (existing) |

```csharp
alarm.OnResetRequested = (ctx, a) =>
{
    if (!hardwareDiagnosticPassed)
    {
        return new ServiceResult(StatusCodes.BadUserAccessDenied,
            "Hardware diagnostic must pass before reset.");
    }
    return ServiceResult.Good;
};
```

### Audit events

Every Part 9 alarm method generates the spec-mandated audit event
type automatically. You do not have to call `ReportEvent` for the
audit event — it happens inside the method handler when
`AreEventsMonitored` is true:

| Method | Audit event type |
|---|---|
| `Silence` | `AuditConditionSilenceEventType` |
| `Suppress` / `Unsuppress` / `*2` | `AuditConditionSuppressionEventType` |
| `RemoveFromService` / `PlaceInService` / `*2` | `AuditConditionOutOfServiceEventType` |
| `Reset` / `Reset2` | `AuditConditionResetEventType` |
| Existing: `Acknowledge` / `Confirm` | `AuditConditionAcknowledgeEventType` / `AuditConditionConfirmEventType` |
| Existing: shelving | `AuditConditionShelvingEventType` |

## Client side

### `AlarmClient` — typed operations

`AlarmClient` is the strongly-typed client API for Part 9 methods.
Each method delegates to the matching source-generated proxy:
`ConditionTypeClient`, `AcknowledgeableConditionTypeClient`,
`AlarmConditionTypeClient`, `DialogConditionTypeClient`, or
`ShelvedStateMachineTypeClient`. The proxy receives the caller-supplied
`conditionId` as its `ObjectId`, following the Part 9 §5.5.4 idiom that
accepts `ConditionId` as `ObjectId`. The generated proxy remains the single
source for each method NodeId and argument shape.

```csharp
AlarmClient alarms = session.GetAlarmClient(telemetry);

// ConditionType methods
await alarms.EnableAsync(conditionId);
await alarms.DisableAsync(conditionId);
await alarms.AddCommentAsync(conditionId, eventId,
    new LocalizedText("en", "Looks like flow sensor drift"));
await alarms.ConditionRefreshAsync(subscriptionId);
await alarms.ConditionRefresh2Async(subscriptionId, monitoredItemId);

// AcknowledgeableConditionType
await alarms.AcknowledgeAsync(conditionId, eventId,
    new LocalizedText("en", "Operator review complete"));
await alarms.ConfirmAsync(conditionId, eventId,
    new LocalizedText("en", "Maintenance verified"));

// AlarmConditionType
await alarms.SilenceAsync(conditionId);
await alarms.SuppressAsync(conditionId,
    comment: new LocalizedText("en", "Routine maintenance"));
await alarms.UnsuppressAsync(conditionId);
await alarms.RemoveFromServiceAsync(conditionId);
await alarms.PlaceInServiceAsync(conditionId);
await alarms.ResetAsync(conditionId);                            // latched-alarm reset
await alarms.TimedShelveAsync(conditionId, shelvingTime: 30000); // 30s
await alarms.OneShotShelveAsync(conditionId);
await alarms.UnshelveAsync(conditionId);

ArrayOf<NodeId> groups = await alarms.GetGroupMembershipsAsync(conditionId);
```

The `*Async(... comment ...)` overloads call the spec-defined `*2` method
when the comment is non-empty: `Suppress2`, `Unsuppress2`,
`RemoveFromService2`, `PlaceInService2`, and `Reset2`.

### Subscribing to alarms with `IAsyncEnumerable`

Alarm events flow through the [streaming subscription
API](Subscriptions.md#streaming-subscriptions). The
`AlarmStreamExtensions.SubscribeAlarmsAsync` extension returns
strongly-typed records:

```csharp
ManagedSession session = ...;
IStreamingSubscription streaming = session.DefaultStreaming;

await foreach (ConditionTypeRecord record in streaming
    .SubscribeAlarmsAsync(notifierId: ObjectIds.Server, ct: ct)
    .ConfigureAwait(false))
{
    switch (record)
    {
        case ExclusiveLimitAlarmTypeRecord limit:
            Console.WriteLine($"{limit.SourceName} limit-state-id={limit.LimitState}");
            break;
        case AlarmConditionTypeRecord alarm when alarm.ActiveStateId == true:
            await alarms.AcknowledgeAsync(alarm.ConditionId,
                alarm.EventId,
                new LocalizedText("en", "Auto-ack")).ConfigureAwait(false);
            break;
        case DialogConditionTypeRecord dialog:
            // Pick a response index from dialog.ResponseOptionSet
            await alarms.RespondAsync(dialog.ConditionId,
                selectedResponse: 0).ConfigureAwait(false);
            break;
    }
}
```

State-machine waits compose naturally with
`TakeUntilAsync` / `WithTimeoutAsync`:

```csharp
// Wait for myAlarm to clear, or 5 minutes — whichever comes first.
await streaming.SubscribeAlarmsAsync(ObjectIds.Server)
    .TakeUntilAsync(r =>
        r is AlarmConditionTypeRecord a && a.ConditionId == myAlarmId &&
        a.ActiveStateId == false)
    .WithTimeoutAsync(TimeSpan.FromMinutes(5))
    .LastAsync(ct);
```

### Typed alarm records

Alarm and condition records are **source-generated** by the
`Opc.Ua.SourceGeneration` analyzer (see the `EventRecordGenerator`).
For every `ObjectType` derived from `BaseEventType`, the generator emits
a `partial record {Type}Record`. Each generated record derives from its
parent record and exposes one init-only property for each declared field.
The standard NodeSet produces the following hierarchy, abridged to show
the most commonly observed types:

```
EventRecord                              (anchor; hand-written)
└── BaseEventTypeRecord                  (i=2041)
    └── ConditionTypeRecord              (i=2782)
        ├── DialogConditionTypeRecord    (i=2830)
        └── AcknowledgeableConditionTypeRecord
            └── AlarmConditionTypeRecord
                ├── LimitAlarmTypeRecord
                │   ├── ExclusiveLimitAlarmTypeRecord
                │   └── NonExclusiveLimitAlarmTypeRecord
                ├── DiscreteAlarmTypeRecord
                │   └── OffNormalAlarmTypeRecord
                │       └── CertificateExpirationAlarmTypeRecord
                └── DiscrepancyAlarmTypeRecord
```

Vendor models that derive from these types automatically get their own
`*TypeRecord`, which derives from the closest standard ancestor. No
checked-in code or manual class definition is needed. Add a hand-written
`partial record VibrationAlarmTypeRecord` to your project to extend the
generated declaration with computed properties or custom helpers.

The decoder upgrades the record type based on which fields are
populated in the event. A simple `switch` on the record type gives you
the right field set:

```csharp
EventRecord? record = EventRecordDecoderRegistry.Default.Decode(eventFields);

if (record is CertificateExpirationAlarmTypeRecord cert)
{
    Console.WriteLine($"Cert {cert.CertificateType} expires {cert.ExpirationDate}");
}
```

The shared `ConditionTypeRecord.ConditionId` property is a hand-written
alias for `SourceNode`. Part 9 defines `ConditionId` as the NodeId of the
condition object that raised the event. The event reports this NodeId in
the `SourceNode` field.

### Source-generated decoders + `EventRecordDecoderRegistry`

Every record emitted by `EventRecordGenerator` also exposes a nested
`static class Decoder`. The decoder provides a positional `StandardFields`
table and a `Decode(IReadOnlyList<Variant>)` method that populates the
record's own and inherited init-only properties. A per-file
`Register{ModelPrefix}Decoders(this EventRecordDecoderRegistry)` extension
registers each generated decoder with a caller-supplied registry.

The process-wide `EventRecordDecoderRegistry.Default` registers the
standard UA model. It routes by the event's `EventType` field. If the
exact type is not registered, it walks the OPC UA event-type hierarchy
through an optional `SuperTypeResolver`. Vendor models can register their
generated extensions with `Register{Prefix}Decoders` or
`CreateChildScope().Register{Prefix}Decoders()` for test isolation.

```csharp
// EventType-keyed dispatch backed by source-generated decoders:
EventRecord? rec = EventRecordDecoderRegistry.Default.Decode(eventFields);

// Vendor scenario — register extra decoders on an app-scoped child:
var app = EventRecordDecoderRegistry.Default
    .CreateChildScope()
    .RegisterMyVendorDecoders();
EventRecord? vendorRec = app.Decode(eventFields);
```

### Per-record event filters

Every generated `{Type}Record` exposes a nested
`static class EventFilters` alongside its `Decoder` block. The
`Build(registry?)` factory returns an `EventFilter`. Its where clause
restricts events to `OfType({recordTypeId})`, and its select clauses come
from the supplied registry's composed `StandardFields` (by default,
`EventRecordDecoderRegistry.Default`). Use the filter with
`EventRecordDecoderRegistry.Decode`. The registry remaps composed field
positions to each decoder's layout before decoding, so vendor models
extend transparently.

```csharp
// Filter for alarm events:
EventFilter filter = AlarmConditionTypeRecord.EventFilters.Build();

// Or for any condition:
EventFilter f2 = ConditionTypeRecord.EventFilters.Build();

// Or for dialog events:
EventFilter f3 = DialogConditionTypeRecord.EventFilters.Build();

// Or for a specific subtype:
EventFilter f4 = CertificateExpirationAlarmTypeRecord.EventFilters.Build();

// Vendor scenario — pass a child registry so the filter superset
// includes the vendor model's fields:
var app = EventRecordDecoderRegistry.Default
    .CreateChildScope()
    .RegisterMyVendorDecoders();
EventFilter vendorFilter = VibrationAlarmTypeRecord.EventFilters.Build(app);
```

Pass the same registry to `Subscribe*Async` through its `registry:`
parameter. This ensures the streaming side uses the registry that built
the filter.

### Dialog conditions

A `DialogConditionType` event arrives as a
`DialogConditionTypeRecord`. The `Respond` and `Respond2` methods on
`IDialogConditionOperations` close out the dialog. The decoded record
exposes the prompt and available response option set so the caller
can pick an index:

```csharp
await foreach (DialogConditionTypeRecord dialog in streaming.SubscribeDialogsAsync(notifierId))
{
    Console.WriteLine($"Prompt: {dialog.Prompt}");
    LocalizedText[] options = dialog.ResponseOptionSet ?? Array.Empty<LocalizedText>();
    Console.WriteLine($"Options: {string.Join(", ", options)}");

    // Pick whichever option matches your scenario.
    int selectedIndex = 0;
    await alarms.Respond2Async(dialog.ConditionId, selectedIndex,
        new LocalizedText("en", "Approved by operator-1")).ConfigureAwait(false);
}
```

The server-side `DialogConditionType` properties `OkResponse`,
`CancelResponse`, and `DefaultResponse` carry canonical indices (Part 9
§5.6.2). Applications can read these indices separately through the Read
service. The standard `DialogConditionTypeRecord` does not include them;
it contains only the dialog prompt and active state.

### `ConditionRefresh`

Both Part 9 refresh methods are available:

```csharp
// Refresh all conditions for the subscription
await alarms.ConditionRefreshAsync(subscriptionId);

// Refresh just one monitored item's conditions (Part 9 §5.5.8)
await alarms.ConditionRefresh2Async(subscriptionId, monitoredItemId);
```

The streaming subscription also provides
`ISubscription.ConditionRefreshAsync`. Both refresh methods are
equivalent when `AlarmClient` and `IStreamingSubscription` use the same
session.

## Reference

- [OPC UA Part 9 — Alarms and Conditions](https://reference.opcfoundation.org/specs/OPC-10000-9/full)
- [IEC 62682](https://webstore.iec.ch/publication/61256) — Management of alarm systems for the process industries
- [ISA 18.2](https://www.isa.org/products/ansi-isa-18-2-2016-management-of-alarm-systems-) — Management of Alarm Systems for the Process Industries
- [Subscriptions and Monitored Items Service Set](Subscriptions.md) — `IStreamingSubscription` and the V2 subscription engine
- [State Machines](StateMachines.md) — generic Part 16 state-machine API used by
  `AlarmClient.GetShelvingStateAsync` / `ObserveShelvingTransitionsAsync`
- [Model Change Tracking](ModelChangeTracking.md) — client cache invalidation on address-space changes
- Source: `src/Opc.Ua.Server/Alarms/`, `src/Opc.Ua.Client/Alarms/`,
  `src/Opc.Ua.Core.Types/State/AlarmConditionState.Methods.cs`
- Reference client sample: `samples/Reference/ConsoleReferenceClient/AlarmClientSample.cs`
- Conformance tests: `tests/Opc.Ua.History.Tests/AlarmsAndConditions*.cs`