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
- [Classic → V2 surface mapping](#classic--v2-surface-mapping)
  - [Subscription lifecycle](#subscription-lifecycle)
  - [Notifications and callbacks](#notifications-and-callbacks)
  - [Subscription-level service operations](#subscription-level-service-operations)
  - [Monitored-item management](#monitored-item-management)
  - [Per-item runtime](#per-item-runtime)
  - [Persistence (save / load)](#persistence-save--load)
  - [Tuning knobs](#tuning-knobs)
  - [Engine wiring](#engine-wiring)
- [Three-API summary](#three-api-summary)

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
`SetTriggering` request carrying both the merged `linksToAdd` and
`linksToRemove` lists. Per Part 4 §5.13.5.2 the server processes
`linksToRemove` before `linksToAdd`, which matches the engine's
last-intent-wins conflict resolution.

The V2 engine models the topology as a per-item **desired state**:
each monitored item carries a list of stable triggering-item names
(`DesiredTriggeredByNames`) that reflects what the caller wants. The
engine reconciles desired state against the server via `SetTriggering`
requests whenever:

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
`SetTriggering` requests and applies them on the next subscription
apply pass. There is no need to explicitly call any method to
"finish" the triggering setup — once both items are `Created`, the
engine issues the `SetTriggering` call automatically.

Order of `TryAddMonitoredItem` calls does not matter; the engine
will keep the triggering operation queued until both items are
created on the server, then issue the request.

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

### Cancellation semantics

The `CancellationToken` passed to `SetTriggeringAsync` controls *only the await*. It does **not** cancel the queued operation:

- Cancelling the token **aborts the await** and surfaces an `OperationCanceledException` to the caller.
- The desired-state mutations performed synchronously by the engine before the await (the updates that make `IMonitoredItem.TriggeringItems` / `IMonitoredItem.TriggeredItems` and any subsequent snapshot reflect the new topology) **already happened** when `SetTriggeringAsync` returned its `ValueTask`. They stand regardless of cancellation.
- The queued operation **still runs on the next apply pass** and may still issue a `SetTriggering` request against the server, mutating server state.
- Therefore cancellation cannot be used to "undo" or "prevent" a `SetTriggering` call. To revert intent, the caller must explicitly issue an opposing `SetTriggeringAsync` (or change the declarative `MonitoredItemOptions.TriggeredByNames`).

> [!WARNING]
> Cancelling the `CancellationToken` does NOT cancel the server-side `SetTriggering` call; the queued operation still runs and may still mutate server state.

```csharp
// Original intent: when "trig" fires, also report "sensor1" and "sensor2".
await sub.SetTriggeringAsync("trig", "sensor1", "sensor2");

// To undo, issue an opposing call with the items in `remove` —
// passing the same cancellation token to the first call would NOT
// have prevented the link.
await sub.SetTriggeringAsync(
    "trig",
    add: null,
    remove: ["sensor1", "sensor2"],
    ct: ct);
```

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
work immediately on `TryAdd` before any request fires, and survive
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
| **TransferSubscriptions success** | Local desired state is restored from the snapshot. NO `SetTriggering` request is issued — per Part 4 §5.13.5 conformant servers preserve server-side triggering relationships across a session transfer. |
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

## Classic → V2 surface mapping

This section is the working reference for porting classic
(`Opc.Ua.Client.Subscription` / `Opc.Ua.Client.MonitoredItem`) code to
the V2 surface (`Opc.Ua.Client.Subscriptions.ISubscription` /
`Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem`). The
tables describe today's APIs side-by-side; "not on V2" rows record
behaviour the V2 engine replaces by design (handler-centric,
channel-based pipeline, snapshot/restore, options-monitor-driven
reconciliation, etc.) along with the recommended V2 alternative.

### Subscription lifecycle

| Classic | V2 |
|---|---|
| `new Subscription(template)` + `Session.AddSubscription(s)` + `s.CreateAsync()` | `ISubscriptionManager.Add(handler, IOptionsMonitor<SubscriptionOptions>)` — V2 creates the subscription on the server asynchronously; callers poll `subscription.Created` (or attach an `OnSubscriptionStateChangedAsync` handler). |
| `Session.RemoveSubscriptionAsync(s)` | `await subscription.DisposeAsync()` — V2 removal is dispose-on-subscription. |
| `s.CreateAsync(ct)` / `s.ModifyAsync(ct)` / `s.DeleteAsync(silent, ct)` | Implicit via `Add` / options push / `DisposeAsync`. No explicit V2 calls — behaviour is driven by options + lifecycle. |
| `s.SetPublishingModeAsync(bool, ct)` | Push `SubscriptionOptions { PublishingEnabled = ... }` via the `IOptionsMonitor<SubscriptionOptions>`. The V2 manager picks up the change automatically. |
| `s.ChangesPending` / `s.ChangesCompleted()` | Not on V2 (the engine is fully push-driven; no "pending changes" concept). Callers wait on the side-effect (e.g. `IMonitoredItem.Created`, options-monitor change tokens). |

### Notifications and callbacks

| Classic | V2 |
|---|---|
| `s.FastDataChangeCallback` (delegate) | `ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(...)` |
| `s.FastKeepAliveCallback` (delegate) | `ISubscriptionNotificationHandler.OnKeepAliveNotificationAsync(...)` |
| `s.FastEventCallback` | `ISubscriptionNotificationHandler.OnEventDataNotificationAsync(...)` |
| `item.Notification += handler` (per-item event) | Per-item dispatch through the handler with `DataValueChange.MonitoredItem` to identify the source. |
| `s.PublishStatusChanged` / `s.StateChanged` events | Unified into a single callback `ISubscriptionNotificationHandler.OnSubscriptionStateChangedAsync(ISubscription, SubscriptionState, PublishState, CancellationToken)` that surfaces lifecycle (Opened / Created / Modified / Deleted) and publish-state (Republish / Recovered / Transferred) transitions. |
| `item.DequeueValues()` (client-side cache) | Not on V2 — values stream into the handler. The handler is the cache; callers retain whatever they need. |
| `s.LastNotification` / `s.Notifications` / `s.LastNotificationTime` | Not on `ISubscription`. The manager exposes `MissingMessageCount` / `RepublishMessageCount`; handlers maintain their own derived state (e.g. via the `publishTime` arg on each callback). |
| `s.PublishingStopped` | Not exposed as a property. Handlers derive it from `OnSubscriptionStateChangedAsync` (the `PublishState` mask flips between `Republish` / `Recovered` / `Transferred`). |

### Subscription-level service operations

| Classic | V2 |
|---|---|
| `s.RepublishAsync(seq, ct)` | Not on V2. The V2 engine auto-republishes on gap detection via `MessageProcessor.TryRepublishAsync`; there is no user-driven variant. Callers needing raw access can call `session.RepublishAsync(null, subscriptionId, seq, ct)`. |
| `s.ResendDataAsync(ct)` | Not on V2. Callers needing raw access call `session.CallAsync(null, ResendData methodId, ...)`. |
| `s.ConditionRefreshAsync(ct)` | `s.ConditionRefreshAsync(ct)` — same shape. |
| `s.ConditionRefresh2Async(monitoredItemId, ct)` | `item.ConditionRefreshAsync(ct)` on `IMonitoredItem` (per-item; no `monitoredItemId` arg needed). |
| `s.SetTriggeringAsync(triggering, links, removes, ct)` returning `SetTriggeringResponse` | `ISubscription.SetTriggeringAsync(IMonitoredItem triggeringItem, IReadOnlyCollection<IMonitoredItem>? linksToAdd, IReadOnlyCollection<IMonitoredItem>? linksToRemove, CancellationToken ct)` returning `SetTriggeringResult` with per-link statuses; plus name-based fluent overloads; plus the declarative `MonitoredItemOptions.TriggeredByNames` option (see [Triggering](#triggering-settriggering)). Supports N:M via `IMonitoredItem.TriggeringItems` plural; batches per-triggering-item requests; replays automatically on recreate/reconnect. |
| `s.TransferAsync(target, sendInitialValues, ct)` | Configure transfer-on-recreate via `ManagedSessionBuilder.WithTransferSubscriptionsOnRecreate(true)`. The per-call `sendInitialValues` toggle lives on `SubscriptionOptions.SendInitialValuesOnTransfer` (default `false`). |
| `s.SetSubscriptionDurableAsync(...)` | `ISubscription.SetAsDurableAsync(TimeSpan lifetime, CancellationToken ct = default)` → revised lifetime hours. |
| `s.SaveMessageInCache(...)` | Not on V2 (classic internal). The V2 message pipeline is channel-based with no replay cache. |

### Monitored-item management

| Classic | V2 |
|---|---|
| `s.AddItem(item)` / `s.AddItems(IEnumerable)` | `s.MonitoredItems.TryAdd(name, IOptionsMonitor<MonitoredItemOptions>, out IMonitoredItem)` — V2 keys items by a caller-supplied stable string `name`. Fluent helper: `s.TryAddMonitoredItem(name, nodeId, configure, out _)`. |
| `s.RemoveItem(item)` / `s.RemoveItems(...)` | `s.MonitoredItems.TryRemove(clientHandle)`. |
| `s.ApplyChangesAsync(ct)` | Not needed — V2 batches automatically via the options monitor. |
| `s.CreateItemsAsync(ct)` / `s.ModifyItemsAsync(ct)` / `s.DeleteItemsAsync(...)` | Implicit via `TryAdd` / options push / `TryRemove`. |
| `s.SetMonitoringModeAsync(mode, ids, ct)` | Push `MonitoredItemOptions { MonitoringMode = ... }` per item. |
| `s.ResolveItemNodeIdsAsync(ct)` | Not on V2 — callers resolve `RelativePath` to `NodeId` ahead of time via `Browse` / `TranslateBrowsePathsToNodeIds`. |
| `s.MonitoredItems` / `s.MonitoredItemCount` | `s.MonitoredItems.Items` / `s.MonitoredItems.Count`. |

### Per-item runtime

| Classic | V2 |
|---|---|
| `item.ClientHandle` | `IMonitoredItem.ClientHandle`. |
| `item.ServerId` | `IMonitoredItem.ServerId`. |
| `item.Status.Error` / `item.Status.Created` / `item.Status.Id` | `IMonitoredItem.Error` / `Created` / `ServerId`. |
| `item.AttributesModified` | Not on V2 — reconciliation is driven by `IOptionsMonitor<MonitoredItemOptions>` change tokens; there is no "modified" flag to query. |
| `item.Filter` round-trip | `IMonitoredItem.FilterResult`. |
| `item.DequeueValues()` / `item.LastValue` | Not on V2 — values flow through `ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(...)`; the handler decides what to retain. |
| `item.Notification += ...` (event) | Per-item dispatch through `OnDataChangeNotificationAsync` with `DataValueChange.MonitoredItem`. |
| `item.GetEventTypeAsync` / `GetFieldValue` / `GetEventTime` / `GetFieldName` | Not on V2 `IMonitoredItem` — event-field helpers are caller-side. For typed Part 9 alarm records use [`AlarmStreamExtensions.SubscribeAlarmsAsync`](AlarmsAndConditions.md). |
| `item.TriggeringItemId` / `item.TriggeredItems` (1:1) | `IMonitoredItem.TriggeringItems` (plural, N:M) and `IMonitoredItem.TriggeredItems` (reverse "items I trigger") — both on-demand projections by stable name. Matches OPC UA Part 4 §5.13.5 N:M; runtime desired-state mutations from imperative `SetTriggeringAsync` are immediately visible. |

### Persistence (save / load)

| Classic | V2 |
|---|---|
| `session.Save(Stream, IEnumerable<Subscription>)` (BinaryEncoder + `SubscriptionState.Encode`) | `ISubscriptionManager.SaveAsync(Stream, IServiceMessageContext, IEnumerable<ISubscription>?, CancellationToken)`, with fluent `ManagedSession.SaveSubscriptionsAsync(stream)` extension. |
| `session.Load(Stream, bool transferSubscriptions)` | `ISubscriptionManager.LoadAsync(Stream, IServiceMessageContext, handlerFactory, transferSubscriptions, CancellationToken)`, with fluent `ManagedSession.LoadSubscriptionsAsync(stream, factory, transfer, ct)`. Recreate (`false`) and transfer (`true` via TransferSubscriptions; falls back to recreate on `Bad_SubscriptionIdInvalid` / `Bad_ServiceUnsupported`) both work end-to-end. |
| `s.Snapshot(out SubscriptionState)` / `s.Restore(SubscriptionState)` | `ISubscription.Snapshot()` → `SubscriptionStateSnapshot`; `ISubscriptionManager.RestoreAsync(handler, state, transfer, ct)`; per-item `IMonitoredItem.Snapshot()` → `MonitoredItemStateSnapshot`. Fluent: `ManagedSession.SnapshotSubscriptions()` / `RestoreSubscriptionsAsync(states, factory, transfer, ct)`. |
| Triggering survives save/load | Classic: `MonitoredItemState.TriggeredItems` (server-id based, 1:1). V2: `MonitoredItemStateSnapshot.TriggeredByNames` (name based, N:M; round-trips through the binary encoder; replays automatically on recreate, see [Save / load / restore behavior](#save--load--restore-behavior)). |

### Tuning knobs

| Classic | V2 |
|---|---|
| `s.MaxMessageCount` | Not on V2 — the channel-based pipeline uses an unbounded queue with backpressure. |
| `s.MinLifetimeInterval` (property) | `SubscriptionOptions.MinLifetimeInterval`. |
| `s.DisableMonitoredItemCache` | Not on V2 — there is no per-item cache to disable (the handler is the cache). |
| `s.SequentialPublishing` | Always-on. The V2 prioritized publish-ack channel guarantees per-subscription in-order delivery; documented on `ISubscriptionNotificationHandler`. |
| `s.RepublishAfterTransfer` | Implicit via `MessageProcessor.TryRepublishAsync` (always-on gap fill); no opt-out. |
| `s.OutstandingMessageWorkers` (per-subscription) | Manager-wide `PublishWorkerCount`. |
| `s.Id` / `s.TransferId` | `ISubscription.ServerId` (`uint`). |
| `s.Handle` (caller bookkeeping) | Not on `ISubscription`. Callers keep a side dictionary keyed by the item `Name`. |

### Engine wiring

| Classic | V2 |
|---|---|
| `Session.SubscriptionEngineFactory` defaulted to `ClassicSubscriptionEngineFactory.Instance` | Defaulted to `DefaultSubscriptionEngineFactory.Instance` on `ManagedSession`. Raw `Session` constructed with `DefaultSessionFactory` defaults to classic for backwards compatibility; opt in by passing `SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance`. |
| `Session.AddSubscription(Subscription)` (classic-typed) | Unchanged — classic subscriptions are still added via this API on classic-engine sessions. On a V2-engine `ManagedSession`, classic subscriptions are bridged onto the V2 publish pipeline by an internal `SubscriptionBridge`. |
| `ManagedSession.SubscriptionManager` (V2) | Available alongside `ManagedSession.Subscriptions` (classic API), so both surfaces coexist on the same session. |

### Per-item options mapping

Every classic configurable property maps directly to a field on
`MonitoredItemOptions`: `StartNodeId`, `AttributeId`, `IndexRange`,
`Encoding`, `MonitoringMode`, `SamplingInterval`, `Filter`,
`QueueSize`, `DiscardOldest`, `TimestampsToReturn`. The `Order`
and `Name` keys are V2-only (the latter is the dictionary key
used by `IMonitoredItemCollection`). `item.RelativePath` /
`item.ResolveItemNodeIdsAsync` / `item.DisplayName` are classic
caller conventions with no V2 equivalent — V2 uses a stable
per-item `Name` string instead.

## Three-API summary

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

All three APIs can coexist on the same session — pick the one that
matches the use case.

## Reference

- Subscription source: `Libraries/Opc.Ua.Client/Subscription/`
- Streaming source: `Libraries/Opc.Ua.Client/Subscription/Streaming/`
- Helpers: `Libraries/Opc.Ua.Client/Subscription/Streaming/StreamingSubscriptionExtensions.cs`
- Alarm streaming: `Libraries/Opc.Ua.Client/Alarms/AlarmStreamExtensions.cs`
- Sessions architecture and engine choice: [Sessions.md](Sessions.md)
- Reference client sample:
  `Applications/ConsoleReferenceClient/AlarmClientSample.cs`
