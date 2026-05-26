# Streaming Subscriptions

`IStreamingSubscription` exposes OPC UA subscription notifications as
`IAsyncEnumerable<T>` streams. Each `SubscribeXxxAsync` call adds a
monitored item to a shared, lazy-created OPC UA subscription and pipes
notifications through a `System.Threading.Channels.Channel<T>`.
Disposing the enumerator removes the monitored item.

This API sits on top of the V2 subscription engine
(`Libraries/Opc.Ua.Client/Subscription`). It is **not** a replacement
for either the classic or V2 callback-based API — it is a thin
abstraction targeted at three concrete client scenarios:

- **State-machine waits** — "subscribe, observe transitions until X,
  unsubscribe".
- **Short-lived monitoring** — "I need a sample for 30 seconds".
- **Typed alarm streaming** — Part 9 alarm records via
  `AlarmStreamExtensions` (see [AlarmsAndConditions.md](AlarmsAndConditions.md)).

For long-lived application subscriptions, the callback-based
`ISubscriptionManager.Add` + `ISubscriptionNotificationHandler` API
remains the right choice (see
[Sessions.md](Sessions.md) §4.2 *V2 subscription manager*).

- [Quick reference](#quick-reference)
- [Obtaining an `IStreamingSubscription`](#obtaining-an-istreamingsubscription)
- [Subscribing to data changes](#subscribing-to-data-changes)
- [Subscribing to events](#subscribing-to-events)
- [Composing streams](#composing-streams)
- [Lifecycle, cancellation, disposal](#lifecycle-cancellation-disposal)
- [Pairing with `AlarmClient`](#pairing-with-alarmclient)
- [Comparison with classic and V2 APIs](#comparison-with-classic-and-v2-apis)

## Quick reference

| Concern | API |
|---|---|
| Get a streaming subscription | `ManagedSession.DefaultStreaming` |
| Construct one manually | `new StreamingSubscription(subscriptionManager, options?)` |
| Subscribe to one variable | `SubscribeDataChangesAsync(nodeId, options?, ct)` |
| Subscribe to many variables | `SubscribeDataChangesAsync(IReadOnlyList<NodeId>, ...)` |
| Subscribe to events | `SubscribeEventsAsync(notifierId, EventFilter, options?, ct)` |
| Wait until predicate matches | `.TakeUntilAsync(predicate)` |
| Cap by wall-clock time | `.WithTimeoutAsync(timeout)` |
| Take N items | `.TakeAsync(count)` |
| Buffer first N into a list | `.BufferedAsync(count)` |
| Typed alarm stream | `streaming.SubscribeAlarmsAsync(notifierId, filter?)` |

## Obtaining an `IStreamingSubscription`

`ManagedSession.DefaultStreaming` lazily constructs a shared instance
the first time it is accessed and disposes it when the session is
disposed.

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .ConnectAsync(ct);

IStreamingSubscription streaming = session.DefaultStreaming;
```

If you need to bind to a specific `ISubscriptionManager` (for example
in tests or with the raw `Session`'s V2 engine), construct directly:

```csharp
var streaming = new StreamingSubscription(
    subscriptionManager: session.SubscriptionManager,
    subscriptionOptions: new SubscriptionOptions
    {
        PublishingInterval = TimeSpan.FromMilliseconds(250),
        KeepAliveCount = 10,
        LifetimeCount = 100,
    });
```

The underlying OPC UA subscription is **not** created until the first
`SubscribeXxxAsync` call. Once created it is shared by every active
`SubscribeXxxAsync` enumerator on this `StreamingSubscription`.

## Subscribing to data changes

```csharp
await foreach (DataValueChange change in streaming
    .SubscribeDataChangesAsync(VariableIds.Server_ServerStatus_CurrentTime, ct: ct)
    .ConfigureAwait(false))
{
    Console.WriteLine($"{change.MonitoredItem?.Name}: {change.Value}");
}
```

For multiple variables in a single stream:

```csharp
var nodes = new[]
{
    VariableIds.Server_ServerStatus_CurrentTime,
    VariableIds.Server_ServerStatus_SecondsTillShutdown,
};

await foreach (DataValueChange change in streaming
    .SubscribeDataChangesAsync(nodes, options: new MonitoredItemOptions
    {
        SamplingInterval = TimeSpan.FromMilliseconds(500),
        QueueSize = 5,
    }, ct: ct).ConfigureAwait(false))
{
    Console.WriteLine($"{change.MonitoredItem?.ClientHandle}: {change.Value}");
}
```

Each `MonitoredItemOptions` is treated as a template; only `StartNodeId`
is overridden internally per node.

## Subscribing to events

```csharp
EventFilter filter = new AlarmEventFilterBuilder()
    .ForAlarms()
    .Build();

await foreach (EventNotification evt in streaming
    .SubscribeEventsAsync(ObjectIds.Server, filter, ct: ct)
    .ConfigureAwait(false))
{
    // Raw access; for typed records use SubscribeAlarmsAsync.
    Console.WriteLine($"#{evt.Fields.Count} fields");
}
```

`SubscribeEventsAsync` does *not* validate the filter. Use
`AlarmEventFilterBuilder` for Part 9 events and any custom
`SimpleAttributeOperand`-based filter for other event types.

## Composing streams

`StreamingSubscriptionExtensions` ships four LINQ-style helpers
designed for the bounded-observation use cases. They wrap the source
`IAsyncEnumerable<T>` and stop iterating (which disposes the
underlying enumerator and the monitored item) when their condition
fires.

```csharp
// 1) Wait until a predicate matches, then return the matching item.
DataValueChange completed = await streaming
    .SubscribeDataChangesAsync(buildProgressVariable)
    .TakeUntilAsync(c => (double)c.Value.Value >= 100.0)
    .LastAsync(ct);

// 2) Cap by wall-clock time. Completes silently when the timeout elapses.
List<DataValueChange> samples = await streaming
    .SubscribeDataChangesAsync(sensor)
    .WithTimeoutAsync(TimeSpan.FromSeconds(30))
    .ToListAsync(ct);

// 3) Take exactly N values.
List<DataValueChange> firstThree = await streaming
    .SubscribeDataChangesAsync(sensor)
    .TakeAsync(3)
    .ToListAsync(ct);

// 4) Buffer N items into a list — for one-shot snapshots.
IReadOnlyList<DataValueChange> snapshot = await streaming
    .SubscribeDataChangesAsync(sensor)
    .BufferedAsync(count: 10, ct);
```

`TakeUntilAsync` yields the matching item *last*, so callers can grab
the transition value with `LastAsync` / a terminating handler.

## Lifecycle, cancellation, disposal

The streaming subscription guarantees three invariants:

1. **Lazy subscription creation.** No OPC UA `CreateSubscription`
   round-trip happens until your first `SubscribeXxxAsync` call.
2. **Reference-counted monitored items.** Each call adds a monitored
   item; disposing the enumerator (end of the `await foreach`, an
   exception, or explicit `await using`) removes that monitored item.
   The underlying subscription stays alive for other enumerators.
3. **Disposal order.** Disposing the `StreamingSubscription` completes
   all open channels and deletes the underlying OPC UA subscription.
   Disposing the `ManagedSession` calls this for you.

Cancellation propagates the natural way: pass a `CancellationToken` to
`SubscribeXxxAsync` *or* the outer `await foreach` (via
`WithCancellation`). When the token fires the enumerator stops, the
finally block removes the monitored item, and the loop body throws
`OperationCanceledException`.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
try
{
    await foreach (DataValueChange change in
        streaming.SubscribeDataChangesAsync(sensor, ct: cts.Token))
    {
        Process(change);
    }
}
catch (OperationCanceledException) { /* expected */ }
```

## Pairing with `AlarmClient`

The streaming subscription and `AlarmClient` are complementary:

- `IStreamingSubscription` delivers the *events* (raw or typed).
- `AlarmClient` calls the *methods* (acknowledge, shelve, suppress,
  reset, etc.).

```csharp
AlarmClient alarms = session.GetAlarmClient();
IStreamingSubscription streaming = session.DefaultStreaming;

await foreach (ConditionRecord rec in streaming
    .SubscribeAlarmsAsync(ObjectIds.Server)
    .TakeUntilAsync(r =>
        r is AlarmRecord a &&
        a.ConditionId == myAlarmId &&
        a.ActiveStateId == true))
{
    if (rec is AlarmRecord active && active.ActiveStateId == true)
    {
        await alarms.AcknowledgeAsync(
            active.ConditionId!,
            active.EventId,
            new LocalizedText("en", "Auto-acked")).ConfigureAwait(false);
    }
}
```

See [AlarmsAndConditions.md](AlarmsAndConditions.md) for the typed
record hierarchy.

## Comparison with classic and V2 APIs

| Aspect | Classic `Subscription` | V2 `ISubscriptionManager` | `IStreamingSubscription` |
|---|---|---|---|
| Notification delivery | `MonitoredItem.Notification` event | `ISubscriptionNotificationHandler` callback | `IAsyncEnumerable<T>` |
| Add monitored item | `subscription.AddItem(item); ApplyChanges()` | `subscription.MonitoredItems.TryAdd(name, options, out _)` | `streaming.SubscribeXxxAsync(...)` |
| Remove monitored item | `subscription.RemoveItem(item); ApplyChanges()` | `subscription.MonitoredItems.TryRemove(handle)` | Dispose enumerator |
| Cancellation | Manual | Per call (CT in handler) | Built-in (CT on stream) |
| Composition | Hand-rolled | Hand-rolled | LINQ-style helpers |
| Backpressure | Custom logic | Custom logic | Bounded channel internally |
| Best for | Legacy callers | Long-lived, multi-item subscriptions | Short-lived / wait-for-X scenarios |

There is **no migration cost** — all three APIs can coexist on the same
session. Pick the one that matches the use case.

## Reference

- Source: `Libraries/Opc.Ua.Client/Subscription/Streaming/`
- Helpers source:
  `Libraries/Opc.Ua.Client/Subscription/Streaming/StreamingSubscriptionExtensions.cs`
- Alarm streaming: `Libraries/Opc.Ua.Client/Alarms/AlarmStreamExtensions.cs`
- Sessions architecture: [Sessions.md](Sessions.md)
- Reference client sample:
  `Applications/ConsoleReferenceClient/AlarmClientSample.cs`