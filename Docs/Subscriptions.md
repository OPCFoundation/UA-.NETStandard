# Subscriptions and Monitored Items Service Set

The OPC UA Subscription and MonitoredItems service sets (Part 4 §5.13
and §5.14) are exposed through three layered client APIs in this
stack:

1. **Classic `Opc.Ua.Client.Subscription`** — the historical
   event-driven API. Items added with
   `subscription.AddItem(item); subscription.ApplyChangesAsync()`;
   notifications delivered through the per-item `Notification` event
   or the per-subscription `FastDataChangeCallback` / `FastEventCallback`.
2. **V2 `ISubscriptionManager` / `ISubscription`** — the options-based
   callback API in `Libraries/Opc.Ua.Client/Subscription/`. Items added
   with `subscription.MonitoredItems.TryAdd(name, options, out _)`;
   notifications delivered through `ISubscriptionNotificationHandler`
   callbacks. The default engine on `ManagedSession`.
3. **`IStreamingSubscription`** — a thin `IAsyncEnumerable<T>`
   abstraction on top of the V2 engine for state-machine waits and
   short-lived monitoring.

**Recommendation:** new code should use the V2 surface
(`ISubscriptionManager` for long-lived application subscriptions,
`IStreamingSubscription` for short-lived / await-until-X scenarios).
The classic API stays supported for migration and continues to ship
in `Opc.Ua.Client.Subscription`; an internal bridge forwards classic
subscriptions onto the V2 publish pipeline when the V2 engine is
active, so existing classic-API code keeps working alongside V2.

For the full engine comparison (publish-pipeline ownership, worker
pools, when to pick which) see
[Sessions.md §4](Sessions.md#4-subscription-engines).

- [Quick reference](#quick-reference)
- [Triggering (SetTriggering)](#triggering-settriggering)
  - [Declarative triggering](#declarative-triggering)
  - [Imperative triggering](#imperative-triggering)
  - [N:M relationships](#nm-relationships)
  - [Save / load / restore behavior](#save--load--restore-behavior)
  - [Error handling](#error-handling)
- [Streaming subscriptions](#streaming-subscriptions)
  - [Obtaining an `IStreamingSubscription`](#obtaining-an-istreamingsubscription)
  - [Subscribing to data changes](#subscribing-to-data-changes)
  - [Subscribing to events](#subscribing-to-events)
  - [Composing streams](#composing-streams)
  - [Lifecycle, cancellation, disposal](#lifecycle-cancellation-disposal)
  - [Pairing with `AlarmClient`](#pairing-with-alarmclient)
- [API comparison summary](#api-comparison-summary)

## Quick reference

| Concern | API |
|---|---|
| Add a V2 subscription | `session.AddSubscription(handler, SubscriptionOptions)` (or `ISubscriptionManager.Add`) |
| Add a monitored item | `subscription.MonitoredItems.TryAdd(name, IOptionsMonitor<MonitoredItemOptions>, out _)` (or `subscription.TryAddMonitoredItem(name, nodeId, configure, out _)` extension) |
| Set triggering — declarative | `MonitoredItemOptions.TriggeredByNames` (`IReadOnlyList<string>`) at item-add time |
| Set triggering — imperative | `ISubscription.SetTriggeringAsync(IMonitoredItem, add, remove, ct)` returning `SetTriggeringResult` |
| Triggering — name-based fluent | `ISubscription.SetTriggeringAsync(string trigName, params string[] tgtNames)` |
| Triggering — navigation | `IMonitoredItem.TriggeringItems` / `IMonitoredItem.TriggeredItems` (N:M) |
| Save / load subscriptions | `session.SaveSubscriptionsAsync(stream)` / `session.LoadSubscriptionsAsync(stream, factory)` |
| Stream — get streaming subscription | `ManagedSession.DefaultStreaming` |
| Stream — construct manually | `new StreamingSubscription(subscriptionManager, options?)` |
| Stream — subscribe one variable | `streaming.SubscribeDataChangesAsync(nodeId, options?, ct)` |
| Stream — subscribe many variables | `streaming.SubscribeDataChangesAsync(IReadOnlyList<NodeId>, ...)` |
| Stream — subscribe to events | `streaming.SubscribeEventsAsync(notifierId, EventFilter, options?, ct)` |
| Stream — wait until predicate | `.TakeUntilAsync(predicate)` |
| Stream — cap by wall-clock time | `.WithTimeoutAsync(timeout)` |
| Stream — take N items | `.TakeAsync(count)` |
| Stream — buffer first N | `.BufferedAsync(count)` |
| Stream — typed alarms | `streaming.SubscribeAlarmsAsync(notifierId, filter?)` |

## Triggering (SetTriggering)

Triggering (OPC UA Part 4 §5.13.5) links a *triggering* monitored
item to one or more *triggered* items. When the triggering item fires
a notification, the triggered items' queued notifications are
reported in the next publish even if their monitoring mode is
`Sampling` (which would otherwise suppress reporting). This is the
canonical pattern for "sample many items at high rate, report on
demand". See Part 4 §5.13.1.6 for the full triggering model.

The V2 engine exposes triggering through a **hybrid API**:

- **Declarative** — set `MonitoredItemOptions.TriggeredByNames` when
  the item is added.
- **Imperative** — call
  `ISubscription.SetTriggeringAsync(IMonitoredItem triggeringItem,
  add, remove, ct)` at any time.

Both paths flow through the same batched apply pipeline: multiple
operations targeting the same triggering item collapse into a single
`SetTriggering` RPC carrying both the merged `linksToAdd` and
`linksToRemove` lists. Per Part 4 §5.13.5.2 the server processes
`linksToRemove` before `linksToAdd`, which matches the engine's
last-intent-wins conflict resolution.

The V2 engine models the topology as a per-item **desired state**:
each monitored item carries a list of stable triggering-item names
(`DesiredTriggeredByNames`) that reflects what the caller wants. The
engine reconciles desired state against the server via `SetTriggering`
RPCs whenever:

- An options change pushes a different `TriggeredByNames` value.
- The imperative API mutates the desired set.
- An item is `Reset` (recreate scenario) — the engine replays the
  desired set so transient server-side state matches the durable
  client-side intent.

The OPC UA spec allows a triggered item to be linked to **multiple**
triggering items (N:M); the V2 API exposes this directly via the
plural `TriggeringItems` / `TriggeredItems` projections.

### Declarative triggering

The simplest way to declare triggering is via `MonitoredItemOptions`
at item-add time:

```csharp
ManagedSession session = await new ManagedSessionBuilder(config, telemetry)
    .UseEndpoint(endpoint)
    .ConnectAsync(ct);

ISubscription sub = session.AddSubscription(handler, new SubscriptionOptions
{
    PublishingInterval = TimeSpan.FromSeconds(1)
});

// Triggering item — reports normally at its sampling interval.
sub.TryAddMonitoredItem("trig",
    VariableIds.Server_ServerStatus_CurrentTime,
    o => o with { MonitoringMode = MonitoringMode.Reporting },
    out _);

// Triggered item — samples in the background; only reports when
// "trig" reports.
sub.TryAddMonitoredItem("sensor",
    new NodeId("Sensor1", 2),
    o => o with
    {
        MonitoringMode = MonitoringMode.Sampling,
        TriggeredByNames = ["trig"]
    },
    out _);
```

The engine batches the underlying `CreateMonitoredItems` /
`SetTriggering` RPCs and applies them on the next subscription
apply pass. There is no need to explicitly call any method to
"finish" the triggering setup — once both items are `Created`, the
engine issues the `SetTriggering` call automatically.

Order of `TryAddMonitoredItem` calls does not matter; the engine
will keep the triggering operation queued until both items are
created on the server, then issue the RPC.

### Imperative triggering

Mutate triggering relationships at any time after items exist via
`SetTriggeringAsync`:

```csharp
SetTriggeringResult result = await sub.SetTriggeringAsync(
    triggeringItem: trig,
    linksToAdd: [sensor1, sensor2],
    linksToRemove: null,
    ct: ct);

foreach ((IMonitoredItem item, StatusCode status) in result.AddResults)
{
    if (!StatusCode.IsGood(status))
    {
        // BadMonitoredItemIdInvalid, BadInvalidState, etc.
    }
}
```

The call queues a single `TriggeringOperation` and returns a
`ValueTask` that completes when the next apply pass applies it. The
result's per-link entries are in the same order as the input lists
for easy pairing.

Convenience overloads use stable monitored-item names:

```csharp
await sub.SetTriggeringAsync("trig", "sensor1", "sensor2");

// add + remove in one call (server processes remove before add per §5.13.5.2):
await sub.SetTriggeringAsync(
    "trig",
    add: ["sensor3"],
    remove: ["sensor1"],
    ct: ct);
```

Unknown names throw `ArgumentException` synchronously.

### N:M relationships

A triggered item may be linked to many triggering items, and a
triggering item may have many triggered items. Use multiple
`SetTriggeringAsync` calls (or include the same triggered name under
multiple triggers' `TriggeredByNames`):

```csharp
sub.TryAddMonitoredItem("trigA", ..., out var trigA);
sub.TryAddMonitoredItem("trigB", ..., out var trigB);
sub.TryAddMonitoredItem("shared",
    nodeId,
    o => o with
    {
        MonitoringMode = MonitoringMode.Sampling,
        TriggeredByNames = ["trigA", "trigB"]
    },
    out var shared);

// "shared" reports whenever EITHER trigA OR trigB fires:
Assert.That(shared.TriggeringItems, Is.EquivalentTo(new[] { trigA, trigB }));
Assert.That(trigA.TriggeredItems, Has.Member(shared));
Assert.That(trigB.TriggeredItems, Has.Member(shared));
```

`TriggeringItems` and `TriggeredItems` are read-only projections
resolved on demand against the subscription's monitored-item
collection. They reflect the *desired* topology (intent) — so they
work immediately on `TryAdd` before any RPC fires, and survive
restore-from-snapshot without depending on server state.

### Save / load / restore behavior

The triggering topology round-trips through the V2 snapshot path:

- `MonitoredItemStateSnapshot.TriggeredByNames` captures the runtime
  desired state at snapshot time.
- `SubscriptionManager.SaveAsync` / `LoadAsync` (and the in-memory
  `SnapshotSubscriptions` / `RestoreSubscriptionsAsync` extensions)
  persist this field via the standard binary encoder.

On load, the engine takes one of three paths:

| Scenario | Behavior |
|---|---|
| **TransferSubscriptions success** | Local desired state is restored from the snapshot. NO `SetTriggering` RPC is issued — per Part 4 §5.13.5 conformant servers preserve server-side triggering relationships across a session transfer. |
| **Recreate-fallback** (transfer rejected or unsupported) | Items reset to "not created"; the saved `DesiredTriggeredByNames` is preserved on each item; the batched apply pass replays `SetTriggering` after items finish re-creating. |
| **In-session `RecreateAsync`** (forced recreate) | Same as recreate-fallback — desired state preserved through reset, replayed after re-creation. |

This closes the gap from issue
[#3834](https://github.com/OPCFoundation/UA-.NETStandard/issues/3834):
triggering links are now automatically restored on
`TransferSubscriptions` *and* on recreate/reconnect; no manual
re-issue of `SetTriggering` is required by callers.

If a server fails to preserve links across transfer (contrary to the
spec), the V2 client view will indicate the links exist while
server-side notifications stop firing. Callers detecting this can
manually re-issue `Subscription.SetTriggeringAsync` per relationship.

### Error handling

Per Part 4 §5.13.5.4 (Table 74) the only spec-specific per-link status
code is `Bad_MonitoredItemIdInvalid`. Service-level codes (Table 73)
include `Bad_NothingToDo`, `Bad_TooManyOperations`, and
`Bad_SubscriptionIdInvalid`.

The engine handles these as follows:

| Condition | Engine response |
|---|---|
| Per-link `Bad_MonitoredItemIdInvalid` on add | Surfaced via the returned `SetTriggeringResult.AddResults[i].Status`; the triggered item's desired-state entry for that triggering name is rolled back so it matches reality. |
| Per-link `Bad` on remove | Surfaced via `RemoveResults[i].Status`; the desired-state entry is re-added so the snapshot continues to reflect the still-existing server link. |
| Service-level `Bad_SubscriptionIdInvalid` | Operations are re-queued (not failed terminally); they replay after the subscription state machine recreates the subscription. |
| Service-level `Bad_TooManyOperations` | Surfaced as a fatal `ServiceResultException` on all contributing TCSs. Callers wishing to recover should chunk their `SetTriggeringAsync` calls manually; auto-chunking is a planned follow-up. |
| Communication errors (timeout, channel closed) | Routed through the existing retry policy; bounded by `MaxTriggeringRetryCount` to prevent infinite loops. |
| Triggering item never reaches `Created` | After `MaxTriggeringRetryCount` re-queues, the operation completes with the triggering item's propagated error status (or `Bad_MonitoredItemIdInvalid` as a fallback). |

## Streaming subscriptions

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
[Sessions.md §4.2 *DefaultSubscriptionEngine (V2)*](Sessions.md#defaultsubscriptionengine-v2)).

### Obtaining an `IStreamingSubscription`

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

### Subscribing to data changes

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

### Subscribing to events

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

### Composing streams

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

### Lifecycle, cancellation, disposal

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

### Pairing with `AlarmClient`

The streaming subscription and `AlarmClient` are complementary:

- `IStreamingSubscription` delivers the *events* (raw or typed).
- `AlarmClient` calls the *methods* (acknowledge, shelve, suppress,
  reset, etc.).

```csharp
AlarmClient alarms = session.GetAlarmClient();
IStreamingSubscription streaming = session.DefaultStreaming;

await foreach (ConditionTypeRecord rec in streaming
    .SubscribeAlarmsAsync(ObjectIds.Server)
    .TakeUntilAsync(r =>
        r is AlarmConditionTypeRecord a &&
        a.ConditionId == myAlarmId &&
        a.ActiveStateId == true))
{
    if (rec is AlarmConditionTypeRecord active && active.ActiveStateId == true)
    {
        await alarms.AcknowledgeAsync(
            active.ConditionId,
            active.EventId,
            new LocalizedText("en", "Auto-acked")).ConfigureAwait(false);
    }
}
```

See [AlarmsAndConditions.md](AlarmsAndConditions.md) for the typed
record hierarchy.

## API comparison summary

| Aspect | Classic `Subscription` | V2 `ISubscriptionManager` | `IStreamingSubscription` |
|---|---|---|---|
| Notification delivery | `MonitoredItem.Notification` event | `ISubscriptionNotificationHandler` callback | `IAsyncEnumerable<T>` |
| Add monitored item | `subscription.AddItem(item); ApplyChanges()` | `subscription.MonitoredItems.TryAdd(name, options, out _)` | `streaming.SubscribeXxxAsync(...)` |
| Remove monitored item | `subscription.RemoveItem(item); ApplyChanges()` | `subscription.MonitoredItems.TryRemove(handle)` | Dispose enumerator |
| Triggering API | `SetTriggeringAsync(MonitoredItem, ArrayOf<MonitoredItem>, ArrayOf<MonitoredItem>, ct)` (1:1 from triggered side via `MonitoredItem.TriggeringItemId`) | `SetTriggeringAsync(IMonitoredItem, IReadOnlyCollection<IMonitoredItem>?, IReadOnlyCollection<IMonitoredItem>?, ct)` returning `SetTriggeringResult`; declarative `MonitoredItemOptions.TriggeredByNames`; N:M | n/a (use the underlying V2 subscription) |
| Save / restore triggering | `MonitoredItemState.TriggeredItems` (server-id based) | `MonitoredItemStateSnapshot.TriggeredByNames` (name based, N:M, round-trips through binary encoder) | n/a |
| Reconnect / transfer triggering replay | `RestoreTriggeringAsync` re-issues `SetTriggering` after every reconnect | `TransferSubscriptions`: no replay (server preserves links per spec); Recreate: automatic replay | n/a |
| Cancellation | Manual | Per call (CT in handler) | Built-in (CT on stream) |
| Composition | Hand-rolled | Hand-rolled | LINQ-style helpers |
| Backpressure | Custom logic | Custom logic | Bounded channel internally |
| Best for | Legacy callers | Long-lived, multi-item subscriptions | Short-lived / wait-for-X scenarios |

There is **no migration cost** — all three APIs can coexist on the
same session. Pick the one that matches the use case.

## Reference

- Subscription source: `Libraries/Opc.Ua.Client/Subscription/`
- Streaming source: `Libraries/Opc.Ua.Client/Subscription/Streaming/`
- Helpers: `Libraries/Opc.Ua.Client/Subscription/Streaming/StreamingSubscriptionExtensions.cs`
- Alarm streaming: `Libraries/Opc.Ua.Client/Alarms/AlarmStreamExtensions.cs`
- Sessions architecture and engine choice: [Sessions.md](Sessions.md)
- Reference client sample:
  `Applications/ConsoleReferenceClient/AlarmClientSample.cs`
