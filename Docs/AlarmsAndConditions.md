# OPC UA Alarms and Conditions

This guide covers Part 9 Alarms & Conditions support in the OPC UA .NET Standard Stack.

## Overview

The stack provides full Part 9 support for both **servers** and **clients**:

* **Server**: Source-generated state types (`ConditionState`, `AlarmConditionState`,
  `DialogConditionState`, etc.) with manual behavior extensions for every Part 9
  method (Enable/Disable, Acknowledge/Confirm, Silence, Suppress/Unsuppress,
  Place/RemoveFromService, Reset, Shelve/Unshelve, GetGroupMemberships).
* **Client**: `AlarmClient` with strongly-typed operations targeting the well-known
  ConditionType method NodeIds, an `AlarmEventDecoder` for typed alarm records, and
  an `AlarmEventFilterBuilder` for fluent event filter construction.

## Server-side: Creating an Alarm

```csharp
var alarm = new AlarmConditionState(telemetry, parent);
alarm.Create(context, NodeId.Null, new QualifiedName("MyAlarm"), null, true);

// Wire optional state machines / methods (all wired automatically in OnAfterCreate)
alarm.SetEnableState(context, true);
alarm.SetActiveState(context, true);
alarm.SetSeverity(context, EventSeverity.High);
alarm.ReportEvent(context, alarm);
```

### Latched Alarms

When `LatchedState` is configured, activation automatically sets `LatchedState = true`
and the alarm remains "latched" after the underlying condition clears. The `Reset` method
clears the latched state — only valid when the alarm is inactive, acknowledged, and
confirmed.

### Re-Alarm

Use `ProcessReAlarm(context)` from your re-alarm timer (when `ReAlarmTime` > 0 and the
alarm is active+unacknowledged). The method clears `AckedState`, clears `SilenceState`,
and increments `ReAlarmRepeatCount`.

### Alarm Groups & Suppression

```csharp
var group = new AlarmGroup(alarmGroupState);
group.AddMember(motorHighTempAlarm);
group.AddMember(motorLowOilAlarm);

var engine = new AlarmSuppressionEngine();
engine.RegisterSuppressionGroup(
    suppressionGroup: motorShutdownGroup,
    suppressionSource: () => motorIsShutDown.Value,
    alarmMembers: new[] { motorHighTempAlarm, motorLowOilAlarm });

// Call periodically or on suppression source change
engine.Evaluate(context);
```

### Address Space Model Changes

```csharp
// In a CustomNodeManager subclass
var aggregator = new ModelChangeAggregator();
aggregator.RecordNodeAdded(newNode.NodeId, newNode.TypeDefinitionId);

// Drain and emit per publish cycle
if (aggregator.HasPending)
{
    RaiseGeneralModelChangeEvent(context, aggregator.Drain());
}
```

## Client-side: Alarm Operations

```csharp
AlarmClient alarms = session.GetAlarmClient();

await alarms.EnableAsync(conditionId);
await alarms.AcknowledgeAsync(conditionId, eventId, new LocalizedText("Acked"));
await alarms.TimedShelveAsync(conditionId, 30000); // shelve for 30s
await alarms.SilenceAsync(conditionId);
await alarms.SuppressAsync(conditionId, comment: new LocalizedText("Maintenance"));

NodeId[] groups = await alarms.GetGroupMembershipsAsync(conditionId);
```

## Client-side: Subscribing to Alarms

The streaming subscription API (see `Docs/StreamingSubscription.md`) plus
`AlarmStreamExtensions` provides typed alarm subscriptions:

```csharp
IStreamingSubscription streaming = ...;

await foreach (ConditionRecord rec in streaming
    .SubscribeAlarmsAsync(notifierId: ObjectIds.Server)
    .TakeUntilAsync(r => r.ConditionId == myAlarmId && r is AlarmRecord ar && ar.ActiveStateId == false))
{
    if (rec is AlarmRecord alarm)
    {
        Console.WriteLine($"{alarm.SourceName}: active={alarm.ActiveStateId} acked={alarm.AckedStateId}");
    }
}
```

The `AlarmEventFilterBuilder` constructs the underlying `EventFilter`:

```csharp
EventFilter filter = new AlarmEventFilterBuilder()
    .ForAlarms()
    .Build();
```

### Dialog Conditions

```csharp
await foreach (DialogRecord dialog in streaming.SubscribeDialogsAsync(notifierId))
{
    Console.WriteLine($"Prompt: {dialog.Prompt}");
    await alarms.RespondAsync(dialog.ConditionId, selectedResponse: 0);
}
```

## ConditionRefresh

Both `ConditionRefresh` and `ConditionRefresh2` are available:

```csharp
await alarms.ConditionRefreshAsync(subscriptionId);
await alarms.ConditionRefresh2Async(subscriptionId, monitoredItemId);
```

## References

* OPC UA Part 9 - Alarms and Conditions: https://reference.opcfoundation.org/specs/OPC-10000-9/full
* IEC 62682 - Management of alarm systems for the process industries
* ISA 18.2 - Management of Alarm Systems for the Process Industries