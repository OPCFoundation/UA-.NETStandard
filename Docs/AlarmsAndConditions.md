# Alarms and Conditions (OPC UA Part 9)

This guide describes the OPC UA Part 9 *Alarms & Conditions* support in
this stack: how to build alarm-aware servers, how to consume alarm events
on the client, and how the new helpers (latched alarms, alarm groups,
suppression engine, alarm metrics, streaming alarm records) fit together.

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
  - [Alarm groups and first-in-group](#alarm-groups-and-first-in-group)
  - [Alarm metrics (rate tracking)](#alarm-metrics-rate-tracking)
  - [Vetoing alarm operations](#vetoing-alarm-operations)
  - [Audit events](#audit-events)
- [Client side](#client-side)
  - [`AlarmClient` — typed operations](#alarmclient--typed-operations)
  - [Subscribing to alarms with `IAsyncEnumerable`](#subscribing-to-alarms-with-iasyncenumerable)
  - [Typed alarm records](#typed-alarm-records)
  - [`AlarmEventFilterBuilder`](#alarmeventfilterbuilder)
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
| Alarm groups | `AlarmGroup`, `AlarmSuppressionEngine` | `AlarmClient.GetGroupMembershipsAsync` |
| Alarm rate | `AlarmRateTracker` | read `AlarmMetricsType` attributes |
| Refresh state | `Server.ConditionRefresh` (server-driven) | `AlarmClient.ConditionRefreshAsync` / `ConditionRefresh2Async` |
| Stream alarm events | n/a | `IStreamingSubscription.SubscribeAlarmsAsync` |
| Decode raw event fields | n/a | `AlarmEventDecoder` |

`AlarmClient` is obtained from any `ISession`:

```csharp
AlarmClient alarms = session.GetAlarmClient();
```

## Server side

### Creating an alarm

`AlarmConditionState` is the central server-side state type for all
Part 9 alarms. It is *source-generated* from the standard NodeSet and
extended with hand-written behavior. The behavior partial file
(`Stack/Opc.Ua.Core.Types/State/AlarmConditionState.Methods.cs`) wires
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

The optional state nodes are created the way you would expect on a
generated `AlarmConditionState`: assign to `alarm.SilenceState`,
`alarm.OutOfServiceState`, `alarm.LatchedState`, etc. before calling
`Create`. The quickstart reference server creates these via
`AlarmConditionTypeHolder.Initialize` —
`Applications/Quickstarts.Servers/Alarms/AlarmHolders/`.

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
  The `Reset` method validates **all** preconditions before accepting:
  enabled, not active, acknowledged, confirmed (if `ConfirmedState` is
  present). Any other state returns `Bad_InvalidState`.

Latched alarms are *retained* (`Retain = true`) for as long as
`LatchedState.Id` is `true`, so a client refresh sees them.

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

`ProcessReAlarm`:

- clears `AckedState` (forces re-acknowledgement)
- clears `SilenceState` (audible annunciation resumes)
- increments `ReAlarmRepeatCount`
- calls `ReportStateChange` so the new event is published

### Audible alarms and silencing

If `AudibleEnabled = true` and `AudibleSound` is populated, the
`UpdateAudibleState` helper takes care of clearing the silence state
when the alarm activates (so the next activation is audible again):

```csharp
ByteString sound = LoadWavFile();
alarm.UpdateAudibleState(context, active: true, soundData: sound);
```

`SilenceAsync` from the client (or the `Silence` method handler on the
server) sets `SilenceState.Id = true`.

### Suppression, out-of-service, shelving

All three states contribute to the `SuppressedOrShelved` boolean. The
state-type setters keep that flag in sync — clearing one does **not**
clear `SuppressedOrShelved` while the others are still active:

```csharp
alarm.SetSuppressedState(context, true);      // SuppressedOrShelved = true
alarm.SetOutOfServiceState(context, true);    // SuppressedOrShelved stays true
alarm.SetSuppressedState(context, false);     // SuppressedOrShelved stays true
alarm.SetOutOfServiceState(context, false);   // SuppressedOrShelved = false
```

### Alarm groups and first-in-group

`Libraries/Opc.Ua.Server/Alarms/AlarmGroup.cs` wraps a generated
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

The first `Evaluate` call always applies the current state, so
clients see a coherent suppression state immediately after
registration — there is no edge required.

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
Internally it uses the well-known `ConditionType` /
`AcknowledgeableConditionType` / `AlarmConditionType` /
`DialogConditionType` method NodeIds, so it works whether or not the
server exposes condition instances as nodes in the address space
(Part 9 §5.5.4 — `ConditionId` is acceptable as `ObjectId`).

```csharp
AlarmClient alarms = session.GetAlarmClient();

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

The `*Async(... comment ...)` overloads automatically pick the
spec-defined `*2` method when a non-empty comment is supplied
(`Suppress2`, `Unsuppress2`, `RemoveFromService2`, `PlaceInService2`,
`Reset2`).

### Subscribing to alarms with `IAsyncEnumerable`

Alarm events flow through the [streaming subscription
API](StreamingSubscription.md). The
`AlarmStreamExtensions.SubscribeAlarmsAsync` extension returns
strongly-typed records:

```csharp
ManagedSession session = ...;
IStreamingSubscription streaming = session.DefaultStreaming;

await foreach (ConditionRecord record in streaming
    .SubscribeAlarmsAsync(notifierId: ObjectIds.Server, ct: ct)
    .ConfigureAwait(false))
{
    switch (record)
    {
        case ExclusiveLimitAlarmRecord limit:
            Console.WriteLine($"{limit.SourceName} now {limit.CurrentLimitState}");
            break;
        case AlarmRecord alarm when alarm.ActiveStateId == true:
            await alarms.AcknowledgeAsync(alarm.ConditionId!,
                alarm.EventId,
                new LocalizedText("en", "Auto-ack")).ConfigureAwait(false);
            break;
        case DialogRecord dialog:
            // Pick a response index from dialog.ResponseOptionSet
            await alarms.RespondAsync(dialog.ConditionId!,
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
        r is AlarmRecord a && a.ConditionId == myAlarmId &&
        a.ActiveStateId == false)
    .WithTimeoutAsync(TimeSpan.FromMinutes(5))
    .LastAsync(ct);
```

### Typed alarm records

The `AlarmEventDecoder` maps raw field arrays into a record hierarchy:

```
ConditionRecord
├── AcknowledgeableConditionRecord
│   └── AlarmRecord
│       ├── LimitAlarmRecord
│       │   ├── ExclusiveLimitAlarmRecord
│       │   └── NonExclusiveLimitAlarmRecord
│       ├── DiscreteAlarmRecord
│       │   └── OffNormalAlarmRecord
│       │       └── CertificateExpirationAlarmRecord
│       └── DiscrepancyAlarmRecord
└── DialogRecord
```

The decoder upgrades the record type based on which fields are
populated in the event. A simple `switch` on the record type gives you
the right field set:

```csharp
ConditionRecord? record = AlarmEventDecoder.Decode(eventFields);

if (record is CertificateExpirationAlarmRecord cert)
{
    Console.WriteLine($"Cert {cert.CertificateType} expires {cert.ExpirationDate}");
}
```

### `AlarmEventFilterBuilder`

`AlarmEventFilterBuilder` produces an `EventFilter` whose select-clause
list matches the decoder's field order. Always use the builder when
you intend to decode with `AlarmEventDecoder`:

```csharp
EventFilter filter = new AlarmEventFilterBuilder()
    .ForAlarms()                                       // OfType AlarmConditionType
    .Build();

// Or for condition (any subtype) events:
EventFilter f2 = new AlarmEventFilterBuilder().ForConditions().Build();

// Or for dialog events:
EventFilter f3 = new AlarmEventFilterBuilder().ForDialogs().Build();

// Or for a specific subtype:
EventFilter f4 = new AlarmEventFilterBuilder()
    .OfType(ObjectTypeIds.CertificateExpirationAlarmType)
    .Build();
```

### Dialog conditions

A `DialogConditionType` event arrives as a `DialogRecord`. The
`Respond` and `Respond2` methods on `IDialogConditionOperations` close
out the dialog. The decoded record exposes the prompt and available
response option set so the caller can pick an index:

```csharp
await foreach (DialogRecord dialog in streaming.SubscribeDialogsAsync(notifierId))
{
    Console.WriteLine($"Prompt: {dialog.Prompt}");
    LocalizedText[] options = dialog.ResponseOptionSet ?? Array.Empty<LocalizedText>();
    Console.WriteLine($"Options: {string.Join(", ", options)}");

    // Pick whichever option matches your scenario.
    int selectedIndex = 0;
    await alarms.Respond2Async(dialog.ConditionId!, selectedIndex,
        new LocalizedText("en", "Approved by operator-1")).ConfigureAwait(false);
}
```

The `OkResponse` / `CancelResponse` / `DefaultResponse` properties on
the *server-side* `DialogConditionType` (Part 9 §5.6.2) carry their
canonical indices for clients to read separately via Read service if
the application needs them; they are not surfaced in the standard
`DialogRecord` (which only carries the dialog prompt + active state).

### `ConditionRefresh`

Both Part 9 refresh methods are available:

```csharp
// Refresh all conditions for the subscription
await alarms.ConditionRefreshAsync(subscriptionId);

// Refresh just one monitored item's conditions (Part 9 §5.5.8)
await alarms.ConditionRefresh2Async(subscriptionId, monitoredItemId);
```

The classic `ISubscription.ConditionRefreshAsync` on the streaming
subscription is also available — they are equivalent when the
`AlarmClient` and the `IStreamingSubscription` are bound to the same
session.

## Reference

- [OPC UA Part 9 — Alarms and Conditions](https://reference.opcfoundation.org/specs/OPC-10000-9/full)
- [IEC 62682](https://webstore.iec.ch/publication/61256) — Management of alarm systems for the process industries
- [ISA 18.2](https://www.isa.org/products/ansi-isa-18-2-2016-management-of-alarm-systems-) — Management of Alarm Systems for the Process Industries
- [Streaming Subscriptions](StreamingSubscription.md) — `IStreamingSubscription`
- [Model Change Tracking](ModelChangeTracking.md) — client cache invalidation on address-space changes
- Source: `Libraries/Opc.Ua.Server/Alarms/`, `Libraries/Opc.Ua.Client/Alarms/`,
  `Stack/Opc.Ua.Core.Types/State/AlarmConditionState.Methods.cs`
- Reference client sample: `Applications/ConsoleReferenceClient/AlarmClientSample.cs`
- Conformance tests: `Tests/Opc.Ua.History.Tests/AlarmsAndConditions*.cs`