# Streaming Subscriptions

The `IStreamingSubscription` API exposes OPC UA subscription notifications as
`IAsyncEnumerable<T>` streams. Each `SubscribeXxxAsync` call adds a monitored
item to a shared, lazy-created subscription and pipes notifications through a
bounded channel. Disposing the enumerator removes the monitored item.

## Why?

The classic event-driven subscription model (`Subscription.MonitoredItemNotification`)
requires callers to manage subscriptions, monitored items, lifecycle, and event
handlers explicitly. The streaming API replaces this with a familiar `await foreach`
pattern, which composes naturally with cancellation tokens, timeouts, and
state-machine wait helpers.

## Basic Usage

```csharp
var streaming = new StreamingSubscription(session.SubscriptionManager);

// Subscribe to data changes
await foreach (DataValueChange change in streaming.SubscribeDataChangesAsync(nodeId, ct: token))
{
    Console.WriteLine($"{change.MonitoredItem?.Name}: {change.Value}");
}
```

## Short-Lived Subscriptions

`StreamingSubscriptionExtensions` includes helpers for state-machine waits:

* `TakeUntilAsync(predicate)` — completes when the predicate matches (matching
  item is yielded last)
* `WithTimeoutAsync(timeout)` — completes when the timeout elapses (silently)
* `TakeAsync(count)` — completes after N items
* `BufferedAsync(count)` — collects N items into a list

```csharp
// Wait for alarm acknowledgment
ConditionRecord ack = await streaming
    .SubscribeAlarmsAsync(notifierId)
    .TakeUntilAsync(r => r is AlarmRecord ar && ar.AckedStateId == true)
    .WithTimeoutAsync(TimeSpan.FromMinutes(5))
    .FirstAsync();
```

## Lifecycle

The underlying subscription is created on the first `SubscribeXxxAsync` call.
Multiple concurrent subscribers share the same OPC UA subscription. When
`StreamingSubscription` is disposed, the subscription is deleted from the server.

## Event Subscriptions with Filtering

```csharp
EventFilter filter = new AlarmEventFilterBuilder()
    .ForAlarms()
    .Build();

await foreach (EventNotification evt in streaming.SubscribeEventsAsync(notifierId, filter))
{
    // Raw event fields
}
```

For typed alarm records, see `AlarmStreamExtensions.SubscribeAlarmsAsync`.

## Comparison with Classic Subscription

| Aspect | Classic | Streaming |
|---|---|---|
| API style | Event handlers | `await foreach` |
| Lifecycle | Explicit Add/Remove | Disposal on enumerator end |
| Composition | Hand-rolled | LINQ-like helpers |
| Backpressure | Custom logic | Built-in via Channel |
| Cancellation | Manual | Built-in via CT |